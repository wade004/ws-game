using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;
using Core.Rules.ExprHost;

namespace Core.Rules.Ai
{
    /// <summary>
    /// 本模块专属校验规则（见 <see cref="IValidationRule"/>"模块专属校验规则的扩展点"、任务书
    /// T2-10 验收标准"校验：priority 不重复（同表内）、flee_hp_pct_threshold ∈ [0,1]、points ≥ 2"）。
    /// <see cref="AiSchemas"/> 里 <c>entries</c>/<c>points</c>/<c>transitions</c> 均声明为
    /// <see cref="FieldKind.Array"/>/<see cref="FieldKind.Object"/>，DataRegistry 通用校验只检查
    /// "存在且是数组/对象"（见 <see cref="FieldKind"/> 注释），不深入检查数组元素/对象取值的结构——
    /// 那属于"模块专属校验规则"的职责，本类据此承担。
    /// <para>
    /// 判断记录：本规则不由 <c>data_registry</c> 自动注册，调用方（集成任务或本模块的测试）需要
    /// 显式 <c>registry.RegisterValidationRule(new AiContentValidationRule())</c>（与
    /// <see cref="IValidationRule"/> 契约"由各内容模块自行登记"一致）。
    /// </para>
    /// </summary>
    public sealed class AiContentValidationRule : IValidationRule
    {
        private const string Check = "ai_content";

        // 集成任务改动：本模块原先自带的临时占位 schema（只覆盖 AI 直接相关、文档已给出示例
        // 引用的最小词汇集合，阶段 3 整理后已删除）改为默认使用集成任务提供的
        // core/rules/expr_host.RulesExprSchema.Base——后者是 skill/combat/targeting/ai 四模块共用的
        // 同一份词汇表（ADR-0015"同一份 schema 供内容校验与运行期共用"），覆盖面更完整。保留
        // 可注入口子（构造参数 exprSchema）：不强制调用方必须换成 RulesExprSchema，需要复现旧行为
        // 或注入测试专用的更严格 schema 时可以显式传入。
        private readonly IExprSchema _schema;

        public AiContentValidationRule(IExprSchema? exprSchema = null)
        {
            _schema = exprSchema ?? RulesExprSchema.Base;
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var issue in ValidateRotations(view))
            {
                yield return issue;
            }

            foreach (var issue in ValidateBehaviorProfiles(view))
            {
                yield return issue;
            }

            foreach (var issue in ValidatePatrolPaths(view))
            {
                yield return issue;
            }
        }

        /// <summary>
        /// ADR-0019 F1c 收窄：<c>AiSchemas.Rotation.entries</c> 现登记子结构（<c>priority:Int</c>/
        /// <c>condition:Expr</c>/<c>skill_id:Reference(skill.def)</c> 均必填），以下检查已被
        /// <c>data_registry</c> 内建校验完全覆盖、整段删除：元素是否为对象（<c>field_type</c>）、
        /// <c>priority</c>/<c>condition</c>/<c>skill_id</c> 是否缺失或类型不符
        /// （<c>required_field</c>/<c>field_type</c>）、<c>condition</c> 能否解析
        /// （<c>expr_parsable</c>，登记为 <see cref="FieldKind.Expr"/> 后与顶层字段共用同一份
        /// <c>ValidateExprField</c> 实现，见 <c>DataRegistry.ValidateFieldValue</c> 递归校验）、
        /// <c>skill_id</c> 引用是否存在（<c>reference_integrity</c>，此前完全没有这项检查）。仅保留
        /// <c>priority</c> 同表内不重复这条纯业务判断（登记层无法表达"数组内多个元素互相比较"）。
        /// </summary>
        private IEnumerable<ValidationIssue> ValidateRotations(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(AiSchemas.Rotation.Name))
            {
                if (!record.TryGetArray("entries", out var entries))
                {
                    continue;
                }

                var seenPriorities = new HashSet<long>();

                foreach (var item in entries)
                {
                    if (!(item is JsonObject entry) ||
                        !entry.TryGetValue("priority", out var priorityValue) || !(priorityValue is JsonNumber priorityNumber))
                    {
                        // 元素非对象/priority 缺失或类型不符：已由 field_type/required_field 报告。
                        continue;
                    }

                    var priority = (long)priorityNumber.Value;
                    if (!seenPriorities.Add(priority))
                    {
                        yield return new ValidationIssue(ValidationSeverity.Error, AiSchemas.Rotation.Name, Check,
                            $"priority {priority} 在同一张 ai.rotation 表内重复", recordKey: record.Key, field: "entries");
                    }
                }
            }
        }

        private IEnumerable<ValidationIssue> ValidateBehaviorProfiles(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(AiSchemas.BehaviorProfile.Name))
            {
                if (record.TryGetNumber("flee_hp_pct_threshold", out var threshold) && (threshold < 0.0 || threshold > 1.0))
                {
                    yield return new ValidationIssue(ValidationSeverity.Error, AiSchemas.BehaviorProfile.Name, Check,
                        $"flee_hp_pct_threshold 必须在 [0,1] 区间内，实际为 {threshold}", recordKey: record.Key, field: "flee_hp_pct_threshold");
                }

                if (record.TryGetObject("transitions", out var transitions))
                {
                    foreach (var kv in transitions)
                    {
                        if (!(kv.Value is JsonString text))
                        {
                            yield return new ValidationIssue(ValidationSeverity.Error, AiSchemas.BehaviorProfile.Name, Check,
                                $"transitions.{kv.Key} 必须是字符串（Expr 文本）", recordKey: record.Key, field: "transitions");
                            continue;
                        }

                        foreach (var exprIssue in TryParseExpr(text.Value))
                        {
                            yield return new ValidationIssue(ValidationSeverity.Error, AiSchemas.BehaviorProfile.Name, "expr_parsable",
                                $"transitions.{kv.Key} 解析失败：{exprIssue}", recordKey: record.Key, field: "transitions");
                        }
                    }
                }
            }
        }

        private IEnumerable<ValidationIssue> ValidatePatrolPaths(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(AiSchemas.PatrolPath.Name))
            {
                if (!record.TryGetArray("points", out var points) || points.Count < 2)
                {
                    yield return new ValidationIssue(ValidationSeverity.Error, AiSchemas.PatrolPath.Name, Check,
                        "points 至少需要 2 个点", recordKey: record.Key, field: "points");
                }
            }
        }

        /// <summary>解析 + 静态校验一段 Expr 文本，返回可读的问题描述列表；空列表表示通过。</summary>
        private IEnumerable<string> TryParseExpr(string text)
        {
            ExprNode? node = null;
            string? parseError = null;
            try
            {
                node = ExprParser.Parse(text, _schema);
            }
            catch (ExprParseException ex)
            {
                parseError = ex.Message;
            }

            if (parseError != null)
            {
                return new[] { parseError };
            }

            var issues = ExprValidator.Validate(node!, _schema);
            var messages = new List<string>();
            foreach (var issue in issues)
            {
                if (issue.Severity == ExprIssueSeverity.Error)
                {
                    messages.Add(issue.Message);
                }
            }
            return messages;
        }
    }
}
