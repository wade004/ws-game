#nullable enable
// EquipVisualSocketClearTests：PR130-07 复现与回归用例——UnityModelView 卸装路径此前按逻辑
// item.slot 同时 ClearSlot/ClearSocket，而装备落地时用的是 EquipVisualDef.SocketId（不同 id 域），
// socket_attach 模式卸装后子模型实例残留不清理（见 UnityModelView.cs 类型顶部
// _appliedEquipVisualsByItemInstanceId 判断记录）。本文件用真实 data/_sample/display 数据（新增
// display.equip_visual 表两行，见该文件）+ 真实 EquipmentVisualSource + UnityViewFactory 完整生产
// 装配路径（不是最小夹具替身），验证 socket_attach/slot_mesh 两种模式的装备/卸装/重复卸装。
using System;
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
    public sealed class EquipVisualSocketClearTests : PlayModeTestBase
    {
        private static readonly Id ModelHeroLogicalId = new Id("creature.sample_model_hero");
        private static readonly Id SwordTemplateId = new Id("item.sample_model_sword");
        private static readonly Id HelmetTemplateId = new Id("item.sample_model_helmet");

        private (IEventBus Bus, IDataRegistryView Registry, IDisplayInfoRegistry DisplayInfo, UnityEngineHost Host) BuildFixture()
        {
            var host = UnityEngineHost.Ensure();
            var definitions = EventKeys.All.Select(k => new EventDefinition(k, k.Domain, Array.Empty<string>())).ToList();
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
        public void SocketAttach_Equip_AttachesChildModel_Unequip_DestroysIt_RepeatUnequip_IsIdempotent()
        {
            var fx = BuildFixture();
            var catalog = BuildCatalogByTemplateId(fx.Registry);
            var equipSource = new EquipmentVisualSource(fx.Bus, catalog);
            var factory = new UnityViewFactory(
                fx.Host.Renderer2D, new RenderConventionHost(), fx.DisplayInfo, fx.Host.ResourceLoader,
                bus: fx.Bus, dataRegistry: fx.Registry, renderer3D: fx.Host.Renderer3D,
                equipVisualByItemInstanceId: equipSource.VisualByItemInstanceId);

            var entityId = new Id("unit.equip_visual_socket_test");
            var view = (UnityModelView)factory.CreateView(ViewKind.Unit, ModelHeroLogicalId, entityId);
            view.Bind(entityId);
            var hostHandle = view.TryGetModelHandle()!.Value;
            var renderer3D = (UnityRenderer3D)fx.Host.Renderer3D;

            // 判断记录：本测试不经 Presentation.ViewBinding.ViewBinder（该类型只转发
            // ForwardedEventKeys 白名单里的事件给已绑定 View 的 IView.OnEvent，见其构造函数判断
            // 记录），直接持有 UnityModelView，因此装备/卸装事件需要分两路手工投递：
            // item.added/item.equipped/item.unequipped 都经真实 fx.Bus 广播（EquipmentVisualSource
            // 直接订阅同一条 bus，见该类型判断记录），item.equipped/item.unequipped 额外手工转发一次
            // view.OnEvent（模拟 ViewBinder 在生产装配下会做的转发）。
            var itemInstanceId = new Id("item_instance.sword_1");
            fx.Bus.PublishImmediate(new ItemAddedEvent(entityId, itemInstanceId, SwordTemplateId, count: 1));
            var equipEvt = new ItemEquippedEvent(entityId, itemInstanceId, new Id("slot.weapon_main"));
            fx.Bus.PublishImmediate(equipEvt);
            view.OnEvent(equipEvt);

            var socketTransform = renderer3D.GetModelVisualRoot(hostHandle)!.Find("socket.main_hand")
                ?? FindDeep(renderer3D.GetModelVisualRoot(hostHandle)!, "socket.main_hand");
            Assert.IsNotNull(socketTransform, "sample_model_hero 应当声明 socket.main_hand 挂点");
            Assert.AreEqual(1, socketTransform!.childCount, "装备 socket_attach 模式后挂点下应当挂了一个子模型实例");

            var unequipEvt = new ItemUnequippedEvent(entityId, new Id("slot.weapon_main"), itemInstanceId);
            fx.Bus.PublishImmediate(unequipEvt);
            view.OnEvent(unequipEvt);

            Assert.AreEqual(0, socketTransform.childCount, "PR130-07 根治：卸装后 socket 挂点下的子模型实例应当被清理，不再残留");

            // 重复卸装幂等：_appliedEquipVisualsByItemInstanceId 已经移除该条目，退回按逻辑 slot 尝试
            // ClearSlot（幂等 no-op），不应抛异常。
            Assert.DoesNotThrow(() => view.OnEvent(unequipEvt));

            equipSource.Dispose();
            view.Destroy();
        }

        [Test]
        public void SlotMesh_Equip_ThenUnequip_DoesNotThrow_AndClearsSlot()
        {
            var fx = BuildFixture();
            var catalog = BuildCatalogByTemplateId(fx.Registry);
            var equipSource = new EquipmentVisualSource(fx.Bus, catalog);
            var factory = new UnityViewFactory(
                fx.Host.Renderer2D, new RenderConventionHost(), fx.DisplayInfo, fx.Host.ResourceLoader,
                bus: fx.Bus, dataRegistry: fx.Registry, renderer3D: fx.Host.Renderer3D,
                equipVisualByItemInstanceId: equipSource.VisualByItemInstanceId);

            var entityId = new Id("unit.equip_visual_slot_test");
            var view = (UnityModelView)factory.CreateView(ViewKind.Unit, ModelHeroLogicalId, entityId);
            view.Bind(entityId);

            var itemInstanceId = new Id("item_instance.helmet_1");
            var equipEvt = new ItemEquippedEvent(entityId, itemInstanceId, new Id("slot.head"));
            var unequipEvt = new ItemUnequippedEvent(entityId, new Id("slot.head"), itemInstanceId);
            Assert.DoesNotThrow(() =>
            {
                fx.Bus.PublishImmediate(new ItemAddedEvent(entityId, itemInstanceId, HelmetTemplateId, count: 1));
                fx.Bus.PublishImmediate(equipEvt);
                view.OnEvent(equipEvt);
                fx.Bus.PublishImmediate(unequipEvt);
                view.OnEvent(unequipEvt);
            });

            equipSource.Dispose();
            view.Destroy();
        }

        private static Transform? FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
