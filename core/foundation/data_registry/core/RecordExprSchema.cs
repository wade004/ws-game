using System;
using System.Collections.Generic;
using Core.Foundation.Expr;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 供 <see cref="DataRegistry.Query(string, string)"/> 便捷重载解析谓词文本用的
    /// <see cref="IExprSchema"/>：把某张表的全部字段登记成零参引用 <c>self.&lt;field&gt;</c>
    /// （见 04 第 4 节"query 的 predicate...宿主上下文只暴露记录自身字段"、第 6.2 节 <c>self</c>
    /// 分组）。不支持带参数的引用（04 该字段只是"记录自身状态"，不存在需要参数的场景）。
    /// 类型映射见 <see cref="RecordExprMapping.ToExprValueKind"/>：无法映射为 Expr 标量类型的
    /// 字段（<see cref="FieldKind.IdList"/>/<see cref="FieldKind.Vec2"/>/
    /// <see cref="FieldKind.Object"/>/<see cref="FieldKind.Array"/>）不登记，谓词文本引用到
    /// 这些字段名时会被 <see cref="ExprParser"/> 按 Id 字面量处理（未登记 = 非引用，见
    /// ADR-0015），不会抛异常。
    /// </summary>
    public sealed class RecordExprSchema : IExprSchema
    {
        private readonly Dictionary<string, ExprSignature> _fields;

        private RecordExprSchema(Dictionary<string, ExprSignature> fields)
        {
            _fields = fields;
        }

        public static RecordExprSchema For(TableSchema table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var fields = new Dictionary<string, ExprSignature>(StringComparer.Ordinal);
            foreach (var field in table.Fields)
            {
                var kind = RecordExprMapping.ToExprValueKind(field.Kind);
                if (kind.HasValue)
                {
                    fields[field.Name] = new ExprSignature(kind.Value, Array.Empty<ExprValueKind>());
                }
            }
            return new RecordExprSchema(fields);
        }

        public bool TryGetSignature(string group, string key, out ExprSignature signature)
        {
            if (group == ExprGroups.Self && _fields.TryGetValue(key, out signature))
            {
                return true;
            }
            signature = default;
            return false;
        }
    }

    /// <summary>字段类型（<see cref="FieldKind"/>）到 Expr 标量类型（<see cref="ExprValueKind"/>）
    /// 的映射，供 <see cref="RecordExprSchema"/>（静态签名）与 <see cref="RecordExprHost"/>
    /// （实际取值）共用同一份规则，保证两者一致。</summary>
    internal static class RecordExprMapping
    {
        public static ExprValueKind? ToExprValueKind(FieldKind kind)
        {
            switch (kind)
            {
                case FieldKind.Bool: return ExprValueKind.Bool;
                case FieldKind.Int: return ExprValueKind.Int;
                case FieldKind.Number: return ExprValueKind.Number;
                case FieldKind.String:
                case FieldKind.Enum:
                case FieldKind.Expr:
                    return ExprValueKind.String;
                case FieldKind.Id:
                case FieldKind.Reference:
                case FieldKind.TextKey:
                    return ExprValueKind.Id;
                default:
                    // IdList/Vec2/Object/Array 不是 Expr 标量类型，不登记为 self.<field> 引用。
                    return null;
            }
        }
    }
}
