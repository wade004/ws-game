using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using Core.Sim;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// 仿真工具类型的边界与异常路径（T-M5 余项 / T-M15 sim 半，2026-10-01 测试覆盖第四批）：
    /// <see cref="AnchorCreatureLevelScaler"/>、<see cref="ExpectedStatCalculator"/>、<see cref="StandardPlayerBuilder"/>、
    /// <see cref="AnchorTableSkillBudgetAnchorProvider"/> 的守卫与防御分支，以及报告/基线类型的空参守卫。
    /// 判断记录：<c>StandardPlayerBuilder.FindLatestInstance</c> 的"找不回背包实例"分支（<c>AddItem</c> 之后按
    /// 身份匹配落空）在现有宿主上不可达——<c>InventoryHost.AddItem</c> 对新槽位恒成功且原样记录模板/品质/词缀，
    /// 要构造它必须改生产代码或注入返回不一致数据的宿主替身，属防御性兜底，本批不为它改生产代码。
    /// </summary>
    public sealed class SimBoundaryTests
    {
        private static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
            });
            return new EventBus(catalog);
        }

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static string AnchorRow(int level, double hp, double dps, double ttk, double ttd, double itemLevel) =>
            "{\"id\": \"sim.anchor.l" + level + "\", \"level\": " + level + ", \"hp\": " + Num(hp) + ", \"dps\": " + Num(dps) +
            ", \"ttk_seconds\": " + Num(ttk) + ", \"ttd_seconds\": " + Num(ttd) + ", \"expected_item_level\": " + Num(itemLevel) +
            ", \"level_duration_seconds\": 300, \"kill_interval_seconds\": 15, \"quest_share\": 0.3}";

        private static string Num(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>只含 <c>sim.anchor</c> 的最小注册表上构造 <see cref="AnchorTable"/>（rowsJson 为 <c>[]</c> 即空表）。</summary>
        private static AnchorTable BuildAnchors(string rowsJson)
        {
            var source = new InMemoryDataSource().Add("sim.anchor", Envelope("sim.anchor", rowsJson));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(SimSchemas.Anchor); // 不登记校验规则：用例要能构造"单行/dps=0"这类本应被规则关注的表。
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return new AnchorTable(registry);
        }

        // -----------------------------------------------------------------
        // AnchorCreatureLevelScaler
        // -----------------------------------------------------------------

        private static (Core.Sim.HeadlessWorld World, CreatureTemplateAccess Template) EmbeddedWorld()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 5);
            return (world, new CreatureTemplateAccess(world.Gameplay.Carriers.Creatures.Get(SimTestWorldFactory.EmbeddedCreatureWolfL1)));
        }

        /// <summary>只为让 <c>CreatureTemplate</c> 这个类型名不必在每个用例里重复书写。</summary>
        private readonly struct CreatureTemplateAccess
        {
            public Core.Carriers.Creature.CreatureTemplate Value { get; }

            public CreatureTemplateAccess(Core.Carriers.Creature.CreatureTemplate value)
            {
                Value = value;
            }
        }

        [Fact]
        public void Scaler_NullConstructorArguments_Throw()
        {
            var (world, _) = EmbeddedWorld();

            Assert.Equal("registry", Assert.Throws<ArgumentNullException>(() => new AnchorCreatureLevelScaler(null!, world.AnchorTable!)).ParamName);
            Assert.Equal("anchors", Assert.Throws<ArgumentNullException>(() => new AnchorCreatureLevelScaler(world.Registry, null!)).ParamName);
        }

        [Fact]
        public void Scaler_NullBaseStats_Throws()
        {
            var (world, template) = EmbeddedWorld();
            var scaler = new AnchorCreatureLevelScaler(world.Registry, world.AnchorTable!);

            Assert.Equal("baseStats", Assert.Throws<ArgumentNullException>(() => scaler.ScaleBaseStats(template.Value, 1, 5, null!)).ParamName);
        }

        [Fact]
        public void Scaler_EmptyAnchorTable_ThrowsInvalidOperationOnFirstUse()
        {
            var (world, template) = EmbeddedWorld();
            var emptyAnchors = BuildAnchors("[]");
            Assert.Equal(0, emptyAnchors.MaxLevel);
            var scaler = new AnchorCreatureLevelScaler(world.Registry, emptyAnchors); // 构造不抛，首次换算才抛。

            var ex = Assert.Throws<InvalidOperationException>(() =>
                scaler.ScaleBaseStats(template.Value, 1, 5, new Dictionary<Id, double> { [new Id("stat.strength")] = 10 }));

            Assert.Contains("MaxLevel=0", ex.Message);
        }

        [Fact]
        public void Scaler_LevelsOutsideTheTable_AreClampedToItsEnds()
        {
            var (world, template) = EmbeddedWorld();
            var anchors = world.AnchorTable!;
            var scaler = new AnchorCreatureLevelScaler(world.Registry, anchors);
            var stats = new Dictionary<Id, double> { [new Id("stat.strength")] = 10.0 };

            var atZero = scaler.ScaleBaseStats(template.Value, 1, 0, stats);
            var atOne = scaler.ScaleBaseStats(template.Value, 1, 1, stats);
            var beyondMax = scaler.ScaleBaseStats(template.Value, 1, anchors.MaxLevel + 50, stats);
            var atMax = scaler.ScaleBaseStats(template.Value, 1, anchors.MaxLevel, stats);

            Assert.Equal(atOne[new Id("stat.strength")], atZero[new Id("stat.strength")], 12);
            Assert.Equal(atMax[new Id("stat.strength")], beyondMax[new Id("stat.strength")], 12);
        }

        [Fact]
        public void Scaler_NonHealthStats_ScaleByTheDamageRatio_ComputedFromTheAnchorRows()
        {
            var (world, template) = EmbeddedWorld();
            var anchors = world.AnchorTable!;
            var scaler = new AnchorCreatureLevelScaler(world.Registry, anchors);
            const int sourceLevel = 1;
            const int targetLevel = 10;
            var source = anchors.Get(sourceLevel);
            var target = anchors.Get(targetLevel);
            var damageRatio = (target.Hp / target.TtdSeconds) / (source.Hp / source.TtdSeconds);
            var stat = new Id("stat.strength");

            var scaled = scaler.ScaleBaseStats(template.Value, sourceLevel, targetLevel, new Dictionary<Id, double> { [stat] = 12.0 });

            Assert.Equal(12.0 * damageRatio, scaled[stat], 9);
        }

        [Fact]
        public void Scaler_SameLevel_IsIdentity()
        {
            var (world, template) = EmbeddedWorld();
            var scaler = new AnchorCreatureLevelScaler(world.Registry, world.AnchorTable!);
            var stats = new Dictionary<Id, double> { [new Id("stat.strength")] = 12.0, [new Id("stat.stamina")] = 150.0 };

            var scaled = scaler.ScaleBaseStats(template.Value, 7, 7, stats);

            Assert.Equal(12.0, scaled[new Id("stat.strength")], 9);
            Assert.Equal(150.0, scaled[new Id("stat.stamina")], 9);
        }

        [Fact]
        public void Scaler_NonPositiveSourceAnchor_DoesNotScale_InsteadOfProducingNanOrInfinity()
        {
            // 源等级锚点 dps=0：healthRatio 的分母为 0，防御性兜底返回 1.0（保留原值），不得出现 NaN/Infinity。
            var (world, template) = EmbeddedWorld();
            var degenerate = BuildAnchors("[" + AnchorRow(1, 100, 0, 5, 8, 1) + "," + AnchorRow(2, 140, 14, 5, 8, 2) + "]");
            var scaler = new AnchorCreatureLevelScaler(world.Registry, degenerate);
            var health = new Id("stat.stamina");
            var other = new Id("stat.strength");

            var scaled = scaler.ScaleBaseStats(template.Value, 1, 2,
                new Dictionary<Id, double> { [health] = 150.0, [other] = 12.0 });

            Assert.False(double.IsNaN(scaled[health]) || double.IsInfinity(scaled[health]));
            Assert.Equal(150.0, scaled[health], 9);
            // 非血量槽位走另一条比值（hp/ttd），源 hp/ttd 为正，正常缩放。
            Assert.Equal(12.0 * ((140.0 / 8.0) / (100.0 / 8.0)), scaled[other], 9);
        }

        // -----------------------------------------------------------------
        // ExpectedStatCalculator
        // -----------------------------------------------------------------

        private static DataRegistry BuildStatRegistry(string archClassRows, string statDefinitionRows)
        {
            var source = new InMemoryDataSource()
                .Add("arch.class", Envelope("arch.class", archClassRows))
                .Add("stat.definition", Envelope("stat.definition", statDefinitionRows))
                .Add("sim.anchor", Envelope("sim.anchor", "[" + AnchorRow(1, 100, 10, 5, 8, 1) + "," + AnchorRow(2, 140, 14, 5, 8, 2) + "]"));
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(SimSchemas.Anchor);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return registry;
        }

        private static ExpectedStatCalculator NewCalculator(DataRegistry registry, string classId = "arch.class.cov") =>
            new ExpectedStatCalculator(registry, new AnchorTable(registry), new Id(classId), new Id("item.quality.cov"), new BudgetSolver());

        private const string CovClass = "[{\"id\": \"arch.class.cov\", \"base_stats\": {\"stat.base\": 12}}]";

        [Fact]
        public void Calculator_NullConstructorArguments_Throw()
        {
            var registry = BuildStatRegistry(CovClass, "[{\"id\": \"stat.base\", \"category\": \"primary\", \"default_base\": 0}]");
            var anchors = new AnchorTable(registry);
            var classId = new Id("arch.class.cov");
            var qualityId = new Id("item.quality.cov");

            Assert.Equal("view", Assert.Throws<ArgumentNullException>(() => new ExpectedStatCalculator(null!, anchors, classId, qualityId, new BudgetSolver())).ParamName);
            Assert.Equal("anchors", Assert.Throws<ArgumentNullException>(() => new ExpectedStatCalculator(registry, null!, classId, qualityId, new BudgetSolver())).ParamName);
            Assert.Equal("budgetSolver", Assert.Throws<ArgumentNullException>(() => new ExpectedStatCalculator(registry, anchors, classId, qualityId, null!)).ParamName);
        }

        [Fact]
        public void Calculator_UnregisteredClass_ThrowsArgumentExceptionNamingClassId()
        {
            var registry = BuildStatRegistry(CovClass, "[{\"id\": \"stat.base\", \"category\": \"primary\", \"default_base\": 0}]");

            var ex = Assert.Throws<ArgumentException>(() => NewCalculator(registry, "arch.class.nope"));

            Assert.Equal("classId", ex.ParamName);
            Assert.Contains("arch.class.nope", ex.Message);
        }

        [Fact]
        public void Calculator_DerivationCycle_ThrowsInvalidOperationNamingTheStat()
        {
            var statDefs = "[" +
                "{\"id\": \"stat.a\", \"category\": \"derived\", \"derived_from\": [{\"stat\": \"stat.b\", \"coefficient\": 1}]}," +
                "{\"id\": \"stat.b\", \"category\": \"derived\", \"derived_from\": [{\"stat\": \"stat.a\", \"coefficient\": 1}]}" +
                "]";
            var calculator = NewCalculator(BuildStatRegistry(CovClass, statDefs));

            var ex = Assert.Throws<InvalidOperationException>(() => calculator.Compute(1));

            Assert.Contains("派生环", ex.Message);
            Assert.True(ex.Message.Contains("stat.a") || ex.Message.Contains("stat.b"));
        }

        [Fact]
        public void Calculator_SelfDerivation_IsACycleToo()
        {
            var statDefs = "[{\"id\": \"stat.self\", \"category\": \"derived\", \"derived_from\": [{\"stat\": \"stat.self\", \"coefficient\": 2}]}]";
            var calculator = NewCalculator(BuildStatRegistry(CovClass, statDefs));

            Assert.Throws<InvalidOperationException>(() => calculator.Compute(1));
        }

        [Fact]
        public void Calculator_DerivedStat_UsesCoefficientTimesSourceValue_AndClassOverrideWins()
        {
            var statDefs = "[" +
                "{\"id\": \"stat.base\", \"category\": \"primary\", \"default_base\": 0}," +
                "{\"id\": \"stat.power\", \"category\": \"derived\", \"derived_from\": [{\"stat\": \"stat.base\", \"coefficient\": 2}]}" +
                "]";
            var plain = NewCalculator(BuildStatRegistry(CovClass, statDefs));
            var overridden = NewCalculator(BuildStatRegistry(
                "[{\"id\": \"arch.class.cov\", \"base_stats\": {\"stat.base\": 12}, " +
                "\"derivation_overrides\": [{\"stat\": \"stat.power\", \"source\": \"stat.base\", \"coefficient\": 5}]}]",
                statDefs));

            Assert.Equal(12.0 * 2.0, plain.Compute(1)[new Id("stat.power")], 9);
            Assert.Equal(12.0 * 5.0, overridden.Compute(1)[new Id("stat.power")], 9);
        }

        [Fact]
        public void Calculator_ClampBoundsApplyToTheResolvedValue()
        {
            var statDefs = "[" +
                "{\"id\": \"stat.base\", \"category\": \"primary\", \"default_base\": 0, \"clamp\": {\"min\": 0, \"max\": 7}}" +
                "]";
            var calculator = NewCalculator(BuildStatRegistry(CovClass, statDefs));

            Assert.Equal(7.0, calculator.Compute(1)[new Id("stat.base")], 9);
        }

        [Fact]
        public void Calculator_DerivedFromUndefinedStat_ContributesZero()
        {
            var statDefs = "[{\"id\": \"stat.orphan\", \"category\": \"derived\", \"derived_from\": [{\"stat\": \"stat.never_defined\", \"coefficient\": 9}]}]";
            var calculator = NewCalculator(BuildStatRegistry(CovClass, statDefs));

            Assert.Equal(0.0, calculator.Compute(1)[new Id("stat.orphan")], 9);
        }

        [Fact]
        public void Calculator_LevelOutsideTheAnchorTable_ThrowsArgumentOutOfRange()
        {
            var calculator = NewCalculator(BuildStatRegistry(CovClass, "[{\"id\": \"stat.base\", \"category\": \"primary\", \"default_base\": 0}]"));

            Assert.Throws<ArgumentOutOfRangeException>(() => calculator.Compute(99));
        }

        // -----------------------------------------------------------------
        // StandardPlayerBuilder
        // -----------------------------------------------------------------

        private static readonly Id QualityCommon = new Id("item.quality.sim_common");

        [Fact]
        public void StandardPlayerBuilder_NullWorld_Throws()
        {
            Assert.Equal("world", Assert.Throws<ArgumentNullException>(
                () => StandardPlayerBuilder.Build(null!, SimTestWorldFactory.EmbeddedClassId, 1, QualityCommon)).ParamName);
        }

        [Fact]
        public void StandardPlayerBuilder_WorldWithoutAnchorTable_ThrowsInvalidOperation()
        {
            // 把嵌入式 sim.anchor 改成零行：装配根因此不构造 AnchorTable（world.AnchorTable == null）。
            var sources = SimTestWorldFactory.BuildEmbeddedDataSourcesWithEdit((path, text) =>
                path.EndsWith("sim/sim.anchor.json", StringComparison.Ordinal)
                    ? Envelope("sim.anchor", "[]")
                    : text);
            var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = sources,
                MapId = SimTestWorldFactory.EmbeddedMapId,
                PlayerId = SimTestWorldFactory.PlayerId,
                PlayerFactionId = SimTestWorldFactory.FactionPlayer,
                PlayerClassId = SimTestWorldFactory.EmbeddedClassId,
                GameId = SimTestWorldFactory.EmbeddedGameId,
            });
            Assert.Null(world.AnchorTable);

            var ex = Assert.Throws<InvalidOperationException>(
                () => StandardPlayerBuilder.Build(world, SimTestWorldFactory.EmbeddedClassId, 1, QualityCommon));

            Assert.Contains("AnchorTable", ex.Message);
        }

        [Fact]
        public void StandardPlayerBuilder_UnregisteredClass_ThrowsArgumentExceptionNamingClassId()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 6);

            var ex = Assert.Throws<ArgumentException>(
                () => StandardPlayerBuilder.Build(world, new Id("arch.class.cov_nope"), 1, QualityCommon));

            Assert.Equal("classId", ex.ParamName);
        }

        [Fact]
        public void StandardPlayerBuilder_UnregisteredRotation_ThrowsInvalidOperationNamingTheRotation()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 7);
            var rotation = new Id("ai.rotation.cov_nope");

            var ex = Assert.Throws<InvalidOperationException>(
                () => StandardPlayerBuilder.Build(world, SimTestWorldFactory.EmbeddedClassId, 1, QualityCommon, rotation));

            Assert.Contains(rotation.Value, ex.Message);
        }

        [Fact]
        public void StandardPlayerBuilder_ExplicitRotation_IsReportedBackOnTheResult()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 8);
            var inferred = StandardPlayerBuilder.Build(world, SimTestWorldFactory.EmbeddedClassId, 1, QualityCommon);

            var world2 = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 8);
            var explicitResult = StandardPlayerBuilder.Build(world2, SimTestWorldFactory.EmbeddedClassId, 1, QualityCommon, inferred.RotationId);

            Assert.Equal("ai.rotation.sim_warrior", inferred.RotationId.Value);
            Assert.Equal(inferred.RotationId, explicitResult.RotationId);
        }

        // -----------------------------------------------------------------
        // AnchorTableSkillBudgetAnchorProvider
        // -----------------------------------------------------------------

        [Fact]
        public void AnchorProvider_NullArguments_Throw()
        {
            Assert.Equal("registry", Assert.Throws<ArgumentNullException>(() => new AnchorTableSkillBudgetAnchorProvider((IDataRegistry)null!)).ParamName);
            Assert.Equal("registryFactory", Assert.Throws<ArgumentNullException>(() => new AnchorTableSkillBudgetAnchorProvider((Func<IDataRegistry>)null!)).ParamName);
            Assert.Equal("sources", Assert.Throws<ArgumentNullException>(() => AnchorTableSkillBudgetAnchorProvider.DataSourcesHaveAnchorRows(null!)).ParamName);
        }

        [Fact]
        public void AnchorProvider_EmptyAnchorTable_ThrowsOnFirstUse_NotOnConstruction()
        {
            var source = new InMemoryDataSource().Add("sim.anchor", Envelope("sim.anchor", "[]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(SimSchemas.Anchor);
            Assert.False(registry.LoadAll().IsBlocking);
            var provider = new AnchorTableSkillBudgetAnchorProvider(registry);

            Assert.Throws<InvalidOperationException>(() => provider.GetAnchorDps(1));
            Assert.Throws<InvalidOperationException>(() => provider.GetExpectedScalingStatValue(new Id("stat.strength"), 1));
        }

        [Fact]
        public void AnchorProvider_NoClassAndNoScenario_CannotResolveTheStandardPlayer()
        {
            var source = new InMemoryDataSource()
                .Add("sim.anchor", Envelope("sim.anchor", "[" + AnchorRow(1, 100, 10, 5, 8, 1) + "]"))
                .Add("sim.scenario", Envelope("sim.scenario", "[]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(SimSchemas.Anchor);
            registry.RegisterSchema(SimSchemas.Scenario);
            Assert.False(registry.LoadAll().IsBlocking);
            var provider = new AnchorTableSkillBudgetAnchorProvider(registry);

            var ex = Assert.Throws<InvalidOperationException>(() => _ = provider.ResolvedStandardPlayer);

            Assert.Contains("职业", ex.Message);
        }

        [Fact]
        public void AnchorProvider_AnchorDps_ClampsLevelIntoTheTable()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 9);
            var anchors = world.AnchorTable!;
            var provider = new AnchorTableSkillBudgetAnchorProvider(world.Registry);

            Assert.Equal(anchors.Get(1).Dps, provider.GetAnchorDps(0));
            Assert.Equal(anchors.Get(1).Dps, provider.GetAnchorDps(-5));
            Assert.Equal(anchors.Get(anchors.MaxLevel).Dps, provider.GetAnchorDps(anchors.MaxLevel + 40));
            Assert.Equal(anchors.Get(5).Dps, provider.GetAnchorDps(5));
        }

        // -----------------------------------------------------------------
        // 报告 / 基线类型的空参守卫
        // -----------------------------------------------------------------

        private static (ArenaReport Report, ScenarioDef Scenario, Core.Sim.HeadlessWorld World) SmallArena()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_arena_matrix")).WithRuns(1);
            // 缩成一个格子：只测空参守卫与产物的基本形状，不需要完整矩阵。
            var report = ArenaSimulation.Run(
                scenario, world.AnchorTable!, SimTestWorldFactory.BuildEmbeddedDataSources());
            return (report, scenario, world);
        }

        [Fact]
        public void SimReport_FromReports_NullArguments_Throw()
        {
            var (report, scenario, world) = SmallArena();

            Assert.Equal("report", Assert.Throws<ArgumentNullException>(() => SimReport.FromArenaReport(null!, scenario, world.Registry, "v")).ParamName);
            Assert.Equal("scenario", Assert.Throws<ArgumentNullException>(() => SimReport.FromArenaReport(report, null!, world.Registry, "v")).ParamName);
            Assert.Equal("registry", Assert.Throws<ArgumentNullException>(() => SimReport.FromArenaReport(report, scenario, null!, "v")).ParamName);
            Assert.Equal("report", Assert.Throws<ArgumentNullException>(() => SimReport.FromGrowthReport(null!, scenario, world.Registry, "v")).ParamName);
            Assert.Equal("report", Assert.Throws<ArgumentNullException>(() => SimReport.FromCoverageReport(null!, scenario, world.Registry, "v")).ParamName);
            Assert.Equal("registry", Assert.Throws<ArgumentNullException>(() => SimReport.ComputeDatasetFingerprint(null!)).ParamName);
        }

        [Fact]
        public void SimStat_NullPath_Throws_AndOptionalFieldsDefaultToNull()
        {
            Assert.Equal("path", Assert.Throws<ArgumentNullException>(() => new SimStat(null!, 1.0)).ParamName);

            var stat = new SimStat("a.b", 2.5);
            Assert.Equal("a.b", stat.Path);
            Assert.Equal(2.5, stat.Value);
            Assert.Null(stat.Anchor);
            Assert.Null(stat.Deviation);
            Assert.Null(stat.Level);

            var full = new SimStat("a.c", 1.0, anchor: 2.0, deviation: 0.5, level: 7);
            Assert.Equal(2.0, full.Anchor);
            Assert.Equal(0.5, full.Deviation);
            Assert.Equal(7, full.Level);
        }

        [Fact]
        public void SimBaseline_NullArguments_Throw()
        {
            var id = new Id("sim.scenario.cov");
            var stats = new Dictionary<string, double>();

            Assert.Equal("datasetFingerprint", Assert.Throws<ArgumentNullException>(() => new SimBaseline(1, id, "arena", null!, "v", 1UL, stats)).ParamName);
            Assert.Equal("generatedWithVersion", Assert.Throws<ArgumentNullException>(() => new SimBaseline(1, id, "arena", "fp", null!, 1UL, stats)).ParamName);
            Assert.Equal("stats", Assert.Throws<ArgumentNullException>(() => new SimBaseline(1, id, "arena", "fp", "v", 1UL, null!)).ParamName);
            Assert.Equal("report", Assert.Throws<ArgumentNullException>(() => SimBaseline.FromReport(null!)).ParamName);
        }

        [Fact]
        public void BaselineComparer_NullArguments_Throw()
        {
            var (report, scenario, world) = SmallArena();
            var simReport = SimReport.FromArenaReport(report, scenario, world.Registry, "v");
            var baseline = SimBaseline.FromReport(simReport);

            Assert.Equal("current", Assert.Throws<ArgumentNullException>(() => BaselineComparer.Compare(null!, baseline)).ParamName);
            Assert.Equal("baseline", Assert.Throws<ArgumentNullException>(() => BaselineComparer.Compare(simReport, null!)).ParamName);
        }
    }
}
