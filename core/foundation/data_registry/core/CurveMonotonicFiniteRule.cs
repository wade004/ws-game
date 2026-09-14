using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 通用"曲线单调递增且逐值有限"阻断规则（分阶段落地计划 T-N0-3；落地清单 2.1 C3；04 第 5 节
    /// 数值类校验项分级表"曲线单调有限"；数值总纲第 3 节原则 1）：对全部已登记为断点表形态
    /// （<see cref="CurveSchema"/>，<see cref="CurveShape.Breakpoints"/>）的字段——无论它在记录顶层还是
    /// 嵌在 <see cref="FieldSchema.Fields"/>/<see cref="FieldSchema.Item"/>/<see cref="FieldSchema.Map"/>/
    /// <see cref="FieldSchema.Variants"/> 之内——逐条记录检查：断点非空；每个 <c>x</c>/<c>y</c> 为有限数；
    /// <c>x</c> 严格递增（排序后无重复，否则同一横轴取值对应两个 <c>y</c>，曲线不成函数）；<c>y</c>
    /// 沿 <c>x</c> 不递减（允许相等——封顶后的平台段是合法形态，如换算曲线到上限后持平）。任一不满足
    /// 报 <see cref="ValidationSeverity.Error"/>，检查名 <see cref="CheckName"/>。
    /// <para>
    /// 判断记录：
    /// </para>
    /// <list type="number">
    /// <item><description><b>不与 <c>field_finite</c> 合并</b>（T-N0-3 禁止事项）：<c>field_finite</c> 是
    /// 字段级检查，管单个 <c>Number</c> 值；本规则是曲线级检查，管一组断点之间的关系（单调、无重复
    /// 横轴、非空），层次不同。断点里的 NaN/无穷在 JSON 文本路径上本就进不来（<c>JsonReader</c> 拒绝
    /// 非有限数），本规则仍检查有限性是为程序构造的记录与未来其它数据源兜底。</description></item>
    /// <item><description><b>形态不符不重复报</b>：元素缺 <c>x</c>/<c>y</c>、类型不对、字段不是数组，
    /// 已由 <c>required_field</c>/<c>field_type</c> 按登记子结构报过，本规则跳过（<see cref="CurveSchema.TryReadBreakpoints"/>
    /// 返回 false 即跳过），避免一处内容错误报两条。</description></item>
    /// <item><description><b>空断点表算错误</b>：曲线字段一旦出现（必填或可选但已填），空数组意味着运行时
    /// 插值恒为 0——这是"内容错误被静默降级"的同款模式（同 <c>points_per_percent &gt; 0</c> 登记的判断记录），
    /// 提前到加载期阻断；字段缺席（可选且未填）不在本规则范围。</description></item>
    /// <item><description><b>只认登记形态</b>：本规则不知道任何具体表名——迁移到断点表形态的表自动纳入，
    /// 未迁移的密集枚举表（如 <c>prog.level_curve</c>）由各自模块的规则检查（T-N0-5）。</description></item>
    /// <item><description><b>与 <c>DataRegistry</c> 的子结构递归共用同一套路径记法</b>（<c>a.b</c>/<c>a[2]</c>/
    /// <c>a[key]</c>/变体按判别字段取分支），深度上限与 <c>DataRegistry.MaxSubstructureDepth</c> 一致，
    /// 超限静默停止（<c>substructure_depth</c> 已由字段级校验报过）。</description></item>
    /// </list>
    /// </summary>
    public sealed class CurveMonotonicFiniteRule : IValidationRule
    {
        /// <summary>检查名（04 第 5 节数值类校验项分级表"曲线单调有限"）。</summary>
        public const string CheckName = "curve_monotonic_finite";

        private const int MaxDepth = 32;

        public string RuleId => nameof(CurveMonotonicFiniteRule);

        public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

        public bool NonEscalatable => false;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var issues = new List<ValidationIssue>();
            var tables = view.Tables;
            for (var t = 0; t < tables.Count; t++)
            {
                var table = tables[t];
                var schema = view.GetSchema(table);
                if (schema == null || !HasCurveField(schema.Fields, 0))
                {
                    continue;
                }

                var records = view.GetAll(table);
                for (var r = 0; r < records.Count; r++)
                {
                    var record = records[r];
                    WalkFields(schema.Fields, record.Raw, "", 0, table, record.Key, issues);
                }
            }

            return issues;
        }

        /// <summary>检查一条已解析的断点表；满足"非空、逐值有限、x 严格递增、y 不递减"返回 null，否则返回
        /// 面向内容作者的问题描述（含首个出问题的断点下标，下标按排序后的顺序）。</summary>
        public static string? Inspect(PiecewiseCurve curve)
        {
            if (curve == null) throw new System.ArgumentNullException(nameof(curve));

            var points = curve.Points;
            if (points.Count == 0)
            {
                return "曲线没有任何断点（空断点表会让插值恒为 0）";
            }

            for (var i = 0; i < points.Count; i++)
            {
                if (!IsFinite(points[i].X) || !IsFinite(points[i].Y))
                {
                    return $"第 {i} 个断点 {points[i]} 含非有限数（NaN/无穷）";
                }
            }

            for (var i = 0; i < points.Count - 1; i++)
            {
                var lo = points[i];
                var hi = points[i + 1];
                if (hi.X == lo.X)
                {
                    return $"横轴取值 {lo.X.ToString(System.Globalization.CultureInfo.InvariantCulture)} 重复出现（第 {i}/{i + 1} 个断点），同一横轴对应两个值";
                }
                if (hi.Y < lo.Y)
                {
                    return $"曲线在第 {i} 到第 {i + 1} 个断点之间下降（{lo} → {hi}），要求单调递增（允许相等）";
                }
            }

            return null;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        /// <summary>静态预筛：登记里没有任何断点表曲线字段的表直接跳过，不逐条记录遍历。</summary>
        private static bool HasCurveField(IReadOnlyList<FieldSchema>? fields, int depth)
        {
            if (fields == null || depth >= MaxDepth)
            {
                return false;
            }

            for (var i = 0; i < fields.Count; i++)
            {
                if (HasCurve(fields[i], depth))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasCurve(FieldSchema field, int depth)
        {
            if (field.Curve != null && field.Curve.Shape == CurveShape.Breakpoints)
            {
                return true;
            }
            if (depth >= MaxDepth)
            {
                return false;
            }
            if (field.Kind == FieldKind.Object)
            {
                if (HasCurveField(field.Fields, depth + 1)) return true;
                if (field.Map != null && HasCurve(field.Map.ValueSchema, depth + 1)) return true;
                var variants = field.Variants;
                if (variants != null)
                {
                    if (HasCurveField(variants.CommonFields, depth + 1)) return true;
                    foreach (var kv in variants.Cases)
                    {
                        if (HasCurveField(kv.Value, depth + 1)) return true;
                    }
                }
            }
            else if (field.Kind == FieldKind.Array)
            {
                var item = field.Item;
                if (item != null && HasCurve(item, depth + 1)) return true;
            }

            return false;
        }

        private static void WalkFields(
            IReadOnlyList<FieldSchema>? fields, JsonObject obj, string prefix, int depth,
            string table, string recordKey, List<ValidationIssue> issues)
        {
            if (fields == null || depth >= MaxDepth)
            {
                return;
            }

            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (!obj.TryGetValue(field.Name, out var value) || value.Kind == JsonKind.Null)
                {
                    continue;
                }

                WalkValue(field, value, prefix.Length == 0 ? field.Name : prefix + "." + field.Name, depth, table, recordKey, issues);
            }
        }

        private static void WalkValue(
            FieldSchema field, JsonValue value, string path, int depth,
            string table, string recordKey, List<ValidationIssue> issues)
        {
            if (field.Curve != null && field.Curve.Shape == CurveShape.Breakpoints)
            {
                if (value is JsonArray raw && CurveSchema.TryReadBreakpoints(raw, out var curve, out _))
                {
                    var problem = Inspect(curve);
                    if (problem != null)
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, table, CheckName,
                            $"曲线字段 \"{path}\"：{problem}", recordKey, path));
                    }
                }

                // 形态不符（不是数组 / 元素缺 x、y）由 required_field/field_type 报过，不重复；
                // 曲线字段的元素也不再向内递归（{x, y} 里不会再有曲线）。
                return;
            }

            if (depth + 1 >= MaxDepth)
            {
                return;
            }

            if (field.Kind == FieldKind.Object && value is JsonObject obj)
            {
                if (field.Map != null)
                {
                    foreach (var kv in obj)
                    {
                        if (kv.Value.Kind == JsonKind.Null) continue;
                        WalkValue(field.Map.ValueSchema, kv.Value, path + "[" + kv.Key + "]", depth + 1, table, recordKey, issues);
                    }
                    return;
                }

                var variants = field.Variants;
                if (variants != null)
                {
                    WalkFields(variants.CommonFields, obj, path, depth + 1, table, recordKey, issues);
                    if (obj.TryGetValue(variants.Discriminator, out var disc) && disc is JsonString discStr &&
                        variants.Cases.TryGetValue(discStr.Value, out var caseFields))
                    {
                        WalkFields(caseFields, obj, path, depth + 1, table, recordKey, issues);
                    }
                    return;
                }

                WalkFields(field.Fields, obj, path, depth + 1, table, recordKey, issues);
            }
            else if (field.Kind == FieldKind.Array && value is JsonArray array)
            {
                var item = field.Item;
                if (item == null)
                {
                    return;
                }

                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i].Kind == JsonKind.Null) continue;
                    WalkValue(item, array[i], path + "[" + i + "]", depth + 1, table, recordKey, issues);
                }
            }
        }
    }
}
