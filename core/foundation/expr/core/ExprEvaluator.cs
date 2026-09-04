using System;

namespace Core.Foundation.Expr
{
    /// <summary>
    /// Expr 求值器（见 01_分层与依赖.md L0 模块表"条件表达式求值器"契约名，04 第 6.3、6.4 节
    /// 求值规则）：同步调用，不发事件。<c>and</c>/<c>or</c> 短路，被跳过的子表达式不触发
    /// <see cref="IExprHost.Query"/>；宿主 <c>Query</c> 抛异常按 6.4 节处理——记一条错误，
    /// 整个表达式判定为 <c>false</c>，不向调用方抛出。
    /// </summary>
    public static class ExprEvaluator
    {
        public static ExprValue Evaluate(ExprNode node, IExprHost host, IExprDiagnostics diagnostics)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));

            try
            {
                return EvaluateCore(node, host, diagnostics);
            }
            catch (ExprHostFailureSignal)
            {
                // 宿主 Query 内部异常：见 04 第 6.4 节，视为"该表达式"（整个顶层表达式，不只是
                // 触发异常的那一个子引用）求值为 false；异常已在抛出点记为一条诊断错误，
                // 这里只负责把内部控制流信号收敛为返回值，不向调用方抛出。
                return ExprValue.OfBool(false);
            }
        }

        /// <summary>仅用于把"宿主 Query 抛异常"从任意递归深度收敛回 <see cref="Evaluate"/> 顶层的内部控制流信号，不对外暴露。</summary>
        private sealed class ExprHostFailureSignal : Exception
        {
        }

        /// <summary>便捷求值：结果非 Bool 时记一条错误并返回 false。</summary>
        public static bool EvaluateBool(ExprNode node, IExprHost host, IExprDiagnostics diagnostics)
        {
            var value = Evaluate(node, host, diagnostics);
            if (value.Kind != ExprValueKind.Bool)
            {
                diagnostics.Error($"Expr 求值结果不是 Bool（实际为 {value.Kind}），按 false 处理");
                return false;
            }
            return value.AsBool;
        }

        private static ExprValue EvaluateCore(ExprNode node, IExprHost host, IExprDiagnostics diagnostics)
        {
            switch (node)
            {
                case ExprLiteralNode literal:
                    return literal.Value;

                case ExprReferenceNode reference:
                    return EvaluateReference(reference, host, diagnostics);

                case ExprNotNode notNode:
                {
                    var operandValue = EvaluateCore(notNode.Operand, host, diagnostics);
                    if (operandValue.Kind != ExprValueKind.Bool)
                    {
                        diagnostics.Error($"not 操作数不是 Bool（实际为 {operandValue.Kind}），表达式判定为 false");
                        return ExprValue.OfBool(false);
                    }
                    return ExprValue.OfBool(!operandValue.AsBool);
                }

                case ExprAndNode andNode:
                {
                    // 短路：一旦某个操作数为 false 立即返回，后续操作数（含其中的引用）不再求值。
                    foreach (var operand in andNode.Operands)
                    {
                        var value = EvaluateCore(operand, host, diagnostics);
                        if (value.Kind != ExprValueKind.Bool)
                        {
                            diagnostics.Error($"and 操作数不是 Bool（实际为 {value.Kind}），表达式判定为 false");
                            return ExprValue.OfBool(false);
                        }
                        if (!value.AsBool)
                        {
                            return ExprValue.OfBool(false);
                        }
                    }
                    return ExprValue.OfBool(true);
                }

                case ExprOrNode orNode:
                {
                    // 短路：一旦某个操作数为 true 立即返回，后续操作数（含其中的引用）不再求值。
                    foreach (var operand in orNode.Operands)
                    {
                        var value = EvaluateCore(operand, host, diagnostics);
                        if (value.Kind != ExprValueKind.Bool)
                        {
                            diagnostics.Error($"or 操作数不是 Bool（实际为 {value.Kind}），表达式判定为 false");
                            return ExprValue.OfBool(false);
                        }
                        if (value.AsBool)
                        {
                            return ExprValue.OfBool(true);
                        }
                    }
                    return ExprValue.OfBool(false);
                }

                case ExprCompareNode compareNode:
                    return EvaluateCompare(compareNode, host, diagnostics);

                default:
                    diagnostics.Error($"未知的 ExprNode 类型：{node.GetType().Name}");
                    return ExprValue.OfBool(false);
            }
        }

        private static ExprValue EvaluateReference(ExprReferenceNode reference, IExprHost host, IExprDiagnostics diagnostics)
        {
            var args = new ExprValue[reference.Args.Count];
            for (int i = 0; i < reference.Args.Count; i++)
            {
                args[i] = EvaluateCore(reference.Args[i], host, diagnostics);
            }

            try
            {
                return host.Query(reference.Group, reference.Key, args);
            }
            catch (Exception ex)
            {
                diagnostics.Error($"宿主 Query(\"{reference.Group}\", \"{reference.Key}\") 抛出异常：{ex.Message}", ex);
                throw new ExprHostFailureSignal();
            }
        }

        private static ExprValue EvaluateCompare(ExprCompareNode node, IExprHost host, IExprDiagnostics diagnostics)
        {
            var left = EvaluateCore(node.Left, host, diagnostics);
            var right = EvaluateCore(node.Right, host, diagnostics);

            if (left.IsNumeric && right.IsNumeric)
            {
                return ExprValue.OfBool(CompareNumeric(left.ToDouble(), right.ToDouble(), node.Op));
            }

            if (left.Kind != right.Kind)
            {
                diagnostics.Error($"比较两侧类型不匹配：{left.Kind} 与 {right.Kind}，表达式判定为 false");
                return ExprValue.OfBool(false);
            }

            bool eqOnly = node.Op == ExprCompareOp.Eq || node.Op == ExprCompareOp.Ne;

            switch (left.Kind)
            {
                case ExprValueKind.Bool:
                    if (!eqOnly)
                    {
                        diagnostics.Error("Bool 只支持 ==/!= 比较，表达式判定为 false");
                        return ExprValue.OfBool(false);
                    }
                    return ExprValue.OfBool((left.AsBool == right.AsBool) == (node.Op == ExprCompareOp.Eq));

                case ExprValueKind.String:
                    if (!eqOnly)
                    {
                        diagnostics.Error("String 只支持 ==/!= 比较，表达式判定为 false");
                        return ExprValue.OfBool(false);
                    }
                    var strEq = string.Equals(left.AsString, right.AsString, StringComparison.Ordinal);
                    return ExprValue.OfBool(strEq == (node.Op == ExprCompareOp.Eq));

                case ExprValueKind.Id:
                    if (!eqOnly)
                    {
                        diagnostics.Error("Id 只支持 ==/!= 比较，表达式判定为 false");
                        return ExprValue.OfBool(false);
                    }
                    var idEq = left.AsId.Equals(right.AsId);
                    return ExprValue.OfBool(idEq == (node.Op == ExprCompareOp.Eq));

                default:
                    diagnostics.Error($"不支持的比较类型：{left.Kind}，表达式判定为 false");
                    return ExprValue.OfBool(false);
            }
        }

        private static bool CompareNumeric(double a, double b, ExprCompareOp op)
        {
            switch (op)
            {
                case ExprCompareOp.Eq: return a == b;
                case ExprCompareOp.Ne: return a != b;
                case ExprCompareOp.Gt: return a > b;
                case ExprCompareOp.Ge: return a >= b;
                case ExprCompareOp.Lt: return a < b;
                case ExprCompareOp.Le: return a <= b;
                default: throw new ArgumentOutOfRangeException(nameof(op), op, "未知比较运算符");
            }
        }
    }
}
