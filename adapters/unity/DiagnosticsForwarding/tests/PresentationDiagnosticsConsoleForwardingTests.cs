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

    /// <summary>诊断转发到引擎控制台跟进（presentation/assembly/README.md 判断记录 10）：覆盖
    /// <see cref="PresentationAssemblyDiagnosticsForwarder"/>——VfxPlayer/SfxPlayer/FeedbackBinder/
    /// ViewBinder 四条新增诊断源的轮询转发。</summary>
    public class PresentationAssemblyDiagnosticsForwarderTests
    {
        /// <summary><see cref="PresentationDiagnosticsRecorder"/> 之外的 <see cref="IPresentationDiagnostics"/>
        /// 实现，供"来源不是 recorder 时静默跳过"用例使用。</summary>
        private sealed class CustomDiagnostics : IPresentationDiagnostics
        {
            public void Warn(string message) { }
        }

        [Fact]
        public void Pump_ForwardsNewWarningsFromAllFourSources()
        {
            var vfx = new PresentationDiagnosticsRecorder();
            var sfx = new PresentationDiagnosticsRecorder();
            var feedback = new PresentationDiagnosticsRecorder();
            var viewBinder = new PresentationDiagnosticsRecorder();
            var sink = new RecordingConsoleSink();
            var forwarder = new PresentationAssemblyDiagnosticsForwarder(sink, vfx, sfx, feedback, viewBinder);

            vfx.Warn("vfx.def 未登记 id=\"vfx.missing\"");
            sfx.Warn("sfx.def 未登记 id=\"sfx.missing\"");
            feedback.Warn("feedback 规则使用了 from_display，但未注入 DisplayInfoResolver，跳过该动作");
            viewBinder.Warn("锚点查询：实体 \"unit.a\" 未绑定 View");

            forwarder.Pump();

            Assert.Equal(4, sink.Messages.Count);
            Assert.Contains("vfx.def 未登记 id=\"vfx.missing\"", sink.Messages);
            Assert.Contains("sfx.def 未登记 id=\"sfx.missing\"", sink.Messages);
            Assert.Contains("feedback 规则使用了 from_display，但未注入 DisplayInfoResolver，跳过该动作", sink.Messages);
            Assert.Contains("锚点查询：实体 \"unit.a\" 未绑定 View", sink.Messages);
        }

        [Fact]
        public void Pump_OnlyForwardsEntriesAddedSinceLastPump_PerSource()
        {
            var vfx = new PresentationDiagnosticsRecorder();
            var sink = new RecordingConsoleSink();
            var forwarder = new PresentationAssemblyDiagnosticsForwarder(sink, vfx, null, null, null);

            vfx.Warn("first");
            forwarder.Pump();
            Assert.Single(sink.Messages);

            // 无新增：再次 Pump 不应重复转发。
            forwarder.Pump();
            Assert.Single(sink.Messages);

            vfx.Warn("second");
            forwarder.Pump();
            Assert.Equal(new[] { "first", "second" }, sink.Messages);
        }

        [Fact]
        public void Pump_NullSources_SilentlySkipped_DoesNotThrow()
        {
            var sink = new RecordingConsoleSink();
            var forwarder = new PresentationAssemblyDiagnosticsForwarder(sink, null, null, null, null);

            forwarder.Pump();

            Assert.Empty(sink.Messages);
        }

        [Fact]
        public void Pump_NonRecorderSource_SilentlySkipped_OtherSourcesStillForwarded()
        {
            var vfx = new PresentationDiagnosticsRecorder();
            var custom = new CustomDiagnostics();
            var sink = new RecordingConsoleSink();
            var forwarder = new PresentationAssemblyDiagnosticsForwarder(sink, vfx, custom, null, null);

            vfx.Warn("vfx warning");
            custom.Warn("this can never be observed by the forwarder");

            forwarder.Pump();

            Assert.Single(sink.Messages);
            Assert.Equal("vfx warning", sink.Messages[0]);
        }

        [Fact]
        public void Pump_SameMessageTextFromDifferentSources_DedupedBySharedGate()
        {
            var vfx = new PresentationDiagnosticsRecorder();
            var sfx = new PresentationDiagnosticsRecorder();
            var sink = new RecordingConsoleSink();
            var forwarder = new PresentationAssemblyDiagnosticsForwarder(sink, vfx, sfx, null, null);

            vfx.Warn("identical message");
            sfx.Warn("identical message");

            forwarder.Pump();

            Assert.Single(sink.Messages);
        }

        [Fact]
        public void Disabled_NeverForwards_EvenForBrandNewMessages()
        {
            var vfx = new PresentationDiagnosticsRecorder();
            var sink = new RecordingConsoleSink();
            var forwarder = new PresentationAssemblyDiagnosticsForwarder(sink, vfx, null, null, null, enabled: false);

            vfx.Warn("should stay silent");
            forwarder.Pump();

            Assert.Empty(sink.Messages);
        }

        [Fact]
        public void Enabled_CanBeToggledAtRuntime()
        {
            var vfx = new PresentationDiagnosticsRecorder();
            var sink = new RecordingConsoleSink();
            var forwarder = new PresentationAssemblyDiagnosticsForwarder(sink, vfx, null, null, null, enabled: false);

            vfx.Warn("m");
            forwarder.Pump();
            Assert.Empty(sink.Messages);

            // 判断记录（关闭期间 Pump 仍然推进 SpriteRigDiagnosticsPump 的"已转发到第几条"游标，
            // 不是"跳过整次 Pump 调用"）：SpriteRigDiagnosticsPump.Pump 对 gate.ShouldForward 返回
            // false 的条目照样把游标推进到 warnings.Count（游标记的是"看过"而不是"转发过"，见该
            // 方法实现），所以关闭期间已经被 Pump 看到过一次的 "m" 不会在重新开启后被当成新消息
            // 再次转发——这与 PresentationDiagnosticsConsoleGate.Enabled 自身"关闭期间的消息不计入
            // 去重表、重新开启后仍视为新消息"是两层不同的记账（gate 记的是消息文本去重，pump 记的是
            // 每个 recorder 已经看到第几条），本测试只覆盖后者、不代表 gate 那层判断记录有变化。
            forwarder.Enabled = true;
            forwarder.Pump();
            Assert.Empty(sink.Messages);

            // 重新开启后新产生的消息应当照常转发。
            vfx.Warn("n");
            forwarder.Pump();
            Assert.Equal(new[] { "n" }, sink.Messages);
        }

        // ------------------------------------------------------------------------------------
        // 诊断转发到引擎控制台第三批（presentation/assembly/README.md 判断记录 10b）：新增第五个
        // 来源 CompositeFeedbackSink.Diagnostics，接的是新增五源构造函数重载（旧四源签名保持不变，
        // ABI 只新增重载）。以下用例专门覆盖五源重载，四源重载的既有用例（上面）保持不变、不需要
        // 因为新增来源而修改。
        // ------------------------------------------------------------------------------------

        [Fact]
        public void FiveSourceOverload_ForwardsNewWarningsFromAllFiveSources()
        {
            var vfx = new PresentationDiagnosticsRecorder();
            var sfx = new PresentationDiagnosticsRecorder();
            var feedback = new PresentationDiagnosticsRecorder();
            var viewBinder = new PresentationDiagnosticsRecorder();
            var feedbackSink = new PresentationDiagnosticsRecorder();
            var sink = new RecordingConsoleSink();
            var forwarder = new PresentationAssemblyDiagnosticsForwarder(sink, vfx, sfx, feedback, viewBinder, feedbackSink);

            vfx.Warn("vfx.def 未登记 id=\"vfx.missing\"");
            sfx.Warn("sfx.def 未登记 id=\"sfx.missing\"");
            feedback.Warn("feedback 规则使用了 from_display，但未注入 DisplayInfoResolver，跳过该动作");
            viewBinder.Warn("锚点查询：实体 \"unit.a\" 未绑定 View");
            feedbackSink.Warn("vfx \"vfx.spark\" 的反馈动作 attach=world 但没有可用世界坐标，跳过播放");

            forwarder.Pump();

            Assert.Equal(5, sink.Messages.Count);
            Assert.Contains("vfx.def 未登记 id=\"vfx.missing\"", sink.Messages);
            Assert.Contains("sfx.def 未登记 id=\"sfx.missing\"", sink.Messages);
            Assert.Contains("feedback 规则使用了 from_display，但未注入 DisplayInfoResolver，跳过该动作", sink.Messages);
            Assert.Contains("锚点查询：实体 \"unit.a\" 未绑定 View", sink.Messages);
            Assert.Contains("vfx \"vfx.spark\" 的反馈动作 attach=world 但没有可用世界坐标，跳过播放", sink.Messages);
        }

        [Fact]
        public void FiveSourceOverload_FifthSourceNull_OtherFourSourcesStillForwarded()
        {
            // 反向确认场景之一：第五个来源为 null（如某次装配未启用该链路）不应阻断其余四个来源，
            // 同四源重载"某来源为 null 静默跳过"的既有惯例一致。
            var vfx = new PresentationDiagnosticsRecorder();
            var sink = new RecordingConsoleSink();
            var forwarder = new PresentationAssemblyDiagnosticsForwarder(sink, vfx, null, null, null, feedbackSinkDiagnostics: null);

            vfx.Warn("vfx warning");
            forwarder.Pump();

            Assert.Single(sink.Messages);
            Assert.Equal("vfx warning", sink.Messages[0]);
        }

        [Fact]
        public void FiveSourceOverload_OnlyFifthSourceProvided_StillForwarded()
        {
            var feedbackSink = new PresentationDiagnosticsRecorder();
            var sink = new RecordingConsoleSink();
            var forwarder = new PresentationAssemblyDiagnosticsForwarder(sink, null, null, null, null, feedbackSink);

            feedbackSink.Warn("first");
            forwarder.Pump();
            Assert.Single(sink.Messages);

            // 无新增：再次 Pump 不应重复转发（同 SpriteRigDiagnosticsPump 逐条推进游标的既有惯例）。
            forwarder.Pump();
            Assert.Single(sink.Messages);

            feedbackSink.Warn("second");
            forwarder.Pump();
            Assert.Equal(new[] { "first", "second" }, sink.Messages);
        }

        [Fact]
        public void FiveSourceOverload_SameMessageTextFromFifthAndFirstSource_DedupedBySharedGate()
        {
            var vfx = new PresentationDiagnosticsRecorder();
            var feedbackSink = new PresentationDiagnosticsRecorder();
            var sink = new RecordingConsoleSink();
            var forwarder = new PresentationAssemblyDiagnosticsForwarder(sink, vfx, null, null, null, feedbackSink);

            vfx.Warn("identical message");
            feedbackSink.Warn("identical message");

            forwarder.Pump();

            Assert.Single(sink.Messages);
        }

        [Fact]
        public void FourSourceConstructor_StillWorks_AndIsEquivalentToFiveSourceWithNullFifth()
        {
            // 判断记录（旧四源构造函数内部委托给五源构造函数、第五参数传 null）：本用例锁定这一
            // 委托关系确实等价，防止未来重构时两个构造函数各自维护一份 _recorders 数组、行为悄悄
            // 分叉。
            var vfx = new PresentationDiagnosticsRecorder();
            var sinkA = new RecordingConsoleSink();
            var sinkB = new RecordingConsoleSink();
            var forwarderFourSource = new PresentationAssemblyDiagnosticsForwarder(sinkA, vfx, null, null, null);
            var forwarderFiveSourceNullFifth = new PresentationAssemblyDiagnosticsForwarder(sinkB, vfx, null, null, null, null);

            vfx.Warn("shared warning");
            forwarderFourSource.Pump();
            forwarderFiveSourceNullFifth.Pump();

            Assert.Equal(sinkA.Messages, sinkB.Messages);
        }
    }
}
