using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;

namespace Core.Rules.Targeting
{
    /// <summary>
    /// <c>target.chain_def</c> 专属的内容校验规则（见任务书"校验规则：source 已注册……fallback
    /// 链无环……Expr 可解析"）：调用方需要
    /// <c>registry.RegisterValidationRule(new ChainDefValidationRule(strategyRegistry.Names))</c>
    /// 才会生效，本模块不自动注册（与 <c>ArchTalentTreeCycleValidationRule</c> 同一惯例）。
    /// <para>
    /// 判断记录（"Expr 可解析"一项，见 targeting/README.md"契约缺口"）：任务书原句"Expr
    /// 可解析（数据注册表已做）"是就 <c>target.chain_def.<see cref="FieldKind.Expr"/></c> 标量字段
    /// 而言的一般表述，但本表的 <c>filters</c> 字段是 <c>Array</c>（每个元素才是一段独立的
    /// Expr 文本或内置过滤简写），<c>DataRegistry</c> 的内置 <c>expr_parsable</c> 检查项只覆盖
    /// <see cref="FieldKind.Expr"/> 标量字段（见 <c>DataRegistry.ValidateRecordField</c>），不会
    /// 逐元素解析数组——因此"filters 数组里的 Expr 文本能否解析"这项检查改由本规则自己跑
    /// <see cref="ExprParser.Parse"/> 完成，不是"数据注册表已经做好、本规则零工作量"。
    /// </para>
    /// </summary>
    public sealed class ChainDefValidationRule : IValidationRule
    {
        private static readonly IExprSchema FilterSchema = new TargetFilterExprSchema();

        private readonly IReadOnlyCollection<string> _knownSources;

        public ChainDefValidationRule(IReadOnlyCollection<string> knownSources)
        {
            _knownSources = knownSources ?? throw new ArgumentNullException(nameof(knownSources));
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var table = TargetSchemas.ChainDef.Name;
            if (!view.Tables.Contains(table))
            {
                yield break;
            }

            var all = view.GetAll(table);
            var fallbackOf = new Dictionary<string, string?>(StringComparer.Ordinal);

            foreach (var record in all)
            {
                fallbackOf[record.Key] = record.TryGetId("fallback", out var fallbackId) ? fallbackId.ToString() : null;

                if (record.TryGetString("source", out var source) && !_knownSources.Contains(source))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, table, "target_source_unknown",
                        $"未注册的目标来源策略：\"{source}\"（见 TargetStrategyRegistry.Register）",
                        recordKey: record.Key, field: "source");
                }

                if (record.TryGetArray("filters", out var filters))
                {
                    for (int i = 0; i < filters.Count; i++)
                    {
                        if (!(filters[i] is JsonString filterStr))
                        {
                            yield return new ValidationIssue(
                                ValidationSeverity.Error, table, "target_filter_type",
                                $"filters 第 {i} 个元素不是字符串", recordKey: record.Key, field: "filters");
                            continue;
                        }

                        if (IsBuiltinFilterShorthand(filterStr.Value))
                        {
                            continue;
                        }

                        // yield return 不能出现在 catch 子句体内（CS1631），先把错误消息存到
                        // 局部变量，跳出 try/catch 之后再 yield。
                        string? parseError = null;
                        try
                        {
                            ExprParser.Parse(filterStr.Value, FilterSchema);
                        }
                        catch (ExprParseException ex)
                        {
                            parseError = ex.Message;
                        }

                        if (parseError != null)
                        {
                            yield return new ValidationIssue(
                                ValidationSeverity.Error, table, "target_filter_expr_parsable",
                                $"filters 第 {i} 项 Expr 解析失败：{parseError}", recordKey: record.Key, field: "filters");
                        }
                    }
                }
            }

            foreach (var record in all)
            {
                if (HasFallbackCycle(record.Key, fallbackOf))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, table, "target_chain_fallback_cycle",
                        $"链 \"{record.Key}\" 的 fallback 链存在环", recordKey: record.Key, field: "fallback");
                }
            }
        }

        private static bool IsBuiltinFilterShorthand(string text)
        {
            return text == "alive"
                || text.StartsWith("relation:", StringComparison.Ordinal)
                || text.StartsWith("tag:", StringComparison.Ordinal);
        }

        private static bool HasFallbackCycle(string startKey, Dictionary<string, string?> fallbackOf)
        {
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            string? current = startKey;
            while (current != null)
            {
                if (!visiting.Add(current))
                {
                    return true;
                }

                fallbackOf.TryGetValue(current, out current);
            }

            return false;
        }
    }
}
