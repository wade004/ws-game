using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Foundation.SceneRouter;
using Core.Gameplay.Assembly;
using Core.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// P4 备忘 7 不变量（真实 <c>GameplayAssembly</c> + 真实 <c>SaveSystem</c>）：<see cref="ISaveSystem.ResetAllSections"/> 把
    /// 已登记的游戏层持久化域（等级/经验、背包）清回默认态，不碰地图与实体本身，不发业务事件——新游戏不再需要"装配后先存一份原始槽"。
    /// </summary>
    public sealed class GameplayAssemblySaveWiringTests
    {
        private static readonly Id MapId = new Id("world.p4_reset_map");
        private static readonly Id PlayerId = new Id("unit.p4_reset_player");
        private static readonly Id PlayerFactionId = new Id("fac.p4_reset_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.p4_reset_sample");

        private static readonly Id GatedSlot = new Id("item.slot.p4_reset_gated");
        private static readonly Id GatedItemTemplate = new Id("item.sample_p4_reset_gated");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.power\", \"name_key\": \"l10n.stat.power.name\", \"group\": \"primary\", \"default_base\": 1}," +
            "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string LevelCurveRows =
            "[{\"id\": \"" + "prog.curve.p4_reset_sample" + "\", \"max_level\": 3, " +
            "\"entries\": [" +
            "{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}," +
            "{\"level\": 2, \"xp_to_next\": 100, \"growth\": {}}," +
            "{\"level\": 3, \"xp_to_next\": 0, \"growth\": {}}" +
            "]}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.p4_reset_sample" + "\", \"name_key\": \"l10n.arch.class.p4_reset_sample.name\", " +
            "\"primary_stat\": \"stat.power\", \"base_stats\": {\"stat.power\": 1}, " +
            "\"power_types\": [\"arch.power.health\"], " +
            "\"level_curve_ref\": \"" + "prog.curve.p4_reset_sample" + "\"}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static InMemoryDataSource BuildDataSource()
        {
            var itemSlotDefinitionRows = "[{\"id\": \"" + GatedSlot.Value + "\", \"name_key\": \"l10n.item.slot.p4_reset_gated\"}]";
            var itemQualityDefinitionRows =
                "[{\"id\": \"item.quality.p4_reset_common\", \"name_key\": \"l10n.item.quality.p4_reset_common\"}]";

            // 等级 2 需求装备：真实探针（core-persistence-probe RunRollbackRequirementOrder）用的
            // 同一种配置——requirements.level 是 EquipmentHost.Equip 判断 RequirementNotMet 的真实
            // 生产路径（EquipmentHost.cs:262-265 _unitAccess.GetLevel），不是本测试自己模拟的假条件。
            var itemTemplateRows = "[{\"id\": \"" + GatedItemTemplate.Value + "\", \"slot\": \"" + GatedSlot.Value + "\", " +
                "\"quality\": \"item.quality.p4_reset_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.p4_reset_gated\", \"stack_size\": 1, \"name_key\": \"l10n.item.p4_reset_gated\", " +
                "\"requirements\": {\"level\": 2}}]";

            return new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", PowerTypeRows))
                .Add("prog.level_curve", Envelope("prog.level_curve", LevelCurveRows))
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("item.slot_definition", Envelope("item.slot_definition", itemSlotDefinitionRows))
                .Add("item.quality_definition", Envelope("item.quality_definition", itemQualityDefinitionRows))
                .Add("item.template", Envelope("item.template", itemTemplateRows))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"));
        }

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;
            public SaveSystem SaveSystem = null!;
        }

        /// <summary>
        /// 惯例同真实生产入口（<c>games/_template/Runtime/GameBootstrap.cs</c>）：<see
        /// cref="PlayerUnit"/> 构造时不手工设置 <see cref="PlayerUnit.Level"/>（沿用构造函数默认值
        /// 1，这正是 P4-7 真实探针复现用的"生产不同步"前提——<c>RunRollbackRequirementOrder</c>
        /// 注释"Production bootstraps pass the level to RulesAssembly but construct PlayerUnit
        /// without copying it; use that exact unsynchronised setup here"），只把 <paramref
        /// name="level"/> 传给 <c>Rules.RegisterUnit</c>。根治后的 <see
        /// cref="Core.Numbers.Progression.LevelSync"/> 委托应当在 <c>RegisterUnit</c> 内部就把这份
        /// 权威等级同步写回实体字段，不需要调用方自己再补一行 <c>player.Level = level</c>。
        /// </summary>
        private static Fixture Build(int level = 1, StubFileSystem? sharedFs = null, AutoSavePolicy? autoSave = null)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

            var source = BuildDataSource();
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            // 惯例同 GameplayAssemblyDeathReloadTests：两个独立的 GameplayAssembly 组合根（模拟
            // "全新进程读一份已有存档"）可以共享同一个内存文件系统实例，读写同一个存档槽——不需要
            // 额外发明"导出/导入原始存档文件"这类本仓库并不存在的 API。
            var fs = sharedFs ?? new StubFileSystem();
            var saveOptions = new SaveSystemOptions(new Id("game.p4_reset_test"));
            if (autoSave != null)
            {
                saveOptions.AutoSave = autoSave;
            }

            var saveSystem = new SaveSystem(fs, saveOptions, bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId);

            var player = new PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeSample) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: level);

            return new Fixture { Bus = bus, World = world, Gameplay = gameplay, Player = player, SaveSystem = saveSystem };
        }

        private static void AssertLevelConsistent(Fixture fx, int expectedLevel)
        {
            var progressionLevel = fx.Gameplay.Carriers.Rules.Progression.GetLevel(PlayerId);
            var unitAccessLevel = fx.Gameplay.Carriers.Units.GetLevel(PlayerId);
            var entityLevel = fx.Player.Level;

            Assert.Equal(expectedLevel, progressionLevel);
            Assert.Equal(expectedLevel, unitAccessLevel);
            Assert.Equal(expectedLevel, entityLevel);
            Assert.True(progressionLevel == unitAccessLevel && unitAccessLevel == entityLevel,
                $"P4-7 核心断言：Progression.GetLevel（{progressionLevel}）、" +
                $"WorldUnitAccess.GetLevel（{unitAccessLevel}）、PlayerUnit.Level（{entityLevel}）三者必须一致。");
        }

        [Fact]
        public void ResetAllSections_ClearsProgressionAndInventory_ToNewGameDefaults()
        {
            var fx = Build(level: 1);
            fx.Gameplay.RegisterPersistables(fx.SaveSystem, fx.Player);
            fx.Gameplay.Carriers.Rules.Progression.AddXp(PlayerId, new Id("p4_reset.xp_source"), 100);
            fx.Gameplay.Carriers.Inventory.AddItem(PlayerId, GatedItemTemplate, 1);
            fx.Bus.DispatchPending();
            Assert.Equal(2, fx.Gameplay.Carriers.Rules.Progression.GetLevel(PlayerId));
            Assert.NotEmpty(fx.Gameplay.Carriers.Inventory.ListItems(PlayerId));

            var ok = fx.SaveSystem.ResetAllSections();

            Assert.True(ok);
            Assert.Equal(1, fx.Gameplay.Carriers.Rules.Progression.GetLevel(PlayerId));
            Assert.Equal(1, fx.Player.Level);
            Assert.Empty(fx.Gameplay.Carriers.Inventory.ListItems(PlayerId));
            Assert.Same(fx.Player, fx.World.GetEntity(PlayerId));
        }

        private static readonly Id AutosaveSlot = new Id("slot.autosave");

        private static void TickOnce(Fixture fx)
        {
            fx.Bus.DispatchPending();
            fx.World.Tick(SimStep.Continuous(0.1));
        }

        /// <summary>P4 备忘 4（红：此前没有任何框架消费者订阅 scene.load_finished）：策略位 OnMapSwitch 开启时，地图切换完成后的下一个固定步写自动存档。</summary>
        [Fact]
        public void AutosaveOnMapSwitch_Enabled_WritesAutosaveSlot_AtNextTick_NotInsideEventHandling()
        {
            var fx = Build(autoSave: new AutoSavePolicy { OnMapSwitch = true });
            fx.Gameplay.RegisterPersistables(fx.SaveSystem, fx.Player);

            fx.Bus.Enqueue(new SceneLoadFinishedEvent(MapId));
            fx.Bus.DispatchPending();
            Assert.False(fx.SaveSystem.SlotExists(AutosaveSlot), "事件处理里只记待存，不写盘");

            fx.World.Tick(SimStep.Continuous(0.1));
            Assert.True(fx.SaveSystem.SlotExists(AutosaveSlot));
        }

        /// <summary>不变量：策略位默认关闭（OnMapSwitch 默认 false）时换图不写自动存档——开关由数据/选项声明，不是无条件行为。</summary>
        [Fact]
        public void AutosaveOnMapSwitch_DefaultPolicy_DoesNotWrite()
        {
            var fx = Build();
            fx.Gameplay.RegisterPersistables(fx.SaveSystem, fx.Player);

            fx.Bus.Enqueue(new SceneLoadFinishedEvent(MapId));
            TickOnce(fx);

            Assert.False(fx.SaveSystem.SlotExists(AutosaveSlot));
        }

        [Fact]
        public void AutosaveOnQuestComplete_TurnedInOrCompletedByPlayer_WritesAutosaveSlot()
        {
            var fx = Build(autoSave: new AutoSavePolicy { OnQuestComplete = true });
            fx.Gameplay.RegisterPersistables(fx.SaveSystem, fx.Player);

            fx.Bus.Enqueue(new QuestTurnedInEvent(PlayerId, new Id("quest.p4_any")));
            TickOnce(fx);

            Assert.True(fx.SaveSystem.SlotExists(AutosaveSlot));
        }

        [Fact]
        public void AutosaveOnQuestComplete_PolicyOff_OrOtherUnit_DoesNotWrite()
        {
            var off = Build(autoSave: new AutoSavePolicy { OnQuestComplete = false });
            off.Gameplay.RegisterPersistables(off.SaveSystem, off.Player);
            off.Bus.Enqueue(new QuestTurnedInEvent(PlayerId, new Id("quest.p4_any")));
            TickOnce(off);
            Assert.False(off.SaveSystem.SlotExists(AutosaveSlot));

            var other = Build(autoSave: new AutoSavePolicy { OnQuestComplete = true });
            other.Gameplay.RegisterPersistables(other.SaveSystem, other.Player);
            other.Bus.Enqueue(new QuestTurnedInEvent(new Id("unit.p4_someone_else"), new Id("quest.p4_any")));
            TickOnce(other);
            Assert.False(other.SaveSystem.SlotExists(AutosaveSlot));
        }

        /// <summary>不变量：同一固定步内的多个触发合并成一次写盘；下一步没有新触发则不再写。</summary>
        [Fact]
        public void Autosave_MultipleTriggersInOneStep_CoalesceToOneWrite()
        {
            var fx = Build(autoSave: new AutoSavePolicy { OnMapSwitch = true, OnQuestComplete = true });
            fx.Gameplay.RegisterPersistables(fx.SaveSystem, fx.Player);
            var saves = 0;
            using var _ = fx.Bus.Subscribe(SaveEventKeys.SaveCompleted, e => saves++);

            fx.Bus.Enqueue(new SceneLoadFinishedEvent(MapId));
            fx.Bus.Enqueue(new QuestTurnedInEvent(PlayerId, new Id("quest.p4_a")));
            fx.Bus.Enqueue(new QuestTurnedInEvent(PlayerId, new Id("quest.p4_b")));
            TickOnce(fx);
            TickOnce(fx);
            fx.Bus.DispatchPending();

            Assert.Equal(1, saves);
        }
    }
}
