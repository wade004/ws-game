using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Numbers.Faction;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Core.Rules.Combat
{
    /// <summary>
    /// 结算管线（见 06_规则层_属性技能战斗AI.md 第 4.1 节固定九步："判定 → 基础值 → 暴击 →
    /// 施法者乘区 → 减免 → 目标乘区 → 免疫吸收 → 落地 → 后置"）。每一步只读取上一步的输出与
    /// 只读的 <see cref="IStatHost"/>/<see cref="IPowerHost"/> 查询，不做跨步骤回填——本类把
    /// 每一步的中间值追加进 <see cref="ResolveResult.Steps"/>，供测试与调试逐位比对手算结果。
    /// <para>
    /// 判断记录（06 原文未规定的实现细节，见本模块 README 完整列表）：
    /// (1) 命中表六个分支里，miss/crit 的概率查询主体是攻击者（<c>SourceId</c>），
    /// dodge/parry/glancing_blow/block 的概率与格挡量查询主体是防御者（<c>TargetId</c>）——
    /// 06 只给出六项是什么、默认值与开关，未规定"向谁查询概率属性"，本实现按 WoW 命中表的
    /// 常见语义分配。
    /// (2) 偏斜的伤害系数、格挡的固定减免量，应用时点选在"基础值"确定之后、"暴击"倍率相乘
    /// 之前——06 的判定步骤本身不产生数值（尚未算出基础值），本实现把它们挪到基础值可用的
    /// 第一时机应用，语义不变（偏斜/格挡仍然是"判定"阶段决出的结果，只是数值应用点顺延）。
    /// (3) 偏斜、格挡命中时不再参与暴击判定（一次结算只报告一种"特殊分支"标签，
    /// <see cref="HitResult"/> 是扁平枚举而非位标记）；<see cref="ResolveResult.Hit"/> 只反映
    /// miss/dodge/parry/glancing_blow/block 这一优先序里最先触发的分支，或在均未触发时反映
    /// 是否暴击（Crit/Hit）。
    /// </para>
    /// </summary>
    public sealed class Resolver
    {
        private readonly IStatHost _stats;
        private readonly IPowerHost _powers;
        private readonly IUnitAccess _units;
        private readonly IAuraQuery _auras;
        private readonly IFactionMatrix _factions;
        private readonly IRngHost _rng;
        private readonly IEventBus _bus;
        private readonly CombatOptions _options;
        private readonly IReadOnlyDictionary<Id, HitTableConfig> _hitTables;
        private readonly IReadOnlyDictionary<Id, ResistCurve> _resistCurvesBySchool;
        private readonly ICombatDiagnostics _diagnostics;
        private readonly ThreatTable _threatTable;
        private readonly Action<Id, Id?> _notifyCombatEvent;

        /// <summary>阶段 3 整理"事项三"：生物模板/tier 一类内容驱动的静态免疫，在光环免疫之外叠加
        /// 查询（见 <see cref="Resolve"/> 步骤 7）。可选构造参数，缺省
        /// <see cref="NullStaticImmunityProvider.Instance"/>（一律不免疫，不改变既有行为）。</summary>
        private readonly IStaticImmunityProvider _staticImmunity;

        private readonly HashSet<Id> _warnedMissingStats = new HashSet<Id>();

        public Resolver(
            IStatHost stats,
            IPowerHost powers,
            IUnitAccess units,
            IAuraQuery auras,
            IFactionMatrix factions,
            IRngHost rng,
            IEventBus bus,
            CombatOptions options,
            IReadOnlyDictionary<Id, HitTableConfig> hitTables,
            IReadOnlyDictionary<Id, ResistCurve> resistCurvesBySchool,
            ICombatDiagnostics diagnostics,
            ThreatTable threatTable,
            Action<Id, Id?> notifyCombatEvent,
            IStaticImmunityProvider? staticImmunity = null)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _powers = powers ?? throw new ArgumentNullException(nameof(powers));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _auras = auras ?? throw new ArgumentNullException(nameof(auras));
            _factions = factions ?? throw new ArgumentNullException(nameof(factions));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _hitTables = hitTables ?? throw new ArgumentNullException(nameof(hitTables));
            _resistCurvesBySchool = resistCurvesBySchool ?? throw new ArgumentNullException(nameof(resistCurvesBySchool));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _threatTable = threatTable ?? throw new ArgumentNullException(nameof(threatTable));
            _notifyCombatEvent = notifyCombatEvent ?? throw new ArgumentNullException(nameof(notifyCombatEvent));
            _staticImmunity = staticImmunity ?? NullStaticImmunityProvider.Instance;
        }

        public ResolveResult Resolve(EffectContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var isHeal = context.Kind == EffectKind.Heal;
            var steps = new List<string>();

            // 拍板：对已死亡目标再结算，直接返回 Miss 并记诊断，不触碰任何数值/事件（见任务书
            // "设计"一节"对死亡目标再结算 → 直接 Immune/无效...返回 Miss 并记诊断"）。
            if (!_units.IsAlive(context.TargetId))
            {
                _diagnostics.Warn($"Resolver.Resolve: 目标 \"{context.TargetId}\" 已死亡，本次结算按 Miss 处理，不落地、不发事件");
                steps.Add("precheck: target already dead -> Miss");
                return new ResolveResult(HitResult.Miss, 0.0, 0.0, 0.0, immune: false, isHeal, steps);
            }

            var hitTable = RequireHitTable();

            // ---------------- 步骤 1：判定 ----------------
            var hit = DetermineHit(hitTable, context, isHeal, steps, out var isCrit, out var glancingMult, out var blockFlat);

            if (hit == HitResult.Miss || hit == HitResult.Dodge || hit == HitResult.Parry)
            {
                steps.Add($"terminal: hit={hit}，跳过步骤 2-8，FinalAmount=0");
                _notifyCombatEvent(context.SourceId, context.TargetId);
                _notifyCombatEvent(context.TargetId, context.SourceId);
                return new ResolveResult(hit, 0.0, 0.0, 0.0, immune: false, isHeal, steps);
            }

            // ---------------- 步骤 2：基础值 ----------------
            var amount = context.BaseValue;
            steps.Add($"base_value: {amount}");

            if (hit == HitResult.GlancingBlow)
            {
                amount *= glancingMult;
                steps.Add($"glancing_adjust: ×{glancingMult} -> {amount}");
            }
            else if (hit == HitResult.Block)
            {
                var before = amount;
                amount = Math.Max(0.0, amount - blockFlat);
                steps.Add($"block_adjust: {before} - {blockFlat} -> {amount}");
            }

            // ---------------- 步骤 3：暴击 ----------------
            if (isCrit)
            {
                var critMultiplier = hitTable.CritMultiplierStat.HasValue
                    ? GetStatSafe(context.SourceId, hitTable.CritMultiplierStat.Value)
                    : hitTable.CritMultiplierBase;
                amount *= critMultiplier;
                steps.Add($"crit_multiplier: ×{critMultiplier} -> {amount}");
            }
            else
            {
                steps.Add("crit_multiplier: 未暴击，跳过");
            }

            // ---------------- 步骤 4：施法者乘区 ----------------
            var casterPctStat = isHeal ? _options.HealingDonePctStat : _options.DamageDonePctStat;
            var casterPct = GetStatSafe(context.SourceId, casterPctStat);
            amount *= (1.0 + casterPct / 100.0);
            steps.Add($"caster_multiplier: stat={casterPctStat}={casterPct} -> {amount}");

            // ---------------- 步骤 5：减免（仅伤害分支） ----------------
            if (!isHeal)
            {
                var reduction = ComputeMitigation(context.TargetId, context.School, context.SourceId, steps);
                amount *= (1.0 - reduction);
                steps.Add($"mitigation_applied: reduction={reduction} -> {amount}");
            }
            else
            {
                steps.Add("mitigation: 治疗分支不减免，跳过");
            }

            // ---------------- 步骤 6：目标乘区（仅伤害分支） ----------------
            if (!isHeal)
            {
                var targetPct = GetStatSafe(context.TargetId, _options.DamageTakenPctStat);
                amount *= (1.0 + targetPct / 100.0);
                steps.Add($"target_multiplier: stat={_options.DamageTakenPctStat}={targetPct} -> {amount}");
            }
            else
            {
                steps.Add("target_multiplier: 治疗分支跳过（无承疗乘区配置）");
            }

            var requestedAmount = amount;

            // ---------------- 步骤 7：免疫吸收 ----------------
            var immune = _auras.IsImmune(context.TargetId, context.School, context.Kind)
                || _staticImmunity.IsImmune(context.TargetId, context.School, context.Kind);
            double absorbed;
            double finalAmount;

            if (immune)
            {
                absorbed = 0.0;
                finalAmount = 0.0;
                steps.Add("immune_absorb: 免疫，FinalAmount=0，不落地、不发事件");
            }
            else if (!isHeal)
            {
                absorbed = _auras.ConsumeAbsorb(context.TargetId, context.School, requestedAmount);
                finalAmount = requestedAmount - absorbed;
                steps.Add($"immune_absorb: absorbed={absorbed} -> FinalAmount={finalAmount}");
            }
            else
            {
                absorbed = 0.0;
                finalAmount = requestedAmount;
                steps.Add("immune_absorb: 治疗分支不吸收");
            }

            // ---------------- 步骤 8：落地 ----------------
            if (!immune)
            {
                if (isHeal)
                {
                    _powers.ModifyPower(context.TargetId, WellKnownPowers.Health, finalAmount, context.SourceId);
                    steps.Add($"land: heal +{finalAmount}");
                }
                else
                {
                    _powers.ModifyPower(context.TargetId, WellKnownPowers.Health, -finalAmount, context.SourceId);
                    steps.Add($"land: damage -{finalAmount}");

                    if (_powers.GetPower(context.TargetId, WellKnownPowers.Health) <= 0.0)
                    {
                        _units.SetAlive(context.TargetId, false);
                        _bus.Enqueue(new UnitDiedEvent(context.TargetId, context.SourceId));
                        steps.Add($"land: 目标生命降至 0，死亡结算，killerId={context.SourceId}");
                    }
                }
            }

            // ---------------- 步骤 9：后置 ----------------
            if (!immune)
            {
                if (isHeal)
                {
                    ApplyHealThreat(context.SourceId, context.TargetId, finalAmount, steps);
                    _bus.Enqueue(new CombatHealDoneEvent(context.SourceId, context.TargetId, finalAmount, isCrit));
                }
                else
                {
                    _threatTable.AddThreat(context.TargetId, context.SourceId, finalAmount);
                    _bus.Enqueue(new CombatDamageDealtEvent(context.SourceId, context.TargetId, context.School, finalAmount, isCrit, hit));
                }
            }

            _notifyCombatEvent(context.SourceId, context.TargetId);
            _notifyCombatEvent(context.TargetId, context.SourceId);
            steps.Add("post: 仇恨/进战/事件处理完成");

            return new ResolveResult(hit, requestedAmount, finalAmount, absorbed, immune, isHeal, steps);
        }

        // -----------------------------------------------------------------
        // 步骤 1：判定
        // -----------------------------------------------------------------

        private HitResult DetermineHit(
            HitTableConfig table,
            EffectContext context,
            bool isHeal,
            List<string> steps,
            out bool isCrit,
            out double glancingMult,
            out double blockFlatReduction)
        {
            isCrit = false;
            glancingMult = 1.0;
            blockFlatReduction = 0.0;

            // Hit 作为"尚未触发任何特殊分支"的哨兵值，见下方 special 的用法。
            var special = HitResult.Hit;

            var rollAvoidance = !isHeal && context.CanMiss;
            if (rollAvoidance)
            {
                if (RollBranch(table.Miss, context.SourceId, steps, "miss"))
                {
                    return HitResult.Miss;
                }

                if (RollBranch(table.Dodge, context.TargetId, steps, "dodge"))
                {
                    return HitResult.Dodge;
                }

                if (RollBranch(table.Parry, context.TargetId, steps, "parry"))
                {
                    return HitResult.Parry;
                }

                if (RollBranch(table.GlancingBlow, context.TargetId, steps, "glancing_blow"))
                {
                    special = HitResult.GlancingBlow;
                    glancingMult = table.GlancingDamagePct;
                    steps.Add($"hit_check: glancing_blow 触发，伤害系数={glancingMult}");
                }
                else if (RollBranch(table.Block, context.TargetId, steps, "block"))
                {
                    special = HitResult.Block;
                    blockFlatReduction = table.BlockValueStat.HasValue
                        ? GetStatSafe(context.TargetId, table.BlockValueStat.Value)
                        : 0.0;
                    steps.Add($"hit_check: block 触发，固定减免={blockFlatReduction}");
                }
            }
            else
            {
                steps.Add(isHeal
                    ? "hit_check: 治疗分支跳过 miss/dodge/parry/glancing_blow/block"
                    : "hit_check: CanMiss=false，跳过 miss/dodge/parry/glancing_blow/block");
            }

            if (context.CanCrit && table.Crit.Enabled)
            {
                var chance = ResolveChance(table.Crit, context.SourceId) + ReadCritChanceBonus(context);
                var roll = _rng.Next(_options.RngStream);
                isCrit = roll < chance;
                steps.Add($"hit_check: crit chance={chance} roll={roll} -> isCrit={isCrit}");
            }
            else
            {
                steps.Add("hit_check: crit 分支未开启或本次结算不可暴击，跳过");
            }

            return special != HitResult.Hit ? special : (isCrit ? HitResult.Crit : HitResult.Hit);
        }

        private bool RollBranch(HitTableBranch branch, Id statSubject, List<string> steps, string label)
        {
            if (!branch.Enabled)
            {
                steps.Add($"hit_check: {label} 未开启，跳过掷骰");
                return false;
            }

            var chance = ResolveChance(branch, statSubject);
            var roll = _rng.Next(_options.RngStream);
            var triggered = roll < chance;
            steps.Add($"hit_check: {label} chance={chance} roll={roll} -> {triggered}");
            return triggered;
        }

        private double ResolveChance(HitTableBranch branch, Id statSubject)
        {
            return branch.Stat.HasValue ? GetStatSafe(statSubject, branch.Stat.Value) : branch.Base;
        }

        private static double ReadCritChanceBonus(EffectContext context)
        {
            if (context.Params.TryGetValue("crit_chance_bonus", out var value) && value is JsonNumber number)
            {
                return number.Value;
            }

            return 0.0;
        }

        // -----------------------------------------------------------------
        // 步骤 5：减免
        // -----------------------------------------------------------------

        private double ComputeMitigation(Id targetId, Id school, Id sourceId, List<string> steps)
        {
            if (!_resistCurvesBySchool.TryGetValue(school, out var curve))
            {
                steps.Add($"mitigation: 学派 {school} 无对应 combat.resist_curve，减免=0");
                return 0.0;
            }

            var statId = school == _options.PhysicalSchool
                ? _options.ArmorStat
                : new Id(_options.ResistStatPrefix + LastSegment(school));

            var statValue = GetStatSafe(targetId, statId);
            var attackerLevel = _units.GetLevel(sourceId);
            var reduction = curve.ComputeReduction(statValue, attackerLevel);
            steps.Add($"mitigation: school={school} stat={statId}={statValue} attackerLevel={attackerLevel} -> reduction={reduction}");
            return reduction;
        }

        private static string LastSegment(Id id)
        {
            var value = id.Value;
            var dot = value.LastIndexOf('.');
            return dot < 0 ? value : value.Substring(dot + 1);
        }

        // -----------------------------------------------------------------
        // 步骤 9：治疗仇恨
        // -----------------------------------------------------------------

        /// <summary>
        /// 治疗产生仇恨（见 06 第 4.4 节"治疗按（可配置系数的）数值增加仇恨值（治疗友方单位时，
        /// 仇恨记在被治疗方所在阵营的仇恨表里，供敌对 AI 选目标）"）。
        /// <para>
        /// 判断记录（任务书"拍板简化"）：不引入"阵营仇恨表"这一新概念，改为对每个当前已经把
        /// 被治疗者或治疗者记在自己仇恨表里的敌对单位，追加 <c>amount × HealThreatCoefficient</c>
        /// 的仇恨——语义上等价于"正在攻击这个治疗小队的敌人，仇恨值因为看到治疗而提升"，且复用
        /// 现有 <see cref="ThreatTable"/> 结构，不新增数据形状。
        /// </para>
        /// </summary>
        private void ApplyHealThreat(Id sourceId, Id targetId, double amount, List<string> steps)
        {
            if (_options.HealThreatCoefficient <= 0.0 || amount <= 0.0)
            {
                steps.Add("heal_threat: 系数<=0 或治疗量<=0，跳过");
                return;
            }

            if (!_units.Exists(targetId))
            {
                steps.Add("heal_threat: 被治疗者不存在，跳过");
                return;
            }

            var healedFaction = _units.GetFaction(targetId);
            var delta = amount * _options.HealThreatCoefficient;
            var applied = 0;

            foreach (var ownerId in _threatTable.TrackedUnits)
            {
                if (!_units.Exists(ownerId) || !_units.IsAlive(ownerId))
                {
                    continue;
                }

                if (!_factions.IsHostile(_units.GetFaction(ownerId), healedFaction))
                {
                    continue;
                }

                var tracksHealed = ContainsSource(ownerId, targetId);
                var tracksHealer = ContainsSource(ownerId, sourceId);
                if (!tracksHealed && !tracksHealer)
                {
                    continue;
                }

                // 记到"敌对单位对被治疗者的仇恨"上（被治疗者是敌对 AI 实际会攻击的目标）。
                _threatTable.AddThreat(ownerId, targetId, delta);
                applied++;
            }

            steps.Add($"heal_threat: delta={delta} 应用到 {applied} 个已跟踪该次治疗相关单位的敌对单位");
        }

        private bool ContainsSource(Id ownerId, Id sourceId)
        {
            foreach (var (source, _) in _threatTable.GetAll(ownerId))
            {
                if (source == sourceId)
                {
                    return true;
                }
            }

            return false;
        }

        // -----------------------------------------------------------------
        // 辅助
        // -----------------------------------------------------------------

        private HitTableConfig RequireHitTable()
        {
            if (_hitTables.TryGetValue(_options.HitTableConfigId, out var table))
            {
                return table;
            }

            throw new InvalidOperationException(
                $"Resolver: CombatOptions.HitTableConfigId=\"{_options.HitTableConfigId}\" 未在 combat.hit_table_config 中找到");
        }

        /// <summary>属性缺失（未在 stat.definition 登记）或单位未在 StatHost 注册时按 0 处理，
        /// 不抛异常，只记一次警告（同一属性 id 只警告一次，见任务书"设计"一节
        /// "属性缺失时视为 0（不抛异常，记一次警告）"）。</summary>
        private double GetStatSafe(Id unitId, Id stat)
        {
            try
            {
                return _stats.GetStat(unitId, stat);
            }
            catch (ArgumentException)
            {
                WarnMissingStatOnce(stat);
                return 0.0;
            }
            catch (InvalidOperationException)
            {
                WarnMissingStatOnce(stat);
                return 0.0;
            }
        }

        private void WarnMissingStatOnce(Id stat)
        {
            if (_warnedMissingStats.Add(stat))
            {
                _diagnostics.Warn($"Resolver: 属性 \"{stat}\" 缺失或单位未在 StatHost 注册，按 0 处理");
            }
        }
    }
}
