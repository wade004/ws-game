using System;
using System.Collections.Generic;
using Core.Foundation.HookRegistry;
using Xunit;

namespace Tests.Foundation.HookRegistry
{
    public class HookArgsTests
    {
        // 1. Get<T> 按名字取值成功
        [Fact]
        public void Get_ReturnsTypedValue()
        {
            var args = new HookArgs(new Dictionary<string, object?> { ["sceneId"] = "world.map.town" });

            Assert.Equal("world.map.town", args.Get<string>("sceneId"));
        }

        // 2. TryGet 对不存在的名字返回 false
        [Fact]
        public void TryGet_MissingName_ReturnsFalse()
        {
            var args = HookArgs.Empty;

            var found = args.TryGet<string>("missing", out var value);

            Assert.False(found);
            Assert.Null(value);
        }

        // 3. TryGet 对类型不匹配返回 false
        [Fact]
        public void TryGet_WrongType_ReturnsFalse()
        {
            var args = new HookArgs(new Dictionary<string, object?> { ["count"] = 3 });

            var found = args.TryGet<string>("count", out var value);

            Assert.False(found);
            Assert.Null(value);
        }

        // 4. Get<T> 对不存在的名字抛异常
        [Fact]
        public void Get_MissingName_Throws()
        {
            var args = HookArgs.Empty;

            Assert.Throws<KeyNotFoundException>(() => args.Get<string>("missing"));
        }

        // 5. Get<T> 对类型不匹配抛异常
        [Fact]
        public void Get_WrongType_Throws()
        {
            var args = new HookArgs(new Dictionary<string, object?> { ["count"] = 3 });

            Assert.Throws<InvalidOperationException>(() => args.Get<string>("count"));
        }

        // 6. TryGet 对值为 null 且 T 为引用类型时返回 true（null 是合法值，不是"不存在"）
        [Fact]
        public void TryGet_NullValueForReferenceType_ReturnsTrue()
        {
            var args = new HookArgs(new Dictionary<string, object?> { ["reason"] = null });

            var found = args.TryGet<string>("reason", out var value);

            Assert.True(found);
            Assert.Null(value);
        }

        // 7. HookArgs.Empty 不含任何参数
        [Fact]
        public void Empty_ContainsNoKeys()
        {
            Assert.False(HookArgs.Empty.ContainsKey("anything"));
        }
    }
}
