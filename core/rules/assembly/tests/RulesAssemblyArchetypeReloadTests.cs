using System;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Rules.Assembly
{
    /// <summary>
    /// T-L14（测试覆盖剩余项 2026-10-01）：<c>RulesAssembly</c> 对外的几个转发/编排方法的直接用例——
    /// <c>RegisterUnit</c>（无曲线/无 AI 的最小路径）、<c>ReapplyRacePassiveAuras</c>（未知种族空操作）、
    /// <c>ReloadArchetypeAndRace</c>（换种族增删修饰、未知职业/种族的失败原子性）、<c>RegisterTickHandlers</c>。
    /// 此前这些方法只经 gameplay 层装配或整局战斗间接执行。惯例同 <c>ArchetypeDerivationOverrideTests</c>：
    /// 独立的最小化 <see cref="Core.Rules.Assembly.RulesAssembly"/> 夹具。
    /// </summary>
    public sealed class RulesAssemblyArchetypeReloadTests
    {
        private static readonly Id StatPower = new Id("stat.rar_power");
        private static readonly Id Unit = new Id("unit.rar_unit");
        private static readonly Id ClassA = new Id("arch.class.rar_a");
        private static readonly Id ClassB = new Id("arch.class.rar_b");
        private static readonly Id RaceA = new Id("arch.race.rar_a");
        private static readonly Id RaceB = new Id("arch.race.rar_b");

        private const double ClassABase = 10;
        private const double ClassBBase = 25;
        private const double RaceAMod = 3;
        private const double RaceBMod = 7;

        private const string StatDefinitionJson = @"
        {
            ""table"": ""stat.definition"", ""schema_version"": 2,
            ""rows"": [
                { ""id"": ""stat.rar_power"", ""name_key"": ""l10n.stat.rar_power.name"", ""category"": ""primary"", ""default_base"": 0 }
            ]
        }";

        private const string ArchPowerTypeJson = @"
        {
            ""table"": ""arch.power_type"", ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""arch.power.rar_energy"", ""name_key"": ""l10n.arch.power.rar_energy.name"",
                  ""max_source"": { ""kind"": ""fixed"", ""value"": 100 } }
            ]
        }";

        private static string ArchClassJson() => @"
        {
            ""table"": ""arch.class"", ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""arch.class.rar_a"", ""name_key"": ""l10n.arch.class.rar_a.name"",
                  ""primary_stat"": ""stat.rar_power"", ""base_stats"": { ""stat.rar_power"": 10 },
                  ""power_types"": [ ""arch.power.rar_energy"" ] },
                { ""id"": ""arch.class.rar_b"", ""name_key"": ""l10n.arch.class.rar_b.name"",
                  ""primary_stat"": ""stat.rar_power"", ""base_stats"": { ""stat.rar_power"": 25 },
                  ""power_types"": [ ""arch.power.rar_energy"" ] }
            ]
        }";

        private static string ArchRaceJson() => @"
        {
            ""table"": ""arch.race"", ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""arch.race.rar_a"", ""name_key"": ""l10n.arch.race.rar_a.name"", ""stat_mods"": { ""stat.rar_power"": 3 } },
                { ""id"": ""arch.race.rar_b"", ""name_key"": ""l10n.arch.race.rar_b.name"", ""stat_mods"": { ""stat.rar_power"": 7 } }
            ]
        }";

        private const string HitTableJson = @"
        {
            ""table"": ""combat.hit_table_config"", ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""combat.hit_table.default"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": false, ""base"": 0},
                  ""crit_multiplier_base"": 2.0 }
            ]
        }";

        private const string ResistCurveJson = @"{ ""table"": ""combat.resist_curve"", ""schema_version"": 1, ""rows"": [] }";

        private static Core.Rules.Assembly.RulesAssembly Build(bool autoRegisterTickHandlers = true)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("stat.definition", StatDefinitionJson)
                .Add("arch.class", ArchClassJson())
                .Add("arch.race", ArchRaceJson())
                .Add("arch.power_type", ArchPowerTypeJson)
                .Add("combat.hit_table_config", HitTableJson)
                .Add("combat.resist_curve", ResistCurveJson);

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            Core.Rules.Assembly.RulesSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            return new Core.Rules.Assembly.RulesAssembly(
                bus, registry, new RngHost(1), new Tests.Rules.Combat.FakeUnitAccess(), new StubSpatialQuery(), new WorldSim(bus),
                statOptions: new Core.Numbers.StatBlock.StatHostOptions(),
                autoRegisterTickHandlers: autoRegisterTickHandlers);
        }

        [Fact]
        public void RegisterUnit_WithClassAndRace_AppliesBaseStatsAndRaceModifier()
        {
            var rules = Build();

            rules.RegisterUnit(Unit, ClassA, RaceA);

            Assert.True(rules.Stats.IsRegistered(Unit));
            Assert.Equal(ClassABase + RaceAMod, rules.Stats.GetStat(Unit, StatPower));
        }

        [Fact]
        public void RegisterUnit_WithoutAiProfile_DoesNotRequireOne()
        {
            var rules = Build();

            var ex = Record.Exception(() => rules.RegisterUnit(Unit, ClassA, raceId: null));

            Assert.Null(ex);
            Assert.Equal(ClassABase, rules.Stats.GetStat(Unit, StatPower));
        }

        [Fact]
        public void ReloadArchetypeAndRace_RaceChange_SwapsRaceModifiers()
        {
            var rules = Build();
            rules.RegisterUnit(Unit, ClassA, RaceA);

            rules.ReloadArchetypeAndRace(Unit, ClassA, RaceB, ClassA, RaceA);

            Assert.Equal(ClassABase + RaceBMod, rules.Stats.GetStat(Unit, StatPower));
        }

        [Fact]
        public void ReloadArchetypeAndRace_SameRace_DoesNotStackRaceModifierTwice()
        {
            var rules = Build();
            rules.RegisterUnit(Unit, ClassA, RaceA);

            rules.ReloadArchetypeAndRace(Unit, ClassA, RaceA, ClassA, RaceA);
            rules.ReloadArchetypeAndRace(Unit, ClassA, RaceA, ClassA, RaceA);

            Assert.Equal(ClassABase + RaceAMod, rules.Stats.GetStat(Unit, StatPower));
        }

        [Fact]
        public void ReloadArchetypeAndRace_ClassChange_RewritesBaseStats_KeepsRaceModifier()
        {
            var rules = Build();
            rules.RegisterUnit(Unit, ClassA, RaceA);

            rules.ReloadArchetypeAndRace(Unit, ClassB, RaceA, ClassA, RaceA);

            Assert.Equal(ClassBBase + RaceAMod, rules.Stats.GetStat(Unit, StatPower));
        }

        [Fact]
        public void ReloadArchetypeAndRace_UnknownClass_ThrowsArgumentException_AndLeavesStatsUntouched()
        {
            var rules = Build();
            rules.RegisterUnit(Unit, ClassA, RaceA);
            var before = rules.Stats.GetStat(Unit, StatPower);

            Assert.Throws<ArgumentException>(() =>
                rules.ReloadArchetypeAndRace(Unit, new Id("arch.class.rar_missing"), RaceB, ClassA, RaceA));

            // 失败原子性：未知职业在任何写入之前就应被识别，旧种族的修饰不应已被摘掉。
            Assert.Equal(before, rules.Stats.GetStat(Unit, StatPower));
        }

        [Fact]
        public void ReloadArchetypeAndRace_UnknownRace_ThrowsArgumentException_AndLeavesStatsUntouched()
        {
            var rules = Build();
            rules.RegisterUnit(Unit, ClassA, RaceA);
            var before = rules.Stats.GetStat(Unit, StatPower);

            Assert.Throws<ArgumentException>(() =>
                rules.ReloadArchetypeAndRace(Unit, ClassB, new Id("arch.race.rar_missing"), ClassA, RaceA));

            Assert.Equal(before, rules.Stats.GetStat(Unit, StatPower));
        }

        [Fact]
        public void ReapplyRacePassiveAuras_UnknownRace_IsSilentNoOp()
        {
            var rules = Build();
            rules.RegisterUnit(Unit, ClassA, raceId: null);

            var ex = Record.Exception(() => rules.ReapplyRacePassiveAuras(Unit, new Id("arch.race.rar_missing")));

            Assert.Null(ex);
        }

        [Fact]
        public void RegisterTickHandlers_AutoRegisterDisabled_DefersPowerDiagnosticsUntilCalled()
        {
            var rules = Build(autoRegisterTickHandlers: false);
            Assert.Null(rules.PowerDiagnostics);

            rules.RegisterTickHandlers();

            Assert.NotNull(rules.PowerDiagnostics);
        }

        [Fact]
        public void Constructor_AutoRegisterEnabled_RegistersTickHandlersImmediately()
        {
            var rules = Build(autoRegisterTickHandlers: true);

            Assert.NotNull(rules.PowerDiagnostics);
        }
    }
}
