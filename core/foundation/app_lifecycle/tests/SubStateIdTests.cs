using System;
using Core.Foundation.AppLifecycle;
using Xunit;

namespace Tests.Foundation.AppLifecycle
{
    public class SubStateIdTests
    {
        // 1. 内置枚举构造与同名自定义字符串构造相等
        [Fact]
        public void BuiltinAndCustomStringWithSameName_AreEqual()
        {
            var fromEnum = new SubStateId(InWorldSubState.Combat);
            var fromString = new SubStateId("Combat");

            Assert.Equal(fromEnum, fromString);
            Assert.True(fromEnum == fromString);
        }

        // 2. 隐式转换：可直接传枚举值给期望 SubStateId 的入口
        [Fact]
        public void ImplicitConversion_FromEnum_ProducesExpectedName()
        {
            SubStateId id = InWorldSubState.Dialog;

            Assert.Equal("Dialog", id.Name);
        }

        // 3. 自定义子状态名为空时构造抛异常
        [Fact]
        public void Constructor_EmptyCustomName_Throws()
        {
            Assert.Throws<ArgumentException>(() => new SubStateId(string.Empty));
        }

        // 4. 不同名字不相等
        [Fact]
        public void DifferentNames_AreNotEqual()
        {
            Assert.NotEqual(SubStateId.Explore, SubStateId.Combat);
        }
    }
}
