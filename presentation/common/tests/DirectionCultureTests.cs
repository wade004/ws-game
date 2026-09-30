using System.Globalization;
using Presentation.Common;
using Xunit;

namespace Tests.PresentationCommon
{
    /// <summary>测试覆盖梳理 T-H13（表现层补全）：<see cref="Direction.ToString"/> 的弧度插值必须固定走不变文化，
    /// 不随当前线程文化变（de-DE 小数逗号、tr-TR、sv-SE 负号 U+2212）。</summary>
    public class DirectionCultureTests
    {
        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void ToString_IsByteIdenticalUnderNonInvariantCulture(string culture)
        {
            var direction = new Direction(-1.5, 3, 8);
            var baseline = CultureScope.Run("", () => direction.ToString());
            var actual = CultureScope.Run(culture, () => direction.ToString());

            Assert.Equal(baseline, actual);
            // 防空测试：基准里确实有带小数点的数值，且是 ASCII 负号。
            Assert.Equal("Direction(raw=" + (-1.5).ToString(CultureInfo.InvariantCulture) + ", index=3/8)", baseline);
            Assert.Contains("-1.5", actual);
        }
    }
}
