using System;
using System.Collections.Generic;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// <see cref="ISimTimers"/> 的默认实现：每条计时器只是一个"剩余时长"数值，
    /// <see cref="Advance"/> 由 <see cref="WorldSim"/> 在每个连续 tick 开头统一调用一次，
    /// 按 dt 对全部存活计时器做一次减法（见 03 第 8 节）。
    /// <para>
    /// 判断记录（到期判定的浮点容差）：<see cref="IsExpired"/> 判断 <c>Remaining &lt;=
    /// ExpiryEpsilon</c> 而不是严格的 <c>&lt;= 0</c>。原因：本实现按"逐 tick 减法"累积
    /// 剩余时长（而不是用"已推进 tick 数 × 步长"重新相乘计算），这样才能正确支持
    /// <see cref="SimStep.Continuous"/> 携带的 dt 在不同 tick 之间不必相同的一般情形；
    /// 代价是对于恰好整除的时长（如 0.1 秒、步长 1/60 秒，6 步理论上恰好归零），
    /// 连续 6 次二进制浮点减法会残留一个远小于任何有意义时间单位的正数噪声
    /// （量级 1e-17，而不是精确的 0）。<see cref="ExpiryEpsilon"/>（1e-9）足够小、不会让
    /// 任何真实的"还有一点点没到期"的计时器被误判为到期，又足够大、能吸收这类浮点噪声。
    /// </para>
    /// </summary>
    public sealed class SimTimers : ISimTimers
    {
        private const double ExpiryEpsilon = 1e-9;

        private readonly Dictionary<TimerHandle, double> _remainingByHandle = new Dictionary<TimerHandle, double>();
        private int _nextHandleValue = 1;

        public TimerHandle Create(double durationUnits)
        {
            if (durationUnits < 0)
            {
                throw new ArgumentException("durationUnits 不能为负数", nameof(durationUnits));
            }

            var handle = new TimerHandle(_nextHandleValue);
            _nextHandleValue++;
            _remainingByHandle[handle] = durationUnits;
            return handle;
        }

        public double Remaining(TimerHandle handle) => GetRemainingOrThrow(handle);

        public bool IsExpired(TimerHandle handle) => GetRemainingOrThrow(handle) <= ExpiryEpsilon;

        public void Cancel(TimerHandle handle)
        {
            _remainingByHandle.Remove(handle);
        }

        public bool IsAlive(TimerHandle handle) => _remainingByHandle.ContainsKey(handle);

        /// <summary>由 <see cref="WorldSim"/> 在每个连续 tick 开头调用，按 dt 推进全部存活
        /// 计时器；离散步不调用本方法（见 03 第 8 节、本模块 README）。</summary>
        internal void Advance(double dt)
        {
            if (_remainingByHandle.Count == 0)
            {
                return;
            }

            // 先取出全部 key 快照再写回：Dictionary 不允许在遍历同时修改值，
            // 这里虽然只修改 value 不新增/删除 key，仍按快照方式遍历以保持写法一致、安全。
            var handles = new List<TimerHandle>(_remainingByHandle.Keys);
            for (var i = 0; i < handles.Count; i++)
            {
                var handle = handles[i];
                _remainingByHandle[handle] = _remainingByHandle[handle] - dt;
            }
        }

        /// <summary>
        /// 把全部存活计时器的剩余时长乘以 <paramref name="factor"/>（见 03 第 3.3 节步骤 2
        /// "进入回合制时，正在生效的光环等以秒计的剩余时长按 seconds_per_turn 折算为剩余回合数；
        /// 反之...按同一系数把剩余回合数折算回秒"）。不是 <see cref="ISimTimers"/> 契约的一部分
        /// （同 <see cref="WorldSim.DiagnosticsWarnings"/> 的判断记录：03/09 没有为"时间单位换算"
        /// 定义独立的接口原语，这是承载该文档要求行为的具体类型上的一个便利方法），只供
        /// <c>core/gameplay/assembly.TimeModelSwitch</c> 在连续/离散模式切换时调用。
        /// </summary>
        public void RescaleAll(double factor)
        {
            if (factor <= 0)
            {
                throw new ArgumentException("factor 必须为正数", nameof(factor));
            }

            if (_remainingByHandle.Count == 0)
            {
                return;
            }

            var handles = new List<TimerHandle>(_remainingByHandle.Keys);
            for (var i = 0; i < handles.Count; i++)
            {
                var handle = handles[i];
                _remainingByHandle[handle] = _remainingByHandle[handle] * factor;
            }
        }

        private double GetRemainingOrThrow(TimerHandle handle)
        {
            if (_remainingByHandle.TryGetValue(handle, out var remaining))
            {
                return remaining;
            }

            throw new ArgumentException($"计时器句柄不存在或已被 Cancel：{handle}", nameof(handle));
        }
    }
}
