using System;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// 音效播放器（见 09_表现层.md 第 5.3 节 <c>SfxPlayer</c>，第 5.5 节分层音效扩展了
    /// <c>SetLayerMuted</c>）：按 <c>sfx.def</c> 查表并经 <see cref="Core.Foundation.EngineAdapter.IAudio"/> 播放。
    /// </summary>
    public interface ISfxPlayer
    {
        /// <summary>播放一次音效；<paramref name="sfxId"/> 未在 <c>sfx.def</c> 登记时返回 null（记
        /// 一条诊断）。<paramref name="at"/> 为 null 表示非定位音效（如 UI/语音）。</summary>
        SfxHandle? Play(Id sfxId, Vec2? at);

        void Stop(SfxHandle handle);

        /// <summary>设置某条分层音效轨道（<c>sfx.def.layer</c>）的音量倍率，作用于该层此后
        /// （以及已在播放、下次调用 <see cref="Play"/> 前保持原音量不变——本模块不追踪单次播放
        /// 归属哪次音量设置，见 vfx_sfx/README.md 判断记录）的播放。</summary>
        void SetLayerVolume(string layer, double volume);

        /// <summary>静音/取消静音某条分层音效轨道；静音时 <see cref="Play"/> 仍会调用
        /// <see cref="Core.Foundation.EngineAdapter.IAudio.PlaySfx"/>（保持句柄语义一致），但音量倍率按 0 传入。</summary>
        void SetLayerMuted(string layer, bool muted);

        /// <summary>GP-09 新增（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：
        /// 仍在排队等待首次异步加载完成（或超时）的播放请求数——同 <see cref="Presentation.VfxSfx.
        /// Contracts.IVfxPlayer.PendingSpawnCount"/> 判断记录，冷资源首次加载的音效同样不应该被
        /// 当作"这一步已经播完"。</summary>
        int PendingPlayCount { get; }

        /// <summary>N17 根治：同 <see cref="Presentation.VfxSfx.Contracts.IVfxPlayer.
        /// PendingSpawnCountChanged"/> 判断记录——<see cref="PendingPlayCount"/> 可能发生变化时
        /// 触发（资源加载完成/失败，或 <c>SfxPlayer.SweepTimedOutPendingPlays</c> 超时清理），只是
        /// "提示重新读取"，不携带数值、不保证已经归零。</summary>
        event Action? PendingPlayCountChanged;

        /// <summary>
        /// C07 根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：此前排队等待
        /// 首次加载完成的播放请求只在下一次 <see cref="Play"/> 调用开头惰性扫一遍超时（见
        /// <c>SfxPlayer.SweepTimedOutPendingPlays</c> 判断记录）——若节奏门已经关闭且此后没有任何
        /// 新的 <see cref="Play"/> 调用，永不回调的加载请求就不会被扫到，<see
        /// cref="PendingPlayCount"/> 永远卡在非零、<see cref="PendingPlayCountChanged"/> 永远不会
        /// 因超时而触发。本方法补一个不依赖下一次 <see cref="Play"/> 的时钟驱动入口——与 <see
        /// cref="Presentation.VfxSfx.Contracts.IVfxPlayer.Update"/> 同一惯例，由
        /// <c>CompositeFeedbackSink</c>/引擎侧逐帧驱动（同 <c>Presentation.VfxSfx.Core.VfxPlayer.
        /// Update</c> 的既有生产接线，见 <c>FrameworkResidentHost.OnFrameTick</c>）；<paramref
        /// name="dt"/> 当前实现内部仍按超时截止时间戳判定（不是按 dt 累计倒计时），保留参数只是为了
        /// 与 <c>IVfxPlayer.Update</c> 签名一致、供统一的逐帧驱动代码调用，不代表已改为 dt 累计语义。
        /// </summary>
        void Update(double dt);
    }
}
