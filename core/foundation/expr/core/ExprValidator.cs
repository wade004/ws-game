using System;
using System.Collections.Generic;

namespace Core.Foundation.Expr
{
    /// <summary>
    /// Expr 静态校验器（见 04 第 5 节"表达式可解析"、第 6.4 节"解析期"错误）：
    /// 未知分组、未知 key、参数个数/类型不匹配、比较两侧类型不匹配、Bool/String/Id 参与
    /// &lt; &gt; 等非法比较、and/or/not 操作数非 Bool。空列表即通过。
    /// 只做静态类型推断（不调用任何宿主），Int 与 Number 互相兼容（见 04 第 6.3 节）。
    /// </summary>
    public static class ExprValidator
    {
        public static IReadOnlyList<ExprIssue> Validate(ExprNode node, IExprSchema schema)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (schema == null) throw new ArgumentNullException(nameof(schema));

            var issues = new List<ExprIssue>();
            InferKind(node, schema, issues);
            return issues;
        }

        /// <summary>便捷判断：<paramref name="issues"/> 里是否存在至少一条 <see cref="ExprIssueSeverity.Error"/>
        /// 级别的问题——调用方通常只应因为 Error 而拒绝内容合入，<see cref="ExprIssueSeverity.Warning"/>
        /// 不阻断（见 ADR-0015）。</summary>
        public static bool HasErrors(IReadOnlyList<ExprIssue> issues)
        {
            if (issues == null) throw new ArgumentNullException(nameof(issues));

            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Severity == ExprIssueSeverity.Error) return true;
            }
            return false;
        }

        private static ExprValueKind? InferKind(ExprNode node, IExprSchema schema, List<ExprIssue> issues)
        {
            switch (node)
            {
                case ExprLiteralNode literal:
                    CheckSuspiciousIdLiteral(literal.Value, schema, issues);
                    return literal.Value.Kind;

                case ExprReferenceNode reference:
                    return ValidateReference(reference, schema, issues);

                case ExprNotNode notNode:
                {
                    var operandKind = InferKind(notNode.Operand, schema, issues);
                    CheckBoolOperand("not", operandKind, issues);
                    return ExprValueKind.Bool;
                }

                case ExprAndNode andNode:
                {
                    foreach (var operand in andNode.Operands)
                    {
                        CheckBoolOperand("and", InferKind(operand, schema, issues), issues);
                    }
                    return ExprValueKind.Bool;
                }

                case ExprOrNode orNode:
                {
                    foreach (var operand in orNode.Operands)
                    {
                        CheckBoolOperand("or", InferKind(operand, schema, issues), issues);
                    }
                    return ExprValueKind.Bool;
                }

                case ExprCompareNode compareNode:
                    return ValidateCompare(compareNode, schema, issues);

                default:
                    return null;
            }
        }

        private static void CheckBoolOperand(string opName, ExprValueKind? kind, List<ExprIssue> issues)
        {
            if (kind.HasValue && kind.Value != ExprValueKind.Bool)
            {
                issues.Add(new ExprIssue(ExprIssueKind.LogicalOperandNotBool,
                    $"{opName} 操作数不是 Bool：实际为 {kind.Value}"));
            }
        }

        private static ExprValueKind? ValidateReference(ExprReferenceNode reference, IExprSchema schema, List<ExprIssue> issues)
        {
            // 无论 group/key 是否已知，都先递归校验全部实参子树，尽量收集问题而不是遇错即停。
            var argKinds = new ExprValueKind?[reference.Args.Count];
            for (int i = 0; i < reference.Args.Count; i++)
            {
                argKinds[i] = InferKind(reference.Args[i], schema, issues);
            }

            if (!ExprGroups.IsKnown(reference.Group))
            {
                issues.Add(new ExprIssue(ExprIssueKind.UnknownGroup, $"未知的引用分组：\"{reference.Group}\""));
                return null;
            }

            if (!schema.TryGetSignature(reference.Group, reference.Key, out var signature))
            {
                issues.Add(new ExprIssue(ExprIssueKind.UnknownKey, $"未知的引用 key：\"{reference.Group}.{reference.Key}\""));
                return null;
            }

            if (signature.ArgKinds.Count != reference.Args.Count)
            {
                issues.Add(new ExprIssue(ExprIssueKind.ArgCountMismatch,
                    $"\"{reference.Group}.{reference.Key}\" 期望 {signature.ArgKinds.Count} 个参数，实际 {reference.Args.Count} 个"));
            }
            else
            {
                for (int i = 0; i < signature.ArgKinds.Count; i++)
                {
                    var expected = signature.ArgKinds[i];
                    var actual = argKinds[i];
                    if (actual.HasValue && !KindsCompatible(expected, actual.Value))
                    {
                        issues.Add(new ExprIssue(ExprIssueKind.ArgTypeMismatch,
                            $"\"{reference.Group}.{reference.Key}\" 第 {i + 1} 个参数期望 {expected}，实际 {actual.Value}"));
                    }
                }
            }

            return signature.ReturnKind;
        }

        private static ExprValueKind? ValidateCompare(ExprCompareNode node, IExprSchema schema, List<ExprIssue> issues)
        {
            var leftKind = InferKind(node.Left, schema, issues);
            var rightKind = InferKind(node.Right, schema, issues);

            if (!leftKind.HasValue || !rightKind.HasValue)
            {
                // 某一侧类型未知（内部已有引用错误），不再叠加比较类型错误。
                return ExprValueKind.Bool;
            }

            bool leftNumeric = IsNumeric(leftKind.Value);
            bool rightNumeric = IsNumeric(rightKind.Value);

            if (leftNumeric && rightNumeric)
            {
                return ExprValueKind.Bool; // Int 与 Number 互相可比，六种比较符均合法
            }

            if (leftKind.Value != rightKind.Value)
            {
                issues.Add(new ExprIssue(ExprIssueKind.CompareTypeMismatch,
                    $"比较两侧类型不一致：{leftKind.Value} 与 {rightKind.Value}"));
                return ExprValueKind.Bool;
            }

            // 走到这里说明两侧类型相同且不是数值类型：只能是 Bool/String/Id，三者都只支持 ==/!=。
            bool eqOnly = node.Op == ExprCompareOp.Eq || node.Op == ExprCompareOp.Ne;
            if (!eqOnly)
            {
                issues.Add(new ExprIssue(ExprIssueKind.InvalidCompareForType,
                    $"{leftKind.Value} 类型只支持 ==/!= 比较，不支持 {ExprCompareNode.OpText(node.Op)}"));
            }

            return ExprValueKind.Bool;
        }

        /// <summary>
        /// ADR-0015 的警告项：Id 字面量的域名若与九个分组之一同名，且"域名.其余段"没有在
        /// <paramref name="schema"/> 中登记为引用，大概率是调用方原本想写一个引用、却因为
        /// 该 <c>group.key</c> 未登记而被 <see cref="ExprParser"/> 按 Id 字面量归类——报一条
        /// <see cref="ExprIssueSeverity.Warning"/>（不阻断，因为"和分组同名的 Id 字面量"本身
        /// 是合法用法，见 04 第 2.2 节域名清单）。
        /// </summary>
        private static void CheckSuspiciousIdLiteral(ExprValue value, IExprSchema schema, List<ExprIssue> issues)
        {
            if (value.Kind != ExprValueKind.Id) return;

            var idText = value.AsId.Value;
            var dotIndex = idText.IndexOf('.');
            if (dotIndex < 0) return; // Id 格式保证至少一个 '.'，防御性检查。

            var domain = idText.Substring(0, dotIndex);
            var key = idText.Substring(dotIndex + 1);

            if (!ExprGroups.IsKnown(domain)) return;
            if (schema.TryGetSignature(domain, key, out _)) return;

            issues.Add(new ExprIssue(ExprIssueKind.SuspiciousReferenceSpelling, ExprIssueSeverity.Warning,
                $"疑似引用拼写错误：\"{idText}\" 的域名与分组 \"{domain}\" 同名但未登记为引用"));
        }

        private static bool IsNumeric(ExprValueKind kind) => kind == ExprValueKind.Int || kind == ExprValueKind.Number;

        private static bool KindsCompatible(ExprValueKind expected, ExprValueKind actual) =>
            expected == actual || (IsNumeric(expected) && IsNumeric(actual));
    }
}
