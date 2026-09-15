using System.Linq;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// T-N3-9（ADR-0032 决策 6"授予的技能或光环走技能预算校验，预算 = 该件装备预算 × 品质特效
    /// 占比，警告级；橙装独特技能免校验，靠超模说明登记意图"；04 第 5 节"授予价值超特效占比"，该行
    /// 原文批注"N3 落地"——本任务补上，见 <see cref="Core.Carriers.Item.ItemGrantValueExceedsShareRule"/>
    /// 类型判断记录）："装备授予价值超占比 Warning 正负例各 1"（分阶段落地计划第 14 节 T-N3-9 行验收
    /// 标准）。
    /// </summary>
    public sealed class T_N3_9_ItemGrantValueExceedsShareRuleTests
    {
        private sealed class FakeAnchorProvider : ISkillBudgetAnchorProvider
        {
            public double AnchorDps { get; set; } = 1.0;

            public double GetAnchorDps(int level) => AnchorDps;

            public double GetExpectedScalingStatValue(Id stat, int level) => 0.0;
        }

        private const string SlotJson =
            "[{\"id\": \"item.slot.consumable\", \"name_key\": \"l10n.item.slot.consumable\"}]";

        private const string QualityWithGrantShareJson =
            "[{\"id\": \"item.quality.common\", \"name_key\": \"l10n.item.quality.common\", " +
            "\"budget_multiplier\": 1, \"grant_budget_share\": 0.1}]";

        private const string BudgetCurveJson =
            "[{\"id\": \"item.budget.default\", \"entries\": [" +
            "{\"item_level\": 1, \"budget\": 20}, {\"item_level\": 10, \"budget\": 200}]}]";

        /// <summary>被授予技能，效果值 = base_value（<see cref="FakeAnchorProvider"/> 缩放贡献恒
        /// 0），T = max(cast_time=0, beat_seconds 缺省 1) = 1，anchorDps = 1 —— <see
        /// cref="Core.Rules.Skill.SkillBudgetAnalyzer.Analyze"/> 内部预算上限公式在本用例里只影响
        /// <c>SkillBudgetResult</c> 的 <c>Verdict</c>（本测试不关心），<see
        /// cref="Core.Rules.Skill.SkillBudgetAnalyzer.ComputeGrantValue"/> 取用的是
        /// <c>EffectiveValue</c>（= base_value），与预算上限无关。</summary>
        private static string GrantedSkillJson(string id, double baseValue) =>
            "[{\"id\": \"" + id + "\", \"school\": \"school.physical\", \"kind\": \"active\", \"range\": 0," +
            " \"cast_time\": 0, \"respects_gcd\": false, \"target_shape_ref\": \"target.chain.n3_9_unused\"," +
            " \"effects\": [{\"kind\": \"school_damage\", \"params\": {\"base_value\": " + baseValue +
            ", \"coefficient\": 0, \"school\": \"school.physical\"}}]}]";

        private static string TemplateJson(string id, string skillId, string? budgetNote = null)
        {
            var noteField = budgetNote != null ? ", \"budget_note\": \"" + budgetNote + "\"" : string.Empty;
            return "[{\"id\": \"" + id + "\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\"," +
                " \"item_level\": 1, \"display_ref\": \"display." + id + "\", \"stack_size\": 1," +
                " \"name_key\": \"l10n." + id + "\", \"grants\": {\"skills\": [\"" + skillId + "\"]}" +
                noteField + "}]";
        }

        private static IDataRegistryView BuildView(string templateJson, string skillDefJson)
        {
            return TestSupport.BuildRegistry(source =>
            {
                source.Add("item.slot_definition", TestSupport.Table("item.slot_definition", SlotJson));
                source.Add("item.quality_definition", TestSupport.Table("item.quality_definition", QualityWithGrantShareJson));
                source.Add("item.budget_curve", TestSupport.Table("item.budget_curve", BudgetCurveJson));
                source.Add("item.template", TestSupport.Table("item.template", templateJson));
                source.Add("skill.def", TestSupport.Table("skill.def", skillDefJson));
                source.Add("skill.aura_def", TestSupport.Table("skill.aura_def", "[]"));
            });
        }

        [Fact]
        public void GrantValue_ExceedsShare_ReportsWarning()
        {
            // 预算上限 B = 20（item_level=1）× 品质倍率 1 × 槽位系数缺省 1 = 20；
            // 授予占比份额 = B × grant_budget_share(0.1) = 2；授予技能实际价值 = base_value = 40，
            // 远超允许份额 2——应报警告。
            var view = BuildView(
                TemplateJson("item.n3_9_grant_over", "skill.n3_9_granted_over"),
                GrantedSkillJson("skill.n3_9_granted_over", baseValue: 40));

            var rule = new ItemGrantValueExceedsShareRule(new Id("item.budget.default"), new FakeAnchorProvider());
            var issues = rule.Validate(view).ToList();

            var issue = Assert.Single(issues);
            Assert.Equal(ValidationSeverity.Warning, issue.Severity);
            Assert.Equal(ItemGrantValueExceedsShareRule.Check, issue.Check);
            Assert.Equal("item.n3_9_grant_over", issue.RecordKey);
            Assert.Equal("grants", issue.Field);
        }

        /// <summary>分阶段落地计划 T-N5-3（数值规则核对表 W4）：不可提升警告在
        /// <see cref="DataRegistryStrictness.WarningsBlock"/> 下不阻断——同
        /// <see cref="GrantValue_ExceedsShare_ReportsWarning"/> 的数据，但走完整的
        /// <see cref="Core.Foundation.DataRegistry.DataRegistry.LoadAll()"/> +
        /// <see cref="ValidationReport"/> 路径（该用例只调用 <c>rule.Validate(view)</c> 拿裸问题
        /// 列表，看不到 <see cref="ValidationReport.IsBlocking"/>）——本条也是核对表 W4"锚点依赖"三条
        /// 之一，需要显式注入 <see cref="FakeAnchorProvider"/> 才会产出问题（同 B11/W1，见
        /// <c>ItemGrantValueExceedsShareRule</c> 类型判断记录"anchorProvider 为 null 时整条规则不产生
        /// 任何问题"）。判断记录（不经 <see cref="TestSupport.BuildRegistry"/>）：同
        /// <c>ItemValidationRulesTests.BudgetRule_UtilizationBelowThreshold_UnderWarningsBlock_DoesNotBlock</c>
        /// 判断记录——<see cref="TestSupport.BuildRegistry"/> 不注册 <c>l10n.locale</c>/<c>l10n.text</c>，
        /// 会另外降级出可提升的"表未加载，跳过"Warning，在 WarningsBlock 下一并阻断，掩盖本规则本身
        /// "不可提升"的行为，因此手工建 registry 并显式补齐 l10n 两张表 + 真实锚点。</summary>
        [Fact]
        public void GrantValue_ExceedsShare_UnderWarningsBlock_DoesNotBlock()
        {
            const string slot = "[{\"id\": \"item.slot.n5_3_w4\", \"name_key\": \"l10n.n5_3_w4_slot\"}]";
            const string quality =
                "[{\"id\": \"item.quality.n5_3_w4\", \"name_key\": \"l10n.n5_3_w4_quality\", " +
                "\"budget_multiplier\": 1, \"grant_budget_share\": 0.1}]";
            const string budgetCurve = "[{\"id\": \"item.budget.n5_3_w4\", \"entries\": [{\"item_level\": 1, \"budget\": 20}]}]";
            const string template =
                "[{\"id\": \"item.n5_3_w4\", \"slot\": \"item.slot.n5_3_w4\", \"quality\": \"item.quality.n5_3_w4\", " +
                "\"item_level\": 1, \"display_ref\": \"display.n5_3_w4\", \"stack_size\": 1, \"name_key\": \"l10n.n5_3_w4_item\", " +
                "\"grants\": {\"skills\": [\"skill.n5_3_w4_granted\"]}}]";
            const string skillDef =
                "[{\"id\": \"skill.n5_3_w4_granted\", \"school\": \"school.physical\", \"kind\": \"active\", \"range\": 0," +
                " \"cast_time\": 0, \"respects_gcd\": false, \"target_shape_ref\": \"target.chain.n5_3_w4_unused\"," +
                " \"effects\": [{\"kind\": \"school_damage\", \"params\": {\"base_value\": 40, \"coefficient\": 0, " +
                " \"school\": \"school.physical\"}}]}]";
            const string locales = "[{\"id\": \"l10n.locale.zh_cn\", \"is_default\": true}]";
            const string texts =
                "[{\"key\": \"l10n.n5_3_w4_slot\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"占位\"}," +
                "{\"key\": \"l10n.n5_3_w4_quality\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"占位\"}," +
                "{\"key\": \"l10n.n5_3_w4_item\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"占位\"}]";

            var source = new InMemoryDataSource()
                .Add("item.slot_definition", TestSupport.Table("item.slot_definition", slot))
                .Add("item.quality_definition", TestSupport.Table("item.quality_definition", quality))
                .Add("item.budget_curve", TestSupport.Table("item.budget_curve", budgetCurve))
                .Add("item.template", TestSupport.Table("item.template", template))
                .Add("skill.def", TestSupport.Table("skill.def", skillDef))
                .Add("skill.aura_def", TestSupport.Table("skill.aura_def", "[]"))
                .Add("l10n.locale", TestSupport.Table("l10n.locale", locales))
                .Add("l10n.text", TestSupport.Table("l10n.text", texts));

            var registry = new DataRegistry(source, TestSupport.CreateBus(),
                new DataRegistryOptions { FailOnUnknownTable = false, Strictness = DataRegistryStrictness.WarningsBlock });
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.SlotDefinition);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.QualityDefinition);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.BudgetCurve);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Template);
            registry.RegisterSchema(Core.Rules.Skill.SkillSchemas.Def);
            registry.RegisterSchema(Core.Rules.Skill.SkillSchemas.AuraDef);
            registry.RegisterSchema(Core.Foundation.Localization.L10nSchemas.Locale);
            registry.RegisterSchema(Core.Foundation.Localization.L10nSchemas.Text);
            registry.RegisterValidationRule(
                new ItemGrantValueExceedsShareRule(new Id("item.budget.n5_3_w4"), new FakeAnchorProvider()));

            var report = registry.LoadAll();

            var issue = Assert.Single(report.Issues, i => i.Check == ItemGrantValueExceedsShareRule.Check);
            Assert.Equal(ValidationSeverity.Warning, issue.Severity);
            Assert.Equal(1, report.NonEscalatableWarningCount);
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void GrantValue_WithinShare_NoIssue()
        {
            // 同上预算/份额，授予技能实际价值 = base_value = 1，未超允许份额 2——不应报告。
            var view = BuildView(
                TemplateJson("item.n3_9_grant_ok", "skill.n3_9_granted_ok"),
                GrantedSkillJson("skill.n3_9_granted_ok", baseValue: 1));

            var rule = new ItemGrantValueExceedsShareRule(new Id("item.budget.default"), new FakeAnchorProvider());
            var issues = rule.Validate(view).ToList();

            Assert.Empty(issues);
        }

        /// <summary>ADR-0032 决策 6"橙装独特技能免校验，靠超模说明登记意图"：即便授予价值远超份额，
        /// 模板填了 <c>budget_note</c> 后整条豁免，不产生任何问题（不是降级为警告，见 <see
        /// cref="ItemGrantValueExceedsShareRule"/> 类型判断记录"本条件在这里整条豁免"）。</summary>
        [Fact]
        public void GrantValue_ExceedsShare_WithBudgetNote_Exempted()
        {
            var view = BuildView(
                TemplateJson("item.n3_9_grant_note", "skill.n3_9_granted_note", budgetNote: "橙装独特技能"),
                GrantedSkillJson("skill.n3_9_granted_note", baseValue: 40));

            var rule = new ItemGrantValueExceedsShareRule(new Id("item.budget.default"), new FakeAnchorProvider());
            var issues = rule.Validate(view).ToList();

            Assert.Empty(issues);
        }

        /// <summary>同 <see cref="Core.Rules.Skill.SkillBudgetValidationRule"/> 判断记录"为 null 时
        /// 该规则不注册这项跨表检查完全跳过"：未注入 <see cref="ISkillBudgetAnchorProvider"/> 时，
        /// 即便授予价值本该超占比，也不产生任何问题。</summary>
        [Fact]
        public void GrantValue_WithoutAnchorProvider_SkipsEntirely()
        {
            var view = BuildView(
                TemplateJson("item.n3_9_grant_noprovider", "skill.n3_9_granted_noprovider"),
                GrantedSkillJson("skill.n3_9_granted_noprovider", baseValue: 40));

            var rule = new ItemGrantValueExceedsShareRule(new Id("item.budget.default"), anchorProvider: null);
            var issues = rule.Validate(view).ToList();

            Assert.Empty(issues);
        }
    }
}
