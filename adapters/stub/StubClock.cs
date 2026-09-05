// StubClock：IClock 的最小可用桩实现——手动时钟，不读取任何系统时间。
// 用途：测试驱动固定步长主循环、帧回调，而不依赖真实的墙钟或渲染帧率。
// 与真实实现的差异：Now() 从 0 开始，只能通过测试方法 Advance(seconds) 推进；
// 真实引擎实现会由渲染循环驱动帧回调、由系统时钟驱动 Now()，本桩把这两件事都收拢成
// 一次显式的 Advance 调用，便于测试断言"推进 N 秒后固定步回调触发了几次"。
// OnFrame/RequestFixedStep 返回 SubscriptionHandle（见 ADR-0016 决策 1）：Dispose 后该回调
// 不再参与后续 Advance 的触发；退订在遍历期间发生也安全（遍历用快照，退订只置标记位）。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Stub
{
    public sealed class StubClock : IClock
    {
        private sealed class FrameRegistration
        {
            public FrameCallback Callback = null!;
            public bool Disposed;
        }

        private sealed class FixedStepRegistration
        {
            public double StepSeconds;
            public FixedStepCallback Callback = null!;
            public double Accumulator;
            public bool Disposed;
        }

        private double _now;
        private double _lastDelta;
        private readonly List<FrameRegistration> _frameCallbacks = new List<FrameRegistration>();
        private readonly List<FixedStepRegistration> _fixedSteps = new List<FixedStepRegistration>();

        public double Now() => _now;

        public double GetDeltaSeconds() => _lastDelta;

        public SubscriptionHandle OnFrame(FrameCallback callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            var registration = new FrameRegistration { Callback = callback };
            _frameCallbacks.Add(registration);
            return new SubscriptionHandle(() =>
            {
                registration.Disposed = true;
                _frameCallbacks.Remove(registration);
            });
        }

        public SubscriptionHandle RequestFixedStep(double stepSeconds, FixedStepCallback callback)
        {
            if (stepSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(stepSeconds), "步长必须为正数");
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            var registration = new FixedStepRegistration { StepSeconds = stepSeconds, Callback = callback, Accumulator = 0 };
            _fixedSteps.Add(registration);
            return new SubscriptionHandle(() =>
            {
                registration.Disposed = true;
                _fixedSteps.Remove(registration);
            });
        }

        /// <summary>
        /// 测试用：推进模拟时钟 seconds 秒。先按各已登记的固定步长做累积器结算
        /// （可能在一次 Advance 内触发 0 次或多次固定步回调），再触发全部帧回调，最后推进 Now()。
        /// 对已登记回调的一次快照遍历：回调内部退订自身或其它回调不会破坏本次遍历，
        /// 也不会导致刚退订的回调在本次 Advance 内被再次触发。
        /// </summary>
        public void Advance(double seconds)
        {
            if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds), "推进的秒数不能为负");

            _lastDelta = seconds;

            foreach (var registration in _fixedSteps.ToArray())
            {
                if (registration.Disposed) continue;
                registration.Accumulator += seconds;
                while (registration.Accumulator >= registration.StepSeconds)
                {
                    if (registration.Disposed) break;
                    registration.Accumulator -= registration.StepSeconds;
                    registration.Callback(registration.StepSeconds);
                }
            }

            foreach (var registration in _frameCallbacks.ToArray())
            {
                if (registration.Disposed) continue;
                registration.Callback(seconds);
            }

            _now += seconds;
        }

        /// <summary>测试用：已登记（未退订）的固定步回调数量。</summary>
        public int FixedStepRegistrationCount => _fixedSteps.Count;

        /// <summary>测试用：已登记（未退订）的帧回调数量。</summary>
        public int FrameRegistrationCount => _frameCallbacks.Count;

        /// <summary>测试用：查询第 registrationIndex 个（按注册顺序，从 0 开始）固定步回调当前累积器剩余秒数。</summary>
        public double GetFixedStepAccumulator(int registrationIndex) => _fixedSteps[registrationIndex].Accumulator;
    }
}
