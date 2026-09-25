using System;
using Core.Foundation.Common;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>
    /// 反馈动作的出口（见 09_表现层.md 第 6.1 节六种 <c>FeedbackAction</c>）：<c>FeedbackBinder</c>
    /// 只负责"哪个事件、满足什么条件、按什么顺序触发哪些动作"，具体动作怎么落地（飘字用哪套 UI
    /// 控件池、顿帧怎么影响 tick 节奏、震屏具体强度换算）留给注入的实现——本模块只提供把 vfx/sfx
    /// 转发给 <c>Presentation.VfxSfx</c> 播放器的默认实现 <c>CompositeFeedbackSink</c>，其余四种
    /// 动作转给调用方注入的委托（见该类型）。
    /// </summary>
    public interface IFeedbackSink
    {
        void FloatingText(Id entityId, Id styleId, string text);

        void PlayVfx(Id vfxId, FeedbackAttachSpec attach);

        /// <summary>ADR-0075 新增：按 (<paramref name="vfxId"/>, <paramref name="attach"/> 解析出的
        /// 附着实体) 定位此前由 <see cref="PlayVfx"/> 播放、当前仍在播的实例并停止；查不到在播实例时
        /// （从未播过、已自然超时回收、或已被停止过）静默忽略，不写诊断——"光环没播过特效就被移除"
        /// 一类场景属于正常情况，不是缺陷信号。带默认实现（C# 8+ default interface member，默认体为
        /// 空操作），保证既有 <see cref="IFeedbackSink"/> 实现方不需要改一行代码即可继续编译通过
        /// （ABI 只加法）；本模块默认实现 <c>Presentation.FeedbackBinder.Core.CompositeFeedbackSink</c>
        /// 显式覆盖，转给 <c>Presentation.VfxSfx.Contracts.IVfxPlayer.Stop</c>，见该类型判断记录
        /// "为什么按 (vfx_id, 实体) 建表，不把句柄塞进数据层"。</summary>
        void StopVfx(Id vfxId, FeedbackAttachSpec attach) { }

        void PlaySfx(Id sfxId, Vec2? at);

        /// <summary>ADR-0089 新增：带 <paramref name="attach"/> 的重载——循环音效
        /// （<c>sfx.def.loop=true</c>）挂接到具体实体后才能被 <see cref="StopSfx"/> 按同一对
        /// (sfxId, 附着实体) 键定位停止，非循环音效/<paramref name="attach"/> 为 world 时行为与
        /// <see cref="PlaySfx(Id, Vec2?)"/> 完全一致（不跟踪、不建键）。默认接口成员，默认体转调
        /// 旧 <see cref="PlaySfx(Id, Vec2?)"/>、忽略 <paramref name="attach"/>（ABI 只加法，同
        /// <see cref="StopVfx"/> 惯例），保证既有 <see cref="IFeedbackSink"/> 实现方不需要改一行
        /// 代码即可继续编译通过；本模块默认实现 <c>CompositeFeedbackSink</c> 显式覆盖，转给
        /// <c>Presentation.VfxSfx.Contracts.ISfxPlayer.PlayAttached</c>，见该类型判断记录。</summary>
        void PlaySfx(Id sfxId, Vec2? at, FeedbackAttachSpec attach) => PlaySfx(sfxId, at);

        /// <summary>ADR-0089 新增：按 (<paramref name="sfxId"/>, <paramref name="attach"/> 解析出的
        /// 附着实体) 定位此前由 <see cref="PlaySfx(Id, Vec2?, FeedbackAttachSpec)"/> 播放、当前仍在播
        /// 的循环音效实例并停止；查不到在播实例时（从未播过、不是循环音效、已被停止过）静默忽略，
        /// 不写诊断——同 <see cref="StopVfx"/> 惯例。带默认实现（空操作，ABI 只加法）；本模块默认
        /// 实现 <c>CompositeFeedbackSink</c> 显式覆盖，转给
        /// <c>Presentation.VfxSfx.Contracts.ISfxPlayer.StopAttached</c>。</summary>
        void StopSfx(Id sfxId, FeedbackAttachSpec attach) { }

        void Freeze(double durationMs);

        void ShakeCamera(Id profileId);

        void Flash(Id entityId, Id profileId);

        /// <summary>GP-09 新增（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：
        /// 本 sink 转发出去的 <c>play_vfx</c>/<c>play_sfx</c> 是否仍有排队等待首次异步加载完成
        /// （或超时）、因此还没真正产生播放效果的请求——<see cref="PlayVfx"/>/<see cref="PlaySfx"/>
        /// 命中冷资源时立即返回，不会阻塞调用方，但也不提供任何"这条播放到底有没有真的开始"的信号；
        /// <c>FeedbackBinder.HasPendingPlayback</c> 需要这个信号才能把"首次加载中的 vfx/sfx"也计入
        /// 离散步的表现完成门，否则该步会在特效/音效真正播出前就被判定为已完成，后续动作可能在它
        /// 前面先播、造成乱序。</summary>
        bool HasPendingPlayback { get; }

        /// <summary>N17 根治（architecture/落地计划/audit-68c9bed-20260907/code-review.md）：
        /// <see cref="HasPendingPlayback"/> 可能发生变化时触发（冷 vfx/sfx 资源加载完成、失败或
        /// 超时清理，见 <see cref="Presentation.VfxSfx.Contracts.IVfxPlayer.
        /// PendingSpawnCountChanged"/>/<see cref="Presentation.VfxSfx.Contracts.ISfxPlayer.
        /// PendingPlayCountChanged"/>）。<c>FeedbackBinder</c> 借此在"排队时判定这一步确有待回放
        /// 内容"之后，于资源真正加载完成那一刻补一次完成检查——此前只有
        /// <see cref="Presentation.FeedbackBinder.Core.PlaybackQueue.Finished"/>（队列本身清空）
        /// 一条驱动路径，冷资源加载完成时队列早已清空、不会再有"清空"这个事件可触发，导致
        /// Sequential 模式下节奏门提前打开、Immediate 模式下永远等不到解门信号（见
        /// <see cref="HasPendingPlayback"/> 判断记录"GP-09 根治补充"）。本事件只是"提示重新读取
        /// <see cref="HasPendingPlayback"/>"，不保证触发时已经归零、不携带具体数值。</summary>
        event Action? PendingPlaybackChanged;
    }
}
