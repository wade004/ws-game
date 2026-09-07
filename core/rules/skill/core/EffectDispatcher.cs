using System;
using System.Collections.Generic;
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
    /// 实现。<c>projectile</c> 收边任务补齐后转交 <see cref="IProjectileSpawner"/>（见该接口判断
    /// 记录"依赖倒置"，专用依赖倒置接口而非六合一扩展点，理由见该接口注释）；<c>summon</c>/
    /// <c>open_lock</c>/<c>create_item</c>/<c>set_world_flag</c>/<c>script</c> 五类转交
    /// <see cref="IEffectExtension"/>（见该接口注释）。
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
        private readonly IProjectileSpawner? _projectileSpawner;
        private readonly IWeaponDamageQuery? _weaponDamageQuery;
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
            Action<Id, Id> learnSkill,
            IProjectileSpawner? projectileSpawner = null,
            IWeaponDamageQuery? weaponDamageQuery = null)
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
            _projectileSpawner = projectileSpawner;
            _weaponDamageQuery = weaponDamageQuery;
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
                    return ApplyProjectile(context);

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

        // 判断记录：本方法实现 IEffectSink.ApplyAura（接口签名不带 tags，见该接口判断记录），
        // 恒不传标签（AuraHost.ApplyAura 的 tags 参数缺省 null）——真正携带标签的施加路径是
        // ApplyAuraEffectPrimitive 直接调用 AuraHost.ApplyAura 的重载，不经过本方法。
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
                // RC-11 收边勘误：pct 是"武器基础伤害的百分比"（06 第 3.2 节），原实现把 params.pct
                // 本身直接当成落地基础值，从未真正读取过武器伤害——换装不改变该技能伤害，等价于
                // weaponBase 恒为 1（见外部审计 RC-11）。现在经 IWeaponDamageQuery（依赖倒置，见该
                // 接口判断记录）取施法者当前武器基础伤害（damage_min/damage_max 均值，无武器为 0，
                // 见该接口方法注释"判断记录"）再乘以 pct。
                var pct = ParamsX.GetNumber(context.Params, "pct", context.BaseValue);
                var weaponBase = _weaponDamageQuery?.GetWeaponBaseDamage(context.SourceId) ?? 0.0;
                value = weaponBase * pct;
            }
            else
            {
                var baseValue = ParamsX.GetNumber(context.Params, "base_value", context.BaseValue);
                coefficient = ParamsX.GetNumber(context.Params, "coefficient", context.Coefficient);
                var scalingStat = ParamsX.GetIdOpt(context.Params, "scaling_stat");

                // C02 收口（外部审计 7e63d66 第四轮）：周期性效果（periodic_damage/periodic_heal）
                // 的 EffectContext.SourceId 恒是施加光环时的施法者（AuraHost.FirePeriodic 每次都
                // 用 instance.SourceId 重建上下文），真实 CreatureFactory.Despawn 会同步注销来源的
                // IStatHost 注册（见 CreatureFactory.Despawn），但光环实例只在"目标"被销毁时才由
                // AuraHost.OnEntityDestroyed 摘除（判断记录：来源销毁不代表已施加到其他目标身上的
                // 光环应当消失，06 未规定这种情形）——来源销毁后光环仍会继续按周期结算，此前
                // 无条件调用 _statHost.GetStat(context.SourceId, ...) 会因来源已注销直接抛
                // InvalidOperationException（外部审计复现：真实 Despawn 施法者后下一次周期 tick）。
                // <para>
                // 判断记录（降级为无缩放，不做"快照冻结"）：来源销毁后，"这份周期效果本该按来源
                // 死前哪个时刻的属性值继续缩放"没有唯一正确答案（06 未规定），而"来源已经不存在，
                // 缩放属性无从查起"是明确可判定的边界条件——参照 AuraHost.OnEntityDestroyed 判断
                // 记录同一惯例（面对未规定的情形选择"确定安全"的退化路径，不是引入一整套额外的
                // 按实例快照基础设施），来源未注册时这部分周期效果的缩放贡献按 0 处理（只保留
                // base_value，不含来源属性加成），效果本身继续正常结算/落地，不中断周期 tick 循环、
                // 不抛异常。非周期效果的 SourceId 通常是"正在执行的施法者"，理论上不会遇到这种
                // 情形，本次改动同时覆盖它是为了不在"什么时候会未注册"这件事上做额外的路径区分。
                // </para>
                double scalingContribution = 0;
                if (scalingStat.HasValue)
                {
                    if (_statHost.IsRegistered(context.SourceId))
                    {
                        scalingContribution = coefficient * _statHost.GetStat(context.SourceId, scalingStat.Value);
                    }
                    else
                    {
                        _diagnostics.Warn(
                            $"效果的来源 \"{context.SourceId}\" 未注册（很可能已被销毁），" +
                            $"scaling_stat \"{scalingStat.Value}\" 的缩放贡献已按 0 处理（见 EffectDispatcher.ApplyDamageOrHeal 判断记录 C02）");
                    }
                }

                value = baseValue + scalingContribution;
            }

            // 契约缺口已补齐：EffectContext.Tags 携带技能标签集合（来自 skill.def.tags），
            // effect_value/crit_chance 维度的 SpellMod 过滤现在按学派/技能 id/标签三个维度一并
            // 匹配（见 SkillFilter.Matches）。CastPipeline.ExecuteEffectsOnly 构造 EffectContext
            // 时会传入 def.Tags；周期性光环效果（AuraHost.FirePeriodic）W1 收边补齐后也会带上
            // 施加该光环时传入的标签（见 AuraInstanceState.Tags/IEffectSink.ApplyAura 的 tags
            // 参数），不再恒为空——只有调用方本身没传标签（如种族被动光环、装备 grants.auras）时
            // 才退化为原先"按学派/技能 id"匹配的行为。
            value = _spellMods.Apply(context.SourceId, SpellModDimension.EffectValue, context.SkillId, context.School, context.Tags, value);

            var critBonus = _spellMods.Apply(context.SourceId, SpellModDimension.CritChance, context.SkillId, context.School, context.Tags, 0);
            var mergedParams = critBonus != 0 ? ParamsX.MergeNumber(context.Params, "crit_chance_bonus", critBonus) : context.Params;

            var outbound = new EffectContext(
                context.SourceId, context.TargetId, context.SkillId, context.Kind, context.School,
                value, coefficient, mergedParams, context.AuraInstanceId, context.IsPeriodic, context.CanCrit, context.CanMiss,
                context.Tags, context.TriggerChainDepth);

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

            // W1 收边补齐（A3 审计 #9）：把触发本次 apply_aura 的 EffectContext.Tags（源自
            // skill.def.tags，见 CastPipeline.ExecuteEffectsOnly）转发给 AuraHost，供后续周期效果
            // 结算按标签过滤 SpellMod（此前恒不传，见 AuraHost.FirePeriodic/AuraInstanceState.Tags）。
            _auraHost.ApplyAura(context.TargetId, auraDefId, context.SourceId, durationOverride, context.Tags, context.TriggerChainDepth);
            return NoOp(context);
        }

        private ResolveResult ApplyDispel(EffectContext context)
        {
            var category = ParamsX.GetId(context.Params, "category", default);
            var count = (int)ParamsX.GetNumber(context.Params, "count", 1);
            // N04 收边补齐：把本次结算的 TriggerChainDepth 传给 AuraHost.Dispel，使 dispel 产生的
            // aura.removed 携带正确深度，见 AuraRemovedEvent 类型注释。
            _auraHost.Dispel(context.TargetId, category, count, context.TriggerChainDepth);
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
            // RC-01 收边补齐：传入当前效果上下文自身的触发链深度（同步嵌套调用，见
            // ProcHost.TriggerCastCallback 类型注释——trigger_spell 是直接方法调用，深度就是
            // "正在执行的这一层"，由 CastPipeline.TriggerCast 校验后 +1 向下传播）。
            _triggerCast(context.SourceId, skillId, new[] { context.TargetId }, context.TriggerChainDepth);
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
        // projectile（收边任务补齐：从"未实现/委托六合一扩展点"改为委托 IProjectileSpawner，
        // 见该接口判断记录"依赖倒置"）
        // -----------------------------------------------------------------

        private ResolveResult ApplyProjectile(EffectContext context)
        {
            if (_projectileSpawner != null)
            {
                _projectileSpawner.Spawn(context, this);
                return NoOp(context);
            }

            _diagnostics.Warn(
                "EffectKind.Projectile 未注入 IProjectileSpawner（见 core/rules/common/contracts/" +
                "IProjectileSpawner.cs 判断记录\"依赖倒置\"，通常经 CarriersAssembly 注入），已按未处理返回");
            return NoOp(context);
        }

        // -----------------------------------------------------------------
        // 扩展点：summon / open_lock / create_item / set_world_flag / script
        // -----------------------------------------------------------------

        private ResolveResult ApplyExtension(EffectContext context)
        {
            if (_extension != null && _extension.TryHandle(context, out var result))
            {
                return result;
            }

            _diagnostics.Warn(
                $"EffectKind.{context.Kind} 未被 IEffectExtension 处理（见 core/rules/skill/README.md" +
                " \"五类效果原语委托扩展点\"），已按未处理返回");
            return NoOp(context);
        }

        private static ResolveResult NoOp(EffectContext context) =>
            new ResolveResult(HitResult.Hit, 0, 0, 0, immune: false, isHeal: context.Kind == EffectKind.Heal);
    }
}
