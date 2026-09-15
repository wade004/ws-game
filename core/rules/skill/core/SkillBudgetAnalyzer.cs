using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// T-N3-9（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 2；06 第
    /// 3.10 节；04 第 5 节数值类校验项分级表）：技能预算比值分析——与 07 第 1.2 节装备预算对称，照
    /// <c>Core.Carriers.Item.EquipmentScoreAnalyzer</c>/<c>ItemBudgetCurve.ComputeConsumed</c>/
    /// <c>Core.Gameplay.Loot.LootTableAnalyzer</c> 三个先例的契约面形态：静态类、纯函数，不持有任何
    /// 字段/可变状态，不修改 <c>view</c> 或任何入参的状态，可重复调用；只读依赖经每次调用的参数注入，
    /// 不在构造期/静态字段里缓存任何 registry 快照。
    /// <para>
    /// <b>公式（06 第 3.10 节 + 数值总纲第 4.5 节原文）</b>：
    /// <code>
    /// 施放时间当量 T = max(动作时长, 一拍常数)；周期效果按总持续时间乘折价
    /// 预算上限 = 锚点秒伤(技能等级) × T × 冷却溢价(冷却÷T) × 范围折价(max_targets) × 消耗溢价(消耗÷期望回复率)
    /// 实际价值 = 基础值 + Σ(系数 × 该等级期望缩放属性)
    /// 控制价值 = 时长 × 目标数 × 控制类别权重；增益价值 = 属性当量(stat.weight) × 时长 ÷ 冷却
    /// </code>
    /// </para>
    /// <para>
    /// <b>设计层裁定（2026-09-15）：采纳——锚点数据来源</b>：公式分子分母都要用到
    /// "某技能等级下的锚点秒伤/期望缩放属性最终值"，权威来源 <c>sim.anchor</c> 归阶段 N6，晚于本
    /// 阶段（N3）。本类型不允许假装 <c>sim.anchor</c> 已经存在，改为接受调用方注入的
    /// <see cref="ISkillBudgetAnchorProvider"/>（同 <see cref="IGearLevelOffsetProvider"/> 先例）。
    /// <see cref="Analyze"/> 本身对注入值老实求值，不做"未接入即跳过"的特判——那一层判断留给规则
    /// 注册层（<c>SkillBudgetValidationRule</c>），见该类型与 <see cref="ISkillBudgetAnchorProvider"/>
    /// 判断记录。
    /// </para>
    /// <para>
    /// <b>实现取舍（本任务范围收窄，均已文档化，非契约条文明文规定）</b>：
    /// <list type="number">
    /// <item><description><b>实际价值的加总方式</b>：06 原文给了三条并列的价值公式（"实际价值"/"控制
    /// 价值"/"增益价值"），未规定一个同时含伤害、控制、增益内容的混合技能该如何合成单一比值。本
    /// 实现把 <c>effects[]</c> 里每一项结算类内容各自算出的价值直接相加，作为唯一的
    /// <see cref="SkillBudgetResult.EffectiveValue"/>（伤害/治疗/吸收按"基础值+Σ系数×期望属性"、
    /// 控制按"时长×目标数×类别权重"、增益按"属性当量×时长÷冷却"，公式互不冲突，相加是最保守的
    /// "不低估实际产出"处理）。</description></item>
    /// <item><description><b>周期效果的施放时间当量</b>：技能效果列表里若存在 <c>apply_aura</c> 引用
    /// 的光环含 <c>periodic_damage</c>/<c>periodic_heal</c> 且该光环 <c>duration</c> 非空，T 改取
    /// "该光环总持续时间 × <c>skill.budget_rule.periodic_time_discount</c>"（有多个这样的光环时取
    /// 持续时间最长的一个），不再是 <c>max(动作时长, 一拍常数)</c>；<c>duration</c> 为空（永久光环）
    /// 时本实现不参与周期部分的价值/T 计算（06 未规定永久周期光环的施放时间当量如何折算，见类型内
    /// <c>ComputeEffectContributions</c> 判断记录），退化为按 <c>0</c> 贡献处理。</description></item>
    /// <item><description><b>消耗溢价的"期望回复率"</b>：取 <c>cost[0].power_type</c> 指向的
    /// <c>arch.power_type.regen_in_combat</c>（真实已注册字段，见
    /// <c>Core.Rules.Assembly.RulesSchemaCatalog.RegisterTimeFieldConsistencyRule</c>）；多资源消耗
    /// （06 第 3.1 节"可选二级资源至多一个，默认关闭"，罕见）只取第一条资源折算比值，其余资源的
    /// <c>amount</c> 仍计入分子求和。</description></item>
    /// <item><description><b>范围折价曲线登记为"除数"而非直接倍数</b>：为满足 04 第 5 节
    /// <c>curve_monotonic_finite</c>（纵轴不递减）与"折价应随 max_targets 增大而递减"这一游戏设计
    /// 直觉的张力，<c>skill.budget_rule.range_discount_curve</c> 登记为随 max_targets 增大而递增的
    /// 除数，本类型按 <c>1.0 / 曲线取值</c> 换算成实际相乘的折价倍数，见 <see
    /// cref="SkillSchemas.BudgetRule"/> 字段判断记录。</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public static class SkillBudgetAnalyzer
    {
        /// <summary>同 <c>EffectDispatcher.DefaultBudgetRuleId</c> 字面量一致（判断记录：两处各自
        /// 持有一份字面量常量，不互相引用——<c>EffectDispatcher</c> 的常量是 <c>private</c>，本类型
        /// 不属于同一文件也不适合把它改为 <c>internal</c> 只为共享一个字符串常量，重复声明的维护
        /// 成本低于新增跨文件耦合）。</summary>
        private static readonly Id DefaultBudgetRuleId = new Id("skill.budget_rule.default");

        private static readonly HashSet<AuraEffectKind> SettlementAuraEffectKinds = new HashSet<AuraEffectKind>
        {
            AuraEffectKind.PeriodicDamage,
            AuraEffectKind.PeriodicHeal,
            AuraEffectKind.ModStat,
            AuraEffectKind.Control,
            AuraEffectKind.Absorb,
        };

        /// <summary>对 <paramref name="skillId"/> 指向的 <c>skill.def</c> 做技能预算分析。
        /// <paramref name="options"/> 缺省时按 <c>new SkillOptions().BudgetRuleId</c>（缺省
        /// <c>skill.budget_rule.default</c>，同 <c>EffectDispatcher</c> 惯例）；<paramref
        /// name="anchorProvider"/> 缺省时按 <see cref="NullSkillBudgetAnchorProvider.Instance"/>
        /// （见该类型判断记录"未接入时是否当真"）。</summary>
        public static SkillBudgetResult Analyze(
            Id skillId,
            IDataRegistryView view,
            SkillOptions? options = null,
            ISkillBudgetAnchorProvider? anchorProvider = null)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));

            var skillRecord = view.Get("skill.def", skillId);
            if (skillRecord == null)
            {
                throw new ArgumentException($"技能 \"{skillId}\" 不存在", nameof(skillId));
            }

            var defs = new SkillDefCache(view);
            var skillDef = defs.GetSkillDef(skillId);
            var provider = anchorProvider ?? NullSkillBudgetAnchorProvider.Instance;
            var budgetRuleId = options?.BudgetRuleId ?? DefaultBudgetRuleId;

            if (!Participates(skillDef, defs))
            {
                return new SkillBudgetResult(
                    skillId, participates: false, SkillBudgetTier.Unattributed, level: 0,
                    effectiveValue: 0, timeEquivalent: 0, cooldownPremium: 0, rangeDiscount: 0,
                    costPremium: 0, anchorDps: 0, budgetLimit: 0, ratio: 0,
                    bandwidth: 0, hardCap: 0, budgetNote: null, SkillBudgetVerdict.NotApplicable);
            }

            defs.TryResolveBudgetAttribution(skillId, out var tier, out var level);

            var ruleRecord = view.Get("skill.budget_rule", budgetRuleId);
            var beatSeconds = ruleRecord != null && ruleRecord.TryGetNumber("beat_seconds", out var bs) ? bs : 1.0;
            var periodicDiscount = ruleRecord != null && ruleRecord.TryGetNumber("periodic_time_discount", out var pd) ? pd : 1.0;

            var maxTargets = ResolveMaxTargets(view, skillDef.TargetShapeRef);

            var (effectiveValue, periodicTimeCandidate) =
                ComputeEffectContributions(skillDef, defs, view, level, provider, ruleRecord, maxTargets);

            double timeEquivalent;
            if (periodicTimeCandidate.HasValue)
            {
                timeEquivalent = Math.Max(periodicTimeCandidate.Value * periodicDiscount, beatSeconds);
            }
            else
            {
                var actionDuration = skillDef.ChannelTime > 0 ? skillDef.ChannelTime : skillDef.CastTime;
                timeEquivalent = Math.Max(actionDuration, beatSeconds);
            }

            var cooldownRatio = timeEquivalent > 0 ? skillDef.CooldownDuration / timeEquivalent : 0;
            var cooldownPremium = EvaluateCurveOrNeutral(ruleRecord, "cooldown_premium_curve", cooldownRatio);

            double rangeDiscount;
            if (maxTargets.HasValue)
            {
                var rangeDivisor = EvaluateCurveOrNeutral(ruleRecord, "range_discount_curve", maxTargets.Value);
                rangeDiscount = rangeDivisor > 0 ? 1.0 / rangeDivisor : 1.0;
            }
            else
            {
                // max_targets == 0（不限）：见 SkillSchemas.BudgetRule.range_discount_curve 字段
                // 判断记录"无上限技能不参与本曲线"，取中性倍数。
                rangeDiscount = 1.0;
            }

            var costRatio = ComputeCostRatio(skillDef.Cost, view);
            var costPremium = EvaluateCurveOrNeutral(ruleRecord, "cost_premium_curve", costRatio);

            var anchorDps = provider.GetAnchorDps(level);
            var budgetLimit = anchorDps * timeEquivalent * cooldownPremium * rangeDiscount * costPremium;
            var ratio = budgetLimit > 0 ? effectiveValue / budgetLimit : (effectiveValue > 0 ? double.PositiveInfinity : 1.0);

            var isPlayerTier = tier != SkillBudgetTier.Monster;
            var bandwidth = ResolveTieredNumber(ruleRecord, isPlayerTier, "player_bandwidth", "monster_bandwidth", 0.2, 5.0);
            var hardCap = ResolveTieredNumber(ruleRecord, isPlayerTier, "player_hard_cap", "monster_hard_cap", 3.0, 50.0);

            var budgetNote = skillRecord.TryGetString("budget_note", out var noteText) && !string.IsNullOrEmpty(noteText)
                ? noteText
                : null;

            var verdict = Classify(ratio, bandwidth, hardCap, budgetNote);

            return new SkillBudgetResult(
                skillId, participates: true, tier, level,
                effectiveValue, timeEquivalent, cooldownPremium, rangeDiscount, costPremium,
                anchorDps, budgetLimit, ratio, bandwidth, hardCap, budgetNote, verdict);
        }

        /// <summary>结算类原语判定（06 第 3.2 节 2026-09-14 修订段"结算类原语集合"；精确判定
        /// <c>apply_aura</c>，见 <see cref="Core.Rules.Common.SettlementEffectKinds"/> 类型判断
        /// 记录"这一层更细的判定……留给 T-N3-9（SkillBudgetAnalyzer）……决定具体收窄方式"）：本方法
        /// 就是该判断记录点名的收窄实现——<c>apply_aura</c> 只有引用的光环含
        /// <c>periodic_damage</c>/<c>periodic_heal</c>/<c>mod_stat</c>/<c>control</c>/<c>absorb</c>
        /// 任一效果时才计入。</summary>
        private static bool Participates(SkillDef skillDef, SkillDefCache defs)
        {
            foreach (var effect in skillDef.Effects)
            {
                switch (effect.Kind)
                {
                    case EffectKind.SchoolDamage:
                    case EffectKind.WeaponDamagePct:
                    case EffectKind.Heal:
                    case EffectKind.Projectile:
                        return true;
                    case EffectKind.ApplyAura:
                        var auraDefId = ParamsX.GetIdOpt(effect.Params, "aura_def");
                        if (auraDefId.HasValue && defs.TryGetAuraDef(auraDefId.Value, out var auraDef) &&
                            AuraContainsSettlementEffect(auraDef))
                        {
                            return true;
                        }
                        break;
                }
            }

            return false;
        }

        private static bool AuraContainsSettlementEffect(AuraDef auraDef)
        {
            foreach (var effect in auraDef.Effects)
            {
                if (SettlementAuraEffectKinds.Contains(effect.Kind))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>遍历 <paramref name="skillDef"/> 的结算类效果，累加"实际价值"（见类型顶部判断
        /// 记录"实际价值的加总方式"），并在遇到带 <c>duration</c> 的周期性光环时收集"周期效果施放
        /// 时间当量"候选（多个取最大者，见类型顶部判断记录"周期效果的施放时间当量"）。</summary>
        private static (double EffectiveValue, double? PeriodicTimeCandidate) ComputeEffectContributions(
            SkillDef skillDef, SkillDefCache defs, IDataRegistryView view, int level,
            ISkillBudgetAnchorProvider provider, DataRecord? ruleRecord, int? maxTargets)
        {
            double total = 0;
            double? periodicTimeCandidate = null;

            foreach (var effect in skillDef.Effects)
            {
                switch (effect.Kind)
                {
                    case EffectKind.SchoolDamage:
                    case EffectKind.Heal:
                        total += ComputeDamageOrHealValue(effect.Params, level, provider, defs);
                        break;

                    case EffectKind.WeaponDamagePct:
                        var pct = ParamsX.GetNumber(effect.Params, "pct", 0);
                        var beatSeconds = ruleRecord != null && ruleRecord.TryGetNumber("beat_seconds", out var bs) ? bs : 1.0;
                        total += pct * provider.GetAnchorDps(level) * beatSeconds;
                        break;

                    case EffectKind.Projectile:
                        var onHitEffects = ParamsX.GetObjectArray(effect.Params, "on_hit_effects");
                        foreach (var onHit in onHitEffects)
                        {
                            var kindText = onHit.TryGetValue("kind", out var kindRaw) && kindRaw is JsonString kindStr ? kindStr.Value : null;
                            if (!EffectKindNames.TryParse(kindText, out var onHitKind))
                            {
                                continue;
                            }

                            if (onHitKind != EffectKind.SchoolDamage && onHitKind != EffectKind.Heal)
                            {
                                continue;
                            }

                            var onHitParams = onHit.TryGetValue("params", out var paramsRaw) && paramsRaw is JsonObject paramsObj
                                ? paramsObj
                                : new JsonObjectBuilder().Build();
                            total += ComputeDamageOrHealValue(onHitParams, level, provider, defs);
                        }
                        break;

                    case EffectKind.ApplyAura:
                        var auraDefId = ParamsX.GetIdOpt(effect.Params, "aura_def");
                        if (!auraDefId.HasValue || !defs.TryGetAuraDef(auraDefId.Value, out var auraDef))
                        {
                            break;
                        }

                        var (auraValue, auraPeriodicCandidate) = ComputeAuraSettlementValue(
                            auraDef, defs, view, level, provider, ruleRecord, maxTargets, skillDef.CooldownDuration);
                        total += auraValue;
                        if (auraPeriodicCandidate.HasValue)
                        {
                            periodicTimeCandidate = periodicTimeCandidate.HasValue
                                ? Math.Max(periodicTimeCandidate.Value, auraPeriodicCandidate.Value)
                                : auraPeriodicCandidate.Value;
                        }
                        break;
                }
            }

            return (total, periodicTimeCandidate);
        }

        /// <summary>
        /// ADR-0032 决策 6（07 第 1.2 节修订段"授予规则"）：装备 <c>item.template.grants.skills</c>/
        /// <c>grants.auras</c> 所授予的单个技能/光环的预算价值（供 <c>Core.Carriers.Item
        /// .ItemGrantValueExceedsShareRule</c> 消费——L3 <c>core/carriers/item</c> 依赖 L2
        /// <c>core/rules/skill</c> 合法，见 <c>item.template.grants</c> 字段判断记录"任务书额外要求
        /// 1：L3 引用 L2 程序集合法"）。<paramref name="isAura"/> 为 <c>true</c> 时 <paramref
        /// name="id"/> 指向 <c>skill.aura_def</c>，否则指向 <c>skill.def</c>。
        /// <para>
        /// 判断记录（授予技能走 <see cref="Analyze"/> 而不是单独一套逻辑；<paramref name="level"/>
        /// 语义为"该件装备的期望等级代理"）：授予技能本身可能是一个完整的可主动施放技能（含
        /// <c>cast_time</c>/<c>cooldown_duration</c> 等），用同一条 <see cref="Analyze"/> 求出的
        /// <see cref="SkillBudgetResult.EffectiveValue"/>（技能不参与结算校验时为 0，见 <see
        /// cref="SkillBudgetResult.Participates"/>）作为"这个技能的预算价值"是最省代码路径的自洽
        /// 取法；<see cref="Analyze"/> 内部按 <see cref="SkillDefCache.TryResolveBudgetAttribution"/>
        /// 反查等级，会覆盖本方法传入的 <paramref name="level"/>——这是有意的（技能预算的"技能等级"
        /// 权威定义仍是"习得等级"，不因为它同时被装备授予就改用装备等级）。授予的<b>光环</b>（多数
        /// 场景，如"穿戴期间持续提供的被动效果"）没有习得等级概念，<paramref name="level"/>（装备
        /// 期望等级代理，通常传 <c>item_level</c> 或经 <c>item.req_level_curve</c> 反推的需求等级）
        /// 直接决定期望缩放属性的求值点，见调用方 <c>ItemGrantValueExceedsShareRule</c> 判断记录。
        /// </para>
        /// </summary>
        public static double ComputeGrantValue(
            Id id, bool isAura, IDataRegistryView view, int level,
            ISkillBudgetAnchorProvider anchorProvider, SkillOptions? options = null)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (anchorProvider == null) throw new ArgumentNullException(nameof(anchorProvider));

            if (!isAura)
            {
                if (view.Get("skill.def", id) == null)
                {
                    return 0;
                }

                var result = Analyze(id, view, options, anchorProvider);
                return result.Participates ? result.EffectiveValue : 0;
            }

            var defs = new SkillDefCache(view);
            if (!defs.TryGetAuraDef(id, out var auraDef) || !AuraContainsSettlementEffect(auraDef))
            {
                return 0;
            }

            var budgetRuleId = options?.BudgetRuleId ?? DefaultBudgetRuleId;
            var ruleRecord = view.Get("skill.budget_rule", budgetRuleId);
            var (value, _) = ComputeAuraSettlementValue(
                auraDef, defs, view, level, anchorProvider, ruleRecord,
                maxTargets: 1, // 授予的光环没有 target_shape_ref，见方法顶部判断记录，按单目标处理。
                cooldownDuration: 0);
            return value;
        }

        private static (double Value, double? PeriodicTimeCandidate) ComputeAuraSettlementValue(
            AuraDef auraDef, SkillDefCache defs, IDataRegistryView view, int level,
            ISkillBudgetAnchorProvider provider, DataRecord? ruleRecord, int? maxTargets, double cooldownDuration)
        {
            double total = 0;
            double? periodicTimeCandidate = null;

            foreach (var auraEffect in auraDef.Effects)
            {
                switch (auraEffect.Kind)
                {
                    case AuraEffectKind.PeriodicDamage:
                    case AuraEffectKind.PeriodicHeal:
                        if (!auraDef.Duration.HasValue)
                        {
                            // 永久光环：见类型顶部判断记录"周期效果的施放时间当量"，本实现不参与
                            // 周期部分的价值/T 计算，按 0 贡献处理。
                            break;
                        }

                        var interval = ParamsX.GetNumber(auraEffect.Params, "interval", 0);
                        if (interval <= 0)
                        {
                            break;
                        }

                        var perTick = ComputeDamageOrHealValue(auraEffect.Params, level, provider, defs);
                        var ticks = auraDef.Duration.Value / interval;
                        total += perTick * ticks;

                        periodicTimeCandidate = periodicTimeCandidate.HasValue
                            ? Math.Max(periodicTimeCandidate.Value, auraDef.Duration.Value)
                            : auraDef.Duration.Value;
                        break;

                    case AuraEffectKind.Absorb:
                        total += ParamsX.GetNumber(auraEffect.Params, "amount", 0);
                        break;

                    case AuraEffectKind.ModStat:
                        total += ComputeBuffValue(auraEffect.Params, auraDef.Duration, cooldownDuration, ruleRecord, view);
                        break;

                    case AuraEffectKind.Control:
                        total += ComputeControlValue(auraEffect.Params, auraDef.Duration, maxTargets, ruleRecord);
                        break;
                }
            }

            return (total, periodicTimeCandidate);
        }

        /// <summary>06 第 3.2 节效果值契约"效果值 = 基础值 + Σ(缩放属性最终值 × 系数)"，与
        /// <c>EffectDispatcher.ApplyDamageOrHeal</c> 同一条公式（scaling 列表优先，缺失时回退旧单
        /// 字段 <c>scaling_stat</c>/<c>coefficient</c>；<c>base_curve_ref</c> 存在时取代
        /// <c>base_value</c>）——唯一差异：<c>EffectDispatcher</c> 从 <c>IStatHost</c> 读来源单位当前
        /// 活值，本方法从 <see cref="ISkillBudgetAnchorProvider.GetExpectedScalingStatValue"/> 读
        /// 该等级的期望值（数值总纲第 4.5 节"期望属性来自锚点表……"）。</summary>
        private static double ComputeDamageOrHealValue(JsonObject @params, int level, ISkillBudgetAnchorProvider provider, SkillDefCache defs)
        {
            double baseValue;
            var baseCurveRef = ParamsX.GetIdOpt(@params, "base_curve_ref");
            if (baseCurveRef.HasValue && defs.TryGetBaseCurve(baseCurveRef.Value, out var curve))
            {
                baseValue = curve.Evaluate(level);
            }
            else
            {
                baseValue = ParamsX.GetNumber(@params, "base_value", 0);
            }

            double scalingContribution = 0;
            var scalingEntries = ParamsX.GetObjectArray(@params, "scaling");
            if (scalingEntries.Count > 0)
            {
                foreach (var entry in scalingEntries)
                {
                    var entryStat = ParamsX.GetIdOpt(entry, "stat");
                    if (!entryStat.HasValue) continue;
                    var entryCoefficient = ParamsX.GetNumber(entry, "coefficient", 0);
                    scalingContribution += entryCoefficient * provider.GetExpectedScalingStatValue(entryStat.Value, level);
                }
            }
            else
            {
                var scalingStat = ParamsX.GetIdOpt(@params, "scaling_stat");
                if (scalingStat.HasValue)
                {
                    var coefficient = ParamsX.GetNumber(@params, "coefficient", 0);
                    scalingContribution = coefficient * provider.GetExpectedScalingStatValue(scalingStat.Value, level);
                }
            }

            return baseValue + scalingContribution;
        }

        /// <summary>06 第 3.10 节"增益价值 = 属性当量 × 时长 ÷ 冷却"；属性当量 = |mod_stat.value| ×
        /// <c>stat.weight</c>（永久光环/零冷却的分母保护见判断记录）。</summary>
        private static double ComputeBuffValue(JsonObject @params, double? auraDuration, double cooldownDuration, DataRecord? ruleRecord, IDataRegistryView view)
        {
            if (!auraDuration.HasValue)
            {
                // 永久增益：同周期效果一致的取舍（见类型顶部判断记录），不参与增益价值计算。
                return 0;
            }

            var statId = ParamsX.GetIdOpt(@params, "stat");
            var weight = statId.HasValue ? ResolveStatWeight(view, statId.Value) : 1.0;
            var value = Math.Abs(ParamsX.GetNumber(@params, "value", 0));

            // 判断记录：冷却为 0（技能无冷却，如可随意重复施放的增益）时"时长÷冷却"分母为零——本
            // 实现改用 beat_seconds 兜底（一个恒生效的短增益，近似按"每拍都能维持"处理），避免除零，
            // 06 未规定这一边界，属临时判断。
            var beatSeconds = ruleRecord != null && ruleRecord.TryGetNumber("beat_seconds", out var bs) ? bs : 1.0;
            var divisor = cooldownDuration > 0 ? cooldownDuration : beatSeconds;

            return weight * value * auraDuration.Value / divisor;
        }

        /// <summary>06 第 3.10 节"控制价值 = 时长 × 目标数 × 控制类别权重"。</summary>
        private static double ComputeControlValue(JsonObject @params, double? auraDuration, int? maxTargets, DataRecord? ruleRecord)
        {
            if (!auraDuration.HasValue)
            {
                return 0;
            }

            // 判断记录：max_targets 为空（0，不限）时的目标数——真实运行期目标数取决于场上单位分布，
            // 预算校验阶段无法预知，设计层裁定（2026-09-15）：采纳，按 1 处理（保守：不因"理论上可以
            // 打无数人"而放大控制价值，见 SkillSchemas.BudgetRule.range_discount_curve 字段同款
            // "无上限不参与折价曲线"处理口径一致）。
            var targetCount = maxTargets ?? 1;
            var category = ParamsX.GetStringOpt(@params, "category");
            var weight = ResolveControlCategoryWeight(ruleRecord, category);

            return auraDuration.Value * targetCount * weight;
        }

        private static double ResolveControlCategoryWeight(DataRecord? ruleRecord, string? category)
        {
            if (ruleRecord == null || string.IsNullOrEmpty(category))
            {
                return 1.0;
            }

            if (!ruleRecord.TryGetObject("control_category_weights", out var weights))
            {
                return 1.0;
            }

            return weights.TryGetValue(category!, out var raw) && raw is JsonNumber num ? num.Value : 1.0;
        }

        /// <summary><c>stat.weight</c> 表按 <c>stat</c> 字段反查权重（同 <c>ItemBudgetCurve
        /// .BuildStatBudgetInfo</c> 判断记录"L2 不能依赖 L3 <c>Core.Carriers</c>"的反方向——本类型
        /// 属 L2 <c>core/rules/skill</c>，不能引用 L3 <c>Core.Carriers.Item.ItemBudgetCurve</c>，
        /// 只能就地重新实现这段"按 stat 字段找 stat.weight 记录"的查找，逻辑不复杂（O(n) 扫描一张
        /// 通常只有个位数到十位数记录的小表），不值得为此新增跨层依赖或把查找逻辑上提到 L1。未登记
        /// 权重的属性缺省 1.0（见 <see cref="Core.Numbers.StatBlock.StatSchemas.Weight"/> 类型判断
        /// 记录"stat.weight 未被 class_overrides 命中的职业使用……"同一"缺省即中性"取向）。</summary>
        private static double ResolveStatWeight(IDataRegistryView view, Id stat)
        {
            if (!view.Tables.Contains("stat.weight"))
            {
                return 1.0;
            }

            foreach (var record in view.GetAll("stat.weight"))
            {
                if (record.TryGetId("stat", out var candidate) && candidate.Equals(stat))
                {
                    return record.TryGetNumber("weight", out var weight) ? weight : 1.0;
                }
            }

            return 1.0;
        }

        /// <summary>消耗溢价比值的分子分母（类型顶部判断记录"消耗溢价的期望回复率"）：分子为
        /// <c>cost[]</c> 全部 <c>amount</c> 之和，分母取第一条资源的 <c>arch.power_type
        /// .regen_in_combat</c>；无消耗/无法解析回复率时返回 0（对应中性曲线取值，见 <see
        /// cref="EvaluateCurveOrNeutral"/>）。</summary>
        private static double ComputeCostRatio(IReadOnlyList<(Id PowerType, double Amount)> cost, IDataRegistryView view)
        {
            if (cost.Count == 0)
            {
                return 0;
            }

            var total = 0.0;
            foreach (var entry in cost)
            {
                total += entry.Amount;
            }

            var powerRecord = view.Get("arch.power_type", cost[0].PowerType);
            if (powerRecord == null)
            {
                return total;
            }

            var regen = powerRecord.TryGetNumber("regen_in_combat", out var r) ? r : 0.0;
            return regen > 0 ? total / regen : total;
        }

        /// <summary>目标形状 <c>max_targets</c>（04 §3.7/06 §3.7；<see
        /// cref="Core.Rules.Targeting.TargetSchemas.ChainDef"/> 字段判断记录"默认 1；0 表示不限"）：
        /// 链记录不存在（未注册/引用悬空）或字段未声明时按 1（单目标）处理；显式 0 返回
        /// <c>null</c>（不限）。</summary>
        private static int? ResolveMaxTargets(IDataRegistryView view, Id targetShapeRef)
        {
            var chainRecord = view.Get("target.chain_def", targetShapeRef);
            if (chainRecord == null || !chainRecord.TryGetInt("max_targets", out var raw))
            {
                return 1;
            }

            var maxTargets = (int)raw;
            return maxTargets == 0 ? (int?)null : maxTargets;
        }

        /// <summary>断点表曲线求值，字段缺失/空断点表时取中性倍数 1.0（不是 <see
        /// cref="Core.Foundation.Common.PiecewiseCurve"/> 的"空表恒 0"语义——见 <see
        /// cref="SkillSchemas.BudgetRule"/> 各曲线字段判断记录）。</summary>
        private static double EvaluateCurveOrNeutral(DataRecord? ruleRecord, string field, double x)
        {
            if (ruleRecord == null || !ruleRecord.TryGetArray(field, out var raw) || raw.Count == 0)
            {
                return 1.0;
            }

            return CurveSchema.TryReadBreakpoints(raw, out var curve, out _) ? curve.Evaluate(x) : 1.0;
        }

        private static double ResolveTieredNumber(
            DataRecord? ruleRecord, bool isPlayerTier, string playerField, string monsterField,
            double playerDefault, double monsterDefault)
        {
            if (isPlayerTier)
            {
                return ruleRecord != null && ruleRecord.TryGetNumber(playerField, out var v) ? v : playerDefault;
            }

            return ruleRecord != null && ruleRecord.TryGetNumber(monsterField, out var v2) ? v2 : monsterDefault;
        }

        private static SkillBudgetVerdict Classify(double ratio, double bandwidth, double hardCap, string? budgetNote)
        {
            var upperBound = 1.0 + bandwidth;
            if (ratio <= upperBound)
            {
                return SkillBudgetVerdict.Pass;
            }

            if (!string.IsNullOrEmpty(budgetNote))
            {
                // 硬性规则"禁止阻断带说明的超模技能"：有说明时无论是否超硬上限，一律归"已确认"警告。
                return SkillBudgetVerdict.ConfirmedDeviation;
            }

            return ratio > hardCap ? SkillBudgetVerdict.HardCapExceeded : SkillBudgetVerdict.UnconfirmedDeviation;
        }
    }
}
