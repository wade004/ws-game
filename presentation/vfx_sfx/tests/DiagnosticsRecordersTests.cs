using System;
using Core.Foundation.Common;
using Presentation.VfxSfx.Contracts;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    /// <summary>
    /// T-L16（测试覆盖剩余项第四批，vfx_sfx 部分）：<see cref="PresentationDiagnosticsRecorder"/> 与
    /// <see cref="SfxPlaybackDiagnosticsRecorder"/> / <see cref="SfxPlaybackRecord"/> 的默认值与记账规则。
    /// </summary>
    public class DiagnosticsRecordersTests
    {
        // ---------------- PresentationDiagnosticsRecorder ----------------

        [Fact]
        public void PresentationRecorder_StartsEmpty()
        {
            Assert.Empty(new PresentationDiagnosticsRecorder().Warnings);
        }

        [Fact]
        public void PresentationRecorder_AppendsInOrder_KeepsDuplicates_AndEmptyStrings()
        {
            var recorder = new PresentationDiagnosticsRecorder();

            recorder.Warn("a");
            recorder.Warn("a");
            recorder.Warn(string.Empty);
            recorder.Warn("b");

            Assert.Equal(new[] { "a", "a", string.Empty, "b" }, recorder.Warnings);
        }

        [Fact]
        public void PresentationRecorder_NullMessage_Throws_AndRecordsNothing()
        {
            var recorder = new PresentationDiagnosticsRecorder();

            var ex = Assert.Throws<ArgumentNullException>(() => recorder.Warn(null!));

            Assert.Equal("message", ex.ParamName);
            Assert.Empty(recorder.Warnings);
        }

        [Fact]
        public void PresentationRecorder_Warnings_IsALiveViewOverTheSameList()
        {
            var recorder = new PresentationDiagnosticsRecorder();
            var view = recorder.Warnings;

            recorder.Warn("later");

            Assert.Same(view, recorder.Warnings);
            Assert.Equal("later", Assert.Single(view));
        }

        [Fact]
        public void PresentationRecorder_ImplementsTheDiagnosticsInterface()
        {
            IPresentationDiagnostics diagnostics = new PresentationDiagnosticsRecorder();

            diagnostics.Warn("via interface");

            Assert.Equal("via interface", Assert.Single(((PresentationDiagnosticsRecorder)diagnostics).Warnings));
        }

        // ---------------- SfxPlaybackDiagnosticsRecorder ----------------

        private static readonly Id SfxA = new Id("sfx.diag_a");
        private static readonly Id SfxB = new Id("sfx.diag_b");

        [Fact]
        public void SfxRecorder_StartsAtZero_WithNoLastPlay()
        {
            ISfxPlaybackDiagnostics recorder = new SfxPlaybackDiagnosticsRecorder();

            Assert.Equal(0, recorder.PlayRequestedCount);
            Assert.Equal(0, recorder.PlayStartedCount);
            Assert.Equal(0, recorder.PlayDroppedCount);
            Assert.Null(recorder.LastPlay);
        }

        [Fact]
        public void SfxRecorder_CountersAreIndependent()
        {
            var recorder = new SfxPlaybackDiagnosticsRecorder();

            recorder.RecordRequested();
            recorder.RecordRequested();
            recorder.RecordRequested();
            recorder.RecordDropped();
            recorder.RecordStarted(SfxA);

            Assert.Equal(3, recorder.PlayRequestedCount);
            Assert.Equal(1, recorder.PlayDroppedCount);
            Assert.Equal(1, recorder.PlayStartedCount);
        }

        [Fact]
        public void SfxRecorder_RecordStarted_SequenceEqualsStartedCount_AndLastPlayIsTheLatest()
        {
            var recorder = new SfxPlaybackDiagnosticsRecorder();

            recorder.RecordStarted(SfxA);
            var first = recorder.LastPlay!.Value;
            recorder.RecordStarted(SfxB);
            var second = recorder.LastPlay!.Value;

            Assert.Equal(SfxA, first.ResourceRef);
            Assert.Equal(1, first.Sequence);
            Assert.Equal(SfxB, second.ResourceRef);
            Assert.Equal(recorder.PlayStartedCount, second.Sequence);
            Assert.True(second.Sequence > first.Sequence);
        }

        [Fact]
        public void SfxRecorder_RequestedAndDropped_DoNotTouchLastPlayOrSequence()
        {
            var recorder = new SfxPlaybackDiagnosticsRecorder();
            recorder.RecordStarted(SfxA);
            var last = recorder.LastPlay;

            recorder.RecordRequested();
            recorder.RecordDropped();

            Assert.Equal(last, recorder.LastPlay);
            Assert.Equal(1, recorder.PlayStartedCount);
        }

        [Fact]
        public void SfxPlaybackRecord_ExposesConstructorArguments()
        {
            var record = new SfxPlaybackRecord(SfxA, 7);

            Assert.Equal(SfxA, record.ResourceRef);
            Assert.Equal(7, record.Sequence);
        }
    }
}
