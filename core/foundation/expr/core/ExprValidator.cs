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
            return InferKind(node, schema, issues, suppressSuspiciousIdWarning: false);
        }

        /// <summary>
        /// 判断记录（消费方反馈 E6 根治，2026-09-10，见
        /// architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E6）：<paramref name="suppressSuspiciousIdWarning"/>
        /// 只在 <see cref="ValidateReference"/> 处理某个参数位置、且该位置按已登记签名期望类型
        /// 恰好是 <see cref="ExprValueKind.Id"/> 时才会传 <c>true</c>（见该方法判断记录），其余
        /// 全部调用点维持默认的 <c>false</c>（行为与改动前完全一致）。
        /// </summary>
        private static ExprValueKind? InferKind(ExprNode node, IExprSchema schema, List<ExprIssue> issues, bool suppressSuspiciousIdWarning)
        {
            switch (node)
            {
                case ExprLiteralNode literal:
                    if (!suppressSuspiciousIdWarning)
                    {
                        CheckSuspiciousIdLiteral(literal, schema, issues);
                    }
                    return literal.Value.Kind;

                case ExprReferenceNode reference:
                    return ValidateReference(reference, schema, issues);

                case ExprNotNode notNode:
                {
                    var operandKind = InferKind(notNode.Operand, schema, issues);
                    CheckBoolOperand("not", notNode.Operand, operandKind, issues);
                    return ExprValueKind.Bool;
                }

                case ExprAndNode andNode:
                {
                    foreach (var operand in andNode.Operands)
                    {
                        CheckBoolOperand("and", operand, InferKind(operand, schema, issues), issues);
                    }
                    return ExprValueKind.Bool;
                }

                case ExprOrNode orNode:
                {
                    foreach (var operand in orNode.Operands)
                    {
                        CheckBoolOperand("or", operand, InferKind(operand, schema, issues), issues);
                    }
                    return ExprValueKind.Bool;
                }

                case ExprCompareNode compareNode:
                    return ValidateCompare(compareNode, schema, issues);

                default:
                    return null;
            }
        }

        private static void CheckBoolOperand(string opName, ExprNode operand, ExprValueKind? kind, List<ExprIssue> issues)
        {
            if (kind.HasValue && kind.Value != ExprValueKind.Bool)
            {
                issues.Add(new ExprIssue(ExprIssueKind.LogicalOperandNotBool,
                    $"{opName} 操作数不是 Bool：实际为 {kind.Value}", operand.Start, operand.Length));
            }
        }

        private static ExprValueKind? ValidateReference(ExprReferenceNode reference, IExprSchema schema, List<ExprIssue> issues)
        {
            // 判断记录（消费方反馈 E6 根治，2026-09-10）：提前（在处理实参之前）尝试解析本次引用的
            // 签名——只是为了知道每个实参位置"期望的静态类型"，不改变下面第二次同样查询之后的
            // UnknownGroup/UnknownKey 报错时机与顺序（那两处判断保持在原来的位置不变，本次查询
            // 结果只读不用于任何提前 return）。
            ExprSignature signatureForArgs = default;
            bool signatureKnownForArgs = ExprGroups.IsKnown(reference.Group) &&
                schema.TryGetSignature(reference.Group, reference.Key, out signatureForArgs);

            // 无论 group/key 是否已知，都先递归校验全部实参子树，尽量收集问题而不是遇错即停。
            var argKinds = new ExprValueKind?[reference.Args.Count];
            for (int i = 0; i < reference.Args.Count; i++)
            {
                // 判断记录（消费方反馈 E6 根治，见 core/foundation/expr/README.md"疑似引用拼写
                // 错误"判断记录）："疑似引用拼写错误"警告的原意是提醒"这段文本本该是一个引用、
                // 却因为未登记而被归类成 Id 字面量"；但当这个位置的静态期望类型（来自已登记签名）
                // 本身就是 Id 时，该位置天然应该填一个内容 id，不是"疑似漏注册的引用"——04 第 2.2
                // 节允许内容 id 的 domain 与九个分组之一同名（`quest.sample_hunt`/
                // `world.bridge.repaired` 一类用法本就是设计内的正常写法，见
                // core/foundation/expr/tests/ExprAdr0015Tests.cs 对应用例），此前的规则只认"把
                // 该 Id 本身也登记进签名表"这一种消除警告的方式（见
                // SuspiciousReferenceSpelling_NotReported_WhenGroupKeyIsRegistered 用例），对参数
                // 位置期望类型已经是 Id 的场景没有对应豁免，产生系统性误报（任何"域名与某个分组
                // 同名的内容 domain"，只要把自己的 id 当参数传给该分组下任意一个已登记签名，都会
                // 触发）。这里改为额外识别这条豁免：期望类型是 Id 时，该实参位置的字面量不再触发
                // "疑似拼错"警告；其它位置（期望类型非 Id、或签名本身未知）行为不变。
                bool suppress = signatureKnownForArgs && i < signatureForArgs.ArgKinds.Count &&
                    signatureForArgs.ArgKinds[i] == ExprValueKind.Id;
                argKinds[i] = InferKind(reference.Args[i], schema, issues, suppress);
            }

            if (!ExprGroups.IsKnown(reference.Group))
            {
                issues.Add(new ExprIssue(ExprIssueKind.UnknownGroup, $"未知的引用分组：\"{reference.Group}\"",
                    reference.Start, reference.Length));
                return null;
            }

            if (!schema.TryGetSignature(reference.Group, reference.Key, out var signature))
            {
                // 判断记录（阶段 3 集成"事项二"）：event 分组的 key 随触发事件类型动态变化，
                // ExprParser 已经把未登记的 event.<key> 归类为 <reference>（见该类型 ParseIdentTerm
                // 判断记录）而不是 Id 字面量——这里对应地不把"未登记"当错误：现有 ExprValueKind 枚举
                // 没有 Any 概念（见该类型注释"Expr 语言本身没有其它类型"），本类型用"返回类型未知"
                // 这一最宽松的既有表示——返回 null（同"某一侧类型未知"分支），调用方
                // （ValidateCompare/CheckBoolOperand 等）看到 null 一律跳过类型检查而不是报错，
                // 等价于把 event.<未登记 key> 当作 Any 类型处理，静态期不作任何类型假设，交给运行期
                // RulesExprHostFactory.QueryEvent 按事件实际携带的字段值求值。非 event 分组维持原有
                // 行为：未登记的 group.key 仍是 UnknownKey 错误。
                if (reference.Group != ExprGroups.Event)
                {
                    issues.Add(new ExprIssue(ExprIssueKind.UnknownKey, $"未知的引用 key：\"{reference.Group}.{reference.Key}\"",
                        reference.Start, reference.Length));
                }
                return null;
            }

            if (signature.ArgKinds.Count != reference.Args.Count)
            {
                issues.Add(new ExprIssue(ExprIssueKind.ArgCountMismatch,
                    $"\"{reference.Group}.{reference.Key}\" 期望 {signature.ArgKinds.Count} 个参数，实际 {reference.Args.Count} 个",
                    reference.Start, reference.Length));
            }
            else
            {
                for (int i = 0; i < signature.ArgKinds.Count; i++)
                {
                    var expected = signature.ArgKinds[i];
                    var actual = argKinds[i];
                    if (actual.HasValue && !KindsCompatible(expected, actual.Value))
                    {
                        var argNode = reference.Args[i];
                        issues.Add(new ExprIssue(ExprIssueKind.ArgTypeMismatch,
                            $"\"{reference.Group}.{reference.Key}\" 第 {i + 1} 个参数期望 {expected}，实际 {actual.Value}",
                            argNode.Start, argNode.Length));
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
                    $"比较两侧类型不一致：{leftKind.Value} 与 {rightKind.Value}", node.Start, node.Length));
                return ExprValueKind.Bool;
            }

            // 走到这里说明两侧类型相同且不是数值类型：只能是 Bool/String/Id，三者都只支持 ==/!=。
            bool eqOnly = node.Op == ExprCompareOp.Eq || node.Op == ExprCompareOp.Ne;
            if (!eqOnly)
            {
                issues.Add(new ExprIssue(ExprIssueKind.InvalidCompareForType,
                    $"{leftKind.Value} 类型只支持 ==/!= 比较，不支持 {ExprCompareNode.OpText(node.Op)}", node.Start, node.Length));
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
        private static void CheckSuspiciousIdLiteral(ExprLiteralNode literal, IExprSchema schema, List<ExprIssue> issues)
        {
            var value = literal.Value;
            if (value.Kind != ExprValueKind.Id) return;

            var idText = value.AsId.Value;
            var dotIndex = idText.IndexOf('.');
            if (dotIndex < 0) return; // Id 格式保证至少一个 '.'，防御性检查。

            var domain = idText.Substring(0, dotIndex);
            var key = idText.Substring(dotIndex + 1);

            if (!ExprGroups.IsKnown(domain)) return;
            if (schema.TryGetSignature(domain, key, out _)) return;

            issues.Add(new ExprIssue(ExprIssueKind.SuspiciousReferenceSpelling, ExprIssueSeverity.Warning,
                $"疑似引用拼写错误：\"{idText}\" 的域名与分组 \"{domain}\" 同名但未登记为引用",
                literal.Start, literal.Length));
        }

        private static bool IsNumeric(ExprValueKind kind) => kind == ExprValueKind.Int || kind == ExprValueKind.Number;

        private static bool KindsCompatible(ExprValueKind expected, ExprValueKind actual) =>
            expected == actual || (IsNumeric(expected) && IsNumeric(actual));
    }
}
