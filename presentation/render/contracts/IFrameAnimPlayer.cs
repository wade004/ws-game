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

        /// <summary>
        /// ADR-0072 决策 2 新增：订阅"当前帧下标变化"（<see cref="Play"/> 立即以第 0 帧触发一次，
        /// 此后每次推进到新的帧下标各触发一次，携带该帧下标）——供纸娃娃层逐层动画驱动
        /// （<c>Adapter.Unity.Presentation.UnityViewFactory</c> 的每实体共享回调）按帧号从各自登记的
        /// 逐层剪辑里取出对应 <c>Sprite</c> 写回 <see cref="Presentation.Common.IRenderConventionHost"/>
        /// 之外的具体引擎渲染通道，不需要调用方自己重新实现一遍"经过的时间 → 帧下标"推进逻辑（同
        /// <see cref="Presentation.Render.FrameAnimPlayer"/> 类型注释"<c>FrameChanged</c> 供引擎适配层
        /// 订阅后按帧号切换实际显示的贴图"一贯设计意图，本方法只是把该事件正式提升为
        /// <see cref="IFrameAnimPlayer"/> 契约的一部分，供跨实现统一消费）。
        /// <para>
        /// 判断记录（ABI-additive：默认接口实现，不要求既有 <see cref="IFrameAnimPlayer"/> 实现新增
        /// 任何成员）：本方法新增前已经存在的第三方/测试用 <see cref="IFrameAnimPlayer"/> 实现（未重写
        /// 本方法）调用本方法会拿到一个立即返回、从不触发回调的空句柄——等价于"该实现不支持逐帧通知"，
        /// 调用方（<c>UnityViewFactory</c>）据此只能退回 ADR-0072 决策 2 的整身兜底路线（纸娃娃层逐层
        /// 播放本就要求引擎侧真正实现帧号通知，见 <see cref="Presentation.Render.FrameAnimPlayer.OnFrameChanged"/>/
        /// <c>Adapter.Unity.Presentation.UnityFrameAnimPlayer.OnFrameChanged</c> 两个框架自带实现），
        /// 不会因为存量实现未跟着改就编译失败或运行期抛异常——与本接口既有 <see cref="ICharacterRig"/>
        /// 同类默认接口实现（<c>HitFrameReached</c> 兼容层）同一套"新增成员不强制存量实现跟进"手法。
        /// </para>
        /// </summary>
        SubscriptionHandle OnFrameChanged(Action<int> callback) => new SubscriptionHandle(() => { });

        /// <summary>
        /// 手感落地 M2-A（顿帧表现冻结，手感设计/07 第 5 节）：是否处于暂停。暂停期间时间轴不推进（帧下标、关键帧、播放完成回调都不触发），
        /// 恢复后从暂停点继续；暂停期间调用 <see cref="Play"/> 照常换上新剪辑（停在第 0 帧，随恢复开始推进）。
        /// <para>
        /// 判断记录（ABI-additive 默认接口成员，同 <see cref="OnFrameChanged"/> 的做法）：既有实现（未重写本成员）恒报告"未暂停"、
        /// <see cref="SetPaused"/> 为空操作，等价于"该实现不支持暂停"，调用方（rig 的顿帧冻结）据此只能冻结自己能冻结的部分，不报错。
        /// 框架自带两个实现（<see cref="Presentation.Render.FrameAnimPlayer"/>、Unity 的 <c>UnityFrameAnimPlayer</c>）都真正支持。
        /// </para>
        /// </summary>
        bool IsPaused => false;

        /// <summary>设置暂停状态（幂等），见 <see cref="IsPaused"/>。</summary>
        void SetPaused(bool paused) { }

        /// <summary>
        /// ADR-0147：播放中改变当前剪辑的播放速率倍率（动作分相重映射、移动剪辑匹配地面速度），不重置时间轴；<paramref name="speed"/> 必须为正数。
        /// 默认接口成员：既有实现缺省什么都不做（等价于"该实现不支持播放中调速"，剪辑按 <see cref="Play"/> 时的速率播完）。
        /// 框架自带两个实现（<see cref="Presentation.Render.FrameAnimPlayer"/>、Unity 的 <c>UnityFrameAnimPlayer</c>）都支持。
        /// </summary>
        void SetSpeed(double speed) { }
    }
}
