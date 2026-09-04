using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Xunit;

namespace Tests.Foundation.Expr
{
    public class ExprEvaluatorTests
    {
        [Fact]
        public void Evaluate_CompareIntEqual_True()
        {
            var node = ExprParser.Parse("self.hp_pct == 1");
            var host = new FakeHost().Set("self", "hp_pct", ExprValue.OfInt(1));
            var diagnostics = new ExprDiagnosticsRecorder();

            var result = ExprEvaluator.Evaluate(node, host, diagnostics);

            Assert.Equal(ExprValue.OfBool(true), result);
            Assert.Empty(diagnostics.Errors);
        }

        [Fact]
        public void Evaluate_CompareIntNumber_CrossTypeAllowed()
        {
            var node = ExprParser.Parse("self.hp_pct < 0.3");
            var host = new FakeHost().Set("self", "hp_pct", ExprValue.OfInt(0)); // Int 0 < Number 0.3
            var diagnostics = new ExprDiagnosticsRecorder();

            Assert.True(ExprEvaluator.EvaluateBool(node, host, diagnostics));
            Assert.Empty(diagnostics.Errors);
        }

        [Fact]
        public void Evaluate_And_AllTrue_ReturnsTrue()
        {
            var node = ExprParser.Parse("combat.in_combat and player.level >= 1");
            var host = new FakeHost()
                .Set("combat", "in_combat", ExprValue.OfBool(true))
                .Set("player", "level", ExprValue.OfInt(5));
            var diagnostics = new ExprDiagnosticsRecorder();

            Assert.True(ExprEvaluator.EvaluateBool(node, host, diagnostics));
        }

        [Fact]
        public void Evaluate_Or_AnyTrue_ReturnsTrue()
        {
            var node = ExprParser.Parse("combat.in_combat or player.level >= 1");
            var host = new FakeHost()
                .Set("combat", "in_combat", ExprValue.OfBool(false))
                .Set("player", "level", ExprValue.OfInt(5));
            var diagnostics = new ExprDiagnosticsRecorder();

            Assert.True(ExprEvaluator.EvaluateBool(node, host, diagnostics));
        }

        [Fact]
        public void Evaluate_Not_InvertsBool()
        {
            var node = ExprParser.Parse("not combat.in_combat");
            var host = new FakeHost().Set("combat", "in_combat", ExprValue.OfBool(true));
            var diagnostics = new ExprDiagnosticsRecorder();

            Assert.False(ExprEvaluator.EvaluateBool(node, host, diagnostics));
        }

        [Fact]
        public void Evaluate_ReferenceWithArgs_PassesEvaluatedArgValues()
        {
            var node = ExprParser.Parse("self.has_aura(skill.aura.burning)");
            IReadOnlyList<ExprValue>? seenArgs = null;
            var host = new FakeHost().Set("self", "has_aura", args =>
            {
                seenArgs = args;
                return ExprValue.OfBool(true);
            });
            var diagnostics = new ExprDiagnosticsRecorder();

            Assert.True(ExprEvaluator.EvaluateBool(node, host, diagnostics));
            Assert.NotNull(seenArgs);
            Assert.Single(seenArgs!);
            Assert.Equal(ExprValue.OfId(new Id("skill.aura.burning")), seenArgs![0]);
        }

        [Fact]
        public void Evaluate_StringEquality()
        {
            var node = ExprParser.Parse("event.school == \"fire\"");
            var host = new FakeHost().Set("event", "school", ExprValue.OfString("fire"));
            var diagnostics = new ExprDiagnosticsRecorder();

            Assert.True(ExprEvaluator.EvaluateBool(node, host, diagnostics));
        }

        [Fact]
        public void Evaluate_IdEquality()
        {
            var node = ExprParser.Parse("target.faction == fac.wildlife");
            var host = new FakeHost().Set("target", "faction", ExprValue.OfId(new Id("fac.wildlife")));
            var diagnostics = new ExprDiagnosticsRecorder();

            Assert.True(ExprEvaluator.EvaluateBool(node, host, diagnostics));
        }

        [Fact]
        public void Evaluate_TopLevelNonBoolReference_ReturnsRawValue()
        {
            var node = ExprParser.Parse("time.since_combat_start");
            var host = new FakeHost().Set("time", "since_combat_start", ExprValue.OfNumber(42.5));
            var diagnostics = new ExprDiagnosticsRecorder();

            var result = ExprEvaluator.Evaluate(node, host, diagnostics);

            Assert.Equal(ExprValue.OfNumber(42.5), result);
            Assert.Empty(diagnostics.Errors);
        }

        [Fact]
        public void EvaluateBool_NonBoolResult_RecordsErrorAndReturnsFalse()
        {
            var node = ExprParser.Parse("time.since_combat_start");
            var host = new FakeHost().Set("time", "since_combat_start", ExprValue.OfNumber(42.5));
            var diagnostics = new ExprDiagnosticsRecorder();

            var result = ExprEvaluator.EvaluateBool(node, host, diagnostics);

            Assert.False(result);
            Assert.NotEmpty(diagnostics.Errors);
        }

        // ---------- 短路（>= 2 条） ----------

        [Fact]
        public void ShortCircuit_AndFalse_DoesNotCallRightOperandHost()
        {
            var node = ExprParser.Parse("false and self.hp_pct > 0");
            var host = new FakeHost().Set("self", "hp_pct", ExprValue.OfInt(999));
            var diagnostics = new ExprDiagnosticsRecorder();

            var result = ExprEvaluator.EvaluateBool(node, host, diagnostics);

            Assert.False(result);
            Assert.Equal(0, host.CallCount("self", "hp_pct"));
        }

        [Fact]
        public void ShortCircuit_OrTrue_DoesNotCallRightOperandHost()
        {
            var node = ExprParser.Parse("true or self.hp_pct > 0");
            var host = new FakeHost().Set("self", "hp_pct", ExprValue.OfInt(999));
            var diagnostics = new ExprDiagnosticsRecorder();

            var result = ExprEvaluator.EvaluateBool(node, host, diagnostics);

            Assert.True(result);
            Assert.Equal(0, host.CallCount("self", "hp_pct"));
        }

        [Fact]
        public void ShortCircuit_AndTrue_DoesCallRightOperandHost()
        {
            // 对照组：and 左侧为 true 时不短路，右侧的 Query 必须被调用到。
            var node = ExprParser.Parse("true and self.hp_pct > 0");
            var host = new FakeHost().Set("self", "hp_pct", ExprValue.OfInt(5));
            var diagnostics = new ExprDiagnosticsRecorder();

            var result = ExprEvaluator.EvaluateBool(node, host, diagnostics);

            Assert.True(result);
            Assert.Equal(1, host.CallCount("self", "hp_pct"));
        }

        // ---------- 运行期错误（>= 2 条） ----------

        [Fact]
        public void Runtime_HostThrows_WholeExpressionIsFalseWithError()
        {
            var node = ExprParser.Parse("self.hp_pct > 0 and player.level >= 1");
            var host = new FakeHost()
                .Throws("self", "hp_pct", new InvalidOperationException("boom"))
                .Set("player", "level", ExprValue.OfInt(999));
            var diagnostics = new ExprDiagnosticsRecorder();

            var result = ExprEvaluator.EvaluateBool(node, host, diagnostics);

            Assert.False(result);
            Assert.NotEmpty(diagnostics.Errors);
            // 异常发生在 and 的第一个操作数：既然整个表达式已判定为 false，第二个操作数不应再被求值。
            Assert.Equal(0, host.CallCount("player", "level"));
        }

        [Fact]
        public void Runtime_TypeMismatch_ResultsInFalseWithError()
        {
            // 手工构造一个校验器本应拦截、但求值器仍需自行兜底的非法比较：Bool > Int。
            var node = new ExprCompareNode(
                new ExprLiteralNode(ExprValue.OfBool(true)),
                ExprCompareOp.Gt,
                new ExprLiteralNode(ExprValue.OfInt(1)));
            var host = new FakeHost();
            var diagnostics = new ExprDiagnosticsRecorder();

            var result = ExprEvaluator.Evaluate(node, host, diagnostics);

            Assert.Equal(ExprValue.OfBool(false), result);
            Assert.NotEmpty(diagnostics.Errors);
        }

        [Fact]
        public void Runtime_HostExceptionInNestedArg_AbortsWholeExpression()
        {
            var node = ExprParser.Parse("self.has_aura(target.faction) and player.level >= 1");
            var host = new FakeHost()
                .Throws("target", "faction", new InvalidOperationException("no target"))
                .Set("player", "level", ExprValue.OfInt(999));
            var diagnostics = new ExprDiagnosticsRecorder();

            var result = ExprEvaluator.EvaluateBool(node, host, diagnostics);

            Assert.False(result);
            Assert.NotEmpty(diagnostics.Errors);
            Assert.Equal(0, host.CallCount("player", "level"));
        }
    }
}
