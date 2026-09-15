using Core.Foundation.DataRegistry;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// T-N3-1 验收标准"schema 覆盖"：<c>skill.def</c> 新增 <c>use_condition</c>（Expr，可选）/
    /// <c>budget_note</c>（String，可选）两个字段的元数据与加载期校验行为，以及
    /// <c>cast_time</c>/<c>respects_gcd</c>/<c>cost</c> 三个既有字段"只改描述、不改类型"这一约束
    /// （ADR-0031 决策 2/9/10；06 第 3.1 节 2026-09-14 修订段）。
    /// </summary>
    public sealed class T_N3_1_SkillDefUseConditionBudgetNoteTests
    {
        // -----------------------------------------------------------------
        // 字段元数据：种类、必填性不因本任务改变（新增字段可选，既有字段改描述不改类型）
        // -----------------------------------------------------------------

        [Fact]
        public void UseCondition_Field_IsOptionalExpr()
        {
            var field = SkillSchemas.Def.GetField("use_condition");

            Assert.NotNull(field);
            Assert.Equal(FieldKind.Expr, field!.Kind);
            Assert.False(field.Required);
        }

        [Fact]
        public void BudgetNote_Field_IsOptionalString()
        {
            var field = SkillSchemas.Def.GetField("budget_note");

            Assert.NotNull(field);
            Assert.Equal(FieldKind.String, field!.Kind);
            Assert.False(field.Required);
        }

        [Theory]
        [InlineData("cast_time", FieldKind.Number, true)]
        [InlineData("respects_gcd", FieldKind.Bool, true)]
        [InlineData("cost", FieldKind.Array, false)]
        public void ExistingField_DescriptionRewrite_DoesNotChangeKindOrRequired(
            string fieldName, FieldKind expectedKind, bool expectedRequired)
        {
            var field = SkillSchemas.Def.GetField(fieldName);

            Assert.NotNull(field);
            Assert.Equal(expectedKind, field!.Kind);
            Assert.Equal(expectedRequired, field.Required);
        }

        // -----------------------------------------------------------------
        // 加载期校验：use_condition 走内建 expr_parsable；budget_note 走内建 field_type
        // -----------------------------------------------------------------

        private const string ValidHeader =
            "\"id\": \"skill.n3_1_cov\", \"school\": \"skill.school.n3_1_cov\", \"kind\": \"active\", " +
            "\"range\": 0, \"cast_time\": 0, \"respects_gcd\": true, " +
            "\"target_shape_ref\": \"target.n3_1_cov\", \"effects\": []";

        /// <summary>直接拼 JSON 文本（而不是用 <c>J</c> 构造器）以便任意注入待测字段，复用
        /// <see cref="SkillWorldBuilder"/> 同一份表注册与已配置好的 <c>ExprSchema</c>——
        /// <c>SkillWorldBuilder.Validate()</c> 本身未配置 <c>ExprSchema</c>（见该方法判断记录，
        /// <c>expr_parsable</c> 未配置时只降级为 Warning），本文件需要真正触发 Error 级判定，因此
        /// 自行按 <c>Tests.Rules.Ai.AiContentValidationRuleTests</c> 同一惯例装配
        /// <c>Core.Rules.ExprHost.RulesExprSchema.Base</c>。</summary>
        private static ValidationReport ValidateRaw(string extraFieldsJson)
        {
            var rowJson = "{" + ValidHeader + (string.IsNullOrEmpty(extraFieldsJson) ? "" : ", " + extraFieldsJson) + "}";
            var rowsJson = "[" + rowJson + "]";
            var envelope = "{\"table\": \"skill.def\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

            var source = new InMemoryDataSource().Add(SkillSchemas.Def.Name, envelope);
            var registry = new DataRegistry(source, SkillWorldBuilder.CreateBus(), new DataRegistryOptions
            {
                FailOnUnknownTable = false,
                ExprSchema = Core.Rules.ExprHost.RulesExprSchema.Base,
            });
            registry.RegisterSchema(SkillSchemas.Def);
            return registry.LoadAll();
        }

        [Fact]
        public void UseCondition_ValidSelfGroupExpr_PassesWithoutExprParsableError()
        {
            var report = ValidateRaw("\"use_condition\": \"self.hp_pct > 0.5\"");

            Assert.DoesNotContain(report.Issues, i => i.Check == "expr_parsable" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void UseCondition_UnparsableExpr_ReportsExprParsableError()
        {
            var report = ValidateRaw("\"use_condition\": \"(( not valid\"");

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "expr_parsable" && i.Field == "use_condition" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void UseCondition_Omitted_Passes()
        {
            var report = ValidateRaw(extraFieldsJson: "");

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void BudgetNote_StringValue_Passes()
        {
            var report = ValidateRaw("\"budget_note\": \"故意超模：终极技能设计意图如此\"");

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void BudgetNote_WrongType_ReportsFieldTypeError()
        {
            var report = ValidateRaw("\"budget_note\": 123");

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "budget_note");
        }

        [Fact]
        public void BudgetNote_Omitted_Passes()
        {
            var report = ValidateRaw(extraFieldsJson: "");

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }
    }
}
