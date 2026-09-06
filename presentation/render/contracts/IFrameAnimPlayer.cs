using System;
using Core.Foundation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// 序列帧动画播放器（见 09_表现层.md 第 4.5 节"序列帧动画预留接口（sprite 型专用）"）：
    /// <c>play(clipId, loop, speed)</c>/<c>stop()</c>/<c>onComplete(callback)</c> 三个方法与 09 原文
    /// 逐字对应；<see cref="OnAnimEvent"/> 是本模块的勘误扩展（见 <see cref="FrameAnimClip"/> 类型
    /// 注释、09 勘误"sprite 型序列帧剪辑字段"一句），与 model 型 <c>IRenderer3D.onAnimEvent</c>（02 第
    /// 1.12 节）对称，服务 09 第 4.3 节 <c>anim_keyframe_driven</c> 命中帧同步策略。
    /// </summary>
    public interface IFrameAnimPlayer
    {
        /// <summary>播放 <paramref name="clipId"/>；<paramref name="loop"/> 为 true 时到达末帧后从头
        /// 循环（不触发 <see cref="OnComplete"/>）；<paramref name="speed"/> 是播放速度倍率，必须为
        /// 正数。播放一个新剪辑会立即替换正在播放的剪辑（同 <see cref="ProceduralAnimSequencer"/>
        /// "新触发替换旧实例"的一贯惯例），不触发旧剪辑的 <see cref="OnComplete"/>（旧剪辑是被主动
        /// 切走，不是自然播放完毕，语义与 <see cref="Stop"/> 一致）。</summary>
        void Play(Id clipId, bool loop, double speed);

        /// <summary>停止播放，不触发 <see cref="OnComplete"/>。空闲时调用是空操作。</summary>
        void Stop();

        /// <summary>订阅"非循环剪辑自然播放完毕"（到达末帧且 <c>loop=false</c>）。返回句柄供取消订阅。</summary>
        SubscriptionHandle OnComplete(Action callback);

        /// <summary>订阅关键帧事件（见 <see cref="FrameAnimClip.Keyframes"/>）：播放推进到某个已登记
        /// 标记对应的帧下标时，以该标记名为参数触发一次。返回句柄供取消订阅。</summary>
        SubscriptionHandle OnAnimEvent(Action<string> callback);
    }
}
