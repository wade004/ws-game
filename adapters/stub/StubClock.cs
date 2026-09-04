// StubClock：IClock 的最小可用桩实现——手动时钟，不读取任何系统时间。
// 用途：测试驱动固定步长主循环、帧回调，而不依赖真实的墙钟或渲染帧率。
// 与真实实现的差异：Now() 从 0 开始，只能通过测试方法 Advance(seconds) 推进；
// 真实引擎实现会由渲染循环驱动帧回调、由系统时钟驱动 Now()，本桩把这两件事都收拢成
// 一次显式的 Advance 调用，便于测试断言"推进 N 秒后固定步回调触发了几次"。
using System;
using System.Collections.Generic;
using Core.Foundation.EngineAdapter;

namespace Adapters.Stub
{
    public sealed class StubClock : IClock
    {
        private sealed class FixedStepRegistration
        {
            public double StepSeconds;
            public FixedStepCallback Callback = null!;
            public double Accumulator;
        }

        private double _now;
        private double _lastDelta;
        private readonly List<FrameCallback> _frameCallbacks = new List<FrameCallback>();
        private readonly List<FixedStepRegistration> _fixedSteps = new List<FixedStepRegistration>();

        public double Now() => _now;

        public double GetDeltaSeconds() => _lastDelta;

        public void OnFrame(FrameCallback callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            _frameCallbacks.Add(callback);
        }

        public void RequestFixedStep(double stepSeconds, FixedStepCallback callback)
        {
            if (stepSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(stepSeconds), "步长必须为正数");
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            _fixedSteps.Add(new FixedStepRegistration { StepSeconds = stepSeconds, Callback = callback, Accumulator = 0 });
        }

        /// <summary>
        /// 测试用：推进模拟时钟 seconds 秒。先按各已登记的固定步长做累积器结算
        /// （可能在一次 Advance 内触发 0 次或多次固定步回调），再触发全部帧回调，最后推进 Now()。
        /// </summary>
        public void Advance(double seconds)
        {
            if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds), "推进的秒数不能为负");

            _lastDelta = seconds;

            foreach (var registration in _fixedSteps)
            {
                registration.Accumulator += seconds;
                while (registration.Accumulator >= registration.StepSeconds)
                {
                    registration.Accumulator -= registration.StepSeconds;
                    registration.Callback(registration.StepSeconds);
                }
            }

            foreach (var callback in _frameCallbacks)
            {
                callback(seconds);
            }

            _now += seconds;
        }

        /// <summary>测试用：已登记的固定步回调数量。</summary>
        public int FixedStepRegistrationCount => _fixedSteps.Count;

        /// <summary>测试用：查询第 registrationIndex 个（按注册顺序，从 0 开始）固定步回调当前累积器剩余秒数。</summary>
        public double GetFixedStepAccumulator(int registrationIndex) => _fixedSteps[registrationIndex].Accumulator;
    }
}
