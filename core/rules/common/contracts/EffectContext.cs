using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;

namespace Core.Rules.Common
{
    /// <summary>
    /// 结算管线的输入（见 06 第 4.1 节"每一步只依赖上一步的输出与只读的 StatHost/PowerHost 查询，
    /// 不允许跨步骤回填修改，保证同种子同输入同结果"）。不可变：全部成员为构造期设定的只读属性，
    /// 没有任何方法可以在构造后修改本实例——结算管线的确定性要求（拍板决策 3）落到类型层面就是
    /// "输入对象本身不能变"。
    /// </summary>
    public sealed class EffectContext
    {
        private static readonly JsonObject EmptyParams = new JsonObjectBuilder().Build();
        private static readonly IReadOnlyList<Id> EmptyTags = Array.Empty<Id>();

        public Id SourceId { get; }

        public Id TargetId { get; }

        public Id SkillId { get; }

        public EffectKind Kind { get; }

        /// <summary>学派（见 06 第 3.1 节 <c>school</c>，用于免疫、抗性、护甲曲线按学派区分）。</summary>
        public Id School { get; }

        public double BaseValue { get; }

        public double Coefficient { get; }

        public JsonObject Params { get; }

        /// <summary>周期效果（<c>periodic_damage</c>/<c>periodic_heal</c>）来源的光环实例 id；
        /// 非周期效果为 null。</summary>
        public Id? AuraInstanceId { get; }

        /// <summary>是否为光环周期性触发（区别于技能效果的一次性结算）。</summary>
        public bool IsPeriodic { get; }

        /// <summary>本次结算是否参与暴击判定（见 06 第 4.2 节命中表 <c>crit</c> 行）。</summary>
        public bool CanCrit { get; }

        /// <summary>本次结算是否参与命中判定（部分效果如治疗、周期伤害可豁免未命中判定）。</summary>
        public bool CanMiss { get; }

        /// <summary>
        /// 集成任务补齐的契约缺口：本次结算所属技能的标签集合（来自 <c>skill.def.tags</c>，见
        /// 06 第 3.1 节），供 <see cref="EffectDispatcher"/> 在应用 <c>effect_value</c>/
        /// <c>crit_chance</c> 维度的 SpellMod（见 <see cref="SkillFilter.Matches"/>）时按标签维度
        /// 过滤——原契约的 <see cref="EffectContext"/> 不携带标签，调用点只能传空列表，标签维度的
        /// SpellMod 过滤在效果落地这一步完全不生效（见 <c>core/rules/skill</c> 模块
        /// <c>EffectDispatcher.ApplyDamageOrHeal</c> 改动前的判断记录）。未提供时为空列表（不是
        /// null），周期性光环效果等脱离具体 <c>skill.def</c> 上下文的调用点可以继续不传。
        /// </summary>
        public IReadOnlyList<Id> Tags { get; }

        public EffectContext(
            Id sourceId,
            Id targetId,
            Id skillId,
            EffectKind kind,
            Id school,
            double baseValue,
            double coefficient,
            JsonObject? @params = null,
            Id? auraInstanceId = null,
            bool isPeriodic = false,
            bool canCrit = true,
            bool canMiss = true,
            IReadOnlyList<Id>? tags = null)
        {
            SourceId = sourceId;
            TargetId = targetId;
            SkillId = skillId;
            Kind = kind;
            School = school;
            BaseValue = baseValue;
            Coefficient = coefficient;
            Params = @params ?? EmptyParams;
            AuraInstanceId = auraInstanceId;
            IsPeriodic = isPeriodic;
            CanCrit = canCrit;
            CanMiss = canMiss;
            Tags = tags ?? EmptyTags;
        }
    }
}
