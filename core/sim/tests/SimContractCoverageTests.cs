using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Sim;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// 仿真契约类型的字段断言与两个校验/登记类的直接用例（T-L6，2026-10-01 测试覆盖第四批）：
    /// <see cref="SimProgress"/>、<see cref="FightLogEntry"/>、<see cref="ResourceSample"/>、<see cref="ArenaCellResult"/>、
    /// <see cref="SimSchemas"/>/<see cref="SimSchemaCatalog"/>、<see cref="SimGrowthOpponentAmbiguityValidationRule"/>。
    /// 此前这些只经 <c>ToJson</c> 间接触及，字段本身与校验规则的各分支没有直接断言。
    /// </summary>
    public sealed class SimContractCoverageTests
    {
        // -----------------------------------------------------------------
        // SimProgress
        // -----------------------------------------------------------------

        [Fact]
        public void SimProgress_Fields_AreCarriedVerbatim_DetailDefaultsToNull()
        {
            var plain = new SimProgress("stage.x", 3, 10);
            var withDetail = new SimProgress("stage.y", 0, 0, "L5");

            Assert.Equal("stage.x", plain.Stage);
            Assert.Equal(3, plain.Completed);
            Assert.Equal(10, plain.Total);
            Assert.Null(plain.Detail);
            Assert.Equal("L5", withDetail.Detail);
        }

        [Fact]
        public void SimProgress_NullStage_Throws()
        {
            Assert.Equal("stage", Assert.Throws<ArgumentNullException>(() => new SimProgress(null!, 0, 1)).ParamName);
        }

        [Fact]
        public void SimProgress_Reports_FromArenaRun_HaveKnownStage_MonotonicCompleted_AndNoDetail()
        {
            var (world, scenario, sources) = BuildSmallArena();
            var reports = new List<SimProgress>();

            ArenaSimulation.Run(scenario, world.AnchorTable!, sources, false, default, new CollectingProgress(reports));

            var expectedTotal = scenario.Levels.Count * scenario.Opponent.LevelOffsets.Count;
            Assert.Equal(expectedTotal, reports.Count);
            for (var i = 0; i < reports.Count; i++)
            {
                Assert.Equal(ArenaSimulation.ProgressStageCell, reports[i].Stage);
                Assert.Equal(expectedTotal, reports[i].Total);
                Assert.Equal(i + 1, reports[i].Completed);
                Assert.Null(reports[i].Detail);
            }
        }

        private sealed class CollectingProgress : IProgress<SimProgress>
        {
            private readonly List<SimProgress> _sink;

            public CollectingProgress(List<SimProgress> sink)
            {
                _sink = sink;
            }

            public void Report(SimProgress value) => _sink.Add(value);
        }

        // -----------------------------------------------------------------
        // FightLogEntry / ResourceSample
        // -----------------------------------------------------------------

        [Fact]
        public void FightLogEntry_Fields_AreCarriedVerbatim_IncludingNulls()
        {
            var source = new Id("unit.a");
            var target = new Id("unit.b");
            var skill = new Id("skill.s");

            var full = new FightLogEntry(7, FightLogEventCategory.Damage, source, target, skill, 12.5, "crit");
            var sparse = new FightLogEntry(1, FightLogEventCategory.UnitDied, null, null, null, null, null);

            Assert.Equal(7, full.Tick);
            Assert.Equal(FightLogEventCategory.Damage, full.Category);
            Assert.Equal(source, full.SourceId);
            Assert.Equal(target, full.TargetId);
            Assert.Equal(skill, full.SkillOrEffectId);
            Assert.Equal(12.5, full.Amount);
            Assert.Equal("crit", full.ResultTag);

            Assert.Null(sparse.SourceId);
            Assert.Null(sparse.TargetId);
            Assert.Null(sparse.SkillOrEffectId);
            Assert.Null(sparse.Amount);
            Assert.Null(sparse.ResultTag);
        }

        [Fact]
        public void FightLogEventCategory_HasTheDocumentedMembersInOrder()
        {
            Assert.Equal(
                new[]
                {
                    FightLogEventCategory.Damage, FightLogEventCategory.Heal, FightLogEventCategory.SkillCastSuccess,
                    FightLogEventCategory.SkillCastFailed, FightLogEventCategory.AuraApplied, FightLogEventCategory.AuraRemoved,
                    FightLogEventCategory.UnitDied, FightLogEventCategory.ResourceChanged,
                },
                (FightLogEventCategory[])Enum.GetValues(typeof(FightLogEventCategory)));
        }

        [Fact]
        public void ResourceSample_Fields_AreCarriedVerbatim()
        {
            var sample = new ResourceSample(4, 37.25);

            Assert.Equal(4, sample.Tick);
            Assert.Equal(37.25, sample.Value);
        }

        [Fact]
        public void FightResult_CapturedLogEntriesAndResourceCurves_FollowTheirContracts()
        {
            var result = FightRunner.Run(new FightRunnerOptions
            {
                DataSources = SimTestWorldFactory.BuildEmbeddedDataSources(),
                ClassId = SimTestWorldFactory.EmbeddedClassId,
                PlayerLevel = 1,
                QualityId = new Id("item.quality.sim_common"),
                CreatureId = SimTestWorldFactory.EmbeddedCreatureWolfL1,
                CreatureLevel = 1,
                Seed = 4401UL,
                MaxTicks = 1200,
                CaptureEvents = true,
            });

            Assert.NotEmpty(result.CapturedEvents);
            var previousTick = 0;
            foreach (var entry in result.CapturedEvents)
            {
                Assert.InRange(entry.Tick, 1, result.TicksUsed);
                Assert.True(entry.Tick >= previousTick, "事件日志按 tick 升序追加");
                previousTick = entry.Tick;
            }

            Assert.Contains(result.CapturedEvents, e => e.Category == FightLogEventCategory.UnitDied);
            Assert.Contains(result.CapturedEvents, e => e.Category == FightLogEventCategory.Damage && e.Amount.HasValue && e.Amount.Value > 0);

            foreach (var curve in result.PlayerResourceCurves.Values)
            {
                var last = 0;
                foreach (var sample in curve)
                {
                    Assert.InRange(sample.Tick, 1, result.TicksUsed);
                    Assert.True(sample.Tick > last, "资源采样 tick 严格递增");
                    last = sample.Tick;
                }
            }
        }

        // -----------------------------------------------------------------
        // ArenaCellResult
        // -----------------------------------------------------------------

        private static (Core.Sim.HeadlessWorld World, ScenarioDef Scenario, IReadOnlyList<IDataSource> Sources) BuildSmallArena()
        {
            const string json = @"{
  ""table"": ""sim.scenario"", ""schema_version"": 1,
  ""rows"": [ {
      ""id"": ""sim.scenario.cov_small_arena"", ""kind"": ""arena"",
      ""player"": { ""class_id"": ""arch.class.sim_warrior"", ""level"": 1, ""quality_id"": ""item.quality.sim_common"" },
      ""opponent"": { ""creature_id"": ""creature.sim_wolf_l1"", ""tier_id"": ""creature.tier.sim_normal"", ""level_offsets"": [-1, 0] },
      ""levels"": [1, 5], ""runs"": 4, ""base_seed"": 880011, ""max_ticks"": 1200,
      ""bandwidths"": { ""dps"": 0.25, ""hp"": 0.25 }
  } ]
}";
            var fs = new StubFileSystem();
            fs.WriteTextAtomic("cov_overlay/sim/sim.scenario.json", json);
            var overlay = new FileSystemDataSource(fs, "cov_overlay");
            var sources = SimTestWorldFactory.BuildEmbeddedDataSources().Concat(new IDataSource[] { overlay }).ToList();

            var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = sources,
                Seed = 1,
                MapId = SimTestWorldFactory.EmbeddedMapId,
                PlayerId = SimTestWorldFactory.PlayerId,
                PlayerFactionId = SimTestWorldFactory.FactionPlayer,
                PlayerClassId = SimTestWorldFactory.EmbeddedClassId,
                GameId = SimTestWorldFactory.EmbeddedGameId,
            });
            return (world, world.ScenarioCatalog!.Get(new Id("sim.scenario.cov_small_arena")), sources);
        }

        [Fact]
        public void ArenaCellResult_Fields_SatisfyTheirStructuralInvariants()
        {
            var (world, scenario, sources) = BuildSmallArena();

            var report = ArenaSimulation.Run(scenario, world.AnchorTable!, sources);

            Assert.Equal(scenario.Levels.Count * scenario.Opponent.LevelOffsets.Count, report.Cells.Count);
            foreach (var cell in report.Cells)
            {
                Assert.Contains(cell.PlayerLevel, scenario.Levels);
                Assert.Contains(cell.LevelOffset, scenario.Opponent.LevelOffsets);
                Assert.Equal(Math.Max(1, cell.PlayerLevel + cell.LevelOffset), cell.CreatureLevel); // 低于 1 级钳到 1 级
                Assert.Equal(scenario.Runs, cell.Runs);

                // 胜率是 胜场数 / Runs：乘回 Runs 必为整数。
                var wins = cell.WinRate * cell.Runs;
                Assert.Equal(Math.Round(wins), wins, 9);
                Assert.InRange(cell.WinRate, 0.0, 1.0);
                Assert.InRange(cell.PlayerHitRateMean, 0.0, 1.0);
                Assert.True(cell.PlayerMaxHealth > 0);
                Assert.True(cell.PlayerDpsMean >= 0);
                Assert.True(cell.TtdEstimateMean >= 0);

                // 分位数有序：P10 <= 中位数 <= P90（耗时样本非空时），均值落在 [P10 下界, 样本最大值] 内不做强断言。
                if (cell.WinRate > 0)
                {
                    Assert.True(cell.TtkP10Seconds <= cell.TtkMedianSeconds + 1e-9);
                    Assert.True(cell.TtkMedianSeconds <= cell.TtkP90Seconds + 1e-9);
                    Assert.True(cell.TtkMeanSeconds > 0);
                }

                foreach (var share in cell.SkillShareMean.Values)
                {
                    Assert.InRange(share, 0.0, 1.0 + 1e-9);
                }
            }
        }

        // -----------------------------------------------------------------
        // SimSchemas / SimSchemaCatalog
        // -----------------------------------------------------------------

        [Fact]
        public void SimSchemas_AnchorAndScenario_DeclareTheirTableIdentity()
        {
            Assert.Equal("sim.anchor", SimSchemas.Anchor.Name);
            Assert.Equal("sim.scenario", SimSchemas.Scenario.Name);
            foreach (var schema in new[] { SimSchemas.Anchor, SimSchemas.Scenario })
            {
                Assert.Equal("id", schema.PrimaryKey);
                Assert.Equal(1, schema.CurrentSchemaVersion);
                Assert.Equal(SchemaLayer.Sim, schema.Layer);
                Assert.Equal("sim", schema.Module);
            }
        }

        [Fact]
        public void SimSchemas_AnchorFields_AllRequired_ExceptNote()
        {
            var required = SimSchemas.Anchor.Fields.Where(f => f.Required).Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

            Assert.Equal(
                new[]
                {
                    "dps", "expected_item_level", "hp", "id", "kill_interval_seconds", "level",
                    "level_duration_seconds", "quest_share", "ttd_seconds", "ttk_seconds",
                },
                required);
            Assert.False(SimSchemas.Anchor.Fields.Single(f => f.Name == "note").Required);
        }

        [Fact]
        public void SimSchemas_ScenarioFields_RequiredAndOptionalSplit()
        {
            var required = SimSchemas.Scenario.Fields.Where(f => f.Required).Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var optional = SimSchemas.Scenario.Fields.Where(f => !f.Required).Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

            Assert.Equal(new[] { "bandwidths", "base_seed", "id", "kind", "max_ticks", "opponent", "player", "runs" }, required);
            Assert.Equal(new[] { "anchor_ref", "levels", "level_from", "level_to", "note" }.OrderBy(n => n, StringComparer.Ordinal), optional);
            Assert.Equal(new[] { "arena", "growth", "coverage" }, SimSchemas.Scenario.Fields.Single(f => f.Name == "kind").EnumValues);
        }

        [Fact]
        public void SimSchemaCatalog_RegisterAll_RegistersBothSchemasAndTheThreeRules()
        {
            var source = new InMemoryDataSource();
            var registry = new DataRegistry(source, new EventBus(EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
            })));

            SimSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.Same(SimSchemas.Anchor, registry.GetSchema("sim.anchor"));
            Assert.Same(SimSchemas.Scenario, registry.GetSchema("sim.scenario"));
            var ruleIds = report.Rules.Select(r => r.RuleId).ToList();
            Assert.Contains(nameof(SimAnchorValidationRule), ruleIds);
            Assert.Contains(nameof(SimScenarioValidationRule), ruleIds);
            Assert.Contains(nameof(SimGrowthOpponentAmbiguityValidationRule), ruleIds);
            Assert.All(report.Rules.Where(r => r.RuleId.StartsWith("Sim", StringComparison.Ordinal)), r => Assert.True(r.NonEscalatable));
        }

        [Fact]
        public void SimBandwidthKeys_KnownKeys_EqualTheComparerLeafBandwidthKeyValues()
        {
            var expected = BaselineCompareOptions.DefaultLeafBandwidthKeys.Values.Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();

            Assert.Equal(expected, SimBandwidthKeys.KnownKeys.OrderBy(k => k, StringComparer.Ordinal).ToList());
            Assert.Contains("dps", SimBandwidthKeys.KnownKeys);
        }

        // -----------------------------------------------------------------
        // SimGrowthOpponentAmbiguityValidationRule
        // -----------------------------------------------------------------

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static TableSchema FactionStub() => new TableSchema("fac.faction", "id", 1, new[]
        {
            new FieldSchema("id", FieldKind.Id, required: true),
            new FieldSchema("default_reaction", FieldKind.String, required: false),
        });

        private static TableSchema ReactionStub() => new TableSchema("fac.reaction_matrix", "id", 1, new[]
        {
            new FieldSchema("id", FieldKind.Id, required: true),
            new FieldSchema("from", FieldKind.Id, required: false),
            new FieldSchema("to", FieldKind.Id, required: false),
            new FieldSchema("reaction", FieldKind.String, required: false),
        });

        private static TableSchema CreatureStub() => new TableSchema("creature.template", "id", 1, new[]
        {
            new FieldSchema("id", FieldKind.Id, required: true),
            new FieldSchema("tier", FieldKind.Id, required: false),
            new FieldSchema("faction_id", FieldKind.Id, required: false),
            new FieldSchema("level", FieldKind.Int, required: false),
        });

        private static List<ValidationIssue> RunRule(string factionRows, string reactionRows, string creatureRows)
        {
            var source = new InMemoryDataSource()
                .Add("fac.faction", Envelope("fac.faction", factionRows))
                .Add("fac.reaction_matrix", Envelope("fac.reaction_matrix", reactionRows))
                .Add("creature.template", Envelope("creature.template", creatureRows));
            var registry = new DataRegistry(source, new EventBus(EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
            })));
            registry.RegisterSchema(FactionStub());
            registry.RegisterSchema(ReactionStub());
            registry.RegisterSchema(CreatureStub());
            Assert.False(registry.LoadAll().IsBlocking);

            return new SimGrowthOpponentAmbiguityValidationRule().Validate(registry).ToList();
        }

        private const string Factions =
            "[{\"id\": \"fac.player\", \"default_reaction\": \"hostile\"}, {\"id\": \"fac.mob_a\", \"default_reaction\": \"hostile\"}, {\"id\": \"fac.mob_b\", \"default_reaction\": \"hostile\"}]";

        private static string Creature(string id, string tier, string faction, int level) =>
            "{\"id\": \"" + id + "\", \"tier\": \"" + tier + "\", \"faction_id\": \"" + faction + "\", \"level\": " + level + "}";

        [Fact]
        public void AmbiguityRule_Metadata_IsNonEscalatableWarningGradeCheckName()
        {
            IValidationRule rule = new SimGrowthOpponentAmbiguityValidationRule();

            Assert.True(rule.NonEscalatable);
            Assert.Equal(nameof(SimGrowthOpponentAmbiguityValidationRule), rule.RuleId);
            Assert.Equal("sim_growth_opponent_ambiguous", SimGrowthOpponentAmbiguityValidationRule.OpponentAmbiguousCheck);
        }

        [Fact]
        public void AmbiguityRule_TwoHostileCandidatesSameTierAndLevel_WarnsOnEach()
        {
            var issues = RunRule(Factions, "[]", "[" + string.Join(",", new[]
            {
                Creature("creature.a", "creature.tier.normal", "fac.mob_a", 5),
                Creature("creature.b", "creature.tier.normal", "fac.mob_b", 5),
            }) + "]");

            Assert.Equal(2, issues.Count);
            Assert.All(issues, i =>
            {
                Assert.Equal(ValidationSeverity.Warning, i.Severity);
                Assert.Equal(SimGrowthOpponentAmbiguityValidationRule.OpponentAmbiguousCheck, i.Check);
                Assert.Contains("creature.a", i.Message);
                Assert.Contains("creature.b", i.Message);
                Assert.Contains("creature.tier.normal", i.Message);
                Assert.Contains("level=5", i.Message);
            });
            Assert.Equal(new[] { "creature.a", "creature.b" }, issues.Select(i => i.RecordKey).OrderBy(k => k, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void AmbiguityRule_DifferentLevelOrDifferentTier_IsNotAmbiguous()
        {
            var differentLevel = RunRule(Factions, "[]", "[" + string.Join(",", new[]
            {
                Creature("creature.a", "creature.tier.normal", "fac.mob_a", 5),
                Creature("creature.b", "creature.tier.normal", "fac.mob_b", 6),
            }) + "]");
            var differentTier = RunRule(Factions, "[]", "[" + string.Join(",", new[]
            {
                Creature("creature.a", "creature.tier.normal", "fac.mob_a", 5),
                Creature("creature.b", "creature.tier.elite", "fac.mob_b", 5),
            }) + "]");

            Assert.Empty(differentLevel);
            Assert.Empty(differentTier);
        }

        [Fact]
        public void AmbiguityRule_ExplicitFriendlyReaction_ExcludesThatCandidate()
        {
            var reactions = "[{\"id\": \"fac.react.1\", \"from\": \"fac.player\", \"to\": \"fac.mob_b\", \"reaction\": \"friendly\"}]";

            var issues = RunRule(Factions, reactions, "[" + string.Join(",", new[]
            {
                Creature("creature.a", "creature.tier.normal", "fac.mob_a", 5),
                Creature("creature.b", "creature.tier.normal", "fac.mob_b", 5),
            }) + "]");

            Assert.Empty(issues);
        }

        [Fact]
        public void AmbiguityRule_SamePlayerFactionCandidate_IsNeverHostile()
        {
            var issues = RunRule(Factions, "[]", "[" + string.Join(",", new[]
            {
                Creature("creature.a", "creature.tier.normal", "fac.player", 5),
                Creature("creature.b", "creature.tier.normal", "fac.mob_b", 5),
            }) + "]");

            Assert.Empty(issues);
        }

        [Fact]
        public void AmbiguityRule_PlayerDefaultReactionNotHostile_NoCandidateCountsAsHostile()
        {
            var factions = "[{\"id\": \"fac.player\", \"default_reaction\": \"neutral\"}, {\"id\": \"fac.mob_a\"}, {\"id\": \"fac.mob_b\"}]";

            var issues = RunRule(factions, "[]", "[" + string.Join(",", new[]
            {
                Creature("creature.a", "creature.tier.normal", "fac.mob_a", 5),
                Creature("creature.b", "creature.tier.normal", "fac.mob_b", 5),
            }) + "]");

            Assert.Empty(issues);
        }

        [Fact]
        public void AmbiguityRule_NoFactionSystemOrNoPlayerFaction_ProducesNothing()
        {
            var creatures = "[" + string.Join(",", new[]
            {
                Creature("creature.a", "creature.tier.normal", "fac.mob_a", 5),
                Creature("creature.b", "creature.tier.normal", "fac.mob_b", 5),
            }) + "]";

            Assert.Empty(RunRule("[]", "[]", creatures));
            Assert.Empty(RunRule("[{\"id\": \"fac.mob_a\", \"default_reaction\": \"hostile\"}]", "[]", creatures));
        }

        [Fact]
        public void AmbiguityRule_ThreeCandidates_ReportsOneWarningPerCandidate_AndOrderIsDeterministic()
        {
            var factions = Factions;
            var creatures = "[" + string.Join(",", new[]
            {
                Creature("creature.c", "creature.tier.normal", "fac.mob_a", 5),
                Creature("creature.a", "creature.tier.normal", "fac.mob_b", 5),
                Creature("creature.b", "creature.tier.normal", "fac.mob_a", 5),
            }) + "]";

            var first = RunRule(factions, "[]", creatures).Select(i => i.RecordKey).ToList();
            var second = RunRule(factions, "[]", creatures).Select(i => i.RecordKey).ToList();

            Assert.Equal(3, first.Count);
            Assert.Equal(first, second);
        }
    }
}
