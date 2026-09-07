using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// 特效播放器（见 09_表现层.md 第 5.3 节 <c>VfxPlayer</c>）：按 <c>vfx.def</c> 查表并经
    /// <see cref="Core.Foundation.EngineAdapter.IRenderer2D"/>/<see cref="Core.Foundation.EngineAdapter.IRenderer3D"/>/
    /// <see cref="Core.Foundation.EngineAdapter.ICamera"/> 播放（表现层铁律 P4，只经 L-1 接口绘制）。
    /// </summary>
    public interface IVfxPlayer
    {
        /// <summary>播放一条特效；<paramref name="vfxId"/> 未在 <c>vfx.def</c> 登记、或
        /// <paramref name="at"/> 的挂接目标不可解析时返回 null（记一条诊断，不抛异常）。
        /// <paramref name="parameters"/> 供 <c>IRenderer2D.EmitParticle</c> 的着色器/发射参数
        /// 透传，可为 null（等价空字典）。</summary>
        ParticleHandle? Spawn(Id vfxId, VfxAttach at, IReadOnlyDictionary<string, double>? parameters);

        /// <summary>提前停止一次播放；<paramref name="handle"/> 不存在/已停止时安全忽略。</summary>
        void Stop(ParticleHandle handle);

        /// <summary>按 <paramref name="dt"/> 推进对象池：到达 <c>vfx.def.lifetime</c> 的播放实例
        /// 自动回收（见 09 第 5.4 节对象池"按 lifetime 超时回收"）。</summary>
        void Update(double dt);

        /// <summary>GP-09 新增（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：
        /// 仍在排队等待首次异步加载完成（或超时）的播放请求数——<see cref="Spawn"/> 命中未加载完成
        /// 的资源时不会立即产生播放效果，也不返回可用于判定"这一步已经播完"的信号（返回 null），
        /// 调用方（<c>Presentation.FeedbackBinder.Core.CompositeFeedbackSink</c>/
        /// <c>FeedbackBinder.HasPendingPlayback</c>）需要靠本属性把"冷资源首次加载"也计入离散步的
        /// 表现完成门，否则该步会在特效真正播出前就被判定为已完成。</summary>
        int PendingSpawnCount { get; }

        /// <summary>N17 根治（architecture/落地计划/audit-68c9bed-20260907/code-review.md）：
        /// <see cref="PendingSpawnCount"/> 可能发生变化时触发（资源加载完成/失败，见
        /// <c>VfxPlayer.OnResourceLoadCompleted</c>）——<see cref="PendingSpawnCount"/> 是一个纯
        /// 轮询属性，调用方（<c>CompositeFeedbackSink</c>）此前只能在自己主动查询的那一刻看到最新
        /// 值，冷资源真正加载完成那一刻没有任何信号可以驱动"重新检查一次是否已经真正播完"，导致
        /// <c>FeedbackBinder</c> 的 <c>PlaybackFinishedEvent</c> 要么提前发出（<c>PlaybackQueue.
        /// Finished</c> 触发时只看队列本身，见该类型 N17 判断记录），要么在 Immediate 模式下永远
        /// 不会补发。本事件只是"提示重新读取 <see cref="PendingSpawnCount"/>"，不保证触发时刻
        /// 一定已经归零，也不携带具体数值，调用方必须重新读取属性而不是依赖本事件的调用次数/时机。
        /// </summary>
        event Action? PendingSpawnCountChanged;
    }
}
