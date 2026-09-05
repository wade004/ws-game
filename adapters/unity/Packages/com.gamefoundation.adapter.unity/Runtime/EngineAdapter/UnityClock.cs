#nullable enable
// UnityClock：IClock 的 Unity 引擎实现。
//
// 确定性判断记录（对应任务硬性规则 8）：Now() 返回的真实时间只允许流向表现/UI 动画一类场景，
// 不得被逻辑层用作模拟输入；逻辑层的固定步长节拍完全由 RequestFixedStep 注册的回调驱动，
// 且该节拍的"驱动源"是 UnityEngineHost.FixedUpdate 传入的 Time.fixedDeltaTime——
// Unity 自身的固定步长本就是一个恒定配置值（非系统挂钟采样），与桩实现里"测试代码显式
// Advance(seconds)"在语义上等价（都是"由确定的节拍源驱动"，不是"读一次挂钟决定要不要往前走"）。
// 因此本实现不违反"逻辑层 tick 由固定步长驱动"的铁律。
//
// 帧回调（OnFrame）与固定步回调（RequestFixedStep）分别由宿主的 Update/FixedUpdate 驱动，
// 对应各自不同的时间源：
//   - TickFrame 由 Update 调用，携带 Time.unscaledDeltaTime（不受 Time.timeScale 影响，
//     符合契约"onFrame 供表现层与主循环的插值/累积逻辑使用"的语义——表现层不应该被暂停/慢动作
//     打断）。
//   - TickFixedStep 由 FixedUpdate 调用，携带 Time.fixedDeltaTime，按各注册的 stepSeconds
//     做独立累积器结算（同一次 FixedUpdate 内可能触发 0 次或多次某个注册的回调），
//     算法与 adapters/stub/StubClock.Advance 的固定步累积逻辑一致。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityClock : IClock
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

        private double _lastFrameDelta;
        private readonly List<FrameRegistration> _frameCallbacks = new List<FrameRegistration>();
        private readonly List<FixedStepRegistration> _fixedSteps = new List<FixedStepRegistration>();

        /// <summary>
        /// 单调递增的真实时间（秒），不受模拟暂停/慢动作影响。直接读取
        /// <see cref="Time.realtimeSinceStartupAsDouble"/>（进程启动以来的挂钟秒数），
        /// 只允许供表现/UI 动画使用，逻辑层不得依赖本方法的返回值做模拟推进
        /// （见类型顶部"确定性判断记录"）。
        /// </summary>
        public double Now() => Time.realtimeSinceStartupAsDouble;

        public double GetDeltaSeconds() => _lastFrameDelta;

        /// <summary>返回 <see cref="SubscriptionHandle"/>（ADR-0016 决策 1）：主循环在场景/世界
        /// 重建前必须退订旧世界注册的全部固定步回调，避免旧回调残留导致重复推进。</summary>
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

        /// <summary>由 <see cref="UnityEngineHost"/>.Update 调用：触发全部帧回调、更新
        /// GetDeltaSeconds() 的返回值。用快照遍历，回调内部退订不会破坏本次遍历、也不会导致
        /// 刚退订的回调在本次调用内被再次触发（同 <c>StubClock.Advance</c> 判断记录）。</summary>
        internal void TickFrame(double deltaSeconds)
        {
            _lastFrameDelta = deltaSeconds;
            var snapshot = _frameCallbacks.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i].Disposed) continue;
                snapshot[i].Callback(deltaSeconds);
            }
        }

        /// <summary>由 <see cref="UnityEngineHost"/>.FixedUpdate 调用：按各注册的固定步长
        /// 独立累积结算，与 <c>StubClock.Advance</c> 的固定步循环算法一致。</summary>
        internal void TickFixedStep(double deltaSeconds)
        {
            var snapshot = _fixedSteps.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                var registration = snapshot[i];
                if (registration.Disposed) continue;
                registration.Accumulator += deltaSeconds;
                while (registration.Accumulator >= registration.StepSeconds)
                {
                    if (registration.Disposed) break;
                    registration.Accumulator -= registration.StepSeconds;
                    registration.Callback(registration.StepSeconds);
                }
            }
        }

        /// <summary>测试/诊断用：已登记（未退订）的固定步回调数量。</summary>
        public int FixedStepRegistrationCount => _fixedSteps.Count;

        /// <summary>测试/诊断用：已登记（未退订）的帧回调数量。</summary>
        public int FrameRegistrationCount => _frameCallbacks.Count;
    }
}
