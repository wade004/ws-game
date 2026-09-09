using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="Core.Carriers.Item.ItemSchemas"/> 的 <c>template.stats</c>/
    /// <c>template.grants</c>/<c>template.weapon_profile</c>/<c>template.requirements</c>/
    /// <c>set.bonuses</c>/<c>budget_curve.entries</c> 子结构登记，覆盖范围：<c>stats[].op</c> 枚举
    /// 与 <c>EquipmentHost.ParseOp</c> 运行时集合一致、子结构命中/坏形状各一例、L3→L2/L1 跨层
    /// Reference（<c>stat.definition</c>/<c>skill.def</c>/<c>skill.aura_def</c>）存在性校验。
    /// 惯例同 <c>core/gameplay/loot/tests/LootSchemaCoverageTests.cs</c>。
    /// </summary>
    public sealed class ItemSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) => TestSupport.Table(table, rowsJson);

        private static InMemoryDataSource BaseSource() => new InMemoryDataSource()
            .Add("item.slot_definition", Envelope("item.slot_definition",
                "[{\"id\":\"item.slot.cov_sample\",\"name_key\":\"l10n.slot.cov_sample\"}]"))
            .Add("item.quality_definition", Envelope("item.quality_definition",
                "[{\"id\":\"item.quality.cov_sample\",\"name_key\":\"l10n.quality.cov_sample\"}]"))
            .Add("stat.definition", Envelope("stat.definition",
                "[{\"id\":\"stat.cov_sample\",\"name_key\":\"l10n.stat.cov_sample\",\"group\":\"primary\"}]"));

        private static DataRegistry NewRegistry(InMemoryDataSource source, bool withSkillSchemas = false)
        {
            var registry = new DataRegistry(source, TestSupport.CreateBus(), new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Template);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.SlotDefinition);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.QualityDefinition);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.BudgetCurve);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Set);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Affix);
            registry.RegisterSchema(StatDefinitionSchema());
            if (withSkillSchemas)
            {
                registry.RegisterSchema(SkillSchemas.Def);
                registry.RegisterSchema(SkillSchemas.AuraDef);
            }
            return registry;
        }

        private static TableSchema StatDefinitionSchema() => new TableSchema(
            name: "stat.definition",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("name_key", FieldKind.TextKey, required: true),
                new FieldSchema("group", FieldKind.Enum, required: true,
                    enumValues: new[] { "primary", "secondary", "derived", "resistance" }),
                new FieldSchema("default_base", FieldKind.Number, required: false),
            });

        private static string TemplateRow(string id, string slot, string quality, string extraFields) =>
            "[{\"id\":\"" + id + "\",\"slot\":\"" + slot + "\",\"quality\":\"" + quality + "\"," +
            "\"item_level\":1,\"display_ref\":\"display.item." + id + "\",\"stack_size\":1," +
            "\"name_key\":\"l10n.item." + id + "\"" + (string.IsNullOrEmpty(extraFields) ? "" : "," + extraFields) + "}]";

        // -----------------------------------------------------------------
        // stats[].op：枚举取值与运行时 ParseOp 集合一致
        // -----------------------------------------------------------------

        [Fact]
        public void StatOpEnumValues_MatchRuntimeParseOp()
        {
            var registered = Core.Carriers.Item.ItemSchemas.StatOpValues.ToHashSet();
            Assert.Equal(new[] { "flat", "pct", "mult" }.ToHashSet(), registered);
        }

        [Fact]
        public void Stats_WellFormed_LoadsWithoutErrors()
        {
            var rows = TemplateRow("item.cov_stats_ok", "item.slot.cov_sample", "item.quality.cov_sample",
                "\"stats\":[{\"stat\":\"stat.cov_sample\",\"op\":\"pct\",\"value\":0.1}]");

            var source = BaseSource().Add("item.template", Envelope("item.template", rows));
            var registry = NewRegistry(source);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Stats_UnknownStatReference_ReportsReferenceIntegrity()
        {
            var rows = TemplateRow("item.cov_stats_bad", "item.slot.cov_sample", "item.quality.cov_sample",
                "\"stats\":[{\"stat\":\"stat.does_not_exist\",\"op\":\"flat\",\"value\":1}]");

            var source = BaseSource().Add("item.template", Envelope("item.template", rows));
            var registry = NewRegistry(source);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "stats[0].stat");
        }

        [Fact]
        public void Stats_InvalidOpEnum_ReportsFieldType()
        {
            var rows = TemplateRow("item.cov_stats_badop", "item.slot.cov_sample", "item.quality.cov_sample",
                "\"stats\":[{\"stat\":\"stat.cov_sample\",\"op\":\"not_an_op\",\"value\":1}]");

            var source = BaseSource().Add("item.template", Envelope("item.template", rows));
            var registry = NewRegistry(source);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "stats[0].op");
        }

        // -----------------------------------------------------------------
        // grants.skills/auras：Reference(skill.def)/Reference(skill.aura_def)，L3 引用 L2 合法
        // -----------------------------------------------------------------

        [Fact]
        public void Grants_WellFormed_LoadsWithoutErrors()
        {
            var rows = TemplateRow("item.cov_grants_ok", "item.slot.cov_sample", "item.quality.cov_sample",
                "\"grants\":{\"skills\":[\"skill.cov_sample\"],\"auras\":[\"skill.aura_def.cov_sample\"]}");

            var source = BaseSource()
                .Add("item.template", Envelope("item.template", rows))
                .Add(SkillSchemas.Def.Name, Envelope(SkillSchemas.Def.Name, SampleSkillDefRow("skill.cov_sample")))
                .Add(SkillSchemas.AuraDef.Name, Envelope(SkillSchemas.AuraDef.Name,
                    "[{\"id\":\"skill.aura_def.cov_sample\",\"effects\":[]}]"));

            var registry = NewRegistry(source, withSkillSchemas: true);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Grants_UnknownSkillReference_ReportsReferenceIntegrity()
        {
            var rows = TemplateRow("item.cov_grants_bad", "item.slot.cov_sample", "item.quality.cov_sample",
                "\"grants\":{\"skills\":[\"skill.does_not_exist\"]}");

            var source = BaseSource()
                .Add("item.template", Envelope("item.template", rows))
                .Add(SkillSchemas.Def.Name, Envelope(SkillSchemas.Def.Name, "[]"))
                .Add(SkillSchemas.AuraDef.Name, Envelope(SkillSchemas.AuraDef.Name, "[]"));

            var registry = NewRegistry(source, withSkillSchemas: true);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "grants.skills[0]");
        }

        // -----------------------------------------------------------------
        // weapon_profile / requirements：命中 + 坏形状
        // -----------------------------------------------------------------

        [Fact]
        public void WeaponProfile_WellFormed_LoadsWithoutErrors()
        {
            var rows = TemplateRow("item.cov_weapon_ok", "item.slot.cov_sample", "item.quality.cov_sample",
                "\"weapon_profile\":{\"damage_min\":1,\"damage_max\":3,\"speed\":1.5,\"weapon_school\":\"skill.school.cov_sample\"}");

            var source = BaseSource().Add("item.template", Envelope("item.template", rows));
            var registry = NewRegistry(source);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void WeaponProfile_DamageMinNotNumber_ReportsFieldType()
        {
            var rows = TemplateRow("item.cov_weapon_bad", "item.slot.cov_sample", "item.quality.cov_sample",
                "\"weapon_profile\":{\"damage_min\":\"not_a_number\"}");

            var source = BaseSource().Add("item.template", Envelope("item.template", rows));
            var registry = NewRegistry(source);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "weapon_profile.damage_min");
        }

        [Fact]
        public void Requirements_LevelNotInt_ReportsFieldType()
        {
            var rows = TemplateRow("item.cov_req_bad", "item.slot.cov_sample", "item.quality.cov_sample",
                "\"requirements\":{\"level\":\"not_an_int\"}");

            var source = BaseSource().Add("item.template", Envelope("item.template", rows));
            var registry = NewRegistry(source);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "requirements.level");
        }

        // -----------------------------------------------------------------
        // item.set.bonuses：Reference(skill.aura_def)
        // -----------------------------------------------------------------

        [Fact]
        public void SetBonuses_WellFormed_LoadsWithoutErrors()
        {
            var setRows = "[{\"id\":\"item.set.cov_sample\",\"name_key\":\"l10n.set.cov_sample\"," +
                "\"pieces\":[],\"bonuses\":[{\"count\":2,\"aura_ref\":\"skill.aura_def.cov_set\"}]}]";

            var source = BaseSource()
                .Add("item.set", Envelope("item.set", setRows))
                .Add(SkillSchemas.Def.Name, Envelope(SkillSchemas.Def.Name, "[]"))
                .Add(SkillSchemas.AuraDef.Name, Envelope(SkillSchemas.AuraDef.Name,
                    "[{\"id\":\"skill.aura_def.cov_set\",\"effects\":[]}]"));

            var registry = NewRegistry(source, withSkillSchemas: true);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void SetBonuses_UnknownAuraRef_ReportsReferenceIntegrity()
        {
            var setRows = "[{\"id\":\"item.set.cov_bad\",\"name_key\":\"l10n.set.cov_bad\"," +
                "\"pieces\":[],\"bonuses\":[{\"count\":2,\"aura_ref\":\"skill.aura_def.does_not_exist\"}]}]";

            var source = BaseSource()
                .Add("item.set", Envelope("item.set", setRows))
                .Add(SkillSchemas.Def.Name, Envelope(SkillSchemas.Def.Name, "[]"))
                .Add(SkillSchemas.AuraDef.Name, Envelope(SkillSchemas.AuraDef.Name, "[]"));

            var registry = NewRegistry(source, withSkillSchemas: true);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "bonuses[0].aura_ref");
        }

        // -----------------------------------------------------------------
        // budget_curve.entries：命中 + 坏形状
        // -----------------------------------------------------------------

        [Fact]
        public void BudgetCurveEntries_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"item.budget.cov_sample\",\"entries\":[{\"item_level\":1,\"budget\":10}," +
                "{\"item_level\":10,\"budget\":100}]}]";

            var source = BaseSource().Add("item.budget_curve", Envelope("item.budget_curve", rows));
            var registry = NewRegistry(source);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void BudgetCurveEntries_MissingBudget_ReportsRequiredField()
        {
            var rows = "[{\"id\":\"item.budget.cov_bad\",\"entries\":[{\"item_level\":1}]}]";

            var source = BaseSource().Add("item.budget_curve", Envelope("item.budget_curve", rows));
            var registry = NewRegistry(source);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "entries[0].budget");
        }

        private static string SampleSkillDefRow(string id) =>
            "[{\"id\":\"" + id + "\",\"school\":\"skill.school.cov_sample\",\"kind\":\"active\",\"range\":0," +
            "\"cast_time\":0,\"respects_gcd\":true,\"target_shape_ref\":\"target.cov_sample\",\"effects\":[]}]";
    }
}
