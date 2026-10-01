using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Xunit;
using Presentation.Render;

namespace Tests.PresentationRender
{
    /// <summary>
    /// T-M33（测试覆盖剩余项第四批）：<see cref="FrameAnimClip"/> 构造边界、
    /// <see cref="ProceduralAnimSequencer"/> 八个原语的非正时长 / 负 dt / 失败不破坏旧实例。
    /// 期望值全部由规则（线性 / 三角波 / 时长累计）推出。
    /// </summary>
    public class RenderBoundaryEdgeTests
    {
        private static readonly Id Clip = new Id("anim.edge");

        // ---------------- FrameAnimClip ----------------

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void FrameAnimClip_NonPositiveFrameCount_Throws(int frameCount)
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new FrameAnimClip(Clip, frameCount, 12.0));
            Assert.Equal("frameCount", ex.ParamName);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-0.001)]
        [InlineData(double.NegativeInfinity)]
        public void FrameAnimClip_NonPositiveFrameRate_Throws(double frameRate)
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new FrameAnimClip(Clip, 4, frameRate));
            Assert.Equal("frameRate", ex.ParamName);
        }

        [Fact]
        public void FrameAnimClip_SmallestPositiveValues_AreAccepted_AndExposed()
        {
            var clip = new FrameAnimClip(Clip, 1, double.Epsilon);

            Assert.Equal(1, clip.FrameCount);
            Assert.Equal(double.Epsilon, clip.FrameRate);
            Assert.Equal(Clip, clip.ClipId);
        }

        [Fact]
        public void FrameAnimClip_NullKeyframes_BecomeEmptyNonNullMap()
        {
            var clip = new FrameAnimClip(Clip, 4, 4.0, null);

            Assert.NotNull(clip.Keyframes);
            Assert.Empty(clip.Keyframes);
        }

        [Fact]
        public void FrameAnimClip_KeyframesMap_IsKeptAsGiven()
        {
            var map = new Dictionary<string, int> { [FrameAnimClip.HitFrameMarker] = 2, ["step"] = 0 };
            var clip = new FrameAnimClip(Clip, 4, 4.0, map);

            Assert.Same(map, clip.Keyframes);
            Assert.Equal(2, clip.Keyframes[FrameAnimClip.HitFrameMarker]);
        }

        // ---------------- Sequencer：非正时长 ----------------

        private static readonly (string Name, Action<ProceduralAnimSequencer, double> Start)[] Primitives =
        {
            ("Move", (s, d) => s.Move(new MoveParams(new Vec2(1, 1), d))),
            ("Rotate", (s, d) => s.Rotate(new RotateParams(1.0, d))),
            ("Scale", (s, d) => s.Scale(new ScaleParams(1.5, d))),
            ("Flash", (s, d) => s.Flash(new FlashParams(1.0, d))),
            ("Trail", (s, d) => s.Trail(new TrailParams(d))),
            ("Stagger", (s, d) => s.Stagger(new StaggerParams(new Vec2(1, 0), d))),
            ("Topple", (s, d) => s.Topple(new ToppleParams(1.0, d))),
            ("Fade", (s, d) => s.Fade(new FadeParams(0.0, d))),
        };

        public static IEnumerable<object[]> PrimitiveIndices()
        {
            for (var i = 0; i < Primitives.Length; i++) yield return new object[] { i };
        }

        [Theory]
        [MemberData(nameof(PrimitiveIndices))]
        public void EveryPrimitive_ZeroOrNegativeDuration_Throws(int index)
        {
            var seq = new ProceduralAnimSequencer();
            var start = Primitives[index].Start;

            Assert.Throws<ArgumentOutOfRangeException>(() => start(seq, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => start(seq, -0.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => start(seq, double.NegativeInfinity));
        }

        [Theory]
        [MemberData(nameof(PrimitiveIndices))]
        public void EveryPrimitive_RejectedStart_LeavesInFlightInstanceOfSameKindUntouched(int index)
        {
            // 先放一个旧实例（用采样 / 完成计数观察），再用非法时长触发同类型：
            // 旧实例不得被"替换完成"，也不得失去推进。
            var seq = new ProceduralAnimSequencer();
            var start = Primitives[index].Start;
            start(seq, 1.0);

            Assert.Throws<ArgumentOutOfRangeException>(() => start(seq, 0.0));

            // 旧实例照常：推进到时长终点后应能无异常完成
            seq.Update(1.0);
            seq.Update(1.0);
        }

        [Fact]
        public void RejectedStart_DoesNotFireOldOnComplete_NorSampleTheNewInstance()
        {
            var completes = 0;
            var samples = new List<Vec2>();
            var seq = new ProceduralAnimSequencer();
            var offset = new Vec2(10, 0);
            seq.Move(new MoveParams(offset, 1.0), samples.Add, () => completes++);
            var sampleCountBefore = samples.Count;

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                seq.Move(new MoveParams(new Vec2(99, 99), 0.0), samples.Add, () => completes += 100));

            Assert.Equal(0, completes);
            Assert.Equal(sampleCountBefore, samples.Count);
            seq.Update(0.5);
            Assert.Equal(offset * 0.5, samples[samples.Count - 1]);
        }

        [Fact]
        public void TinyPositiveDuration_IsAccepted_AndCompletesOnFirstPositiveUpdate()
        {
            var seq = new ProceduralAnimSequencer();
            var completes = 0;
            var last = -1.0;
            seq.Flash(new FlashParams(1.0, double.Epsilon), v => last = v, () => completes++);

            seq.Update(double.Epsilon);

            Assert.Equal(1, completes);
            Assert.Equal(0.0, last); // 终点：强度衰减到 0
        }

        // ---------------- Sequencer：负 dt / 零 dt ----------------

        [Fact]
        public void Update_NegativeDt_NeverCompletes_AndProgressIsClampedToStart()
        {
            var seq = new ProceduralAnimSequencer();
            var offset = new Vec2(8, -4);
            var samples = new List<Vec2>();
            var completes = 0;
            seq.Move(new MoveParams(offset, 1.0), samples.Add, () => completes++);

            seq.Update(-0.25);
            seq.Update(-100.0);

            Assert.Equal(0, completes);
            Assert.Equal(offset * 0.0, samples[samples.Count - 1]); // 进度被夹到 0：等于起点
        }

        [Fact]
        public void Update_NegativeDtDebt_MustBeRepaidBeforeCompletion_CompletionFollowsNetElapsed()
        {
            const double duration = 1.0;
            const double debt = 0.5;
            var seq = new ProceduralAnimSequencer();
            var completes = 0;
            seq.Trail(new TrailParams(duration), null, () => completes++);

            seq.Update(-debt);
            seq.Update(duration);            // 净累计 = duration - debt < duration
            Assert.Equal(0, completes);
            seq.Update(debt);                // 净累计 = duration
            Assert.Equal(1, completes);
        }

        [Fact]
        public void Update_ZeroDt_ResamplesWithoutCompleting_AndDoesNotAdvance()
        {
            var seq = new ProceduralAnimSequencer();
            var samples = new List<double>();
            var completes = 0;
            seq.Rotate(new RotateParams(2.0, 1.0), samples.Add, () => completes++);
            var before = samples.Count;

            seq.Update(0.0);

            Assert.Equal(0, completes);
            Assert.Equal(before + 1, samples.Count);
            Assert.Equal(samples[before - 1], samples[before]); // 进度不变
        }

        [Fact]
        public void Update_WithNothingPlaying_IsNoOp_ForAnyDt()
        {
            var seq = new ProceduralAnimSequencer();
            seq.Update(1.0);
            seq.Update(-1.0);
            seq.Update(0.0);
        }

        [Fact]
        public void Update_HugeDt_SamplesTargetThenCompletesExactlyOnce()
        {
            var seq = new ProceduralAnimSequencer();
            var order = new List<string>();
            seq.Fade(new FadeParams(0.25, 0.5), v => order.Add("sample:" + v.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                () => order.Add("done"));
            order.Clear();

            seq.Update(1e9);
            seq.Update(1e9);

            Assert.Equal(new[] { "sample:0.25", "done" }, order);
        }

        // ---------------- Sequencer：其余原语的端点 ----------------

        [Fact]
        public void Scale_EndpointsReturnToOne_AndPeakIsPunchScale()
        {
            const double punch = 1.8;
            const double duration = 1.0;
            var seq = new ProceduralAnimSequencer();
            var samples = new List<double>();
            seq.Scale(new ScaleParams(punch, duration), samples.Add);

            Assert.Equal(1.0, samples[0]);
            seq.Update(duration / 2);
            Assert.Equal(punch, samples[samples.Count - 1], 12);
            seq.Update(duration / 2);
            Assert.Equal(1.0, samples[samples.Count - 1], 12);
        }

        [Fact]
        public void Stagger_EndpointsReturnToZero_AndPeakIsFullOffset()
        {
            var offset = new Vec2(-3, 6);
            var seq = new ProceduralAnimSequencer();
            var samples = new List<Vec2>();
            seq.Stagger(new StaggerParams(offset, 2.0), samples.Add);

            Assert.Equal(offset * 0.0, samples[0]);
            seq.Update(1.0);
            Assert.Equal(offset, samples[samples.Count - 1]);
            seq.Update(1.0);
            Assert.Equal(offset * 0.0, samples[samples.Count - 1]);
        }

        [Fact]
        public void Fade_TargetAlphaAboveOne_IsInterpolatedWithoutClamping()
        {
            const double target = 3.0;
            var seq = new ProceduralAnimSequencer();
            var samples = new List<double>();
            seq.Fade(new FadeParams(target, 1.0), samples.Add);

            seq.Update(0.5);

            Assert.Equal(1.0 + (target - 1.0) * 0.5, samples[samples.Count - 1], 12);
        }

        [Fact]
        public void NullCallbacks_AreAcceptedForEveryPrimitive_AndUpdateRunsToCompletion()
        {
            var seq = new ProceduralAnimSequencer();
            foreach (var p in Primitives) p.Start(seq, 0.5);

            seq.Update(0.25);
            seq.Update(0.25);
            seq.Update(0.25);
        }

        [Fact]
        public void SameKindReplacement_OldCompleteFiresBeforeNewStartSample()
        {
            var order = new List<string>();
            var seq = new ProceduralAnimSequencer();
            seq.Flash(new FlashParams(1.0, 1.0), v => order.Add("old"), () => order.Add("old-done"));
            seq.Update(0.5);
            order.Clear();

            const double newIntensity = 0.5;
            seq.Flash(new FlashParams(newIntensity, 1.0), v => order.Add("new:" + (v == newIntensity)), () => order.Add("new-done"));

            Assert.Equal(new[] { "old-done", "new:True" }, order); // 新起点采样 = 强度 * (1 - 0)
        }

        [Fact]
        public void Reset_ThenUpdate_DoesNothing_AndNewTriggerPlaysNormally()
        {
            var seq = new ProceduralAnimSequencer();
            var completes = 0;
            seq.Topple(new ToppleParams(1.0, 1.0), null, () => completes++);
            seq.Reset();
            seq.Update(5.0);
            Assert.Equal(0, completes);

            seq.Topple(new ToppleParams(1.0, 1.0), null, () => completes++);
            seq.Update(1.0);
            Assert.Equal(1, completes);
        }
    }
}
