using System.Collections.Generic;
using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Xunit;

namespace Tests.Foundation.DisplayInfo
{
    /// <summary><see cref="AnimSetEventsShapeRule"/> 用例（ADR-0017 决策 c）。</summary>
    public class AnimSetEventsShapeRuleTests
    {
        private static IReadOnlyList<ValidationIssue> ShapeIssues(ValidationReport report) =>
            report.Issues.Where(i => i.Check == "anim_set_events_shape").ToArray();

        [Fact]
        public void ValidRow_NoIssues()
        {
            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.anim_set"] = "[" + DisplayInfoTestSupport.StoneGolemAnimSetRow + "]" },
                new IValidationRule[] { new AnimSetEventsShapeRule() });

            Assert.False(report.IsBlocking);
            Assert.Empty(ShapeIssues(report));
        }

        [Fact]
        public void RowWithoutClips_NoIssues()
        {
            const string row = @"{ ""id"": ""display.anim_set.empty"", ""clips"": {} }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.anim_set"] = "[" + row + "]" },
                new IValidationRule[] { new AnimSetEventsShapeRule() });

            Assert.False(report.IsBlocking);
            Assert.Empty(ShapeIssues(report));
        }

        [Fact]
        public void EventMissingName_ReportsError()
        {
            const string row = @"
            {
              ""id"": ""display.anim_set.bad"",
              ""clips"": { ""attack"": {""resource_ref"": ""anim.bad.attack"", ""events"": [{""time_pct"": 0.5}]} }
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.anim_set"] = "[" + row + "]" },
                new IValidationRule[] { new AnimSetEventsShapeRule() });

            var issues = ShapeIssues(report);
            Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error && i.RecordKey == "display.anim_set.bad");
            Assert.True(report.IsBlocking);
        }

        [Fact]
        public void EventMissingTimePct_ReportsError()
        {
            const string row = @"
            {
              ""id"": ""display.anim_set.bad2"",
              ""clips"": { ""attack"": {""resource_ref"": ""anim.bad2.attack"", ""events"": [{""name"": ""hit_frame""}]} }
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.anim_set"] = "[" + row + "]" },
                new IValidationRule[] { new AnimSetEventsShapeRule() });

            var issues = ShapeIssues(report);
            Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error && i.RecordKey == "display.anim_set.bad2");
        }

        [Fact]
        public void EventsNotArray_ReportsError()
        {
            const string row = @"
            {
              ""id"": ""display.anim_set.bad3"",
              ""clips"": { ""attack"": {""resource_ref"": ""anim.bad3.attack"", ""events"": ""not_an_array""} }
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.anim_set"] = "[" + row + "]" },
                new IValidationRule[] { new AnimSetEventsShapeRule() });

            var issues = ShapeIssues(report);
            Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error && i.RecordKey == "display.anim_set.bad3");
        }

        [Fact]
        public void TimePctOutOfRange_ReportsWarningOnly()
        {
            const string row = @"
            {
              ""id"": ""display.anim_set.warn"",
              ""clips"": { ""attack"": {""resource_ref"": ""anim.warn.attack"", ""events"": [{""name"": ""hit_frame"", ""time_pct"": 1.5}]} }
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.anim_set"] = "[" + row + "]" },
                new IValidationRule[] { new AnimSetEventsShapeRule() });

            var issues = ShapeIssues(report);
            Assert.Single(issues);
            Assert.Equal(ValidationSeverity.Warning, issues[0].Severity);
            Assert.False(report.IsBlocking);
        }

        [Fact]
        public void ClipNotObject_ReportsError()
        {
            const string row = @"
            {
              ""id"": ""display.anim_set.bad4"",
              ""clips"": { ""attack"": ""not_an_object"" }
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.anim_set"] = "[" + row + "]" },
                new IValidationRule[] { new AnimSetEventsShapeRule() });

            var issues = ShapeIssues(report);
            Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error && i.RecordKey == "display.anim_set.bad4");
        }
    }
}
