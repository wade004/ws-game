#nullable enable
// SpriteEquipVisualWiringTests：PR140 文档漂移根治（architecture/落地计划/audit-c86bfa9-20260908/
// 第七方审核）——UnitySpriteView 此前没有 equipVisual 构造入口，即便 Presentation.Render.SpriteViewBase
// 早已完整实现"缺口 10"（display.equip_visual 驱动的纸娃娃层装备外观合成），UnityViewFactory 的
// equipVisualByItemInstanceId 表也接不到纸娃娃层路线（只有 model 路线的 UnityModelView 有对应入口，
// 见 EquipVisualSocketClearTests.cs）。本文件用真实 data/_sample/display 数据（display.
// equip_visual.sample_hero_hat_wiring_test 一行，本文件专属，见下方判断记录）+ 真实
// EquipmentVisualSource + UnityViewFactory 完整生产装配路径验证纸娃娃层同样能生效——断言不满足于
// "不抛异常"，而是直接查
// UnityResourceLoader.GetLoadProgress 确认装备覆盖的具体资源 id 确实被请求加载过（证明
// SetLayers 用的是 EquipVisualDef.MeshRef，不是基线朝向解析出的资源 id）。
//
// 判断记录（PlayMode 全量门禁失败 1/3 局部修复，2026-09-19，取代此前提交 0a07c0c 的全局方案）：
// 本文件与 EquipmentVisualReplayTests.cs 此前共享同一资源引用字面量常量
// "paperdoll.item.sample_hero_hat_test"，只要其中一个先于另一个在同一批 -runTests 进程里跑过，
// 后跑的那个"装备前不应加载过 mesh_ref"断言就必然落空（UnityEngineHost/UnityResourceLoader 是
// DontDestroyOnLoad 单例，_loaded 缓存跨整个批处理进程存活，从不自动清空）。真实引擎实测证伪了
// 一版"每条用例结束后清空 ResourceLoader 的 ResourceKind.Image 缓存"的全局方案（引入了另外 3 处
// 新失败，见 PlayModeIsolation.TearDownAfterTest 判断记录），已撤销。改为局部修复：本文件专用一个
// 它独有的资源引用字面量 "paperdoll.item.sample_hero_hat_wiring_test"（display.equip_visual.
// sample_hero_hat_wiring_test 一行，配套占位资产已并入 toolchain/import_sample_assets.py 的
// PAPERDOLL_FILES 生成流水线），不再与 EquipmentVisualReplayTests 共享任何字面量——"未被加载过"
// 这一前提由夹具本身独占保证，与执行顺序、跨用例缓存清理均无关。
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
        // 本文件专属（不与 EquipmentVisualReplayTests 共享，见类型顶部判断记录），
        // 对应 display.equip_visual.sample_hero_hat_wiring_test 一行。
        private static readonly Id HatTemplateId = new Id("item.sample_hero_hat_wiring_test");
        private static readonly Id HatMeshRef = new Id("paperdoll.item.sample_hero_hat_wiring_test");

        // ADR-0071 决策 1（2026-09-23）：HatMeshRef 现在是"装备层资源集引用"，实际请求加载的资源 id
        // 经 SpriteViewBase.ResolveEquipLayerResourceId 换算——"layer.<去类别前缀点号转下划线>__
        // <方向裸档位名>__<槽位推导层名>"。下方三个常量对应 creature.sample_hero（direction_count=8）
        // 在 front/back/side_r 三个 canonical 档位、slot.head 推导层名 head 时各自解析到的资源 id，
        // 用于验证"同一件装备、不同朝向解析到不同资源"（ADR-0071 验收标准 1），公式与
        // SpriteViewBaseTests.RebuildEquippedLayers_AcrossThreeDirections_ResolvesThreeDistinctResourceIds
        // 单测验证过的同一套换算一致。
        private static readonly Id HatResolvedLayerResourceIdSideR = new Id("layer.item_sample_hero_hat_wiring_test__side_r__head");
        private static readonly Id HatResolvedLayerResourceIdFront = new Id("layer.item_sample_hero_hat_wiring_test__front__head");
        private static readonly Id HatResolvedLayerResourceIdBack = new Id("layer.item_sample_hero_hat_wiring_test__back__head");

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
                "测试前置条件：display.equip_visual 应当有一行 item_id=item.sample_hero_hat_wiring_test 的 slot_mesh 记录");
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

            // 装备前：覆盖用的资源 id 不应该被请求过（排除"资源恰好因为别的原因也被加载"这种假阳性；
            // 本文件专属字面量，见类型顶部判断记录，不依赖跨用例执行顺序或资源缓存清理）。
            Assert.AreEqual(0.0, fx.Host.ResourceLoader.GetLoadProgress(HatResolvedLayerResourceIdSideR),
                "装备前不应该有任何请求加载 display.equip_visual.sample_hero_hat_wiring_test 声明的 mesh_ref 换算出的装备层资源");

            var itemInstanceId = new Id("item_instance.hat_1");
            var equipEvt = new ItemEquippedEvent(entityId, itemInstanceId, new Id("slot.head"));
            fx.Bus.PublishImmediate(new ItemAddedEvent(entityId, itemInstanceId, HatTemplateId, count: 1));
            fx.Bus.PublishImmediate(equipEvt);
            view.OnEvent(equipEvt);

            // PR140 根治前：UnitySpriteView 构造函数没有 equipVisualByItemInstanceId 参数，
            // SpriteViewBase 内部的 _equipVisuals 恒为 null，OnEvent 直接短路返回（见该类型判断
            // 记录），mesh_ref 永远不会被请求加载——本断言是根治前后的分界线。
            Assert.Greater(fx.Host.ResourceLoader.GetLoadProgress(HatResolvedLayerResourceIdSideR), 0.0,
                "根治后：装备应当已经把 slot_id=slot.head 的纸娃娃层替换为 EquipVisualDef.MeshRef 换算出的" +
                "装备层资源，触发一次真正的 LoadAsync 请求");

            var unequipEvt = new ItemUnequippedEvent(entityId, new Id("slot.head"), itemInstanceId);
            Assert.DoesNotThrow(() =>
            {
                fx.Bus.PublishImmediate(unequipEvt);
                view.OnEvent(unequipEvt);
            });

            equipSource.Dispose();
            view.Destroy();
        }

        /// <summary>
        /// ADR-0071 验收标准 1：同一件已装备物品在 front/back/side_r 三个朝向下应当解析到三个不同的
        /// 装备层资源 id（不是根治前"无论朝向如何都固定同一张图"）。用真实生产装配入口
        /// UnityViewFactory + 真实 data/_sample 数据 + 真实 UnityResourceLoader 验证，不经中间层
        /// 断言——同 SpriteViewBaseTests.RebuildEquippedLayers_AcrossThreeDirections_
        /// ResolvesThreeDistinctResourceIds 的场景，但那个用例走的是 StubRenderer2D，本用例走的是
        /// Unity 真实资源加载器，满足"必须经真实生产装配入口验证"的要求（见任务书验收标准 1）。
        /// </summary>
        [Test]
        public void UnityViewFactory_SpriteKind_EquipVisualWired_FacingChange_ResolvesThreeDistinctLayerResources()
        {
            var fx = BuildFixture();
            var catalog = BuildCatalogByTemplateId(fx.Registry);
            var equipSource = new EquipmentVisualSource(fx.Bus, catalog);
            var factory = new UnityViewFactory(
                fx.Host.Renderer2D, new RenderConventionHost(), fx.DisplayInfo, fx.Host.ResourceLoader,
                bus: fx.Bus, dataRegistry: fx.Registry,
                equipVisualByItemInstanceId: equipSource.VisualByItemInstanceId);

            var entityId = new Id("unit.sprite_equip_visual_facing_test");
            var view = (UnitySpriteView)factory.CreateView(ViewKind.Unit, SpriteHeroLogicalId, entityId);
            view.Bind(entityId);

            var itemInstanceId = new Id("item_instance.hat_facing_1");
            var equipEvt = new ItemEquippedEvent(entityId, itemInstanceId, new Id("slot.head"));
            fx.Bus.PublishImmediate(new ItemAddedEvent(entityId, itemInstanceId, HatTemplateId, count: 1));
            fx.Bus.PublishImmediate(equipEvt);
            view.OnEvent(equipEvt);

            // 判断记录（为什么每次换向后要重新 OnEvent(equipEvt) 而不是只调 SyncPose）：
            // SpriteViewBase.SyncPose 只更新 _lastFacing/flipX/Transform，不主动触发
            // RebuildEquippedLayers（09 表述"随装备/外形变化事件更新"——层合成只在装备/卸下等事件
            // 到达时才重算，不是每帧/每次换向自动重算，这是框架既有行为，不是本次 ADR-0071 引入或
            // 改动的部分）。真实调用序列因此是"SyncPose 落地新朝向 → 装备事件（或任何会触发
            // RebuildEquippedLayers 的事件）重新计算层"——与 SpriteViewBaseTests.
            // RebuildEquippedLayers_AcrossThreeDirections_ResolvesThreeDistinctResourceIds 单测
            // 每次 SyncPose 后显式调用 ResetEquipmentVisuals 触发重建同一惯例；本用例复用同一个
            // equipEvt 重新 OnEvent 一次达到同样效果（槽位覆盖值不变，只是强制 RebuildEquippedLayers
            // 按 _lastFacing 最新值重新换算）。
            //
            // 根治前（记录实测值，供报告"修复前→修复后"对照）：RebuildEquippedLayers 命中覆盖的层
            // 直接把 HatMeshRef 字面量当最终资源 id 使用，不经 ResolveLayerResourceId 方向换算——
            // 三个朝向请求加载的都是同一个 "paperdoll.item.sample_hero_hat_wiring_test"，
            // GetLoadProgress(HatResolvedLayerResourceIdFront/Back) 恒为 0，只有 HatMeshRef 本身的
            // 进度会变化，三个方向永远解析到同一张图。根治后触发 SyncPose 换向 + 重新 OnEvent 应当
            // 分别请求三个不同 id，下方逐一断言。
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(System.Math.PI / 2, 8), height: 0.0);
            view.OnEvent(equipEvt);
            Assert.Greater(fx.Host.ResourceLoader.GetLoadProgress(HatResolvedLayerResourceIdFront), 0.0,
                "朝向 front 应当请求加载 front 档位的装备层资源");

            view.SyncPose(Vec2.Zero, Direction.FromQuantized(System.Math.PI * 3 / 2, 8), height: 0.0);
            view.OnEvent(equipEvt);
            Assert.Greater(fx.Host.ResourceLoader.GetLoadProgress(HatResolvedLayerResourceIdBack), 0.0,
                "朝向 back 应当请求加载 back 档位的装备层资源");

            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), height: 0.0);
            view.OnEvent(equipEvt);
            Assert.Greater(fx.Host.ResourceLoader.GetLoadProgress(HatResolvedLayerResourceIdSideR), 0.0,
                "朝向 side_r 应当请求加载 side_r 档位的装备层资源");

            Assert.AreNotEqual(HatResolvedLayerResourceIdFront, HatResolvedLayerResourceIdBack);
            Assert.AreNotEqual(HatResolvedLayerResourceIdFront, HatResolvedLayerResourceIdSideR);
            Assert.AreNotEqual(HatResolvedLayerResourceIdBack, HatResolvedLayerResourceIdSideR);

            equipSource.Dispose();
            view.Destroy();
        }
    }
}
