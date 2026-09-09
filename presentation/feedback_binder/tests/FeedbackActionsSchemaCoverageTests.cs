using System.Linq;
using Core.Foundation.DataRegistry;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Schema;
using Xunit;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="FeedbackSchemas.ActionsItemSchema"/> 的 Variants 登记，覆盖范围：
    /// 变体键集合与 <see cref="Presentation.FeedbackBinder.Contracts.FeedbackActionKind"/> 运行时
    /// 枚举全集一致、子结构命中/坏形状各一例。
    /// </summary>
    public sealed class FeedbackActionsSchemaCoverageTests
    {
        [Fact]
        public void ActionVariantKeys_MatchRuntimeEnum()
        {
            var registered = FeedbackSchemas.ActionsItemSchema.Variants!.Cases.Keys.ToHashSet();
            Assert.Equal(FeedbackSchemas.ActionKindValues.ToHashSet(), registered);
            Assert.Equal(6, System.Enum.GetValues(typeof(FeedbackActionKind)).Length);
        }

        [Fact]
        public void Freeze_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"feedback.cov_freeze_ok\",\"event\":\"combat.damage_dealt\"," +
                "\"actions\":[{\"kind\":\"freeze\",\"params\":{\"duration_ms\":40}}]}]";

            var (registry, report) = FeedbackBinderTestSupport.BuildRegistry(
                new System.Collections.Generic.Dictionary<string, string> { ["feedback.binding"] = rows });

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Freeze_MissingDurationMs_ReportsRequiredField()
        {
            var rows = "[{\"id\":\"feedback.cov_freeze_bad\",\"event\":\"combat.damage_dealt\"," +
                "\"actions\":[{\"kind\":\"freeze\",\"params\":{}}]}]";

            var (registry, report) = FeedbackBinderTestSupport.BuildRegistry(
                new System.Collections.Generic.Dictionary<string, string> { ["feedback.binding"] = rows });

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "actions[0].params.duration_ms");
        }

        [Fact]
        public void UnknownActionKind_ReportsVariantDiscriminator()
        {
            var rows = "[{\"id\":\"feedback.cov_bad_kind\",\"event\":\"combat.damage_dealt\"," +
                "\"actions\":[{\"kind\":\"not_a_real_kind\",\"params\":{}}]}]";

            var (registry, report) = FeedbackBinderTestSupport.BuildRegistry(
                new System.Collections.Generic.Dictionary<string, string> { ["feedback.binding"] = rows });

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "variant_discriminator" && i.Field == "actions[0].kind");
        }
    }
}
