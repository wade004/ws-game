using System.Linq;
using Core.Foundation.Expr;
using Xunit;

namespace Tests.Foundation.Expr
{
    /// <summary>
    /// T-N5-2：<see cref="ExprReferenceCollector"/> 只读遍历入口的单测——覆盖"收集嵌套/函数调用
    /// 参数里的引用"与"不执行"两条验收标准（见 11 第 11 节风险段、任务派发提示词验收标准）。
    /// 全部用例统一用 <see cref="PermissiveExprSchema.Instance"/> 解析（本类型场景本就是"不知道
    /// 完整业务 schema 仍要遍历"，见该类型判断记录），不需要像 <see cref="TestSchema"/> 那样逐条
    /// 登记签名。
    /// </summary>
    public class ExprReferenceCollectorTests
    {
        private static ExprNode Parse(string text) => ExprParser.Parse(text, PermissiveExprSchema.Instance);

        [Fact]
        public void Collect_SingleReference_ReturnsThatOne()
        {
            var refs = ExprReferenceCollector.Collect(Parse("self.hp_pct"));

            Assert.Single(refs);
            Assert.Equal("self", refs[0].Group);
            Assert.Equal("hp_pct", refs[0].Key);
            Assert.Empty(refs[0].Args);
        }

        [Fact]
        public void Collect_Literal_ReturnsEmpty()
        {
            // "10" 是数字字面量，没有任何引用节点。
            var refs = ExprReferenceCollector.Collect(Parse("10"));

            Assert.Empty(refs);
        }

        [Fact]
        public void Collect_ReferenceWithNestedReferenceArg_ReturnsBothOuterAndInner()
        {
            // self.stat(stat.strength)：外层 self.stat 调用，参数本身在宽松 schema 下也被解析成
            // 一个（零参）引用节点 stat.strength——04 第 5 节"属性无消费者"正是要从这个参数位置
            // 摘出被引用的属性 id（见 StatDefinitionConsumerValidationRule 判断记录）。
            var refs = ExprReferenceCollector.Collect(Parse("self.stat(stat.strength)"));

            Assert.Equal(2, refs.Count);
            Assert.Equal(("self", "stat"), (refs[0].Group, refs[0].Key));
            Assert.Single(refs[0].Args);
            Assert.Equal(("stat", "strength"), (refs[1].Group, refs[1].Key));
        }

        [Fact]
        public void Collect_MultiLevelNestedFunctionCallArgs_CollectsAllLevels()
        {
            // self.stat(target.stat(stat.agility)) 人为构造的多层嵌套（业务上不会这样写，但用来
            // 验证"函数调用参数里的引用"递归到底，不只展开一层）。
            var refs = ExprReferenceCollector.Collect(Parse("self.stat(target.stat(stat.agility))"));

            Assert.Equal(3, refs.Count);
            Assert.Equal(("self", "stat"), (refs[0].Group, refs[0].Key));
            Assert.Equal(("target", "stat"), (refs[1].Group, refs[1].Key));
            Assert.Equal(("stat", "agility"), (refs[2].Group, refs[2].Key));
        }

        [Fact]
        public void Collect_CompareNode_WalksBothSides()
        {
            var refs = ExprReferenceCollector.Collect(Parse("self.stat(stat.strength) > target.stat(stat.armor)"));

            var pairs = refs.Select(r => (r.Group, r.Key)).ToList();
            Assert.Contains(("self", "stat"), pairs);
            Assert.Contains(("stat", "strength"), pairs);
            Assert.Contains(("target", "stat"), pairs);
            Assert.Contains(("stat", "armor"), pairs);
            Assert.Equal(4, refs.Count);
        }

        [Fact]
        public void Collect_AndOrNot_WalksEveryOperand()
        {
            var refs = ExprReferenceCollector.Collect(
                Parse("not self.is_alive and (self.stat(stat.strength) > 1 or target.stat(stat.agility) > 1)"));

            var pairs = refs.Select(r => (r.Group, r.Key)).ToList();
            Assert.Contains(("self", "is_alive"), pairs);
            Assert.Contains(("self", "stat"), pairs);
            Assert.Contains(("stat", "strength"), pairs);
            Assert.Contains(("target", "stat"), pairs);
            Assert.Contains(("stat", "agility"), pairs);
        }

        [Fact]
        public void Collect_DoesNotRequireOrCallAnyHost()
        {
            // "不执行"验收标准的直接体现：Collect 签名只接受 ExprNode，没有任何 IExprHost/
            // IExprDiagnostics 参数可传——即便表达式引用了运行时才有意义的 group.key（如
            // enemies.count_in_range(3)），本方法也只是把节点摘出来，不会尝试求值（求值需要
            // 宿主，这里从未构造、也不可能传入任何宿主）。
            var refs = ExprReferenceCollector.Collect(Parse("enemies.count_in_range(3) > 0"));

            Assert.Single(refs);
            Assert.Equal("enemies", refs[0].Group);
            Assert.Equal("count_in_range", refs[0].Key);
        }
    }
}
