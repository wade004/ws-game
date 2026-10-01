using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    /// <summary>
    /// 行动者动作时钟契约（S0 只定义契约）：用一个按契约语义写的假实现，验证接口足以表达
    /// "嵌套取大、到期解除、无条件释放、句柄计数为零"（手感设计/03 第 3 节），后续真实实现必须通过同一组断言。
    /// </summary>
    public class ActorActionClockContractTests
    {
        private static readonly Id A = new Id("unit.a");
        private static readonly Id B = new Id("unit.b");

        [Fact]
        public void NestedPause_TakesTheLargerRemaining_NotTheSum()
        {
            var clock = new FakeClock();
            clock.Pause(A, 5);
            clock.Pause(A, 3);
            Assert.Equal(5, clock.RemainingPausedTicks(A));
            clock.Pause(A, 9);
            Assert.Equal(9, clock.RemainingPausedTicks(A));
            Assert.True(clock.IsPaused(A));
            Assert.False(clock.IsPaused(B));
        }

        [Fact]
        public void ActionTicks_DoNotAdvanceWhilePaused_AndExpireAutomatically()
        {
            var clock = new FakeClock();
            clock.Pause(A, 2);
            clock.Tick();
            clock.Tick();
            Assert.Equal(0, clock.ActionTicks(A));
            Assert.False(clock.IsPaused(A));
            clock.Tick();
            Assert.Equal(1, clock.ActionTicks(A));
            Assert.Equal(3, clock.ActionTicks(B));
        }

        [Fact]
        public void ReleaseAll_ClearsHandles_PerActorAndGlobally()
        {
            var clock = new FakeClock();
            clock.Pause(A, 10);
            clock.Pause(B, 10);
            Assert.Equal(2, clock.TotalPauseHandleCount);
            clock.ReleaseAll(A);
            Assert.False(clock.IsPaused(A));
            Assert.Equal(0, clock.PauseHandleCount(A));
            Assert.Equal(1, clock.TotalPauseHandleCount);
            clock.ReleaseAll();
            Assert.Equal(0, clock.TotalPauseHandleCount);
            Assert.False(clock.IsPaused(B));
        }

        [Fact]
        public void Pause_RejectsNonPositiveTicks()
        {
            var clock = new FakeClock();
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Pause(A, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Pause(A, -1));
        }

        [Fact]
        public void ControlInterface_ExtendsTheQueryInterface_SoReadOnlyConsumersNeedNoWriteAccess()
        {
            Assert.True(typeof(IActorActionClockQuery).IsAssignableFrom(typeof(IActorActionClockControl)));
            Assert.Empty(typeof(IActorActionClockQuery).GetMethods().Where(m => m.Name == "Pause" || m.Name == "ReleaseAll"));
        }

        private sealed class FakeClock : IActorActionClockControl
        {
            private readonly Dictionary<Id, int> _remaining = new Dictionary<Id, int>();
            private readonly Dictionary<Id, long> _ticks = new Dictionary<Id, long>();

            public int TotalPauseHandleCount => _remaining.Count;

            public bool IsPaused(Id actorId) => _remaining.ContainsKey(actorId);

            public int RemainingPausedTicks(Id actorId) => _remaining.TryGetValue(actorId, out var n) ? n : 0;

            public long ActionTicks(Id actorId) => _ticks.TryGetValue(actorId, out var n) ? n : 0;

            public int PauseHandleCount(Id actorId) => _remaining.ContainsKey(actorId) ? 1 : 0;

            public void Pause(Id actorId, int ticks)
            {
                if (ticks <= 0) throw new ArgumentOutOfRangeException(nameof(ticks));
                _remaining[actorId] = Math.Max(RemainingPausedTicks(actorId), ticks);
            }

            public void ReleaseAll(Id actorId) => _remaining.Remove(actorId);

            public void ReleaseAll() => _remaining.Clear();

            /// <summary>推进一个模拟 tick：暂停中的行动者剩余减一并在到期当 tick 仍不前进；其余前进。</summary>
            public void Tick()
            {
                foreach (var actor in new[] { A, B })
                {
                    if (_remaining.TryGetValue(actor, out var left))
                    {
                        if (left <= 1) _remaining.Remove(actor); else _remaining[actor] = left - 1;
                    }
                    else
                    {
                        _ticks[actor] = ActionTicks(actor) + 1;
                    }
                }
            }
        }
    }
}
