using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary><c>skill.def.interrupt_flags</c> 的强类型标志位组合（见 06 第 3.1 节
    /// "interrupt_flags: List&lt;movement|damage_taken|...&gt;"，任务书拍板补齐第三项
    /// <c>control</c>，见 <see cref="SkillSchemas.InterruptFlagValues"/>）。</summary>
    [Flags]
    public enum InterruptFlags
    {
        None = 0,
        Movement = 1 << 0,
        DamageTaken = 1 << 1,
        Control = 1 << 2,
    }

    /// <summary>解析后的 <c>skill.def</c> 记录（字段见 06 第 3.1 节、schema/README.md）。</summary>
    public sealed class SkillDef
    {
        public Id Id { get; }
        public Id School { get; }
        public bool IsPassive { get; }
        public double Range { get; }
        public IReadOnlyList<Id> Tags { get; }
        public double CastTime { get; }
        public double ChannelTime { get; }
        public IReadOnlyList<(Id PowerType, double Amount)> Cost { get; }
        public Id? CooldownCategory { get; }
        public double CooldownDuration { get; }
        public int? ChargesMax { get; }
        public double ChargesRechargeTime { get; }
        public bool RespectsGcd { get; }
        public Id TargetShapeRef { get; }
        public IReadOnlyList<EffectRef> Effects { get; }
        public InterruptFlags InterruptFlags { get; }

        /// <summary>离散模式下释放本技能消耗的行动点数量（06 第 3.1 节 <c>action_cost</c>）；
        /// 连续模式忽略本字段。未声明时为 0（不消耗行动点），与"该字段可选"的文档语义一致。
        /// W1 收边补齐：此前 <see cref="SkillDefCache"/> 只把该字段登记进 schema 供数据校验通过，
        /// 从未解析进 <see cref="SkillDef"/>，<see cref="CastPipeline"/> 也从未消费——离散模式下
        /// 技能可无限连续释放，见 A3 审计 #5。</summary>
        public double ActionCost { get; }

        /// <summary>
        /// ADR-0027《地面坐标施法请求》补充：本技能是否允许经
        /// <see cref="Core.Rules.Common.ISkillHost.CastSkillAtGround"/> 以地面坐标为落点施放
        /// （<c>skill.def.ground_target</c>，见 schema/README.md）。缺省 <c>false</c>——保持既有
        /// 技能（本字段落地前登记的全部 <c>skill.def</c> 行）行为不变，地面坐标是新增能力，需要
        /// 内容作者显式声明才生效，不是"只要技能有射程/形状就自动允许"。
        /// </summary>
        public bool AllowGroundTarget { get; }

        /// <summary>
        /// T-N3-4（ADR-0031 决策 9，06 第 3.1 节 2026-09-14 修订）：使用条件（<c>skill.def.use_condition</c>，
        /// 宿主为施法者上下文），解析自 T-N3-1 已登记的 <c>FieldKind.Expr</c> 原始文本（见
        /// <see cref="SkillDefCache"/> 解析判断记录，与 <see cref="ProcDef.Condition"/> 同一套
        /// <c>ExprParser.Parse</c> 惯例）。<c>null</c> 表示未声明——施法管线步骤 1.5 直接放行，不产生
        /// 任何行为变化（呼应本字段落地前登记的全部既有 <c>skill.def</c> 行）。
        /// </summary>
        public ExprNode? UseCondition { get; }

        /// <summary>
        /// 消费方反馈第 3 条（2026-09-20，ADR-0048）：技能名称文本键（<c>skill.def.name_key</c>），
        /// 命名/字段类型沿用 ADR-0043 <c>dialog.gossip_menu.GreetingKey</c> 的 TextKey 惯例。
        /// <c>null</c> 表示未声明——本字段落地前登记的全部既有 <c>skill.def</c> 行均无此字段，解析为
        /// <c>null</c>，与"未声明"语义一致，不产生任何行为变化；消费方（<c>ActionBarViewModel</c>/
        /// <c>ActionBarPanel</c>）在 <c>null</c> 时不渲染名称，不回退占位文案（同 <c>greeting_key</c>
        /// 判断记录，ADR-0043"后果"一节）。
        /// </summary>
        public Id? NameKey { get; }

        public bool HasCharges => ChargesMax.HasValue;

        public SkillDef(
            Id id, Id school, bool isPassive, double range, IReadOnlyList<Id> tags,
            double castTime, double channelTime, IReadOnlyList<(Id, double)> cost,
            Id? cooldownCategory, double cooldownDuration, int? chargesMax, double chargesRechargeTime,
            bool respectsGcd, Id targetShapeRef, IReadOnlyList<EffectRef> effects, InterruptFlags interruptFlags,
            double actionCost = 0)
        {
            Id = id;
            School = school;
            IsPassive = isPassive;
            Range = range;
            Tags = tags;
            CastTime = castTime;
            ChannelTime = channelTime;
            Cost = cost;
            CooldownCategory = cooldownCategory;
            CooldownDuration = cooldownDuration;
            ChargesMax = chargesMax;
            ChargesRechargeTime = chargesRechargeTime;
            RespectsGcd = respectsGcd;
            TargetShapeRef = targetShapeRef;
            Effects = effects;
            InterruptFlags = interruptFlags;
            ActionCost = actionCost;
            AllowGroundTarget = false;
            UseCondition = null;
            NameKey = null;
        }

        /// <summary>
        /// ADR-0027 新增重载：携带 <see cref="AllowGroundTarget"/>。判断记录（不是给既有构造函数的
        /// <c>actionCost</c> 之后再加一个可选参数）：同 <see cref="Core.Rules.Common.EffectContext"/>
        /// 十六参数重载判断记录同一套 ABI 兼容惯例——既有构造函数追加参数会改变其物理 IL 签名；本
        /// 重载十八个参数全部不带默认值，与既有构造函数（16 个必填 + <c>actionCost</c> 最多 17 个）
        /// 参数个数不重叠，互不冲突。
        /// </summary>
        public SkillDef(
            Id id, Id school, bool isPassive, double range, IReadOnlyList<Id> tags,
            double castTime, double channelTime, IReadOnlyList<(Id, double)> cost,
            Id? cooldownCategory, double cooldownDuration, int? chargesMax, double chargesRechargeTime,
            bool respectsGcd, Id targetShapeRef, IReadOnlyList<EffectRef> effects, InterruptFlags interruptFlags,
            double actionCost, bool allowGroundTarget)
        {
            Id = id;
            School = school;
            IsPassive = isPassive;
            Range = range;
            Tags = tags;
            CastTime = castTime;
            ChannelTime = channelTime;
            Cost = cost;
            CooldownCategory = cooldownCategory;
            CooldownDuration = cooldownDuration;
            ChargesMax = chargesMax;
            ChargesRechargeTime = chargesRechargeTime;
            RespectsGcd = respectsGcd;
            TargetShapeRef = targetShapeRef;
            Effects = effects;
            InterruptFlags = interruptFlags;
            ActionCost = actionCost;
            AllowGroundTarget = allowGroundTarget;
            UseCondition = null;
            NameKey = null;
        }

        /// <summary>
        /// T-N3-4 新增重载：携带 <see cref="UseCondition"/>。判断记录（不是给上一个构造函数的
        /// <c>allowGroundTarget</c> 之后再加一个可选参数）：同上方 ADR-0027 十八参数重载判断记录同一套
        /// ABI 兼容惯例——既有构造函数追加参数会改变其物理 IL 签名；本重载十九个参数全部不带默认值，
        /// 与既有两个构造函数（分别最多 17、恰好 18 个参数）参数个数不重叠，互不冲突。
        /// </summary>
        public SkillDef(
            Id id, Id school, bool isPassive, double range, IReadOnlyList<Id> tags,
            double castTime, double channelTime, IReadOnlyList<(Id, double)> cost,
            Id? cooldownCategory, double cooldownDuration, int? chargesMax, double chargesRechargeTime,
            bool respectsGcd, Id targetShapeRef, IReadOnlyList<EffectRef> effects, InterruptFlags interruptFlags,
            double actionCost, bool allowGroundTarget, ExprNode? useCondition)
        {
            Id = id;
            School = school;
            IsPassive = isPassive;
            Range = range;
            Tags = tags;
            CastTime = castTime;
            ChannelTime = channelTime;
            Cost = cost;
            CooldownCategory = cooldownCategory;
            CooldownDuration = cooldownDuration;
            ChargesMax = chargesMax;
            ChargesRechargeTime = chargesRechargeTime;
            RespectsGcd = respectsGcd;
            TargetShapeRef = targetShapeRef;
            Effects = effects;
            InterruptFlags = interruptFlags;
            ActionCost = actionCost;
            AllowGroundTarget = allowGroundTarget;
            UseCondition = useCondition;
            NameKey = null;
        }

        /// <summary>
        /// 消费方反馈第 3 条新增重载（2026-09-20，ADR-0048）：携带 <see cref="NameKey"/>。判断记录
        /// （不是给上一个构造函数的 <c>useCondition</c> 之后再加一个可选参数）：同上方两处重载判断
        /// 记录同一套 ABI 兼容惯例——既有构造函数追加参数会改变其物理 IL 签名；本重载二十个参数
        /// 全部不带默认值，与既有三个构造函数（分别最多 17、恰好 18、恰好 19 个参数）参数个数不
        /// 重叠，互不冲突。
        /// </summary>
        public SkillDef(
            Id id, Id school, bool isPassive, double range, IReadOnlyList<Id> tags,
            double castTime, double channelTime, IReadOnlyList<(Id, double)> cost,
            Id? cooldownCategory, double cooldownDuration, int? chargesMax, double chargesRechargeTime,
            bool respectsGcd, Id targetShapeRef, IReadOnlyList<EffectRef> effects, InterruptFlags interruptFlags,
            double actionCost, bool allowGroundTarget, ExprNode? useCondition, Id? nameKey)
        {
            Id = id;
            School = school;
            IsPassive = isPassive;
            Range = range;
            Tags = tags;
            CastTime = castTime;
            ChannelTime = channelTime;
            Cost = cost;
            CooldownCategory = cooldownCategory;
            CooldownDuration = cooldownDuration;
            ChargesMax = chargesMax;
            ChargesRechargeTime = chargesRechargeTime;
            RespectsGcd = respectsGcd;
            TargetShapeRef = targetShapeRef;
            Effects = effects;
            InterruptFlags = interruptFlags;
            ActionCost = actionCost;
            AllowGroundTarget = allowGroundTarget;
            UseCondition = useCondition;
            NameKey = nameKey;
        }
    }

    /// <summary><c>skill.aura_def.effects[]</c> 一项（见 06 第 3.3 节）。</summary>
    public readonly struct AuraEffectEntry
    {
        public AuraEffectKind Kind { get; }
        public JsonObject Params { get; }

        public AuraEffectEntry(AuraEffectKind kind, JsonObject @params)
        {
            Kind = kind;
            Params = @params;
        }
    }

    /// <summary>解析后的 <c>skill.aura_def</c> 记录（字段见 06 第 3.3 节）。</summary>
    public sealed class AuraDef
    {
        public Id Id { get; }
        public double? Duration { get; }
        public int MaxStacks { get; }
        public Id? StackCategory { get; }
        public Id? DispelType { get; }
        public IReadOnlyList<AuraEffectEntry> Effects { get; }

        public AuraDef(Id id, double? duration, int maxStacks, Id? stackCategory, Id? dispelType, IReadOnlyList<AuraEffectEntry> effects)
        {
            Id = id;
            Duration = duration;
            MaxStacks = maxStacks <= 0 ? 1 : maxStacks;
            StackCategory = stackCategory;
            DispelType = dispelType;
            Effects = effects;
        }
    }

    /// <summary>解析后的 <c>skill.proc_def</c> 记录（字段见 06 第 3.4 节）。</summary>
    public sealed class ProcDef
    {
        public Id Id { get; }
        public Id TriggerEvent { get; }
        public ExprNode? Condition { get; }
        public Id TriggerSkill { get; }
        public double? InternalCooldown { get; }
        public double ProcChance { get; }

        public ProcDef(Id id, Id triggerEvent, ExprNode? condition, Id triggerSkill, double? internalCooldown, double procChance)
        {
            Id = id;
            TriggerEvent = triggerEvent;
            Condition = condition;
            TriggerSkill = triggerSkill;
            InternalCooldown = internalCooldown;
            ProcChance = procChance;
        }
    }

    /// <summary>解析后的 <c>skill.spell_mod_def</c> 记录（字段见 06 第 3.5 节）。</summary>
    public sealed class SpellModDefRecord
    {
        public Id Id { get; }
        public SpellModDimension TargetDimension { get; }
        public SpellModOp Op { get; }
        public double Value { get; }
        public SkillFilter Affects { get; }

        public SpellModDefRecord(Id id, SpellModDimension targetDimension, SpellModOp op, double value, SkillFilter affects)
        {
            Id = id;
            TargetDimension = targetDimension;
            Op = op;
            Value = value;
            Affects = affects;
        }
    }

    /// <summary>解析后的 <c>skill.book</c> 记录（见 06 第 3.1 节表清单 skill.book 行、04 第 1.1 节）。</summary>
    public sealed class SkillBookDef
    {
        public Id Id { get; }
        public IReadOnlyList<(int Level, Id SkillId)> Entries { get; }

        public SkillBookDef(Id id, IReadOnlyList<(int, Id)> entries)
        {
            Id = id;
            Entries = entries;
        }
    }
}
