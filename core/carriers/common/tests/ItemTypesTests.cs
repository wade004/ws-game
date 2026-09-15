using System;
using System.Collections.Generic;
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

        // -----------------------------------------------------------------
        // T-N2-8b：IInventoryHost 带身份的 AddItem/TryAddItem 默认接口成员——未覆盖本方法的既有
        // 实现（本测试专门验证这个默认实现本身，不是某个具体宿主）应转发到旧签名，丢弃身份，行为
        // 与改造前完全一致（见 IInventoryHost.AddItem(Id, Id, int, Id?, IReadOnlyList{Id}) 判断
        // 记录"默认接口实现 = 兼容退化"）。
        // -----------------------------------------------------------------

        /// <summary>只实现 <see cref="IInventoryHost"/> 最初的 5 个方法（<see cref="AddItem(Id, Id,
        /// int)"/>/<see cref="RemoveItem"/>/<see cref="ListItems"/>/<see cref="CountOf"/>/<see
        /// cref="FindInstance"/>），不覆盖 <see cref="TryAddItem(Id, Id, int, out int)"/>/<see
        /// cref="GetCapacity"/>/两个带身份重载——专门验证这些默认接口成员本身的转发行为，不是某个
        /// 具体宿主实现的正确性。</summary>
        private sealed class MinimalInventoryHost : IInventoryHost
        {
            public readonly List<(Id UnitId, Id TemplateId, int Count)> Calls = new List<(Id, Id, int)>();

            public bool AddItem(Id unitId, Id templateId, int count)
            {
                Calls.Add((unitId, templateId, count));
                return true;
            }

            public bool RemoveItem(Id unitId, Id instanceId, int count) => false;

            public IReadOnlyList<ItemInstance> ListItems(Id unitId) => Array.Empty<ItemInstance>();

            public int CountOf(Id unitId, Id templateId) => 0;

            public ItemInstance? FindInstance(Id unitId, Id instanceId) => null;
        }

        [Fact]
        public void IInventoryHost_AddItemWithIdentity_DefaultImplementation_ForwardsToPlainAddItem_DroppingIdentity()
        {
            IInventoryHost host = new MinimalInventoryHost();
            var unitId = new Id("player.hero");
            var templateId = new Id("item.iron_sword");
            var qualityId = new Id("item.quality.rare");
            var affixes = new[] { new Id("item.affix.a1") };

            var ok = host.AddItem(unitId, templateId, 2, qualityId, affixes);

            Assert.True(ok);
            var minimal = (MinimalInventoryHost)host;
            var call = Assert.Single(minimal.Calls);
            Assert.Equal(unitId, call.UnitId);
            Assert.Equal(templateId, call.TemplateId);
            Assert.Equal(2, call.Count);
        }

        [Fact]
        public void IInventoryHost_TryAddItemWithIdentity_DefaultImplementation_ForwardsToPlainTryAddItem_DroppingIdentity()
        {
            IInventoryHost host = new MinimalInventoryHost();
            var unitId = new Id("player.hero");
            var templateId = new Id("item.iron_sword");
            var qualityId = new Id("item.quality.rare");

            var ok = host.TryAddItem(unitId, templateId, 3, qualityId, affixes: null, out var actualCount);

            Assert.True(ok);
            Assert.Equal(3, actualCount);
            var minimal = (MinimalInventoryHost)host;
            var call = Assert.Single(minimal.Calls);
            Assert.Equal(unitId, call.UnitId);
            Assert.Equal(templateId, call.TemplateId);
            Assert.Equal(3, call.Count);
        }
    }
}
