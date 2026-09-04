using System.Collections.Generic;
using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Xunit;

namespace Tests.Foundation.DisplayInfo
{
    public class DisplayKindFieldGroupRuleTests
    {
        private static IReadOnlyList<ValidationIssue> FieldGroupIssues(ValidationReport report) =>
            report.Issues.Where(i => i.Check == "display_kind_field_group").ToArray();

        // ---------------------------------------------------------------
        // 正例：有效的 sprite / model 记录各一条，不产生字段组问题。
        // ---------------------------------------------------------------

        [Fact]
        public void ValidSpriteRecord_NoFieldGroupIssues()
        {
            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.map"] = "[" + DisplayInfoTestSupport.GreyWolfSpriteRow + "]" },
                new IValidationRule[] { new DisplayKindFieldGroupRule() });

            Assert.False(report.IsBlocking);
            Assert.Empty(FieldGroupIssues(report));
        }

        [Fact]
        public void ValidModelRecord_NoFieldGroupIssues()
        {
            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string>
                {
                    ["display.map"] = "[" + DisplayInfoTestSupport.StoneGolemModelRow + "]",
                    ["display.anim_set"] = "[" + DisplayInfoTestSupport.StoneGolemAnimSetRow + "]",
                },
                new IValidationRule[] { new DisplayKindFieldGroupRule() });

            Assert.False(report.IsBlocking);
            Assert.Empty(FieldGroupIssues(report));
        }

        // ---------------------------------------------------------------
        // 反例：sprite 型必填字段缺失、sprite 型携带 model 专属字段。
        // ---------------------------------------------------------------

        [Fact]
        public void SpriteRecord_MissingDirectionCount_ReportsRequiredFieldError()
        {
            const string row = @"
            {
              ""id"": ""display.broken_sprite"",
              ""category"": ""creature"",
              ""logical_id"": ""creature.broken_sprite"",
              ""kind"": ""sprite"",
              ""sprite_set_id"": ""sprite.creature.broken""
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.map"] = "[" + row + "]" },
                new IValidationRule[] { new DisplayKindFieldGroupRule() });

            var issues = FieldGroupIssues(report);
            Assert.Contains(issues, i => i.Field == "direction_count" && i.RecordKey == "display.broken_sprite");
        }

        [Fact]
        public void SpriteRecord_WithModelOnlyFieldPresent_ReportsMustBeEmptyError()
        {
            const string row = @"
            {
              ""id"": ""display.contaminated_sprite"",
              ""category"": ""creature"",
              ""logical_id"": ""creature.contaminated_sprite"",
              ""kind"": ""sprite"",
              ""sprite_set_id"": ""sprite.creature.contaminated"",
              ""direction_count"": 8,
              ""model_ref"": ""model.creature.should_not_be_here""
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.map"] = "[" + row + "]" },
                new IValidationRule[] { new DisplayKindFieldGroupRule() });

            var issues = FieldGroupIssues(report);
            Assert.Contains(issues, i => i.Field == "model_ref" && i.RecordKey == "display.contaminated_sprite");
        }

        // ---------------------------------------------------------------
        // 反例：model 型必填字段缺失、model 型携带 sprite 专属字段。
        // ---------------------------------------------------------------

        [Fact]
        public void ModelRecord_MissingAnimSetRef_ReportsRequiredFieldError()
        {
            const string row = @"
            {
              ""id"": ""display.broken_model"",
              ""category"": ""creature"",
              ""logical_id"": ""creature.broken_model"",
              ""kind"": ""model"",
              ""model_ref"": ""model.creature.broken""
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.map"] = "[" + row + "]" },
                new IValidationRule[] { new DisplayKindFieldGroupRule() });

            var issues = FieldGroupIssues(report);
            Assert.Contains(issues, i => i.Field == "anim_set_ref" && i.RecordKey == "display.broken_model");
        }

        [Fact]
        public void ModelRecord_WithSpriteOnlyFieldPresent_ReportsMustBeEmptyError()
        {
            const string row = @"
            {
              ""id"": ""display.contaminated_model"",
              ""category"": ""creature"",
              ""logical_id"": ""creature.contaminated_model"",
              ""kind"": ""model"",
              ""model_ref"": ""model.creature.contaminated"",
              ""anim_set_ref"": ""display.anim_set.stone_golem"",
              ""sprite_set_id"": ""sprite.should_not_be_here""
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string>
                {
                    ["display.map"] = "[" + row + "]",
                    ["display.anim_set"] = "[" + DisplayInfoTestSupport.StoneGolemAnimSetRow + "]",
                },
                new IValidationRule[] { new DisplayKindFieldGroupRule() });

            var issues = FieldGroupIssues(report);
            Assert.Contains(issues, i => i.Field == "sprite_set_id" && i.RecordKey == "display.contaminated_model");
        }
    }
}
