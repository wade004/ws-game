using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// CORE-170-02 根治（architecture/落地计划/audit-8160178-20260908，P2，第十轮审计已复现）：
    /// <c>Core.Numbers.Progression.ProgressionHost.AddXp</c>/<c>RestoreState</c> 更新 Progression
    /// 内部权威等级，但修复前从不同步 <c>Core.Carriers.Unit.PlayerUnit.Level</c> 实体字段；生产装配
    /// （<c>games/_template/Runtime/GameBootstrap.cs</c> 等）构造 <c>PlayerUnit</c> 时也从未写入
    /// <c>Level</c>（构造函数默认值 1），只把等级传给 <c>RulesAssembly.RegisterUnit</c>。
    /// <c>Core.Carriers.Unit.WorldUnitAccess.GetLevel</c>（<see cref="Core.Rules.Common.IUnitAccess"/>
    /// 的真实实现，供装备需求判断等运行期消费者调用）直接读取实体字段，与 Progression 内部权威等级
    /// 各自独立、互不同步——真实探针复现 <c>rules_progression_level=2;entity_level=1</c>，等级 2 的
    /// 装备需求判断因此读到过期的实体等级 1，返回 <c>RequirementNotMet</c>。
    /// <para>
    /// 根治后 <see cref="Core.Numbers.Progression.ProgressionHost"/> 是单位等级的唯一权威，经
    /// <see cref="Core.Numbers.Progression.LevelSync"/> 委托（<c>CarriersAssembly</c> 注入
    /// <c>WorldUnitAccess.SetLevel</c>）在首次注册（<c>RegisterUnit</c>）、升级（<c>AddXp</c>）、读档
    /// 恢复（<c>RestoreState</c>）三个时机把权威等级同步写回实体字段。本文件用真实
    /// <c>GameplayAssembly</c> + 真实 <c>EquipmentHost</c> + 真实 <c>SaveSystem</c> 验证审计验收清单：
    /// 多级真实 <c>AddXp</c>、<c>Progression</c> 读档、<c>WorldUnitAccess.GetLevel</c>、等级需求装备
    /// 四者在真实组合根中保持同一等级。
    /// </para>
    /// </summary>
    public sealed class CORE_170_02_ProgressionLevelSyncTests
    {
        private static readonly Id MapId = new Id("world.core_170_02_map");
        private static readonly Id PlayerId = new Id("unit.core_170_02_player");
        private static readonly Id PlayerFactionId = new Id("fac.core_170_02_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.core_170_02_sample");

        private static readonly Id GatedSlot = new Id("item.slot.core_170_02_gated");
        private static readonly Id GatedItemTemplate = new Id("item.sample_core_170_02_gated");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.power\", \"name_key\": \"l10n.stat.power.name\", \"group\": \"primary\", \"default_base\": 1}," +
            "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string LevelCurveRows =
            "[{\"id\": \"" + "prog.curve.core_170_02_sample" + "\", \"max_level\": 3, " +
            "\"entries\": [" +
            "{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}," +
            "{\"level\": 2, \"xp_to_next\": 100, \"growth\": {}}," +
            "{\"level\": 3, \"xp_to_next\": 0, \"growth\": {}}" +
            "]}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.core_170_02_sample" + "\", \"name_key\": \"l10n.arch.class.core_170_02_sample.name\", " +
            "\"primary_stat\": \"stat.power\", \"base_stats\": {\"stat.power\": 1}, " +
            "\"power_types\": [\"arch.power.health\"], " +
            "\"level_curve_ref\": \"" + "prog.curve.core_170_02_sample" + "\"}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static InMemoryDataSource BuildDataSource()
        {
            var itemSlotDefinitionRows = "[{\"id\": \"" + GatedSlot.Value + "\", \"name_key\": \"l10n.item.slot.core_170_02_gated\"}]";
            var itemQualityDefinitionRows =
                "[{\"id\": \"item.quality.core_170_02_common\", \"name_key\": \"l10n.item.quality.core_170_02_common\"}]";

            // 等级 2 需求装备：真实探针（core-persistence-probe RunRollbackRequirementOrder）用的
            // 同一种配置——requirements.level 是 EquipmentHost.Equip 判断 RequirementNotMet 的真实
            // 生产路径（EquipmentHost.cs:262-265 _unitAccess.GetLevel），不是本测试自己模拟的假条件。
            var itemTemplateRows = "[{\"id\": \"" + GatedItemTemplate.Value + "\", \"slot\": \"" + GatedSlot.Value + "\", " +
                "\"quality\": \"item.quality.core_170_02_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.core_170_02_gated\", \"stack_size\": 1, \"name_key\": \"l10n.item.core_170_02_gated\", " +
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
        /// 1，这正是 CORE-170-02 真实探针复现用的"生产不同步"前提——<c>RunRollbackRequirementOrder</c>
        /// 注释"Production bootstraps pass the level to RulesAssembly but construct PlayerUnit
        /// without copying it; use that exact unsynchronised setup here"），只把 <paramref
        /// name="level"/> 传给 <c>Rules.RegisterUnit</c>。根治后的 <see
        /// cref="Core.Numbers.Progression.LevelSync"/> 委托应当在 <c>RegisterUnit</c> 内部就把这份
        /// 权威等级同步写回实体字段，不需要调用方自己再补一行 <c>player.Level = level</c>。
        /// </summary>
        private static Fixture Build(int level = 1, StubFileSystem? sharedFs = null)
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
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.core_170_02_test")), bus);

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
                $"CORE-170-02 核心断言：Progression.GetLevel（{progressionLevel}）、" +
                $"WorldUnitAccess.GetLevel（{unitAccessLevel}）、PlayerUnit.Level（{entityLevel}）三者必须一致。");
        }

        private static Id EquipFreshInstance(Fixture fx, Id templateId, Id slot, out Core.Carriers.Common.EquipResult result)
        {
            fx.Gameplay.Carriers.Inventory.AddItem(PlayerId, templateId, 1);
            var items = fx.Gameplay.Carriers.Inventory.ListItems(PlayerId);
            var instanceId = items[items.Count - 1].InstanceId;
            result = fx.Gameplay.Carriers.Equipment.Equip(PlayerId, instanceId, slot);
            return instanceId;
        }

        /// <summary>首次注册（默认 1 级）即三者一致；首次注册就已经暴露 CORE-170-02（生产装配从不
        /// 同步 <c>PlayerUnit.Level</c>，即便压根没有升级过，1 级本身也可能与传给 <c>RegisterUnit</c>
        /// 的非 1 起始等级不一致）——本用例额外覆盖"起始等级不是 1"的注册场景。</summary>
        [Fact]
        public void RegisterUnit_NonDefaultStartLevel_SyncsEntityLevelImmediately()
        {
            var fx = Build(level: 2);
            AssertLevelConsistent(fx, 2);

            var instance = EquipFreshInstance(fx, GatedItemTemplate, GatedSlot, out var result);
            Assert.True(result.Success, $"起始等级已经是 2，等级需求装备应当直接可装备：{result.Reason}");
            _ = instance;
        }

        /// <summary>核心复现与根治：多级真实 <c>AddXp</c> 升级后，三者应保持一致，且原本因为实体等级
        /// 过期返回 <c>RequirementNotMet</c> 的等级需求装备应当变为可装备成功。</summary>
        [Fact]
        public void MultiLevelAddXp_KeepsProgressionEntityAndUnitAccessLevelConsistent_AndUnlocksLevelGatedEquipment()
        {
            var fx = Build(level: 1);
            AssertLevelConsistent(fx, 1);

            // 修复前的真实探针行为：1 级时装备等级 2 需求的物品应该失败。
            var earlyInstance = EquipFreshInstance(fx, GatedItemTemplate, GatedSlot, out var earlyResult);
            Assert.False(earlyResult.Success);
            Assert.Equal(Core.Carriers.Common.EquipFailureReason.RequirementNotMet, earlyResult.Reason);
            fx.Gameplay.Carriers.Inventory.RemoveItem(PlayerId, earlyInstance, 1);

            // 真实升级路径：AddXp 到 2 级（曲线 1 级 100 xp）。
            fx.Gameplay.Carriers.Rules.Progression.AddXp(PlayerId, new Id("core_170_02.xp_source"), 100);
            fx.Bus.DispatchPending();
            AssertLevelConsistent(fx, 2);

            var midInstance = EquipFreshInstance(fx, GatedItemTemplate, GatedSlot, out var midResult);
            Assert.True(midResult.Success,
                $"CORE-170-02 核心断言：升级到 2 级后，等级 2 需求的装备应当可以装备成功：{midResult.Reason}");
            fx.Gameplay.Carriers.Equipment.Unequip(PlayerId, GatedSlot);
            fx.Gameplay.Carriers.Inventory.RemoveItem(PlayerId, midInstance, 1);

            // 再升一级（曲线 2 级 100 xp）——多级验证不是只在"恰好跨过一次"时凑巧一致。
            fx.Gameplay.Carriers.Rules.Progression.AddXp(PlayerId, new Id("core_170_02.xp_source"), 100);
            fx.Bus.DispatchPending();
            AssertLevelConsistent(fx, 3);
        }

        /// <summary>核心复现与根治：读档恢复（<c>ProgressionHost.RestoreState</c>）同样是一条等级会
        /// 确立的路径——真实生产场景是"先创建新角色（默认 1 级）、再读档覆盖"，本用例用两个独立的
        /// 真实 <c>GameplayAssembly</c> 组合根（各自的 <c>PlayerUnit</c> 都以默认 1 级构造）模拟这一
        /// 顺序：fixture A 升到 2 级并存档，fixture B（全新、默认 1 级）读档后，三者应同步为 2 级。</summary>
        [Fact]
        public void RestoreState_FromFreshBootstrap_SyncsProgressionEntityAndUnitAccessLevel()
        {
            var sharedFs = new StubFileSystem();
            var fxA = Build(level: 1, sharedFs: sharedFs);
            fxA.Gameplay.RegisterPersistables(fxA.SaveSystem, fxA.Player);
            fxA.Gameplay.Carriers.Rules.Progression.AddXp(fxA.Player.EntityId, new Id("core_170_02.xp_source"), 100);
            fxA.Bus.DispatchPending();
            AssertLevelConsistent(fxA, 2);

            var saveResult = fxA.SaveSystem.Save(new SaveRequest(new Id("slot.core_170_02"), "t1"));
            Assert.True(saveResult.Success, "存档应当成功。");

            // fixture B：全新组合根，PlayerUnit 同样以默认 1 级构造（真实"新建角色→读档"顺序），
            // 与 fixture A 共享同一个内存文件系统实例，读到的是 fixture A 刚写入的同一个存档槽。
            var fxB = Build(level: 1, sharedFs: sharedFs);
            AssertLevelConsistent(fxB, 1);
            fxB.Gameplay.RegisterPersistables(fxB.SaveSystem, fxB.Player);

            var loadResult = fxB.SaveSystem.Load(new Id("slot.core_170_02"));
            Assert.True(loadResult.Status == LoadStatus.Loaded || loadResult.Status == LoadStatus.LoadedFromBackup,
                $"读档应当成功：{loadResult.Status}");

            AssertLevelConsistent(fxB, 2);

            var instance = EquipFreshInstance(fxB, GatedItemTemplate, GatedSlot, out var result);
            Assert.True(result.Success, $"读档恢复到 2 级后，等级 2 需求的装备应当可以装备成功：{result.Reason}");
            _ = instance;
        }
    }
}
