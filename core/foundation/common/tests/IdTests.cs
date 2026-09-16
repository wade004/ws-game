using System;
using Core.Foundation.Common;
using Xunit;

namespace Tests.Foundation.Common
{
    public class IdTests
    {
        [Theory]
        [InlineData("skill.fireball")]
        [InlineData("item.iron_sword")]
        [InlineData("quest.find_the_missing_child")]
        [InlineData("a.b.c")]
        [InlineData("world.map_01")]
        public void Constructor_AcceptsLegalFormat(string value)
        {
            var id = new Id(value);
            Assert.Equal(value, id.Value);
        }

        [Theory]
        [InlineData("Skill.Fireball")] // 大写
        [InlineData("fireball")] // 缺 domain（没有点）
        [InlineData("skill-fireball")] // 连字符
        [InlineData("技能.火球")] // 非 ASCII
        [InlineData("")] // 空字符串
        [InlineData(".fireball")] // domain 为空
        [InlineData("skill.")] // name 段为空
        public void Constructor_RejectsIllegalFormat(string value)
        {
            Assert.Throws<ArgumentException>(() => new Id(value));
        }

        [Fact]
        public void Constructor_RejectsNull()
        {
            Assert.Throws<ArgumentException>(() => new Id(null!));
        }

        [Fact]
        public void Domain_ReturnsFirstSegment()
        {
            Assert.Equal("skill", new Id("skill.fireball").Domain);
            Assert.Equal("a", new Id("a.b.c").Domain);
        }

        [Fact]
        public void Equality_IsOrdinalAndCaseSensitive()
        {
            var a = new Id("skill.fireball");
            var b = new Id("skill.fireball");
            var c = new Id("skill.frostbolt");

            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.False(a == c);
            Assert.True(a != c);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void TryParse_ReturnsTrueForLegalFormat()
        {
            var ok = Id.TryParse("skill.fireball", out var id);
            Assert.True(ok);
            Assert.Equal("skill.fireball", id.Value);
        }

        [Fact]
        public void TryParse_ReturnsFalseForIllegalFormat()
        {
            var ok = Id.TryParse("Not Legal", out var id);
            Assert.False(ok);
            Assert.Equal(default, id);
        }

        [Fact]
        public void TryParse_ReturnsFalseForNull()
        {
            var ok = Id.TryParse(null, out _);
            Assert.False(ok);
        }

        // -----------------------------------------------------------------
        // 消费方反馈第 47 条：DataRegistry 字段级 field_id_format 检查直接调用
        // IsValidFormat（与构造函数/TryParse 同一份判定实现），补一组直接针对该方法的
        // 正反例，覆盖反馈原文的触发形态（空字符串）。
        // -----------------------------------------------------------------

        [Theory]
        [InlineData("skill.fireball")]
        [InlineData("a.b.c")]
        [InlineData("world.map_01")]
        public void IsValidFormat_ReturnsTrueForLegalMultiSegmentFormat(string value)
        {
            Assert.True(Id.IsValidFormat(value));
        }

        [Theory]
        [InlineData("")] // 空字符串（消费方反馈第 47 条触发形态：必填 Id 字段留空）
        [InlineData(" ")] // 含空格
        [InlineData("Skill.Fireball")] // 大写
        [InlineData("fireball")] // 缺 domain
        [InlineData("skill-fireball")] // 非法字符（连字符）
        [InlineData(null)]
        public void IsValidFormat_ReturnsFalseForIllegalFormat(string? value)
        {
            Assert.False(Id.IsValidFormat(value));
        }

        [Fact]
        public void CompareTo_IsOrdinal()
        {
            var a = new Id("skill.a");
            var b = new Id("skill.b");
            Assert.True(a.CompareTo(b) < 0);
            Assert.True(b.CompareTo(a) > 0);
            Assert.Equal(0, a.CompareTo(a));
        }

        [Fact]
        public void ToString_ReturnsValue()
        {
            var id = new Id("skill.fireball");
            Assert.Equal("skill.fireball", id.ToString());
        }
    }
}
