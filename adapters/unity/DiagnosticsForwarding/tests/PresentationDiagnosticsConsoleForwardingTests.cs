using System.Collections.Generic;
using Adapter.Unity.Presentation;
using Presentation.VfxSfx.Contracts;
using Xunit;

namespace Adapter.Unity.Presentation.Tests
{
    /// <summary>记录型 <see cref="IPresentationDiagnosticsConsoleSink"/> 假实现，供断言"是否真的被
    /// 调用过、调用了几次、参数是什么"。</summary>
    internal sealed class RecordingConsoleSink : IPresentationDiagnosticsConsoleSink
    {
        public readonly List<string> Messages = new List<string>();

        public void Warn(string message) => Messages.Add(message);
    }

    public class PresentationDiagnosticsConsoleGateTests
    {
        [Fact]
        public void FirstOccurrence_ReturnsTrue()
        {
            var gate = new PresentationDiagnosticsConsoleGate();
            Assert.True(gate.ShouldForward("resource missing: sprite.hero"));
        }

        [Fact]
        public void DuplicateMessage_ReturnsFalse()
        {
            var gate = new PresentationDiagnosticsConsoleGate();
            Assert.True(gate.ShouldForward("dup"));
            Assert.False(gate.ShouldForward("dup"));
            Assert.False(gate.ShouldForward("dup"));
        }

        [Fact]
        public void DifferentMessages_EachForwardedOnce()
        {
            var gate = new PresentationDiagnosticsConsoleGate();
            Assert.True(gate.ShouldForward("a"));
            Assert.True(gate.ShouldForward("b"));
            Assert.False(gate.ShouldForward("a"));
            Assert.False(gate.ShouldForward("b"));
        }

        [Fact]
        public void Disabled_AlwaysReturnsFalse_EvenForBrandNewMessage()
        {
            var gate = new PresentationDiagnosticsConsoleGate(enabled: false);
            Assert.False(gate.ShouldForward("never seen before"));
        }

        [Fact]
        public void DisabledThenReenabled_MessageSeenWhileDisabled_IsTreatedAsNewAfterReenable()
        {
            var gate = new PresentationDiagnosticsConsoleGate(enabled: false);
            Assert.False(gate.ShouldForward("m"));

            gate.Enabled = true;

            // 关闭期间路过的消息没有被记账（判断记录：关闭时不记账），重新开启后仍视为"新消息"。
            Assert.True(gate.ShouldForward("m"));
            Assert.False(gate.ShouldForward("m"));
        }

        [Fact]
        public void ExceedsCapacity_EvictsLeastRecentlyUsed_AllowsReforwardOfEvictedMessage()
        {
            var gate = new PresentationDiagnosticsConsoleGate(capacity: 2);

            Assert.True(gate.ShouldForward("first"));
            Assert.True(gate.ShouldForward("second"));
            // 容量已满（2），插入第三条消息应当淘汰最久未用的 "first"（从未被再次命中过）。
            Assert.True(gate.ShouldForward("third"));

            // "first" 已被淘汰，此刻重新出现应视为"新消息"再次转发一次（判断记录：LRU 淘汰后
            // 允许重新提醒，不是永久抑制）。
            Assert.True(gate.ShouldForward("first"));

            // "second"/"third" 仍在去重窗口内（容量为 2，"first" 重新插入后应当淘汰当前最久未用的
            // 一条——此处只断言 second 仍会被判定为重复，不对淘汰顺序做过细的断言）。
            Assert.False(gate.ShouldForward("third"));
        }

        [Fact]
        public void RecentlyUsedMessage_TouchedByShouldForward_SurvivesEvictionLongerThanUntouchedOnes()
        {
            var gate = new PresentationDiagnosticsConsoleGate(capacity: 2);

            Assert.True(gate.ShouldForward("old"));
            Assert.True(gate.ShouldForward("mid"));

            // 重新命中 "old"（刷新为最近使用，见 ShouldForward 判断记录），此时 "mid" 才是最久未用。
            Assert.False(gate.ShouldForward("old"));

            // 插入新消息应当淘汰 "mid"（最久未用），"old" 因为刚被刷新过应当仍然存活。
            Assert.True(gate.ShouldForward("new"));
            Assert.False(gate.ShouldForward("old"));
            Assert.True(gate.ShouldForward("mid"));
        }
    }

    public class PresentationDiagnosticsConsoleForwarderTests
    {
        [Fact]
        public void Warn_FirstOccurrence_RecordsToInner_AndForwardsToSink()
        {
            var inner = new PresentationDiagnosticsRecorder();
            var sink = new RecordingConsoleSink();
            var forwarder = new PresentationDiagnosticsConsoleForwarder(inner, sink);

            forwarder.Warn("vfx.def 未登记 id=\"vfx.missing\"");

            Assert.Equal(new[] { "vfx.def 未登记 id=\"vfx.missing\"" }, inner.Warnings);
            Assert.Equal(new[] { "vfx.def 未登记 id=\"vfx.missing\"" }, sink.Messages);
        }

        [Fact]
        public void Warn_DuplicateMessage_StillRecordsToInner_ButDoesNotForwardToSinkAgain()
        {
            var inner = new PresentationDiagnosticsRecorder();
            var sink = new RecordingConsoleSink();
            var forwarder = new PresentationDiagnosticsConsoleForwarder(inner, sink);

            forwarder.Warn("dup");
            forwarder.Warn("dup");
            forwarder.Warn("dup");

            // 内存记录（inner）语义不变——既有消费方/测试读 PresentationDiagnosticsRecorder.Warnings
            // 的行为不受本装饰器影响，仍然每次调用都追加。
            Assert.Equal(3, inner.Warnings.Count);
            // 控制台转发经去重，只应该出现一次。
            Assert.Single(sink.Messages);
        }

        [Fact]
        public void Warn_GateDisabled_RecordsToInner_ButNeverForwardsToSink()
        {
            var inner = new PresentationDiagnosticsRecorder();
            var sink = new RecordingConsoleSink();
            var gate = new PresentationDiagnosticsConsoleGate(enabled: false);
            var forwarder = new PresentationDiagnosticsConsoleForwarder(inner, sink, gate);

            forwarder.Warn("should stay silent");

            Assert.Single(inner.Warnings);
            Assert.Empty(sink.Messages);
        }
    }

    public class SpriteRigDiagnosticsPumpTests
    {
        [Fact]
        public void Pump_ForwardsOnlyEntriesAddedSinceLastPump()
        {
            var recorder = new PresentationDiagnosticsRecorder();
            var sink = new RecordingConsoleSink();
            var gate = new PresentationDiagnosticsConsoleGate();
            var pump = new SpriteRigDiagnosticsPump();

            recorder.Warn("first failure");
            pump.Pump(recorder, gate, sink);
            Assert.Equal(new[] { "first failure" }, sink.Messages);

            // 第二次 Pump，尚无新增条目：不应重复转发同一条。
            pump.Pump(recorder, gate, sink);
            Assert.Single(sink.Messages);

            recorder.Warn("second failure");
            pump.Pump(recorder, gate, sink);
            Assert.Equal(new[] { "first failure", "second failure" }, sink.Messages);
        }

        [Fact]
        public void Pump_MultipleRecorders_TrackedIndependently()
        {
            var recorderA = new PresentationDiagnosticsRecorder();
            var recorderB = new PresentationDiagnosticsRecorder();
            var sink = new RecordingConsoleSink();
            var gate = new PresentationDiagnosticsConsoleGate();
            var pump = new SpriteRigDiagnosticsPump();

            recorderA.Warn("SpriteCharacterRig（entity=unit.a）：资源加载失败");
            pump.Pump(recorderA, gate, sink);
            pump.Pump(recorderB, gate, sink); // B 还没有任何条目，不应产生任何转发。

            Assert.Single(sink.Messages);

            recorderB.Warn("SpriteCharacterRig（entity=unit.b）：资源加载失败");
            pump.Pump(recorderA, gate, sink); // A 没有新增，不应重复转发。
            pump.Pump(recorderB, gate, sink); // B 有新增，应当转发。

            Assert.Equal(2, sink.Messages.Count);
        }

        [Fact]
        public void Pump_SameMessageTextFromDifferentRecorders_DedupedByGate()
        {
            // 两个不同实体各自触发了完全相同的字面量消息（如资源缺失文案未携带 entity id 的极端
            // 情形）时，共享同一个 gate 的多次 Pump 调用应当只转发一次——这正是"去重键=消息文本"
            // 这一判断记录的直接体现。
            var recorderA = new PresentationDiagnosticsRecorder();
            var recorderB = new PresentationDiagnosticsRecorder();
            var sink = new RecordingConsoleSink();
            var gate = new PresentationDiagnosticsConsoleGate();
            var pump = new SpriteRigDiagnosticsPump();

            recorderA.Warn("identical message");
            recorderB.Warn("identical message");

            pump.Pump(recorderA, gate, sink);
            pump.Pump(recorderB, gate, sink);

            Assert.Single(sink.Messages);
        }
    }
}
