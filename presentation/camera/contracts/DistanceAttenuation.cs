using System;
using System.Globalization;

namespace Presentation.Camera
{
    /// <summary>
    /// 镜头冲击距离衰减取值（<c>camera_distance_attenuation</c>，手感设计/07 第 2 节，ADR-0148）。四类写法：
    /// <list type="bullet">
    /// <item><c>none</c>（缺省，空串同义）：不衰减。</item>
    /// <item><c>linear:&lt;跨度&gt;</c>：真线性衰减——命中点到镜头拥有者的距离（身高倍数）为 0 时系数 1，到跨度时降为 0，其间线性。</item>
    /// <item>曲线 id：按分段曲线（横轴身高倍数）取系数，需装配根注入曲线解析。</item>
    /// <item>裸 <c>linear</c>：旧数据把"不衰减"写成这个值；行为保持不衰减，运行期提示迁移到 <c>none</c>（ADR-0039，不静默改手感）。</item>
    /// </list>
    /// </summary>
    public static class DistanceAttenuation
    {
        public const string None = "none";

        public const string LegacyLinear = "linear";

        public const string LinearPrefix = "linear:";

        /// <summary>解析 <c>linear:&lt;跨度&gt;</c>；跨度必须是正的有限数。</summary>
        public static bool TryParseLinearSpan(string value, out double span)
        {
            span = 0;
            if (value == null || !value.StartsWith(LinearPrefix, StringComparison.Ordinal))
            {
                return false;
            }
            if (!double.TryParse(value.Substring(LinearPrefix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                || !(parsed > 0) || double.IsInfinity(parsed))
            {
                return false;
            }
            span = parsed;
            return true;
        }

        /// <summary>线性衰减系数：<c>1 - 距离 / 跨度</c>，夹到 [0, 1]。</summary>
        public static double LinearFactor(double bodyHeights, double span)
        {
            if (!(span > 0)) return 1.0;
            var f = 1.0 - bodyHeights / span;
            return f < 0 ? 0.0 : (f > 1.0 ? 1.0 : f);
        }

        /// <summary>取值是否合法的写法（none / 空 / 旧 linear / linear:跨度 / 其它视为曲线 id）；格式错误的 <c>linear:</c> 返回 false。</summary>
        public static bool IsWellFormed(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            if (value.StartsWith(LinearPrefix, StringComparison.Ordinal)) return TryParseLinearSpan(value, out _);
            return true;
        }
    }
}
