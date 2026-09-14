using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.Progression;
using Xunit;

namespace Tests.Numbers.Progression
{
    /// <summary>
    /// 分阶段落地计划 T-N0-5 验收（<c>prog.level_curve</c> 侧，拍板 3）：<c>xp_to_next</c> 非单调报 Error
    /// （检查名 <c>level_curve_xp_monotonic</c>）；末级条目（约定 0）不参与比较；相等允许；既有
    /// <c>level_curve_entries</c>/<c>level_curve_continuity</c> 行为不变；表仍是逐级密集枚举（不迁移）。
    /// </summary>
    public sealed class ProgLevelCurveXpMonotonicTests
    {
        private static IEventBus MakeBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        private static ValidationReport Load(string entries, int maxLevel)
        {
            var json = "{\"table\": \"prog.level_curve\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"prog.curve.t\", \"max_level\": " + maxLevel + ", \"entries\": " + entries + "}]}";
            var source = new InMemoryDataSource().Add("prog.level_curve", json);
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(ProgSchemas.LevelCurve);
            registry.RegisterValidationRule(new ProgLevelCurveValidationRule());
            return registry.LoadAll();
        }

        [Fact]
        public void XpToNext_Decreasing_ReportsError()
        {
            var report = Load("[{\"level\": 1, \"xp_to_next\": 100}, {\"level\": 2, \"xp_to_next\": 300}, {\"level\": 3, \"xp_to_next\": 200}, {\"level\": 4, \"xp_to_next\": 0}]", 4);

            Assert.True(report.IsBlocking);
            var issue = Assert.Single(report.Issues, i => i.Check == "level_curve_xp_monotonic");
            Assert.Equal(ValidationSeverity.Error, issue.Severity);
            Assert.Equal("prog.curve.t", issue.RecordKey);
            Assert.Equal("entries[2].xp_to_next", issue.Field);
        }

        [Fact]
        public void XpToNext_NonDecreasing_WithZeroAtMaxLevel_NoIssue()
        {
            var report = Load("[{\"level\": 1, \"xp_to_next\": 100}, {\"level\": 2, \"xp_to_next\": 100}, {\"level\": 3, \"xp_to_next\": 250}, {\"level\": 4, \"xp_to_next\": 0}]", 4);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.DoesNotContain(report.Issues, i => i.Check == "level_curve_xp_monotonic");
        }

        [Fact]
        public void SingleAndTwoLevelCurves_NeverCompareAgainstMaxLevelSentinel()
        {
            Assert.False(Load("[{\"level\": 1, \"xp_to_next\": 0}]", 1).IsBlocking);
            Assert.False(Load("[{\"level\": 1, \"xp_to_next\": 500}, {\"level\": 2, \"xp_to_next\": 0}]", 2).IsBlocking);
        }

        [Fact]
        public void ExistingChecks_Unchanged()
        {
            var countMismatch = Load("[{\"level\": 1, \"xp_to_next\": 100}]", 2);
            Assert.Contains(countMismatch.Issues, i => i.Check == "level_curve_entries");

            var discontinuous = Load("[{\"level\": 1, \"xp_to_next\": 100}, {\"level\": 3, \"xp_to_next\": 200}]", 2);
            Assert.Contains(discontinuous.Issues, i => i.Check == "level_curve_continuity");
        }
    }
}
