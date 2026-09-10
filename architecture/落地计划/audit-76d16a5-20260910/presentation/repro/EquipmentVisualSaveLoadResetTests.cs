#nullable enable
// EquipmentVisualSaveLoadResetTests：P2-08 根治验收（原探针 architecture/落地计划/
// audit-c9ff301-20260909/presentation/ExistingViewEquipmentSaveLoadAuditTests.cs，第十四轮收口时
// 曾迁入 games/_template/Tests/Runtime/ExistingViewEquipmentSaveLoadTests.cs 作为常设回归）——
// ViewBinder.OnSaveLoaded 对"同图内继续存活的既有 View"做装备外观对账：A 为真实装备状态，B 为空
// 装备存档；LoadB 后要求 EquipmentHost 为空且 View identity 不变，再读取同一个 model socket 的
// childCount，区分"状态恢复"（EquipmentHost 查询已经是空）与"外观已刷新"（socket 子物体确实被
// 清空）——根治前二者不一致（前者已是 0、后者仍是 1），是本用例存在的意义（见
// Presentation.Render.IEquipmentVisualResettable 类型注释）。
//
// 判断记录（消费方演练失败根治：为什么改写成本文件而不是原样迁移 GameBootstrap 版本）：
// games/_template/Tests/Runtime/ExistingViewEquipmentSaveLoadTests.cs 原版本借 GameBootstrap 把
// _gameDatasetRoot 私有字段反射改成 "data/_sample" 才能拿到 creature.sample_model_hero 一类真实
// 3D 模型 + display.equip_visual 装备外观数据——但 data/_sample 是本仓库自测用的工作台示例数据
// （data/README.md"两类目录"一节），consumer_smoke.ps1 演练消费方工程时数据根只有
// data/_framework + data/game（模板自己的最小数据集，不含 creature/display/gobj 三张表，见
// games/_template/data/game/ 目录列表），该用例因此必然装配失败。games/_template 模板本身的设计
// 意图是保持技术无关的最小闭环（不预置任何具体 3D 模型/装备外观内容），不适合为了这一条回归用例
// 反过来给模板追加整套模型/装备外观样例数据（那会让"新游戏复制模板起步"背上不必要的内容负担）。
// 本用例验证的行为——ViewBinder.OnSaveLoaded 对 IEquipmentVisualResettable 的对账——完全是
// presentation/view_binding + adapters/unity 两个框架层级自身的能力，不依赖 games/_template.
// GameBootstrap 这一具体游戏层组合根：改为直接用 Core.Gameplay.Assembly.GameplayAssembly（真实
// EquipmentHost/InventoryHost/SaveSystem）+ Presentation.ViewBinding.ViewBinder（真实
// entity.created/save.loaded 订阅）+ Adapter.Unity.Presentation.UnityViewFactory 组装一个最小但
// 完整的真实链路夹具，惯例同本目录 EquipVisualSocketClearTests.cs/EquipmentVisualReplayTests.cs
// （同样直接用 data/_sample 工作台示例数据 + UnityViewFactory 真实生产装配路径，不是替身），只是
// 额外接上 GameplayAssembly + 真实 SaveSystem 覆盖"同一个存活 View 经历一次真实 Save/Load"这条更
// 完整的链路。这样一来：(a) 不再依赖 Game.Template 程序集（adapters/unity 是框架 L-1 层，不应反过来
// 依赖任何具体游戏/模板的组合根，即便只是测试代码——见 games/_template/package.json 对
// com.gamefoundation.adapter.unity 的单向依赖声明），(b) 不需要加载 GameTemplateShell 场景/驱动
// ShellHost 状态机，测试更快更聚焦；games/_template/Tests/Runtime 下的原文件已删除，模板侧改为
// GameTemplateSmokeTests.cs 覆盖的默认 GameOptions 零阻断加载 + 主菜单验收，不再声称覆盖模型外观
// 读档对账这条对模板自身数据集不成立的路径。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using NUnit.Framework;
using Presentation.Assembly;
using Presentation.Common;
using Presentation.Render;
using Presentation.ViewBinding;
using UnityEngine;

// 判断记录（惯例同 core/gameplay/tests/EndToEnd/GameWorldFixture.cs 顶部同款判断记录）：
// Core.Foundation.SaveSystem 命名空间与其内的 SaveSystem 类同名，裸写 "SaveSystem" 会被编译器
// 解析成命名空间本身而报 CS0118，用别名区分。
using RealSaveSystem = Core.Foundation.SaveSystem.SaveSystem;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class EquipmentVisualSaveLoadResetTests : PlayModeTestBase
    {
        private const string SwordTemplateId = "item.sample_model_sword";
        private const string MainHandSlotId = "item.slot.sample_main_hand";
        private const string ModelPlayerTemplateId = "creature.sample_model_hero";

        private static readonly Id MapId = new Id("world.sample_field");
        private static readonly Id PlayerId = new Id("unit.equip_visual_save_load_reset_player");
        private static readonly Id FactionPlayer = new Id("fac.player");
        private static readonly Id ClassSample = new Id("arch.class.sample_a");
        private static readonly Id GameId = new Id("game.equip_visual_save_load_reset_test");
        private static readonly Id SlotB = new Id("slot.equip_visual_save_load_reset_test_b");

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public GameplayAssembly Gameplay = null!;
            public ViewBinder ViewBinder = null!;
            public EquipmentVisualSource EquipVisualSource = null!;
            public UnityEngineHost Host = null!;
        }

        private static Fixture BuildFixture()
        {
            var host = UnityEngineHost.Ensure();
            var definitions = EventKeys.All.Select(k => new EventDefinition(k, k.Domain, Array.Empty<string>())).ToList();
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            // 数据：真实 data/_sample（工作台示例数据，含 creature.sample_model_hero 3D 模型 +
            // display.equip_visual.sample_model_sword 装备外观定义）+ data/_framework，惯例同
            // EquipVisualSocketClearTests.BuildFixture/EquipmentVisualReplayTests.BuildFixture。
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

            var rng = new RngHost(20260910UL);
            var world = new WorldSim(bus);
            // 真实 SaveSystem，写往 UnityEngineHost 自带的可写文件系统（同 games/_template.
            // GameBootstrap.Bootstrap 用 _host.FileSystem 构造 SaveSystem 的惯例），GameId 用本用例
            // 专属 id，与本目录其它套件（"game.sample."/"game.template." 前缀）各自独立的存档目录
            // 互不相干，不占用彼此的 SaveSystemOptions.MaxSlots 配额。
            var saveSystem = new RealSaveSystem(host.FileSystem, new SaveSystemOptions(GameId), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, host.SpatialQuery, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: FactionPlayer);

            var player = new PlayerUnit(PlayerId, MapId, FactionPlayer, ClassSample) { Position = Vec2.Zero, TemplateId = new Id(ModelPlayerTemplateId) };
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ClassSample, raceId: null, level: 1);
            gameplay.Economy.RegisterUnit(PlayerId);
            gameplay.RegisterPersistables(saveSystem, player);

            var displayInfo = new DisplayInfoRegistry(registry, bus);
            var equipVisualCatalog = new Dictionary<Id, EquipVisualDef>();
            foreach (var record in registry.GetAll("display.equip_visual"))
            {
                var def = EquipVisualDef.FromRecord(record);
                equipVisualCatalog[def.ItemId] = def;
            }

            // 同 games/_template.GameBootstrap.Bootstrap 判断记录：把 EquipmentHost.GetAllEquippedInstances
            // 转成 EquipmentSnapshotResolver，供 EquipmentVisualSource 在新 View 创建/save.loaded 对账时
            // 重放真实装备快照。
            EquipmentSnapshotResolver resolver = unitId =>
            {
                var equippedBySlot = gameplay.Carriers.Equipment.GetAllEquippedInstances(unitId);
                if (equippedBySlot.Count == 0)
                {
                    return Array.Empty<EquippedItemRef>();
                }

                var list = new List<EquippedItemRef>(equippedBySlot.Count);
                foreach (var kv in equippedBySlot)
                {
                    list.Add(new EquippedItemRef(kv.Key, kv.Value.InstanceId, kv.Value.TemplateId));
                }
                return list;
            };
            var equipVisualSource = new EquipmentVisualSource(bus, equipVisualCatalog, resolver);

            var viewFactory = new UnityViewFactory(
                host.Renderer2D, new RenderConventionHost(), displayInfo, host.ResourceLoader,
                bus: bus, dataRegistry: registry, renderer3D: host.Renderer3D,
                equipVisualByItemInstanceId: equipVisualSource.VisualByItemInstanceId,
                equipmentVisualSource: equipVisualSource);

            var snapshot = new WorldSimSnapshot(world);
            var viewBinder = new ViewBinder(bus, viewFactory, snapshot, displayInfo,
                renderConvention: new RenderConventionHost(), equipmentVisualSource: equipVisualSource);

            host.SpatialQuery.Register(PlayerId, player.Position, 0.5);
            world.AddEntity(player);
            bus.DispatchPending();

            return new Fixture
            {
                Bus = bus,
                Gameplay = gameplay,
                ViewBinder = viewBinder,
                EquipVisualSource = equipVisualSource,
                Host = host,
            };
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
        public void SurvivingView_SaveEmptyThenEquip_LoadEmptySave_ResetsStaleEquipmentVisual()
        {
            var fx = BuildFixture();
            try
            {
                Assert.IsTrue(fx.ViewBinder.TryGetView(PlayerId, out var beforeView), "AddEntity 后应存在玩家 View");
                Assert.IsInstanceOf<UnityModelView>(beforeView,
                    "本探针配置 creature.sample_model_hero，必须走真实 UnityModelView");
                var view = (UnityModelView)beforeView!;
                var initialHandle = view.TryGetModelHandle();
                Assert.IsTrue(initialHandle.HasValue, "玩家 View 应持有存活 ModelHandle");
                var renderer3D = (UnityRenderer3D)fx.Host.Renderer3D;
                var visualRoot = renderer3D.GetModelVisualRoot(initialHandle!.Value);
                Assert.IsNotNull(visualRoot, "真实 UnityRenderer3D 应返回模型外观根");
                var socket = visualRoot!.Find("socket.main_hand") ?? FindDeep(visualRoot, "socket.main_hand");
                Assert.IsNotNull(socket, "sample_model_hero 应声明 socket.main_hand");

                // 判断记录（不再另存一份"存档 A"，同原探针文件同款判断记录）：真正驱动断言的是
                // "刚刚在这个仍存活的实时 EquipmentHost/View 上完成了装备"这一实时状态，不需要额外
                // 落一份从未被 Load 过的存档。
                var saveB = fx.Gameplay.SaveSystem.Save(new SaveRequest(SlotB, "test-B"));
                Assert.IsTrue(saveB.Success, "Save B（空装备）应成功：" + saveB.Message);

                var inventory = fx.Gameplay.Carriers.Inventory;
                Assert.IsTrue(inventory.AddItem(PlayerId, new Id(SwordTemplateId), 1),
                    "真实 InventoryHost 应接受 data/_sample 的合法 sample_model_sword fixture");
                fx.Bus.DispatchPending();
                var instance = inventory.ListItems(PlayerId).Single(x => x.TemplateId.Equals(new Id(SwordTemplateId)));
                var equipResult = fx.Gameplay.Carriers.Equipment.Equip(PlayerId, instance.InstanceId, new Id(MainHandSlotId));
                Assert.IsTrue(equipResult.Success, "真实 EquipmentHost.Equip 应成功：" + equipResult.Reason);
                fx.Bus.DispatchPending();
                Assert.AreEqual(1, socket!.childCount, "装备后真实既有 View 的 socket.main_hand 应有一个子模型");

                Assert.IsTrue(fx.ViewBinder.TryGetView(PlayerId, out var sameViewBeforeLoad));
                Assert.AreSame(view, sameViewBeforeLoad, "Load B 前后应始终使用同一 View 对象");

                var loadB = fx.Gameplay.SaveSystem.Load(SlotB);
                Assert.IsTrue(loadB.Status == LoadStatus.Loaded || loadB.Status == LoadStatus.LoadedFromBackup,
                    "Load B 应成功：" + loadB.Status);

                var equippedAfterLoad = fx.Gameplay.Carriers.Equipment.GetAllEquippedInstances(PlayerId);
                Assert.IsTrue(fx.ViewBinder.TryGetView(PlayerId, out var afterView), "Load B 后玩家应仍有 View");
                Assert.IsNotNull(afterView);
                Assert.AreSame(view, afterView, "正确 oracle：Load B 不应重建玩家 View");
                var afterHandle = ((UnityModelView)afterView!).TryGetModelHandle();
                Assert.IsTrue(afterHandle.HasValue, "Load B 后同一 View 应仍持有 ModelHandle");
                var currentRoot = renderer3D.GetModelVisualRoot(afterHandle!.Value);
                Assert.IsNotNull(currentRoot, "Load B 后应能重新读取同一模型根");
                var currentSocket = currentRoot!.Find("socket.main_hand") ?? FindDeep(currentRoot, "socket.main_hand");
                Assert.IsNotNull(currentSocket, "Load B 后应能重新读取 socket.main_hand");

                Debug.Log($"[EquipmentVisualSaveLoadResetTests] saveB={saveB.Success};loadB={loadB.Status};" +
                          $"equipment_after_load={equippedAfterLoad.Count};socket_childCount_before={socket.childCount};" +
                          $"socket_childCount_after={currentSocket!.childCount};view_same={ReferenceEquals(view, afterView)}");
                Assert.AreEqual(0, equippedAfterLoad.Count, "正确 oracle：Load B 后真实 EquipmentHost 应为空装备");
                Assert.AreEqual(0, currentSocket.childCount,
                    "正确 oracle：Load B 后既有 View 的主手外观应清除；若仍为 1 即确认同图读档外观缺陷");

                view.Destroy();
            }
            finally
            {
                // 判断记录（消费方演练根治过程中真实复现过的隔离缺口）：UnityEngineHost.SpatialQuery
                // 是跨整个 -runTests 批处理进程存活的单例（同本目录 PlayModeIsolation.cs 顶部判断
                // 记录"三次 DontDestroyOnLoad 单例污染下一条无关用例"同一类问题），本用例
                // BuildFixture 里向它 Register 过 PlayerId——若不在这里退订，后续任意一条其它套件的
                // 用例（真实复现：GreyBoxTests 的目标选取 NearestInShapeStrategy）在其自己的
                // WorldSim 上按半径查询空间索引时会捞到这个陈旧 id，进而对一个只存在于本用例已销毁
                // WorldSim 里的单位调用 WorldUnitAccess.GetPosition 抛 InvalidOperationException。
                fx.Gameplay.SaveSystem.DeleteSlot(SlotB);
                fx.Host.SpatialQuery.Unregister(PlayerId);
                fx.ViewBinder.Dispose();
                fx.EquipVisualSource.Dispose();
            }
        }
    }
}
