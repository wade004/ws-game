using System;
using System.Collections.Generic;
using Core.Gameplay.AreaTrigger;
using Xunit;

namespace Tests.Gameplay.AreaTrigger
{
    /// <summary>
    /// T-L14 补充（测试覆盖剩余项 2026-10-01）：<see cref="AreaTriggerTypeNames"/> 与数据表 snake_case 文本互转。
    /// 重点是不变量而非逐值抄表：每个枚举成员都有文本且互不重复、文本是 snake_case、解析与输出互为逆运算、
    /// 解析严格区分大小写（数据表文本是权威写法）。
    /// </summary>
    public class AreaTriggerTypeNamesTests
    {
        private static IEnumerable<AreaTriggerType> AllTypes() => (AreaTriggerType[])Enum.GetValues(typeof(AreaTriggerType));

        [Fact]
        public void EveryEnumMember_HasText_AndTextsAreDistinctSnakeCase()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in AllTypes())
            {
                var text = AreaTriggerTypeNames.ToText(type);

                Assert.False(string.IsNullOrEmpty(text));
                Assert.All(text, c => Assert.True(c == '_' || (c >= 'a' && c <= 'z'), $"\"{text}\" 含非 snake_case 字符 '{c}'"));
                Assert.True(seen.Add(text), $"文本 \"{text}\" 被多个枚举成员占用");
            }
        }

        [Fact]
        public void TryParse_IsTheInverseOfToText_ForEveryMember()
        {
            foreach (var type in AllTypes())
            {
                Assert.True(AreaTriggerTypeNames.TryParse(AreaTriggerTypeNames.ToText(type), out var parsed));
                Assert.Equal(type, parsed);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("unknown_type")]
        [InlineData("Script")]
        [InlineData("SCRIPT")]
        [InlineData("MapTransition")]
        [InlineData(" script")]
        [InlineData("script ")]
        public void TryParse_UnknownOrMiscasedText_ReturnsFalse(string text)
        {
            Assert.False(AreaTriggerTypeNames.TryParse(text, out _));
        }

        [Fact]
        public void TryParse_NullText_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => AreaTriggerTypeNames.TryParse(null!, out _));
        }

        [Fact]
        public void ToText_UndefinedEnumValue_Throws()
        {
            Assert.ThrowsAny<Exception>(() => AreaTriggerTypeNames.ToText((AreaTriggerType)999));
        }
    }
}
