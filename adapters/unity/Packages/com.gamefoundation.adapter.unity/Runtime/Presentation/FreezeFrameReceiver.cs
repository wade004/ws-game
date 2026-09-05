#nullable enable
// FreezeFrameReceiver：09_表现层.md 第 6.1 节 Freeze（顿帧）反馈动作的 Unity 侧落地。
//
// 判断记录（顿帧只暂停表现层插值/动画与相机，不停逻辑 tick，见任务书硬性规则 6"表现层铁律"）：
// PresentationAssemblyOptions.OnFreeze 的契约注释只说"顿帧怎么影响 tick 节奏不属于表现层契约
// 范围"，未规定具体怎么暂停；本类型按 09 第 1 节铁律与任务书措辞取"暂停表现层插值/动画与相机，
// 不暂停 WorldSim.Tick"这一种解释——GameFoundationBootstrap.FixedUpdate 驱动的
// world.Tick(...)完全不读取本类型状态，顿帧期间逻辑世界继续正常推进；GameFoundationBootstrap.Update
// 在本类型 IsFrozen 为真时跳过 ViewBinder.SyncAll/CameraHost.Update 两步调用，视觉上表现为"画面
// 卡一下"，与常见动作游戏"打击顿帧"的直觉效果一致，同时保证确定性（逻辑 tick 从不因为表现层的
// 顿帧而改变节奏或产生额外分支）。
using System;

namespace Adapter.Unity.Presentation
{
    public sealed class FreezeFrameReceiver
    {
        private double _remainingSeconds;

        /// <summary>当前是否处于顿帧中（供 <c>GameFoundationBootstrap.Update</c> 决定是否跳过
        /// 表现层插值/相机更新）。</summary>
        public bool IsFrozen => _remainingSeconds > 0.0;

        /// <summary>迄今为止触发过的顿帧次数（PlayMode 测试/诊断用）。</summary>
        public int TriggerCount { get; private set; }

        /// <summary>绑定给 <c>PresentationAssemblyOptions.OnFreeze</c>：请求顿帧
        /// <paramref name="seconds"/> 秒。多次请求取剩余时间的较大者（不叠加，避免连续多次暴击
        /// 顿帧无限延长）。</summary>
        public void Freeze(double seconds)
        {
            if (seconds <= 0.0)
            {
                return;
            }
            _remainingSeconds = Math.Max(_remainingSeconds, seconds);
            TriggerCount++;
        }

        /// <summary>由 <c>GameFoundationBootstrap.Update</c> 每帧调用：推进顿帧剩余时间（用不受
        /// 时间缩放影响的真实秒数，顿帧本身不应该被"游戏内时间缩放"影响——与 <c>UnityClock.Now()</c>
        /// 使用 <c>Time.unscaledDeltaTime</c> 同一惯例）。</summary>
        public void Tick(double unscaledDeltaSeconds)
        {
            if (_remainingSeconds > 0.0)
            {
                _remainingSeconds = Math.Max(0.0, _remainingSeconds - unscaledDeltaSeconds);
            }
        }
    }
}
