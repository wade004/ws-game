using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;

namespace Core.Foundation.DataRegistry
{
    /// <summary>曲线横轴的语义，由登记该曲线字段的模块声明（数值总纲第 1 节"统一自变量 = 等级"；
    /// 落地清单 2.1 C1"横轴语义由字段登记声明（等级 / 物品等级 / 数值）"）。</summary>
    public enum CurveAxis
    {
        /// <summary>角色/怪物等级（<c>prog.level_curve</c>、<c>stat.rating_conversion</c>、
        /// <c>econ.gold_base_curve</c> 一类）；横轴取整数。</summary>
        Level,

        /// <summary>物品等级（<c>item.budget_curve</c>、<c>item.armor_curve</c> 一类）；横轴取整数。</summary>
        ItemLevel,

        /// <summary>任意数值（如护甲/抗性点数、评级点数）；横轴为实数。</summary>
        Value,
    }

    /// <summary>曲线的登记形态（分阶段落地计划 T-N0-1："一元断点表"与"二元输入的饱和形态"分开登记）。</summary>
    public enum CurveShape
    {
        /// <summary>一元断点表：<c>Array</c> 字段，元素 <c>{x, y}</c>，按 <c>x</c> 分段线性插值
        /// （运行时载体 <see cref="PiecewiseCurve"/>）。</summary>
        Breakpoints,

        /// <summary>二元饱和：<c>Object</c> 字段 <c>{k, cap}</c>，输入为"数值 × 等级"两个量，输出
        /// <c>value / (value + k × level)</c> 再以 <c>cap</c> 封顶（06 第 4 节护甲/抗性减免、数值设计
        /// 01 第 5 节"变态版：饱和曲线，除数随等级增长"）。本类型只登记形态与参数字段，不提供求值
        /// ——既有 <c>combat.resist_curve</c> 的 <c>saturation</c> 分支公式保持原样（T-N0-5 禁止事项），
        /// 换算层的饱和形态求值随阶段 N1 的换算层任务落地。</summary>
        Saturation,
    }

    /// <summary>
    /// 曲线字段的登记形态（分阶段落地计划 T-N0-1；落地清单 2.1 C1；数值总纲第 3 节原则 1）：把
    /// "这个字段是一条曲线、横轴是什么"作为机器可读元数据挂到 <see cref="FieldSchema"/> 上
    /// （<see cref="FieldSchema.WithCurve"/>），使通用曲线校验规则（<c>curve_monotonic_finite</c>，
    /// T-N0-3）、内容工具与运行时解析（<see cref="ReadBreakpoints(DataRecord, string)"/>）都能只
    /// 认这一份登记，而不是各模块各自约定一套 <c>{item_level, budget}</c>/<c>{level, points_per_percent}</c>
    /// 之类的私有子字段名。
    /// <para>
    /// 判断记录：
    /// </para>
    /// <list type="number">
    /// <item><description><b>不新增 <see cref="FieldKind"/></b>：曲线仍是 <see cref="FieldKind.Array"/>
    /// （断点表）或 <see cref="FieldKind.Object"/>（饱和）字段，子结构沿 ADR-0019 的
    /// <see cref="FieldSchema.Item"/>/<see cref="FieldSchema.Fields"/> 登记，加载期的必填/类型/范围
    /// 检查全部复用既有检查项；本类型只是附加的形态标记 + 生成这套标准子结构的工厂。与
    /// <see cref="MapSchema"/> 一样，登记在错误种类字段上的错误刻意不在 <see cref="FieldSchema.WithCurve"/>
    /// 挂载时检查，交由 <c>SchemaAudit</c> 的 <c>field_curve_shape</c> 检查项事后报告。</description></item>
    /// <item><description><b>子字段名固定为 <c>x</c>/<c>y</c></b>（<see cref="XFieldName"/>/
    /// <see cref="YFieldName"/>）：横轴语义由 <see cref="Axis"/> 表达而不是靠子字段名（<c>item_level</c>
    /// 还是 <c>level</c>）区分，这样通用规则与工具不必逐表维护字段名映射。等级类横轴
    /// （<see cref="CurveAxis.Level"/>/<see cref="CurveAxis.ItemLevel"/>）的 <c>x</c> 登记为
    /// <see cref="FieldKind.Int"/>，数值横轴登记为 <see cref="FieldKind.Number"/>——与迁移前各表对
    /// 等级列的 <c>Int</c> 登记强度一致（T-N0-4 迁移不放宽类型）。</description></item>
    /// <item><description><b>饱和形态单独登记、不并入断点表</b>：它的输入是二元的（数值 × 等级），
    /// 无法用一元断点表表达；两种形态共用 <see cref="CurveSchema"/> 这一个元数据类型只是为了让
    /// "该字段是曲线"这个事实有单一查询入口，求值路径完全不同。</description></item>
    /// <item><description><b>解析入口放在本类型而不是 <see cref="PiecewiseCurve"/></b>：依赖方向是
    /// <c>data_registry → common</c>，<c>common</c> 不能认识 <see cref="DataRecord"/>/JSON 记录结构。</description></item>
    /// </list>
    /// </summary>
    public sealed class CurveSchema
    {
        /// <summary>断点表元素的横轴子字段名。</summary>
        public const string XFieldName = "x";

        /// <summary>断点表元素的纵轴子字段名。</summary>
        public const string YFieldName = "y";

        /// <summary>断点表元素登记的占位名（沿 ADR-0019 惯例，Array 元素的 <c>Name</c> 不出现在校验路径里）。</summary>
        public const string BreakpointItemName = "<curve_entry>";

        /// <summary>饱和形态的除数系数子字段名（<c>value / (value + k × level)</c> 中的 <c>k</c>，&gt; 0）。</summary>
        public const string SaturationKFieldName = "k";

        /// <summary>饱和形态的输出封顶子字段名（可选，缺省 1，&gt; 0）。</summary>
        public const string SaturationCapFieldName = "cap";

        /// <summary>登记形态。</summary>
        public CurveShape Shape { get; }

        /// <summary>横轴语义：断点表为 <c>x</c> 的语义；饱和形态恒为 <see cref="CurveAxis.Value"/>
        /// （主输入是数值，第二输入恒为等级）。</summary>
        public CurveAxis Axis { get; }

        private CurveSchema(CurveShape shape, CurveAxis axis)
        {
            Shape = shape;
            Axis = axis;
        }

        /// <summary>一元断点表形态，横轴语义为 <paramref name="axis"/>。</summary>
        public static CurveSchema Breakpoints(CurveAxis axis) => new CurveSchema(CurveShape.Breakpoints, axis);

        /// <summary>二元饱和形态（数值 × 等级 → 0～cap）。</summary>
        public static CurveSchema Saturation() => new CurveSchema(CurveShape.Saturation, CurveAxis.Value);

        /// <summary>生成一条按标准形态登记的断点表字段：<see cref="FieldKind.Array"/>，元素
        /// <c>{x, y}</c>（<c>x</c> 按 <paramref name="axis"/> 取 <see cref="FieldKind.Int"/> 或
        /// <see cref="FieldKind.Number"/>，<c>y</c> 为 <see cref="FieldKind.Number"/>，两者必填），
        /// 并挂上 <see cref="Breakpoints"/> 形态标记。<paramref name="xRange"/>/<paramref name="yRange"/>
        /// 可选登记 ADR-0021 范围约束（如 <c>y &gt; 0</c>）。</summary>
        public static FieldSchema BreakpointsField(
            string name,
            CurveAxis axis,
            bool required,
            string description,
            string? xDescription = null,
            string? yDescription = null,
            FieldRange? xRange = null,
            FieldRange? yRange = null)
        {
            if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("description 不能为空", nameof(description));

            var x = new FieldSchema(XFieldName, axis == CurveAxis.Value ? FieldKind.Number : FieldKind.Int, required: true,
                description: xDescription ?? DefaultXDescription(axis));
            if (xRange != null) x.WithRange(xRange);

            var y = new FieldSchema(YFieldName, FieldKind.Number, required: true,
                description: yDescription ?? "该横轴取值对应的曲线值");
            if (yRange != null) y.WithRange(yRange);

            var item = new FieldSchema(BreakpointItemName, FieldKind.Object, required: true,
                fields: new[] { x, y },
                description: "{x, y}，曲线的一个断点；横轴语义见所属字段登记的 CurveSchema.Axis");

            return new FieldSchema(name, FieldKind.Array, required, item: item, description: description)
                .WithCurve(Breakpoints(axis));
        }

        /// <summary>生成一条按标准形态登记的饱和曲线字段：<see cref="FieldKind.Object"/>，子字段
        /// <c>k</c>（必填，&gt; 0）与 <c>cap</c>（可选，&gt; 0，缺省 1），并挂上 <see cref="Saturation"/>
        /// 形态标记。</summary>
        public static FieldSchema SaturationField(
            string name,
            bool required,
            string description,
            string? kDescription = null,
            string? capDescription = null)
        {
            if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("description 不能为空", nameof(description));

            var k = new FieldSchema(SaturationKFieldName, FieldKind.Number, required: true,
                description: kDescription ?? "除数系数：输出 = value / (value + k × level)，> 0")
                .WithRange(FieldRange.Range(min: 0, minExclusive: true));
            var cap = new FieldSchema(SaturationCapFieldName, FieldKind.Number, required: false,
                description: capDescription ?? "输出封顶，> 0，缺省 1")
                .WithRange(FieldRange.Range(min: 0, minExclusive: true));

            return new FieldSchema(name, FieldKind.Object, required, fields: new[] { k, cap }, description: description)
                .WithCurve(Saturation());
        }

        /// <summary>从记录的断点表字段解析出 <see cref="PiecewiseCurve"/>（按 <c>x</c> 稳定排序）。
        /// 字段缺失/不是数组，或某个元素不是 <c>{x: Number, y: Number}</c> 对象时抛
        /// <see cref="DataFieldException"/>（沿 <see cref="DataRecord"/> 类型化访问器"调用方明知字段
        /// 应当存在却读错类型属编程错误"的既有约定；正常数据缺失应已被 <c>required_field</c>/
        /// <c>field_type</c> 在加载期拦下）。</summary>
        public static PiecewiseCurve ReadBreakpoints(DataRecord record, string fieldName)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrEmpty(fieldName)) throw new ArgumentException("fieldName 不能为空", nameof(fieldName));

            var raw = record.GetArray(fieldName);
            var points = new CurvePoint[raw.Count];
            for (var i = 0; i < raw.Count; i++)
            {
                if (!TryReadPoint(raw[i], out points[i]))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, fieldName + "[" + i + "]",
                        "断点表元素不是 {x: Number, y: Number} 对象");
                }
            }

            return new PiecewiseCurve(points);
        }

        /// <summary>从原始 JSON 数组解析断点表（供校验规则在 <see cref="DataRecord"/> 之外的
        /// 场景复用）；元素不合形态时返回 false 并给出首个不合元素的下标，不抛异常。</summary>
        public static bool TryReadBreakpoints(JsonArray raw, out PiecewiseCurve curve, out int badIndex)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));

            var points = new CurvePoint[raw.Count];
            for (var i = 0; i < raw.Count; i++)
            {
                if (!TryReadPoint(raw[i], out points[i]))
                {
                    curve = PiecewiseCurve.Empty;
                    badIndex = i;
                    return false;
                }
            }

            curve = new PiecewiseCurve(points);
            badIndex = -1;
            return true;
        }

        /// <summary>迁移环节公共实现（T-N0-4；ADR-0029 迁移链）：把 <paramref name="row"/> 里
        /// <paramref name="fieldName"/> 数组的每个对象元素中，键 <paramref name="oldXName"/>/
        /// <paramref name="oldYName"/> 改名为 <see cref="XFieldName"/>/<see cref="YFieldName"/>，其余键与
        /// 顺序原样保留；非对象元素、字段缺失或不是数组时原样透传（形状问题留给迁移后的字段级校验报告，
        /// 迁移函数不做校验——沿 <c>BuiltinSchemas.MigrationSample</c> 的迁移环节惯例）。输入不被修改。</summary>
        public static JsonObject MigrateBreakpointsFieldNames(JsonObject row, string fieldName, string oldXName, string oldYName)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (string.IsNullOrEmpty(fieldName)) throw new ArgumentException("fieldName 不能为空", nameof(fieldName));
            if (string.IsNullOrEmpty(oldXName)) throw new ArgumentException("oldXName 不能为空", nameof(oldXName));
            if (string.IsNullOrEmpty(oldYName)) throw new ArgumentException("oldYName 不能为空", nameof(oldYName));

            var builder = new JsonObjectBuilder();
            foreach (var entry in row)
            {
                if (entry.Key == fieldName && entry.Value is JsonArray array)
                {
                    var migrated = new JsonValue[array.Count];
                    for (var i = 0; i < array.Count; i++)
                    {
                        migrated[i] = array[i] is JsonObject element ? RenameKeys(element, oldXName, oldYName) : array[i];
                    }
                    builder.Add(entry.Key, new JsonArray(migrated));
                }
                else
                {
                    builder.Add(entry.Key, entry.Value);
                }
            }

            return builder.Build();
        }

        private static JsonObject RenameKeys(JsonObject element, string oldXName, string oldYName)
        {
            var builder = new JsonObjectBuilder();
            foreach (var kv in element)
            {
                var key = kv.Key == oldXName ? XFieldName : (kv.Key == oldYName ? YFieldName : kv.Key);
                builder.Add(key, kv.Value);
            }
            return builder.Build();
        }

        private static bool TryReadPoint(JsonValue element, out CurvePoint point)
        {
            if (element is JsonObject obj &&
                obj.TryGetValue(XFieldName, out var xRaw) && xRaw is JsonNumber xNum &&
                obj.TryGetValue(YFieldName, out var yRaw) && yRaw is JsonNumber yNum)
            {
                point = new CurvePoint(xNum.Value, yNum.Value);
                return true;
            }

            point = default;
            return false;
        }

        private static string DefaultXDescription(CurveAxis axis)
        {
            switch (axis)
            {
                case CurveAxis.Level: return "横轴：等级（整数）";
                case CurveAxis.ItemLevel: return "横轴：物品等级（整数）";
                default: return "横轴：数值";
            }
        }
    }
}
