using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.Events
{
    /// <summary>
    /// event_bus 构造/订阅/发布的空参与非法定义守卫（T-M15 core 半，2026-10-01 测试覆盖第四批）：
    /// <c>Subscribe/Enqueue/PublishImmediate(null)</c>、<see cref="EventDefinition"/> 空 domain / 空 fields、
    /// <see cref="EventCatalog.FromDefinitions"/> 的 null / null 元素 / 重复 key。守卫失败后总线状态不受影响。
    /// </summary>
    public sealed class EventBusArgumentGuardTests
    {
        private static readonly Id KeyA = new Id("test.a");

        private static EventBus NewBus() => new EventBus(EventCatalog.FromDefinitions(new[]
        {
            new EventDefinition(KeyA, "test", new string[0]),
        }));

        [Fact]
        public void Constructor_NullCatalog_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new EventBus(null!));
        }

        [Fact]
        public void Subscribe_NullRawHandler_Throws_AndRegistersNothing()
        {
            var bus = NewBus();

            Assert.Throws<ArgumentNullException>(() => bus.Subscribe(KeyA, (Core.Foundation.EventBus.EventHandler)null!));

            bus.PublishImmediate(new GenericEvent(KeyA)); // 没有残留订阅者，不应抛
        }

        [Fact]
        public void Subscribe_NullTypedHandler_Throws()
        {
            var bus = NewBus();

            Assert.Throws<ArgumentNullException>(() => bus.Subscribe<GenericEvent>(KeyA, null!));
        }

        [Fact]
        public void Enqueue_Null_Throws_AndQueueStaysEmpty()
        {
            var bus = NewBus();

            Assert.Throws<ArgumentNullException>(() => bus.Enqueue(null!));

            Assert.Equal(0, bus.DispatchPending());
        }

        [Fact]
        public void PublishImmediate_Null_Throws()
        {
            var bus = NewBus();

            Assert.Throws<ArgumentNullException>(() => bus.PublishImmediate(null!));
        }

        [Fact]
        public void Enqueue_And_PublishImmediate_NullEvent_ThrowEvenInsideSuppressScope()
        {
            // 抑制作用域丢弃的是"合法事件"；null 参数仍按空参守卫抛出（守卫先于抑制判断）。
            var bus = NewBus();
            using (bus.SuppressDispatch())
            {
                Assert.Throws<ArgumentNullException>(() => bus.Enqueue(null!));
                Assert.Throws<ArgumentNullException>(() => bus.PublishImmediate(null!));
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void EventDefinition_NullOrEmptyDomain_Throws(string? domain)
        {
            var ex = Assert.Throws<ArgumentException>(() => new EventDefinition(KeyA, domain!, new string[0]));
            Assert.Equal("domain", ex.ParamName);
        }

        [Fact]
        public void EventDefinition_NullFields_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new EventDefinition(KeyA, "test", null!));
        }

        [Fact]
        public void EventDefinition_ValidArguments_ExposeThem()
        {
            var fields = new[] { "x", "y" };
            var def = new EventDefinition(KeyA, "test", fields, "说明");

            Assert.Equal(KeyA, def.Key);
            Assert.Equal("test", def.Domain);
            Assert.Equal(fields, def.Fields);
            Assert.Equal("说明", def.Description);
            Assert.Null(new EventDefinition(KeyA, "test", fields).Description);
        }

        [Fact]
        public void Catalog_FromDefinitions_Null_NullElement_AndDuplicateKey_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => EventCatalog.FromDefinitions(null!));
            Assert.Throws<ArgumentException>(() => EventCatalog.FromDefinitions(new EventDefinition[] { null! }));
            Assert.Throws<InvalidOperationException>(() => EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(KeyA, "test", new string[0]),
                new EventDefinition(KeyA, "other", new string[0]),
            }));
        }

        [Fact]
        public void Catalog_Lookup_UnknownKey_IsNotRegistered_AndGetReturnsNull()
        {
            var catalog = EventCatalog.FromDefinitions(new[] { new EventDefinition(KeyA, "test", new string[0]) });

            Assert.True(catalog.IsRegistered(KeyA));
            Assert.False(catalog.IsRegistered(new Id("test.zzz")));
            Assert.Null(catalog.Get(new Id("test.zzz")));
            Assert.Single(catalog.All);
        }

        [Fact]
        public void StrictCatalog_UnregisteredKey_ThrowsOnEnqueueAndPublish_NamingTheCaller()
        {
            var catalog = EventCatalog.FromDefinitions(new[] { new EventDefinition(KeyA, "test", new string[0]) });
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = true });
            var ghost = new GenericEvent(new Id("test.ghost"));

            var enqueueEx = Assert.Throws<InvalidOperationException>(() => bus.Enqueue(ghost));
            var publishEx = Assert.Throws<InvalidOperationException>(() => bus.PublishImmediate(ghost));

            Assert.Contains("Enqueue", enqueueEx.Message);
            Assert.Contains("PublishImmediate", publishEx.Message);
            Assert.Contains("test.ghost", enqueueEx.Message);
            Assert.Equal(0, bus.DispatchPending());
        }
    }
}
