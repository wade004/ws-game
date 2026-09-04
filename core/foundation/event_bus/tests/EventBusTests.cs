using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.Events
{
    public class EventBusTests
    {
        private sealed class TestEvent : IEvent
        {
            public Id Key { get; }

            public int Value { get; }

            public TestEvent(Id key, int value = 0)
            {
                Key = key;
                Value = value;
            }
        }

        private sealed class OtherEvent : IEvent
        {
            public Id Key { get; }

            public OtherEvent(Id key)
            {
                Key = key;
            }
        }

        private static EventCatalog MakeCatalog(params string[] keys)
        {
            var definitions = new List<EventDefinition>();
            foreach (var key in keys)
            {
                var id = new Id(key);
                definitions.Add(new EventDefinition(id, id.Domain, Array.Empty<string>()));
            }

            return EventCatalog.FromDefinitions(definitions);
        }

        // 1. 订阅/入队/派发：三个订阅者按注册顺序收到
        [Fact]
        public void DispatchPending_CallsSubscribersInRegistrationOrder()
        {
            var catalog = MakeCatalog("test.a");
            var bus = new EventBus(catalog);
            var key = new Id("test.a");
            var order = new List<string>();

            bus.Subscribe(key, evt => order.Add("first"));
            bus.Subscribe(key, evt => order.Add("second"));
            bus.Subscribe(key, evt => order.Add("third"));

            bus.Enqueue(new TestEvent(key));
            var dispatched = bus.DispatchPending();

            Assert.Equal(1, dispatched);
            Assert.Equal(new[] { "first", "second", "third" }, order);
        }

        // 2. 入队顺序 = 派发顺序（10 个事件）
        [Fact]
        public void DispatchPending_PreservesEnqueueOrder()
        {
            var catalog = MakeCatalog("test.a");
            var bus = new EventBus(catalog);
            var key = new Id("test.a");
            var received = new List<int>();
            bus.Subscribe<TestEvent>(key, evt => received.Add(evt.Value));

            for (var i = 0; i < 10; i++)
            {
                bus.Enqueue(new TestEvent(key, i));
            }

            var dispatched = bus.DispatchPending();

            Assert.Equal(10, dispatched);
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, received);
        }

        // 3. 派发前订阅者收不到（批处理语义）；PublishImmediate 立即收到
        [Fact]
        public void Enqueue_DoesNotDispatchUntilDispatchPendingCalled()
        {
            var catalog = MakeCatalog("test.a");
            var bus = new EventBus(catalog);
            var key = new Id("test.a");
            var received = false;
            bus.Subscribe(key, evt => received = true);

            bus.Enqueue(new TestEvent(key));
            Assert.False(received);

            bus.DispatchPending();
            Assert.True(received);
        }

        [Fact]
        public void PublishImmediate_DispatchesSynchronously()
        {
            var catalog = MakeCatalog("test.a");
            var bus = new EventBus(catalog);
            var key = new Id("test.a");
            var received = false;
            bus.Subscribe(key, evt => received = true);

            bus.PublishImmediate(new TestEvent(key));

            Assert.True(received);
        }

        // 4. 派发过程中入队的事件在同一次 DispatchPending 内被派发；MaxDispatchPasses 触发时停止并记错误
        [Fact]
        public void DispatchPending_DispatchesEventsEnqueuedDuringDispatchInSameCall()
        {
            var catalog = MakeCatalog("test.a", "test.b");
            var bus = new EventBus(catalog);
            var keyA = new Id("test.a");
            var keyB = new Id("test.b");
            var log = new List<string>();

            bus.Subscribe(keyA, evt =>
            {
                log.Add("a");
                bus.Enqueue(new TestEvent(keyB));
            });
            bus.Subscribe(keyB, evt => log.Add("b"));

            bus.Enqueue(new TestEvent(keyA));
            var dispatched = bus.DispatchPending();

            Assert.Equal(new[] { "a", "b" }, log);
            Assert.Equal(2, dispatched);
        }

        [Fact]
        public void DispatchPending_StopsAndLogsErrorWhenExceedingMaxDispatchPasses()
        {
            var catalog = MakeCatalog("test.a");
            var diagnostics = new InMemoryEventDiagnostics();
            var options = new EventBusOptions { MaxDispatchPasses = 3 };
            var bus = new EventBus(catalog, options, diagnostics);
            var key = new Id("test.a");

            // 每次处理都重新入队一个新事件，模拟事件互相触发导致的无限递归。
            bus.Subscribe(key, evt => bus.Enqueue(new TestEvent(key)));
            bus.Enqueue(new TestEvent(key));

            var dispatched = bus.DispatchPending();

            Assert.Equal(3, dispatched);
            Assert.NotEmpty(diagnostics.Errors);
        }

        // 5. 取消订阅后不再收到；派发中取消订阅不崩溃
        [Fact]
        public void Unsubscribe_StopsReceivingFurtherEvents()
        {
            var catalog = MakeCatalog("test.a");
            var bus = new EventBus(catalog);
            var key = new Id("test.a");
            var count = 0;

            var handle = bus.Subscribe(key, evt => count++);
            bus.PublishImmediate(new TestEvent(key));
            handle.Dispose();
            bus.PublishImmediate(new TestEvent(key));

            Assert.Equal(1, count);
        }

        [Fact]
        public void Unsubscribe_DuringDispatch_DoesNotThrowAndSkipsCancelledSubscriber()
        {
            var catalog = MakeCatalog("test.a");
            var bus = new EventBus(catalog);
            var key = new Id("test.a");
            var calls = new List<string>();
            SubscriptionHandle? handleB = null;

            bus.Subscribe(key, evt =>
            {
                calls.Add("A");
                handleB!.Dispose();
            });
            handleB = bus.Subscribe(key, evt => calls.Add("B"));
            bus.Subscribe(key, evt => calls.Add("C"));

            var ex = Record.Exception(() => bus.PublishImmediate(new TestEvent(key)));

            Assert.Null(ex);
            Assert.Equal(new[] { "A", "C" }, calls);
        }

        // 6. 某订阅者抛异常，其他订阅者仍收到，异常记入诊断
        [Fact]
        public void SubscriberException_DoesNotStopOtherSubscribers_AndIsRecordedInDiagnostics()
        {
            var catalog = MakeCatalog("test.a");
            var diagnostics = new InMemoryEventDiagnostics();
            var bus = new EventBus(catalog, diagnostics: diagnostics);
            var key = new Id("test.a");
            var calls = new List<string>();

            bus.Subscribe(key, evt => calls.Add("first"));
            bus.Subscribe(key, evt => throw new InvalidOperationException("boom"));
            bus.Subscribe(key, evt => calls.Add("third"));

            bus.PublishImmediate(new TestEvent(key));

            Assert.Equal(new[] { "first", "third" }, calls);
            Assert.Single(diagnostics.Errors);
            Assert.NotNull(diagnostics.Errors[0].Exception);
            Assert.Equal("boom", diagnostics.Errors[0].Exception!.Message);
        }

        // 7. 严格模式下未登记 key 抛异常；非严格模式记警告并照常派发
        [Fact]
        public void StrictCatalog_ThrowsForUnregisteredKey()
        {
            var catalog = MakeCatalog("test.a");
            var bus = new EventBus(catalog);

            Assert.Throws<InvalidOperationException>(
                () => bus.Enqueue(new TestEvent(new Id("test.unknown"))));
        }

        [Fact]
        public void NonStrictCatalog_WarnsAndDispatchesForUnregisteredKey()
        {
            var catalog = MakeCatalog("test.a");
            var diagnostics = new InMemoryEventDiagnostics();
            var options = new EventBusOptions { StrictCatalog = false };
            var bus = new EventBus(catalog, options, diagnostics);
            var key = new Id("test.unknown");
            var received = false;
            bus.Subscribe(key, evt => received = true);

            bus.PublishImmediate(new TestEvent(key));

            Assert.True(received);
            Assert.NotEmpty(diagnostics.Warnings);
        }

        // 8. 泛型订阅类型不匹配时跳过并记警告
        [Fact]
        public void GenericSubscribe_SkipsAndWarnsOnTypeMismatch()
        {
            var catalog = MakeCatalog("test.a");
            var diagnostics = new InMemoryEventDiagnostics();
            var bus = new EventBus(catalog, diagnostics: diagnostics);
            var key = new Id("test.a");
            var received = false;

            bus.Subscribe<TestEvent>(key, evt => received = true);
            bus.PublishImmediate(new OtherEvent(key));

            Assert.False(received);
            Assert.NotEmpty(diagnostics.Warnings);
        }

        [Fact]
        public void GenericSubscribe_InvokesHandlerWhenTypeMatches()
        {
            var catalog = MakeCatalog("test.a");
            var bus = new EventBus(catalog);
            var key = new Id("test.a");
            var received = -1;

            bus.Subscribe<TestEvent>(key, evt => received = evt.Value);
            bus.PublishImmediate(new TestEvent(key, 42));

            Assert.Equal(42, received);
        }

        // 9. 审计日志开启时记录条数正确
        [Fact]
        public void AuditLog_RecordsEachDispatchedEventWhenEnabled()
        {
            var catalog = MakeCatalog("test.a", "test.b");
            var audit = new InMemoryEventAudit();
            var options = new EventBusOptions { AuditLog = true };
            var bus = new EventBus(catalog, options, audit: audit);
            var keyA = new Id("test.a");
            var keyB = new Id("test.b");

            bus.Enqueue(new TestEvent(keyA));
            bus.Enqueue(new TestEvent(keyB));
            bus.DispatchPending();
            bus.PublishImmediate(new TestEvent(keyA));

            Assert.Equal(3, audit.Records.Count);
            Assert.Equal(keyA, audit.Records[0].Key);
            Assert.Equal(keyB, audit.Records[1].Key);
            Assert.Equal(keyA, audit.Records[2].Key);
            Assert.Equal(0, audit.Records[0].Sequence);
            Assert.Equal(1, audit.Records[1].Sequence);
            Assert.Equal(2, audit.Records[2].Sequence);
        }

        [Fact]
        public void AuditLog_DoesNotRecordWhenDisabled()
        {
            var catalog = MakeCatalog("test.a");
            var audit = new InMemoryEventAudit();
            var bus = new EventBus(catalog, audit: audit);

            bus.PublishImmediate(new TestEvent(new Id("test.a")));

            Assert.Empty(audit.Records);
        }
    }
}
