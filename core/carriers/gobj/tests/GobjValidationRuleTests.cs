using Core.Carriers.Gobj;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Xunit;

namespace Tests.Carriers.Gobj
{
    /// <summary>三条校验规则（见 schema/README.md"校验规则"表）各一条正例、一条反例（惯例同
    /// <c>core/rules/skill/tests</c> 的 <c>SkillValidationRuleTests</c>）。</summary>
    public sealed class GobjValidationRuleTests
    {
        private static readonly Id DisplayRef = new Id("display.sample_gobj");

        private static JsonObject Template(string id, string kind, JsonObject typeData, JsonObject? onUse = null)
        {
            var fields = new System.Collections.Generic.List<(string, JsonValue)>
            {
                ("id", J.S(id)),
                ("name_key", J.S("l10n." + id.Replace('.', '_') + ".name")),
                ("kind", J.S(kind)),
                ("type_data", typeData),
                ("display_ref", J.S(DisplayRef.Value)),
            };

            if (onUse != null)
            {
                fields.Add(("on_use", onUse));
            }

            return J.O(fields.ToArray());
        }

        // -----------------------------------------------------------------
        // GobjTypeDataFieldGroupRule
        // -----------------------------------------------------------------

        [Fact]
        public void TypeDataFieldGroup_PassesWithRequiredFields_FailsWhenChestMissingLootTableRef()
        {
            var ok = new GobjWorldBuilder()
                .Template(Template("gobj.sample_chest_ok", "chest", J.O(("loot_table_ref", J.S("loot.sample_a")))))
                .ValidationRule(new GobjTypeDataFieldGroupRule());
            Assert.False(ok.Validate().IsBlocking);

            var bad = new GobjWorldBuilder()
                .Template(Template("gobj.sample_chest_bad", "chest", J.O()))
                .ValidationRule(new GobjTypeDataFieldGroupRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_type_data_field_group");
        }

        [Fact]
        public void TypeDataFieldGroup_GatherNodeRequiresBothFields()
        {
            var ok = new GobjWorldBuilder()
                .Template(Template("gobj.sample_node_ok", "gather_node",
                    J.O(("loot_table_ref", J.S("loot.sample_b")), ("respawn_after_use", J.N(30)))))
                .ValidationRule(new GobjTypeDataFieldGroupRule());
            Assert.False(ok.Validate().IsBlocking);

            var bad = new GobjWorldBuilder()
                .Template(Template("gobj.sample_node_bad", "gather_node", J.O(("loot_table_ref", J.S("loot.sample_c")))))
                .ValidationRule(new GobjTypeDataFieldGroupRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_type_data_field_group");
        }

        // ADR-0019 F1c 退役：GobjOnUseKindRule/GobjLockRequirementFieldGroupRule 两条规则连同这里的
        // 测试一并删除，覆盖场景迁移至 GobjSchemaCoverageTests（variant_discriminator/required_field/
        // reference_integrity 三个内建检查名，见该文件）。
    }
}
