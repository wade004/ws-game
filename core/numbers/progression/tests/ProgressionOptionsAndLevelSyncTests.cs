using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.Progression;
using Xunit;

namespace Tests.Numbers.Progression
{
    /// <summary>
    /// <see cref="ProgressionOptions"/> 各字段、<see cref="LevelSync"/> 委托的三个触发时机、
    /// <see cref="InMemoryProgressionDiagnostics"/> 与 <see cref="ProgLevelCurveValidationRule"/> 元数据的直接用例
    /// （T-L8 numbers 半 / T-L3 numbers 半 / T-L4，2026-10-01 测试覆盖第四批）。
    /// 此前 <c>ProgressionOptions</c> 除 <c>MaxLevel</c>/<c>ExtraXpMultiplierProvider</c> 外只经 gameplay listener
    /// 间接触及，<c>LevelSync</c> 没有任何直接用例。
    /// </summary>
    public sealed class ProgressionOptionsAndLevelSyncTests
    {
        private const int CurveMaxLevel = 5;
        private const int XpPerLevel = 100;
        private static readonly Id CurveId = new Id("prog.curve.cov_l8");
        private static readonly Id Source = new Id("prog.xp.cov_l8");

        private static string CurveRows()
        {
            var entries = new List<string>();
            for (var level = 1; level <= CurveMaxLevel; level++)
            {
                var xp = level == CurveMaxLevel ? 0 : XpPerLevel;
                entries.Add("{\"level\":" + level + ",\"xp_to_next\":" + xp + ",\"growth\":{}}");
            }

            return "[{\"id\":\"" + CurveId.Value + "\",\"max_level\":" + CurveMaxLevel + ",\"entries\":[" + string.Join(",", entries) + "]}]";
        }

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(new EventDefinition[0]), new EventBusOptions { StrictCatalog = false });

        private static DataRegistry NewRegistry(IEventBus bus)
        {
            var source = new InMemoryDataSource()
                .Add("prog.level_curve",
                    "{\"table\":\"prog.level_curve\",\"schema_version\":1,\"rows\":" + CurveRows() + "}");
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(ProgSchemas.LevelCurve);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return registry;
        }

        private sealed class Harness
        {
            public readonly IEventBus Bus = NewBus();
            public readonly List<(Id Unit, int Level)> Syncs = new List<(Id, int)>();
            public readonly List<string> Order = new List<string>();
            public ProgressionHost Host = null!;

            public Harness(ProgressionOptions? options = null, bool withSync = true)
            {
                var registry = NewRegistry(Bus);
                Bus.Subscribe<LevelUpEvent>(ProgressionEventKeys.LevelUp, e => Order.Add("levelup:" + e.NewLevel));
                Host = new ProgressionHost(
                    registry, Bus, (u, s, o, v, src) => { }, (u, src) => { }, options,
                    levelSync: withSync ? (LevelSync)((u, l) => { Syncs.Add((u, l)); Order.Add("sync:" + l); }) : null);
            }
        }

        // -----------------------------------------------------------------
        // ProgressionOptions 缺省与字段
        // -----------------------------------------------------------------

        [Fact]
        public void Options_Defaults_AreTheDocumentedValues()
        {
            var options = new ProgressionOptions();

            Assert.Equal(0, options.MaxLevel);
            Assert.True(options.RefillOnLevelUp);
            Assert.Equal("prog.explore", options.DefaultOnceKeyPrefix);
            Assert.Null(options.ExtraXpMultiplierProvider);
            Assert.Null(options.KillXpSourceId);
            Assert.Null(options.DiscoveryXpSourceId);
            Assert.Null(options.QuestXpSourceId);
        }

        [Fact]
        public void Options_EachField_IsIndependentlySettable()
        {
            var kill = new Id("prog.xp.cov_kill");
            var discovery = new Id("prog.xp.cov_discovery");
            var quest = new Id("prog.xp.cov_quest");
            ProgressionXpMultiplierProvider provider = (u, s, t) => 2.5;

            var options = new ProgressionOptions
            {
                MaxLevel = 7,
                RefillOnLevelUp = false,
                DefaultOnceKeyPrefix = "prog.custom",
                ExtraXpMultiplierProvider = provider,
                KillXpSourceId = kill,
                DiscoveryXpSourceId = discovery,
                QuestXpSourceId = quest,
            };

            Assert.Equal(7, options.MaxLevel);
            Assert.False(options.RefillOnLevelUp);
            Assert.Equal("prog.custom", options.DefaultOnceKeyPrefix);
            Assert.Same(provider, options.ExtraXpMultiplierProvider);
            Assert.Equal(kill, options.KillXpSourceId);
            Assert.Equal(discovery, options.DiscoveryXpSourceId);
            Assert.Equal(quest, options.QuestXpSourceId);
        }

        [Fact]
        public void Options_SettingOneField_DoesNotDisturbTheOthers()
        {
            var options = new ProgressionOptions { KillXpSourceId = new Id("prog.xp.cov_kill") };

            Assert.Null(options.DiscoveryXpSourceId);
            Assert.Null(options.QuestXpSourceId);
            Assert.Equal(0, options.MaxLevel);
            Assert.True(options.RefillOnLevelUp);
        }

        [Fact]
        public void Host_NullOptions_BehavesLikeDefaultOptions_NoCap()
        {
            var nullOptions = new Harness(options: null);
            var defaults = new Harness(new ProgressionOptions());
            var unit = new Id("unit.cov_l8_a");
            nullOptions.Host.RegisterUnit(unit, CurveId);
            defaults.Host.RegisterUnit(unit, CurveId);

            nullOptions.Host.AddXp(unit, Source, XpPerLevel * CurveMaxLevel);
            defaults.Host.AddXp(unit, Source, XpPerLevel * CurveMaxLevel);

            Assert.Equal(CurveMaxLevel, nullOptions.Host.GetLevel(unit));
            Assert.Equal(nullOptions.Host.GetLevel(unit), defaults.Host.GetLevel(unit));
        }

        [Fact]
        public void Options_MaxLevelAboveCurveMax_CannotWidenBeyondCurve()
        {
            var harness = new Harness(new ProgressionOptions { MaxLevel = CurveMaxLevel + 10 });
            var unit = new Id("unit.cov_l8_b");
            harness.Host.RegisterUnit(unit, CurveId);

            harness.Host.AddXp(unit, Source, XpPerLevel * (CurveMaxLevel + 10));

            Assert.Equal(CurveMaxLevel, harness.Host.GetLevel(unit));
            Assert.Equal(0, harness.Host.GetXpToNext(unit));
        }

        [Fact]
        public void Options_MaxLevelBelowCurveMax_StopsLevelingThere_AndDropsSurplusXp()
        {
            const int cap = 3;
            var harness = new Harness(new ProgressionOptions { MaxLevel = cap });
            var unit = new Id("unit.cov_l8_c");
            harness.Host.RegisterUnit(unit, CurveId);

            harness.Host.AddXp(unit, Source, XpPerLevel * CurveMaxLevel);

            Assert.Equal(cap, harness.Host.GetLevel(unit));
            Assert.Equal(0, harness.Host.GetXp(unit));
            Assert.Equal(0, harness.Host.GetXpToNext(unit));
        }

        [Fact]
        public void Options_MaxLevel_AppliesToRestoreState_UnitBelowCapStillValidatedAgainstThreshold()
        {
            const int cap = 3;
            var harness = new Harness(new ProgressionOptions { MaxLevel = cap });
            var unit = new Id("unit.cov_l8_d");

            // 低于有效满级的等级：经验达到本级升级门槛属存档损坏。
            Assert.Throws<System.FormatException>(() => harness.Host.RestoreState(unit, CurveId, 2, XpPerLevel));
            // 等于有效满级（曲线自身满级是 CurveMaxLevel）：门槛不再校验，原样恢复。
            harness.Host.RestoreState(unit, CurveId, cap, XpPerLevel * 5);
            Assert.Equal(cap, harness.Host.GetLevel(unit));
        }

        // -----------------------------------------------------------------
        // LevelSync 三个触发时机
        // -----------------------------------------------------------------

        [Fact]
        public void LevelSync_RegisterUnit_ReportsStartLevel()
        {
            var harness = new Harness();
            var unit = new Id("unit.cov_l4_a");

            harness.Host.RegisterUnit(unit, CurveId, startLevel: 3);

            Assert.Equal(new[] { (unit, 3) }, harness.Syncs);
        }

        [Fact]
        public void LevelSync_AddXp_WithoutLevelUp_DoesNotFire()
        {
            var harness = new Harness();
            var unit = new Id("unit.cov_l4_b");
            harness.Host.RegisterUnit(unit, CurveId);
            harness.Syncs.Clear();

            harness.Host.AddXp(unit, Source, XpPerLevel - 1);

            Assert.Empty(harness.Syncs);
        }

        [Fact]
        public void LevelSync_AddXp_MultiLevelJump_FiresOnceWithFinalLevel_AfterAllLevelUpEvents()
        {
            var harness = new Harness();
            var unit = new Id("unit.cov_l4_c");
            harness.Host.RegisterUnit(unit, CurveId);
            harness.Syncs.Clear();
            harness.Order.Clear();
            const int jumps = 3;

            harness.Host.AddXp(unit, Source, XpPerLevel * jumps);

            var finalLevel = 1 + jumps;
            Assert.Equal(new[] { (unit, finalLevel) }, harness.Syncs);
            // 最终等级的同步写回发生在所有逐级 LevelUp 事件之后，且只写一次。
            Assert.Equal(jumps + 1, harness.Order.Count);
            Assert.Equal("sync:" + finalLevel, harness.Order[harness.Order.Count - 1]);
            for (var i = 0; i < jumps; i++)
            {
                Assert.Equal("levelup:" + (2 + i), harness.Order[i]);
            }
        }

        [Fact]
        public void LevelSync_AddXp_AtMaxLevel_DoesNotFire()
        {
            var harness = new Harness();
            var unit = new Id("unit.cov_l4_d");
            harness.Host.RegisterUnit(unit, CurveId, startLevel: CurveMaxLevel);
            harness.Syncs.Clear();

            harness.Host.AddXp(unit, Source, XpPerLevel);

            Assert.Empty(harness.Syncs);
            Assert.Contains(Assert.IsType<InMemoryProgressionDiagnostics>(harness.Host.Diagnostics).Warnings, w => w.Contains("满级"));
        }

        [Fact]
        public void LevelSync_RestoreState_ReportsRestoredLevel()
        {
            var harness = new Harness();
            var unit = new Id("unit.cov_l4_e");

            harness.Host.RestoreState(unit, CurveId, 4, 10);

            Assert.Equal(new[] { (unit, 4) }, harness.Syncs);
        }

        [Fact]
        public void LevelSync_RestoreStateRejected_DoesNotFire()
        {
            var harness = new Harness();
            var unit = new Id("unit.cov_l4_f");

            Assert.Throws<System.ArgumentException>(() => harness.Host.RestoreState(unit, CurveId, CurveMaxLevel + 1, 0));
            Assert.Throws<System.FormatException>(() => harness.Host.RestoreState(unit, CurveId, 2, XpPerLevel));

            Assert.Empty(harness.Syncs);
        }

        [Fact]
        public void LevelSync_NotInjected_HostWorksAsBefore()
        {
            var harness = new Harness(withSync: false);
            var unit = new Id("unit.cov_l4_g");

            harness.Host.RegisterUnit(unit, CurveId);
            harness.Host.AddXp(unit, Source, XpPerLevel);

            Assert.Equal(2, harness.Host.GetLevel(unit));
            Assert.Empty(harness.Syncs);
        }

        // -----------------------------------------------------------------
        // InMemoryProgressionDiagnostics（T-L3 numbers 半）
        // -----------------------------------------------------------------

        [Fact]
        public void InMemoryProgressionDiagnostics_Warn_AccumulatesInOrder_AndKeepsDuplicates()
        {
            var diagnostics = new InMemoryProgressionDiagnostics();
            Assert.Empty(diagnostics.Warnings);

            diagnostics.Warn("a");
            diagnostics.Warn("b");
            diagnostics.Warn("a");

            Assert.Equal(new[] { "a", "b", "a" }, diagnostics.Warnings);
        }

        [Fact]
        public void InMemoryProgressionDiagnostics_ReturnsLiveView()
        {
            var diagnostics = new InMemoryProgressionDiagnostics();
            var view = diagnostics.Warnings;

            diagnostics.Warn("late");

            Assert.Single(view);
        }

        [Fact]
        public void Host_DefaultDiagnostics_IsInMemory_AndInjectedDiagnosticsReceivesWarnings()
        {
            var defaultHarness = new Harness();
            Assert.IsType<InMemoryProgressionDiagnostics>(defaultHarness.Host.Diagnostics);

            var bus = NewBus();
            var injected = new InMemoryProgressionDiagnostics();
            var host = new ProgressionHost(NewRegistry(bus), bus, (u, s, o, v, src) => { }, (u, src) => { }, injected);
            var unit = new Id("unit.cov_l3");
            host.RegisterUnit(unit, CurveId, CurveMaxLevel);

            host.AddXp(unit, Source, 1);

            Assert.Single(injected.Warnings);
        }

        // -----------------------------------------------------------------
        // ProgLevelCurveValidationRule 元数据
        // -----------------------------------------------------------------

        [Fact]
        public void ProgLevelCurveRule_Metadata_IsTypeNameAndBlockingGrade()
        {
            IValidationRule rule = new ProgLevelCurveValidationRule();

            Assert.Equal(nameof(ProgLevelCurveValidationRule), rule.RuleId);
            Assert.Equal(ValidationSeverity.Error, rule.DefaultSeverity);
            Assert.False(rule.NonEscalatable);
        }
    }
}
