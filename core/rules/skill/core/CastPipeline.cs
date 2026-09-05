using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Numbers.PowerSet;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 施法管线（见 06 第 3.6 节九步固定顺序、落地方案 T2-4 行）。每单位同一时刻至多一个
    /// <see cref="CastState"/>（读条或引导）；法术队列复用同一个 <see cref="CastState"/> 实例的
    /// <see cref="CastState.Queued"/> 槽位（每单位一个槽，见 06 第 3.6 节"法术队列"）。
    /// <para>
    /// 触发链递归深度（<see cref="TriggerCast"/>，供 <see cref="ProcHost"/> 与
    /// <c>trigger_spell</c> 效果原语共用）用一个 ambient 计数器 <see cref="_triggerDepth"/> 维护：
    /// 进入 <see cref="TriggerCast"/> 自增、退出自减，超过 <see cref="SkillOptions.MaxTriggerDepth"/>
    /// 直接拒绝（见落地方案 T2-6 禁止事项"禁止触发链无限递归"）。因为本引擎单线程同步执行，
    /// ambient 计数器不需要额外的调用上下文对象即可正确工作。
    /// </para>
    /// </summary>
    public sealed class CastPipeline
    {
        private sealed class CastState
        {
            public Id SkillId;
            public SkillDef Def = default!;
            public IReadOnlyList<Id> Targets = Array.Empty<Id>();
            public bool IsChannel;
            public double Remaining;
            public double TickInterval;
            public double TickAccumulator;
            public IReadOnlyList<(Id PowerType, double Amount)> ModifiedCost = Array.Empty<(Id, double)>();
            public (Id SkillId, IReadOnlyList<Id> Targets)? Queued;
        }

        private readonly SkillDefCache _defs;
        private readonly CooldownTracker _cooldowns;
        private readonly AuraHost _auraHost;
        private readonly EffectDispatcher _effects;
        private readonly ITargetHost _targetHost;
        private readonly IUnitAccess _units;
        private readonly ISpatialQuery? _spatialQuery;
        private readonly IPowerHost _powerHost;
        private readonly SpellModResolver _spellMods;
        private readonly IEventBus _bus;
        private readonly SkillOptions _options;
        private readonly ISkillDiagnostics _diagnostics;

        private readonly Dictionary<Id, CastState> _casting = new Dictionary<Id, CastState>();
        private readonly Dictionary<(Id Unit, Id School), double> _schoolLocks = new Dictionary<(Id, Id), double>();

        private int _castInstanceSeq;
        private int _triggerDepth;

        public CastPipeline(
            SkillDefCache defs,
            CooldownTracker cooldowns,
            AuraHost auraHost,
            EffectDispatcher effects,
            ITargetHost targetHost,
            IUnitAccess units,
            ISpatialQuery? spatialQuery,
            IPowerHost powerHost,
            SpellModResolver spellMods,
            IEventBus bus,
            SkillOptions options,
            ISkillDiagnostics diagnostics)
        {
            _defs = defs ?? throw new ArgumentNullException(nameof(defs));
            _cooldowns = cooldowns ?? throw new ArgumentNullException(nameof(cooldowns));
            _auraHost = auraHost ?? throw new ArgumentNullException(nameof(auraHost));
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
            _targetHost = targetHost ?? throw new ArgumentNullException(nameof(targetHost));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _spatialQuery = spatialQuery;
            _powerHost = powerHost ?? throw new ArgumentNullException(nameof(powerHost));
            _spellMods = spellMods ?? throw new ArgumentNullException(nameof(spellMods));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            _bus.Subscribe(RulesEventKeys.AuraApplied, OnAuraApplied);
            _bus.Subscribe(RulesEventKeys.CombatDamageDealt, OnDamageDealt);
        }

        public bool IsCasting(Id unitId) => _casting.ContainsKey(unitId);

        // -----------------------------------------------------------------
        // 施法请求入口
        // -----------------------------------------------------------------

        public CastResult CastSkill(Id casterId, Id skillId, IReadOnlyList<Id> targets)
        {
            var safeTargets = targets ?? Array.Empty<Id>();

            if (_casting.TryGetValue(casterId, out var activeState))
            {
                if (activeState.Remaining <= _options.QueueWindow)
                {
                    activeState.Queued = (skillId, safeTargets);
                    return CastResult.Ok(NextCastInstanceId());
                }

                // 判断记录：06 第 3.6 节只描述了"窗口内入队"的行为，未规定窗口外再次施法请求的
                // 处理方式；本模块拍板窗口外一律拒绝（呼应"每单位一个队列槽"——不支持排更多队）。
                // 契约缺口已补齐：原实现这里复用 OnCooldown 作为最接近的失败语义，集成任务已给
                // CastFailureReason 补上专门的 Busy（施法者当前"不可用"，原因是仍在读条/引导而非
                // 真正的冷却），见该原因码注释；本处改用 Busy，OnCooldown 恢复只表示步骤 3 冷却/
                // 充能未就绪。
                return Fail(casterId, skillId, CastFailureReason.Busy);
            }

            return TryStartCast(casterId, skillId, safeTargets);
        }

        private CastResult TryStartCast(Id casterId, Id skillId, IReadOnlyList<Id> targets)
        {
            if (!_defs.TryGetSkillDef(skillId, out var def))
            {
                return Fail(casterId, skillId, CastFailureReason.UnknownSkill);
            }

            var overridden = _auraHost.ResolveSkillOverride(casterId, skillId);
            if (overridden.HasValue && _defs.TryGetSkillDef(overridden.Value, out var overriddenDef))
            {
                skillId = overridden.Value;
                def = overriddenDef;
            }

            if (def.IsPassive)
            {
                return Fail(casterId, skillId, CastFailureReason.PassiveSkill);
            }

            // 步骤 1：存活与状态
            if (!_units.IsAlive(casterId))
            {
                return Fail(casterId, skillId, CastFailureReason.Dead);
            }

            var control = _auraHost.GetControlFlags(casterId);
            const ControlFlags fullyIncapacitated = ControlFlags.NoCast | ControlFlags.NoMove | ControlFlags.NoAttack;
            if ((control & fullyIncapacitated) == fullyIncapacitated)
            {
                return Fail(casterId, skillId, CastFailureReason.Stunned);
            }

            if ((control & ControlFlags.NoCast) != 0)
            {
                return Fail(casterId, skillId, CastFailureReason.Silenced);
            }

            // 步骤 2：学派锁定
            if (GetSchoolLockRemaining(casterId, def.School) > 0)
            {
                return Fail(casterId, skillId, CastFailureReason.SchoolLocked);
            }

            // 步骤 3：冷却/充能
            if (!_cooldowns.IsSkillReady(casterId, def))
            {
                var reason = def.HasCharges && _cooldowns.GetCharges(casterId, def) <= 0
                    ? CastFailureReason.NoCharges
                    : CastFailureReason.OnCooldown;
                return Fail(casterId, skillId, reason);
            }

            // 步骤 4：公共冷却
            if (_options.GcdEnabled && def.RespectsGcd && !_cooldowns.IsGcdReady(casterId))
            {
                return Fail(casterId, skillId, CastFailureReason.GcdActive);
            }

            // 步骤 5：资源
            var modifiedCost = ComputeCost(casterId, def);
            foreach (var (powerType, amount) in modifiedCost)
            {
                if (_powerHost.GetPower(casterId, powerType) < amount)
                {
                    return Fail(casterId, skillId, CastFailureReason.InsufficientPower);
                }
            }

            // 步骤 6：目标合法性
            var resolvedTargets = targets.Count > 0 ? targets : _targetHost.Resolve(def.TargetShapeRef, casterId);
            if (resolvedTargets.Count == 0)
            {
                return Fail(casterId, skillId, CastFailureReason.NoValidTarget);
            }

            // 步骤 7：距离与视线（Range == 0 表示无限制/作用于自身，见 06 第 3.1 节）
            if (def.Range > 0)
            {
                var casterPos = _units.GetPosition(casterId);
                foreach (var targetId in resolvedTargets)
                {
                    if (Vec2.Distance(casterPos, _units.GetPosition(targetId)) > def.Range)
                    {
                        return Fail(casterId, skillId, CastFailureReason.OutOfRange);
                    }
                }

                if (_spatialQuery != null)
                {
                    foreach (var targetId in resolvedTargets)
                    {
                        if (!_spatialQuery.HasLineOfSight(casterPos, _units.GetPosition(targetId)))
                        {
                            return Fail(casterId, skillId, CastFailureReason.LineOfSight);
                        }
                    }
                }
            }

            // 步骤 8：读条/引导
            return EnterCastOrChannel(casterId, skillId, def, resolvedTargets, modifiedCost);
        }

        private CastResult EnterCastOrChannel(
            Id casterId, Id skillId, SkillDef def, IReadOnlyList<Id> targets, IReadOnlyList<(Id, double)> modifiedCost)
        {
            var castInstanceId = NextCastInstanceId();
            var castTime = ComputeCastTime(casterId, def);
            var isChannel = def.ChannelTime > 0;

            _bus.Enqueue(new SkillCastStartEvent(casterId, skillId, isChannel ? def.ChannelTime : castTime));

            if (!isChannel && castTime <= 0)
            {
                // 瞬发：步骤 8 立即完成，直接执行步骤 9。
                DeductResources(casterId, def.Id, modifiedCost);
                StartCooldownAndGcd(casterId, def);
                ExecuteEffectsOnly(casterId, def, targets);
                _bus.Enqueue(new SkillCastSuccessEvent(casterId, skillId, targets));
                return CastResult.Ok(castInstanceId);
            }

            if (isChannel)
            {
                // 判断记录：06 第 3.6 节步骤 9 只描述了"读条/引导完成后"统一扣资源进冷却，未单独
                // 规定引导类技能资源/冷却的扣减时点；本模块拍板在引导开始（步骤 8）时一次性扣除，
                // 避免"引导中途打断是否退还部分资源"这一更复杂的分摊语义，与 06 第 3.6 节"打断"
                // 小节只提到"中止步骤 8"、未提资源找回一致（不找回）。
                DeductResources(casterId, def.Id, modifiedCost);
                StartCooldownAndGcd(casterId, def);
            }

            var state = new CastState
            {
                SkillId = skillId,
                Def = def,
                Targets = targets,
                IsChannel = isChannel,
                Remaining = isChannel ? def.ChannelTime : castTime,
                TickInterval = isChannel ? ComputeChannelTickInterval(def) : 0,
                ModifiedCost = modifiedCost,
            };

            _casting[casterId] = state;
            return CastResult.Ok(castInstanceId);
        }

        // -----------------------------------------------------------------
        // Update：推进读条/引导
        // -----------------------------------------------------------------

        public void Update(double dt)
        {
            AdvanceSchoolLocks(dt);

            var casterIds = new List<Id>(_casting.Keys);
            foreach (var casterId in casterIds)
            {
                AdvanceOne(casterId, dt);
            }
        }

        /// <summary>
        /// H4 补齐（离散模式"读条跨回合"，见 <c>SkillHost.AdvanceCastForActor</c>/
        /// <c>SkillTickHandler</c> 判断记录）：只推进 <paramref name="casterId"/> 自己的读条/引导
        /// 剩余时间与周期效果（逻辑与 <see cref="Update"/> 对单个施法者所做的完全一致，
        /// <see cref="Update"/> 现改为对 <see cref="_casting"/> 里每个施法者各调用一次本方法，
        /// 行为不变），但只处理这一个施法者，不遍历全部在场单位——离散模式下"我方读条是否推进"
        /// 应当只在该行动者自己的离散步内发生（一步 = 该行动者的一回合），不能像连续模式那样
        /// 每个 tick 对全体施法者统一推进（离散步是"以行动者为粒度"，见 03 第 4.2 节步骤 1、
        /// <c>Core.Foundation.SimLoop.WorldSim</c> 判断记录）。该单位当前未在读条/引导时空操作。不推进
        /// <see cref="AdvanceSchoolLocks"/>——学派锁定与"世界时钟"一样按轮统一推进（见
        /// <c>SkillHost.AdvanceRoundTimers</c>），不属于"该行动者自己的时间"。
        /// </summary>
        public void AdvanceOne(Id casterId, double dt)
        {
            if (!_casting.TryGetValue(casterId, out var state))
            {
                return;
            }

            if (state.IsChannel)
            {
                state.TickAccumulator += dt;
                while (state.TickInterval > 0 && state.TickAccumulator >= state.TickInterval && _casting.ContainsKey(casterId))
                {
                    state.TickAccumulator -= state.TickInterval;
                    ExecuteEffectsOnly(casterId, state.Def, state.Targets);
                }
            }

            if (!_casting.ContainsKey(casterId))
            {
                // 引导期间的效果触发了自我打断（例如控制类效果）。
                return;
            }

            state.Remaining -= dt;
            if (state.Remaining <= 0)
            {
                FinishCast(casterId, state);
            }
        }

        private void FinishCast(Id casterId, CastState state)
        {
            _casting.Remove(casterId);

            if (!state.IsChannel)
            {
                DeductResources(casterId, state.Def.Id, state.ModifiedCost);
                StartCooldownAndGcd(casterId, state.Def);
                ExecuteEffectsOnly(casterId, state.Def, state.Targets);
            }

            _bus.Enqueue(new SkillCastSuccessEvent(casterId, state.SkillId, state.Targets));

            if (state.Queued.HasValue)
            {
                var (queuedSkill, queuedTargets) = state.Queued.Value;
                TryStartCast(casterId, queuedSkill, queuedTargets);
            }
        }

        // -----------------------------------------------------------------
        // 打断
        // -----------------------------------------------------------------

        public void Interrupt(Id unitId, Id interrupterId, Id? lockSchool, double lockDuration)
        {
            if (!_casting.TryGetValue(unitId, out var state))
            {
                return;
            }

            _casting.Remove(unitId);

            if (lockSchool.HasValue)
            {
                _schoolLocks[(unitId, lockSchool.Value)] = lockDuration;
            }

            _bus.Enqueue(new SkillCastInterruptedEvent(unitId, state.SkillId, interrupterId));
        }

        /// <summary>供调用方（移动系统）在单位位移时通知（见 06 第 3.1 节 <c>interrupt_flags</c>
        /// 的 <c>movement</c> 项）：若正在读条/引导且该技能声明了 <see cref="InterruptFlags.Movement"/>，
        /// 视为自我打断。</summary>
        public void NotifyMoved(Id unitId)
        {
            if (_casting.TryGetValue(unitId, out var state) && (state.Def.InterruptFlags & InterruptFlags.Movement) != 0)
            {
                Interrupt(unitId, unitId, null, 0);
            }
        }

        private void OnAuraApplied(IEvent evt)
        {
            if (evt is AuraAppliedEvent applied
                && _casting.TryGetValue(applied.TargetId, out var state)
                && (state.Def.InterruptFlags & InterruptFlags.Control) != 0
                && (_auraHost.GetControlFlags(applied.TargetId) & ControlFlags.NoCast) != 0)
            {
                Interrupt(applied.TargetId, applied.SourceId, null, 0);
            }
        }

        private void OnDamageDealt(IEvent evt)
        {
            if (evt is CombatDamageDealtEvent dmg
                && _casting.TryGetValue(dmg.TargetId, out var state)
                && (state.Def.InterruptFlags & InterruptFlags.DamageTaken) != 0)
            {
                Interrupt(dmg.TargetId, dmg.SourceId, null, 0);
            }
        }

        // -----------------------------------------------------------------
        // 触发链（Proc / trigger_spell 共用，见类型注释）
        // -----------------------------------------------------------------

        internal bool TriggerCast(Id casterId, Id skillId, IReadOnlyList<Id> targets)
        {
            if (_triggerDepth >= _options.MaxTriggerDepth)
            {
                _diagnostics.Error(
                    $"触发链深度达到上限 {_options.MaxTriggerDepth}（casterId=\"{casterId}\", skillId=\"{skillId}\"），" +
                    "已拒绝本次触发，防止无限递归（见落地方案 T2-6 禁止事项）");
                return false;
            }

            if (!_defs.TryGetSkillDef(skillId, out var def))
            {
                _diagnostics.Warn($"TriggerCast 引用的技能 \"{skillId}\" 不存在，已忽略");
                return false;
            }

            var resolvedTargets = targets != null && targets.Count > 0 ? targets : new[] { casterId };

            _triggerDepth++;
            try
            {
                ExecuteEffectsOnly(casterId, def, resolvedTargets);
            }
            finally
            {
                _triggerDepth--;
            }

            return true;
        }

        // -----------------------------------------------------------------
        // 帮助方法
        // -----------------------------------------------------------------

        private void ExecuteEffectsOnly(Id casterId, SkillDef def, IReadOnlyList<Id> targets)
        {
            foreach (var effect in def.Effects)
            {
                foreach (var targetId in targets)
                {
                    var school = ParamsX.GetIdOpt(effect.Params, "school") ?? def.School;
                    var baseValue = ParamsX.GetNumber(effect.Params, "base_value");
                    var coefficient = ParamsX.GetNumber(effect.Params, "coefficient");
                    var canMiss = effect.Kind != EffectKind.Heal;

                    var context = new EffectContext(
                        casterId, targetId, def.Id, effect.Kind, school, baseValue, coefficient,
                        effect.Params, auraInstanceId: null, isPeriodic: false, canCrit: true, canMiss: canMiss,
                        tags: def.Tags);

                    _effects.ApplyEffect(context);
                }
            }
        }

        private double ComputeCastTime(Id casterId, SkillDef def) =>
            Math.Max(0, _spellMods.Apply(casterId, SpellModDimension.CastTime, def.Id, def.School, def.Tags, def.CastTime));

        private IReadOnlyList<(Id, double)> ComputeCost(Id casterId, SkillDef def)
        {
            var result = new List<(Id, double)>(def.Cost.Count);
            foreach (var (powerType, amount) in def.Cost)
            {
                result.Add((powerType, _spellMods.Apply(casterId, SpellModDimension.Cost, def.Id, def.School, def.Tags, amount)));
            }

            return result;
        }

        private void DeductResources(Id casterId, Id skillId, IReadOnlyList<(Id PowerType, double Amount)> cost)
        {
            foreach (var (powerType, amount) in cost)
            {
                _powerHost.ModifyPower(casterId, powerType, -amount, skillId);
            }
        }

        private void StartCooldownAndGcd(Id casterId, SkillDef def)
        {
            var modifiedCooldown = _spellMods.Apply(casterId, SpellModDimension.Cooldown, def.Id, def.School, def.Tags, def.CooldownDuration);
            _cooldowns.StartCooldown(casterId, def, modifiedCooldown);

            if (_options.GcdEnabled && def.RespectsGcd)
            {
                _cooldowns.StartGcd(casterId, _options.GcdDuration);
            }
        }

        private static double ComputeChannelTickInterval(SkillDef def)
        {
            foreach (var effect in def.Effects)
            {
                if (effect.Params.ContainsKey("tick_interval"))
                {
                    return ParamsX.GetNumber(effect.Params, "tick_interval", def.ChannelTime);
                }
            }

            return def.ChannelTime;
        }

        private double GetSchoolLockRemaining(Id unitId, Id school) =>
            _schoolLocks.TryGetValue((unitId, school), out var v) ? Math.Max(0, v) : 0;

        private void AdvanceSchoolLocks(double dt)
        {
            var keys = new List<(Id, Id)>(_schoolLocks.Keys);
            foreach (var key in keys)
            {
                _schoolLocks[key] = Math.Max(0, _schoolLocks[key] - dt);
            }
        }

        private Id NextCastInstanceId() => new Id($"skill.cast_inst_{++_castInstanceSeq}");

        private CastResult Fail(Id casterId, Id skillId, CastFailureReason reason)
        {
            _bus.Enqueue(new SkillCastFailedEvent(casterId, skillId, reason));
            return CastResult.Fail(reason);
        }
    }
}
