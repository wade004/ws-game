using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// 行动者动作时钟的默认实现（手感设计/00 第 5 节、03 第 3 节、ADR-0117）：每个行动者一个，随模拟 tick 前进，
    /// 被局部顿帧暂停；模拟时钟、冷却、光环、其它行动者照常。
    /// <para>
    /// <b>时间模型</b>：时钟按"已完成的 tick 数"计时，只认两个边界信号——tick 开始（<see cref="BeginTick"/>）与 tick 结束
    /// （<see cref="EndTick"/>），由 <see cref="Attach"/> 订阅 <c>sim.tick_started</c>/<c>sim.tick_finished</c> 驱动，
    /// 因此<b>与阶段处理器的注册顺序无关</b>；没有世界宿主的测试可手动调用 <see cref="Advance"/>。
    /// </para>
    /// <para>
    /// <b>暂停窗口</b>：每个行动者至多一个冻结窗口 <c>[From, Until)</c>（tick 序号）。<see cref="Pause"/> 在 tick 内被调用
    /// （步骤 5 结算、步骤 7 派发都属于 tick 内）时窗口从<b>下一个 tick</b>开始（本 tick 里该行动者的时间线、位移已经跑完），
    /// 在 tick 之间被调用时从下一个将要执行的 tick 开始；所以 <c>Pause(a, n)</c> 恰好冻结之后 n 个 tick，
    /// 与调用点在 tick 内的哪个步骤无关。<see cref="IsPaused"/>/<see cref="RemainingPausedTicks"/> 的口径：窗口未结束
    /// （<c>Until &gt; 已完成 tick 数</c>）即为暂停中，剩余数含当前正在处理的那个冻结 tick，两者恒一致（暂停中恒 &gt; 0，否则为 0）。
    /// </para>
    /// <para>
    /// <b>嵌套取大</b>：窗口未结束时再次暂停，<c>Until</c> 取两者较大者（等价于"剩余时长与新时长取大"），不相加；
    /// 窗口结束后的暂停是全新窗口。到期自动解除（<see cref="EndTick"/> 摘掉已结束的窗口，句柄计数归零）。
    /// </para>
    /// <para>
    /// <b><see cref="ActionTicks"/></b>：动作时钟累计 tick 数 = 已完成 tick 数 − 该行动者已被冻结完成的 tick 数；
    /// 同一 tick 内读到的值恒等于该 tick 开始时的值（不随处理器顺序漂移）。它相对时钟创建时刻计数，消费者应取差值
    /// （动作开始时记一次、之后相减），不要把它当作行动者出生以来的绝对 tick 数。
    /// </para>
    /// </summary>
    public sealed class ActorActionClock : IActorActionClockControl
    {
        private struct Window
        {
            public long From;
            public long Until;
        }

        private readonly Dictionary<Id, Window> _windows = new Dictionary<Id, Window>();
        private readonly Dictionary<Id, long> _frozenFinished = new Dictionary<Id, long>();

        // 已完成的 tick 数；tick 进行中等于该 tick 的序号，tick 之间等于下一个将要执行的 tick 序号。
        private long _now;
        private bool _inTick;

        /// <summary>已完成的 tick 数（tick 进行中即当前 tick 序号）。</summary>
        public long Now => _now;

        /// <summary>是否正处于一个 tick 之内（<c>sim.tick_started</c> 之后、<c>sim.tick_finished</c> 之前）。</summary>
        public bool InTick => _inTick;

        /// <summary>订阅 <c>sim.tick_started</c>/<c>sim.tick_finished</c>，使时钟随世界 tick 前进；返回的句柄可释放订阅。</summary>
        public IDisposable Attach(IEventBus bus)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            var started = bus.Subscribe<SimTickStartedEvent>(SimEventKeys.TickStarted, e => BeginTick(e.TickIndex));
            var finished = bus.Subscribe<SimTickFinishedEvent>(SimEventKeys.TickFinished, _ => EndTick());
            return new Subscriptions(started, finished);
        }

        /// <summary>
        /// tick 开始：同步序号。序号回退（世界被重建、读档等）按"场景卸载"处理，无条件清空全部暂停窗口。
        /// </summary>
        public void BeginTick(long tickIndex)
        {
            if (tickIndex < _now)
            {
                _windows.Clear();
                _frozenFinished.Clear();
            }

            _now = tickIndex;
            _inTick = true;
        }

        /// <summary>tick 结束：累计本 tick 的冻结、摘掉到期窗口、序号 +1。</summary>
        public void EndTick()
        {
            if (_windows.Count > 0)
            {
                List<Id>? expired = null;
                foreach (var pair in _windows)
                {
                    var w = pair.Value;
                    if (w.From <= _now && _now < w.Until)
                    {
                        _frozenFinished.TryGetValue(pair.Key, out var n);
                        _frozenFinished[pair.Key] = n + 1;
                    }

                    if (w.Until <= _now + 1)
                    {
                        (expired ??= new List<Id>()).Add(pair.Key);
                    }
                }

                if (expired != null)
                {
                    for (var i = 0; i < expired.Count; i++) _windows.Remove(expired[i]);
                }
            }

            _now++;
            _inTick = false;
        }

        /// <summary>无世界宿主的手动推进一个 tick（<see cref="BeginTick"/> + <see cref="EndTick"/>）。</summary>
        public void Advance()
        {
            BeginTick(_now);
            EndTick();
        }

        public bool IsPaused(Id actorId) => _windows.TryGetValue(actorId, out var w) && w.Until > _now;

        public int RemainingPausedTicks(Id actorId)
        {
            if (!_windows.TryGetValue(actorId, out var w) || w.Until <= _now) return 0;
            var start = w.From > _now ? w.From : _now;
            var remaining = w.Until - start;
            return remaining > int.MaxValue ? int.MaxValue : (int)remaining;
        }

        public long ActionTicks(Id actorId)
        {
            _frozenFinished.TryGetValue(actorId, out var frozen);
            return _now - frozen;
        }

        public int PauseHandleCount(Id actorId) => IsPaused(actorId) ? 1 : 0;

        public int TotalPauseHandleCount
        {
            get
            {
                var n = 0;
                foreach (var pair in _windows)
                {
                    if (pair.Value.Until > _now) n++;
                }

                return n;
            }
        }

        public void Pause(Id actorId, int ticks)
        {
            if (ticks <= 0) throw new ArgumentOutOfRangeException(nameof(ticks), "顿帧 tick 数必须为正");
            var from = _inTick ? _now + 1 : _now;
            var until = from + ticks;
            if (_windows.TryGetValue(actorId, out var existing) && existing.Until > _now)
            {
                if (existing.From < from) from = existing.From;
                if (existing.Until > until) until = existing.Until;
            }

            _windows[actorId] = new Window { From = from, Until = until };
        }

        public void ReleaseAll(Id actorId) => _windows.Remove(actorId);

        public void ReleaseAll() => _windows.Clear();

        private sealed class Subscriptions : IDisposable
        {
            private readonly SubscriptionHandle _a;
            private readonly SubscriptionHandle _b;

            public Subscriptions(SubscriptionHandle a, SubscriptionHandle b)
            {
                _a = a;
                _b = b;
            }

            public void Dispose()
            {
                _a.Dispose();
                _b.Dispose();
            }
        }
    }
}
