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
    /// 攻击各自入队一条，各自等待各自的下一次命中帧事件，不会互相抢占或合并——本条约束由
    /// <c>HitFrameSyncPolicyTests.MultipleAttacks_SameEntity_DoNotCrossTalk</c> 锁定，PR130-04
    /// 根治（见下一条判断记录）刻意保留，没有改动。
    /// </para>
    /// <para>
    /// 判断记录（PR130-04 根治：一次 <see cref="WaitForHitFrame"/> 调用即一个不可拆分的批次）：本类型
    /// 从不感知"这次调用携带的 <c>release</c> 委托内部打包了几个具体动作"——<see cref="Presentation.FeedbackBinder.Core.FeedbackBinder"/>
    /// 已经改为把同一次 <c>OnEvent</c>（同一个逻辑事件）命中的全部 <c>sync: hit_frame</c> 规则的动作
    /// 合并进同一个 <c>release</c> 委托、只调用一次本方法（见该类型 <c>OnEvent</c> 判断记录"同一事件的
    /// 全部动作作为一个批次"），取代此前"每条规则各自调用一次"、被本类型的 FIFO 单条释放机制拆散成
    /// 多条互相错开的等待项这一问题——批次的边界由调用方（本类型的调用方，不是本类型自己）划定，本
    /// 类型只需要老实保证"一次 <see cref="WaitForHitFrame"/> 调用 = 一次命中帧时的一次完整
    /// <c>release()</c> 调用"这一最基本的原子性，不需要（也不应该）自己再猜测多次调用之间是否属于
    /// "同一次攻击"——那是只有调用方才知道的语义边界。
    /// </para>
    /// <para>
    /// 判断记录（PR140-04 根治：同一次攻击多个目标按 <paramref name="batchToken"/>（见
    /// <see cref="WaitForHitFrame(Id, object, Action)"/>）整批原子释放，取代此前"同一实体的多条等待项
    /// 永远各自 FIFO 逐条释放"的立场）：<c>core/rules/common/contracts/Events.cs</c> 的
    /// <c>CombatDamageDealtEvent</c> 没有携带施法/攻击实例 id（范围攻击对多个目标各自派发独立事件，
    /// 事件本身不知道自己和另一个事件同属哪一次逻辑攻击），本类型因此不能从事件本身推出"批次"边界；
    /// 批次改由调用方（<see cref="Presentation.FeedbackBinder.Core.FeedbackBinder"/>）显式传入一个
    /// <c>batchToken</c>（按引用比较，不解释语义）——同一个 <c>batchToken</c> 的全部
    /// <see cref="WaitForHitFrame(Id, object, Action)"/> 调用视为同一次攻击的不可拆分批次：命中帧到达
    /// 或超时兜底时一次性原子释放该 token 名下当前全部等待项（不再是"只释放最早一条"），释放顺序按
    /// 各自入队顺序；释放完毕后同一 token 不再持有任何等待项，调用方若之后用同一个 token 对象再次调用
    /// 本方法会开启一批新的等待（本类型不阻止，但那已经不是"批次"这一概念要处理的问题——调用方在
    /// FeedbackBinder 一侧按"该攻击者当前是否还有未释放的批次"决定要不要复用同一个 token 对象，见
    /// 该类型 <c>OnEvent</c> 判断记录）。旧的两个无 token 重载（<see cref="WaitForHitFrame(Id, Action)"/>）
    /// 保留：每次调用各自分配一个全新的、与任何其他调用都不相等的 token 对象，行为与改动前逐字相同
    /// （每条各自单独一批，互不合并）——<c>HitFrameSyncPolicyTests.MultipleAttacks_SameEntity_DoNotCrossTalk</c>
    /// 验证的正是"两次确实不同的攻击不应该被误合并成一批"这一相反场景，不能删除或放宽，本次改动刻意
    /// 保持它逐字通过：不传 token 时永远是"各自一批"，只有调用方显式传入同一个 token 才会合批。
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
            public readonly object BatchToken;
            public double RemainingSeconds;

            public PendingEntry(Id entityId, Action release, object batchToken, double remainingSeconds)
            {
                EntityId = entityId;
                Release = release;
                BatchToken = batchToken;
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

        /// <summary>PR140-04 新增：某个攻击者名下的一整批等待项刚被原子释放（命中帧或超时兜底皆会
        /// 触发，携带该攻击者 <see cref="Id"/>）——供 <see cref="Presentation.FeedbackBinder.Core.FeedbackBinder"/>
        /// 得知"这个攻击者当前打开的批次 token 已经用完"，从而在下一次同一攻击者触发 <c>sync: hit_frame</c>
        /// 规则时分配一个新 token（不与已经释放的旧批次继续合并），见该类型判断记录。</summary>
        public event Action<Id>? BatchReleased;

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
        /// 调用 <paramref name="release"/>（见类型注释判断记录）。本重载不接受批次 token，等价于每次
        /// 调用各自分配一个全新、与任何其他调用都不相等的 token（见 <see cref="WaitForHitFrame(Id, object, Action)"/>
        /// 判断记录"两个无 token 重载"）——多次调用永远各自单独一批，不合并。</summary>
        public void WaitForHitFrame(Id attackerEntityId, Action release) =>
            WaitForHitFrame(attackerEntityId, new object(), release);

        /// <summary>PR140-04 新增：带批次 token 的重载——<paramref name="batchToken"/> 相同（按引用比较）
        /// 的多次调用视为同一次攻击的不可分割批次，命中帧到达或超时时一次性原子释放该 token 名下当前
        /// 全部等待项，见类型判断记录。<paramref name="attackerEntityId"/> 当前未登记 rig 时立即同步
        /// 调用 <paramref name="release"/>，不入队、不占用 token（与无 token 重载同一套宽容策略）。</summary>
        public void WaitForHitFrame(Id attackerEntityId, object batchToken, Action release)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            if (batchToken == null) throw new ArgumentNullException(nameof(batchToken));

            if (!_source.HasRig(attackerEntityId))
            {
                release();
                return;
            }

            _pending.Add(new PendingEntry(attackerEntityId, release, batchToken, _timeoutSeconds));
            PendingChanged?.Invoke();
        }

        /// <summary>按 <paramref name="dt"/> 推进全部等待项的超时计时；到期的批次（按 <see cref="PendingEntry.BatchToken"/>
        /// 分组，见类型判断记录）逐批原子释放——同一批次的全部等待项入队时刻相同（同一次
        /// <see cref="Presentation.FeedbackBinder.Core.FeedbackBinder.OnEvent"/> 派发窗口内的多次
        /// <see cref="WaitForHitFrame(Id, object, Action)"/> 调用共用同一个初始 <c>RemainingSeconds</c>，
        /// 此后每次 <see cref="Update"/> 按相同 <paramref name="dt"/> 一并递减），天然会在同一次
        /// <see cref="Update"/> 调用里一起越过 0，不需要额外的时间对齐逻辑。</summary>
        public void Update(double dt)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            for (var i = 0; i < _pending.Count; i++)
            {
                _pending[i].RemainingSeconds -= dt;
            }

            List<(object Token, Id EntityId)>? timedOutBatches = null;
            for (var i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].RemainingSeconds > 0)
                {
                    continue;
                }

                var token = _pending[i].BatchToken;
                var alreadyQueued = false;
                if (timedOutBatches != null)
                {
                    for (var j = 0; j < timedOutBatches.Count; j++)
                    {
                        if (ReferenceEquals(timedOutBatches[j].Token, token))
                        {
                            alreadyQueued = true;
                            break;
                        }
                    }
                }
                if (!alreadyQueued)
                {
                    timedOutBatches ??= new List<(object, Id)>();
                    timedOutBatches.Add((token, _pending[i].EntityId));
                }
            }

            if (timedOutBatches == null)
            {
                return;
            }

            foreach (var (token, entityId) in timedOutBatches)
            {
                _diagnostics.Warn($"命中帧同步等待超时（{_timeoutSeconds}s）：实体 \"{entityId}\" 未在超时前收到命中帧事件，按兜底策略立即释放该批次全部等待项");
                ReleaseBatch(token, entityId);
            }
        }

        public void Dispose() => _source.HitFrameReached -= OnHitFrameReached;

        private void OnHitFrameReached(Id entityId)
        {
            object? token = null;
            for (var i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].EntityId == entityId)
                {
                    token = _pending[i].BatchToken;
                    break;
                }
            }

            if (token == null)
            {
                return;
            }

            ReleaseBatch(token, entityId);
        }

        /// <summary>PR140-04 新增：原子释放 <paramref name="token"/> 名下当前全部等待项（按入队顺序
        /// 依次调用 <see cref="PendingEntry.Release"/>），供 <see cref="OnHitFrameReached"/> 与
        /// <see cref="Update"/> 的超时分支共用——两条释放路径都必须保证"同一批次要么全释放、要么全不
        /// 释放"这一原子性，不允许出现同一批次一部分随命中帧释放、另一部分掉进下一次命中帧或超时的
        /// 情形（PR140-04 复现的正是这个缺口）。</summary>
        private void ReleaseBatch(object token, Id entityId)
        {
            List<PendingEntry>? batch = null;
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_pending[i].BatchToken, token))
                {
                    continue;
                }
                batch ??= new List<PendingEntry>();
                batch.Add(_pending[i]);
                _pending.RemoveAt(i);
            }

            if (batch == null)
            {
                return;
            }

            // 上面按下标从后往前收集，此处翻转回原始入队顺序，保证批内动作按登记顺序派发。
            batch.Reverse();
            foreach (var entry in batch)
            {
                entry.Release();
            }

            BatchReleased?.Invoke(entityId);
            PendingChanged?.Invoke();
        }
    }
}
