using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Foundation.SimLoop;
using Tests.Foundation.Feel;
using Tests.Foundation.SimLoop;
using Xunit;
using static Tests.Foundation.InputMap.BufferRig;

namespace Tests.Foundation.InputMap
{
    /// <summary>
    /// 永远接不了的缓冲记录不挡次优先级候选（手感落地 M4 清扫，取代"未映射技能的动作会在过期前挡住优先级更低的候选"这条已知限制）。
    /// 期望值由规则算出：候选排序 = 优先级降序、提交 tick 升序；<see cref="IBufferedIntentSink.CanHandle"/> 为假的记录不参与排序，
    /// 其余记录的"此刻能否接受"仍由 <see cref="IBufferedIntentSink.TryAccept"/> 决定。
    /// </summary>
    public class InputBufferUnhandledSkipTests
    {
        private const double Step = 1.0 / 60.0;
        private static readonly Id DodgeId = new Id("input.action.dodge");
        private static readonly Id AttackId = new Id("input.action.attack");

        /// <summary>只认攻击、闪避被声明为永远接不了（等价于"没有 skill_slot 映射"）的动作层；<see cref="DodgeIsUnhandled"/> 为假时闪避只是"此刻不能接受"。</summary>
        private sealed class SelectiveSink : IBufferedIntentSink
        {
            public bool DodgeIsUnhandled;
            public readonly List<string> Asked = new List<string>();

            public bool CanHandle(Id actorId, BufferedIntent record) => !(DodgeIsUnhandled && record.ActionId.Equals(DodgeId));

            public bool TryAccept(Id actorId, BufferedIntent record, out Intent intent)
            {
                Asked.Add(record.ActionId.Value);
                if (record.ActionId.Equals(DodgeId))
                {
                    intent = default;
                    return false; // 闪避此刻不能接受（或永远接不了，见 CanHandle）
                }

                intent = new Intent(actorId, "cast", new JsonObjectBuilder().Add("skill_id", new JsonString("skill.basic_attack")).Build());
                return true;
            }
        }

        private static (WorldSim World, InputBufferHost Buffer, IEventBus Bus, List<string> Drops, List<string> Casts) Rig(SelectiveSink sink)
        {
            var bus = SimLoopTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var resolver = new FeelResolver(FeelTestSupport.FrameworkProfiles(), FeelTestSupport.CalA("feel.preset.arpg_responsive"), Step);
            var buffer = new InputBufferHost(bus, new InputBufferOptions { StepSeconds = Step, Feel = resolver });
            buffer.DeclareActions(new[] { Def("input.action.dodge", ActionClass.Dodge), Def("input.action.attack", ActionClass.Attack) });
            InputBufferTickHandler.Register(world, buffer, sink);

            var casts = new List<string>();
            world.RegisterPhaseHandler(TickPhase.SkillPipeline, new DelegatePhaseHandler((s, w) =>
            {
                foreach (var intent in w.CurrentIntents)
                {
                    if (intent.Kind == "cast") casts.Add(buffer.CurrentTick + ":" + intent.ActorId.Value);
                }
            }));
            var drops = new List<string>();
            bus.Subscribe<InputBufferDroppedEvent>(InputMapEventKeys.BufferDropped, e => drops.Add(e.ActionId.Value + ":" + e.Reason));
            return (world, buffer, bus, drops, casts);
        }

        [Fact]
        public void TemporarilyUnacceptableTopRecord_StillBlocksTheLowerPriorityCandidate_UntilItExpires()
        {
            // 对照（既有的优先级语义不变）：闪避优先级 40 高于攻击 30，闪避此刻接不了 → 攻击不让位，直到闪避过期。
            var sink = new SelectiveSink { DodgeIsUnhandled = false };
            var (world, buffer, bus, drops, casts) = Rig(sink);

            buffer.Submit(BufferRig.Actor, DodgeId);
            buffer.Submit(BufferRig.Actor, AttackId);
            for (var t = 0; t < 4; t++) world.Tick(SimStep.Continuous(Step));
            bus.DispatchPending();

            Assert.Empty(casts); // 攻击被挡住
            Assert.NotEmpty(sink.Asked);
            Assert.All(sink.Asked, a => Assert.Equal(DodgeId.Value, a)); // 每个 tick 只问最前的那一条
        }

        [Fact]
        public void NeverHandledTopRecord_DoesNotBlockTheLowerPriorityCandidate_AndStaysBufferedUntilItExpires()
        {
            var sink = new SelectiveSink { DodgeIsUnhandled = true };
            var (world, buffer, bus, drops, casts) = Rig(sink);

            buffer.Submit(BufferRig.Actor, DodgeId);
            buffer.Submit(BufferRig.Actor, AttackId);
            world.Tick(SimStep.Continuous(Step));
            bus.DispatchPending();

            // 第一个 tick 就接受了攻击（量：0 → 1 条 cast 意图，tick 序号 = 提交后的第一个 tick）；闪避从未被问过。
            Assert.Single(casts);
            Assert.Equal(new[] { AttackId.Value }, sink.Asked);

            // 闪避记录没有被消费、也没有被丢弃：留在缓冲里等自己的窗口到期（供游戏自己的消费者取用）。
            var snapshot = buffer.Snapshot(BufferRig.Actor);
            var dodge = Assert.Single(snapshot, r => r.ActionId.Equals(DodgeId));
            Assert.False(dodge.Consumed);
            Assert.Empty(drops);

            for (var t = 0; t < 120 && !drops.Contains(DodgeId.Value + ":Expired"); t++)
            {
                world.Tick(SimStep.Continuous(Step));
                bus.DispatchPending();
            }

            Assert.Contains(DodgeId.Value + ":Expired", drops);
            Assert.Single(casts); // 之后也没有再出现意图
        }

        [Fact]
        public void NeverHandledRecord_StillReachesAnotherConsumerThroughTheDirectQueryPath()
        {
            // 不变量：跳过只作用于"向出口询问"这一步；记录仍然在缓冲里，别的消费者（带谓词的 TryConsume）照样取得到它。
            var sink = new SelectiveSink { DodgeIsUnhandled = true };
            var (world, buffer, _, _, _) = Rig(sink);
            buffer.Submit(BufferRig.Actor, DodgeId);
            world.Tick(SimStep.Continuous(Step));

            Assert.True(buffer.TryPeek(BufferRig.Actor, out var top));
            Assert.Equal(DodgeId, top.ActionId); // 无 skip 的查询仍以优先级排序，最前是闪避
            Assert.False(buffer.TryPeek(BufferRig.Actor, r => r.ActionId.Equals(DodgeId), out _)); // 带 skip 的查询看不到它
        }

        [Fact]
        public void SinkWithoutCanHandle_BehavesExactlyAsBefore_DefaultInterfaceMemberIsTrue()
        {
            IBufferedIntentSink legacy = new LegacySink();
            Assert.True(legacy.CanHandle(BufferRig.Actor, default));
        }

        private sealed class LegacySink : IBufferedIntentSink
        {
            public bool TryAccept(Id actorId, BufferedIntent record, out Intent intent)
            {
                intent = default;
                return false;
            }
        }
    }
}
