#nullable enable
// SpriteEquipVisualWiringTests：PR140 文档漂移根治（architecture/落地计划/audit-c86bfa9-20260908/
// 第七方审核）——UnitySpriteView 此前没有 equipVisual 构造入口，即便 Presentation.Render.SpriteViewBase
// 早已完整实现"缺口 10"（display.equip_visual 驱动的纸娃娃层装备外观合成），UnityViewFactory 的
// equipVisualByItemInstanceId 表也接不到纸娃娃层路线（只有 model 路线的 UnityModelView 有对应入口，
// 见 EquipVisualSocketClearTests.cs）。本文件用真实 data/_sample/display 数据（新增
// display.equip_visual.sample_hero_hat 一行，见该文件）+ 真实 EquipmentVisualSource + UnityViewFactory
// 完整生产装配路径验证纸娃娃层同样能生效——断言不满足于"不抛异常"，而是直接查
// UnityResourceLoader.GetLoadProgress 确认装备覆盖的具体资源 id 确实被请求加载过（证明
// SetLayers 用的是 EquipVisualDef.MeshRef，不是基线朝向解析出的资源 id）。
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using NUnit.Framework;
using Presentation.Assembly;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class SpriteEquipVisualWiringTests : PlayModeTestBase
    {
        private static readonly Id SpriteHeroLogicalId = new Id("creature.sample_hero");
        private static readonly Id HatTemplateId = new Id("item.sample_hero_hat");
        private static readonly Id HatMeshRef = new Id("sprite.item.sample_hero_hat_test");

        private (IEventBus Bus, IDataRegistryView Registry, IDisplayInfoRegistry DisplayInfo, UnityEngineHost Host) BuildFixture()
        {
            var host = UnityEngineHost.Ensure();
            var definitions = EventKeys.All.Select(k => new EventDefinition(k, k.Domain, System.Array.Empty<string>())).ToList();
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            var contentFs = new UnityFileSystem(readOnlyContentMode: true, contentRoot: repoRoot);
            var sampleSource = new FileSystemDataSource(contentFs, "data/_sample");
            var frameworkSource = new FileSystemDataSource(contentFs, "data/_framework");

            var options = PresentationSchemaCatalog.CreateOptions();
            options.FailOnUnknownTable = false;
            var registry = new DataRegistry(sampleSource, bus, options);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll(new IDataSource[] { frameworkSource, sampleSource });
            Assert.IsFalse(report.IsBlocking, "测试数据集应当能无阻断加载：" + string.Join("; ", report.Issues));

            var displayInfo = new DisplayInfoRegistry(registry, bus);
            return (bus, registry, displayInfo, host);
        }

        private static IReadOnlyDictionary<Id, EquipVisualDef> BuildCatalogByTemplateId(IDataRegistryView registry)
        {
            var catalog = new Dictionary<Id, EquipVisualDef>();
            foreach (var record in registry.GetAll("display.equip_visual"))
            {
                var def = EquipVisualDef.FromRecord(record);
                catalog[def.ItemId] = def;
            }
            return catalog;
        }

        [Test]
        public void UnityViewFactory_SpriteKind_EquipVisualWired_EquippingRequestsOverrideLayerResource()
        {
            var fx = BuildFixture();
            var catalog = BuildCatalogByTemplateId(fx.Registry);
            Assert.IsTrue(catalog.ContainsKey(HatTemplateId),
                "测试前置条件：display.equip_visual 应当有一行 item_id=item.sample_hero_hat 的 slot_mesh 记录");
            var equipSource = new EquipmentVisualSource(fx.Bus, catalog);
            var factory = new UnityViewFactory(
                fx.Host.Renderer2D, new RenderConventionHost(), fx.DisplayInfo, fx.Host.ResourceLoader,
                bus: fx.Bus, dataRegistry: fx.Registry,
                equipVisualByItemInstanceId: equipSource.VisualByItemInstanceId);

            var entityId = new Id("unit.sprite_equip_visual_test");
            var view = (UnitySpriteView)factory.CreateView(ViewKind.Unit, SpriteHeroLogicalId, entityId);
            view.Bind(entityId);
            // creature.sample_hero 声明 direction_count=8（见 data/_sample/display/display.map.json），
            // SpriteViewBase.SyncPose 直接把 facing 传给 ResolveDirectionSlot，要求 Direction 自带与
            // 该值一致的量化档位数（不像 model 路线可以直接用 Direction.Continuous，见
            // AnimationLayerTests.cs 同一惯例）。
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), height: 0.0);

            // 装备前：覆盖用的资源 id 不应该被请求过（排除"资源恰好因为别的原因也被加载"这种假阳性）。
            Assert.AreEqual(0.0, fx.Host.ResourceLoader.GetLoadProgress(HatMeshRef),
                "装备前不应该有任何请求加载 display.equip_visual.sample_hero_hat 声明的 mesh_ref");

            var itemInstanceId = new Id("item_instance.hat_1");
            var equipEvt = new ItemEquippedEvent(entityId, itemInstanceId, new Id("slot.head"));
            fx.Bus.PublishImmediate(new ItemAddedEvent(entityId, itemInstanceId, HatTemplateId, count: 1));
            fx.Bus.PublishImmediate(equipEvt);
            view.OnEvent(equipEvt);

            // PR140 根治前：UnitySpriteView 构造函数没有 equipVisualByItemInstanceId 参数，
            // SpriteViewBase 内部的 _equipVisuals 恒为 null，OnEvent 直接短路返回（见该类型判断
            // 记录），mesh_ref 永远不会被请求加载——本断言是根治前后的分界线。
            Assert.Greater(fx.Host.ResourceLoader.GetLoadProgress(HatMeshRef), 0.0,
                "根治后：装备应当已经把 slot_id=slot.head 的纸娃娃层替换为 EquipVisualDef.MeshRef 声明的资源，" +
                "触发一次真正的 LoadAsync 请求");

            var unequipEvt = new ItemUnequippedEvent(entityId, new Id("slot.head"), itemInstanceId);
            Assert.DoesNotThrow(() =>
            {
                fx.Bus.PublishImmediate(unequipEvt);
                view.OnEvent(unequipEvt);
            });

            equipSource.Dispose();
            view.Destroy();
        }
    }
}
