using System;
using System.Collections.Generic;

namespace Core.Foundation.Expr
{
    /// <summary>
    /// 分阶段落地计划 T-N5-2（04 第 5 节"属性无消费者"一行"……或任一表达式引用"；11 第 6 节
    /// 风险段"现有 ExprValidator 只做引用登记消歧，未提供'列出全部属性引用'的出口"）：只读遍历
    /// 入口，深度优先收集 <see cref="ExprNode"/> 语法树里出现过的全部 <see cref="ExprReferenceNode"/>
    /// （含嵌套在其它引用节点参数列表里的引用，以及比较/<c>and</c>/<c>or</c>/<c>not</c> 各操作数
    /// 子树里的引用），按遇到顺序（深度优先、每个节点内部按其操作数/参数的书写顺序）返回。
    /// <para>
    /// <b>只读，不执行</b>（硬性规则）：本类型只做语法树的结构遍历，不调用
    /// <see cref="ExprEvaluator"/>、不查询任何 <see cref="IExprHost"/>，也不做
    /// <see cref="ExprValidator"/> 那样的静态类型推断——纯粹把树上已经存在的
    /// <see cref="ExprReferenceNode"/> 节点摘出来，节点本身在 <see cref="ExprParser.Parse"/> 阶段
    /// 已经构造好，本类型不新增任何求值副作用。
    /// </para>
    /// <para>
    /// 判断记录（返回形状）：任务派发提示词给出的示意签名是
    /// <c>Collect(ExprNode) -&gt; IReadOnlyList&lt;(group, key)&gt;</c>；本实现改为返回完整的
    /// <see cref="ExprReferenceNode"/>（而不是拆开的 <c>(group, key)</c> 元组）——调用方（如
    /// <c>StatDefinitionConsumerValidationRule</c> 扫描 <c>self.stat(&lt;属性 id&gt;)</c>/
    /// <c>target.stat(&lt;属性 id&gt;)</c> 引用）同时需要 <see cref="ExprReferenceNode.Args"/>
    /// 才能取出被引用的具体属性 id，只给 <c>(group, key)</c> 会丢失这个信息、逼调用方另外重新
    /// 遍历一遍原始语法树。<see cref="ExprReferenceNode"/> 本身已经携带 <c>Group</c>/<c>Key</c>/
    /// <c>Args</c> 三者，是示意元组的严格超集，采纳为最终形状（设计层裁定：待确认，本任务先按
    /// 此实现，理由已如实记录在此）。
    /// </para>
    /// </summary>
    public static class ExprReferenceCollector
    {
        public static IReadOnlyList<ExprReferenceNode> Collect(ExprNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));

            var result = new List<ExprReferenceNode>();
            Walk(node, result);
            return result;
        }

        private static void Walk(ExprNode node, List<ExprReferenceNode> result)
        {
            switch (node)
            {
                case ExprReferenceNode reference:
                    result.Add(reference);
                    for (var i = 0; i < reference.Args.Count; i++)
                    {
                        // 引用节点的参数本身可以是任意子表达式（包括另一个引用节点，即"函数调用
                        // 参数里嵌套引用"），一并递归收集——见类型顶部判断记录第一段。
                        Walk(reference.Args[i], result);
                    }
                    break;

                case ExprNotNode notNode:
                    Walk(notNode.Operand, result);
                    break;

                case ExprAndNode andNode:
                    for (var i = 0; i < andNode.Operands.Count; i++)
                    {
                        Walk(andNode.Operands[i], result);
                    }
                    break;

                case ExprOrNode orNode:
                    for (var i = 0; i < orNode.Operands.Count; i++)
                    {
                        Walk(orNode.Operands[i], result);
                    }
                    break;

                case ExprCompareNode compareNode:
                    Walk(compareNode.Left, result);
                    Walk(compareNode.Right, result);
                    break;

                case ExprLiteralNode:
                    // 字面量是叶子节点，没有子结构可遍历。
                    break;

                default:
                    // 防御：ExprNode 目前只有以上六个具体子类（见该类型注释），走到这里说明未来
                    // 新增了子类却未同步本方法——不静默吞掉，也不抛异常中断调用方的批量扫描，按
                    // "未知节点没有已知子结构"处理（不递归），与新增 ExprTokenKind 分支时的既有
                    // 保守处理惯例一致。
                    break;
            }
        }
    }
}
