using System.Collections.Generic;
using System.Linq;

namespace Core.Rules.Common
{
    /// <summary>
    /// 结算管线的输出（见 06 第 4.1/4.7 节 <c>CombatHost.resolveEffect</c>/<c>EffectSink.applyEffect</c>
    /// 返回值）。不可变：<see cref="Steps"/> 在构造期做防御性拷贝，构造之后传入的原始列表被外部修改
    /// 不会影响本实例（见任务书"tests...ResolveResult 不可变性"）。
    /// </summary>
    public sealed class ResolveResult
    {
        public HitResult Hit { get; }

        /// <summary>结算前效果原语给出的原始请求量（伤害/治疗的"理论值"，落地前）。</summary>
        public double RequestedAmount { get; }

        /// <summary>实际落地的量（经免疫/吸收/减免后的最终值）。</summary>
        public double FinalAmount { get; }

        /// <summary>被吸收池（<c>absorb</c> 光环）扣减掉的量。</summary>
        public double Absorbed { get; }

        /// <summary>目标是否对本次效果免疫（见 06 第 4.1 节"免疫吸收"步骤）。</summary>
        public bool Immune { get; }

        /// <summary>本次结算是治疗分支（true）还是伤害分支（false）。</summary>
        public bool IsHeal { get; }

        /// <summary>各步骤的中间值文本，供测试与调试比对手算结果；可为 null（不强制记录）。</summary>
        public IReadOnlyList<string>? Steps { get; }

        public ResolveResult(
            HitResult hit,
            double requestedAmount,
            double finalAmount,
            double absorbed,
            bool immune,
            bool isHeal,
            IEnumerable<string>? steps = null)
        {
            Hit = hit;
            RequestedAmount = requestedAmount;
            FinalAmount = finalAmount;
            Absorbed = absorbed;
            Immune = immune;
            IsHeal = isHeal;
            Steps = steps?.ToArray();
        }
    }
}
