#nullable enable
// EquipmentVisualReplayTests：第九方审核任务书"第 0 步"补齐——"新 View 初始装备外观重放"缺口的
// 复现与根治验证。EquipmentVisualSource 此前只靠 item.equipped 事件累积 _visualByItemInstanceId，
// 跨图新 View（实体在别的地图/别的时间点已经装备好，这次只是重新创建 View）与
// InventoryHost.InjectInstance 路径（存档恢复，不发 item.added/item.equipped）都不会重放既有装备
// 外观——新 View 刚创建出来时是"裸模型"，必须等玩家换一次装才会看见挂点/纸娃娃层。本文件用真实
// EquipmentVisualSource + UnityViewFactory 完整生产装配路径（同 EquipVisualSocketClearTests 惯例），
// 唯一区别是本次不发布任何 item.equipped 事件，改用 EquipmentSnapshotResolver 模拟"装备发生在
// View 创建之前"这一跨图场景。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
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
    public sealed class EquipmentVisualReplayTests : PlayModeTestBase
    {
        private static readonly Id ModelHeroLogicalId = new Id("creature.sample_model_hero");
        private static readonly Id SwordTemplateId = new Id("item.sample_model_sword");

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

        [Test]
        public void CreateView_EquipmentAlreadyEquippedBeforeViewExisted_ReplaysVisualWithoutAnyEquipEvent()
        {
            var fx = BuildFixture();
            var catalog = BuildCatalogByTemplateId(fx.Registry);

            var entityId = new Id("unit.equip_visual_replay_test");
            var itemInstanceId = new Id("item_instance.sword_replay_1");
            var slotId = new Id("slot.weapon_main");

            // 模拟"先装备再创建 View"（跨图场景）：这件装备的 item.added/item.equipped 早已在别的
            // 地图/别的时间点发生过（本测试从头到尾都不 PublishImmediate 任何这两个事件），
            // EquipmentVisualSource 只能靠 snapshotResolver（生产环境底层是
            // EquipmentHost.GetAllEquippedInstances）在 View 创建时补上这份状态。
            EquipmentSnapshotResolver resolver = unitId => unitId.Equals(entityId)
                ? new[] { new EquippedItemRef(slotId, itemInstanceId, SwordTemplateId) }
                : Array.Empty<EquippedItemRef>();

            var equipSource = new EquipmentVisualSource(fx.Bus, catalog, resolver);
            var factory = new UnityViewFactory(
                fx.Host.Renderer2D, new RenderConventionHost(), fx.DisplayInfo, fx.Host.ResourceLoader,
                bus: fx.Bus, dataRegistry: fx.Registry, renderer3D: fx.Host.Renderer3D,
                equipVisualByItemInstanceId: equipSource.VisualByItemInstanceId,
                equipmentVisualSource: equipSource);

            var view = (UnityModelView)factory.CreateView(ViewKind.Unit, ModelHeroLogicalId, entityId);
            view.Bind(entityId);
            var hostHandle = view.TryGetModelHandle()!.Value;
            var renderer3D = (UnityRenderer3D)fx.Host.Renderer3D;

            var socketTransform = renderer3D.GetModelVisualRoot(hostHandle)!.Find("socket.main_hand")
                ?? FindDeep(renderer3D.GetModelVisualRoot(hostHandle)!, "socket.main_hand");
            Assert.IsNotNull(socketTransform, "sample_model_hero 应当声明 socket.main_hand 挂点");
            Assert.AreEqual(1, socketTransform!.childCount,
                "第九方审核任务书第 0 步根治：新 View 创建时应当按 EquipmentSnapshotResolver 重放已装备" +
                "物品外观，不需要等待一次真正的 item.equipped 事件才能看见挂点子模型");

            equipSource.Dispose();
            view.Destroy();
        }

        [Test]
        public void CreateView_NoEquipmentSnapshotResolver_BehavesExactlyAsBefore_NoReplayNoThrow()
        {
            // equipmentVisualSource 未传（默认 null）：与改动前完全一致的构造方式（同
            // EquipVisualSocketClearTests 既有用例），不应因为本次改动而产生任何新的行为/异常。
            var fx = BuildFixture();
            var catalog = BuildCatalogByTemplateId(fx.Registry);
            var equipSource = new EquipmentVisualSource(fx.Bus, catalog);
            var factory = new UnityViewFactory(
                fx.Host.Renderer2D, new RenderConventionHost(), fx.DisplayInfo, fx.Host.ResourceLoader,
                bus: fx.Bus, dataRegistry: fx.Registry, renderer3D: fx.Host.Renderer3D,
                equipVisualByItemInstanceId: equipSource.VisualByItemInstanceId);

            var entityId = new Id("unit.equip_visual_replay_no_resolver_test");
            IView view = null!;
            Assert.DoesNotThrow(() => view = factory.CreateView(ViewKind.Unit, ModelHeroLogicalId, entityId));
            view.Bind(entityId);

            var hostHandle = ((UnityModelView)view).TryGetModelHandle()!.Value;
            var renderer3D = (UnityRenderer3D)fx.Host.Renderer3D;
            var socketTransform = renderer3D.GetModelVisualRoot(hostHandle)!.Find("socket.main_hand")
                ?? FindDeep(renderer3D.GetModelVisualRoot(hostHandle)!, "socket.main_hand");
            Assert.IsNotNull(socketTransform);
            Assert.AreEqual(0, socketTransform!.childCount, "没有装备来源/重放能力时新 View 应当仍是裸模型");

            equipSource.Dispose();
            view.Destroy();
        }
    }
}
