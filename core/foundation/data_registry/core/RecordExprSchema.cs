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
    /// <para>
    /// 判断记录（消费方反馈第四批第 25 条，2026-09-10）：<see cref="FieldKind.Expr"/> 字段同样不
    /// 登记为 <c>self.&lt;field&gt;</c>。该字段种类的值是待求值的表达式文本，不是可直接比较的标量
    /// 数据——把它当 <see cref="ExprValueKind.String"/> 注册会让 <c>self.&lt;expr_field&gt;</c>
    /// 在谓词里被当作字符串字面量参与比较，但记录里实际存的是一段代码，取值语义不明确。已 grep 全仓
    /// <c>data/</c> 与全部测试，未发现任何 <c>self.&lt;Expr 字段名&gt;</c> 的现存引用（见
    /// <c>RecordExprSchemaExprFieldExclusionTests</c>），故直接排除，不采用"保留注册但只在文档
    /// 说明取到的是文本"的折中方案。
    /// </para>
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

        /// <summary>
        /// 消费方反馈（编辑器）第 27 条根治（2026-09-11，见
        /// architecture/落地计划/消费方反馈-2026-09-11-编辑器-第27条.md）：<paramref name="group"/>
        /// 为 <see cref="ExprGroups.Self"/> 时返回 <see cref="For"/> 登记过的全部字段名（按序号
        /// Ordinal 排序，确定性；与 <see cref="ExprSchema.KnownKeys"/> 同一份判断记录），其它分组
        /// 本类型从不登记（见 <see cref="TryGetSignature"/>），返回空集合。</summary>
        public IReadOnlyCollection<string> KnownKeys(string group)
        {
            if (group != ExprGroups.Self || _fields.Count == 0)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>(_fields.Keys);
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>本类型只可能登记 <see cref="ExprGroups.Self"/> 一个分组（见
        /// <see cref="TryGetSignature"/>），字段非空时返回该分组，否则返回空集合。</summary>
        public IReadOnlyCollection<string> KnownGroups =>
            _fields.Count == 0 ? Array.Empty<string>() : new[] { ExprGroups.Self };
    }

    /// <summary>
    /// 字段类型（<see cref="FieldKind"/>）到 Expr 标量类型（<see cref="ExprValueKind"/>）的映射，
    /// 供 <see cref="RecordExprSchema"/>（静态签名）与 <see cref="RecordExprHost"/>（实际取值）
    /// 共用同一份规则，保证两者一致。
    /// <para>
    /// 判断记录（消费方反馈第四批第 26 条，2026-09-10）：本类型与 <see cref="ToExprValueKind"/>
    /// 由 <c>internal</c>/隐式 <c>public</c> 放宽为显式 <c>public static</c>，纯可见性放宽，不改变
    /// 任何映射结果或调用方行为——编辑器等框架外消费方需要独立复用这份"字段种类 → Expr 标量类型"
    /// 映射表（例如做字段级联动提示），此前只能通过 <see cref="RecordExprSchema.For"/> 间接观察到
    /// 结果，拿不到可直接调用的映射函数本身。
    /// </para>
    /// <para>
    /// 完整映射表（13 种 <see cref="FieldKind"/>，见 <c>RecordExprSchemaExprFieldExclusionTests</c>
    /// 与其余映射测试逐条断言）：
    /// <list type="bullet">
    /// <item><description><see cref="FieldKind.Bool"/> → <see cref="ExprValueKind.Bool"/></description></item>
    /// <item><description><see cref="FieldKind.Int"/> → <see cref="ExprValueKind.Int"/></description></item>
    /// <item><description><see cref="FieldKind.Number"/> → <see cref="ExprValueKind.Number"/></description></item>
    /// <item><description><see cref="FieldKind.String"/>/<see cref="FieldKind.Enum"/> →
    /// <see cref="ExprValueKind.String"/></description></item>
    /// <item><description><see cref="FieldKind.Id"/>/<see cref="FieldKind.Reference"/>/
    /// <see cref="FieldKind.TextKey"/> → <see cref="ExprValueKind.Id"/></description></item>
    /// <item><description><see cref="FieldKind.Expr"/>/<see cref="FieldKind.IdList"/>/
    /// <see cref="FieldKind.Vec2"/>/<see cref="FieldKind.Object"/>/<see cref="FieldKind.Array"/> →
    /// <c>null</c>（不是 Expr 标量类型，不登记为 <c>self.&lt;field&gt;</c> 引用；<c>Expr</c> 的排除
    /// 理由见 <see cref="RecordExprSchema"/> 类型注释判断记录，其余四种是本就不可能映射为标量的复合
    /// 类型）</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public static class RecordExprMapping
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
                    return ExprValueKind.String;
                case FieldKind.Id:
                case FieldKind.Reference:
                case FieldKind.TextKey:
                    return ExprValueKind.Id;
                case FieldKind.Expr:
                    // 判断记录（消费方反馈第四批第 25 条）：Expr 字段的值是待求值文本，不是可比较的
                    // 标量数据，self.<expr_field> 没有明确的取值语义，排除出 self. 自动注册（与
                    // IdList/Vec2/Object/Array 同等对待，不是折中方案），见 RecordExprSchema 类型
                    // 注释判断记录。
                    return null;
                default:
                    // IdList/Vec2/Object/Array 不是 Expr 标量类型，不登记为 self.<field> 引用。
                    return null;
            }
        }
    }
}
