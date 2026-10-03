using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.InputMap;
using Xunit;
using static Tests.Foundation.InputMap.BufferRig;

namespace Tests.Foundation.InputMap
{
    /// <summary>
    /// 输入层补全（ADR-0143）：蓄力自动释放与下限门槛、按住状态查询。每个机制一条复现、一条不变量；期望值全部由规则字段与
    /// <c>FeelCalibration.MillisecondsToTicks</c> 的换算算出，不写死裸 tick 数。
    /// </summary>
    public class InputBufferChargeAndHeldTests
    {
        private static readonly Id ChargeAction = new Id("input.action.attack");

        private sealed class FixedChargeRules : IChargeRuleSource
        {
            private readonly ChargeRule _rule;

            public FixedChargeRules(ChargeRule rule) => _rule = rule;

            public bool TryGetChargeRule(Id actorId, Id actionId, out ChargeRule rule)
            {
                rule = _rule;
                return actionId.Equals(ChargeAction);
            }
        }

        private static BufferRig NewRig(double holdMs, ChargeRule? rule, List<InputChargeReadyEvent> ready)
        {
            var rig = new BufferRig(new[] { Def("input.action.attack", ActionClass.Attack, holdMs: holdMs) }, "feel.preset.arpg_responsive");
            if (rule.HasValue) rig.Buffer.ChargeRules = new FixedChargeRules(rule.Value);
            rig.Bus.Subscribe<InputChargeReadyEvent>(InputMapEventKeys.ChargeReady, e => ready.Add(e));
            return rig;
        }

        // ---------------------------------------------------------------- 蓄力自动释放

        [Fact]
        public void Charge_HeldPastMaxTicks_AutoReleasesAtExactlyMax_AndEmitsChargeReadyOnce()
        {
            var ready = new List<InputChargeReadyEvent>();
            var probe = new BufferRig(new[] { Def("input.action.attack", ActionClass.Attack, holdMs: 100) }, "feel.preset.arpg_responsive");
            var maxTicks = probe.Ticks(800);
            var rig = NewRig(100, new ChargeRule(probe.Ticks(200), maxTicks, false), ready);

            rig.Step(() => rig.Buffer.Press(Actor, ChargeAction));
            var pressTick = rig.Tick;

            // 一直按着，推进到上限之后很多 tick：恰好在 pressTick + maxTicks 那一 tick 释放，之后不再重复发事件。
            var releasedAt = -1;
            for (var i = 0; i < maxTicks + 20 && releasedAt < 0; i++)
            {
                rig.Step();
                if (ready.Count > 0) releasedAt = rig.Tick;
            }

            Assert.Equal(pressTick + maxTicks, releasedAt);
            Assert.Single(ready);
            Assert.Equal(maxTicks, ready[0].HeldTicks);
            Assert.True(rig.Buffer.TryPeek(Actor, out var rec));
            Assert.Equal(BufferHoldState.HoldReleased, rec.HoldState);
            Assert.Equal(maxTicks, rec.HeldTicks);

            // 按键此后仍按着也不会再次触发。
            for (var i = 0; i < 5; i++) rig.Step();
            Assert.Single(ready);
        }

        [Fact]
        public void Charge_ReleasedBeforeMax_NeverEmitsChargeReady_AndLateReleaseNeverExceedsMax()
        {
            var ready = new List<InputChargeReadyEvent>();
            var probe = new BufferRig(new[] { Def("input.action.attack", ActionClass.Attack, holdMs: 100) }, "feel.preset.arpg_responsive");
            var maxTicks = probe.Ticks(800);
            var minTicks = probe.Ticks(200);

            // 不变量 1：上限之前抬起——不发事件，held 等于实际按住时长。
            var rig = NewRig(100, new ChargeRule(minTicks, maxTicks, false), ready);
            rig.Step(() => rig.Buffer.Press(Actor, ChargeAction));
            for (var i = 0; i < minTicks + 3; i++) rig.Step();
            rig.Step(() => rig.Buffer.Release(Actor, ChargeAction));
            Assert.Empty(ready);
            Assert.True(rig.Buffer.TryPeek(Actor, out var early));
            Assert.Equal(BufferHoldState.HoldReleased, early.HoldState);
            Assert.InRange(early.HeldTicks, minTicks, maxTicks - 1);

            // 不变量 2：按住远超上限后才抬起——记录的 held 恒为上限，不超。
            var ready2 = new List<InputChargeReadyEvent>();
            var rig2 = NewRig(100, new ChargeRule(minTicks, maxTicks, false), ready2);
            rig2.Step(() => rig2.Buffer.Press(Actor, ChargeAction));
            for (var i = 0; i < maxTicks; i++) rig2.Step();
            Assert.True(rig2.Buffer.TryPeek(Actor, out var late));
            Assert.Equal(maxTicks, late.HeldTicks);
            Assert.Single(ready2);
            rig2.Step(() => rig2.Buffer.Release(Actor, ChargeAction)); // 迟到的抬起不改变已自动释放的记录
            Assert.Single(ready2);
            Assert.True(rig2.Buffer.TryPeek(Actor, out var afterRelease));
            Assert.Equal(maxTicks, afterRelease.HeldTicks);
        }

        [Fact]
        public void Charge_ActionClockPause_StopsTheChargeClock_AndNoRuleSourceKeepsLegacyBehavior()
        {
            var ready = new List<InputChargeReadyEvent>();
            var probe = new BufferRig(new[] { Def("input.action.attack", ActionClass.Attack, holdMs: 100) }, "feel.preset.arpg_responsive");
            var maxTicks = probe.Ticks(400);
            var rig = NewRig(100, new ChargeRule(0, maxTicks, false), ready);

            rig.Step(() => rig.Buffer.Press(Actor, ChargeAction));
            var pausedTicks = 10;
            rig.Clock.Pause(Actor, pausedTicks);
            var releasedAt = -1;
            for (var i = 0; i < maxTicks + pausedTicks + 5; i++)
            {
                rig.Step();
                if (releasedAt < 0 && ready.Count > 0) releasedAt = rig.Tick;
            }

            // 顿帧期间动作时钟不走：释放比无顿帧晚恰好 pausedTicks 个 tick。
            Assert.Equal(0 + maxTicks + pausedTicks, releasedAt);

            // 没有注入规则来源：按住再久也不自动释放（既有行为）。
            var ready2 = new List<InputChargeReadyEvent>();
            var legacy = NewRig(100, null, ready2);
            legacy.Step(() => legacy.Buffer.Press(Actor, ChargeAction));
            for (var i = 0; i < maxTicks * 3; i++) legacy.Step();
            Assert.Empty(ready2);
            Assert.False(legacy.Buffer.TryPeek(Actor, out _)); // 仍是 HoldPending，不可消费
        }

        // ---------------------------------------------------------------- 蓄力下限门槛

        [Fact]
        public void BelowMin_Cancel_DropsTheRecordWithChargeBelowMin_ReleaseKeepsLowestTier()
        {
            var probe = new BufferRig(new[] { Def("input.action.attack", ActionClass.Attack, holdMs: 100) }, "feel.preset.arpg_responsive");
            var holdTicks = probe.Ticks(100);
            var minTicks = probe.Ticks(500);
            var maxTicks = probe.Ticks(1000);
            var heldTicks = holdTicks + 2; // 过了按住阈值、不足下限
            Assert.True(heldTicks < minTicks);

            // cancel：记录丢弃，原因 ChargeBelowMin，缓冲里不再有候选。
            var cancelRig = NewRig(100, new ChargeRule(minTicks, maxTicks, true), new List<InputChargeReadyEvent>());
            cancelRig.Step(() => cancelRig.Buffer.Press(Actor, ChargeAction));
            for (var i = 0; i < heldTicks; i++) cancelRig.Step();
            cancelRig.Step(() => cancelRig.Buffer.Release(Actor, ChargeAction));
            Assert.False(cancelRig.Buffer.TryPeek(Actor, out _));
            Assert.Single(cancelRig.Drops);
            Assert.EndsWith(":input.action.attack:ChargeBelowMin", cancelRig.Drops[0]);

            // release（缺省）：按最低档释放——记录保留、HoldReleased，held 是实际按住时长（蓄力比例由时间线按 min 夹成 0）。
            var releaseRig = NewRig(100, new ChargeRule(minTicks, maxTicks, false), new List<InputChargeReadyEvent>());
            releaseRig.Step(() => releaseRig.Buffer.Press(Actor, ChargeAction));
            for (var i = 0; i < heldTicks; i++) releaseRig.Step();
            releaseRig.Step(() => releaseRig.Buffer.Release(Actor, ChargeAction));
            Assert.True(releaseRig.Buffer.TryPeek(Actor, out var rec));
            Assert.Equal(BufferHoldState.HoldReleased, rec.HoldState);
            Assert.Empty(releaseRig.Drops);
        }

        [Fact]
        public void BelowMin_Cancel_NeverAffectsTapsOrFullCharges()
        {
            var probe = new BufferRig(new[] { Def("input.action.attack", ActionClass.Attack, holdMs: 100) }, "feel.preset.arpg_responsive");
            var minTicks = probe.Ticks(500);
            var maxTicks = probe.Ticks(1000);
            var rule = new ChargeRule(minTicks, maxTicks, true);

            // 点按（未过按住阈值）：Tap，不受门槛影响。
            var tapRig = NewRig(100, rule, new List<InputChargeReadyEvent>());
            tapRig.Step(() => tapRig.Buffer.Press(Actor, ChargeAction));
            tapRig.Step(() => tapRig.Buffer.Release(Actor, ChargeAction));
            Assert.True(tapRig.Buffer.TryPeek(Actor, out var tap));
            Assert.Equal(BufferHoldState.Tap, tap.HoldState);
            Assert.Empty(tapRig.Drops);

            // 蓄满（不低于下限）：正常释放。
            var fullRig = NewRig(100, rule, new List<InputChargeReadyEvent>());
            fullRig.Step(() => fullRig.Buffer.Press(Actor, ChargeAction));
            for (var i = 0; i < minTicks + 1; i++) fullRig.Step();
            fullRig.Step(() => fullRig.Buffer.Release(Actor, ChargeAction));
            Assert.True(fullRig.Buffer.TryPeek(Actor, out var full));
            Assert.Equal(BufferHoldState.HoldReleased, full.HoldState);
            Assert.Empty(fullRig.Drops);
        }

        // ---------------------------------------------------------------- 按住状态查询

        [Fact]
        public void IsHeld_TracksPressToRelease_AndClearForgetsIt()
        {
            var rig = new BufferRig(new[] { Def("input.action.attack", ActionClass.Attack) }, "feel.preset.arpg_responsive");
            Assert.False(rig.Buffer.IsHeld(Actor, ChargeAction));

            rig.Step(() => rig.Buffer.Press(Actor, ChargeAction));
            Assert.True(rig.Buffer.IsHeld(Actor, ChargeAction));
            for (var i = 0; i < 5; i++) rig.Step();
            Assert.True(rig.Buffer.IsHeld(Actor, ChargeAction));

            rig.Step(() => rig.Buffer.Release(Actor, ChargeAction));
            Assert.False(rig.Buffer.IsHeld(Actor, ChargeAction));

            // 不变量：一次完整点按（Submit 同 tick 按下+抬起）结束后不留"按着"的状态。
            rig.Step(() => rig.Buffer.Submit(Actor, ChargeAction));
            Assert.False(rig.Buffer.IsHeld(Actor, ChargeAction));

            // Clear 同时清掉按住状态（已知限制：清空后仍按着的键不会自动恢复为"按着"，要重新按下）。
            rig.Step(() => rig.Buffer.Press(Actor, ChargeAction));
            Assert.True(rig.Buffer.IsHeld(Actor, ChargeAction));
            rig.Buffer.Clear(Actor);
            Assert.False(rig.Buffer.IsHeld(Actor, ChargeAction));
        }
    }
}
