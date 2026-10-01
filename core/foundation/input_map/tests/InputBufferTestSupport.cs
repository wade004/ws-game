using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Foundation.SimLoop;
using Tests.Foundation.Feel;

namespace Tests.Foundation.InputMap
{
    /// <summary>测试用行动者动作时钟：按 <see cref="IActorActionClockControl"/> 契约语义（嵌套取大、到期解除）写的最小实现，<see cref="Tick"/> 推进一个模拟 tick。</summary>
    internal sealed class TestActionClock : IActorActionClockControl
    {
        private readonly List<Id> _actors = new List<Id>();
        private readonly Dictionary<Id, int> _remaining = new Dictionary<Id, int>();
        private readonly Dictionary<Id, long> _ticks = new Dictionary<Id, long>();

        public TestActionClock(params Id[] actors) => _actors.AddRange(actors);

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

        /// <summary>推进一个模拟 tick：暂停中的行动者剩余减一且不前进；其余前进。</summary>
        public void Tick()
        {
            foreach (var actor in _actors)
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

    /// <summary>
    /// 输入缓冲测试台：事件总线 + 缓冲 + 记录下来的丢弃事件。<see cref="Step"/> 按一个 tick 的真实顺序驱动：
    /// 采样（本 tick 到达的按下/抬起）→ <see cref="InputBufferHost.BeginTick"/> → 调用方的取用逻辑 → 事件派发 → 动作时钟前进。
    /// </summary>
    internal sealed class BufferRig
    {
        public static readonly Id Actor = new Id("unit.hero");

        public readonly IEventBus Bus;
        public readonly InputBufferHost Buffer;
        public readonly TestActionClock Clock;
        public readonly List<string> Drops = new List<string>();
        public readonly double StepSeconds;
        public int Tick = -1;

        public BufferRig(IEnumerable<ActionDefinition> actions, string presetId, double stepSeconds = 1.0 / 60.0, bool useActionClock = true)
        {
            StepSeconds = stepSeconds;
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(InputMapEventKeys.BufferDropped, "input", new[] { "actorId", "actionId", "reason", "reasonCode" }),
            });
            Bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
            Bus.Subscribe<InputBufferDroppedEvent>(InputMapEventKeys.BufferDropped, e =>
                Drops.Add(Tick + ":" + e.ActionId.Value + ":" + e.Reason + (e.ReasonCode == null ? "" : ":" + e.ReasonCode)));

            Clock = new TestActionClock(Actor);
            var profiles = FeelTestSupport.FrameworkProfiles();
            var resolver = new FeelResolver(profiles, FeelTestSupport.CalA(presetId), stepSeconds);
            Buffer = new InputBufferHost(Bus, new InputBufferOptions
            {
                StepSeconds = stepSeconds,
                Feel = resolver,
                ActionClock = useActionClock ? Clock : null,
            });
            Buffer.DeclareActions(actions);
        }

        /// <summary>推进一个 tick：<paramref name="arrivals"/> 是本 tick 到达的采样（在 BeginTick 之前提交），<paramref name="consume"/> 是步骤 1 里动作层的取用逻辑。</summary>
        public void Step(Action? arrivals = null, Action? consume = null)
        {
            Tick++;
            arrivals?.Invoke();
            Buffer.BeginTick();
            consume?.Invoke();
            Bus.DispatchPending();
            Clock.Tick();
        }

        public int Ticks(double ms) => FeelCalibration.MillisecondsToTicks(ms, StepSeconds);

        public static ActionDefinition Def(
            string id, ActionClass cls, double? bufferMs = null, int? priority = null, double? holdMs = null,
            InputRepeatPolicy repeat = InputRepeatPolicy.Refresh, bool? face = null) =>
            new ActionDefinition(new Id(id), ActionKind.Button, new[] { "key:" + id.Substring(id.LastIndexOf('.') + 1) },
                "default", null, cls, bufferMs, priority, holdMs, repeat, face, null);
    }
}
