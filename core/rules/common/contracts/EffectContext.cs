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

        /// <summary>
        /// RC-01 收边补齐：本次结算所属的触发链深度（0 = 由正常施法管线步骤 8/9 产生的"根"结算，
        /// 未经任何 Proc/<c>trigger_spell</c> 触发；N &gt; 0 = 经 N 层触发链产生）。判断记录：
        /// 06 第 3.4/3.6 节只规定"触发链递归深度超过 <see cref="Core.Rules.Skill.SkillOptions.MaxTriggerDepth"/>
        /// 时拒绝"，未规定深度如何在"效果结算落地事件"（<c>combat.damage_dealt</c>/
        /// <c>combat.heal_done</c> 等）经 <see cref="Core.Foundation.EventBus.IEventBus.Enqueue"/> 异步
        /// 派发、再被 <c>ProcHost</c> 处理触发下一层 <c>trigger_spell</c> 这条路径上传播——原实现只在
        /// <c>CastPipeline.TriggerCast</c> 内部维护一个 ambient 计数器，进入自增/退出自减；但事件总线是
        /// "Enqueue 入队 + 下一个 DispatchPending pass 才派发"（见 <c>core/foundation/event_bus</c>
        /// README"同步派发 + tick 末批处理"），当 Proc 由异步派发的事件触发时，产生该事件的那次
        /// <c>TriggerCast</c> 调用早已返回、ambient 计数器已经归零，深度预算形同虚设（见审计
        /// RC-01：无 ICD 的 <c>heal_done → trigger_skill(heal)</c> 可跨 <c>DispatchPending</c> pass
        /// 无限循环，只靠 <see cref="Core.Foundation.EventBus.EventBusOptions.MaxDispatchPasses"/> 这个
        /// 与技能触发链语义无关的全局熔断兜底）。本字段把"触发链深度"从 ambient 状态改为随
        /// 结算输入/输出显式传播的数据：<c>CastPipeline.ExecuteEffectsOnly</c> 用它构造
        /// <see cref="EffectContext"/>，<c>EffectDispatcher.ApplyDamageOrHeal</c> 把它原样转发进
        /// <c>outbound</c> 上下文，<c>Resolver.Resolve</c> 把它戳到落地事件（<c>CombatDamageDealtEvent</c>/
        /// <c>CombatHealDoneEvent</c>）上；<c>ProcHost.OnEvent</c> 从触发它的事件上读回这个深度（见
        /// <c>EventCorrelation.GetTriggerChainDepth</c>），传给下一次 <c>TriggerCast</c> 调用做真正的
        /// 跨 pass 预算判定。未显式传入（绝大多数不涉及触发链的调用点，如周期性光环效果、纯脚本/
        /// 测试构造）时为 0，语义等价于"根结算"，不改变既有行为。
        /// </summary>
        public int TriggerChainDepth { get; }

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
            IReadOnlyList<Id>? tags = null,
            int triggerChainDepth = 0)
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
            TriggerChainDepth = triggerChainDepth;
        }
    }
}
