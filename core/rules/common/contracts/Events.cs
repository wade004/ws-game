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

        /// <summary>
        /// N19 收边补齐（外部审计 68c9bed，P2）：本次施法是否为瞬发（<c>cast_time == 0</c> 且非
        /// 引导，见 <see cref="Core.Rules.Skill.CastPipeline.EnterCastOrChannel"/> 判断记录）。瞬发时
        /// <c>skill.cast_start</c> 与本事件在同一次 <see cref="Core.Foundation.EventBus.IEventBus.Enqueue"/>
        /// 批次内背靠背发出（步骤 8 立即完成，不经历任何 <see cref="Update"/> tick），逻辑结算本身
        /// 没有问题（两个事件仍然都发出、顺序仍然是 start 先于 success，语义不变）——但纯粹只订阅
        /// <c>cast.succeeded</c>/<c>cast.started</c> 事件对来驱动动画状态机的表现层消费方（见
        /// 09_表现层.md、外部审计 N19）无法单独从这两个事件本身分辨"这是瞬发，来得及播完整个
        /// Attack 播放形态"还是"这是一次真正读条完成，应该立即回到 Idle"，容易在瞬发时把角色的
        /// 攻击播放形态在同一帧内又切回 Idle（读条播放形态与逻辑结算独立这一 09 表现层原则不需要
        /// 靠本字段保证——本字段只是让表现层能做出正确判断的必要信息，具体如何据此驱动状态机仍是
        /// 表现层职责，不在本次写入范围）。</summary>
        public bool IsInstant { get; }

        /// <summary>见 <see cref="IsInstant"/> 判断记录：本次施法实际读条/引导花费的秒数——瞬发为 0；
        /// 引导/读条类为 <see cref="Core.Rules.Common.SkillCastStartEvent.CastTime"/> 在本次施法开始
        /// 时携带的同一个值（引导为 <c>channel_time</c>，读条为 <c>cast_time</c>，均已按当前
        /// <c>SpellMod</c> 修正）。</summary>
        public double CastTimeSeconds { get; }

        public SkillCastSuccessEvent(Id casterId, Id skillId, IReadOnlyList<Id> targets, bool isInstant = false, double castTimeSeconds = 0)
        {
            CasterId = casterId;
            SkillId = skillId;
            Targets = (targets ?? Array.Empty<Id>()).ToArray();
            IsInstant = isInstant;
            CastTimeSeconds = castTimeSeconds;
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
                case "isInstant": value = ExprValue.OfBool(IsInstant); return true;
                case "castTimeSeconds": value = ExprValue.OfNumber(CastTimeSeconds); return true;
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
    public sealed class CombatDamageDealtEvent : IEvent, IExprReadableEvent, ITriggerChainEvent
    {
        public Id Key => RulesEventKeys.CombatDamageDealt;

        public Id SourceId { get; }

        public Id TargetId { get; }

        public Id School { get; }

        public double Amount { get; }

        public bool IsCrit { get; }

        public HitResult HitResult { get; }

        /// <summary>见 <see cref="ITriggerChainEvent"/>/<see cref="EffectContext.TriggerChainDepth"/>
        /// 类型注释（RC-01）：产生本次伤害结算的 <c>EffectContext.TriggerChainDepth</c> 原样戳到
        /// 事件上，供 <c>ProcHost</c> 在本事件是某个 <c>skill.proc_def.trigger_event</c> 时读回，
        /// 作为下一层 <c>TriggerCast</c> 调用的深度预算输入。未显式传入时为 0（根结算）。</summary>
        public int TriggerChainDepth { get; }

        public CombatDamageDealtEvent(Id sourceId, Id targetId, Id school, double amount, bool isCrit, HitResult hitResult, int triggerChainDepth = 0)
        {
            SourceId = sourceId;
            TargetId = targetId;
            School = school;
            Amount = amount;
            IsCrit = isCrit;
            HitResult = hitResult;
            TriggerChainDepth = triggerChainDepth;
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
                case "triggerChainDepth": value = ExprValue.OfInt(TriggerChainDepth); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>结算管线"落地"步骤，治疗类效果（见 06 第 8 节）。</summary>
    public sealed class CombatHealDoneEvent : IEvent, IExprReadableEvent, ITriggerChainEvent
    {
        public Id Key => RulesEventKeys.CombatHealDone;

        public Id SourceId { get; }

        public Id TargetId { get; }

        public double Amount { get; }

        public bool IsCrit { get; }

        /// <summary>见 <see cref="CombatDamageDealtEvent.TriggerChainDepth"/> 判断记录（RC-01），
        /// 同一套机制的治疗分支——审计报告点名的 <c>heal_done → trigger_skill(heal)</c> 无 ICD
        /// 自循环正是靠本字段跨 <see cref="Core.Foundation.EventBus.IEventBus.Enqueue"/> 派发 pass
        /// 传播深度后才被 <see cref="Core.Rules.Skill.SkillOptions.MaxTriggerDepth"/> 正确截断。</summary>
        public int TriggerChainDepth { get; }

        public CombatHealDoneEvent(Id sourceId, Id targetId, double amount, bool isCrit, int triggerChainDepth = 0)
        {
            SourceId = sourceId;
            TargetId = targetId;
            Amount = amount;
            IsCrit = isCrit;
            TriggerChainDepth = triggerChainDepth;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "sourceId": value = ExprValue.OfId(SourceId); return true;
                case "targetId": value = ExprValue.OfId(TargetId); return true;
                case "amount": value = ExprValue.OfNumber(Amount); return true;
                case "isCrit": value = ExprValue.OfBool(IsCrit); return true;
                case "triggerChainDepth": value = ExprValue.OfInt(TriggerChainDepth); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary><c>apply_aura</c> 生效（见 06 第 8 节）。</summary>
    public sealed class AuraAppliedEvent : IEvent, IExprReadableEvent, ITriggerChainEvent
    {
        public Id Key => RulesEventKeys.AuraApplied;

        public Id TargetId { get; }

        public Id AuraDefId { get; }

        public Id SourceId { get; }

        public int Stacks { get; }

        /// <summary>见 <see cref="CombatDamageDealtEvent.TriggerChainDepth"/> 判断记录（RC-01）。
        /// 只有经 <c>skill.def.effects</c> 的 <c>apply_aura</c> 原语（<c>EffectDispatcher.
        /// ApplyAuraEffectPrimitive</c>）施加时才带上产生它的 <see cref="EffectContext.TriggerChainDepth"/>；
        /// 其余调用点（装备 grants、种族被动等不经过 <see cref="EffectContext"/> 的直接施加）为 0。</summary>
        public int TriggerChainDepth { get; }

        public AuraAppliedEvent(Id targetId, Id auraDefId, Id sourceId, int stacks, int triggerChainDepth = 0)
        {
            TargetId = targetId;
            AuraDefId = auraDefId;
            SourceId = sourceId;
            Stacks = stacks;
            TriggerChainDepth = triggerChainDepth;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "targetId": value = ExprValue.OfId(TargetId); return true;
                case "auraDefId": value = ExprValue.OfId(AuraDefId); return true;
                case "sourceId": value = ExprValue.OfId(SourceId); return true;
                case "stacks": value = ExprValue.OfInt(Stacks); return true;
                case "triggerChainDepth": value = ExprValue.OfInt(TriggerChainDepth); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>光环到期/驱散/覆盖移除（见 06 第 8 节）。<see cref="Reason"/> 是自由文本分类
    /// （如 "expired"/"dispelled"/"overwritten"），06 未给出固定枚举，保留字符串。
    /// <para>
    /// N04 收边补齐（外部审计 68c9bed）：本事件此前不实现 <see cref="ITriggerChainEvent"/>，
    /// <see cref="Core.Rules.Skill.EventCorrelation.GetTriggerChainDepth"/> 对其恒取默认值 0——
    /// 一个订阅 <c>aura.removed</c> 的永久 Proc（<c>procChance=1</c>、无 ICD）在"施法 Apply 临时
    /// 光环再 Dispel"的循环里，每一层 Dispel 产生的 <c>aura.removed</c> 都被当作"根事件"，
    /// <see cref="SkillOptions.MaxTriggerDepth"/> 从未生效，循环不会在预算处被拒。现在与
    /// <see cref="AuraAppliedEvent.TriggerChainDepth"/> 同一套机制：只有经
    /// <see cref="Core.Rules.Skill.EffectDispatcher"/> 效果结算路径（<c>dispel</c> 效果原语、
    /// <c>apply_aura</c> 叠加溢出替换）产生的移除才带上产生它的
    /// <see cref="EffectContext.TriggerChainDepth"/>；<see cref="Core.Rules.Skill.AuraHost.Update"/>
    /// 到期（<c>reason == "expired"</c>）与 <c>entity.destroyed</c> 联动清理不经过任何
    /// <see cref="EffectContext"/>，恒为 0（视为新的根事件，与到期前的行为一致，不阻断到期继续作为
    /// Proc 触发源）。</para>
    /// </summary>
    public sealed class AuraRemovedEvent : IEvent, IExprReadableEvent, ITriggerChainEvent
    {
        public Id Key => RulesEventKeys.AuraRemoved;

        public Id TargetId { get; }

        public Id AuraDefId { get; }

        public string Reason { get; }

        /// <summary>见本类型注释"N04 收边补齐"、<see cref="CombatDamageDealtEvent.TriggerChainDepth"/>
        /// 判断记录（RC-01）。</summary>
        public int TriggerChainDepth { get; }

        public AuraRemovedEvent(Id targetId, Id auraDefId, string reason, int triggerChainDepth = 0)
        {
            TargetId = targetId;
            AuraDefId = auraDefId;
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
            TriggerChainDepth = triggerChainDepth;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "targetId": value = ExprValue.OfId(TargetId); return true;
                case "auraDefId": value = ExprValue.OfId(AuraDefId); return true;
                case "reason": value = ExprValue.OfString(Reason); return true;
                case "triggerChainDepth": value = ExprValue.OfInt(TriggerChainDepth); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>叠加层数变化（见 06 第 8 节）。
    /// <para>
    /// N04 收边补齐（外部审计 68c9bed，审查范围内的同类事件）：与 <see cref="AuraRemovedEvent"/>
    /// 同一套判断记录——本事件只在 <see cref="Core.Rules.Skill.AuraHost.ReapplyExisting"/>（叠加
    /// 而非溢出替换分支）产生，该路径经 <c>apply_aura</c> 效果原语调用，同样带有
    /// <see cref="EffectContext.TriggerChainDepth"/>，现予以透传，避免同一类"事件驱动 Proc 自循环"
    /// 缺口以另一个事件类型重现。</para>
    /// </summary>
    public sealed class AuraStackChangedEvent : IEvent, IExprReadableEvent, ITriggerChainEvent
    {
        public Id Key => RulesEventKeys.AuraStackChanged;

        public Id TargetId { get; }

        public Id AuraDefId { get; }

        public int OldStacks { get; }

        public int NewStacks { get; }

        /// <summary>见本类型注释"N04 收边补齐"、<see cref="CombatDamageDealtEvent.TriggerChainDepth"/>
        /// 判断记录（RC-01）。</summary>
        public int TriggerChainDepth { get; }

        public AuraStackChangedEvent(Id targetId, Id auraDefId, int oldStacks, int newStacks, int triggerChainDepth = 0)
        {
            TargetId = targetId;
            AuraDefId = auraDefId;
            OldStacks = oldStacks;
            NewStacks = newStacks;
            TriggerChainDepth = triggerChainDepth;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "targetId": value = ExprValue.OfId(TargetId); return true;
                case "auraDefId": value = ExprValue.OfId(AuraDefId); return true;
                case "oldStacks": value = ExprValue.OfInt(OldStacks); return true;
                case "newStacks": value = ExprValue.OfInt(NewStacks); return true;
                case "triggerChainDepth": value = ExprValue.OfInt(TriggerChainDepth); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>Proc 命中触发条件（见 06 第 8 节；domain 为 skill，见 <see cref="RulesEventKeys"/> 说明）。</summary>
    public sealed class ProcTriggeredEvent : IEvent, IExprReadableEvent, ITriggerChainEvent
    {
        public Id Key => RulesEventKeys.ProcTriggered;

        public Id UnitId { get; }

        public Id ProcDefId { get; }

        public Id TriggerSkillId { get; }

        /// <summary>见 <see cref="CombatDamageDealtEvent.TriggerChainDepth"/> 判断记录（RC-01）：
        /// 本次触发实际执行时的深度（即触发它的事件深度 + 1），使"proc 触发 proc"这类以
        /// <c>proc.triggered</c> 本身作为 <c>trigger_event</c> 的链路也能正确传播预算，不需要
        /// 单独一套判定逻辑。</summary>
        public int TriggerChainDepth { get; }

        public ProcTriggeredEvent(Id unitId, Id procDefId, Id triggerSkillId, int triggerChainDepth = 0)
        {
            UnitId = unitId;
            ProcDefId = procDefId;
            TriggerSkillId = triggerSkillId;
            TriggerChainDepth = triggerChainDepth;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "procDefId": value = ExprValue.OfId(ProcDefId); return true;
                case "triggerSkillId": value = ExprValue.OfId(TriggerSkillId); return true;
                case "triggerChainDepth": value = ExprValue.OfInt(TriggerChainDepth); return true;
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
    /// 覆盖这类场景；有明确攻击者时正常传入。
    /// <para>
    /// W1 收边补齐（拍板 3、A3 审计 #2 前置）：新增 <see cref="MapId"/>/<see cref="Position"/> 两个
    /// 字段（06 第 8 节只给出 <c>unitId, killerId</c>，本次追加是为 L4 死亡复活执行主体
    /// （<c>core/gameplay/death.DeathPolicyHost</c>，见拍板 3）铺垫——<c>respawn_point</c> 策略需要
    /// 知道死亡单位所在地图才能取 <c>spawn_points[0]</c>；死亡坐标供未来"最近复活点"一类更精细的
    /// 策略使用。二者都来自 <see cref="IUnitAccess.GetMapId"/>/<see cref="IUnitAccess.GetPosition"/>
    /// 在死亡结算那一刻的快照（单位死亡后仍留在世界中，实时查询本可行，但事件里带一份快照更贴近
    /// "死亡结算完成"这一时刻的真相，避免消费者拿到的是"之后又移动过的位置"）。<see cref="MapId"/>
    /// 同 <see cref="IUnitAccess.GetMapId"/> 一样可空（未接入地图概念的调用方，如部分测试假实现，
    /// 恒返回 null）；<see cref="Position"/>（<c>Vec2</c>）未在 <see cref="IExprReadableEvent"/>
    /// 暴露——<c>ExprValue</c> 没有向量类型，强行拆成 <c>positionX</c>/<c>positionY</c> 两个 Expr
    /// key 超出本次收边范围，需要用坐标做 Expr 判断的场景待未来有真实需求再补；本字段仍以强类型
    /// C# 属性对外（<c>Subscribe&lt;UnitDiedEvent&gt;</c> 的订阅方可直接读取，如
    /// <c>DeathPolicyHost</c>）。
    /// </para>
    /// </summary>
    public sealed class UnitDiedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.UnitDied;

        public Id UnitId { get; }

        public Id? KillerId { get; }

        /// <summary>死亡单位所属地图 id 快照，见类型注释。</summary>
        public Id? MapId { get; }

        /// <summary>死亡单位的世界坐标快照，见类型注释。</summary>
        public Vec2 Position { get; }

        public UnitDiedEvent(Id unitId, Id? killerId, Id? mapId = null, Vec2 position = default)
        {
            UnitId = unitId;
            KillerId = killerId;
            MapId = mapId;
            Position = position;
        }

        /// <summary><see cref="KillerId"/>/<see cref="MapId"/> 均可空（环境死亡无击杀者、部分调用方
        /// 未接入地图概念，见本类型上方判断记录）：为 null 时对应字段查询返回 false（字段逻辑
        /// 缺失），交由 <c>event</c> 分组的宿主实现按"字段缺失 → 默认值 + 警告"统一处理（见
        /// <see cref="IExprReadableEvent"/> 类型注释）。</summary>
        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "killerId" when KillerId.HasValue: value = ExprValue.OfId(KillerId.Value); return true;
                case "mapId" when MapId.HasValue: value = ExprValue.OfId(MapId.Value); return true;
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
