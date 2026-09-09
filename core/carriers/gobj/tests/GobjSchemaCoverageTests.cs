using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Gobj;
using Core.Carriers.Item;
using Core.Foundation.DataRegistry;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Carriers.Gobj
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="GobjSchemas"/> 的 <c>type_data</c>（Fields 并集，判别字段 kind 与
    /// 被判别对象不同级，Variants 不适用）+ <c>on_use</c>/<c>requirement</c>（Variants）子结构登记，
    /// 覆盖范围：变体键集合与运行时枚举全集一致（<see cref="OnUseKind"/>/<see cref="LockRequirementKind"/>）、
    /// 子结构命中/坏形状各一例、<see cref="GobjTypeDataFieldGroupRule"/>（不退役）与内建校验不重复
    /// 报告同一缺陷。惯例同 <c>core/gameplay/loot/tests/LootSchemaCoverageTests.cs</c>。
    /// </summary>
    public sealed class GobjSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IDataRegistry NewRegistry(InMemoryDataSource source, params IValidationRule[] rules)
        {
            var registry = new DataRegistry(source, GobjWorldBuilder.CreateBus(), new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(GobjSchemas.Template);
            registry.RegisterSchema(GobjSchemas.Lock);
            foreach (var rule in rules)
            {
                registry.RegisterValidationRule(rule);
            }
            return registry;
        }

        // -----------------------------------------------------------------
        // on_use：变体键集合一致性
        // -----------------------------------------------------------------

        [Fact]
        public void OnUseVariantKeys_MatchRuntimeEnum()
        {
            var onUseField = GobjSchemas.Template.GetField("on_use")!;
            var registeredKeys = onUseField.Variants!.Cases.Keys.ToHashSet();

            Assert.Equal(GobjSchemas.OnUseKindValues.ToHashSet(), registeredKeys);
            Assert.Equal(2, System.Enum.GetValues(typeof(OnUseKind)).Length);
        }

        [Fact]
        public void RequirementVariantKeys_MatchRuntimeEnum()
        {
            var requirementField = GobjSchemas.Lock.GetField("requirement")!;
            var registeredKeys = requirementField.Variants!.Cases.Keys.ToHashSet();

            Assert.Equal(GobjSchemas.LockRequirementKindValues.ToHashSet(), registeredKeys);
            Assert.Equal(3, System.Enum.GetValues(typeof(LockRequirementKind)).Length);
        }

        // -----------------------------------------------------------------
        // type_data：命中 + 坏形状（trap.skill_id 引用不存在的 skill.def）
        // -----------------------------------------------------------------

        [Fact]
        public void TypeData_WellFormedTrap_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"gobj.sample_trap_ok\",\"name_key\":\"l10n.gobj.sample_trap_ok\",\"kind\":\"trap\"," +
                "\"type_data\":{\"skill_id\":\"skill.sample_trap\",\"trigger_shape\":{\"kind\":\"circle\",\"radius\":2}}," +
                "\"display_ref\":\"display.sample_trap\"}]";

            var source = new InMemoryDataSource()
                .Add(GobjSchemas.Template.Name, Envelope(GobjSchemas.Template.Name, rows))
                .Add(SkillSchemas.Def.Name, Envelope(SkillSchemas.Def.Name, SampleSkillDefRow("skill.sample_trap")));

            var registry = NewRegistry(source, new GobjTypeDataFieldGroupRule());
            registry.RegisterSchema(SkillSchemas.Def);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void TypeData_TrapSkillIdMissing_ReportsReferenceIntegrity()
        {
            var rows = "[{\"id\":\"gobj.sample_trap_bad\",\"name_key\":\"l10n.gobj.sample_trap_bad\",\"kind\":\"trap\"," +
                "\"type_data\":{\"skill_id\":\"skill.does_not_exist\",\"trigger_shape\":{\"kind\":\"circle\",\"radius\":2}}," +
                "\"display_ref\":\"display.sample_trap\"}]";

            var source = new InMemoryDataSource()
                .Add(GobjSchemas.Template.Name, Envelope(GobjSchemas.Template.Name, rows))
                .Add(SkillSchemas.Def.Name, Envelope(SkillSchemas.Def.Name, "[]"));

            var registry = NewRegistry(source, new GobjTypeDataFieldGroupRule());
            registry.RegisterSchema(SkillSchemas.Def);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "type_data.skill_id");
        }

        [Fact]
        public void TypeData_ChestMissingLootTableRef_ReportsFieldGroup_NotDouble()
        {
            var rows = "[{\"id\":\"gobj.sample_chest_bad\",\"name_key\":\"l10n.gobj.sample_chest_bad\",\"kind\":\"chest\"," +
                "\"type_data\":{},\"display_ref\":\"display.sample_chest\"}]";

            var source = new InMemoryDataSource().Add(GobjSchemas.Template.Name, Envelope(GobjSchemas.Template.Name, rows));
            var registry = NewRegistry(source, new GobjTypeDataFieldGroupRule());
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_type_data_field_group");
        }

        // -----------------------------------------------------------------
        // on_use：命中 + 坏形状
        // -----------------------------------------------------------------

        [Fact]
        public void OnUse_WellFormedSkillRef_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"gobj.sample_sign_ok\",\"name_key\":\"l10n.gobj.sample_sign_ok\",\"kind\":\"sign\"," +
                "\"type_data\":{\"text_key\":\"l10n.sample_a.text\"},\"display_ref\":\"display.sample_sign\"," +
                "\"on_use\":{\"kind\":\"skill\",\"ref\":\"skill.sample_onuse\"}}]";

            var source = new InMemoryDataSource()
                .Add(GobjSchemas.Template.Name, Envelope(GobjSchemas.Template.Name, rows))
                .Add(SkillSchemas.Def.Name, Envelope(SkillSchemas.Def.Name, SampleSkillDefRow("skill.sample_onuse")));

            var registry = NewRegistry(source, new GobjTypeDataFieldGroupRule());
            registry.RegisterSchema(SkillSchemas.Def);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void OnUse_UnknownKind_ReportsVariantDiscriminator()
        {
            var rows = "[{\"id\":\"gobj.sample_sign_bad\",\"name_key\":\"l10n.gobj.sample_sign_bad\",\"kind\":\"sign\"," +
                "\"type_data\":{\"text_key\":\"l10n.sample_b.text\"},\"display_ref\":\"display.sample_sign\"," +
                "\"on_use\":{\"kind\":\"not_a_real_kind\",\"ref\":\"skill.sample_b\"}}]";

            var source = new InMemoryDataSource().Add(GobjSchemas.Template.Name, Envelope(GobjSchemas.Template.Name, rows));
            var registry = NewRegistry(source, new GobjTypeDataFieldGroupRule());
            registry.RegisterSchema(SkillSchemas.Def);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "variant_discriminator" && i.Field == "on_use.kind");
        }

        [Fact]
        public void OnUse_DialogRefMissing_ReportsRequiredField()
        {
            var rows = "[{\"id\":\"gobj.sample_sign_noref\",\"name_key\":\"l10n.gobj.sample_sign_noref\",\"kind\":\"sign\"," +
                "\"type_data\":{\"text_key\":\"l10n.sample_c.text\"},\"display_ref\":\"display.sample_sign\"," +
                "\"on_use\":{\"kind\":\"dialog\"}}]";

            var source = new InMemoryDataSource().Add(GobjSchemas.Template.Name, Envelope(GobjSchemas.Template.Name, rows));
            var registry = NewRegistry(source, new GobjTypeDataFieldGroupRule());
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "on_use.ref");
        }

        // -----------------------------------------------------------------
        // requirement：命中 + 坏形状
        // -----------------------------------------------------------------

        [Fact]
        public void Requirement_WellFormedItemKey_LoadsWithoutErrors()
        {
            var lockRows = "[{\"id\":\"gobj.lock.sample_ok\",\"requirement\":{\"kind\":\"item_key\",\"item_id\":\"item.sample_key\"}}]";

            var source = new InMemoryDataSource()
                .Add(GobjSchemas.Lock.Name, Envelope(GobjSchemas.Lock.Name, lockRows))
                .Add(ItemSchemas.SlotDefinition.Name, Envelope(ItemSchemas.SlotDefinition.Name,
                    "[{\"id\":\"item.slot.gobj_sample\",\"name_key\":\"l10n.slot.gobj_sample\"}]"))
                .Add(ItemSchemas.QualityDefinition.Name, Envelope(ItemSchemas.QualityDefinition.Name,
                    "[{\"id\":\"item.quality.gobj_sample\",\"name_key\":\"l10n.quality.gobj_sample\"}]"))
                .Add(ItemSchemas.Template.Name, Envelope(ItemSchemas.Template.Name,
                    "[{\"id\":\"item.sample_key\",\"slot\":\"item.slot.gobj_sample\",\"quality\":\"item.quality.gobj_sample\"," +
                    "\"item_level\":1,\"display_ref\":\"display.item.sample_key\",\"stack_size\":1,\"name_key\":\"l10n.item.sample_key\"}]"));

            var registry = NewRegistry(source);
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Requirement_UnknownKind_ReportsVariantDiscriminator()
        {
            var lockRows = "[{\"id\":\"gobj.lock.sample_bad\",\"requirement\":{\"kind\":\"not_a_real_kind\",\"item_id\":\"item.sample_a\"}}]";
            var source = new InMemoryDataSource().Add(GobjSchemas.Lock.Name, Envelope(GobjSchemas.Lock.Name, lockRows));
            var registry = NewRegistry(source);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "variant_discriminator" && i.Field == "requirement.kind");
        }

        [Fact]
        public void Requirement_SkillCheckMissingMinValue_ReportsRequiredField()
        {
            var lockRows = "[{\"id\":\"gobj.lock.sample_skill_bad\",\"requirement\":{\"kind\":\"skill_check\",\"skill_tag\":\"stat.sample_b\"}}]";
            var source = new InMemoryDataSource().Add(GobjSchemas.Lock.Name, Envelope(GobjSchemas.Lock.Name, lockRows));
            var registry = NewRegistry(source);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "requirement.min_value");
        }

        private static string SampleSkillDefRow(string id) =>
            "[{\"id\":\"" + id + "\",\"school\":\"skill.school.sample\",\"kind\":\"active\",\"range\":0," +
            "\"cast_time\":0,\"respects_gcd\":true,\"target_shape_ref\":\"target.sample\",\"effects\":[]}]";
    }
}
