// M5-S4（ADR-0147，手感设计/02 第 7 节、01 第 3.5 节）：移动剪辑播放速率与动作分相速率。
// 复现：速度比序列 → 播放速率；重映射后的动作 → 逐相播放速率。期望值由规则公式算出，不写死裸数。
// 不变量：参考速度下速率 = stride_scale；单调；夹取区间；动作速率 × 实际毫秒 = 作者毫秒（动画不与判定时间线脱节）。
using System;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    public class LocomotionPresentationTests
    {
        private static readonly Id Hero = new Id("unit.hero");

        private static IEventBus CreateBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        [Theory]
        [InlineData(0.6, 1.0)]
        [InlineData(0.6, 1.5)]
        [InlineData(1.0, 0.8)]
        public void StrideRate_AtTheReferenceSpeed_EqualsStrideScale_AndIsMonotonicAndClamped(double reference, double strideScale)
        {
            Assert.Equal(
                LocomotionPresentationMath.StrideRate(reference, reference, strideScale),
                Math.Round(strideScale / LocomotionPresentationMath.RateQuantum) * LocomotionPresentationMath.RateQuantum, 9);

            var last = 0.0;
            for (var r = 0.01; r < 4.0; r += 0.01)
            {
                var rate = LocomotionPresentationMath.StrideRate(r, reference, strideScale);
                Assert.InRange(rate, LocomotionPresentationMath.MinRate - 1e-9, LocomotionPresentationMath.MaxRate + 1e-9);
                Assert.True(rate >= last - 1e-9);
                last = rate;
            }

            Assert.Equal(1.0, LocomotionPresentationMath.StrideRate(0.0, reference, strideScale)); // 停下：复位
        }

        [Fact]
        public void StrideRate_ScalesWithGroundSpeed_ProportionallyInsideTheClampBand()
        {
            // 实际速度翻倍，播放速率翻倍（夹取区间内、按取整步长容差）——脚不打滑。
            var a = LocomotionPresentationMath.StrideRate(0.4, 0.6, 1.0);
            var b = LocomotionPresentationMath.StrideRate(0.8, 0.6, 1.0);
            Assert.InRange(b / a, 2.0 - 2 * LocomotionPresentationMath.RateQuantum / a, 2.0 + 2 * LocomotionPresentationMath.RateQuantum / a);
        }

        [Fact]
        public void ReferenceRatio_FollowsThePresentingGaitFields()
        {
            Assert.Equal(0.6, LocomotionPresentationMath.ReferenceRatio(LocomotionGait.Walk, 0.6, 1.3));
            Assert.Equal(0.6, LocomotionPresentationMath.ReferenceRatio(LocomotionGait.Idle, 0.6, 1.3));
            Assert.Equal(1.0, LocomotionPresentationMath.ReferenceRatio(LocomotionGait.Run, 0.6, 1.3));
            Assert.Equal(1.3, LocomotionPresentationMath.ReferenceRatio(LocomotionGait.Sprint, 0.6, 1.3));
            Assert.Equal(1.0, LocomotionPresentationMath.ReferenceRatio(LocomotionGait.Sprint, 0.6, null));
        }

        [Fact]
        public void LeanDeg_IsProportionalToAcceleration_SignedAndClamped()
        {
            Assert.Equal(0.0, LocomotionPresentationMath.LeanDeg(5.0, 0.0)); // 字段为 0：关闭
            var forward = LocomotionPresentationMath.LeanDeg(5.0, 2.0);
            var back = LocomotionPresentationMath.LeanDeg(-5.0, 2.0);
            Assert.Equal(10.0, forward, 9);
            Assert.Equal(-forward, back, 9); // 加速前倾、减速后仰
            Assert.Equal(LocomotionPresentationMath.MaxLeanDeg, LocomotionPresentationMath.LeanDeg(1000.0, 2.0));
            Assert.Equal(-LocomotionPresentationMath.MaxLeanDeg, LocomotionPresentationMath.LeanDeg(-1000.0, 2.0));
        }

        [Fact]
        public void LocomotionPresentation_PublishesOnlyRealChanges_AndResetsToDefaults()
        {
            var loco = new LocomotionPresentation();
            var changes = 0;
            loco.Changed += _ => changes++;
            loco.Set(Hero, 1.0, 0.0);
            Assert.Equal(0, changes);
            loco.Set(Hero, 1.25, 3.0);
            loco.Set(Hero, 1.25, 3.0);
            Assert.Equal(1, changes);
            Assert.Equal(1.25, loco.GetStrideRate(Hero));
            Assert.Equal(3.0, loco.GetLeanDeg(Hero));
            loco.Set(Hero, 1.0, 0.0);
            Assert.Equal(2, changes);
            Assert.Equal(1.0, loco.GetStrideRate(Hero));
        }

        [Fact]
        public void ClipPlaybackRates_FollowActionPhases_AndLocomotion_AndResetWhenTheActionEnds()
        {
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);
            var loco = new LocomotionPresentation();
            var rates = new ClipPlaybackRates(bus, machine, loco);
            var changed = 0;
            rates.RateChanged += _ => changed++;

            // 移动态：取移动播放速率。
            bus.PublishImmediate(new UnitStateChangedEvent(Hero, "Idle", "Run"));
            loco.Set(Hero, 1.4, 0.0);
            Assert.Equal(1.4, rates.GetRate(Hero));
            Assert.Equal(1, changed);

            // 动作态：逐相取重映射速率（作者毫秒 ÷ 实际毫秒），与重映射的三相时长严格对应。
            const double s = 160, a = 80, r = 240;      // 作者毫秒
            const double sActual = 80, aActual = 80, rActual = 120; // 重映射后实际毫秒（加速）
            var cast = new Id("cast.1");
            bus.PublishImmediate(new SkillCastStartEvent(Hero, new Id("skill.slash"), 0.0));
            bus.PublishImmediate(new ActionStartedEvent(Hero, new Id("skill.slash"), cast, 0, 10, 0, true, s / sActual, a / aActual, r / rActual));
            Assert.Equal(AnimState.Attack, machine.GetState(Hero));
            Assert.Equal(s / sActual, rates.GetRate(Hero), 9);
            bus.PublishImmediate(new ActionPhaseChangedEvent(Hero, cast, ActionPhase.Active));
            Assert.Equal(a / aActual, rates.GetRate(Hero), 9);
            bus.PublishImmediate(new ActionPhaseChangedEvent(Hero, cast, ActionPhase.Recovery));
            Assert.Equal(r / rActual, rates.GetRate(Hero), 9);

            // 不变量：每相 速率 × 实际毫秒 = 作者毫秒。
            Assert.Equal(r, rates.GetRate(Hero) * rActual, 9);

            // 过期实例的相位事件不改当前记录；动作结束清除，回到 1。
            bus.PublishImmediate(new ActionPhaseChangedEvent(Hero, new Id("cast.old"), ActionPhase.Startup));
            Assert.Equal(r / rActual, rates.GetRate(Hero), 9);
            bus.PublishImmediate(new ActionFinishedEvent(Hero, cast));
            Assert.Equal(1.0, rates.GetRate(Hero));
        }

        [Fact]
        public void ClipPlaybackRates_ActionStartedBeforeCastStart_StillAppliesTheStartupRate()
        {
            // 两个事件同批到达，先后顺序不固定：先 action.started 后 skill.cast_start 时，状态切换后查询同样得到前摇速率。
            var bus = CreateBus();
            var machine = new AnimStateMachine(bus);
            var rates = new ClipPlaybackRates(bus, machine, null);
            bus.PublishImmediate(new ActionStartedEvent(Hero, new Id("skill.slash"), new Id("cast.2"), 0, 10, 0, true, 1.5, 1.0, 1.0));
            Assert.Equal(1.0, rates.GetRate(Hero)); // 还不是动作态
            bus.PublishImmediate(new SkillCastStartEvent(Hero, new Id("skill.slash"), 0.0));
            Assert.Equal(1.5, rates.GetRate(Hero));
        }
    }
}
