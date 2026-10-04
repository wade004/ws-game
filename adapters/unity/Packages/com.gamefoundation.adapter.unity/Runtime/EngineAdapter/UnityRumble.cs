#nullable enable
// UnityRumble：IRumble（手柄震动）的 Unity 实现（ADR-0148，手感设计/07 第 1 节 rumble）。
//
// 判断记录：
//   - 输出：经 Input System 的 Gamepad.current.SetMotorSpeeds(低频, 高频) 设置马达转速，到期（Tick 推进）归零；
//     强度 0..1 同时给两个马达（低频马达取全强度、高频马达取 0.6 倍：钝重冲击的体感以低频为主）。
//   - 能力声明：SupportsRumble 在"当前有手柄"时为真（Gamepad.current != null）；没有手柄时 Rumble 静默忽略，不报错。
//     测试与不走 Input System 的宿主可以注入马达输出（motorSink）替代真实手柄，此时 SupportsRumble 恒为真。
//   - 叠加：后到的震动与进行中的震动取强度较大者、时长取剩余较长者（不累加，避免连击把马达顶满）。
//   - 玩家强度 feel.intensity.rumble 与幅度夹取由表现层装配出口统一处理（PresentationAssembly），本类型只管落到设备。
//   - 退出：Stop 把马达归零（宿主退出/暂停时调用，避免手柄在应用外继续震）。
using System;
using Core.Foundation.EngineAdapter;
using UnityEngine.InputSystem;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityRumble : IRumble
    {
        /// <summary>高频马达相对低频马达的强度比。</summary>
        public const double HighFrequencyRatio = 0.6;

        private readonly Action<double, double>? _motorSink;
        private double _strength;
        private double _remainingSeconds;

        /// <param name="motorSink">可选：马达输出 <c>(低频, 高频)</c>，缺省走 <see cref="Gamepad.current"/>。</param>
        public UnityRumble(Action<double, double>? motorSink = null)
        {
            _motorSink = motorSink;
        }

        public bool SupportsRumble => _motorSink != null || Gamepad.current != null;

        /// <summary>当前生效的震动强度（0 = 没有；测试/诊断用）。</summary>
        public double CurrentStrength => _remainingSeconds > 0 ? _strength : 0.0;

        /// <summary>当前震动剩余秒数（测试/诊断用）。</summary>
        public double RemainingSeconds => _remainingSeconds > 0 ? _remainingSeconds : 0.0;

        /// <summary>迄今收到的有效震动次数（测试/诊断用）。</summary>
        public int RumbleCount { get; private set; }

        public void Rumble(double strength, double durationMs)
        {
            if (!(strength > 0) || !(durationMs > 0) || !SupportsRumble)
            {
                return;
            }

            if (strength > 1.0)
            {
                strength = 1.0;
            }

            RumbleCount++;
            var seconds = durationMs / 1000.0;
            if (_remainingSeconds > 0)
            {
                _strength = Math.Max(_strength, strength);
                _remainingSeconds = Math.Max(_remainingSeconds, seconds);
            }
            else
            {
                _strength = strength;
                _remainingSeconds = seconds;
            }

            Output(_strength);
        }

        /// <summary>由 UnityEngineHost.Update 每帧调用：推进剩余时间，到期归零。</summary>
        internal void Tick(double deltaSeconds)
        {
            if (!(_remainingSeconds > 0))
            {
                return;
            }

            _remainingSeconds -= deltaSeconds;
            if (_remainingSeconds <= 0)
            {
                _remainingSeconds = 0;
                _strength = 0;
                Output(0);
            }
        }

        /// <summary>立即停止震动并把马达归零。</summary>
        public void Stop()
        {
            _remainingSeconds = 0;
            _strength = 0;
            Output(0);
        }

        private void Output(double strength)
        {
            var low = strength;
            var high = strength * HighFrequencyRatio;
            if (_motorSink != null)
            {
                _motorSink(low, high);
                return;
            }

            Gamepad.current?.SetMotorSpeeds((float)low, (float)high);
        }
    }
}
