#nullable enable
using System;
using System.Collections.Generic;
using Adapter.Unity.Diagnostics;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using NUnit.Framework;

namespace Adapter.Unity.Tests.Editor
{
    /// <summary>
    /// ADR-0086 回归（消费方第三十二批阻塞项第 3 条，适配层用例）：验证 EventBus 订阅者异常经
    /// <see cref="ExceptionAwareErrorProjection"/> + <see cref="DiagnosticsHub"/> 的新 Register
    /// 重载（与 <c>DiagnosticsHubComposition.RegisterCoreSources</c> 生产装配代码同一登记写法，见
    /// 该文件"Core.Foundation.EventBus"一行）转发到引擎控制台时——两个类型不同、消息文字相同的
    /// 异常得到两条可区分的输出（都带异常类型全名与堆栈），同一异常重复出现时累计次数在后续输出里
    /// 可见，不会被去重逻辑静默吞掉。本文件只在 Unity asmdef 编译范围内（dotnet test 侧的
    /// Adapters.Unity.DiagnosticsForwarding.csproj 刻意不纳入 DiagnosticsHubComposition.cs 一类引用
    /// UnityEngine 无关但引用 Core.Gameplay/Presentation.Assembly 的生产装配代码，见该 csproj 判断
    /// 记录），必须在真实 Unity 批处理里跑才能验证到"适配层"这一层的转发行为。
    /// </summary>
    public sealed class ADR0086_ExceptionAwareDiagnosticsForwardingEditorTests
    {
        private sealed class RecordingConsoleSink : IPresentationDiagnosticsConsoleSink
        {
            public readonly List<string> Messages = new List<string>();

            public void Warn(string message) => Messages.Add(message);
        }

        private sealed class TestEvent : IEvent
        {
            public TestEvent(Id key) => Key = key;

            public Id Key { get; }
        }

        /// <summary>把 <paramref name="keys"/> 登记进目录，避免 <see cref="EventBus.PublishImmediate"/>
        /// 因为"事件 key 未登记"额外产生一条 <c>IEventDiagnostics.Warn</c>（该诊断走 Warnings 列表，
        /// 会和本用例真正关心的 Errors 列表混在同一个 sink 里，污染断言的消息计数）——同
        /// <c>core/foundation/event_bus/tests/EventBusTests.cs</c> 的 <c>MakeCatalog</c> 同一惯例。</summary>
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

        private static EventBus BuildBus(EventCatalog catalog, out InMemoryEventDiagnostics diagnostics)
        {
            diagnostics = new InMemoryEventDiagnostics();
            return new EventBus(catalog, new EventBusOptions { StrictCatalog = false }, diagnostics);
        }

        private static DiagnosticsHub BuildHub(EventBus bus, InMemoryEventDiagnostics diagnostics, out RecordingConsoleSink sink)
        {
            sink = new RecordingConsoleSink();
            var hub = new DiagnosticsHub(sink);
            // 同 DiagnosticsHubComposition.RegisterCoreSources 里 "Core.Foundation.EventBus" 一行完全
            // 一致的登记写法——本用例验证的就是这一行实际产出的转发行为。
            hub.Register("Core.Foundation.EventBus", diagnostics.Warnings,
                ExceptionAwareErrorProjection.From(diagnostics.Errors, e => e.Message, e => e.Exception));
            return hub;
        }

        [Test]
        public void TwoDifferentExceptionTypes_SameMessageText_ForwardedAsDistinguishableEntries_WithTypeAndStack()
        {
            var catalog = MakeCatalog("test.adr0086_a", "test.adr0086_b");
            var bus = BuildBus(catalog, out var diagnostics);
            var hub = BuildHub(bus, diagnostics, out var sink);

            var keyA = new Id("test.adr0086_a");
            var keyB = new Id("test.adr0086_b");
            const string sharedMessageText = "boom";
            bus.Subscribe(keyA, _ => throw new InvalidOperationException(sharedMessageText));
            bus.Subscribe(keyB, _ => throw new ArgumentException(sharedMessageText));

            bus.PublishImmediate(new TestEvent(keyA));
            bus.PublishImmediate(new TestEvent(keyB));
            hub.Pump();

            Assert.AreEqual(2, sink.Messages.Count, "两个不同类型的异常应各转发一条，互不覆盖或合并");
            var first = sink.Messages[0];
            var second = sink.Messages[1];

            StringAssert.Contains(nameof(InvalidOperationException), first);
            StringAssert.Contains("at ", first); // 堆栈片段。
            StringAssert.Contains(nameof(ArgumentException), second);
            StringAssert.Contains("at ", second);
            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void SameExceptionRepeatedMultipleTimes_NotSilentlyDropped_OccurrenceCountVisibleInOutput()
        {
            var catalog = MakeCatalog("test.adr0086_repeat");
            var bus = BuildBus(catalog, out var diagnostics);
            var hub = BuildHub(bus, diagnostics, out var sink);

            var key = new Id("test.adr0086_repeat");
            bus.Subscribe(key, _ => throw new InvalidOperationException("repeat-boom"));

            bus.PublishImmediate(new TestEvent(key));
            bus.PublishImmediate(new TestEvent(key));
            bus.PublishImmediate(new TestEvent(key));
            hub.Pump();

            Assert.AreEqual(3, sink.Messages.Count, "不能静默丢弃：每次出现都应产生一条输出");
            StringAssert.Contains(nameof(InvalidOperationException), sink.Messages[0]);
            StringAssert.DoesNotContain("[重复]", sink.Messages[0]); // 首次出现是完整版本。

            StringAssert.Contains("[重复]", sink.Messages[1]);
            StringAssert.Contains("2", sink.Messages[1]); // 累计出现次数可见。
            StringAssert.Contains("[重复]", sink.Messages[2]);
            StringAssert.Contains("3", sink.Messages[2]);
        }
    }
}
