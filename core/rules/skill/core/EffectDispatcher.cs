using System;
using Core.Foundation.Common;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// <see cref="IEffectSink"/> 实现：19 个 Effect 原语（06 第 3.2 节）的落地出口，按
    /// <see cref="EffectKind"/> 分派（见落地方案 T2-4 必读"效果交给 ICombatHost.ResolveEffect 即时
    /// 结算，由 combat 决定落地时机"）。<c>school_damage</c>/<c>weapon_damage_pct</c>/<c>heal</c>
    /// 三类先做本模块职责内的免疫短路与数值组装（基础值 + 系数 × 缩放属性、SpellMod
    /// <c>effect_value</c>/<c>crit_chance</c> 修正），再交给 <see cref="ICombatHost.ResolveEffect"/>；
    /// 06 第 4.1 节结算管线本身（命中/暴击/减免/吸收/落地/后置）完全是 combat 的职责，本类不重复
    /// 实现。<c>projectile</c>/<c>summon</c>/<c>open_lock</c>/<c>create_item</c>/
    /// <c>set_world_flag</c>/<c>script</c> 六类转交 <see cref="IEffectExtension"/>（见该接口注释）。
    /// </summary>
    public sealed class EffectDispatcher : IEffectSink
    {
        private readonly AuraHost _auraHost;
        private readonly CooldownTracker _cooldowns;
        private readonly SkillDefCache _defs;
        private readonly IPowerHost _powerHost;
        private readonly IUnitAccess _units;
        private readonly ICombatHost _combatHost;
        private readonly IStatHost _statHost;
        private readonly SpellModResolver _spellMods;
        private readonly IEffectExtension? _extension;
        private readonly ISkillDiagnostics _diagnostics;
        private readonly ProcHost.TriggerCastCallback _triggerCast;
        private readonly Action<Id, Id, Id?, double> _interrupt;
        private readonly Action<Id, Id> _learnSkill;

        public EffectDispatcher(
            AuraHost auraHost,
            CooldownTracker cooldowns,
            SkillDefCache defs,
            IPowerHost powerHost,
            IUnitAccess units,
            ICombatHost combatHost,
            IStatHost statHost,
            SpellModResolver spellMods,
            IEffectExtension? extension,
            ISkillDiagnostics diagnostics,
            ProcHost.TriggerCastCallback triggerCast,
            Action<Id, Id, Id?, double> interrupt,
            Action<Id, Id> learnSkill)
        {
            _auraHost = auraHost ?? throw new ArgumentNullException(nameof(auraHost));
            _cooldowns = cooldowns ?? throw new ArgumentNullException(nameof(cooldowns));
            _defs = defs ?? throw new ArgumentNullException(nameof(defs));
            _powerHost = powerHost ?? throw new ArgumentNullException(nameof(powerHost));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _combatHost = combatHost ?? throw new ArgumentNullException(nameof(combatHost));
            _statHost = statHost ?? throw new ArgumentNullException(nameof(statHost));
            _spellMods = spellMods ?? throw new ArgumentNullException(nameof(spellMods));
            _extension = extension;
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _triggerCast = triggerCast ?? throw new ArgumentNullException(nameof(triggerCast));
            _interrupt = interrupt ?? throw new ArgumentNullException(nameof(interrupt));
            _learnSkill = learnSkill ?? throw new ArgumentNullException(nameof(learnSkill));
        }

        public ResolveResult ApplyEffect(EffectContext context)
        {
            switch (context.Kind)
            {
                case EffectKind.SchoolDamage:
                case EffectKind.WeaponDamagePct:
                case EffectKind.Heal:
                    return ApplyDamageOrHeal(context);

                case EffectKind.ApplyAura:
                    return ApplyAuraEffectPrimitive(context);

                case EffectKind.Dispel:
                    return ApplyDispel(context);

                case EffectKind.Energize:
                    return ApplyEnergize(context);

                case EffectKind.TriggerSpell:
                    return ApplyTriggerSpell(context);

                case EffectKind.ModifyCooldown:
                    return ApplyModifyCooldown(context);

                case EffectKind.AddCharge:
                    return ApplyAddCharge(context);

                case EffectKind.Interrupt:
                    return ApplyInterrupt(context);

                case EffectKind.Teleport:
                    return ApplyTeleport(context);

                case EffectKind.Move:
                    return ApplyMove(context);

                case EffectKind.LearnSkill:
                    return ApplyLearnSkill(context);

                case EffectKind.Projectile:
                case EffectKind.Summon:
                case EffectKind.OpenLock:
                case EffectKind.CreateItem:
                case EffectKind.SetWorldFlag:
                case EffectKind.Script:
                    return ApplyExtension(context);

                default:
                    throw new InvalidOperationException($"未知 EffectKind：{context.Kind}");
            }
        }

        public AuraInstanceRef ApplyAura(Id targetId, Id auraDefId, Id sourceId, double? durationOverride = null) =>
            _auraHost.ApplyAura(targetId, auraDefId, sourceId, durationOverride);

        public void RemoveAura(Id targetId, AuraInstanceRef auraInstanceRef) => _auraHost.RemoveAura(targetId, auraInstanceRef);

        // -----------------------------------------------------------------
        // school_damage / weapon_damage_pct / heal
        // -----------------------------------------------------------------

        private ResolveResult ApplyDamageOrHeal(EffectContext context)
        {
            if (_auraHost.IsImmune(context.TargetId, context.School, context.Kind))
            {
                return new ResolveResult(HitResult.Miss, 0, 0, 0, immune: true, isHeal: context.Kind == EffectKind.Heal);
            }

            double value;
            double coefficient = context.Coefficient;

            if (context.Kind == EffectKind.WeaponDamagePct)
            {
                value = ParamsX.GetNumber(context.Params, "pct", context.BaseValue);
            }
            else
            {
                var baseValue = ParamsX.GetNumber(context.Params, "base_value", context.BaseValue);
                coefficient = ParamsX.GetNumber(context.Params, "coefficient", context.Coefficient);
                var scalingStat = ParamsX.GetIdOpt(context.Params, "scaling_stat");
                value = baseValue + (scalingStat.HasValue ? coefficient * _statHost.GetStat(context.SourceId, scalingStat.Value) : 0);
            }

            // 判断记录：EffectContext 不携带技能标签集合，effect_value/crit_chance 维度的
            // SpellMod 过滤在本调用点退化为"按学派/技能 id"匹配（tags 传空列表，SkillFilter.Matches
            // 的标签维度不参与），完整的标签过滤留给 CastPipeline 步骤 9 在真正发起施放时按
            // skill.def.tags 处理（见 CastPipeline 注释），此处只覆盖光环周期效果等脱离施法上下文
            // 直接调用 ApplyEffect 的场景。
            value = _spellMods.Apply(context.SourceId, SpellModDimension.EffectValue, context.SkillId, context.School, Array.Empty<Id>(), value);

            var critBonus = _spellMods.Apply(context.SourceId, SpellModDimension.CritChance, context.SkillId, context.School, Array.Empty<Id>(), 0);
            var mergedParams = critBonus != 0 ? ParamsX.MergeNumber(context.Params, "crit_chance_bonus", critBonus) : context.Params;

            var outbound = new EffectContext(
                context.SourceId, context.TargetId, context.SkillId, context.Kind, context.School,
                value, coefficient, mergedParams, context.AuraInstanceId, context.IsPeriodic, context.CanCrit, context.CanMiss);

            return _combatHost.ResolveEffect(outbound);
        }

        // -----------------------------------------------------------------
        // apply_aura / dispel / energize
        // -----------------------------------------------------------------

        private ResolveResult ApplyAuraEffectPrimitive(EffectContext context)
        {
            var auraDefId = ParamsX.GetId(context.Params, "aura_def", default);
            double? durationOverride = context.Params.ContainsKey("duration_override")
                ? ParamsX.GetNumber(context.Params, "duration_override")
                : (double?)null;

            _auraHost.ApplyAura(context.TargetId, auraDefId, context.SourceId, durationOverride);
            return NoOp(context);
        }

        private ResolveResult ApplyDispel(EffectContext context)
        {
            var category = ParamsX.GetId(context.Params, "category", default);
            var count = (int)ParamsX.GetNumber(context.Params, "count", 1);
            _auraHost.Dispel(context.TargetId, category, count);
            return NoOp(context);
        }

        private ResolveResult ApplyEnergize(EffectContext context)
        {
            var powerType = ParamsX.GetId(context.Params, "power_type", default);
            var amount = ParamsX.GetNumber(context.Params, "amount");
            _powerHost.ModifyPower(context.TargetId, powerType, amount, context.SourceId);
            return NoOp(context);
        }

        // -----------------------------------------------------------------
        // trigger_spell / modify_cooldown / add_charge / interrupt
        // -----------------------------------------------------------------

        private ResolveResult ApplyTriggerSpell(EffectContext context)
        {
            var skillId = ParamsX.GetId(context.Params, "skill_id", default);
            _triggerCast(context.SourceId, skillId, new[] { context.TargetId });
            return NoOp(context);
        }

        private ResolveResult ApplyModifyCooldown(EffectContext context)
        {
            var skillId = ParamsX.GetIdOpt(context.Params, "skill_id");
            var category = ParamsX.GetIdOpt(context.Params, "category");
            var delta = ParamsX.GetNumber(context.Params, "delta");

            if (skillId.HasValue)
            {
                _cooldowns.ModifyCooldown(context.TargetId, skillId.Value, delta, isCategory: false);
            }
            else if (category.HasValue)
            {
                _cooldowns.ModifyCooldown(context.TargetId, category.Value, delta, isCategory: true);
            }
            else
            {
                _diagnostics.Warn("modify_cooldown 效果缺少 skill_id/category 参数，已忽略");
            }

            return NoOp(context);
        }

        private ResolveResult ApplyAddCharge(EffectContext context)
        {
            var skillId = ParamsX.GetId(context.Params, "skill_id", default);
            var amount = (int)ParamsX.GetNumber(context.Params, "amount", 1);

            if (_defs.TryGetSkillDef(skillId, out var def))
            {
                _cooldowns.AddCharge(context.TargetId, def, amount);
            }
            else
            {
                _diagnostics.Warn($"add_charge 效果引用的技能 \"{skillId}\" 不存在，已忽略");
            }

            return NoOp(context);
        }

        private ResolveResult ApplyInterrupt(EffectContext context)
        {
            var lockSchool = ParamsX.GetIdOpt(context.Params, "lock_school");
            var lockDuration = ParamsX.GetNumber(context.Params, "lock_duration");
            _interrupt(context.TargetId, context.SourceId, lockSchool, lockDuration);
            return NoOp(context);
        }

        // -----------------------------------------------------------------
        // move / teleport / learn_skill
        // -----------------------------------------------------------------

        private ResolveResult ApplyTeleport(EffectContext context)
        {
            var point = ParamsX.GetVec2(context.Params, "point", _units.GetPosition(context.TargetId));
            _units.SetPosition(context.TargetId, point);
            return NoOp(context);
        }

        /// <summary>
        /// <c>move</c> 效果原语（见 06 第 3.2 节"子类型 charge|leap|knockback"）。判断记录：本模块
        /// 只写目标最终位置，不做寻路/碰撞——06 原文本节未规定位移的插值/寻路细节，由 L3 移动系统
        /// 在表现层/物理层精化（本模块只保证逻辑位置的最终落点正确，呼应 05 对象模型"移动系统"
        /// 分工）。
        /// </summary>
        private ResolveResult ApplyMove(EffectContext context)
        {
            var mode = ParamsX.GetString(context.Params, "mode", "charge");

            switch (mode)
            {
                case "leap":
                {
                    var point = ParamsX.GetVec2(context.Params, "point", _units.GetPosition(context.SourceId));
                    _units.SetPosition(context.SourceId, point);
                    break;
                }

                case "knockback":
                {
                    var distance = ParamsX.GetNumber(context.Params, "distance", 5);
                    var from = _units.GetPosition(context.SourceId);
                    var to = _units.GetPosition(context.TargetId);
                    var direction = to - from;
                    var length = direction.Length;
                    var normalized = length > 1e-9 ? direction * (1.0 / length) : new Vec2(1, 0);
                    _units.SetPosition(context.TargetId, to + normalized * distance);
                    break;
                }

                case "charge":
                default:
                {
                    var stopDistance = ParamsX.GetNumber(context.Params, "stop_distance", 1.0);
                    var from = _units.GetPosition(context.SourceId);
                    var to = _units.GetPosition(context.TargetId);
                    var direction = to - from;
                    var length = direction.Length;
                    if (length > stopDistance)
                    {
                        var normalized = direction * (1.0 / length);
                        _units.SetPosition(context.SourceId, to - normalized * stopDistance);
                    }

                    break;
                }
            }

            return NoOp(context);
        }

        private ResolveResult ApplyLearnSkill(EffectContext context)
        {
            var skillId = ParamsX.GetId(context.Params, "skill_id", default);
            _learnSkill(context.TargetId, skillId);
            return NoOp(context);
        }

        // -----------------------------------------------------------------
        // 扩展点：projectile / summon / open_lock / create_item / set_world_flag / script
        // -----------------------------------------------------------------

        private ResolveResult ApplyExtension(EffectContext context)
        {
            if (_extension != null && _extension.TryHandle(context, out var result))
            {
                return result;
            }

            _diagnostics.Warn(
                $"EffectKind.{context.Kind} 未被 IEffectExtension 处理（见 core/rules/skill/README.md" +
                " \"六类效果原语委托扩展点\"），已按未处理返回");
            return NoOp(context);
        }

        private static ResolveResult NoOp(EffectContext context) =>
            new ResolveResult(HitResult.Hit, 0, 0, 0, immune: false, isHeal: context.Kind == EffectKind.Heal);
    }
}
