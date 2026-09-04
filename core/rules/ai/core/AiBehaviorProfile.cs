using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Expr;

namespace Core.Rules.Ai
{
    /// <summary>
    /// <c>ai.behavior_profile</c> 一条记录解析后的运行期形态：字段照抄 <see cref="AiSchemas.BehaviorProfile"/>，
    /// <see cref="Transitions"/> 是 <c>transitions</c> 字段里各转移名对应 Expr 文本的解析结果
    /// （构造期解析一次，供每次转移判定复用，不重复解析），键为转移名（见
    /// <see cref="AiHost"/> 内部对照表"判断记录"）。本类型只在本模块内部使用，不对外暴露。
    /// </summary>
    internal sealed class AiBehaviorProfile
    {
        public Id Id { get; }

        public double PerceptionRadius { get; }

        public Id? PatrolPathRef { get; }

        public double LeashRange { get; }

        public double? FleeHpPctThreshold { get; }

        public CombatReturnPolicy CombatReturnPolicy { get; }

        public Id RotationRef { get; }

        public double DecisionInterval { get; }

        public IReadOnlyDictionary<string, ExprNode> Transitions { get; }

        public AiBehaviorProfile(
            Id id,
            double perceptionRadius,
            Id? patrolPathRef,
            double leashRange,
            double? fleeHpPctThreshold,
            CombatReturnPolicy combatReturnPolicy,
            Id rotationRef,
            double decisionInterval,
            IReadOnlyDictionary<string, ExprNode> transitions)
        {
            Id = id;
            PerceptionRadius = perceptionRadius;
            PatrolPathRef = patrolPathRef;
            LeashRange = leashRange;
            FleeHpPctThreshold = fleeHpPctThreshold;
            CombatReturnPolicy = combatReturnPolicy;
            RotationRef = rotationRef;
            DecisionInterval = decisionInterval;
            Transitions = transitions;
        }
    }
}
