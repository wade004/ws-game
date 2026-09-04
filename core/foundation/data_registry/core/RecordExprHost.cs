using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.Expr;
using CommonId = Core.Foundation.Common.Id;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// <see cref="DataRegistry.Query(string, ExprNode)"/> 用的 <see cref="IExprHost"/>：只暴露
    /// <c>self</c> 分组，<c>self.&lt;field&gt;</c> 返回记录字段（见 04 第 4 节"宿主上下文只暴露
    /// 记录自身字段"）。类型映射：已在 <see cref="TableSchema"/> 登记该字段时按
    /// <see cref="RecordExprMapping"/> 规则转换；未登记（如 <see cref="TableSchema.Unschematized"/>）
    /// 时退化为按原始 JSON 类型映射（Bool/Number/String 三种，Number 能表示为整数时映射为
    /// <see cref="ExprValueKind.Int"/>，否则 <see cref="ExprValueKind.Number"/>）。缺字段返回
    /// <c>Bool false</c> 并记一条警告（不抛异常，见任务书"缺字段返回 Bool false 并记警告"）。
    /// 不支持带参数的引用。
    /// </summary>
    internal sealed class RecordExprHost : IExprHost
    {
        private readonly DataRecord _record;
        private readonly IExprDiagnostics _diagnostics;

        public RecordExprHost(DataRecord record, IExprDiagnostics diagnostics)
        {
            _record = record ?? throw new ArgumentNullException(nameof(record));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args)
        {
            if (group != ExprGroups.Self)
            {
                throw new InvalidOperationException($"RecordExprHost 只支持 \"self\" 分组，实际 \"{group}\"");
            }
            if (args.Count != 0)
            {
                throw new InvalidOperationException("RecordExprHost 不支持带参数的引用（self 分组语义是记录自身字段）");
            }

            if (!_record.Has(key))
            {
                _diagnostics.Warn($"表 \"{_record.Table.Name}\" 记录 \"{_record.Key}\" 缺少字段 \"{key}\"，self.{key} 按 false 处理");
                return ExprValue.OfBool(false);
            }

            var raw = _record.Raw[key];
            var fieldSchema = _record.Table.GetField(key);
            return fieldSchema != null ? ConvertBySchema(fieldSchema.Kind, raw, key) : ConvertByRawKind(raw, key);
        }

        private ExprValue ConvertBySchema(FieldKind kind, JsonValue raw, string key)
        {
            switch (kind)
            {
                case FieldKind.Bool:
                    if (raw is JsonBool b) return ExprValue.OfBool(b.Value);
                    break;

                case FieldKind.Int:
                    if (raw is JsonNumber ni && ni.TryGetInt64(out var iv)) return ExprValue.OfInt(iv);
                    break;

                case FieldKind.Number:
                    if (raw is JsonNumber nn) return ExprValue.OfNumber(nn.Value);
                    break;

                case FieldKind.String:
                case FieldKind.Enum:
                case FieldKind.Expr:
                    if (raw is JsonString ss) return ExprValue.OfString(ss.Value);
                    break;

                case FieldKind.Id:
                case FieldKind.Reference:
                case FieldKind.TextKey:
                    if (raw is JsonString sid && CommonId.TryParse(sid.Value, out var idv)) return ExprValue.OfId(idv);
                    break;
            }

            _diagnostics.Warn($"字段 \"{key}\" 的实际 JSON 类型（{raw.Kind}）与声明的 {kind} 不匹配，self.{key} 按 false 处理");
            return ExprValue.OfBool(false);
        }

        private ExprValue ConvertByRawKind(JsonValue raw, string key)
        {
            switch (raw.Kind)
            {
                case JsonKind.Bool:
                    return ExprValue.OfBool(((JsonBool)raw).Value);

                case JsonKind.Number:
                    var num = (JsonNumber)raw;
                    return num.TryGetInt64(out var iv) ? ExprValue.OfInt(iv) : ExprValue.OfNumber(num.Value);

                case JsonKind.String:
                    return ExprValue.OfString(((JsonString)raw).Value);

                default:
                    _diagnostics.Warn($"字段 \"{key}\" 的 JSON 类型 {raw.Kind} 不支持作为 Expr 值，self.{key} 按 false 处理");
                    return ExprValue.OfBool(false);
            }
        }
    }
}
