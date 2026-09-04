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

        // -----------------------------------------------------------------
        // GobjOnUseKindRule
        // -----------------------------------------------------------------

        [Fact]
        public void OnUseKind_PassesForSkillOrDialog_FailsForUnknownKind()
        {
            var ok = new GobjWorldBuilder()
                .Template(Template("gobj.sample_sign_ok", "sign", J.O(("text_key", J.S("l10n.sample_a.text"))),
                    onUse: J.O(("kind", J.S("skill")), ("ref", J.S("skill.sample_a")))))
                .ValidationRule(new GobjOnUseKindRule());
            Assert.False(ok.Validate().IsBlocking);

            var bad = new GobjWorldBuilder()
                .Template(Template("gobj.sample_sign_bad", "sign", J.O(("text_key", J.S("l10n.sample_b.text"))),
                    onUse: J.O(("kind", J.S("not_a_real_kind")), ("ref", J.S("skill.sample_b")))))
                .ValidationRule(new GobjOnUseKindRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_on_use_kind");
        }

        [Fact]
        public void OnUseKind_FailsWhenRefMissing()
        {
            var bad = new GobjWorldBuilder()
                .Template(Template("gobj.sample_sign_noref", "sign", J.O(("text_key", J.S("l10n.sample_c.text"))),
                    onUse: J.O(("kind", J.S("dialog")))))
                .ValidationRule(new GobjOnUseKindRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_on_use_kind");
        }

        // -----------------------------------------------------------------
        // GobjLockRequirementFieldGroupRule
        // -----------------------------------------------------------------

        [Fact]
        public void LockRequirementFieldGroup_PassesForKnownKind_FailsForUnknownKind()
        {
            var ok = new GobjWorldBuilder()
                .Lock(J.O(("id", J.S("gobj.lock.sample_ok")), ("requirement", J.O(("kind", J.S("item_key")), ("item_id", J.S("item.sample_a"))))))
                .ValidationRule(new GobjLockRequirementFieldGroupRule());
            Assert.False(ok.Validate().IsBlocking);

            var bad = new GobjWorldBuilder()
                .Lock(J.O(("id", J.S("gobj.lock.sample_bad")), ("requirement", J.O(("kind", J.S("not_a_real_kind")), ("item_id", J.S("item.sample_a"))))))
                .ValidationRule(new GobjLockRequirementFieldGroupRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_lock_requirement_field_group");
        }

        [Fact]
        public void LockRequirementFieldGroup_WorldFlagRequiresFlagKeyAndExpected()
        {
            var ok = new GobjWorldBuilder()
                .Lock(J.O(("id", J.S("gobj.lock.sample_flag_ok")),
                    ("requirement", J.O(("kind", J.S("world_flag")), ("flag_key", J.S("world.sample.b")), ("expected", J.B(true))))))
                .ValidationRule(new GobjLockRequirementFieldGroupRule());
            Assert.False(ok.Validate().IsBlocking);

            var bad = new GobjWorldBuilder()
                .Lock(J.O(("id", J.S("gobj.lock.sample_flag_bad")), ("requirement", J.O(("kind", J.S("world_flag")), ("flag_key", J.S("world.sample.c"))))))
                .ValidationRule(new GobjLockRequirementFieldGroupRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_lock_requirement_field_group");
        }

        [Fact]
        public void LockRequirementFieldGroup_SkillCheckRequiresSkillTagAndMinValue()
        {
            var ok = new GobjWorldBuilder()
                .Lock(J.O(("id", J.S("gobj.lock.sample_skill_ok")),
                    ("requirement", J.O(("kind", J.S("skill_check")), ("skill_tag", J.S("stat.sample_a")), ("min_value", J.N(3))))))
                .ValidationRule(new GobjLockRequirementFieldGroupRule());
            Assert.False(ok.Validate().IsBlocking);

            var bad = new GobjWorldBuilder()
                .Lock(J.O(("id", J.S("gobj.lock.sample_skill_bad")), ("requirement", J.O(("kind", J.S("skill_check")), ("skill_tag", J.S("stat.sample_b"))))))
                .ValidationRule(new GobjLockRequirementFieldGroupRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_lock_requirement_field_group");
        }
    }
}
