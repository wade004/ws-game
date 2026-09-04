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

        public bool HasCharges => ChargesMax.HasValue;

        public SkillDef(
            Id id, Id school, bool isPassive, double range, IReadOnlyList<Id> tags,
            double castTime, double channelTime, IReadOnlyList<(Id, double)> cost,
            Id? cooldownCategory, double cooldownDuration, int? chargesMax, double chargesRechargeTime,
            bool respectsGcd, Id targetShapeRef, IReadOnlyList<EffectRef> effects, InterruptFlags interruptFlags)
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
