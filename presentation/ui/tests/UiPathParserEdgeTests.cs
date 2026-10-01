using System;
using System.Linq;
using Presentation.Ui;
using Xunit;

namespace Tests.PresentationUi
{
    /// <summary>
    /// 测试覆盖剩余项 T-M39：<see cref="UiPathParser.TryParse"/> 余下的错误输入——未闭合括号、负数下标、超长输入，
    /// 以及复现用例（下标数值溢出 int、非 ASCII 数字、尾随换行）。契约：TryParse 对任何输入都不抛异常，
    /// 非法返回 false 且 segments 为空列表。
    /// </summary>
    public class UiPathParserEdgeTests
    {
        [Theory]
        [InlineData("player.inventory[3")]
        [InlineData("player.inventory[")]
        [InlineData("player.inventory3]")]
        [InlineData("player.inventory[3]]")]
        [InlineData("player.inventory[[3]")]
        [InlineData("player.inventory[]")]
        [InlineData("player.inventory[3][4]")]
        [InlineData("[3].player")]
        public void Rejects_unclosed_empty_or_malformed_brackets(string path)
        {
            Assert.False(UiPathParser.TryParse(path, out var segments));
            Assert.Empty(segments);
        }

        [Theory]
        [InlineData("player.inventory[-1]")]
        [InlineData("player.inventory[+1]")]
        [InlineData("player.inventory[1.5]")]
        [InlineData("player.inventory[ 1]")]
        [InlineData("player.inventory[0x10]")]
        public void Rejects_negative_signed_or_non_decimal_index(string path)
        {
            Assert.False(UiPathParser.TryParse(path, out var segments));
            Assert.Empty(segments);
        }

        [Theory]
        [InlineData(" player.level")]
        [InlineData("player.level ")]
        [InlineData("player. level")]
        [InlineData(".player")]
        [InlineData("player.")]
        [InlineData("player-level")]
        [InlineData("1player.level")]
        [InlineData("_player.level")]
        public void Rejects_whitespace_edge_dots_hyphens_and_bad_leading_characters(string path)
        {
            Assert.False(UiPathParser.TryParse(path, out var segments));
            Assert.Empty(segments);
        }

        [Fact]
        public void Accepts_the_largest_int_index_and_leading_zeros()
        {
            Assert.True(UiPathParser.TryParse($"player.inventory[{int.MaxValue}].count", out var max));
            Assert.Equal(int.MaxValue, max[1].Index);

            Assert.True(UiPathParser.TryParse("player.inventory[007].count", out var zeros));
            Assert.Equal(7, zeros[1].Index);
        }

        [Fact]
        public void Accepts_a_very_long_but_well_formed_path_and_keeps_every_segment()
        {
            const int count = 2000;
            var path = string.Join(".", Enumerable.Range(0, count).Select(i => "seg" + i));

            Assert.True(UiPathParser.TryParse(path, out var segments));
            Assert.Equal(count, segments.Count);
            Assert.Equal("seg0", segments[0].Name);
            Assert.Equal("seg" + (count - 1), segments[count - 1].Name);
        }

        [Fact]
        public void Rejects_a_very_long_malformed_path_without_throwing()
        {
            var path = string.Join(".", Enumerable.Range(0, 2000).Select(i => "seg" + i)) + "..tail";

            Assert.False(UiPathParser.TryParse(path, out var segments));
            Assert.Empty(segments);
        }

        [Fact]
        public void Accepts_a_very_long_segment_name()
        {
            var name = "a" + new string('b', 100000);

            Assert.True(UiPathParser.TryParse(name + "[1]", out var segments));
            Assert.Equal(name, segments[0].Name);
            Assert.Equal(1, segments[0].Index);
        }

        [Fact]
        public void UiPathSegment_ToString_RoundTripsNameAndIndex()
        {
            Assert.True(UiPathParser.TryParse("player.inventory[12].count", out var segments));

            Assert.Equal("player.inventory[12].count", string.Join(".", segments.Select(s => s.ToString())));
        }

        // ------------------------------------------------------------------ 复现：以下三例在修复前红

        /// <summary>下标是十进制数字但超出 int 范围：修复前 <c>int.Parse</c> 抛 <see cref="OverflowException"/>，
        /// 破坏 TryParse “非法输入返回 false”的契约，并经 <c>UiDataSource.Query</c> 把异常抛进 UI 绑定。</summary>
        [Theory]
        [InlineData("player.inventory[2147483648]")]
        [InlineData("player.inventory[99999999999999999999999999]")]
        public void Index_beyond_int_range_returns_false_instead_of_throwing(string path)
        {
            var thrown = Record.Exception(() => UiPathParser.TryParse(path, out _));
            Assert.Null(thrown);

            Assert.False(UiPathParser.TryParse(path, out var segments));
            Assert.Empty(segments);
        }

        /// <summary>Regex 的 <c>\d</c> 默认匹配全部 Unicode 十进制数字；修复前阿拉伯-印度数字、全角数字通过正则后
        /// 在 <c>int.Parse</c> 处抛 <see cref="FormatException"/>。只接受 ASCII 数字，其余返回 false。</summary>
        [Theory]
        [InlineData("player.inventory[٣]")] // ARABIC-INDIC DIGIT THREE
        [InlineData("player.inventory[３]")] // FULLWIDTH DIGIT THREE
        public void Non_ascii_digits_in_index_return_false_instead_of_throwing(string path)
        {
            var thrown = Record.Exception(() => UiPathParser.TryParse(path, out _));
            Assert.Null(thrown);

            Assert.False(UiPathParser.TryParse(path, out var segments));
            Assert.Empty(segments);
        }

        /// <summary>.NET 正则里 <c>$</c> 会匹配末尾换行之前：修复前 <c>"player.level\n"</c> 被当作合法路径解析成
        /// <c>player</c>/<c>level</c>，换行被静默吞掉。路径语法是严格的小写点分段，不应容忍尾随换行。</summary>
        [Theory]
        [InlineData("player.level\n")]
        [InlineData("player.inventory[1]\n")]
        public void Trailing_newline_is_rejected_not_silently_swallowed(string path)
        {
            Assert.False(UiPathParser.TryParse(path, out var segments));
            Assert.Empty(segments);
        }
    }
}
