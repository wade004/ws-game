using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
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
    /// <c>trigger_spell</c> 效果原语共用）超过 <see cref="SkillOptions.MaxTriggerDepth"/> 直接拒绝
    /// （见落地方案 T2-6 禁止事项"禁止触发链无限递归"）。
    /// </para>
    /// <para>
    /// RC-01 收边勘误：深度不再用一个 ambient 计数器（进入 <c>TriggerCast</c> 自增、退出自减）
    /// 维护——原实现的注释曾声称"本引擎单线程同步执行，ambient 计数器不需要额外的调用上下文对象
    /// 即可正确工作"，但这个假设只对"同一次 <see cref="TriggerCast"/> 调用栈内的同步嵌套"成立：
    /// <c>trigger_spell</c> 效果原语确实是同步嵌套调用（<see cref="EffectDispatcher.ApplyTriggerSpell"/>
    /// 在 <see cref="ExecuteEffectsOnly"/> 执行到一半时直接方法调用 <see cref="TriggerCast"/>），但
    /// <see cref="ProcHost"/> 由 <c>combat.damage_dealt</c>/<c>combat.heal_done</c> 一类事件触发时，
    /// 这些事件是经 <see cref="IEventBus.Enqueue"/> 入队、在后续某个
    /// <see cref="IEventBus.DispatchPending"/> pass 才被派发的（见 <c>core/foundation/event_bus</c>
    /// README"同步派发 + tick 末批处理"）——产生该事件的那次 <see cref="TriggerCast"/> 调用早已
    /// 返回、ambient 计数器已经归零，深度预算对这条路径完全失效，只能靠与技能触发链语义无关的
    /// <see cref="EventBusOptions.MaxDispatchPasses"/> 全局熔断兜底（审计 RC-01）。现在深度改为随
    /// 数据显式传播：<see cref="TriggerCast"/> 的调用方（<see cref="ProcHost"/>/
    /// <see cref="EffectDispatcher.ApplyTriggerSpell"/>）传入"触发本次调用的深度"
    /// （分别读自触发事件的 <see cref="EffectContext.TriggerChainDepth"/>／当前
    /// <see cref="EffectContext"/> 自身的深度），本方法校验后 +1 传给
    /// <see cref="ExecuteEffectsOnly"/> 构造的 <see cref="EffectContext"/>，效果落地事件
    /// （<c>combat.damage_dealt</c>/<c>combat.heal_done</c> 等，见 <see cref="ITriggerChainEvent"/>）
    /// 把这个深度戳到事件上——不管事件是同步 <c>PublishImmediate</c> 还是异步 <c>Enqueue</c>
    /// 派发，深度都随事件本身传播，不再依赖调用栈是否还"活着"。
    /// </para>
    /// </summary>
    public sealed class CastPipeline
    {
        /// <summary>
        /// 第五轮外部审核相邻缺口根治（architecture/落地计划/audit-5e779c6-20260907，同
        /// <see cref="CooldownTracker._currentFactor"/>/<see cref="AuraHost._currentFactor"/>
        /// 同批语义、同一套推导——当前模式 1 个计时单位相当于连续模式（数据 authoring 的规范单位）
        /// 多少秒；初始 1.0，随 <see cref="RescaleAll"/> 每次模式切换累乘更新）：<see
        /// cref="EnterCastOrChannel"/> 施放当下从 <see cref="SkillDef"/> 读到的原始
        /// <c>cast_time</c>/<c>channel_time</c>（经 <see cref="ComputeCastTime"/>/<see
        /// cref="ComputeChannelTickInterval"/>）此前直接原样喂给 <see cref="CastState.Remaining"/>/
        /// <see cref="CastState.TickInterval"/>，不管施放当下究竟处于连续还是离散模式——技能若是在
        /// 离散战斗<b>进行中</b>才第一次被读条/引导（不是"连续模式下已有读条、切换时刻被换算"这条
        /// R05 已解决的路径），按连续模式秒数 authoring 的原始时长会被离散模式按轮推进的 <see
        /// cref="AdvanceOne"/> 直接当成"轮数"消耗，与 <see cref="CooldownTracker.StartCooldown"/>
        /// 判断记录描述的是同一类缺口。本类型现持有 <see cref="_currentFactor"/>，<see
        /// cref="EnterCastOrChannel"/> 在把原始 <c>cast_time</c>/<c>channel_time</c>/引导周期
        /// <c>tick_interval</c> 写入 <see cref="CastState"/> 前先乘以该系数；<see cref="RescaleAll"/>
        /// 同时把已经在读条/引导中的既有 <see cref="CastState"/>（<c>Remaining</c>/
        /// <c>TickInterval</c>/<c>TickAccumulator</c>）按 <paramref name="factor"/> 换算——这是
        /// <see cref="AuraHost.RescaleAll"/> 同步换算 <c>PeriodicAccumulators</c> 判断记录的直接
        /// 类比：既有读条/引导若不随切换换算，"已读条时长/总时长""累加器/tick_interval 还差多久触发
        /// 下一跳"这些比例关系会在切换瞬间被打破。
        /// </summary>
        private double _currentFactor = 1.0;

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

            /// <summary>N19 收边补齐：本次读条/引导开始时的原始时长（引导为 <c>channel_time</c>、
            /// 读条为 <c>cast_time</c>，均已按当时 SpellMod 修正），与本次施法开始时发布的
            /// <see cref="SkillCastStartEvent.CastTime"/> 同一个值——<see cref="FinishCast"/> 完成时
            /// 原样戳到 <see cref="SkillCastSuccessEvent.CastTimeSeconds"/> 上，供表现层区分"这是
            /// 一次真正花了时间的读条/引导完成"（见 SkillCastSuccessEvent.IsInstant 判断记录）。</summary>
            public double CastTimeSeconds;
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

            // RC-03 收边补齐：施法者死亡/被销毁取消读条/引导/队列（见 OnCasterDiedOrDestroyed
            // 判断记录）——此前只处理控制类打断（OnAuraApplied）与受伤打断（OnDamageDealt），完全
            // 不订阅死亡/销毁，死亡者会继续在 AdvanceOne/FinishCast 里扣资源并结算，销毁后更会因为
            // 访问已注销的 Powers/Unit 资源而抛异常（审计 RC-03）。
            _bus.Subscribe<UnitDiedEvent>(RulesEventKeys.UnitDied, evt => OnCasterDiedOrDestroyed(evt.UnitId));
            _bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, evt => OnCasterDiedOrDestroyed(evt.EntityId));
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

            // 步骤 4：公共冷却（离散步内恒通过，见 06 第 3.6 节"离散模式下的解释"、
            // SkillOptions.IsDiscreteStep 注释）
            var isDiscreteStep = _options.IsDiscreteStep?.Invoke() ?? false;
            if (_options.GcdEnabled && def.RespectsGcd && !isDiscreteStep && !_cooldowns.IsGcdReady(casterId))
            {
                return Fail(casterId, skillId, CastFailureReason.GcdActive);
            }

            // 步骤 5：资源（06 第 3.6 节表格：本步只检查"cost 是否够；离散模式下另检查
            // action_cost 行动点是否够"——是"是否够"的校验，不是扣除；真正扣除见步骤 9）
            var modifiedCost = ComputeCost(casterId, def);
            foreach (var (powerType, amount) in modifiedCost)
            {
                if (_powerHost.GetPower(casterId, powerType) < amount)
                {
                    return Fail(casterId, skillId, CastFailureReason.InsufficientPower);
                }
            }

            // 步骤 6：目标合法性
            // N10 收边补齐（外部审计 68c9bed，P2）：调用方显式传入 targets 时，此前直接跳过
            // ITargetHost.Resolve 整条"来源收集 → 过滤 → 排序 → 截断 → 回退"管线，连带把目标链
            // 声明的 filters（tag/expr 等额外目标条件，见 06 第 5 节）也一并绕过——配置要求
            // "目标必须是 undead"的技能，显式指定一个非 undead 单体目标仍会成功。显式目标不需要
            // 来源收集/排序/截断/回退（调用方已经给定具体目标），但额外目标条件必须继续生效，改用
            // ITargetHost.FilterExplicitTargets 只跑"过滤"这一步（关系类过滤的 AI 侧场景已在
            // ai 模块单独处理，见该接口方法判断记录，不在本步骤重复）。
            var resolvedTargets = targets.Count > 0
                ? _targetHost.FilterExplicitTargets(def.TargetShapeRef, casterId, targets)
                : _targetHost.Resolve(def.TargetShapeRef, casterId);
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

            // RC-04 收边勘误：行动点消耗（离散模式补充，06 第 3.1 节 action_cost）原来插在步骤 5
            // 与步骤 6 之间——TryConsumeActionPoints 是"检查是否够 + 原子扣除"合一的委托（不同于
            // 上面的资源检查：Power 类资源在这一步只探测余量，真正扣除延后到步骤 9 的
            // DeductResources；行动点没有对应的"仅探测"接口，只能整体挪动调用时机），若挪到目标/
            // 射程/视线检查之前，会出现"目标不存在/超距/无视线导致本次施法必然失败"时行动点已经
            // 被扣掉的缺陷（见外部审计 RC-04、validation-repros.txt R6：超距仍产生 1 次消费）。
            // 现在移到全部校验通过、即将进入步骤 8 读条/引导之前——本方法从这里往下不再有会失败
            // 的校验分支，行动点与后续步骤 9 的资源/冷却扣除一样，只在"确定会真正开始读条/引导"
            // 时才真正发生。
            if (isDiscreteStep && def.ActionCost > 0)
            {
                if (_options.TryConsumeActionPoints == null || !_options.TryConsumeActionPoints(casterId, def.ActionCost))
                {
                    return Fail(casterId, skillId, CastFailureReason.InsufficientActionPoints);
                }
            }

            // 步骤 8：读条/引导
            return EnterCastOrChannel(casterId, skillId, def, resolvedTargets, modifiedCost);
        }

        private CastResult EnterCastOrChannel(
            Id casterId, Id skillId, SkillDef def, IReadOnlyList<Id> targets, IReadOnlyList<(Id, double)> modifiedCost)
        {
            var castInstanceId = NextCastInstanceId();
            // 判断记录见类型顶部"_currentFactor"：cast_time/channel_time 都是从 SkillDef 原始数据
            // 读出的一次性初始值，乘 _currentFactor 折算成当前生效模式的计时单位。isChannel 的判定
            // 用未折算的 def.ChannelTime——_currentFactor 恒为正数，乘法不改变 > 0 判定结果。
            var castTime = ComputeCastTime(casterId, def) * _currentFactor;
            var channelTime = def.ChannelTime * _currentFactor;
            var isChannel = def.ChannelTime > 0;

            _bus.Enqueue(new SkillCastStartEvent(casterId, skillId, isChannel ? channelTime : castTime));

            if (!isChannel && castTime <= 0)
            {
                // 瞬发：步骤 8 立即完成，直接执行步骤 9。
                DeductResources(casterId, def.Id, modifiedCost);
                StartCooldownAndGcd(casterId, def);
                ExecuteEffectsOnly(casterId, def, targets);
                // N19 收边补齐：瞬发——IsInstant=true，CastTimeSeconds=0（见 SkillCastSuccessEvent
                // 判断记录）。
                _bus.Enqueue(new SkillCastSuccessEvent(casterId, skillId, targets, isInstant: true, castTimeSeconds: 0));
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
                Remaining = isChannel ? channelTime : castTime,
                TickInterval = isChannel ? ComputeChannelTickInterval(def) * _currentFactor : 0,
                ModifiedCost = modifiedCost,
                // N19 收边补齐：与本次 SkillCastStartEvent.CastTime 同一个值，见 CastState.CastTimeSeconds
                // 判断记录。
                CastTimeSeconds = isChannel ? channelTime : castTime,
            };

            _casting[casterId] = state;
            return CastResult.Ok(castInstanceId);
        }

        // -----------------------------------------------------------------
        // Update：推进读条/引导
        // -----------------------------------------------------------------

        public void Update(double dt)
        {
            // RC-07 收边勘误：学派锁定推进已经挪到 SkillHost.AdvanceRoundTimers（见
            // AdvanceSchoolLocks 判断记录）——本方法（连续模式每 tick 调用）不再在这里重复推进，
            // 避免 SkillHost.Update 内 "_pipeline.Update(dt) + AdvanceRoundTimers(dt)" 两次调用对
            // 同一个 dt 各推进一次、变成双倍衰减速度。

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

            // RC-03 收边补齐：完成前重验施法者仍然存活/存在——构造函数订阅的死亡/销毁事件是主要
            // 清理路径（见 OnCasterDiedOrDestroyed），本处是防御性兜底：例如死亡结算与本次推进
            // 恰好落在同一批 DispatchPending 内、事件尚未先于本次调用被处理的边界情况，或调用方
            // 绕过事件总线直接推进的场景（见类型注释判断记录）。命中则静默丢弃整个 CastState——
            // 不推进周期效果、不结算、不进冷却、不发任何事件（死亡/销毁本身没有"这次读条被谁打断"
            // 这个语义，交给死亡/销毁事件驱动的 Interrupt 路径发 SkillCastInterruptedEvent；这里
            // 命中纯属兜底，理论上不会在正常事件顺序下触发）。
            if (!IsCasterStillValid(casterId))
            {
                _casting.Remove(casterId);
                return;
            }

            if (state.IsChannel)
            {
                // CR130-04 根治（外部审计 audit-5c444f1-20260908）：本次推进的 dt 可能已经超出引导
                // 剩余时间（引导会在这次 Update/AdvanceOne 内结束）——只把"引导仍然有效"的那一段时间
                // （Min(dt, Remaining)）计入周期累加器，不能把引导已经结束之后的那段 dt 也当作还在
                // 引导中继续结算周期效果，否则会多算一跳（复现：channel_time=0.5、tick_interval=1、
                // Update(1) 期望 0 次实际 1 次）。与 AuraHost R07 对周期效果尾跳的 Min(dt,Remaining)
                // 处理同一惯例。
                var channelDt = dt < state.Remaining ? dt : Math.Max(0, state.Remaining);
                state.TickAccumulator += channelDt;
                while (state.TickInterval > 0 && state.TickAccumulator >= state.TickInterval && _casting.ContainsKey(casterId))
                {
                    state.TickAccumulator -= state.TickInterval;
                    var tickTargets = FilterDestroyedTargets(state.Targets);
                    if (tickTargets.Count > 0)
                    {
                        ExecuteEffectsOnly(casterId, state.Def, tickTargets);
                    }
                }
            }

            if (!_casting.ContainsKey(casterId))
            {
                // 引导期间的效果触发了自我打断（例如控制类效果，或本次周期效果本身导致施法者/
                // 目标死亡进而经 RC-03 死亡订阅自我打断）。
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

            // RC-03 收边补齐：同 AdvanceOne 顶部的判断记录——完成前重验施法者仍然有效，命中则
            // 静默丢弃（不结算、不扣资源、不进冷却、不发成功事件、不启动排队的下一个技能）。
            if (!IsCasterStillValid(casterId))
            {
                return;
            }

            var targets = FilterDestroyedTargets(state.Targets);

            if (!state.IsChannel)
            {
                DeductResources(casterId, state.Def.Id, state.ModifiedCost);
                StartCooldownAndGcd(casterId, state.Def);
                if (targets.Count > 0)
                {
                    ExecuteEffectsOnly(casterId, state.Def, targets);
                }
            }

            // N19 收边补齐：非瞬发（真正经历过读条/引导才走到这里）——IsInstant=false，
            // CastTimeSeconds 取本次开始时记录的原始时长（见 CastState.CastTimeSeconds 判断记录）。
            _bus.Enqueue(new SkillCastSuccessEvent(casterId, state.SkillId, state.Targets, isInstant: false, castTimeSeconds: state.CastTimeSeconds));

            if (state.Queued.HasValue)
            {
                var (queuedSkill, queuedTargets) = state.Queued.Value;
                TryStartCast(casterId, queuedSkill, queuedTargets);
            }
        }

        /// <summary>RC-03 收边补齐：施法者是否仍然存在且存活（见 <see cref="AdvanceOne"/>/
        /// <see cref="FinishCast"/> 判断记录）。</summary>
        private bool IsCasterStillValid(Id casterId) => _units.Exists(casterId) && _units.IsAlive(casterId);

        /// <summary>RC-03 收边补齐：从目标列表里剔除已不存在（销毁）的单位——存在但已死亡的目标
        /// 仍保留在列表中交给下游各效果自行处理（伤害/治疗经 <see cref="Combat.Resolver.Resolve"/>
        /// 对死亡目标短路为 Miss，见该方法"对已死亡目标再结算"分支；其它效果原语按各自语义处理，
        /// 06 未要求施法管线本身对"目标已死但仍存在"做统一拦截）。全部目标仍存在时返回原列表，
        /// 不额外分配。</summary>
        private IReadOnlyList<Id> FilterDestroyedTargets(IReadOnlyList<Id> targets)
        {
            var anyDestroyed = false;
            for (var i = 0; i < targets.Count; i++)
            {
                if (!_units.Exists(targets[i]))
                {
                    anyDestroyed = true;
                    break;
                }
            }

            if (!anyDestroyed)
            {
                return targets;
            }

            var result = new List<Id>(targets.Count);
            foreach (var targetId in targets)
            {
                if (_units.Exists(targetId))
                {
                    result.Add(targetId);
                }
            }

            return result;
        }

        /// <summary>RC-03 收边补齐：施法者死亡（<c>unit.died</c>）或被销毁（<c>entity.destroyed</c>）
        /// 时取消其读条/引导/队列——两个事件都订阅是因为死亡与实体销毁未必同时发生（死亡后可能
        /// 留一段时间的尸体才真正销毁，见 <see cref="UnitDiedEvent"/>/<see cref="EntityDestroyedEvent"/>
        /// 判断记录），任一先到达都应立即取消，不等另一个。复用 <see cref="Interrupt"/>（interrupterId
        /// 传自身，不加学派锁）——与 <see cref="NotifyMoved"/> 的"自我打断"是同一惯例：死亡/销毁没有
        /// 引入新事件词汇表词条的必要，<c>skill.cast_interrupted</c> 已经能准确表达"这次读条/引导
        /// 没有正常完成"。<see cref="Interrupt"/> 本身对未在读条/引导中的单位是安全 no-op，本方法
        /// 因此天然幂等，不需要额外的 <c>_casting.ContainsKey</c> 前置判断。</summary>
        private void OnCasterDiedOrDestroyed(Id unitId) => Interrupt(unitId, unitId, null, 0);

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
                // CR130-03 根治（外部审计 audit-5c444f1-20260908）：lockDuration 是调用方按 authoring
                // 规范秒数传入的原始值（与 cast_time/cooldown_duration 同一口径），与
                // <see cref="EnterCastOrChannel"/> 写入 <c>Remaining</c>/<see
                // cref="CooldownTracker.StartCooldown"/> 写入冷却同款处理——写入前先乘
                // <see cref="_currentFactor"/> 折算成当前模式的计时单位，否则连续模式 authoring 的
                // "3 秒沉默"在离散模式下会被当成"3 轮沉默"。
                _schoolLocks[(unitId, lockSchool.Value)] = lockDuration * _currentFactor;
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

        /// <summary>
        /// <paramref name="chainDepth"/>：触发本次调用的触发链深度（0 = 由一次未经触发的根结算/
        /// 根效果发起，见类型注释"RC-01 收边勘误"）——调用方传入的是"触发它的那一层"的深度，
        /// 本方法校验通过后统一 +1 作为"本次触发实际执行"的深度，向下传播。
        /// </summary>
        internal bool TriggerCast(Id casterId, Id skillId, IReadOnlyList<Id> targets, int chainDepth)
        {
            if (chainDepth >= _options.MaxTriggerDepth)
            {
                _diagnostics.Error(
                    $"触发链深度达到上限 {_options.MaxTriggerDepth}（casterId=\"{casterId}\", skillId=\"{skillId}\"），" +
                    "已拒绝本次触发，防止无限递归（见落地方案 T2-6 禁止事项；RC-01 收边后深度随事件/" +
                    "效果上下文显式传播，覆盖跨 EventBus 异步派发 pass 的场景，不再依赖调用栈是否" +
                    "仍在同一次同步调用中）");
                return false;
            }

            if (!_defs.TryGetSkillDef(skillId, out var def))
            {
                _diagnostics.Warn($"TriggerCast 引用的技能 \"{skillId}\" 不存在，已忽略");
                return false;
            }

            var resolvedTargets = targets != null && targets.Count > 0 ? targets : new[] { casterId };

            ExecuteEffectsOnly(casterId, def, resolvedTargets, chainDepth + 1);
            return true;
        }

        // -----------------------------------------------------------------
        // 帮助方法
        // -----------------------------------------------------------------

        /// <summary><paramref name="chainDepth"/>：见类型注释"RC-01 收边勘误"，默认 0（正常施法
        /// 管线步骤 8/9 完成后的根结算，即 <see cref="EnterCastOrChannel"/>/<see cref="AdvanceOne"/>/
        /// <see cref="FinishCast"/> 三个调用点，均不显式传参）；<see cref="TriggerCast"/> 是唯一显式
        /// 传入非零值的调用点。</summary>
        private void ExecuteEffectsOnly(Id casterId, SkillDef def, IReadOnlyList<Id> targets, int chainDepth = 0)
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
                        tags: def.Tags, triggerChainDepth: chainDepth);

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

            var isDiscreteStep = _options.IsDiscreteStep?.Invoke() ?? false;
            if (_options.GcdEnabled && def.RespectsGcd && !isDiscreteStep)
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

        /// <summary>
        /// RC-07 收边勘误：原为 <see cref="Update"/>（连续模式每 tick 调用）内部私有步骤，只在
        /// 连续模式被推进——离散模式完全不调用 <see cref="Update"/>（见 <c>SkillTickHandler.Execute</c>
        /// 判断记录："连续步调用 SkillHost.Update；离散步只推进当前行动者自己的读条/引导 +
        /// 由 sim.round_ended 驱动 SkillHost.AdvanceRoundTimers"），学派锁定因此在离散模式下永远
        /// 不会衰减（见外部审计 RC-07）。现在改为 internal，由 <see cref="SkillHost.AdvanceRoundTimers"/>
        /// 统一调用——该方法本身在连续模式下经 <see cref="SkillHost.Update"/> 每 tick 调用一次
        /// （<c>dt=每 tick 的秒数</c>），在离散模式下经 <c>sim.round_ended</c> 每轮调用一次
        /// （<c>dt=1.0</c>），两种模式各自只有唯一一条推进路径，不会重复推进（见 <see cref="Update"/>
        /// 判断记录）。
        /// </summary>
        internal void AdvanceSchoolLocks(double dt)
        {
            var keys = new List<(Id, Id)>(_schoolLocks.Keys);
            foreach (var key in keys)
            {
                _schoolLocks[key] = Math.Max(0, _schoolLocks[key] - dt);
            }
        }

        /// <summary>
        /// 第五轮外部审核相邻缺口根治：见类型顶部 <see cref="_currentFactor"/> 判断记录。由
        /// <c>SkillHost.OnTimeModelRescaled</c> 与 <see cref="CooldownTracker.RescaleAll"/>/
        /// <see cref="AuraHost.RescaleAll"/> 同一批调用（同一次模式切换广播的
        /// <see cref="Core.Rules.Common.TimeModelRescaledEvent"/>）。<paramref name="factor"/>
        /// 语义同 <see cref="CooldownTracker.RescaleAll"/>：新单位下 1 个单位对应旧单位下
        /// <paramref name="factor"/> 个单位。
        /// </summary>
        public void RescaleAll(double factor)
        {
            if (factor <= 0)
            {
                throw new ArgumentException("factor 必须为正数", nameof(factor));
            }

            _currentFactor *= factor;

            foreach (var state in _casting.Values)
            {
                state.Remaining *= factor;
                state.TickInterval *= factor;
                state.TickAccumulator *= factor;
            }

            // CR130-03 根治：既有学派锁定倒计时（同 CooldownTracker.RescaleAll 换算既有冷却存量的
            // 判断记录）此前从未随模式切换换算——切换前后同一份剩余时间被两种模式的
            // AdvanceSchoolLocks(dt) 用不同单位重新解读，"锁 3 秒"在切换后可能变成"锁 3 轮"或反过来。
            var schoolLockKeys = new List<(Id, Id)>(_schoolLocks.Keys);
            foreach (var key in schoolLockKeys)
            {
                _schoolLocks[key] *= factor;
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
