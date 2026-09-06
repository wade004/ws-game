using System;
using Core.Foundation.Common;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    /// <summary><see cref="ProceduralAnimSequencer"/> 八原语的时序与结束回调用例（见任务书"测试每个
    /// 原语的时序与结束回调"）。</summary>
    public class ProceduralAnimSequencerTests
    {
        [Fact]
        public void Move_SamplesLinearProgress_TowardOffset_ThenHoldsAndCompletes()
        {
            var seq = new ProceduralAnimSequencer();
            var samples = new System.Collections.Generic.List<Vec2>();
            var completed = false;

            seq.Move(new MoveParams(new Vec2(10, 0), 1.0), onSample: v => samples.Add(v), onComplete: () => completed = true);

            Assert.Equal(new Vec2(0, 0), samples[0]);

            seq.Update(0.5);
            Assert.Equal(new Vec2(5, 0), samples[^1]);
            Assert.False(completed);

            seq.Update(0.5);
            Assert.Equal(new Vec2(10, 0), samples[^1]);
            Assert.True(completed);
        }

        [Fact]
        public void Scale_TriangleWave_PunchesThenReturns()
        {
            var seq = new ProceduralAnimSequencer();
            double last = 1.0;
            var completed = false;

            seq.Scale(new ScaleParams(1.5, 1.0), onSample: v => last = v, onComplete: () => completed = true);

            seq.Update(0.5); // 前半程末端：峰值
            Assert.Equal(1.5, last, 6);

            seq.Update(0.5); // 后半程末端：回到 1.0
            Assert.Equal(1.0, last, 6);
            Assert.True(completed);
        }

        [Fact]
        public void Flash_DecaysLinearlyToZero()
        {
            var seq = new ProceduralAnimSequencer();
            double last = -1;

            seq.Flash(new FlashParams(1.0, 1.0), onSample: v => last = v);

            seq.Update(0.25);
            Assert.Equal(0.75, last, 6);

            seq.Update(0.75);
            Assert.Equal(0.0, last, 6);
        }

        [Fact]
        public void Trail_DecaysLinearlyToZero()
        {
            var seq = new ProceduralAnimSequencer();
            double last = -1;

            seq.Trail(new TrailParams(0.5), onSample: v => last = v);

            seq.Update(0.25);
            Assert.Equal(0.5, last, 6);
        }

        [Fact]
        public void Stagger_TriangleWave_RecoilsThenReturns()
        {
            var seq = new ProceduralAnimSequencer();
            Vec2 last = default;

            seq.Stagger(new StaggerParams(new Vec2(-3, 0), 1.0), onSample: v => last = v);

            seq.Update(0.5);
            Assert.Equal(new Vec2(-3, 0), last);

            seq.Update(0.5);
            Assert.Equal(new Vec2(0, 0), last);
        }

        [Fact]
        public void Topple_EasesToTarget_ThenHolds()
        {
            var seq = new ProceduralAnimSequencer();
            double last = 0;
            var completed = false;

            seq.Topple(ToppleParams.Default(1.0), onSample: v => last = v, onComplete: () => completed = true);

            seq.Update(1.0);
            Assert.Equal(Math.PI / 2.0, last, 6);
            Assert.True(completed);
        }

        [Fact]
        public void Fade_EasesToTargetAlpha_ThenHolds()
        {
            var seq = new ProceduralAnimSequencer();
            double last = 1.0;

            seq.Fade(new FadeParams(0.0, 1.0), onSample: v => last = v);

            seq.Update(1.0);
            Assert.Equal(0.0, last, 6);
        }

        [Fact]
        public void Rotate_EasesToDelta_ThenHolds()
        {
            var seq = new ProceduralAnimSequencer();
            double last = 0;

            seq.Rotate(new RotateParams(Math.PI, 1.0), onSample: v => last = v);

            seq.Update(0.5);
            Assert.Equal(Math.PI / 2.0, last, 6);
        }

        [Fact]
        public void SameKind_NewTrigger_ReplacesPrevious_FiresPreviousOnCompleteImmediately()
        {
            var seq = new ProceduralAnimSequencer();
            var firstCompleted = false;
            var secondSamples = 0;

            seq.Flash(new FlashParams(1.0, 10.0), onComplete: () => firstCompleted = true);
            Assert.False(firstCompleted);

            seq.Flash(new FlashParams(1.0, 1.0), onSample: _ => secondSamples++);

            Assert.True(firstCompleted);
            Assert.Equal(1, secondSamples); // 新实例触发时立即采样一次（progress=0）。
        }

        [Fact]
        public void DifferentKinds_PlayIndependently()
        {
            var seq = new ProceduralAnimSequencer();
            double flashValue = -1;
            double scaleValue = -1;

            seq.Flash(new FlashParams(1.0, 1.0), onSample: v => flashValue = v);
            seq.Scale(new ScaleParams(2.0, 1.0), onSample: v => scaleValue = v);

            seq.Update(0.5);

            Assert.Equal(0.5, flashValue, 6);
            Assert.Equal(2.0, scaleValue, 6);
        }

        [Fact]
        public void NonPositiveDuration_Throws()
        {
            var seq = new ProceduralAnimSequencer();
            Assert.Throws<ArgumentOutOfRangeException>(() => seq.Flash(new FlashParams(1.0, 0.0)));
        }

        [Fact]
        public void Reset_ClearsInFlightInstances_WithoutFiringCallbacks()
        {
            var seq = new ProceduralAnimSequencer();
            var completed = false;
            seq.Flash(new FlashParams(1.0, 1.0), onComplete: () => completed = true);

            seq.Reset();
            seq.Update(10.0);

            Assert.False(completed);
        }
    }
}
