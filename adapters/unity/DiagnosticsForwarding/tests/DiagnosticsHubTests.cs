using System.Collections.Generic;
using Adapter.Unity.Diagnostics;
using Adapter.Unity.Presentation;
using Xunit;

namespace Adapter.Unity.Diagnostics.Tests
{
    /// <summary>诊断契约统一转发机制（architecture/adr/0042-诊断契约统一转发到宿主控制台.md）的
    /// dotnet test 侧验证——覆盖 <see cref="DiagnosticsHub"/>/<see cref="ProjectedReadOnlyList{TSource}"/>。
    /// 复用同目录 <c>PresentationDiagnosticsConsoleForwardingTests.RecordingConsoleSink</c> 的姊妹实现
    /// （不同命名空间，独立定义，避免跨命名空间引用测试内部类型）。</summary>
    internal sealed class RecordingConsoleSink : IPresentationDiagnosticsConsoleSink
    {
        public readonly List<string> Messages = new List<string>();

        public void Warn(string message) => Messages.Add(message);
    }

    public class DiagnosticsHubTests
    {
        [Fact]
        public void Register_ForwardsNewWarnings_WithSourceNamePrefix()
        {
            var sink = new RecordingConsoleSink();
            var hub = new DiagnosticsHub(sink);
            var warnings = new List<string>();

            hub.Register("Core.Foundation.EventBus", warnings);
            warnings.Add("未登记事件 \"quest.updated\"");
            hub.Pump();

            Assert.Single(sink.Messages);
            Assert.Equal("[Core.Foundation.EventBus] 未登记事件 \"quest.updated\"", sink.Messages[0]);
        }

        [Fact]
        public void Register_ErrorList_ForwardsWithErrorMarker_StillAsConsoleWarning()
        {
            // 硬约束：Error 级恒映射到控制台 Warning（IPresentationDiagnosticsConsoleSink 只有
            // Warn 一个方法，物理上不可能调用出 LogError），文本额外带 "[error]" 标记保留可辨识性。
            var sink = new RecordingConsoleSink();
            var hub = new DiagnosticsHub(sink);
            var warnings = new List<string>();
            var errors = new List<string>();

            hub.Register("Core.Foundation.SaveSystem", warnings, errors);
            errors.Add("存档段 \"quest\" 的 Save() 抛出异常，存档已中止");
            hub.Pump();

            Assert.Single(sink.Messages);
            Assert.Equal("[Core.Foundation.SaveSystem][error] 存档段 \"quest\" 的 Save() 抛出异常，存档已中止", sink.Messages[0]);
        }

        [Fact]
        public void Pump_OnlyForwardsEntriesAddedSinceLastPump()
        {
            var sink = new RecordingConsoleSink();
            var hub = new DiagnosticsHub(sink);
            var warnings = new List<string>();
            hub.Register("A", warnings);

            warnings.Add("first");
            hub.Pump();
            Assert.Single(sink.Messages);

            hub.Pump();
            Assert.Single(sink.Messages);

            warnings.Add("second");
            hub.Pump();
            Assert.Equal(new[] { "[A] first", "[A] second" }, sink.Messages);
        }

        [Fact]
        public void Register_NullWarnings_SilentlySkipped_DoesNotThrow_OtherSourcesStillForwarded()
        {
            var sink = new RecordingConsoleSink();
            var hub = new DiagnosticsHub(sink);
            var warnings = new List<string>();

            hub.Register("Unavailable", null);
            hub.Register("Available", warnings);
            warnings.Add("m");

            hub.Pump();

            Assert.Single(sink.Messages);
            Assert.Equal("[Available] m", sink.Messages[0]);
        }

        [Fact]
        public void DifferentSources_IdenticalMessageText_NotConfusedByPrefix()
        {
            // 判断记录：两个不同模块凑巧写出完全相同的警告文本时，加来源前缀后两条转发文本本身
            // 不同，去重表按转发后的文本（含前缀）去重，因此两条都应当被转发一次——不会像"不加
            // 前缀"那样被误判为重复而丢掉其中一条。
            var sink = new RecordingConsoleSink();
            var hub = new DiagnosticsHub(sink);
            var warningsA = new List<string> { "未注入回调，已跳过" };
            var warningsB = new List<string> { "未注入回调，已跳过" };
            hub.Register("ModuleA", warningsA);
            hub.Register("ModuleB", warningsB);

            hub.Pump();

            Assert.Equal(2, sink.Messages.Count);
            Assert.Contains("[ModuleA] 未注入回调，已跳过", sink.Messages);
            Assert.Contains("[ModuleB] 未注入回调，已跳过", sink.Messages);
        }

        [Fact]
        public void SameSource_DuplicateMessage_DedupedByGate()
        {
            var sink = new RecordingConsoleSink();
            var hub = new DiagnosticsHub(sink);
            var warnings = new List<string>();
            hub.Register("A", warnings);

            warnings.Add("dup");
            warnings.Add("dup");
            hub.Pump();

            Assert.Single(sink.Messages);
        }

        [Fact]
        public void Disabled_NeverForwards_EvenForBrandNewMessages()
        {
            var sink = new RecordingConsoleSink();
            var hub = new DiagnosticsHub(sink, enabled: false);
            var warnings = new List<string>();
            hub.Register("A", warnings);

            warnings.Add("should stay silent");
            hub.Pump();

            Assert.Empty(sink.Messages);
        }

        [Fact]
        public void Enabled_CanBeToggledAtRuntime()
        {
            var sink = new RecordingConsoleSink();
            var hub = new DiagnosticsHub(sink, enabled: false);
            var warnings = new List<string>();
            hub.Register("A", warnings);

            warnings.Add("m");
            hub.Pump();
            Assert.Empty(sink.Messages);

            hub.Enabled = true;
            warnings.Add("n");
            hub.Pump();
            Assert.Equal(new[] { "[A] n" }, sink.Messages);
        }

        [Fact]
        public void MultipleSourcesRegisteredBeforeAnyDataArrives_AllPolledIndependently()
        {
            var sink = new RecordingConsoleSink();
            var hub = new DiagnosticsHub(sink);
            var a = new List<string>();
            var b = new List<string>();
            var c = new List<string>();
            hub.Register("A", a);
            hub.Register("B", b);
            hub.Register("C", c);

            b.Add("only b has something");
            hub.Pump();

            Assert.Single(sink.Messages);
            Assert.Equal("[B] only b has something", sink.Messages[0]);
        }
    }

    public class ProjectedReadOnlyListTests
    {
        private readonly struct ErrorRecord
        {
            public ErrorRecord(string message) => Message = message;
            public string Message { get; }
        }

        [Fact]
        public void Indexer_AppliesSelector_LazilyPerAccess()
        {
            var source = new List<ErrorRecord> { new ErrorRecord("boom") };
            var projected = new ProjectedReadOnlyList<ErrorRecord>(source, r => r.Message);

            Assert.Single(projected);
            Assert.Equal("boom", projected[0]);
        }

        [Fact]
        public void GrowsWithUnderlyingSource_ReflectsNewItemsWithoutReconstruction()
        {
            var source = new List<ErrorRecord>();
            var projected = new ProjectedReadOnlyList<ErrorRecord>(source, r => r.Message);

            Assert.Empty(projected);

            source.Add(new ErrorRecord("first"));
            Assert.Single(projected);
            Assert.Equal("first", projected[0]);
        }

        [Fact]
        public void UsableDirectlyAsDiagnosticsHubErrorSource()
        {
            // 端到端：ProjectedReadOnlyList 投影出的 IReadOnlyList<string> 可以直接喂给
            // DiagnosticsHub.Register 的 errors 参数（这是它存在的唯一理由——见 DiagnosticsHub.cs
            // 判断记录"消息列表类型不统一"）。
            var sink = new RecordingConsoleSink();
            var hub = new DiagnosticsHub(sink);
            var records = new List<ErrorRecord>();
            var projected = new ProjectedReadOnlyList<ErrorRecord>(records, r => r.Message);
            hub.Register("SomeHost", new List<string>(), projected);

            records.Add(new ErrorRecord("宿主 Query 抛出异常"));
            hub.Pump();

            Assert.Single(sink.Messages);
            Assert.Equal("[SomeHost][error] 宿主 Query 抛出异常", sink.Messages[0]);
        }
    }
}
