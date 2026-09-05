using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;

namespace Core.Rules.Common
{
    /// <summary>
    /// L2 四模块共用的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见 06 第 8 节事件词汇表、
    /// data/_sample/found/found.event_catalog.json 对应行）。惯例同 <c>PowerEventKeys</c>/
    /// <c>StatBlockEventKeys</c>/<c>FactionEventKeys</c>：模块自持一份常量，不依赖生成物。
    /// <para>
    /// 判断记录：06 第 8 节正文只登记 <c>skill</c>/<c>aura</c>/<c>combat</c>/<c>unit</c>/<c>ai</c> 五个
    /// domain（<c>proc.triggered</c> 的 domain 是 skill，见该行说明）；<c>targeting.resolved</c> 与
    /// <c>ai.decision_made</c> 只出现在 01_分层与依赖.md 模块表与 found.event_catalog.json 的"建议"行，
    /// 且 06 第 5 节原文明确写"目标选择本身不发事件"——与事件目录的建议行存在冲突。任务书"必读"清单
    /// 明确要求读取 <c>targeting.resolved</c> 行字段，且 targeting 模块要与 ai/skill 通过事件协作
    /// 离不开这一事件，因此本文件仍把 <see cref="TargetingResolvedEvent"/>/<see cref="AiDecisionMadeEvent"/>
    /// 一并登记为强类型事件，供后续模块选用；若后续设计层认定 06 第 5 节"不发事件"为最终结论，
    /// 删除这两个类型即可，不影响其余事件。
    /// </para>
    /// </summary>
    public static class RulesEventKeys
    {
        public static readonly Id SkillCastStart = new Id("skill.cast_start");
        public static readonly Id SkillCastSuccess = new Id("skill.cast_success");
        public static readonly Id SkillCastFailed = new Id("skill.cast_failed");
        public static readonly Id SkillCastInterrupted = new Id("skill.cast_interrupted");

        public static readonly Id CombatDamageDealt = new Id("combat.damage_dealt");
        public static readonly Id CombatHealDone = new Id("combat.heal_done");
        public static readonly Id CombatThreatChanged = new Id("combat.threat_changed");
        public static readonly Id CombatEntered = new Id("combat.entered");
        public static readonly Id CombatLeft = new Id("combat.left");

        public static readonly Id AuraApplied = new Id("aura.applied");
        public static readonly Id AuraRemoved = new Id("aura.removed");
        public static readonly Id AuraStackChanged = new Id("aura.stack_changed");

        public static readonly Id ProcTriggered = new Id("proc.triggered");

        public static readonly Id UnitDied = new Id("unit.died");
        public static readonly Id UnitRespawned = new Id("unit.respawned");

        public static readonly Id AiStateChanged = new Id("ai.state_changed");

        /// <summary>建议行，见本类型上方判断记录。</summary>
        public static readonly Id AiDecisionMade = new Id("ai.decision_made");

        /// <summary>建议行，见本类型上方判断记录。</summary>
        public static readonly Id TargetingResolved = new Id("targeting.resolved");
    }

    /// <summary>死亡复活策略（见 06 第 4.6 节表格，三值）。<see cref="UnitRespawnedEvent.Policy"/> 用本
    /// 枚举而非裸字符串，判断记录：06 原文把三种策略列成固定表格（非游戏层可扩展的开放集合），与
    /// <see cref="CastFailureReason"/>/<see cref="HitResult"/> 等其它"文档给出固定有限枚举"的字段
    /// 处理方式一致，优于事件目录建议字段表里的裸 <c>policy</c> 字符串。</summary>
    public enum RespawnPolicy
    {
        RespawnPoint,
        ReloadSave,
        Permadeath,
    }

    /// <summary>施法管线步骤 8 开始（见 06 第 8 节）。</summary>
    public sealed class SkillCastStartEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.SkillCastStart;

        public Id CasterId { get; }

        public Id SkillId { get; }

        public double CastTime { get; }

        public SkillCastStartEvent(Id casterId, Id skillId, double castTime)
        {
            CasterId = casterId;
            SkillId = skillId;
            CastTime = castTime;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "casterId": value = ExprValue.OfId(CasterId); return true;
                case "skillId": value = ExprValue.OfId(SkillId); return true;
                case "castTime": value = ExprValue.OfNumber(CastTime); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>施法管线步骤 9 完成（见 06 第 8 节）。</summary>
    public sealed class SkillCastSuccessEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.SkillCastSuccess;

        public Id CasterId { get; }

        public Id SkillId { get; }

        public IReadOnlyList<Id> Targets { get; }

        public SkillCastSuccessEvent(Id casterId, Id skillId, IReadOnlyList<Id> targets)
        {
            CasterId = casterId;
            SkillId = skillId;
            Targets = (targets ?? Array.Empty<Id>()).ToArray();
        }

        /// <summary><see cref="Targets"/> 是列表，Expr 无列表类型（见
        /// <see cref="IExprReadableEvent"/> 类型注释），不在本方法覆盖范围内，查询 "targets"
        /// 返回 false。</summary>
        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "casterId": value = ExprValue.OfId(CasterId); return true;
                case "skillId": value = ExprValue.OfId(SkillId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>施法管线任一步骤失败（见 06 第 8 节）。</summary>
    public sealed class SkillCastFailedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.SkillCastFailed;

        public Id CasterId { get; }

        public Id SkillId { get; }

        public CastFailureReason ReasonCode { get; }

        public SkillCastFailedEvent(Id casterId, Id skillId, CastFailureReason reasonCode)
        {
            CasterId = casterId;
            SkillId = skillId;
            ReasonCode = reasonCode;
        }

        /// <summary>枚举字段按 <c>ToString()</c> 落地为 String（见 <see cref="IExprReadableEvent"/>
        /// 类型注释判断记录）。</summary>
        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "casterId": value = ExprValue.OfId(CasterId); return true;
                case "skillId": value = ExprValue.OfId(SkillId); return true;
                case "reasonCode": value = ExprValue.OfString(ReasonCode.ToString()); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>读条/引导被打断（见 06 第 8 节）。</summary>
    public sealed class SkillCastInterruptedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.SkillCastInterrupted;

        public Id CasterId { get; }

        public Id SkillId { get; }

        public Id InterrupterId { get; }

        public SkillCastInterruptedEvent(Id casterId, Id skillId, Id interrupterId)
        {
            CasterId = casterId;
            SkillId = skillId;
            InterrupterId = interrupterId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "casterId": value = ExprValue.OfId(CasterId); return true;
                case "skillId": value = ExprValue.OfId(SkillId); return true;
                case "interrupterId": value = ExprValue.OfId(InterrupterId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>结算管线"落地"步骤，伤害类效果（见 06 第 8 节）。</summary>
    public sealed class CombatDamageDealtEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.CombatDamageDealt;

        public Id SourceId { get; }

        public Id TargetId { get; }

        public Id School { get; }

        public double Amount { get; }

        public bool IsCrit { get; }

        public HitResult HitResult { get; }

        public CombatDamageDealtEvent(Id sourceId, Id targetId, Id school, double amount, bool isCrit, HitResult hitResult)
        {
            SourceId = sourceId;
            TargetId = targetId;
            School = school;
            Amount = amount;
            IsCrit = isCrit;
            HitResult = hitResult;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "sourceId": value = ExprValue.OfId(SourceId); return true;
                case "targetId": value = ExprValue.OfId(TargetId); return true;
                case "school": value = ExprValue.OfId(School); return true;
                case "amount": value = ExprValue.OfNumber(Amount); return true;
                case "isCrit": value = ExprValue.OfBool(IsCrit); return true;
                case "hitResult": value = ExprValue.OfString(HitResult.ToString()); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>结算管线"落地"步骤，治疗类效果（见 06 第 8 节）。</summary>
    public sealed class CombatHealDoneEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.CombatHealDone;

        public Id SourceId { get; }

        public Id TargetId { get; }

        public double Amount { get; }

        public bool IsCrit { get; }

        public CombatHealDoneEvent(Id sourceId, Id targetId, double amount, bool isCrit)
        {
            SourceId = sourceId;
            TargetId = targetId;
            Amount = amount;
            IsCrit = isCrit;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "sourceId": value = ExprValue.OfId(SourceId); return true;
                case "targetId": value = ExprValue.OfId(TargetId); return true;
                case "amount": value = ExprValue.OfNumber(Amount); return true;
                case "isCrit": value = ExprValue.OfBool(IsCrit); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary><c>apply_aura</c> 生效（见 06 第 8 节）。</summary>
    public sealed class AuraAppliedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.AuraApplied;

        public Id TargetId { get; }

        public Id AuraDefId { get; }

        public Id SourceId { get; }

        public int Stacks { get; }

        public AuraAppliedEvent(Id targetId, Id auraDefId, Id sourceId, int stacks)
        {
            TargetId = targetId;
            AuraDefId = auraDefId;
            SourceId = sourceId;
            Stacks = stacks;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "targetId": value = ExprValue.OfId(TargetId); return true;
                case "auraDefId": value = ExprValue.OfId(AuraDefId); return true;
                case "sourceId": value = ExprValue.OfId(SourceId); return true;
                case "stacks": value = ExprValue.OfInt(Stacks); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>光环到期/驱散/覆盖移除（见 06 第 8 节）。<see cref="Reason"/> 是自由文本分类
    /// （如 "expired"/"dispelled"/"overwritten"），06 未给出固定枚举，保留字符串。</summary>
    public sealed class AuraRemovedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.AuraRemoved;

        public Id TargetId { get; }

        public Id AuraDefId { get; }

        public string Reason { get; }

        public AuraRemovedEvent(Id targetId, Id auraDefId, string reason)
        {
            TargetId = targetId;
            AuraDefId = auraDefId;
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "targetId": value = ExprValue.OfId(TargetId); return true;
                case "auraDefId": value = ExprValue.OfId(AuraDefId); return true;
                case "reason": value = ExprValue.OfString(Reason); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>叠加层数变化（见 06 第 8 节）。</summary>
    public sealed class AuraStackChangedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.AuraStackChanged;

        public Id TargetId { get; }

        public Id AuraDefId { get; }

        public int OldStacks { get; }

        public int NewStacks { get; }

        public AuraStackChangedEvent(Id targetId, Id auraDefId, int oldStacks, int newStacks)
        {
            TargetId = targetId;
            AuraDefId = auraDefId;
            OldStacks = oldStacks;
            NewStacks = newStacks;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "targetId": value = ExprValue.OfId(TargetId); return true;
                case "auraDefId": value = ExprValue.OfId(AuraDefId); return true;
                case "oldStacks": value = ExprValue.OfInt(OldStacks); return true;
                case "newStacks": value = ExprValue.OfInt(NewStacks); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>Proc 命中触发条件（见 06 第 8 节；domain 为 skill，见 <see cref="RulesEventKeys"/> 说明）。</summary>
    public sealed class ProcTriggeredEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.ProcTriggered;

        public Id UnitId { get; }

        public Id ProcDefId { get; }

        public Id TriggerSkillId { get; }

        public ProcTriggeredEvent(Id unitId, Id procDefId, Id triggerSkillId)
        {
            UnitId = unitId;
            ProcDefId = procDefId;
            TriggerSkillId = triggerSkillId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "procDefId": value = ExprValue.OfId(ProcDefId); return true;
                case "triggerSkillId": value = ExprValue.OfId(TriggerSkillId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>仇恨表更新（见 06 第 8 节）。</summary>
    public sealed class CombatThreatChangedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.CombatThreatChanged;

        public Id UnitId { get; }

        public Id SourceId { get; }

        public double OldValue { get; }

        public double NewValue { get; }

        public CombatThreatChangedEvent(Id unitId, Id sourceId, double oldValue, double newValue)
        {
            UnitId = unitId;
            SourceId = sourceId;
            OldValue = oldValue;
            NewValue = newValue;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "sourceId": value = ExprValue.OfId(SourceId); return true;
                case "oldValue": value = ExprValue.OfNumber(OldValue); return true;
                case "newValue": value = ExprValue.OfNumber(NewValue); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>进入战斗（见 06 第 8 节）。</summary>
    public sealed class CombatEnteredEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.CombatEntered;

        public Id UnitId { get; }

        /// <summary>
        /// 收边任务补齐 <c>found.event_catalog</c> 的 <c>combat.entered</c> 行（首个敌对目标，见
        /// 06 第 8 节事件词汇表勘误）：本次触发进战的交互对方 id，由
        /// <see cref="Core.Rules.Common.ICombatHost.NotifyCombatEvent"/> 在该单位真正"首次"进战
        /// 时填充（见该方法参数注释）。判断记录（不做阵营敌对判定）：字段名沿用文档"首个敌对
        /// 目标"，但 <c>core/rules/combat</c> 的 <c>Resolver</c> 在造成伤害/治疗后统一
        /// 对结算双方各调用一次 <c>NotifyCombatEvent(自己, 对方)</c>——不区分本次结算是伤害还是
        /// 治疗，也不查询 <see cref="Core.Numbers.Faction.IFactionMatrix"/> 二次确认对方是否确实
        /// 敌对：06 文档未规定"首个敌对目标"需要额外的阵营校验，本字段的语义收敛为"触发本次
        /// 进战判定的交互对方"，与"进战"本身的既有触发条件（造成/受到伤害、被仇恨表记录）完全
        /// 复用同一对 <c>(sourceId, targetId)</c>，不引入新的判定分支。可空：环境触发的进战（如
        /// <c>SummonTickHandler</c> 同步召唤物进战状态）未传交互对方时为 null。
        /// </summary>
        public Id? HostileId { get; }

        public CombatEnteredEvent(Id unitId, Id? hostileId = null)
        {
            UnitId = unitId;
            HostileId = hostileId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "hostileId" when HostileId.HasValue: value = ExprValue.OfId(HostileId.Value); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>脱离战斗（见 06 第 8 节）。</summary>
    public sealed class CombatLeftEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.CombatLeft;

        public Id UnitId { get; }

        public CombatLeftEvent(Id unitId)
        {
            UnitId = unitId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>死亡结算完成（见 06 第 8 节）。<see cref="KillerId"/> 判断记录：环境死亡（跌落、脚本
    /// 赐死等无明确攻击者的场景）不存在"击杀者"，06 原文字段表未标注是否可空，本类型放宽为可空以
    /// 覆盖这类场景；有明确攻击者时正常传入。</summary>
    public sealed class UnitDiedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.UnitDied;

        public Id UnitId { get; }

        public Id? KillerId { get; }

        public UnitDiedEvent(Id unitId, Id? killerId)
        {
            UnitId = unitId;
            KillerId = killerId;
        }

        /// <summary><see cref="KillerId"/> 可空（环境死亡无击杀者，见本类型上方判断记录）：为
        /// null 时查询 "killerId" 返回 false（字段逻辑缺失），交由 <c>event</c> 分组的宿主实现按
        /// "字段缺失 → 默认值 + 警告"统一处理（见 <see cref="IExprReadableEvent"/> 类型注释）。</summary>
        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "killerId" when KillerId.HasValue: value = ExprValue.OfId(KillerId.Value); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>按死亡复活策略处理完成（见 06 第 8 节）。</summary>
    public sealed class UnitRespawnedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.UnitRespawned;

        public Id UnitId { get; }

        public RespawnPolicy Policy { get; }

        public UnitRespawnedEvent(Id unitId, RespawnPolicy policy)
        {
            UnitId = unitId;
            Policy = policy;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "policy": value = ExprValue.OfString(Policy.ToString()); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>行为外壳状态机切换（见 06 第 6、8 节）。</summary>
    public sealed class AiStateChangedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.AiStateChanged;

        public Id UnitId { get; }

        public BehaviorState OldState { get; }

        public BehaviorState NewState { get; }

        public AiStateChangedEvent(Id unitId, BehaviorState oldState, BehaviorState newState)
        {
            UnitId = unitId;
            OldState = oldState;
            NewState = newState;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "oldState": value = ExprValue.OfString(OldState.ToString()); return true;
                case "newState": value = ExprValue.OfString(NewState.ToString()); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>建议行：AI 按优先级表选出本次决策后触发（见 01 L2 模块表 ai 行）。</summary>
    public sealed class AiDecisionMadeEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.AiDecisionMade;

        public Id UnitId { get; }

        public Id DecisionId { get; }

        public AiDecisionMadeEvent(Id unitId, Id decisionId)
        {
            UnitId = unitId;
            DecisionId = decisionId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "decisionId": value = ExprValue.OfId(DecisionId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>建议行：目标解析策略链求解完成后触发（见 01 L2 模块表 targeting 行；与 06 第 5 节
    /// "目标选择本身不发事件"的冲突见 <see cref="RulesEventKeys"/> 上方判断记录）。</summary>
    public sealed class TargetingResolvedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.TargetingResolved;

        public Id UnitId { get; }

        public Id ChainId { get; }

        public IReadOnlyList<Id> TargetIds { get; }

        public TargetingResolvedEvent(Id unitId, Id chainId, IReadOnlyList<Id> targetIds)
        {
            UnitId = unitId;
            ChainId = chainId;
            TargetIds = (targetIds ?? Array.Empty<Id>()).ToArray();
        }

        /// <summary><see cref="TargetIds"/> 是列表，不在覆盖范围内（同
        /// <see cref="SkillCastSuccessEvent.Targets"/>，见 <see cref="IExprReadableEvent"/>
        /// 类型注释）。</summary>
        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "chainId": value = ExprValue.OfId(ChainId); return true;
                default: value = default; return false;
            }
        }
    }
}
