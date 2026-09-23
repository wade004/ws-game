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

        /// <summary>T-N1-8：<c>combat.level_diff_table</c> 强类型缓存，键为记录 id。旧构造函数（未带
        /// 本参数）恒得到 <see cref="EmptyLevelDiffTables"/>，等价于"没有任何等级差表"——见
        /// <see cref="CombatOptions.LevelDiffTableId"/> 判断记录"缺省 null 不接表"。</summary>
        private readonly IReadOnlyDictionary<Id, LevelDiffTable> _levelDiffTables;

        private readonly ICombatDiagnostics _diagnostics;
        private readonly ThreatTable _threatTable;
        private readonly Action<Id, Id?> _notifyCombatEvent;

        /// <summary>阶段 3 整理"事项三"：生物模板/tier 一类内容驱动的静态免疫，在光环免疫之外叠加
        /// 查询（见 <see cref="Resolve"/> 步骤 7）。可选构造参数，缺省
        /// <see cref="NullStaticImmunityProvider.Instance"/>（一律不免疫，不改变既有行为）。</summary>
        private readonly IStaticImmunityProvider _staticImmunity;

        /// <summary>T-N1-8：装备等级偏移量查询钩子，见 <see cref="IGearLevelOffsetProvider"/> 判断
        /// 记录。可选构造参数，缺省 <see cref="NullGearLevelOffsetProvider.Instance"/>（恒返回 0，
        /// 不改变既有行为）。</summary>
        private readonly IGearLevelOffsetProvider _gearLevelOffsetProvider;

        private readonly HashSet<Id> _warnedMissingStats = new HashSet<Id>();

        /// <summary>消费方反馈第 5 条根治：<see cref="ComputeMitigation"/> 对未登记 <c>combat.resist_curve</c>
        /// 学派只记一次警告（同 <see cref="_warnedMissingStats"/>/<see cref="WarnMissingStatOnce"/> 一贯
        /// 惯例，键为学派 id，与具体目标/结算次数无关）——避免战斗热路径上同一学派反复缺表时把诊断
        /// 消息列表刷爆。</summary>
        private readonly HashSet<Id> _warnedMissingResistCurve = new HashSet<Id>();

        private static readonly IReadOnlyDictionary<Id, LevelDiffTable> EmptyLevelDiffTables =
            new Dictionary<Id, LevelDiffTable>();

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
            : this(stats, powers, units, auras, factions, rng, bus, options, hitTables, resistCurvesBySchool,
                EmptyLevelDiffTables, diagnostics, threatTable, notifyCombatEvent, staticImmunity, null)
        {
        }

        /// <summary>
        /// T-N1-8 新增重载：追加 <paramref name="levelDiffTables"/>/<paramref name="gearLevelOffsetProvider"/>
        /// 两个参数。判断记录（不是给既有构造函数加可选参数，ABI 兼容惯例同
        /// <c>Core.Rules.Common.EffectContext</c> 多参构造重载、<c>HitTableBranch</c> 四参重载）：
        /// 既有十四参数构造函数（含可选的 <paramref name="staticImmunity"/>）已经是发布过的公开签名，
        /// 直接追加参数会改变其物理 IL 签名，对已编译好的外部消费方二进制是破坏性变更；本重载十六个
        /// 参数全部不带默认值，与既有构造函数在参数个数上不重叠（14 对 16），互不冲突，也不产生调用点
        /// 重载二义性。<see cref="CombatHost"/> 是本类唯一的生产调用方，已改经本重载。
        /// </summary>
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
            IReadOnlyDictionary<Id, LevelDiffTable> levelDiffTables,
            ICombatDiagnostics diagnostics,
            ThreatTable threatTable,
            Action<Id, Id?> notifyCombatEvent,
            IStaticImmunityProvider? staticImmunity,
            IGearLevelOffsetProvider? gearLevelOffsetProvider)
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
            _levelDiffTables = levelDiffTables ?? throw new ArgumentNullException(nameof(levelDiffTables));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _threatTable = threatTable ?? throw new ArgumentNullException(nameof(threatTable));
            _notifyCombatEvent = notifyCombatEvent ?? throw new ArgumentNullException(nameof(notifyCombatEvent));
            _staticImmunity = staticImmunity ?? NullStaticImmunityProvider.Instance;
            _gearLevelOffsetProvider = gearLevelOffsetProvider ?? NullGearLevelOffsetProvider.Instance;
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
                var deadTargetResult = new ResolveResult(HitResult.Miss, 0.0, 0.0, 0.0, immune: false, isHeal, steps);
                InvokeResolveTrace(context, deadTargetResult);
                return deadTargetResult;
            }

            var hitTable = RequireHitTable();

            // ---------------- 步骤 1：判定 ----------------
            var hit = DetermineHit(hitTable, context, isHeal, steps, out var isCrit, out var glancingMult, out var blockFlat);

            if (hit == HitResult.Miss || hit == HitResult.Dodge || hit == HitResult.Parry)
            {
                steps.Add($"terminal: hit={hit}，跳过步骤 2-8，FinalAmount=0");
                _notifyCombatEvent(context.SourceId, context.TargetId);
                _notifyCombatEvent(context.TargetId, context.SourceId);
                var terminalResult = new ResolveResult(hit, 0.0, 0.0, 0.0, immune: false, isHeal, steps);
                InvokeResolveTrace(context, terminalResult);
                return terminalResult;
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
                // T-N1-7（ADR-0030 决策 5；06 第 4.1 节 2026-09-14 修订段；复核返工：识别"减免
                // 属性"改为 CombatOptions 显式 id 清单 + scope 过滤，不再按 stat.definition.category
                // 批量扫描——见 CombatOptions.DamageTakenPctStat/DamageTakenPctStats 判断记录）：
                // 实际读取的属性集合 = {DamageTakenPctStat} ∪ DamageTakenPctStats（去重），逐条按
                // scope 与 context.SourceKind 匹配后求和，一次性应用。
                var ids = BuildDamageTakenStatIds();
                var combinedPct = SumScopedStats(context.TargetId, ids, context.SourceKind, steps, "target_multiplier_scoped");
                amount *= (1.0 + combinedPct / 100.0);
                steps.Add($"target_multiplier: stats=[{string.Join(",", ids)}] combined={combinedPct} -> {amount}");
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
                // C03 收口：把产生本次结算的 EffectContext.TriggerChainDepth 传给吸收扣减，
                // 让吸收耗尽移除光环实例发布的 aura.removed 事件携带真实触发链深度，纳入
                // MaxTriggerDepth 收敛预算（见 IAuraQuery.ConsumeAbsorb(Id, Id, double, int) 判断记录）。
                absorbed = _auras.ConsumeAbsorb(context.TargetId, context.School, requestedAmount, context.TriggerChainDepth);
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
                        // W1 收边补齐（拍板 3 前置）：随事件带上死亡那一刻的地图/坐标快照，供
                        // core/gameplay/death.DeathPolicyHost 的 respawn_point 策略取 spawn_points[0]
                        // 不必再反查（见 UnitDiedEvent 类型注释）。
                        _bus.Enqueue(new UnitDiedEvent(
                            context.TargetId, context.SourceId,
                            _units.GetMapId(context.TargetId), _units.GetPosition(context.TargetId)));
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
                    // RC-01 收边补齐：把产生本次结算的 EffectContext.TriggerChainDepth 原样戳到
                    // 落地事件上（见 EffectContext.TriggerChainDepth/ITriggerChainEvent 判断记录），
                    // 供 ProcHost 在 combat.heal_done 是某个 skill.proc_def.trigger_event 时读回，
                    // 让触发链深度预算能跨越本次 _bus.Enqueue 到下一个 DispatchPending pass 正确传播。
                    // 攻击实例 id 遗留根治（architecture/落地计划/audit-3224ca1-20260908/AUDIT_REPORT.md）：
                    // 同样把 EffectContext.AttackInstanceId 原样戳到落地事件上，见该字段判断记录——
                    // 未经 CastPipeline 产生的结算（如光环周期效果）为 null，事件上同样为 null，不改变
                    // 现有语义，只是多携带一份可选的批次身份信息。
                    _bus.Enqueue(new CombatHealDoneEvent(context.SourceId, context.TargetId, finalAmount, isCrit, context.TriggerChainDepth, context.AttackInstanceId));
                }
                else
                {
                    _threatTable.AddThreat(context.TargetId, context.SourceId, finalAmount);
                    // ADR-0073（消费方第十九批第 3 条根治）：把产生本次结算的 EffectContext.SkillId
                    // 原样戳到落地事件上，供 feedback.binding 按 skillId 过滤——但 IsPeriodic=true 的
                    // 结算（AuraHost.FirePeriodic 周期效果）截断为 null，因为该路径构造 EffectContext
                    // 时 skillId 参数传入的实际是光环定义 id（AuraInstanceState.DefId），不是技能 id，
                    // 原样转发会把一个不属于 skill.def 命名空间的 id 冒充成技能 id（见
                    // CombatDamageDealtEvent.SkillId 判断记录）。CastPipeline/AutoAttackHost/
                    // ProjectileHost 三个非周期生产构造点的 EffectContext.SkillId 均已是真正的技能 id
                    // （AutoAttackHost 固定为保留 id AutoAttackHost.NativeSkillId，有意如此，同判断
                    // 记录），isPeriodic 恒为 false，原样转发。
                    var skillId = context.IsPeriodic ? (Id?)null : context.SkillId;
                    _bus.Enqueue(new CombatDamageDealtEvent(context.SourceId, context.TargetId, context.School, finalAmount, isCrit, hit, context.TriggerChainDepth, context.AttackInstanceId, skillId));
                }
            }

            _notifyCombatEvent(context.SourceId, context.TargetId);
            _notifyCombatEvent(context.TargetId, context.SourceId);
            steps.Add("post: 仇恨/进战/事件处理完成");

            var result = new ResolveResult(hit, requestedAmount, finalAmount, absorbed, immune, isHeal, steps);
            InvokeResolveTrace(context, result);
            return result;
        }

        /// <summary>
        /// 消费方反馈 2026-09-11 编辑器第 31 条"方案 1"落地：<see cref="CombatOptions.ResolveTrace"/>
        /// 的唯一调用点，供本方法三条返回路径共用（见该属性判断记录 (1)）。未设置时不做任何工作
        /// （零开销，判断记录 (2)）；回调抛出的异常被捕获后经 <see cref="ICombatDiagnostics.Warn"/>
        /// 记一次警告并吞掉，不向上传播——工具/诊断代码里的 bug 不应打断真实游戏结算（判断记录
        /// (3)），这是本方法与调用方约定的"诊断通道"，不是把异常静默丢弃：调用方接入真实
        /// <see cref="ICombatDiagnostics"/> 实现后能在日志/遥测里看到这条警告。
        /// </summary>
        private void InvokeResolveTrace(EffectContext context, ResolveResult result)
        {
            var trace = _options.ResolveTrace;
            if (trace == null)
            {
                return;
            }

            try
            {
                trace(context, result);
            }
            catch (Exception ex)
            {
                _diagnostics.Warn(
                    $"Resolver.Resolve: CombatOptions.ResolveTrace 回调抛出异常，已捕获并忽略，" +
                    $"不影响本次结算（sourceId={context.SourceId}, targetId={context.TargetId}）：{ex}");
            }
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

            // T-N1-8（ADR-0030 决策 6；06 第 4.2 节修订段）：Δ 加成/压制无条件计算一次（即便本次
            // 结算最终不掷骰 miss/crit 也不影响其它结算步骤或事件流，只是多写一条 steps 追踪日志，
            // 不产生任何 RNG 消耗——不改变 RNG 流 id、不改变取样次数，见 CombatOptions.LevelDiffTableId
            // 判断记录）。未配置 CombatOptions.LevelDiffTableId 或表未加载时两者恒为 0，退化为
            // T-N1-8 之前的行为。
            var (deltaMissBonus, deltaCritSuppression) = ResolveLevelDiffAdjustments(context, steps);

            var rollAvoidance = !isHeal && context.CanMiss;
            if (rollAvoidance)
            {
                if (RollMissBranch(table.Miss, context.SourceId, deltaMissBonus, steps))
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
                // T-N1-7（ADR-0030 决策 5；06 第 4.1 节 2026-09-14 修订段；复核返工：识别"被暴击
                // 减免属性"改为 CombatOptions.CritTakenReductionStats 显式 id 清单 + scope 过滤，
                // 不再按 stat.definition.category 批量扫描）：在取样（_rng.Next）之前，把清单里
                // scope 匹配的属性值之和从暴击率里扣减，下限 0（不影响 RNG 流 id、不影响本次判定
                // 是否取样——只影响取样前的 chance 数值本身，取样次数不变）。
                var baseChance = ResolveChance(table.Crit, context.SourceId) + ReadCritChanceBonus(context);
                var critTakenReduction = SumScopedStats(
                    context.TargetId, _options.CritTakenReductionStats, context.SourceKind, steps, "crit_taken_reduction");
                // T-N1-8（ADR-0030 决策 6；06 第 4.2 节修订段"暴击率 = 攻击者暴击属性 − 暴击压制(Δ)"）：
                // 在既有 T-N1-7 被暴击减免之外再扣减 Δ 压制，双向生效——deltaCritSuppression 可为负
                // （Δ<0，即攻击者相对更高等级时），此时减去一个负数等于额外增加暴击率。未配置
                // CombatOptions.LevelDiffTableId 时 deltaCritSuppression 恒为 0，不影响既有结果。
                var chance = Math.Max(0.0, baseChance - critTakenReduction - deltaCritSuppression);
                var roll = _rng.Next(_options.RngStream);
                isCrit = roll < chance;
                steps.Add($"hit_check: crit chance={chance}(base={baseChance}, taken_reduction={critTakenReduction}, level_diff_suppression={deltaCritSuppression}) roll={roll} -> isCrit={isCrit}");
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
            return RollChance(chance, steps, label);
        }

        /// <summary>
        /// T-N1-8：<c>miss</c> 分支专用掷骰入口——与其余四个"判定"分支（dodge/parry/glancing_blow/
        /// block，仍走 <see cref="RollBranch"/>）不同，miss 的 chance 不是"提供 stat 就直接取该属性
        /// 值，否则取 base"这一既有语义（<see cref="ResolveChance"/>）本身，而是在此基础上再减去
        /// 攻击者命中属性（<see cref="HitTableBranch.HitStat"/>）、加上 Δ 加成（06 第 4.2 节修订段
        /// "未命中率 = 基础未命中 − 攻击者命中属性 + 未命中加成(Δ)"），结果夹取到 [0,1]（
        /// <see cref="Clamp01"/>）。<see cref="HitTableBranch.HitStat"/> 未提供、<paramref name="missBonus"/>
        /// 为 0（未配置 <see cref="CombatOptions.LevelDiffTableId"/>）时，chance 退化为
        /// <c>ResolveChance(branch, attackerId)</c> 本身——与 T-N1-8 之前逐位一致。
        /// </summary>
        private bool RollMissBranch(HitTableBranch branch, Id attackerId, double missBonus, List<string> steps)
        {
            if (!branch.Enabled)
            {
                steps.Add("hit_check: miss 未开启，跳过掷骰");
                return false;
            }

            var baseChance = ResolveChance(branch, attackerId);
            var hitStatValue = branch.HitStat.HasValue ? GetStatSafe(attackerId, branch.HitStat.Value) : 0.0;
            var chance = Clamp01(baseChance - hitStatValue + missBonus);
            var roll = _rng.Next(_options.RngStream);
            var triggered = roll < chance;
            steps.Add($"hit_check: miss chance={chance}(base={baseChance}, hit_stat={hitStatValue}, level_diff_bonus={missBonus}) roll={roll} -> {triggered}");
            return triggered;
        }

        /// <summary>掷骰 + 比较 + 追踪日志的公共部分，从既有 <see cref="RollBranch"/> 抽出（T-N1-8），
        /// 供 <see cref="RollMissBranch"/> 复用同一段 RNG 消耗逻辑——两者对 <see cref="IRngHost.Next"/>
        /// 的调用次数与顺序未发生任何改变，只是取样前 chance 的计算方式不同（同 <c>miss</c> 分支之外
        /// 四个分支）。</summary>
        private bool RollChance(double chance, List<string> steps, string label)
        {
            var roll = _rng.Next(_options.RngStream);
            var triggered = roll < chance;
            steps.Add($"hit_check: {label} chance={chance} roll={roll} -> {triggered}");
            return triggered;
        }

        private double ResolveChance(HitTableBranch branch, Id statSubject)
        {
            return branch.Stat.HasValue ? GetStatSafe(statSubject, branch.Stat.Value) : branch.Base;
        }

        private static double Clamp01(double value) => value < 0.0 ? 0.0 : (value > 1.0 ? 1.0 : value);

        /// <summary>
        /// T-N1-8（ADR-0030 决策 6；06 第 4.2 节修订段）：计算本次结算的 Δ 加成/压制——未配置
        /// <see cref="CombatOptions.LevelDiffTableId"/>，或配置了但该记录未在
        /// <see cref="_levelDiffTables"/> 中找到（表未加载/id 拼错），两者恒为 (0, 0)，不写 steps
        /// 追踪日志（保持既有行为路径的日志输出不变，不给"从未启用过本特性"的场景添加噪音）；配置
        /// 且找到时按 <see cref="ResolveEffectiveLevel"/> 分别取攻击者/目标的有效等级，Δ = 目标有效
        /// 等级 − 攻击者有效等级，代入 <see cref="LevelDiffTable.MissBonus"/>/
        /// <see cref="LevelDiffTable.CritSuppression"/> 两条断点表求值（<see cref="PiecewiseCurve.Evaluate"/>
        /// 越界夹取到端点），并追加一条 steps 追踪日志。
        /// </summary>
        private (double missBonus, double critSuppression) ResolveLevelDiffAdjustments(EffectContext context, List<string> steps)
        {
            if (!TryGetLevelDiffTable(out var table))
            {
                return (0.0, 0.0);
            }

            var attackerLevel = ResolveEffectiveLevel(context.SourceId);
            var targetLevel = ResolveEffectiveLevel(context.TargetId);
            var delta = targetLevel - attackerLevel;
            var missBonus = table.MissBonus.Evaluate(delta);
            var critSuppression = table.CritSuppression.Evaluate(delta);
            steps.Add($"level_diff: table={table.Id} attacker_level={attackerLevel} target_level={targetLevel} " +
                $"delta={delta} miss_bonus={missBonus} crit_suppression={critSuppression}");
            return (missBonus, critSuppression);
        }

        private bool TryGetLevelDiffTable(out LevelDiffTable table)
        {
            var id = _options.LevelDiffTableId;
            if (id == null)
            {
                table = null!;
                return false;
            }

            return _levelDiffTables.TryGetValue(id.Value, out table!);
        }

        /// <summary>
        /// T-N1-8（06 第 4.2 节修订段"有效等级默认等于角色等级；策略配置项有效等级是否计入装备等级
        /// 偏移，默认关闭"）：<see cref="CombatOptions.EffectiveLevelIncludesGearOffset"/> 关闭时恒
        /// 返回 <see cref="IUnitAccess.GetLevel"/>（角色等级本身）；开启时叠加
        /// <see cref="IGearLevelOffsetProvider.GetGearLevelOffset"/> 的返回值（见该接口判断记录——
        /// 缺省 <see cref="NullGearLevelOffsetProvider"/> 恒返回 0，开启但未接入真实来源时退化为与
        /// 关闭时相同的结果）。<paramref name="unitId"/> 已从世界移除时按等级 1 处理（同
        /// <see cref="ResolveAttackerLevelForMitigation"/> 判断记录同一防御姿态，不抛异常）。
        /// </summary>
        private double ResolveEffectiveLevel(Id unitId)
        {
            var baseLevel = _units.Exists(unitId) ? (double)_units.GetLevel(unitId) : 1.0;
            if (!_options.EffectiveLevelIncludesGearOffset)
            {
                return baseLevel;
            }

            return baseLevel + _gearLevelOffsetProvider.GetGearLevelOffset(unitId);
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
        // T-N1-7：步骤 1（被暴击减免介入暴击率）与步骤 6（目标乘区）共用的按显式 id 清单 + 作用域求和
        // -----------------------------------------------------------------

        /// <summary>深度复审 A-S1（2026-09-16）新增的去重结果缓存——见 <see
        /// cref="BuildDamageTakenStatIds"/> 判断记录。</summary>
        private IReadOnlyList<Id>? _damageTakenStatIdsCache;

        /// <summary>上一次算出 <see cref="_damageTakenStatIdsCache"/> 时使用的
        /// <see cref="CombatOptions.DamageTakenPctStat"/> 值，用于判断缓存是否仍然有效。</summary>
        private Id _damageTakenStatIdsCacheKeyStat;

        /// <summary>上一次算出 <see cref="_damageTakenStatIdsCache"/> 时使用的
        /// <see cref="CombatOptions.DamageTakenPctStats"/> 列表引用（按引用相等比较，见 <see
        /// cref="BuildDamageTakenStatIds"/> 判断记录）。</summary>
        private IReadOnlyList<Id>? _damageTakenStatIdsCacheKeyExtra;

        /// <summary>
        /// T-N1-7（[ADR-0030](../../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
        /// 决策 5；06 第 4.1 节 2026-09-14 修订段）：目标乘区实际读取的属性 id 集合 =
        /// <c>{CombatOptions.DamageTakenPctStat} ∪ CombatOptions.DamageTakenPctStats</c>，按首次
        /// 出现顺序去重（<see cref="CombatOptions.DamageTakenPctStat"/> 恒排在最前）——保证既有单
        /// 属性配置项即便也被显式列进 <see cref="CombatOptions.DamageTakenPctStats"/>，也只计入
        /// 一次（见该属性判断记录）。
        /// <para>
        /// 判断记录（深度复审 A-S1 修复，2026-09-16）：本方法在 <see cref="Resolve"/> 步骤 6 的
        /// "目标乘区"里对<b>每一次</b>非治疗伤害结算都调用一次，原实现每次都新建一个
        /// <see cref="HashSet{T}"/> 与一个 <see cref="List{T}"/>——是战斗结算热路径上可避免的 GC
        /// 分配（与本模块"避免每 tick 产生大量临时分配"的既有原则相悖），不影响任何计算结果。
        /// <see cref="CombatOptions"/> 本身可写（各属性均带 <c>set</c>），不能假定它在
        /// <see cref="Resolver"/> 实例生命周期内绝对不变，因此不能只缓存一次就永久复用——这里按
        /// "引用相等"缓存：只要 <see cref="CombatOptions.DamageTakenPctStat"/> 的值与
        /// <see cref="CombatOptions.DamageTakenPctStats"/> 的列表引用都与上一次算缓存时相同，就直接
        /// 返回同一个缓存实例；任一项变化（配置在装配阶段之后被重新赋值）才重新计算一次并更新缓存
        /// 键。<see cref="Id"/> 是值语义，按值比较；<see cref="CombatOptions.DamageTakenPctStats"/>
        /// 是引用类型，按 <see cref="object.ReferenceEquals(object?, object?)"/> 比较——同一个列表
        /// 实例被原地修改（而不是整体替换成新实例）不属于本类判断记录约束范围（同 <see
        /// cref="CombatOptions"/> 其余只读快照式集合属性的既有惯例：装配阶段设定一次，不支持热改
        /// 内容后原地变更同一实例）。
        /// </para>
        /// </summary>
        private IReadOnlyList<Id> BuildDamageTakenStatIds()
        {
            var stat = _options.DamageTakenPctStat;
            var extra = _options.DamageTakenPctStats;

            if (_damageTakenStatIdsCache != null
                && _damageTakenStatIdsCacheKeyStat == stat
                && ReferenceEquals(_damageTakenStatIdsCacheKeyExtra, extra))
            {
                return _damageTakenStatIdsCache;
            }

            var seen = new HashSet<Id>();
            var result = new List<Id>();

            if (seen.Add(stat))
            {
                result.Add(stat);
            }

            for (int i = 0; i < extra.Count; i++)
            {
                if (seen.Add(extra[i]))
                {
                    result.Add(extra[i]);
                }
            }

            _damageTakenStatIdsCache = result;
            _damageTakenStatIdsCacheKeyStat = stat;
            _damageTakenStatIdsCacheKeyExtra = extra;
            return result;
        }

        /// <summary>
        /// T-N1-7（[ADR-0030](../../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
        /// 决策 5；06 第 4.1 节 2026-09-14 修订段；复核返工：撤回"按 <c>stat.definition.category</c>
        /// 批量扫描"的首版实现——ADR-0030 决策 9 把护甲也归入 <c>defense</c> 类别，类别扫描会把
        /// 护甲原始数值误当百分比计入目标乘区；改为本"显式 id 清单"）："目标乘区"（承伤）与"被暴击
        /// 减免"两处共用的求和逻辑：遍历调用方显式给出的 <paramref name="statIds"/>（见
        /// <see cref="BuildDamageTakenStatIds"/>/<see cref="CombatOptions.CritTakenReductionStats"/>），
        /// 逐条经 <see cref="IStatHost.GetScope"/> 读取该属性的作用域，按 <see cref="ScopeMatches"/>
        /// 与 <paramref name="sourceKind"/> 匹配，不匹配则跳过（单机下 <c>scope: from_player</c> 的
        /// 属性对 <see cref="SourceKind.Creature"/>/<see cref="SourceKind.Unknown"/> 来源恒不匹配，
        /// 零成本退化，见 <see cref="SourceKind"/> 类型判断记录）；匹配的属性对
        /// <paramref name="targetId"/> 求最终值（<see cref="GetStatSafe"/>，缺失按 0 处理）累加求和。
        /// <paramref name="label"/> 只用于 <paramref name="steps"/> 追踪日志前缀，不影响计算结果。
        /// </summary>
        private double SumScopedStats(
            Id targetId, IReadOnlyList<Id> statIds, SourceKind sourceKind, List<string> steps, string label)
        {
            double sum = 0.0;

            if (statIds.Count == 0)
            {
                steps.Add($"{label}: 未配置任何属性 id，sum=0");
                return sum;
            }

            for (int i = 0; i < statIds.Count; i++)
            {
                var statId = statIds[i];
                var scope = _stats.GetScope(statId);
                if (!ScopeMatches(scope, sourceKind))
                {
                    steps.Add($"{label}: stat={statId} scope={scope} 与 sourceKind={sourceKind} 不匹配，跳过");
                    continue;
                }

                var value = GetStatSafe(targetId, statId);
                sum += value;
                steps.Add($"{label}: stat={statId} scope={scope} sourceKind={sourceKind} value={value} -> sum={sum}");
            }

            return sum;
        }

        /// <summary>
        /// T-N1-7（ADR-0030 决策 5）：<c>stat.definition.scope</c> 与结算上下文
        /// <see cref="EffectContext.SourceKind"/> 的匹配规则——<c>any</c> 恒匹配；<c>from_player</c>
        /// 仅 <see cref="SourceKind.Player"/> 匹配；<c>from_creature</c> 仅 <see cref="SourceKind.Creature"/>
        /// 匹配；<see cref="SourceKind.Unknown"/> 只对 <c>any</c> 匹配（同 <see cref="SourceKind"/>
        /// 类型判断记录"两类作用域属性对 Unknown 一律不匹配"）。未识别的 <paramref name="scope"/> 取值
        /// （理论上不会发生——内容校验已按 <c>StatSchemas.ScopeValues</c> 枚举拦下非法值，这里的防御
        /// 姿态同 <see cref="GetStatSafe"/> 对缺失属性"按 0/不匹配处理，不抛异常"的既有风格）保守按
        /// 不匹配处理，不静默当 <c>any</c>。
        /// </summary>
        private static bool ScopeMatches(string scope, SourceKind sourceKind)
        {
            switch (scope)
            {
                case "any": return true;
                case "from_player": return sourceKind == SourceKind.Player;
                case "from_creature": return sourceKind == SourceKind.Creature;
                default: return false;
            }
        }

        // -----------------------------------------------------------------
        // 步骤 5：减免
        // -----------------------------------------------------------------

        private double ComputeMitigation(Id targetId, Id school, Id sourceId, List<string> steps)
        {
            if (!_resistCurvesBySchool.TryGetValue(school, out var curve))
            {
                steps.Add($"mitigation: 学派 {school} 无对应 combat.resist_curve，减免=0");
                // 消费方反馈第 5 条根治（运行时路径不静默降级，见 AGENTS.md §3）：此前只写 steps
                // 追踪日志（只有传入 CombatOptions.ResolveTrace 回调的调用方才看得到），未经
                // ICombatDiagnostics 报出——数据漏配（combat.resist_curve 缺该学派一行）时，结算
                // 仍会得到一个合法的"零减免"结果，接入方完全看不出这是配置缺失还是设计如此。本类
                // 已经接了 ADR-0042 的诊断出口（见 CombatHost 装配、DiagnosticsHubComposition 登记
                // "Core.Rules.Combat"），只是这一个分支忘了调用；同 WarnMissingStatOnce 一贯惯例，
                // 按学派 id 只警告一次，不随每次结算重复入账。
                WarnMissingResistCurveOnce(school);
                return 0.0;
            }

            var statId = school == _options.PhysicalSchool
                ? _options.ArmorStat
                : new Id(_options.ResistStatPrefix + LastSegment(school));

            var statValue = GetStatSafe(targetId, statId);

            // N05 收边补齐（外部审计 68c9bed，P1）：来源单位可能已经从世界移除（如 DOT 光环的
            // 施法者 Despawn 后，周期 tick 仍按 AuraInstanceState.SourceId 结算——见
            // core/rules/skill/core/AuraHost.FirePeriodic），此前无条件 _units.GetLevel(sourceId)
            // 命中 WorldUnitAccess.Require 直接抛 InvalidOperationException，整条周期效果中断。
            // 06 未规定"光环来源消失后如何结算"（判断记录：06 第 3.3/3.8 节只定义 periodic_damage/
            // periodic_heal 的效果形状与 tick 顺序，未提及来源生命周期），本实现选择"降级而不终止"：
            // 来源不存在时不再查询其等级，改用目标自身等级作为替代 attackerLevel——等价于"假定
            // 来源与目标同级"这一中性默认值（不引入 06 未定义的等级差惩罚/加成，只保证周期效果
            // 能继续结算，不因为来源销毁这一与效果数值无关的事实而中断）。目标同样不存在的极端
            // 情形（罕见：两者都已从世界移除但事件仍在派发）进一步退化为等级 1，不再抛出。
            var attackerLevel = ResolveAttackerLevelForMitigation(sourceId, targetId, steps);
            var reduction = curve.ComputeReduction(statValue, attackerLevel);
            steps.Add($"mitigation: school={school} stat={statId}={statValue} attackerLevel={attackerLevel} -> reduction={reduction}");
            return reduction;
        }

        /// <summary>N05 收边补齐：见 <see cref="ComputeMitigation"/> 调用处判断记录。</summary>
        private int ResolveAttackerLevelForMitigation(Id sourceId, Id targetId, List<string> steps)
        {
            if (_units.Exists(sourceId))
            {
                return _units.GetLevel(sourceId);
            }

            steps.Add($"mitigation: 来源 \"{sourceId}\" 已不存在于世界模拟（Despawn 等），按目标自身等级降级，不抛异常");

            if (_units.Exists(targetId))
            {
                return _units.GetLevel(targetId);
            }

            return 1;
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

        /// <summary>消费方反馈第 5 条根治：见 <see cref="ComputeMitigation"/> 调用处判断记录——
        /// 定位信息给到具体表名 + 具体缺失的学派 id，供内容作者直接去 <c>combat.resist_curve</c>
        /// 补一行 <c>school={school}</c> 的记录。</summary>
        private void WarnMissingResistCurveOnce(Id school)
        {
            if (_warnedMissingResistCurve.Add(school))
            {
                _diagnostics.Warn(
                    $"Resolver.ComputeMitigation: combat.resist_curve 未登记学派 \"{school}\" 对应的减免曲线，" +
                    "本次及后续同学派结算按 0 减免处理（不阻断结算，可能是数据漏配，请检查 combat.resist_curve 表）");
            }
        }
    }
}
