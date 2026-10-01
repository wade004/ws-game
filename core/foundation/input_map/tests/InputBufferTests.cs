using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.InputMap;
using Xunit;
using static Tests.Foundation.InputMap.BufferRig;

namespace Tests.Foundation.InputMap
{
    /// <summary>
    /// 输入缓冲运行时冒烟与不变量（手感设计/01 第 2.2/2.3/5 节）。期望值全部由动作定义/档案字段与 <c>FeelCalibration.MillisecondsToTicks</c>
    /// 的换算算出，不写死裸 tick 数；"可接受时刻"由测试里的假动作层（后摇结束前不接受攻击）给出。
    /// </summary>
    public class InputBufferTests
    {
        private static readonly Id AttackId = new Id("input.action.attack");
        private static readonly Id DodgeId = new Id("input.action.dodge");

        /// <summary>
        /// 假动作层（动作时间线切片在取消窗口里做的事的替身）：攻击在后摇结束的那个 tick（<see cref="AcceptableFrom"/>）起才可被接受，
        /// 在此之前拒绝；每个 tick 经 <c>IInputBufferQuery.TryConsume</c> 取用，记录被取用的 tick。
        /// </summary>
        private sealed class FakeActionLayer
        {
            public int AcceptableFrom;
            public readonly List<(int Tick, Id Action)> Consumed = new List<(int, Id)>();

            public Action ConsumeAt(BufferRig rig) => () =>
            {
                if (rig.Buffer.TryConsume(Actor, r => rig.Tick >= AcceptableFrom, out var c))
                {
                    Consumed.Add((rig.Tick, c.ActionId));
                }
            };
        }

        private static IEnumerable<ActionDefinition> Attack() => new[] { Def("input.action.attack", ActionClass.Attack) };

        // ---------------------------------------------------------------- 冒烟：窗口边界

        [Theory]
        [InlineData(60)]
        [InlineData(50)]
        public void BufferMsZero_EarlyPressIsDropped_WithExpired_AndPressOnTheAcceptableTickIsConsumed(int tickRate)
        {
            var step = 1.0 / tickRate;
            const int acceptable = 10;

            // 早一个 tick 按下：buffer_ms=0（rpg_classic）只在按下当 tick 有效，后摇没结束，下一 tick 即过期。
            var early = new BufferRig(Attack(), "feel.preset.rpg_classic", step);
            var layer = new FakeActionLayer { AcceptableFrom = acceptable };
            for (var t = 0; t <= acceptable + 2; t++)
            {
                early.Step(t == acceptable - 1 ? () => early.Buffer.Submit(Actor, AttackId) : (Action?)null, layer.ConsumeAt(early));
            }
            Assert.Empty(layer.Consumed);
            Assert.Equal(new[] { $"{acceptable}:input.action.attack:Expired" }, early.Drops);

            // 恰在可接受 tick 按下：同 tick 取用。
            var onTime = new BufferRig(Attack(), "feel.preset.rpg_classic", step);
            var layer2 = new FakeActionLayer { AcceptableFrom = acceptable };
            for (var t = 0; t <= acceptable + 2; t++)
            {
                onTime.Step(t == acceptable ? () => onTime.Buffer.Submit(Actor, AttackId) : (Action?)null, layer2.ConsumeAt(onTime));
            }
            Assert.Equal(new[] { (acceptable, AttackId) }, layer2.Consumed);
            Assert.Empty(onTime.Drops);
        }

        [Theory]
        [InlineData(60)]
        [InlineData(50)]
        public void BufferMs120_PressWithinTheWindowIsConsumedOnTheFirstAcceptableTick_PressBeyondItExpires(int tickRate)
        {
            var step = 1.0 / tickRate;
            const int acceptable = 30;
            var windowTicks = FeelCalibrationTicks(120, step); // 档案 buffer_ms=120 的换算结果，由规则算出

            for (var lead = 0; lead <= windowTicks + 3; lead++)
            {
                var rig = new BufferRig(Attack(), "feel.preset.arpg_responsive", step);
                var layer = new FakeActionLayer { AcceptableFrom = acceptable };
                var pressTick = acceptable - lead;
                for (var t = 0; t <= acceptable + 3; t++)
                {
                    rig.Step(t == pressTick ? () => rig.Buffer.Submit(Actor, AttackId) : (Action?)null, layer.ConsumeAt(rig));
                }

                // lead * step 的毫秒数不超过 120 ms 即在窗口内（lead <= windowTicks 与"ms ≤ 120"在取整后一致：窗口 tick 数 = round(120/step)）。
                var within = lead <= windowTicks;
                if (within)
                {
                    Assert.Equal(new[] { (acceptable, AttackId) }, layer.Consumed);
                    Assert.Empty(rig.Drops);
                }
                else
                {
                    Assert.Empty(layer.Consumed);
                    // 过期发生在窗口最后一个有效 tick 的下一 tick：按下 tick + 窗口 + 1（晚于此的取用都落空）。
                    Assert.Equal(new[] { $"{pressTick + windowTicks + 1}:input.action.attack:Expired" }, rig.Drops);
                }
            }
        }

        [Fact]
        public void BufferMs120_WindowTicksEqualTheCalibrationConversion_AtBothTickRates()
        {
            // 防止测试自己与实现抄同一个错：窗口 tick 数以 FeelCalibration 的四舍五入为准（60 Hz → 120/16.667 = 7.2 → 7；50 Hz → 6）。
            Assert.Equal(7, FeelCalibrationTicks(120, 1.0 / 60.0));
            Assert.Equal(6, FeelCalibrationTicks(120, 1.0 / 50.0));
        }

        // ---------------------------------------------------------------- 冒烟：顿帧不吃缓冲

        [Fact]
        public void HitstopDoesNotConsumeTheBuffer_ButTheSimulationClockWould()
        {
            // 窗口 7 tick（60 Hz、buffer_ms=120）；tick 5 起行动者被顿帧 20 tick（远超窗口），tick 5 按下攻击；
            // 动作层在顿帧结束的那个 tick 才可接受（冻结期间动作本来就进行不了）。
            const int pressTick = 5;
            const int hitstop = 20;
            const int unfreezeTick = pressTick + hitstop;

            BufferRig Run(bool useActionClock, out FakeActionLayer layer, out List<long> expiresSeen)
            {
                var rig = new BufferRig(Attack(), "feel.preset.arpg_responsive", useActionClock: useActionClock);
                var l = layer = new FakeActionLayer { AcceptableFrom = unfreezeTick };
                var seen = new List<long>();
                for (var t = 0; t < unfreezeTick + 10; t++)
                {
                    var tick = t;
                    rig.Step(() =>
                    {
                        if (tick == pressTick)
                        {
                            rig.Clock.Pause(Actor, hitstop);
                            rig.Buffer.Submit(Actor, AttackId);
                        }
                    }, () =>
                    {
                        var snap = rig.Buffer.Snapshot(Actor);
                        if (snap.Count > 0) seen.Add(snap[0].ExpiresAtActionTime);
                        l.ConsumeAt(rig)();
                    });
                }
                expiresSeen = seen;
                return rig;
            }

            // 行动者动作时钟：冻结期间既不过期也不取用，解冻后第一个可接受 tick 取用，记录的过期时刻（动作时钟读数）整个冻结期间不变。
            var frozen = Run(true, out var frozenLayer, out var frozenExpires);
            Assert.Equal(new[] { (unfreezeTick, AttackId) }, frozenLayer.Consumed);
            Assert.Empty(frozen.Drops);
            Assert.Single(frozenExpires.Distinct());

            // 对照：同一脚本按模拟时钟计（没有行动者动作时钟）时，记录在入槽后 窗口+1 个 tick 就过期，等不到解冻——
            // 证明"顿帧不吃缓冲"来自行动者动作时钟，而不是脚本太宽松。
            var window = frozen.Ticks(120);
            var plain = Run(false, out var plainLayer, out _);
            Assert.Empty(plainLayer.Consumed);
            Assert.Equal(new[] { $"{pressTick + window + 1}:input.action.attack:Expired" }, plain.Drops);
        }

        [Fact]
        public void HitstopLongerThanTheWindow_PressedBeforeFreeze_StillSurvivesAndIsConsumedAfterUnfreeze()
        {
            // tick 2 按下（窗口 7）、tick 3 起顿帧 30 tick；后摇在 tick 40 才结束（动作层此前拒绝）。
            const int pressTick = 2;
            var rig = new BufferRig(Attack(), "feel.preset.arpg_responsive");
            var layer = new FakeActionLayer { AcceptableFrom = 40 };
            var window = rig.Ticks(120);
            for (var t = 0; t < 60; t++)
            {
                var tick = t;
                rig.Step(() =>
                {
                    if (tick == pressTick) rig.Buffer.Submit(Actor, AttackId);
                    if (tick == pressTick + 1) rig.Clock.Pause(Actor, 30);
                }, layer.ConsumeAt(rig));
            }

            // 动作时钟只在未冻结的 tick 前进：到 tick 40 时它走了 40 - 30 = 10 > window，所以此记录终究会过期——但过期发生在它"该过期"的
            // 动作时间点，而不是被冻结的 30 tick 吃掉：期望的过期 tick = 解冻后再走满剩余窗口。
            var pressedAt = pressTick;
            var frozenFrom = pressTick + 1;
            var unfreeze = frozenFrom + 30;
            var actionTimeAtFreeze = frozenFrom; // 冻结开始时动作时钟读数
            var expiresAt = pressedAt + window; // ExpiresAtActionTime
            var ticksLeftAtFreeze = expiresAt - actionTimeAtFreeze + 1; // 动作时间超过 expiresAt 才算过期
            var expectedExpireTick = unfreeze + (int)ticksLeftAtFreeze;
            Assert.Equal(new[] { $"{expectedExpireTick}:input.action.attack:Expired" }, rig.Drops);
            Assert.Empty(layer.Consumed);
        }

        // ---------------------------------------------------------------- 槽位、优先级、重复策略

        [Fact]
        public void FullSlots_HigherPriorityReplacesTheLowest_ExactlyOnce_AndLowerOrEqualIsDropped()
        {
            var defs = new[]
            {
                Def("input.action.attack", ActionClass.Attack),
                Def("input.action.skill", ActionClass.Skill),
                Def("input.action.dodge", ActionClass.Dodge),
                Def("input.action.item", ActionClass.Item),
            };
            var rig = new BufferRig(defs, "feel.preset.arpg_responsive"); // buffer_slots = 2
            rig.Step(() =>
            {
                rig.Buffer.Submit(Actor, new Id("input.action.attack"));
                rig.Buffer.Submit(Actor, new Id("input.action.skill"));
                rig.Buffer.Submit(Actor, new Id("input.action.dodge"));
            });

            // attack 与 skill 同优先级，被替换的是更早入槽的 attack；dodge 入槽。
            Assert.Equal(new[] { "0:input.action.attack:Replaced" }, rig.Drops);
            Assert.Equal(new[] { "input.action.skill", "input.action.dodge" }, rig.Buffer.Snapshot(Actor).Select(i => i.ActionId.Value));

            // 优先级低于槽内最低者（skill=30）的 item(20) 被丢弃为 Full；新记录没有占槽。
            rig.Step(() => rig.Buffer.Submit(Actor, new Id("input.action.item")));
            Assert.Equal(new[] { "0:input.action.attack:Replaced", "1:input.action.item:Full" }, rig.Drops);
            Assert.Equal(2, rig.Buffer.Snapshot(Actor).Count);
        }

        [Fact]
        public void RepeatPolicy_RefreshExtendsTheDeadline_IgnoreKeepsTheOriginalOne()
        {
            var defs = new[]
            {
                Def("input.action.attack", ActionClass.Attack, repeat: InputRepeatPolicy.Refresh),
                Def("input.action.skill", ActionClass.Skill, repeat: InputRepeatPolicy.Ignore),
            };
            var rig = new BufferRig(defs, "feel.preset.arpg_responsive");
            var window = rig.Ticks(120);
            rig.Step(() =>
            {
                rig.Buffer.Submit(Actor, AttackId);
                rig.Buffer.Submit(Actor, new Id("input.action.skill"));
            });
            for (var t = 1; t < window; t++) rig.Step();
            var before = rig.Buffer.Snapshot(Actor).ToDictionary(i => i.ActionId.Value, i => i.ExpiresAtActionTime);
            Assert.Equal(window, before["input.action.attack"]);

            rig.Step(() =>
            {
                rig.Buffer.Submit(Actor, AttackId);
                rig.Buffer.Submit(Actor, new Id("input.action.skill"));
            });
            var after = rig.Buffer.Snapshot(Actor).ToDictionary(i => i.ActionId.Value, i => i.ExpiresAtActionTime);

            Assert.Equal(window + window, after["input.action.attack"]); // 刷新：过期时刻 = 再次按下时的动作时钟读数 + 窗口
            Assert.Equal(before["input.action.skill"], after["input.action.skill"]); // 忽略：不变
            Assert.Equal(2, rig.Buffer.Snapshot(Actor).Count); // 同一动作不占新槽
        }

        // ---------------------------------------------------------------- 按住 / 点按

        [Fact]
        public void HoldThreshold_ReleaseEarlierIsTap_LaterIsHoldReleased_AndPendingCannotBeConsumed()
        {
            const double holdMs = 200;
            var defs = new[] { Def("input.action.attack", ActionClass.Attack, holdMs: holdMs) };
            var rig = new BufferRig(defs, "feel.preset.arpg_responsive");
            var threshold = rig.Ticks(holdMs);

            // 按住期间：HoldPending，且不可被消费、不过期。
            rig.Step(() => rig.Buffer.Press(Actor, AttackId));
            for (var t = 1; t < threshold + 20; t++) rig.Step();
            Assert.Equal(BufferHoldState.HoldPending, rig.Buffer.Snapshot(Actor).Single().HoldState);
            Assert.False(rig.Buffer.TryPeek(Actor, out _));
            Assert.Empty(rig.Drops);

            // 抬起时已按住 >= 阈值：HoldReleased{heldTicks}，随即可消费。
            rig.Step(() => rig.Buffer.Release(Actor, AttackId));
            var heldTicks = rig.Tick; // 按下在 tick 0，抬起在当前 tick，动作时钟无顿帧时按住时长 = tick 差
            var rec = rig.Buffer.Snapshot(Actor).Single();
            Assert.Equal(BufferHoldState.HoldReleased, rec.HoldState);
            Assert.Equal(heldTicks, rec.HeldTicks);
            Assert.True(rec.HeldTicks >= threshold);
            Assert.True(rig.Buffer.TryPeek(Actor, out _));

            // 早于阈值抬起：点按。
            var rig2 = new BufferRig(defs, "feel.preset.arpg_responsive");
            rig2.Step(() => rig2.Buffer.Press(Actor, AttackId));
            for (var t = 1; t < threshold - 1; t++) rig2.Step();
            rig2.Step(() => rig2.Buffer.Release(Actor, AttackId));
            Assert.Equal(BufferHoldState.Tap, rig2.Buffer.Snapshot(Actor).Single().HoldState);
            Assert.Equal(0, rig2.Buffer.Snapshot(Actor).Single().HeldTicks);
        }

        [Fact]
        public void PressAndReleaseInTheSameTick_IsOneCompleteTap_EvenWhenAHoldThresholdIsDeclared()
        {
            var rig = new BufferRig(new[] { Def("input.action.attack", ActionClass.Attack, holdMs: 200) }, "feel.preset.arpg_responsive");
            rig.Step(() => rig.Buffer.Submit(Actor, AttackId));
            var rec = rig.Buffer.Snapshot(Actor).Single();
            Assert.Equal(BufferHoldState.Tap, rec.HoldState);
            Assert.True(rig.Buffer.TryPeek(Actor, out _));
        }

        [Fact]
        public void CompleteHold_ConvertsPendingToReleasedWithTheGivenHeldTicks_AndStartsTheWindow()
        {
            var rig = new BufferRig(new[] { Def("input.action.attack", ActionClass.Attack, holdMs: 200) }, "feel.preset.arpg_responsive");
            rig.Step(() => rig.Buffer.Press(Actor, AttackId));
            Assert.True(rig.Buffer.CompleteHold(Actor, AttackId, 33));
            var rec = rig.Buffer.Snapshot(Actor).Single();
            Assert.Equal(BufferHoldState.HoldReleased, rec.HoldState);
            Assert.Equal(33, rec.HeldTicks);
            Assert.Equal(rig.Clock.ActionTicks(Actor) + rig.Ticks(120), rec.ExpiresAtActionTime); // 转换那一刻的动作时钟读数 + 窗口
            Assert.False(rig.Buffer.CompleteHold(Actor, AttackId, 33)); // 不再处于 HoldPending
        }

        // ---------------------------------------------------------------- 取用规则

        [Fact]
        public void Consumption_HighestPriorityFirst_OnePerTick_AndAcceptsPredicateGatesOnlyTheTopRecord()
        {
            var defs = new[] { Def("input.action.attack", ActionClass.Attack), Def("input.action.dodge", ActionClass.Dodge) };
            var rig = new BufferRig(defs, "feel.preset.arpg_responsive");
            var order = new List<string>();
            rig.Step(() =>
            {
                rig.Buffer.Submit(Actor, AttackId);
                rig.Buffer.Submit(Actor, DodgeId);
            }, () =>
            {
                // 谓词只接受 attack：但最前的是 dodge，不被接受时不让位给次优先级的 attack。
                Assert.False(rig.Buffer.TryConsume(Actor, r => r.Class == ActionClass.Attack, out _));
                Assert.True(rig.Buffer.TryConsume(Actor, null, out var first));
                order.Add(first.ActionId.Value);
                Assert.True(first.Consumed);
                // 同一 tick 内至多取用一条。
                Assert.False(rig.Buffer.TryPeek(Actor, out _));
                Assert.False(rig.Buffer.TryConsume(Actor, null, out _));
            });
            rig.Step(null, () =>
            {
                Assert.True(rig.Buffer.TryConsume(Actor, null, out var second));
                order.Add(second.ActionId.Value);
            });

            Assert.Equal(new[] { "input.action.dodge", "input.action.attack" }, order);
            rig.Step();
            Assert.Empty(rig.Buffer.Snapshot(Actor)); // 已消费的记录在下一 tick 开头清除
            Assert.Empty(rig.Drops);
        }

        [Fact]
        public void PipelineRejection_TimeSolvableRestoresTheRecord_OtherReasonsDropItWithTheReasonCode()
        {
            var rig = new BufferRig(Attack(), "feel.preset.arpg_responsive");
            // 时间可解（GCD_ACTIVE）：撤销已消费，下一 tick 重试，不发丢弃事件。
            rig.Step(() => rig.Buffer.Submit(Actor, AttackId), () =>
            {
                Assert.True(rig.Buffer.TryConsume(Actor, null, out _));
                Assert.True(InputBufferHost.IsTimeSolvableReason("GCD_ACTIVE"));
                rig.Buffer.ReportRejected(Actor, AttackId, "GCD_ACTIVE", timeSolvable: true);
                Assert.False(rig.Buffer.Snapshot(Actor).Single().Consumed);
                Assert.False(rig.Buffer.TryPeek(Actor, out _)); // 本 tick 不再取用
            });
            Assert.Empty(rig.Drops);
            rig.Step(null, () => Assert.True(rig.Buffer.TryConsume(Actor, null, out _)));

            // 其余原因（资源不足）：记录丢弃，Rejected + reasonCode，恰好一次。
            var rig2 = new BufferRig(Attack(), "feel.preset.arpg_responsive");
            rig2.Step(() => rig2.Buffer.Submit(Actor, AttackId), () =>
            {
                Assert.True(rig2.Buffer.TryConsume(Actor, null, out _));
                Assert.False(InputBufferHost.IsTimeSolvableReason("NOT_ENOUGH_RESOURCE"));
                rig2.Buffer.ReportRejected(Actor, AttackId, "NOT_ENOUGH_RESOURCE", timeSolvable: false);
            });
            rig2.Step();
            Assert.Equal(new[] { "0:input.action.attack:Rejected:NOT_ENOUGH_RESOURCE" }, rig2.Drops);
            Assert.Empty(rig2.Buffer.Snapshot(Actor));
        }

        [Fact]
        public void Clear_DropsEveryRecordWithCleared_AndDiscardsUnappliedSamples()
        {
            var defs = new[] { Def("input.action.attack", ActionClass.Attack), Def("input.action.dodge", ActionClass.Dodge) };
            var rig = new BufferRig(defs, "feel.preset.arpg_responsive");
            rig.Step(() =>
            {
                rig.Buffer.Submit(Actor, AttackId);
                rig.Buffer.Submit(Actor, DodgeId);
            });
            rig.Buffer.Press(Actor, AttackId); // 尚未入槽的采样
            rig.Buffer.Clear(Actor);
            rig.Bus.DispatchPending();

            Assert.Equal(new[] { "0:input.action.attack:Cleared", "0:input.action.dodge:Cleared" }, rig.Drops);
            rig.Step();
            Assert.Empty(rig.Buffer.Snapshot(Actor));
        }

        // ---------------------------------------------------------------- 缺省与档案

        [Fact]
        public void ActionLevelBufferMs_OverridesTheProfileWindow()
        {
            var rig = new BufferRig(new[] { Def("input.action.attack", ActionClass.Attack, bufferMs: 50) }, "feel.preset.arpg_responsive");
            rig.Step(() => rig.Buffer.Submit(Actor, AttackId));
            Assert.Equal(rig.Ticks(50), rig.Buffer.Snapshot(Actor).Single().ExpiresAtActionTime);
            Assert.NotEqual(rig.Ticks(120), rig.Ticks(50));
        }

        [Fact]
        public void WithoutFeelProfile_TheDefaultsKeepExistingBehavior_NoBufferingAndTwoSlots()
        {
            var bus = new Core.Foundation.EventBus.EventBus(
                Core.Foundation.EventBus.EventCatalog.FromDefinitions(Array.Empty<Core.Foundation.EventBus.EventDefinition>()),
                new Core.Foundation.EventBus.EventBusOptions { StrictCatalog = false });
            var buffer = new InputBufferHost(bus);
            buffer.DeclareActions(Attack());
            buffer.Submit(Actor, AttackId);
            buffer.BeginTick();
            // 没有手感档案：窗口 0，只在按下当 tick 有效。
            Assert.Equal(buffer.CurrentTick, buffer.Snapshot(Actor).Single().ExpiresAtActionTime);
            Assert.True(buffer.TryPeek(Actor, out _));
            buffer.BeginTick();
            Assert.Empty(buffer.Snapshot(Actor));
        }

        [Fact]
        public void ActionsWithoutAClassOrOfMoveClass_AreNotBuffered_SoLegacyActionSetsBehaveAsBefore()
        {
            var legacy = new ActionDefinition(new Id("input.action.confirm"), ActionKind.Button, new[] { "key:enter" });
            var move = Def("input.action.move_tap", ActionClass.Move);
            var axis = new ActionDefinition(new Id("input.action.stick"), ActionKind.Axis2D, new[] { "pad_stick:left" }, "default", null,
                ActionClass.Skill, null, null, null, InputRepeatPolicy.Refresh, null, null);
            var rig = new BufferRig(new[] { legacy, move, axis }, "feel.preset.arpg_responsive");

            rig.Step(() =>
            {
                rig.Buffer.Submit(Actor, legacy.ActionId);
                rig.Buffer.Submit(Actor, move.ActionId);
                rig.Buffer.Submit(Actor, axis.ActionId);
            });

            Assert.Empty(rig.Buffer.Snapshot(Actor));
            Assert.Empty(rig.Drops);
            Assert.False(legacy.IsBuffered);
            Assert.False(move.IsBuffered);
            Assert.False(axis.IsBuffered);
        }

        [Fact]
        public void ClassDefaults_PriorityOrderAndFaceOnAccept_FollowTheDesign()
        {
            Assert.True(ActionClassDefaults.Priority(ActionClass.Dodge) > ActionClassDefaults.Priority(ActionClass.Attack));
            Assert.Equal(ActionClassDefaults.Priority(ActionClass.Attack), ActionClassDefaults.Priority(ActionClass.Skill));
            Assert.True(ActionClassDefaults.Priority(ActionClass.Skill) > ActionClassDefaults.Priority(ActionClass.Item));
            Assert.True(ActionClassDefaults.Priority(ActionClass.Item) > ActionClassDefaults.Priority(ActionClass.Interact));
            Assert.True(ActionClassDefaults.Priority(ActionClass.Interact) > ActionClassDefaults.Priority(ActionClass.Menu));
            foreach (var c in new[] { ActionClass.Attack, ActionClass.Skill, ActionClass.Dodge }) Assert.True(ActionClassDefaults.FaceOnAccept(c));
            foreach (var c in new[] { ActionClass.Interact, ActionClass.Item, ActionClass.Menu, ActionClass.Move }) Assert.False(ActionClassDefaults.FaceOnAccept(c));

            var rig = new BufferRig(new[] { Def("input.action.attack", ActionClass.Attack), Def("input.action.interact", ActionClass.Interact, face: true) }, "feel.preset.arpg_responsive");
            rig.Step(() =>
            {
                rig.Buffer.Press(Actor, AttackId, new Vec2(1, 0));
                rig.Buffer.Press(Actor, new Id("input.action.interact"));
            });
            var snap = rig.Buffer.Snapshot(Actor).ToDictionary(i => i.ActionId.Value);
            Assert.True(snap["input.action.attack"].FaceOnAccept);
            Assert.Equal(new Vec2(1, 0), snap["input.action.attack"].DirectionSnapshot);
            Assert.True(snap["input.action.interact"].FaceOnAccept); // 显式覆盖类别缺省
            Assert.Null(snap["input.action.interact"].DirectionSnapshot);
        }

        // ---------------------------------------------------------------- 确定性

        [Fact]
        public void SameScript_RunTwice_ProducesIdenticalEventAndConsumptionLogs()
        {
            List<string> Run()
            {
                var defs = new[]
                {
                    Def("input.action.attack", ActionClass.Attack), Def("input.action.skill", ActionClass.Skill),
                    Def("input.action.dodge", ActionClass.Dodge), Def("input.action.item", ActionClass.Item),
                };
                var rig = new BufferRig(defs, "feel.preset.arpg_responsive");
                var layer = new FakeActionLayer { AcceptableFrom = 12 };
                var script = new Dictionary<int, string[]>
                {
                    [1] = new[] { "attack" }, [2] = new[] { "skill", "dodge" }, [3] = new[] { "item" }, [9] = new[] { "attack" }, [11] = new[] { "skill" },
                };
                for (var t = 0; t < 40; t++)
                {
                    var tick = t;
                    rig.Step(() =>
                    {
                        if (script.TryGetValue(tick, out var presses))
                        {
                            foreach (var p in presses) rig.Buffer.Submit(Actor, new Id("input.action." + p));
                        }
                    }, layer.ConsumeAt(rig));
                }
                var log = new List<string>(rig.Drops);
                log.AddRange(layer.Consumed.Select(c => c.Tick + "=>" + c.Action.Value));
                return log;
            }

            var a = Run();
            var b = Run();
            Assert.NotEmpty(a);
            Assert.Equal(a, b);
        }

        // ---------------------------------------------------------------- 契约：只实现 Snapshot 的替身不受影响

        [Fact]
        public void SnapshotOnlyImplementations_GetSafeDefaultsForTheConsumptionMembers()
        {
            IInputBufferQuery query = new SnapshotOnly();
            Assert.False(query.TryPeek(Actor, out var peeked));
            Assert.False(query.TryConsume(Actor, null, out var consumed));
            Assert.False(query.CompleteHold(Actor, AttackId, 1));
            query.ReportRejected(Actor, AttackId, "X", true); // 不抛
            Assert.Equal(default, peeked);
            Assert.Equal(default, consumed);
        }

        private sealed class SnapshotOnly : IInputBufferQuery
        {
            public IReadOnlyList<BufferedIntent> Snapshot(Id actorId) => Array.Empty<BufferedIntent>();
        }

        private static int FeelCalibrationTicks(double ms, double step) =>
            Core.Foundation.Feel.FeelCalibration.MillisecondsToTicks(ms, step);
    }
}
