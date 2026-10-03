using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;

namespace Core.Rules.Common
{
    // 手感体系新增事件（手感设计/01 第 3.7 节、02 第 5 节、03 第 2.4/3/7 节；ADR-0114/0115/0117）。
    // 全部登记在 found.event_catalog，key 常量在 RulesEventKeys。本文件只定义契约（事件类型），
    // 发布方由后续切片（动作时间线、时间线命中、顿帧、受击裁决）实现。
    //
    // 判断记录：时间线动作的施法实例 id 与攻击实例 id 都用 Id（沿 CombatDamageDealtEvent.AttackInstanceId 既有惯例）。
    // action.marker 的 args 用字符串映射承载，数值参数以不变文化的文本给出，避免事件载荷依赖异构值类型。
    // 这些事件不实现 ITriggerChainEvent：手感事件不作为 Proc 触发源（需要时由后续 ADR 追加，加法兼容）。

    /// <summary><c>action.started</c>：时间线开始推进（与 <c>skill.cast_start</c> 同批发出）。</summary>
    public sealed class ActionStartedEvent : IEvent
    {
        public Id Key => RulesEventKeys.ActionStarted;

        public Id ActorId { get; }

        public Id SkillId { get; }

        public Id CastInstanceId { get; }

        public int ComboIndex { get; }

        /// <summary>动作总时长（tick，三相之和，已含速率重映射）。</summary>
        public int DurationTicks { get; }

        /// <summary>蓄力比例 0～1（非蓄力动作为 0）。</summary>
        public double ChargeRatio { get; }

        /// <summary>
        /// 动作是否带攻击（含伤害类/投射物效果，或声明了 <c>hit</c>/<c>release</c> 标记）。反馈侧据此决定是否为本动作开挥空窗口
        /// （手感设计/07 第 6 节）：闪避等不带攻击的动作没有"打空"。既有 6 参数构造（物理签名不变）取缺省真。
        /// </summary>
        public bool IsAttack { get; }

        public ActionStartedEvent(Id actorId, Id skillId, Id castInstanceId, int comboIndex, int durationTicks, double chargeRatio)
            : this(actorId, skillId, castInstanceId, comboIndex, durationTicks, chargeRatio, true)
        {
        }

        public ActionStartedEvent(
            Id actorId, Id skillId, Id castInstanceId, int comboIndex, int durationTicks, double chargeRatio, bool isAttack)
        {
            ActorId = actorId;
            SkillId = skillId;
            CastInstanceId = castInstanceId;
            ComboIndex = comboIndex;
            DurationTicks = durationTicks;
            ChargeRatio = chargeRatio;
            IsAttack = isAttack;
        }
    }

    /// <summary><c>action.phase_changed</c>：相位切换。</summary>
    public sealed class ActionPhaseChangedEvent : IEvent
    {
        public Id Key => RulesEventKeys.ActionPhaseChanged;

        public Id ActorId { get; }

        public Id CastInstanceId { get; }

        public ActionPhase Phase { get; }

        public ActionPhaseChangedEvent(Id actorId, Id castInstanceId, ActionPhase phase)
        {
            ActorId = actorId;
            CastInstanceId = castInstanceId;
            Phase = phase;
        }
    }

    /// <summary><c>action.marker</c>：判定类时间标记到达（<c>hit</c>、<c>release</c>、<c>invuln_start</c> 等）。</summary>
    public sealed class ActionMarkerEvent : IEvent
    {
        public Id Key => RulesEventKeys.ActionMarker;

        public Id ActorId { get; }

        public Id CastInstanceId { get; }

        /// <summary>标记名（手感设计/01 第 3.3 节）。</summary>
        public string Name { get; }

        /// <summary>标记参数（如 <c>segment</c>），无参数为空映射。</summary>
        public IReadOnlyDictionary<string, string> Args { get; }

        public ActionMarkerEvent(Id actorId, Id castInstanceId, string name, IReadOnlyDictionary<string, string>? args = null)
        {
            ActorId = actorId;
            CastInstanceId = castInstanceId;
            Name = name;
            Args = args ?? new Dictionary<string, string>();
        }
    }

    /// <summary><c>action.cancelled</c>：动作未自然结束即终止。</summary>
    public sealed class ActionCancelledEvent : IEvent
    {
        public Id Key => RulesEventKeys.ActionCancelled;

        public Id ActorId { get; }

        public Id CastInstanceId { get; }

        public ActionCancelReason Reason { get; }

        /// <summary><see cref="ActionCancelReason.CancelInto"/> 时接续的新技能，其它原因为 null。</summary>
        public Id? NextSkillId { get; }

        public ActionCancelledEvent(Id actorId, Id castInstanceId, ActionCancelReason reason, Id? nextSkillId = null)
        {
            ActorId = actorId;
            CastInstanceId = castInstanceId;
            Reason = reason;
            NextSkillId = nextSkillId;
        }
    }

    /// <summary><c>action.finished</c>：后摇自然结束（与 <c>skill.cast_success</c> 同批）。</summary>
    public sealed class ActionFinishedEvent : IEvent
    {
        public Id Key => RulesEventKeys.ActionFinished;

        public Id ActorId { get; }

        public Id CastInstanceId { get; }

        public ActionFinishedEvent(Id actorId, Id castInstanceId)
        {
            ActorId = actorId;
            CastInstanceId = castInstanceId;
        }
    }

    /// <summary><c>action.target_assisted</c>：目标辅助生效（手感设计/02 第 5 节），没有候选时不发。</summary>
    public sealed class ActionTargetAssistedEvent : IEvent
    {
        public Id Key => RulesEventKeys.ActionTargetAssisted;

        public Id ActorId { get; }

        public Id CastInstanceId { get; }

        public Id TargetId { get; }

        /// <summary>朝向修正量（度，带符号）。</summary>
        public double FacingDelta { get; }

        /// <summary>位移距离修正量（世界单位，<c>close_distance</c> 模式；<c>face_only</c> 为 0）。</summary>
        public double DistanceAdjust { get; }

        public ActionTargetAssistedEvent(Id actorId, Id castInstanceId, Id targetId, double facingDelta, double distanceAdjust)
        {
            ActorId = actorId;
            CastInstanceId = castInstanceId;
            TargetId = targetId;
            FacingDelta = facingDelta;
            DistanceAdjust = distanceAdjust;
        }
    }

    /// <summary><c>action.projectile_launched</c>：时间线动作发射了一发投射物（手感设计/03 第 2.5 节），反馈侧据此把挥空判定推迟到投射物结局。</summary>
    public sealed class ActionProjectileLaunchedEvent : IEvent
    {
        public Id Key => RulesEventKeys.ActionProjectileLaunched;

        public Id ActorId { get; }

        public Id CastInstanceId { get; }

        /// <summary>多段技能的段序号（单段为 0）。</summary>
        public int Segment { get; }

        public ActionProjectileLaunchedEvent(Id actorId, Id castInstanceId, int segment)
        {
            ActorId = actorId;
            CastInstanceId = castInstanceId;
            Segment = segment;
        }
    }

    /// <summary>
    /// <c>action.projectile_ended</c>：时间线动作发射的投射物有了结局（命中后销毁、被地形挡住、射程耗尽/到期、被清场）。
    /// 与 <see cref="ActionProjectileLaunchedEvent"/> 一一配对；穿透命中的投射物只在最终销毁时发一次。
    /// </summary>
    public sealed class ActionProjectileEndedEvent : IEvent
    {
        public Id Key => RulesEventKeys.ActionProjectileEnded;

        public Id ActorId { get; }

        public Id CastInstanceId { get; }

        public int Segment { get; }

        public ProjectileEndReason Reason { get; }

        public ActionProjectileEndedEvent(Id actorId, Id castInstanceId, int segment, ProjectileEndReason reason)
        {
            ActorId = actorId;
            CastInstanceId = castInstanceId;
            Segment = segment;
            Reason = reason;
        }
    }

    /// <summary>
    /// <c>combat.hit_confirmed</c>（手感设计/03 第 2.4 节）：一次命中的完整结论，两种结算模式统一发出，紧随同一批次的
    /// <c>combat.damage_dealt</c>/<c>combat.attack_avoided</c> 之后。回避类结局也发（<c>Amount = 0</c>、<c>Reaction = None</c>）。
    /// 几何字段（接触点、法线、世界方向）永不为空（无接触几何时由适配层给出替代值）。
    /// </summary>
    public sealed class CombatHitConfirmedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => RulesEventKeys.CombatHitConfirmed;

        public Id AttackInstanceId { get; }

        /// <summary>多段技能的段序号（单段为 0）。</summary>
        public int Segment { get; }

        public Id SourceId { get; }

        public Id TargetId { get; }

        public Id? SkillId { get; }

        public HitResult HitResult { get; }

        public double Amount { get; }

        /// <summary><c>Amount</c> / 目标最大生命，供反馈强度缩放。</summary>
        public double AmountRatio { get; }

        public bool IsCrit { get; }

        public bool IsKill { get; }

        public Vec2 ContactPoint { get; }

        /// <summary>从目标中心指向接触点的单位向量。</summary>
        public Vec2 ContactNormal { get; }

        /// <summary>攻击方到目标的单位向量（击退方向）。</summary>
        public Vec2 WorldDirection { get; }

        /// <summary>冲击等级（<c>light|medium|heavy|massive</c>，可扩），来自解析后手感表受击组。</summary>
        public string ImpactClass { get; }

        public int AttackerHitStopTicks { get; }

        public int TargetHitStopTicks { get; }

        public HitReaction Reaction { get; }

        /// <summary>
        /// 发起本次命中的动作/施法实例 id（<c>action.started.castInstanceId</c> 同一个值）；时间线空间命中填入，
        /// instant（目标选择式）命中没有动作实例，为 null。同一次动作实例的多次命中（多段、多目标）共用它，
        /// 而 <see cref="AttackInstanceId"/> 是每次结算一个（沿用既有"一次效果结算批次一个"语义）。
        /// 打击反馈的"挥空"窗口据此按动作实例配对，而不是按行动者配对（见 feedback_binder README）。
        /// </summary>
        public Id? CastInstanceId { get; }

        public CombatHitConfirmedEvent(
            Id attackInstanceId, int segment, Id sourceId, Id targetId, Id? skillId, HitResult hitResult,
            double amount, double amountRatio, bool isCrit, bool isKill,
            Vec2 contactPoint, Vec2 contactNormal, Vec2 worldDirection,
            string impactClass, int attackerHitStopTicks, int targetHitStopTicks, HitReaction reaction)
            : this(
                attackInstanceId, segment, sourceId, targetId, skillId, hitResult, amount, amountRatio, isCrit, isKill,
                contactPoint, contactNormal, worldDirection, impactClass, attackerHitStopTicks, targetHitStopTicks, reaction,
                castInstanceId: null)
        {
        }

        public CombatHitConfirmedEvent(
            Id attackInstanceId, int segment, Id sourceId, Id targetId, Id? skillId, HitResult hitResult,
            double amount, double amountRatio, bool isCrit, bool isKill,
            Vec2 contactPoint, Vec2 contactNormal, Vec2 worldDirection,
            string impactClass, int attackerHitStopTicks, int targetHitStopTicks, HitReaction reaction,
            Id? castInstanceId)
        {
            CastInstanceId = castInstanceId;
            AttackInstanceId = attackInstanceId;
            Segment = segment;
            SourceId = sourceId;
            TargetId = targetId;
            SkillId = skillId;
            HitResult = hitResult;
            Amount = amount;
            AmountRatio = amountRatio;
            IsCrit = isCrit;
            IsKill = isKill;
            ContactPoint = contactPoint;
            ContactNormal = contactNormal;
            WorldDirection = worldDirection;
            ImpactClass = impactClass;
            AttackerHitStopTicks = attackerHitStopTicks;
            TargetHitStopTicks = targetHitStopTicks;
            Reaction = reaction;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "sourceId": value = ExprValue.OfId(SourceId); return true;
                case "attackInstanceId": value = ExprValue.OfId(AttackInstanceId); return true;
                case "targetId": value = ExprValue.OfId(TargetId); return true;
                case "skillId" when SkillId.HasValue: value = ExprValue.OfId(SkillId.Value); return true;
                case "hitResult": value = ExprValue.OfString(HitResult.ToString()); return true;
                case "amount": value = ExprValue.OfNumber(Amount); return true;
                case "amountRatio": value = ExprValue.OfNumber(AmountRatio); return true;
                case "isCrit": value = ExprValue.OfBool(IsCrit); return true;
                case "isKill": value = ExprValue.OfBool(IsKill); return true;
                case "impactClass": value = ExprValue.OfString(ImpactClass); return true;
                case "reaction": value = ExprValue.OfString(Reaction.ToString()); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>
    /// <c>combat.poise_changed</c>（手感落地 M4-L，动态韧性）：一次声明了 <c>poise_damage</c> 的命中扣减了目标的韧性池。
    /// <see cref="Before"/>/<see cref="After"/> 是该命中前后的有效韧性（目标韧性属性减去已损失量，不低于 0），<see cref="Max"/> 是韧性属性值，<see cref="Damage"/> 是这一击声明的韧性伤害；
    /// <see cref="Broken"/> 为真表示这一击把韧性从正数打到 0（破韧：该命中按冲击等级映射出完整受击反应，不再被韧性挡成 Flinch）。
    /// 不声明 <c>poise_damage</c> 的命中（静态韧性）不发本事件。
    /// </summary>
    public sealed class CombatPoiseChangedEvent : IEvent
    {
        public Id Key => RulesEventKeys.CombatPoiseChanged;

        public Id TargetId { get; }

        public Id SourceId { get; }

        public double Before { get; }

        public double After { get; }

        public double Max { get; }

        /// <summary>这一击声明的韧性伤害（<c>poise_damage</c>，标定后）；实际扣掉的量是 <c>Before − After</c>（池子不够时更少）。</summary>
        public double Damage { get; }

        public bool Broken { get; }

        public CombatPoiseChangedEvent(Id targetId, Id sourceId, double before, double after, double max, double damage, bool broken)
        {
            TargetId = targetId;
            SourceId = sourceId;
            Before = before;
            After = after;
            Max = max;
            Damage = damage;
            Broken = broken;
        }
    }

    /// <summary>
    /// <c>combat.poise_recovered</c>（手感落地 M4-L）：目标的动态韧性经 <c>poise_recover_per_s</c> 回复到满（<see cref="Poise"/> 为回满后的有效韧性）。
    /// 只在"之前有损失、现在回满"的那一 tick 发一次。
    /// </summary>
    public sealed class CombatPoiseRecoveredEvent : IEvent
    {
        public Id Key => RulesEventKeys.CombatPoiseRecovered;

        public Id TargetId { get; }

        public double Poise { get; }

        public CombatPoiseRecoveredEvent(Id targetId, double poise)
        {
            TargetId = targetId;
            Poise = poise;
        }
    }

    /// <summary><c>combat.reaction_applied</c>：受击裁决落地（<see cref="HitReaction.None"/> 不发）。</summary>
    public sealed class CombatReactionAppliedEvent : IEvent
    {
        public Id Key => RulesEventKeys.CombatReactionApplied;

        public Id TargetId { get; }

        public HitReaction Reaction { get; }

        public Id SourceId { get; }

        public Id AttackInstanceId { get; }

        /// <summary>反应持续时长（tick）：硬直为 <c>hit_stun_ms</c> 换算值，倒地含 <c>downed_ms</c>。</summary>
        public int DurationTicks { get; }

        public CombatReactionAppliedEvent(Id targetId, HitReaction reaction, Id sourceId, Id attackInstanceId, int durationTicks)
        {
            TargetId = targetId;
            Reaction = reaction;
            SourceId = sourceId;
            AttackInstanceId = attackInstanceId;
            DurationTicks = durationTicks;
        }
    }

    /// <summary><c>feel.hitstop_started</c>：局部顿帧施加（冻结行动者动作时钟）。</summary>
    public sealed class FeelHitstopStartedEvent : IEvent
    {
        public Id Key => RulesEventKeys.FeelHitstopStarted;

        /// <summary>被冻结的单位（攻击方与受击方）。</summary>
        public IReadOnlyList<Id> UnitIds { get; }

        public int Ticks { get; }

        public Id AttackInstanceId { get; }

        public FeelHitstopStartedEvent(IReadOnlyList<Id> unitIds, int ticks, Id attackInstanceId)
        {
            UnitIds = unitIds;
            Ticks = ticks;
            AttackInstanceId = attackInstanceId;
        }
    }

    /// <summary><c>feel.hitstop_ended</c>：顿帧解冻或强制释放。</summary>
    public sealed class FeelHitstopEndedEvent : IEvent
    {
        public Id Key => RulesEventKeys.FeelHitstopEnded;

        public IReadOnlyList<Id> UnitIds { get; }

        public FeelHitstopEndedEvent(IReadOnlyList<Id> unitIds)
        {
            UnitIds = unitIds;
        }
    }
    /// <summary>
    /// <c>feel.weapon_changed</c>（手感设计/08 第 1 节换装链）：单位的主手武器手感引用、副手武器手感引用或武器族发生变化，
    /// 且手感解析器已失效重算（<see cref="FeelVersion"/> 是重算后的新版本号）。发布方是换装链装配
    /// （<c>Core.Carriers.Item.EquipmentFeelChain</c>）；订阅方按需刷新姿势家族、反馈变体、界面。
    /// 武器引用为 null 表示该手空手。
    /// </summary>
    public sealed class FeelWeaponChangedEvent : IEvent
    {
        public Id Key => RulesEventKeys.FeelWeaponChanged;

        public Id UnitId { get; }

        public string? PreviousMainRef { get; }

        public string? MainRef { get; }

        public string? OffhandRef { get; }

        /// <summary>变化前的武器族（空手为 null）。</summary>
        public string? PreviousFamily { get; }

        /// <summary>变化后的武器族（空手或武器行没有族为 null）。</summary>
        public string? Family { get; }

        /// <summary>重算后的手感解析版本号。</summary>
        public int FeelVersion { get; }

        public FeelWeaponChangedEvent(
            Id unitId, string? previousMainRef, string? mainRef, string? offhandRef, string? previousFamily, string? family, int feelVersion)
        {
            UnitId = unitId;
            PreviousMainRef = previousMainRef;
            MainRef = mainRef;
            OffhandRef = offhandRef;
            PreviousFamily = previousFamily;
            Family = family;
            FeelVersion = feelVersion;
        }
    }
}
