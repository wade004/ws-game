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

        // PR150-01 用例专用（sprite 路线，同 SpriteEquipVisualWiringTests.cs 复用的同一份
        // data/_sample/display 固定测试数据：creature.sample_hero / item.sample_hero_hat /
        // display.equip_visual.sample_hero_hat）。
        private static readonly Id SpriteHeroLogicalId = new Id("creature.sample_hero");
        private static readonly Id HatTemplateId = new Id("item.sample_hero_hat");
        private static readonly Id HatMeshRef = new Id("sprite.item.sample_hero_hat_test");

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

        /// <summary>
        /// PR150-01 复现/根治（<c>architecture/落地计划/audit-3224ca1-20260908/AUDIT_REPORT.md</c>
        /// PR150-01"sprite 初始装备重放早于 Bind，装备事件被过滤"）：sprite 路线的
        /// <see cref="Presentation.Render.SpriteViewBase.EntityId"/> 要到 <c>Bind</c> 才设置（默认值
        /// 在此之前一直是 <c>default</c>），<see cref="UnityViewFactory.CreateView"/> 内部按当前已
        /// 装备物品重放初始外观（本类型上一条用例覆盖的机制）如果发生在 <c>Bind</c> 之前，
        /// <see cref="Presentation.Render.SpriteViewBase.OnEvent"/> 按
        /// <c>equipped.UnitId.Equals(EntityId)</c> 过滤会把重放事件误判为"不属于本 View"直接丢弃——
        /// 帽子覆盖资源永远不会被请求加载。根治后 <c>UnityViewFactory.CreateView</c> 在执行重放前
        /// 先对本次创建的 view 调用一次 <c>Bind</c>（见该方法判断记录、09 第 2 节新补的"创建/绑定/
        /// 首次同步"顺序契约），真实调用顺序 <c>CreateView → Bind → SyncPose</c> 后装备覆盖资源应当
        /// 已经开始加载，与"Bind 后手工发同一装备事件"的既有正对照（见
        /// <see cref="SpriteEquipVisualWiringTests.UnityViewFactory_SpriteKind_EquipVisualWired_EquippingRequestsOverrideLayerResource"/>）
        /// 结果一致。
        /// </summary>
        [Test]
        public void CreateView_SpriteKind_EquipmentAlreadyEquippedBeforeViewExisted_ReplaysAfterBind_RequestsOverrideLayerResource()
        {
            var fx = BuildFixture();
            var catalog = BuildCatalogByTemplateId(fx.Registry);
            Assert.IsTrue(catalog.ContainsKey(HatTemplateId),
                "测试前置条件：display.equip_visual 应当有一行 item_id=item.sample_hero_hat 的 slot_mesh 记录");

            var entityId = new Id("unit.pr150_01_sprite_replay_test");
            var itemInstanceId = new Id("item_instance.pr150_01_hat_1");
            var slotId = new Id("slot.head");

            // 模拟"装备发生在 View 创建之前"（跨图/恢复后重建外观场景，同类型顶部第一条用例）。
            EquipmentSnapshotResolver resolver = unitId => unitId.Equals(entityId)
                ? new[] { new EquippedItemRef(slotId, itemInstanceId, HatTemplateId) }
                : Array.Empty<EquippedItemRef>();

            var equipSource = new EquipmentVisualSource(fx.Bus, catalog, resolver);
            var factory = new UnityViewFactory(
                fx.Host.Renderer2D, new RenderConventionHost(), fx.DisplayInfo, fx.Host.ResourceLoader,
                bus: fx.Bus, dataRegistry: fx.Registry, renderer3D: fx.Host.Renderer3D,
                equipVisualByItemInstanceId: equipSource.VisualByItemInstanceId,
                equipmentVisualSource: equipSource);

            // 创建前：覆盖用的资源 id 不应该被请求过（排除"资源恰好因为别的原因也被加载"这种假阳性，
            // 同 SpriteEquipVisualWiringTests 一贯手法）。
            Assert.AreEqual(0.0, fx.Host.ResourceLoader.GetLoadProgress(HatMeshRef),
                "创建前不应该有任何请求加载 display.equip_visual.sample_hero_hat 声明的 mesh_ref");

            // 真实顺序：CreateView（内部触发重放）→ Bind → SyncPose，见方法判断记录。
            var view = (UnitySpriteView)factory.CreateView(ViewKind.Unit, SpriteHeroLogicalId, entityId);
            view.Bind(entityId);
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), height: 0.0);

            Assert.Greater(fx.Host.ResourceLoader.GetLoadProgress(HatMeshRef), 0.0,
                "根治后：CreateView → Bind → SyncPose 这一真实顺序完成后，装备覆盖资源应当已经开始" +
                "加载——根治前本断言会失败（进度恒为 0），因为重放事件在 Bind 之前发生，被 EntityId" +
                "过滤丢弃");

            equipSource.Dispose();
            view.Destroy();
        }
    }
}
