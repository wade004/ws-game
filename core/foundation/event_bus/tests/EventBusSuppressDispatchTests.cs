using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.Events
{
    /// <summary>
    /// 测试覆盖梳理 T-H3：<c>EventBus.SuppressDispatch</c>（CORE-170-03 根治）与
    /// <c>DispatchPending</c> 重入语义的行为钉住。以下用例按"现行为"断言——契约来源是
    /// <c>IEventBus.SuppressDispatch</c> 的判断记录与 <c>EventBus.cs</c> 实现：抑制计数是纯引用计数，
    /// 作用域对象不记录自己的层级，因此 Dispose 顺序颠倒不影响"全部释放后才恢复"这一结果。
    /// </summary>
    public class EventBusSuppressDispatchTests
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

        private static readonly Id KeyA = new Id("test.a");

        // 探针：向总线同时提交一个 Enqueue 与一个 PublishImmediate 事件，返回两者各自最终被订阅者收到的次数。
        // 作用域内全部应被丢弃；恢复后两者都应送达。
        private static (int viaEnqueue, int viaImmediate) Probe(EventBus bus, List<int> received)
        {
            received.Clear();
            bus.Enqueue(new TestEvent(KeyA, 1));
            bus.DispatchPending();
            var viaEnqueue = received.Count;

            received.Clear();
            bus.PublishImmediate(new TestEvent(KeyA, 2));
            var viaImmediate = received.Count;
            return (viaEnqueue, viaImmediate);
        }

        private static EventBus MakeBus(List<int> received, EventBusOptions? options = null,
            IEventDiagnostics? diagnostics = null, IEventAudit? audit = null)
        {
            var bus = new EventBus(MakeCatalog("test.a"), options, diagnostics, audit);
            bus.Subscribe<TestEvent>(KeyA, evt => received.Add(evt.Value));
            return bus;
        }

        // 1. 嵌套计数：内层 Dispose 后仍抑制，外层 Dispose 后恢复
        [Fact]
        public void SuppressDispatch_Nested_InnerDisposeStillSuppresses_OuterDisposeRestores()
        {
            var received = new List<int>();
            var bus = MakeBus(received);

            var outer = bus.SuppressDispatch();
            var inner = bus.SuppressDispatch();

            inner.Dispose();
            Assert.Equal((0, 0), Probe(bus, received));

            outer.Dispose();
            Assert.Equal((1, 1), Probe(bus, received));
        }

        // 2. Dispose 顺序颠倒：先释放外层再释放内层。现行为：计数器是纯引用计数，作用域不记层级，
        //    所以"先外后内"与"先内后外"等价——必须两个都释放后才恢复。
        [Fact]
        public void SuppressDispatch_ReversedDisposeOrder_RestoresOnlyAfterBothDisposed()
        {
            var received = new List<int>();
            var bus = MakeBus(received);

            var outer = bus.SuppressDispatch();
            var inner = bus.SuppressDispatch();

            outer.Dispose(); // 颠倒：外层先释放
            Assert.Equal((0, 0), Probe(bus, received));

            inner.Dispose();
            Assert.Equal((1, 1), Probe(bus, received));
        }

        // 3. 同一作用域重复 Dispose 幂等：第二次 Dispose 不得多减一层、把外层作用域的抑制提前解除
        [Fact]
        public void SuppressDispatch_DoubleDispose_IsIdempotentAndDoesNotStealOuterLevel()
        {
            var received = new List<int>();
            var bus = MakeBus(received);

            var outer = bus.SuppressDispatch();
            var inner = bus.SuppressDispatch();

            inner.Dispose();
            inner.Dispose(); // 重复释放

            Assert.Equal((0, 0), Probe(bus, received)); // 外层仍在，仍抑制

            outer.Dispose();
            Assert.Equal((1, 1), Probe(bus, received));
        }

        // 4. 单个作用域重复 Dispose 后不会把计数压成负数：之后再开新作用域仍能正常抑制
        [Fact]
        public void SuppressDispatch_ExtraDisposeDoesNotUnderflow_NewScopeStillSuppresses()
        {
            var received = new List<int>();
            var bus = MakeBus(received);

            var first = bus.SuppressDispatch();
            first.Dispose();
            first.Dispose();
            first.Dispose();

            using (bus.SuppressDispatch())
            {
                Assert.Equal((0, 0), Probe(bus, received));
            }

            Assert.Equal((1, 1), Probe(bus, received));
        }

        // 5. 作用域内 Enqueue/PublishImmediate 被丢弃：订阅者不被调用、队列里不残留（DispatchPending 返回 0），
        //    且不触发目录校验（严格模式下未登记 key 也不抛）；作用域结束后同样的调用恢复为抛异常
        [Fact]
        public void SuppressDispatch_DropsEventsWithoutCatalogValidation_ThenStrictCatalogResumes()
        {
            var received = new List<int>();
            var bus = MakeBus(received); // 默认 StrictCatalog=true
            var unknown = new Id("test.unknown");
            var unknownHits = 0;
            bus.Subscribe(unknown, evt => unknownHits++);

            using (bus.SuppressDispatch())
            {
                bus.Enqueue(new TestEvent(unknown));
                bus.PublishImmediate(new TestEvent(unknown));
                bus.Enqueue(new TestEvent(KeyA, 9));
                bus.PublishImmediate(new TestEvent(KeyA, 9));
            }

            // 丢弃的事件没有留在队列里，恢复后也不会被补发
            Assert.Equal(0, bus.DispatchPending());
            Assert.Equal(0, unknownHits);
            Assert.Empty(received);

            // 恢复后目录校验重新生效
            Assert.Throws<InvalidOperationException>(() => bus.Enqueue(new TestEvent(unknown)));
            Assert.Throws<InvalidOperationException>(() => bus.PublishImmediate(new TestEvent(unknown)));
        }

        // 6. 非严格模式：作用域内未登记 key 也不产生"未登记"警告；作用域外才产生
        [Fact]
        public void SuppressDispatch_NonStrict_NoCatalogWarningInsideScope()
        {
            var received = new List<int>();
            var diagnostics = new InMemoryEventDiagnostics();
            var bus = MakeBus(received, new EventBusOptions { StrictCatalog = false }, diagnostics);
            var unknown = new Id("test.unknown");

            using (bus.SuppressDispatch())
            {
                bus.Enqueue(new TestEvent(unknown));
                bus.PublishImmediate(new TestEvent(unknown));
            }

            Assert.Empty(diagnostics.Warnings);

            bus.Enqueue(new TestEvent(unknown));
            Assert.NotEmpty(diagnostics.Warnings);
        }

        // 7. 作用域结束后恢复正常：入队顺序与派发计数与从未抑制过的总线一致
        [Fact]
        public void SuppressDispatch_AfterScope_BehavesLikeFreshBus()
        {
            var received = new List<int>();
            var bus = MakeBus(received);

            using (bus.SuppressDispatch())
            {
                bus.Enqueue(new TestEvent(KeyA, 100));
            }

            const int count = 5;
            for (var i = 0; i < count; i++)
            {
                bus.Enqueue(new TestEvent(KeyA, i));
            }

            Assert.Equal(count, bus.DispatchPending());
            var expected = new List<int>();
            for (var i = 0; i < count; i++)
            {
                expected.Add(i);
            }

            Assert.Equal(expected, received);
        }

        // 8. 被丢弃的事件不推进审计序号：作用域前后审计记录的序号连续
        [Fact]
        public void SuppressDispatch_DroppedEvents_DoNotConsumeAuditSequence()
        {
            var received = new List<int>();
            var audit = new InMemoryEventAudit();
            var bus = MakeBus(received, new EventBusOptions { AuditLog = true }, audit: audit);

            bus.PublishImmediate(new TestEvent(KeyA, 1));
            using (bus.SuppressDispatch())
            {
                bus.Enqueue(new TestEvent(KeyA, 2));
                bus.PublishImmediate(new TestEvent(KeyA, 3));
            }

            bus.PublishImmediate(new TestEvent(KeyA, 4));

            Assert.Equal(2, audit.Records.Count);
            Assert.Equal(0, audit.Records[0].Sequence);
            Assert.Equal(audit.Records[0].Sequence + 1, audit.Records[1].Sequence);
        }

        // 9. 现行为边界：抑制只管"新提交"，不管"已在队列里的事件"。
        //    进入作用域前已 Enqueue 的事件，在作用域内调用 DispatchPending 仍会被派发；
        //    这些订阅者在派发期间新提交的事件（Enqueue/PublishImmediate）则被丢弃。
        //    （这是现行为的钉住，不是设计上的承诺；SaveSystem.Load 场景下作用域内不会调 DispatchPending。）
        [Fact]
        public void SuppressDispatch_OnlyAffectsNewSubmissions_AlreadyQueuedEventsStillDispatch()
        {
            var received = new List<int>();
            var bus = MakeBus(received);
            var followUpKey = new Id("test.a");
            var followUpsSeen = 0;

            // 订阅者在收到 Value==1 时又提交一个后继事件
            bus.Subscribe<TestEvent>(followUpKey, evt =>
            {
                if (evt.Value == 1)
                {
                    followUpsSeen++;
                    bus.Enqueue(new TestEvent(KeyA, 50));
                    bus.PublishImmediate(new TestEvent(KeyA, 51));
                }
            });

            bus.Enqueue(new TestEvent(KeyA, 1)); // 作用域之前入队

            using (bus.SuppressDispatch())
            {
                var dispatched = bus.DispatchPending();

                Assert.Equal(1, dispatched);            // 已排队的事件照常派发
                Assert.Equal(new[] { 1 }, received);    // 后继事件 50/51 被丢弃
                Assert.Equal(1, followUpsSeen);
            }

            Assert.Equal(0, bus.DispatchPending());     // 队列里没有残留
        }

        // 10. DispatchPending 重入语义（现行为钉住）：
        //     外层 DispatchPending 取批后已 Clear 队列，批内剩余事件不在队列里，
        //     因此订阅者内再次调用 DispatchPending 只会派发"派发期间新入队的事件"，
        //     即内层插队——观察到的顺序是 A, C, B 而不是 A, B, C。
        //     内层返回值只含 C（1），外层返回值只含自己的批（A、B 共 2），C 不重复计入外层。
        [Fact]
        public void DispatchPending_ReentrantCall_InnerDispatchesNewEventsAheadOfRemainingBatch()
        {
            var keyB = new Id("test.b");
            var keyC = new Id("test.c");
            var bus = new EventBus(MakeCatalog("test.a", "test.b", "test.c"));
            var order = new List<string>();
            var innerReturn = -1;

            bus.Subscribe(KeyA, evt =>
            {
                order.Add("A");
                bus.Enqueue(new TestEvent(keyC));
                innerReturn = bus.DispatchPending(); // 重入
            });
            bus.Subscribe(keyB, evt => order.Add("B"));
            bus.Subscribe(keyC, evt => order.Add("C"));

            bus.Enqueue(new TestEvent(KeyA));
            bus.Enqueue(new TestEvent(keyB));

            var outerReturn = bus.DispatchPending();

            Assert.Equal(new[] { "A", "C", "B" }, order); // 现行为：C 插队到同批的 B 之前
            Assert.Equal(1, innerReturn);
            Assert.Equal(2, outerReturn);
            Assert.Equal(0, bus.DispatchPending());
        }

        // 11. 重入时队列已被外层 Clear：内层调用不会把外层批内尚未派发的事件重复派发（每个事件恰好一次）
        [Fact]
        public void DispatchPending_ReentrantCall_NeverDoubleDispatchesRemainingBatchEvents()
        {
            var keyB = new Id("test.b");
            var bus = new EventBus(MakeCatalog("test.a", "test.b"));
            var bHits = 0;

            bus.Subscribe(KeyA, evt => bus.DispatchPending()); // 纯重入，队列此时为空
            bus.Subscribe(keyB, evt => bHits++);

            bus.Enqueue(new TestEvent(KeyA));
            bus.Enqueue(new TestEvent(keyB));

            Assert.Equal(2, bus.DispatchPending());
            Assert.Equal(1, bHits);
        }
    }
}
