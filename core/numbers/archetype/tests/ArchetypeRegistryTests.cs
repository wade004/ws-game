using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.Archetype;
using Xunit;

namespace Tests.Numbers.Archetype
{
    public class ArchetypeRegistryTests
    {
        // -----------------------------------------------------------------
        // 公共夹具（全程只用 sample_a/sample_b 一类中性 id，不出现具体职业名）
        // -----------------------------------------------------------------

        private static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
                new EventDefinition(ArchetypeEventKeys.Applied, "archetype",
                    new[] { "unitId", "classId", "raceId" }),
            });
            return new EventBus(catalog);
        }

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private const string ClassRows = @"[
            {
                ""id"": ""arch.class.sample_a"",
                ""name_key"": ""l10n.arch.class.sample_a.name"",
                ""primary_stat"": ""stat.strength"",
                ""base_stats"": { ""stat.strength"": 10, ""stat.vitality"": 8 },
                ""power_types"": [ ""arch.power.sample_energy"" ],
                ""talent_tree_ref"": ""arch.talent_tree.sample_tree"",
                ""level_curve_ref"": ""prog.curve.sample""
            }
        ]";

        private const string RaceRows = @"[
            {
                ""id"": ""arch.race.sample_x"",
                ""name_key"": ""l10n.arch.race.sample_x.name"",
                ""stat_mods"": { ""stat.strength"": 1, ""stat.agility"": 2 },
                ""passive_auras"": [ ""skill.aura.sample_glow"" ]
            }
        ]";

        private const string GoodTalentTreeRows = @"[
            {
                ""id"": ""arch.talent_tree.sample_tree"",
                ""nodes"": [
                    { ""id"": ""root"", ""prerequisites"": [], ""cost"": 1, ""grants"": {} },
                    { ""id"": ""branch_a"", ""prerequisites"": [""root""], ""cost"": 1, ""grants"": {} }
                ]
            }
        ]";

        private const string LevelCurveRows = @"[
            {
                ""id"": ""prog.curve.sample"",
                ""max_level"": 1,
                ""entries"": [ { ""level"": 1, ""xp_to_next"": 0, ""growth"": {} } ]
            }
        ]";

        private static DataRegistry MakeRegistry(
            string classRows, string raceRows, string talentTreeRows,
            out IEventBus bus, bool includeLevelCurve = true, bool registerTalentRule = true)
        {
            bus = MakeBus();
            var source = new InMemoryDataSource()
                .Add("arch.class", Envelope("arch.class", classRows))
                .Add("arch.race", Envelope("arch.race", raceRows))
                .Add("arch.talent_tree", Envelope("arch.talent_tree", talentTreeRows));

            if (includeLevelCurve)
            {
                source.Add("prog.level_curve", Envelope("prog.level_curve", LevelCurveRows));
            }

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(ArchSchemas.Class);
            registry.RegisterSchema(ArchSchemas.Race);
            registry.RegisterSchema(ArchSchemas.TalentTree);
            if (includeLevelCurve)
            {
                registry.RegisterSchema(Core.Numbers.Progression.ProgSchemas.LevelCurve);
            }
            if (registerTalentRule)
            {
                registry.RegisterValidationRule(new ArchTalentTreeCycleValidationRule());
            }
            return registry;
        }

        private sealed class BaseWriteCall
        {
            public Id UnitId;
            public Id Stat;
            public double Value;
        }

        private sealed class ModWriteCall
        {
            public Id UnitId;
            public Id Stat;
            public string Op = "";
            public double Value;
            public Id SourceId;
        }

        private sealed class RecordingWriters
        {
            public readonly List<string> CallOrder = new List<string>();
            public readonly List<BaseWriteCall> BaseWrites = new List<BaseWriteCall>();
            public readonly List<ModWriteCall> ModWrites = new List<ModWriteCall>();
            public readonly List<(Id UnitId, IReadOnlyList<Id> PowerTypes)> PowerRegistrations = new List<(Id, IReadOnlyList<Id>)>();

            public void WriteBase(Id unitId, Id stat, double value)
            {
                CallOrder.Add("base");
                BaseWrites.Add(new BaseWriteCall { UnitId = unitId, Stat = stat, Value = value });
            }

            public void WriteMod(Id unitId, Id stat, string op, double value, Id sourceId)
            {
                CallOrder.Add("mod");
                ModWrites.Add(new ModWriteCall { UnitId = unitId, Stat = stat, Op = op, Value = value, SourceId = sourceId });
            }

            public void RegisterPowers(Id unitId, IReadOnlyList<Id> powerTypes)
            {
                CallOrder.Add("powers");
                PowerRegistrations.Add((unitId, powerTypes));
            }
        }

        private static ArchetypeRegistry MakeRegistryHost(IDataRegistryView registry, IEventBus bus, RecordingWriters writers) =>
            new ArchetypeRegistry(registry, bus, writers.WriteBase, writers.WriteMod, writers.RegisterPowers);

        // -----------------------------------------------------------------
        // 1. 加载 class / race / talent_tree
        // -----------------------------------------------------------------

        [Fact]
        public void LoadAll_ParsesClassDefinition()
        {
            var registry = MakeRegistry(ClassRows, RaceRows, GoodTalentTreeRows, out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeRegistryHost(registry, bus, writers);

            var cls = host.GetClass(new Id("arch.class.sample_a"));
            Assert.NotNull(cls);
            Assert.Equal("l10n.arch.class.sample_a.name", cls!.NameKey);
            Assert.Equal("stat.strength", cls.PrimaryStat.Value);
            Assert.Equal(2, cls.BaseStats.Count);
            Assert.Single(cls.PowerTypes);
            Assert.Equal("arch.power.sample_energy", cls.PowerTypes[0].Value);
            Assert.Equal("arch.talent_tree.sample_tree", cls.TalentTreeRef!.Value.Value);
            Assert.Equal("prog.curve.sample", cls.LevelCurveRef!.Value.Value);
            Assert.Null(cls.SkillBookRef);
            Assert.Single(host.Classes);
        }

        [Fact]
        public void LoadAll_ParsesRaceDefinition()
        {
            var registry = MakeRegistry(ClassRows, RaceRows, GoodTalentTreeRows, out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeRegistryHost(registry, bus, writers);

            var race = host.GetRace(new Id("arch.race.sample_x"));
            Assert.NotNull(race);
            Assert.Equal(2, race!.StatMods.Count);
            Assert.Single(race.PassiveAuras);
            Assert.Equal("skill.aura.sample_glow", race.PassiveAuras[0].Value);
        }

        [Fact]
        public void LoadAll_ParsesTalentTree()
        {
            var registry = MakeRegistry(ClassRows, RaceRows, GoodTalentTreeRows, out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeRegistryHost(registry, bus, writers);

            var tree = host.GetTalentTree(new Id("arch.talent_tree.sample_tree"));
            Assert.NotNull(tree);
            Assert.Equal(2, tree!.Nodes.Count);
            Assert.Equal("root", tree.Nodes[0].NodeId);
            Assert.Empty(tree.Nodes[0].Prerequisites);
            Assert.Equal("branch_a", tree.Nodes[1].NodeId);
            Assert.Single(tree.Nodes[1].Prerequisites);
            Assert.Equal("root", tree.Nodes[1].Prerequisites[0]);
        }

        // -----------------------------------------------------------------
        // 2. ApplyTo 调用顺序与参数、事件
        // -----------------------------------------------------------------

        [Fact]
        public void ApplyTo_WithRace_CallsWritersInOrder_AndPublishesEvent()
        {
            var registry = MakeRegistry(ClassRows, RaceRows, GoodTalentTreeRows, out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeRegistryHost(registry, bus, writers);

            ArchetypeAppliedEvent? applied = null;
            bus.Subscribe<ArchetypeAppliedEvent>(ArchetypeEventKeys.Applied, e => applied = e);

            var unit = new Id("unit.hero_01");
            var result = host.ApplyTo(unit, new Id("arch.class.sample_a"), new Id("arch.race.sample_x"));

            // 顺序：base(×2) → mod(×2) → powers(×1)
            Assert.Equal(new[] { "base", "base", "mod", "mod", "powers" }, writers.CallOrder);

            Assert.Equal(2, writers.BaseWrites.Count);
            Assert.Contains(writers.BaseWrites, c => c.Stat.Value == "stat.strength" && c.Value == 10);
            Assert.Contains(writers.BaseWrites, c => c.Stat.Value == "stat.vitality" && c.Value == 8);

            Assert.Equal(2, writers.ModWrites.Count);
            var strengthMod = writers.ModWrites.Find(c => c.Stat.Value == "stat.strength");
            Assert.NotNull(strengthMod);
            Assert.Equal("flat", strengthMod!.Op);
            Assert.Equal(1, strengthMod.Value);
            Assert.Equal("arch.race.sample_x", strengthMod.SourceId.Value);

            Assert.Single(writers.PowerRegistrations);
            Assert.Equal(unit, writers.PowerRegistrations[0].UnitId);
            Assert.Single(writers.PowerRegistrations[0].PowerTypes);

            Assert.NotNull(applied);
            Assert.Equal(unit, applied!.UnitId);
            Assert.Equal("arch.class.sample_a", applied.ClassId.Value);
            Assert.Equal("arch.race.sample_x", applied.RaceId!.Value.Value);

            Assert.Equal("prog.curve.sample", result.LevelCurveRef!.Value.Value);
            Assert.Equal("arch.race.sample_x", result.RaceId!.Value.Value);
        }

        // -----------------------------------------------------------------
        // 3. 无 race
        // -----------------------------------------------------------------

        [Fact]
        public void ApplyTo_WithoutRace_SkipsModifierWriter()
        {
            var registry = MakeRegistry(ClassRows, RaceRows, GoodTalentTreeRows, out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeRegistryHost(registry, bus, writers);

            var unit = new Id("unit.hero_02");
            var result = host.ApplyTo(unit, new Id("arch.class.sample_a"), null);

            Assert.Equal(new[] { "base", "base", "powers" }, writers.CallOrder);
            Assert.Empty(writers.ModWrites);
            Assert.Null(result.RaceId);
        }

        [Fact]
        public void ApplyTo_UnknownClass_Throws()
        {
            var registry = MakeRegistry(ClassRows, RaceRows, GoodTalentTreeRows, out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeRegistryHost(registry, bus, writers);

            Assert.Throws<ArgumentException>(() => host.ApplyTo(new Id("unit.hero_03"), new Id("arch.class.unknown"), null));
        }

        [Fact]
        public void ApplyTo_UnknownRace_Throws()
        {
            var registry = MakeRegistry(ClassRows, RaceRows, GoodTalentTreeRows, out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeRegistryHost(registry, bus, writers);

            Assert.Throws<ArgumentException>(() =>
                host.ApplyTo(new Id("unit.hero_04"), new Id("arch.class.sample_a"), new Id("arch.race.unknown")));
        }

        // -----------------------------------------------------------------
        // 4. 天赋树成环 / 前置不存在被校验规则拦截
        // -----------------------------------------------------------------

        [Fact]
        public void TalentTree_Cycle_DetectedByValidationRule()
        {
            const string cyclicTree = @"[
                {
                    ""id"": ""arch.talent_tree.cyclic"",
                    ""nodes"": [
                        { ""id"": ""a"", ""prerequisites"": [""b""], ""cost"": 1, ""grants"": {} },
                        { ""id"": ""b"", ""prerequisites"": [""a""], ""cost"": 1, ""grants"": {} }
                    ]
                }
            ]";

            var registry = MakeRegistry(ClassRows, RaceRows, cyclicTree, out _);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "talent_prerequisite_cycle");
        }

        [Fact]
        public void TalentTree_MissingPrerequisite_DetectedByValidationRule()
        {
            const string brokenTree = @"[
                {
                    ""id"": ""arch.talent_tree.broken"",
                    ""nodes"": [
                        { ""id"": ""a"", ""prerequisites"": [""does_not_exist""], ""cost"": 1, ""grants"": {} }
                    ]
                }
            ]";

            var registry = MakeRegistry(ClassRows, RaceRows, brokenTree, out _);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "talent_prerequisite_missing");
        }

        // -----------------------------------------------------------------
        // 5. level_curve_ref 引用完整性
        // -----------------------------------------------------------------

        [Fact]
        public void LevelCurveRef_ReferenceIntegrity_ReportsError_WhenTargetMissing()
        {
            // 不加载 prog.level_curve 表：arch.class.level_curve_ref 指向的曲线不存在。
            var registry = MakeRegistry(ClassRows, RaceRows, GoodTalentTreeRows, out _, includeLevelCurve: false);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "level_curve_ref");
        }

        [Fact]
        public void LevelCurveRef_ReferenceIntegrity_Passes_WhenTargetPresent()
        {
            var registry = MakeRegistry(ClassRows, RaceRows, GoodTalentTreeRows, out _, includeLevelCurve: true);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking);
        }
    }
}
