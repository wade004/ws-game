using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Xunit;

namespace Tests.Foundation.Expr
{
    /// <summary>
    /// ADR-0015（点分标识符归类以 <see cref="IExprSchema"/> 登记表为准）的专项用例：
    /// 04 文档原始示例逐字通过、同一文本在不同登记表下归类不同、未登记引用带参数列表报解析
    /// 错误、疑似引用拼写错误的警告、以及 <see cref="ExprIssue.Severity"/> 的过滤与
    /// <see cref="ExprValidator.HasErrors"/>。见 README"判断记录"第 2 条。
    /// </summary>
    public class ExprAdr0015Tests
    {
        // ---------- 04 文档示例逐字通过（>= 7 条） ----------

        [Fact]
        public void Example_SelfHasAura_SkillArgIsIdLiteral()
        {
            var schema = new ExprSchema().Register("self", "has_aura", ExprValueKind.Bool, ExprValueKind.Id);

            var node = Assert.IsType<ExprReferenceNode>(ExprParser.Parse("self.has_aura(skill.aura.burning)", schema));
            Assert.Equal("self", node.Group);
            Assert.Equal("has_aura", node.Key);
            var arg = Assert.IsType<ExprLiteralNode>(Assert.Single(node.Args));
            Assert.Equal(ExprValueKind.Id, arg.Value.Kind);
            Assert.Equal(new Id("skill.aura.burning"), arg.Value.AsId);

            var host = new FakeHost().Set("self", "has_aura",
                args => ExprValue.OfBool(args[0].AsId.Equals(new Id("skill.aura.burning"))));
            Assert.True(ExprEvaluator.EvaluateBool(node, host, new ExprDiagnosticsRecorder()));
        }

        [Fact]
        public void Example_TargetFactionEqualsFacWildlife_RightIsIdLiteral()
        {
            var schema = new ExprSchema().Register("target", "faction", ExprValueKind.Id);

            var node = Assert.IsType<ExprCompareNode>(ExprParser.Parse("target.faction == fac.wildlife", schema));
            Assert.IsType<ExprReferenceNode>(node.Left);
            var right = Assert.IsType<ExprLiteralNode>(node.Right);
            Assert.Equal(ExprValueKind.Id, right.Value.Kind);
            Assert.Equal(new Id("fac.wildlife"), right.Value.AsId);

            var host = new FakeHost().Set("target", "faction", ExprValue.OfId(new Id("fac.wildlife")));
            Assert.True(ExprEvaluator.EvaluateBool(node, host, new ExprDiagnosticsRecorder()));
        }

        [Fact]
        public void Example_WorldGet_BridgeRepairedArgIsIdLiteral()
        {
            // world 既是分组也是内容 id 的合法 domain（04 第 2.2 节）：world.get 登记为引用，
            // world.bridge.repaired 不登记 -> 应解析成 Id 字面量参数，而不是零参引用（ADR-0015）。
            var schema = new ExprSchema().Register("world", "get", ExprValueKind.Bool, ExprValueKind.Id);

            var node = Assert.IsType<ExprReferenceNode>(ExprParser.Parse("world.get(world.bridge.repaired)", schema));
            Assert.Equal("world", node.Group);
            Assert.Equal("get", node.Key);
            var arg = Assert.IsType<ExprLiteralNode>(Assert.Single(node.Args));
            Assert.Equal(ExprValueKind.Id, arg.Value.Kind);
            Assert.Equal(new Id("world.bridge.repaired"), arg.Value.AsId);

            var host = new FakeHost().Set("world", "get",
                args => ExprValue.OfBool(args[0].AsId.Equals(new Id("world.bridge.repaired"))));
            Assert.True(ExprEvaluator.EvaluateBool(node, host, new ExprDiagnosticsRecorder()));
        }

        [Fact]
        public void Example_QuestIsActive_DeliverLetterArgIsIdLiteral()
        {
            // quest 同理：quest.is_active 登记为引用，quest.deliver_letter 不登记 -> Id 字面量。
            var schema = new ExprSchema().Register("quest", "is_active", ExprValueKind.Bool, ExprValueKind.Id);

            var node = Assert.IsType<ExprReferenceNode>(ExprParser.Parse("quest.is_active(quest.deliver_letter)", schema));
            var arg = Assert.IsType<ExprLiteralNode>(Assert.Single(node.Args));
            Assert.Equal(ExprValueKind.Id, arg.Value.Kind);
            Assert.Equal(new Id("quest.deliver_letter"), arg.Value.AsId);

            var host = new FakeHost().Set("quest", "is_active",
                args => ExprValue.OfBool(args[0].AsId.Equals(new Id("quest.deliver_letter"))));
            Assert.True(ExprEvaluator.EvaluateBool(node, host, new ExprDiagnosticsRecorder()));
        }

        [Fact]
        public void Example_PlayerHasItem_TownKeyArgIsIdLiteral()
        {
            var schema = new ExprSchema().Register("player", "has_item", ExprValueKind.Bool, ExprValueKind.Id);

            var node = Assert.IsType<ExprReferenceNode>(ExprParser.Parse("player.has_item(item.town_key)", schema));
            var arg = Assert.IsType<ExprLiteralNode>(Assert.Single(node.Args));
            Assert.Equal(ExprValueKind.Id, arg.Value.Kind);
            Assert.Equal(new Id("item.town_key"), arg.Value.AsId);

            var host = new FakeHost().Set("player", "has_item",
                args => ExprValue.OfBool(args[0].AsId.Equals(new Id("item.town_key"))));
            Assert.True(ExprEvaluator.EvaluateBool(node, host, new ExprDiagnosticsRecorder()));
        }

        [Fact]
        public void Example_EnemiesCountInRange_IntArgEvaluatesThroughHost()
        {
            var schema = new ExprSchema().Register("enemies", "count_in_range", ExprValueKind.Int, ExprValueKind.Int);

            var node = Assert.IsType<ExprReferenceNode>(ExprParser.Parse("enemies.count_in_range(8)", schema));
            var arg = Assert.IsType<ExprLiteralNode>(Assert.Single(node.Args));
            Assert.Equal(ExprValue.OfInt(8), arg.Value);

            var host = new FakeHost().Set("enemies", "count_in_range", args => ExprValue.OfInt(args[0].AsInt + 1));
            var result = ExprEvaluator.Evaluate(node, host, new ExprDiagnosticsRecorder());
            Assert.Equal(ExprValue.OfInt(9), result);
        }

        [Fact]
        public void Example_TimeSinceCombatStart_CompareEvaluatesThroughHost()
        {
            var schema = new ExprSchema().Register("time", "since_combat_start", ExprValueKind.Number);

            var node = Assert.IsType<ExprCompareNode>(ExprParser.Parse("time.since_combat_start > 5", schema));

            var hostAbove = new FakeHost().Set("time", "since_combat_start", ExprValue.OfNumber(6.0));
            Assert.True(ExprEvaluator.EvaluateBool(node, hostAbove, new ExprDiagnosticsRecorder()));

            var hostBelow = new FakeHost().Set("time", "since_combat_start", ExprValue.OfNumber(3.0));
            Assert.False(ExprEvaluator.EvaluateBool(node, hostBelow, new ExprDiagnosticsRecorder()));
        }

        // ---------- 同一文本在不同登记表下归类不同 ----------

        [Fact]
        public void SameText_ClassifiedDifferently_DependingOnSchemaRegistration()
        {
            const string text = "quest.deliver_letter";

            var withoutRegistration = new ExprSchema();
            var literalNode = Assert.IsType<ExprLiteralNode>(ExprParser.Parse(text, withoutRegistration));
            Assert.Equal(ExprValueKind.Id, literalNode.Value.Kind);
            Assert.Equal(new Id("quest.deliver_letter"), literalNode.Value.AsId);

            var withRegistration = new ExprSchema().Register("quest", "deliver_letter", ExprValueKind.Id);
            var referenceNode = Assert.IsType<ExprReferenceNode>(ExprParser.Parse(text, withRegistration));
            Assert.Equal("quest", referenceNode.Group);
            Assert.Equal("deliver_letter", referenceNode.Key);
            Assert.Empty(referenceNode.Args);
        }

        // ---------- 未登记引用带参数列表 -> 解析错误 ----------

        [Fact]
        public void UnregisteredIdentifier_WithArgList_ThrowsParseError()
        {
            var schema = new ExprSchema(); // 什么都不登记
            var ex = Assert.Throws<ExprParseException>(() => ExprParser.Parse("quest.deliver_letter(1)", schema));
            Assert.Contains("未登记的引用不能带参数列表", ex.Message);
        }

        [Fact]
        public void UnregisteredIdentifier_WithArgList_InsideAnotherReference_ThrowsParseError()
        {
            // 未登记标识符出现在已登记引用的参数位置时，同样的规则也适用（不因位置改变判定方式）。
            var schema = new ExprSchema().Register("self", "has_aura", ExprValueKind.Bool, ExprValueKind.Id);
            var ex = Assert.Throws<ExprParseException>(
                () => ExprParser.Parse("self.has_aura(skill.aura.burning(1))", schema));
            Assert.Contains("未登记的引用不能带参数列表", ex.Message);
        }

        // ---------- 疑似引用拼写错误警告 ----------

        [Fact]
        public void SuspiciousReferenceSpelling_WarningReported_AndCompareStillErrors()
        {
            // 只登记 self.hp_pct；文本写成 self.hp_pctt（多打一个 t）。第一段 "self" 与分组同名，
            // 但 "self.hp_pctt" 未登记 -> 解析成 Id 字面量，校验期应报一条 Warning 提示"疑似拼错"，
            // 同时 Id 与 Number 比较本身类型不匹配，仍应报一条 Error（Warning 不能掩盖真正的类型错误）。
            var schema = new ExprSchema().Register("self", "hp_pct", ExprValueKind.Number);

            var node = Assert.IsType<ExprCompareNode>(ExprParser.Parse("self.hp_pctt < 0.3", schema));
            var left = Assert.IsType<ExprLiteralNode>(node.Left);
            Assert.Equal(ExprValueKind.Id, left.Value.Kind);
            Assert.Equal(new Id("self.hp_pctt"), left.Value.AsId);

            var issues = ExprValidator.Validate(node, schema);

            Assert.Contains(issues, i =>
                i.Kind == ExprIssueKind.SuspiciousReferenceSpelling && i.Severity == ExprIssueSeverity.Warning);
            Assert.Contains(issues, i =>
                i.Kind == ExprIssueKind.CompareTypeMismatch && i.Severity == ExprIssueSeverity.Error);
        }

        [Fact]
        public void SuspiciousReferenceSpelling_NotReported_WhenDomainIsNotAKnownGroup()
        {
            // "fac" 不是九个分组之一，即便 fac.wildlife 未登记为引用，也不该触发"疑似拼错"警告
            // ——和分组同名才是这条警告的触发条件（见 ADR-0015）。
            var schema = new ExprSchema().Register("target", "faction", ExprValueKind.Id);
            var node = ExprParser.Parse("target.faction == fac.wildlife", schema);

            var issues = ExprValidator.Validate(node, schema);

            Assert.DoesNotContain(issues, i => i.Kind == ExprIssueKind.SuspiciousReferenceSpelling);
        }

        [Fact]
        public void SuspiciousReferenceSpelling_NotReported_WhenGroupKeyIsRegistered()
        {
            // 正对照：先用一份"只登记 world.get"的 schema 解析出参数是 Id 字面量的 AST
            // （world.bridge.repaired 未登记，符合 ADR-0015 的字面量归类）；
            // 再换一份把 world.bridge.repaired 也登记为引用的 schema 去校验同一棵 AST——
            // AST 本身不会因为换 schema 重新分类（Parse 时已经固定），但校验期看到
            // "world.bridge.repaired" 已经登记，就不该再报"疑似拼错"警告。
            var parseSchema = new ExprSchema().Register("world", "get", ExprValueKind.Bool, ExprValueKind.Id);
            var node = ExprParser.Parse("world.get(world.bridge.repaired)", parseSchema);

            var validateSchema = new ExprSchema()
                .Register("world", "get", ExprValueKind.Bool, ExprValueKind.Id)
                .Register("world", "bridge.repaired", ExprValueKind.Id);
            var issues = ExprValidator.Validate(node, validateSchema);

            Assert.DoesNotContain(issues, i => i.Kind == ExprIssueKind.SuspiciousReferenceSpelling);
        }

        [Fact]
        public void SuspiciousReferenceSpelling_NotReported_WhenArgExpectedKindIsId()
        {
            // 消费方反馈 E6 根治：quest.is_available(quest.sample_hunt) ——quest.sample_hunt 是
            // quest.def 表里真实存在的内容 id（不是拼写错误），域名 "quest" 恰好与分组同名，按
            // ADR-0015 归类为 Id 字面量。此前的"疑似引用拼写错误"规则对此会误报——本用例是那次
            // 误报的最小复现，同时验证根治后不再误报：quest.is_available 的实参签名类型已登记为
            // Id，该位置上域名与分组同名的 Id 字面量不应再触发警告。
            var schema = new ExprSchema().Register("quest", "is_available", ExprValueKind.Bool, ExprValueKind.Id);
            var node = ExprParser.Parse("quest.is_available(quest.sample_hunt)", schema);

            var issues = ExprValidator.Validate(node, schema);

            Assert.DoesNotContain(issues, i => i.Kind == ExprIssueKind.SuspiciousReferenceSpelling);
            Assert.Empty(issues);
        }

        [Fact]
        public void SuspiciousReferenceSpelling_StillReported_WhenArgExpectedKindIsNotId()
        {
            // 反例：豁免只对参数期望类型确实是 Id 的位置生效，不是"只要在参数位置就不检查"。
            // 构造一个期望 Bool 的参数位置，塞一个域名与分组同名、未登记的 Id 字面量
            // （quest.sample_hunt）——类型本身就不匹配（触发 ArgTypeMismatch），"疑似拼写错误"
            // 警告也应照常触发，因为这个位置压根不期望 Id，更可能真的是笔误。
            var schema = new ExprSchema().Register("quest", "some_flag", ExprValueKind.Bool, ExprValueKind.Bool);
            var node = ExprParser.Parse("quest.some_flag(quest.sample_hunt)", schema);

            var issues = ExprValidator.Validate(node, schema);

            Assert.Contains(issues, i => i.Kind == ExprIssueKind.SuspiciousReferenceSpelling);
            Assert.Contains(issues, i => i.Kind == ExprIssueKind.ArgTypeMismatch);
        }

        // ---------- ExprIssue.Severity 过滤 / HasErrors ----------

        [Fact]
        public void HasErrors_FalseWhenOnlyWarningsPresent()
        {
            var schema = new ExprSchema().Register("self", "hp_pct", ExprValueKind.Number);
            // 顶层裸 Id 字面量：不是 and/or/not 的操作数，不触发 LogicalOperandNotBool，
            // 只触发一条 SuspiciousReferenceSpelling 警告。
            var node = ExprParser.Parse("self.hp_pctt", schema);

            var issues = ExprValidator.Validate(node, schema);

            var issue = Assert.Single(issues);
            Assert.Equal(ExprIssueSeverity.Warning, issue.Severity);
            Assert.False(ExprValidator.HasErrors(issues));
        }

        [Fact]
        public void HasErrors_TrueWhenErrorPresentAlongsideWarning()
        {
            var schema = new ExprSchema().Register("self", "hp_pct", ExprValueKind.Number);
            var node = ExprParser.Parse("self.hp_pctt and true", schema);

            var issues = ExprValidator.Validate(node, schema);

            Assert.Contains(issues, i => i.Severity == ExprIssueSeverity.Warning);
            Assert.Contains(issues, i => i.Severity == ExprIssueSeverity.Error);
            Assert.True(ExprValidator.HasErrors(issues));
        }

        [Fact]
        public void Issues_CanBeFilteredBySeverity()
        {
            var schema = new ExprSchema().Register("self", "hp_pct", ExprValueKind.Number);
            var node = ExprParser.Parse("self.hp_pctt and true", schema);

            var issues = ExprValidator.Validate(node, schema);

            var errorsOnly = issues.Where(i => i.Severity == ExprIssueSeverity.Error).ToList();
            var warningsOnly = issues.Where(i => i.Severity == ExprIssueSeverity.Warning).ToList();

            Assert.NotEmpty(errorsOnly);
            Assert.NotEmpty(warningsOnly);
            Assert.Equal(issues.Count, errorsOnly.Count + warningsOnly.Count);
        }
    }
}
