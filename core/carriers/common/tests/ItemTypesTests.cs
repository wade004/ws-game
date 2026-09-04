using System;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Xunit;

namespace Tests.Carriers.Common
{
    public class ItemTypesTests
    {
        [Fact]
        public void ItemStack_StoresTemplateAndCount()
        {
            var stack = new ItemStack(new Id("item.iron_sword"), 3);

            Assert.Equal(new Id("item.iron_sword"), stack.TemplateId);
            Assert.Equal(3, stack.Count);
        }

        [Fact]
        public void ItemStack_RejectsNonPositiveCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemStack(new Id("item.iron_sword"), 0));
        }

        [Fact]
        public void ItemStack_EqualityIsValueBased()
        {
            var a = new ItemStack(new Id("item.iron_sword"), 3);
            var b = new ItemStack(new Id("item.iron_sword"), 3);
            var c = new ItemStack(new Id("item.iron_sword"), 4);

            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.NotEqual(a, c);
        }

        [Fact]
        public void ItemInstance_DefaultsToEmptyExtra()
        {
            var instance = new ItemInstance(new Id("item.inst_1"), new Id("item.iron_sword"), 1);

            Assert.Empty(instance.Extra);
        }

        [Fact]
        public void ItemInstance_ToRef_CarriesSameInstanceId()
        {
            var instanceId = new Id("item.inst_1");
            var instance = new ItemInstance(instanceId, new Id("item.iron_sword"), 1);

            var reference = instance.ToRef();

            Assert.Equal(instanceId, reference.InstanceId);
        }

        [Fact]
        public void ItemInstance_ExtraKeepsProvidedFields()
        {
            var extra = new JsonObjectBuilder().Add("durability", new JsonNumber(80)).Build();
            var instance = new ItemInstance(new Id("item.inst_1"), new Id("item.iron_sword"), 1, extra);

            Assert.True(instance.Extra.TryGetValue("durability", out var value));
            Assert.Equal(80, ((JsonNumber)value).Value);
        }

        [Fact]
        public void ItemInstanceRef_EqualityIsByInstanceId()
        {
            var a = new ItemInstanceRef(new Id("item.inst_1"));
            var b = new ItemInstanceRef(new Id("item.inst_1"));
            var c = new ItemInstanceRef(new Id("item.inst_2"));

            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
            Assert.True(a != c);
        }
    }
}
