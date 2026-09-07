using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.VfxSfx.Contracts;

namespace Presentation.FeedbackBinder.Core
{
    /// <summary>
    /// 命中帧同步等待队列（ADR-0017 决策 d）：<see cref="Presentation.FeedbackBinder.Core.FeedbackBinder"/>
    /// 在 <c>RenderOptions.HitFrameSync == AnimKeyframeDriven</c> 且规则声明
    /// <see cref="Presentation.FeedbackBinder.Contracts.FeedbackSyncMode.HitFrame"/> 时，把该规则的
    /// 一批动作包成一个"释放动作"缓存到本队列，等待攻击方 <see cref="IHitFrameSource"/> 广播的命中帧
    /// 到达（或超时兜底）才真正释放执行。
    /// <para>
    /// 判断记录（超时兜底默认 0.5 秒）：任务书拍板"默认 0.5 秒"；超时释放时经注入的
    /// <see cref="IPresentationDiagnostics"/> 记一条诊断（不阻断——本策略的定位是"呈现时机微调"，即便
    /// 命中帧事件因为某种原因没有到达，反馈动作也必须最终播放，不能无限期悬挂，同 09 第 4.3 节"逻辑
    /// 判定结果与时机永远由规则层按固定步长决定，表现层的关键帧只调节反馈动作播放的呈现时刻"——超时
    /// 只是呈现时机上的一次降级，不影响任何已经结算的战斗结果）。
    /// </para>
    /// <para>
    /// 判断记录（攻击方无 rig 时立即播放）：<see cref="IHitFrameSource.HasRig"/> 为 false 时（该实体从未
    /// 绑定过 View，或 View 未接入 rig——如纯逻辑测试场景、无表现的战斗模拟）说明这个实体永远不会产生
    /// 命中帧事件，继续排队等待只会白白等到超时，不如直接同步释放，与"无表现时逻辑仍可独立运行"（09
    /// 第 1 节表现层铁律 P4 的推论）一致。
    /// </para>
    /// <para>
    /// 判断记录（多次攻击不串扰）：<see cref="_pending"/> 是一个有序列表而非"每实体一个槽位"，命中帧
    /// 事件到达时只释放该实体"最早入队"的那一条（FIFO），不同实体的等待项互不影响；同一实体连续多次
    /// 攻击各自入队一条，各自等待各自的下一次命中帧事件，不会互相抢占或合并。
    /// </para>
    /// </summary>
    public sealed class HitFrameSyncPolicy : IDisposable
    {
        /// <summary>默认超时兜底时长（秒），见类型注释判断记录。</summary>
        public const double DefaultTimeoutSeconds = 0.5;

        private sealed class PendingEntry
        {
            public readonly Id EntityId;
            public readonly Action Release;
            public double RemainingSeconds;

            public PendingEntry(Id entityId, Action release, double remainingSeconds)
            {
                EntityId = entityId;
                Release = release;
                RemainingSeconds = remainingSeconds;
            }
        }

        private readonly IHitFrameSource _source;
        private readonly double _timeoutSeconds;
        private readonly IPresentationDiagnostics _diagnostics;
        private readonly List<PendingEntry> _pending = new List<PendingEntry>();

        /// <summary><see cref="PendingCount"/> 可能发生变化时触发（新入队、按时释放、超时释放、立即
        /// 释放均会触发），供 <see cref="Presentation.FeedbackBinder.Core.FeedbackBinder"/> 复用既有
        /// <c>IFeedbackSink.PendingPlaybackChanged</c> 同一套"完成信号重新检查"接线（见该类型
        /// <c>TryPublishFinished</c> 判断记录）。</summary>
        public event Action? PendingChanged;

        public int PendingCount => _pending.Count;

        public HitFrameSyncPolicy(IHitFrameSource source, double timeoutSeconds = DefaultTimeoutSeconds, IPresentationDiagnostics? diagnostics = null)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : DefaultTimeoutSeconds;
            _diagnostics = diagnostics ?? new PresentationDiagnosticsRecorder();

            _source.HitFrameReached += OnHitFrameReached;
        }

        /// <summary>缓存 <paramref name="release"/>，等待 <paramref name="attackerEntityId"/> 的下一次
        /// 命中帧事件（或超时）后调用；<paramref name="attackerEntityId"/> 当前未登记 rig 时立即同步
        /// 调用 <paramref name="release"/>（见类型注释判断记录）。</summary>
        public void WaitForHitFrame(Id attackerEntityId, Action release)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));

            if (!_source.HasRig(attackerEntityId))
            {
                release();
                return;
            }

            _pending.Add(new PendingEntry(attackerEntityId, release, _timeoutSeconds));
            PendingChanged?.Invoke();
        }

        /// <summary>按 <paramref name="dt"/> 推进全部等待项的超时计时；到期项按入队顺序依次超时释放。</summary>
        public void Update(double dt)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            List<PendingEntry>? timedOut = null;
            for (var i = 0; i < _pending.Count; i++)
            {
                _pending[i].RemainingSeconds -= dt;
                if (_pending[i].RemainingSeconds <= 0)
                {
                    timedOut ??= new List<PendingEntry>();
                    timedOut.Add(_pending[i]);
                }
            }

            if (timedOut == null)
            {
                return;
            }

            foreach (var entry in timedOut)
            {
                _pending.Remove(entry);
                _diagnostics.Warn($"命中帧同步等待超时（{_timeoutSeconds}s）：实体 \"{entry.EntityId}\" 未在超时前收到命中帧事件，按兜底策略立即播放");
                entry.Release();
            }

            PendingChanged?.Invoke();
        }

        public void Dispose() => _source.HitFrameReached -= OnHitFrameReached;

        private void OnHitFrameReached(Id entityId)
        {
            for (var i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].EntityId == entityId)
                {
                    var entry = _pending[i];
                    _pending.RemoveAt(i);
                    entry.Release();
                    PendingChanged?.Invoke();
                    return;
                }
            }
        }
    }
}
