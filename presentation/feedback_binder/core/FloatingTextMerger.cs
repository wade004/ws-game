using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.FeedbackBinder.Contracts;

namespace Presentation.FeedbackBinder.Core
{
    /// <summary>
    /// 短时间窗口内同源同类型飘字的合并（见 09_表现层.md 第 6.3 节）：短时间窗口内同一
    /// <c>(entityId, styleId)</c> 的多条飘字按 <see cref="MergeMode"/> 聚合为一条展示。
    /// <para>
    /// 判断记录（合并范围限定为数值型飘字）：本实现只对 <see cref="TextSourceKind.Amount"/>
    /// （09 第 6.1 节 <c>text_source: amount</c>，多段伤害/治疗的典型场景）做数值合并——
    /// <see cref="MergeMode.Sum"/> 语义（求和）天然要求文本是数值；<see cref="TextSourceKind.Field"/>/
    /// <see cref="TextSourceKind.Literal"/> 文本（如闪避/招架提示语）一律立即派发、不进入合并窗口，
    /// 因为 09 原文举的合并例子（"多段伤害的技能在同一 tick 内命中多个目标，或一次多跳伤害"）
    /// 本就是数值场景，非数值文本的"折叠为 xN"语义留给未来需要时再扩展（本任务范围内的简化，
    /// 见 feedback_binder/README.md）。
    /// </para>
    /// <para>
    /// 判断记录（窗口是否随后续命中延长）：合并窗口从该 <c>(entityId, styleId)</c> 组合第一次出现
    /// 起固定计时，后续同组合的追加不延长窗口——这样合并窗口有确定的上限，行为对测试/回放更容易
    /// 预测；09 原文未规定这一细节。
    /// </para>
    /// </summary>
    public sealed class FloatingTextMerger
    {
        private sealed class Pending
        {
            public Id EntityId;
            public Id StyleId;
            public MergeMode Mode;
            public double RemainingWindow;
            public double Sum;
            public double FirstAmount;
            public int Count;
        }

        private readonly double _mergeWindow;
        private readonly Func<double, string> _numberFormat;
        private readonly Action<Id, Id, string> _dispatch;
        private readonly List<Pending> _pending = new List<Pending>();

        /// <summary><paramref name="mergeWindow"/> 小于等于 0 表示不启用合并：<see cref="Offer"/>
        /// 恒立即派发。<paramref name="dispatch"/> 是合并/立即派发结果的最终出口
        /// （entityId, styleId, 格式化文本）。</summary>
        public FloatingTextMerger(double mergeWindow, Func<double, string> numberFormat, Action<Id, Id, string> dispatch)
        {
            _mergeWindow = mergeWindow;
            _numberFormat = numberFormat ?? throw new ArgumentNullException(nameof(numberFormat));
            _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        }

        /// <summary>提交一条数值飘字；未启用合并或找不到同组合的进行中窗口时视 <paramref name="mode"/>
        /// 开一个新窗口（<see cref="_mergeWindow"/> 小于等于 0 时立即派发，不开窗口）。</summary>
        public void Offer(Id entityId, Id styleId, double amount, MergeMode mode)
        {
            if (_mergeWindow <= 0)
            {
                _dispatch(entityId, styleId, _numberFormat(amount));
                return;
            }

            var existing = FindPending(entityId, styleId);
            if (existing != null)
            {
                existing.Sum += amount;
                existing.Count++;
                return;
            }

            _pending.Add(new Pending
            {
                EntityId = entityId,
                StyleId = styleId,
                Mode = mode,
                RemainingWindow = _mergeWindow,
                Sum = amount,
                FirstAmount = amount,
                Count = 1,
            });
        }

        /// <summary>立即派发一条非数值飘字，不参与合并（见类型判断记录）。</summary>
        public void OfferImmediate(Id entityId, Id styleId, string text) => _dispatch(entityId, styleId, text);

        /// <summary>按 <paramref name="dt"/> 推进全部进行中的合并窗口；到期的按 <see cref="MergeMode"/>
        /// 格式化并派发。</summary>
        public void Update(double dt)
        {
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                p.RemainingWindow -= dt;
                if (p.RemainingWindow > 0)
                {
                    continue;
                }

                _pending.RemoveAt(i);
                Flush(p);
            }
        }

        /// <summary>当前是否还有停留在合并窗口内、尚未派发进 <c>PlaybackQueue</c> 的数值飘字。
        /// <para>
        /// 供 <see cref="FeedbackBinder"/> 组合成"当前离散步是否还有未回放完的表现"这一统一查询
        /// （见 <see cref="FeedbackBinder.HasPendingPlayback"/> 判断记录）：<see cref="Offer"/> 在
        /// <c>MergeWindow &gt; 0</c> 时把飘字暂存进本类私有的 <c>_pending</c> 列表，窗口到期前既不
        /// 派发给 <c>_dispatch</c>、也不进入 <c>PlaybackQueue</c>——仅看 <c>PlaybackQueue.PendingCount</c>
        /// 无法感知这部分"看不见但确实还没播完"的表现内容。
        /// </para>
        /// </summary>
        public bool HasPendingMerges => _pending.Count > 0;

        /// <summary>立即结算全部进行中的窗口（不等待自然到期），例如 <c>PlaybackQueue.Skip()</c>
        /// 联动场景；调用方按需选用，本模块 <c>FeedbackBinder</c> 默认不主动调用。</summary>
        public void FlushAll()
        {
            for (var i = 0; i < _pending.Count; i++)
            {
                Flush(_pending[i]);
            }
            _pending.Clear();
        }

        private void Flush(Pending p)
        {
            // Fold：展示首次命中的数值 + "xN"（同 WoW 式战斗文本"重复次数"惯例），不是均值——
            // 多跳伤害各跳数值通常相近，首值足以代表"这一批大概多少"，且避免均值引入除法舍入。
            var text = p.Mode == MergeMode.Sum
                ? _numberFormat(p.Sum)
                : $"{_numberFormat(p.FirstAmount)} x{p.Count}";
            _dispatch(p.EntityId, p.StyleId, text);
        }

        private Pending? FindPending(Id entityId, Id styleId)
        {
            for (var i = 0; i < _pending.Count; i++)
            {
                var p = _pending[i];
                if (p.EntityId.Equals(entityId) && p.StyleId.Equals(styleId))
                {
                    return p;
                }
            }
            return null;
        }
    }
}
