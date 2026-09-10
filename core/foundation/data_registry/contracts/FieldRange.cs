using System;
using System.Globalization;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// ADR-0021（04 第 4 节勘误"范围约束"）：<see cref="FieldKind.Number"/>/<see cref="FieldKind.Int"/>
    /// 字段的可选数值范围约束——<see cref="Min"/>/<see cref="Max"/> 至少设一个，各自独立选择是否
    /// 含端点（<see cref="MinExclusive"/>/<see cref="MaxExclusive"/>）。由
    /// <see cref="FieldSchema.WithRange"/> 挂到具体字段上，<c>DataRegistry.LoadAll</c> 在字段级
    /// 校验阶段（<c>field_range</c> 检查项）据此拒绝越界的实际数据；<c>SchemaAudit</c> 额外检查
    /// "只能挂在 Number/Int 字段上"这条与具体字段绑定后才能判断的约束（见该类型判断记录）。
    /// <para>
    /// 判断记录：<see cref="Range"/> 是唯一构造入口，在此处（而非事后由 <c>DataRegistry</c>/
    /// <c>SchemaAudit</c> 补救）立即校验"这份范围声明本身是否自洽"——min/max 至少一个、
    /// <c>min &lt;= max</c>、以及 <c>min == max</c> 时不能有任何一端声明排除（否则区间为空集，
    /// 任何数据都无法通过，几乎必是登记错误）——这是纯粹的值级不变量，不依赖挂在哪个字段/哪种
    /// <see cref="FieldKind"/> 上，与 <see cref="FieldSchema"/> 构造函数里"Enum 必须有 EnumValues"
    /// 一类立即失败的既有风格一致。"只能用于数值种类"这条不在这里检查——它依赖 <see cref="FieldSchema.Kind"/>，
    /// 只有把 <see cref="FieldRange"/> 挂到具体字段之后才能判断，且刻意选择让这类登记错误停留在
    /// "可枚举、可被 SchemaAudit 报告、可被测试断言"的软失败形态，而不是让 <c>WithRange</c>
    /// 直接抛异常——后者会让"登记在错误种类字段上"这个错误只能在启动路径上以异常形式出现一次，
    /// 无法被 <c>--schema-audit</c> 这类静态门禁批量枚举出全部违规点（见 <c>SchemaAudit.WalkField</c>
    /// 判断记录、任务测试"非数值种类登记范围被 SchemaAudit 拒绝"）。
    /// </para>
    /// </summary>
    public sealed class FieldRange
    {
        public double? Min { get; }

        /// <summary><c>true</c> 表示 <see cref="Min"/> 不含端点本身（<c>&gt;</c>）；<c>false</c>
        /// 表示含端点（<c>&gt;=</c>）。<see cref="Min"/> 为 null 时无意义。</summary>
        public bool MinExclusive { get; }

        public double? Max { get; }

        /// <summary>同 <see cref="MinExclusive"/>，作用于 <see cref="Max"/>。</summary>
        public bool MaxExclusive { get; }

        private FieldRange(double? min, bool minExclusive, double? max, bool maxExclusive)
        {
            Min = min;
            MinExclusive = minExclusive;
            Max = max;
            MaxExclusive = maxExclusive;
        }

        /// <summary>
        /// 构造一个范围约束。示例：<c>Range(min: 0, minExclusive: true)</c> 表示 <c>&gt; 0</c>；
        /// <c>Range(min: 0)</c> 表示 <c>&gt;= 0</c>；<c>Range(min: 0, max: 1)</c> 表示 <c>[0, 1]</c>。
        /// </summary>
        public static FieldRange Range(double? min = null, bool minExclusive = false, double? max = null, bool maxExclusive = false)
        {
            if (min == null && max == null)
            {
                throw new ArgumentException("Min 与 Max 至少指定一个", nameof(min));
            }

            if (min != null && max != null)
            {
                if (min.Value > max.Value)
                {
                    throw new ArgumentException($"Min({min.Value.ToString(CultureInfo.InvariantCulture)}) 不能大于 Max({max.Value.ToString(CultureInfo.InvariantCulture)})", nameof(max));
                }

                if (min.Value == max.Value && (minExclusive || maxExclusive))
                {
                    throw new ArgumentException(
                        $"Min == Max == {min.Value.ToString(CultureInfo.InvariantCulture)} 时端点不能被排除（区间会变为空集）",
                        nameof(minExclusive));
                }
            }

            return new FieldRange(min, minExclusive, max, maxExclusive);
        }

        /// <summary><paramref name="value"/> 是否落在本范围内（含义见 <see cref="MinExclusive"/>/
        /// <see cref="MaxExclusive"/>）。</summary>
        public bool Contains(double value)
        {
            if (Min.HasValue)
            {
                if (MinExclusive ? value <= Min.Value : value < Min.Value) return false;
            }

            if (Max.HasValue)
            {
                if (MaxExclusive ? value >= Max.Value : value > Max.Value) return false;
            }

            return true;
        }

        /// <summary>人类可读区间描述，如 <c>"(0, +∞)"</c>、<c>"[0, 1]"</c>，供校验消息与
        /// <c>toolchain/validator --list-tables --json</c> 导出使用。</summary>
        public string Describe()
        {
            var left = Min.HasValue ? (MinExclusive ? "(" : "[") + FormatNumber(Min.Value) : "(-∞";
            var right = Max.HasValue ? FormatNumber(Max.Value) + (MaxExclusive ? ")" : "]") : "+∞)";
            return left + ", " + right;
        }

        private static string FormatNumber(double v)
        {
            if (!double.IsInfinity(v) && v == Math.Floor(v) && Math.Abs(v) < 1e15)
            {
                return ((long)v).ToString(CultureInfo.InvariantCulture);
            }

            return v.ToString(CultureInfo.InvariantCulture);
        }
    }
}
