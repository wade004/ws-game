#nullable enable
// LabEffectFilter：试玩时"单项关闭震屏/闪白/音效/镜头冲击"的开关与计数（手感设计/06 第 4 节"场景控制"）。
//
// 判断记录（只管呈现，不碰逻辑）：开关只拦截舞台把打击反馈指令落到引擎的那一步（EngineLabStage 的反馈转发闸），
// 反馈流水线照常出批、逻辑与指纹不变；因此被拦的指令仍然计数（Suppressed），面板可以如实显示"流水线发了 N 次、其中 M 次被你关掉"。
// 顿帧不在这里：局部顿帧是判定型手感（ADR-0117，改的是逻辑时钟），关顿帧走一条录进脚本的覆盖事件（见 LabPlayground），不是呈现闸。
using System;
using System.Collections.Generic;

namespace Adapter.Unity.LabHost
{
    public sealed class LabEffectFilter
    {
        public const string Shake = "shake";
        public const string Flash = "flash";
        public const string Sfx = "sfx";
        public const string CameraImpulse = "camera_impulse";

        /// <summary>可开关的呈现通道（顺序即面板顺序）。</summary>
        public static readonly string[] Channels = { Shake, Flash, Sfx, CameraImpulse };

        private readonly Dictionary<string, bool> _on = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _submitted = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _suppressed = new Dictionary<string, int>(StringComparer.Ordinal);

        public LabEffectFilter()
        {
            foreach (var channel in Channels)
            {
                _on[channel] = true;
                _submitted[channel] = 0;
                _suppressed[channel] = 0;
            }
        }

        /// <summary>流水线每提交一条反馈指令（任何通道，含不可开关的顿帧冻结与特效）时触发；参数为通道名与舞台上当前执行的固定步序号。</summary>
        public event Action<string, int>? Submitted;

        public bool IsOn(string channel) => _on.TryGetValue(channel, out var on) && on;

        public void Set(string channel, bool on)
        {
            if (!_on.ContainsKey(channel))
            {
                throw new ArgumentException("未知的呈现通道：" + channel, nameof(channel));
            }

            _on[channel] = on;
        }

        public int SubmittedCount(string channel) => _submitted.TryGetValue(channel, out var n) ? n : 0;

        public int SuppressedCount(string channel) => _suppressed.TryGetValue(channel, out var n) ? n : 0;

        /// <summary>闸调用：记一次提交；返回该通道当前是否放行（不可开关的通道恒放行）。</summary>
        internal bool Admit(string channel, int tick)
        {
            _submitted.TryGetValue(channel, out var n);
            _submitted[channel] = n + 1;
            var on = !_on.TryGetValue(channel, out var flag) || flag;
            if (!on)
            {
                _suppressed.TryGetValue(channel, out var s);
                _suppressed[channel] = s + 1;
            }

            Submitted?.Invoke(channel, tick);
            return on;
        }
    }
}
