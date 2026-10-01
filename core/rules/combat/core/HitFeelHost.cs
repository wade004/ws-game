using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.SimLoop;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Core.Rules.Combat
{
    /// <summary>
    /// 局部顿帧与受击裁决宿主（手感设计/03 第 3/4 节，ADR-0117）。
    /// <para>
    /// <b>三件事</b>：(1) <see cref="Evaluate"/>——纯函数式的受击裁决（韧性、霸体、<c>reaction_cap</c>、死亡优先、两侧顿帧 tick 数），
    /// 空间命中切片发 <c>combat.hit_confirmed</c> 前用它填字段；(2) 订阅 <c>combat.hit_confirmed</c> 把结论落地——冻结攻击方与受击方的
    /// 行动者动作时钟（局部顿帧）、打断受击方进行中的动作、登记硬直窗口并提交击退，发 <c>feel.hitstop_started/ended</c> 与
    /// <c>combat.reaction_applied</c>；(3) 接 instant（目标选择式）结算的 <c>combat.damage_dealt</c>/<c>combat.attack_avoided</c>，
    /// 合成同样的 <c>combat.hit_confirmed</c>，让目标选择式战斗也有顿帧与受击。
    /// </para>
    /// <para>
    /// <b>时序</b>：顿帧与硬直分别计时、互不包含——受击方先顿帧（动作时钟冻结），顿帧结束后的下一个 tick 才起算硬直
    /// （<c>staggered</c> 状态）；击退在硬直起算的同一 tick 开始。顿帧批次在 tick 末（<c>sim.tick_finished</c>）一次性落地：
    /// 同一批里同一单位取最大值（攻击方多目标命中不累加），冻结中再被命中取"剩余与新值之大者"。
    /// 硬直窗口的推进挂在 <c>sim.tick_started</c>（先于全部阶段处理器），所以运动仲裁在步骤 4 读到的 <see cref="IsStaggered"/>
    /// 与阶段处理器注册顺序无关。<b>订阅顺序约定</b>：<c>ActorActionClock.Attach</c> 先于本类构造（<see cref="HitFeelHostBase"/>
    /// 之外的装配用 <c>HitFeelAssembly</c> 保证），否则 <c>feel.hitstop_ended</c> 会晚一个 tick 发出。
    /// </para>
    /// <para>
    /// 手感系统未装配（没有 <see cref="IFeelJudgingSource"/>）时不创建本类；<see cref="Enabled"/> 为假、离散模式
    /// （<see cref="HitFeelOptions.IsDiscreteMode"/>）下全部入口静默：既有战斗行为逐位不变。
    /// </para>
    /// </summary>
    public sealed class HitFeelHost : IHitFeelArbiter, IHitReactionQuery, IDisposable
    {
        private sealed class BatchEntry
        {
            public Id AttackInstanceId;
            public Id AttackerId;
            public Id TargetId;
            public int AttackerTicks;
            public int TargetTicks;
        }

        private sealed class StaggerRec
        {
            public bool Active;
            public int Duration;
            public int StunTicks;
            public int Remaining;
            public int Elapsed;
            public bool KnockbackPending;
            public Vec2 KnockbackDirection;
            public double KnockbackDistance;
        }

        private readonly IEventBus _bus;
        private readonly IUnitAccess _units;
        private readonly IFeelJudgingSource _feel;
        private readonly IActorActionClockControl _clock;
        private readonly IStatHost _stats;
        private readonly double _stepSeconds;
        private readonly HitFeelOptions _options;
        private readonly IPowerHost? _powers;
        private readonly IActionStateQuery? _actions;
        private readonly IAuraQuery? _auras;
        private readonly List<IStaggerInterruptSink> _interruptSinks = new List<IStaggerInterruptSink>();
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();

        private readonly List<BatchEntry> _batch = new List<BatchEntry>();
        private readonly SortedSet<Id> _frozen = new SortedSet<Id>();

        // 本 tick 内已处理过 unit.died、尚未被"致死那一击"的伤害事件消费的单位：Resolver 先入队 unit.died、紧接着入队致死那一击的
        // combat.damage_dealt，所以"已见 unit.died 再见伤害事件"才是击杀；同一 tick 内更早入队的非致死命中看到的是"还没见到 unit.died"。
        private readonly HashSet<Id> _killPending = new HashSet<Id>();
        private readonly SortedDictionary<Id, StaggerRec> _staggers = new SortedDictionary<Id, StaggerRec>();
        private long _syntheticAttackCounter;
        private bool _disposed;

        /// <summary>击退执行口（运动层）。缺省 null——硬直照常，只是不位移。</summary>
        public IKnockbackSink? Knockback { get; set; }

        /// <summary>总开关；假时全部入口静默。</summary>
        public bool Enabled { get; set; } = true;

        public HitFeelHost(
            IEventBus bus,
            IUnitAccess units,
            IFeelJudgingSource feel,
            IActorActionClockControl clock,
            IStatHost stats,
            double stepSeconds,
            HitFeelOptions? options = null,
            IPowerHost? powers = null,
            IActionStateQuery? actions = null,
            IAuraQuery? auras = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _feel = feel ?? throw new ArgumentNullException(nameof(feel));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            if (!double.IsFinite(stepSeconds) || stepSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(stepSeconds));
            _stepSeconds = stepSeconds;
            _options = options ?? new HitFeelOptions();
            _powers = powers;
            _actions = actions;
            _auras = auras;

            _subscriptions.Add(bus.Subscribe<CombatHitConfirmedEvent>(RulesEventKeys.CombatHitConfirmed, OnHitConfirmed));
            _subscriptions.Add(bus.Subscribe<CombatDamageDealtEvent>(RulesEventKeys.CombatDamageDealt, OnDamageDealt));
            _subscriptions.Add(bus.Subscribe<CombatAttackAvoidedEvent>(RulesEventKeys.CombatAttackAvoided, OnAttackAvoided));
            _subscriptions.Add(bus.Subscribe<UnitDiedEvent>(RulesEventKeys.UnitDied, e =>
            {
                if (Active) _killPending.Add(e.UnitId);
                ReleaseUnit(e.UnitId);
            }));
            _subscriptions.Add(bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, e => ReleaseUnit(e.EntityId)));
            _subscriptions.Add(bus.Subscribe<TimeModelRescaledEvent>(RulesEventKeys.TimeModelRescaled, _ => ReleaseAll()));
            _subscriptions.Add(bus.Subscribe<SimTickStartedEvent>(SimEventKeys.TickStarted, _ => OnTickStarted()));
            _subscriptions.Add(bus.Subscribe<SimTickFinishedEvent>(SimEventKeys.TickFinished, _ => OnTickFinished()));
        }

        /// <summary>注册一个硬直打断口（时间线动作、读条类各一个）。</summary>
        public void AddInterruptSink(IStaggerInterruptSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            _interruptSinks.Add(sink);
        }

        /// <summary>是否生效：总开关开且不处于离散时间模型。</summary>
        public bool Active => Enabled && !(_options.IsDiscreteMode?.Invoke() ?? false);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (var i = 0; i < _subscriptions.Count; i++) _subscriptions[i].Dispose();
            _subscriptions.Clear();
        }

        // ================================================================== 只读查询

        public bool IsStaggered(Id unitId) => _staggers.TryGetValue(unitId, out var rec) && rec.Active;

        public bool IsDowned(Id unitId) => _staggers.TryGetValue(unitId, out var rec) && rec.Active && rec.Elapsed > rec.StunTicks;

        public int RemainingStaggerTicks(Id unitId) =>
            _staggers.TryGetValue(unitId, out var rec) ? (rec.Active ? rec.Remaining : rec.Duration) : 0;

        // ================================================================== 受击裁决

        /// <inheritdoc />
        public HitFeelOutcome Evaluate(in HitFeelInput input)
        {
            if (!Active) return HitFeelOutcome.None();
            if (!_units.Exists(input.AttackerId) || !_units.Exists(input.TargetId)) return HitFeelOutcome.None();

            var atk = input.AttackerFeel ?? _feel.ResolveJudging(input.AttackerId);
            var tgt = _feel.ResolveJudging(input.TargetId);
            var impact = atk.GetText(FeelFieldNames.ImpactClass);
            if (IsAvoided(input.HitResult)) return HitFeelOutcome.None(impact);

            // ---- 顿帧：攻击方时长取武器为主字段（击杀放大），受击方时长同；各自按所属单位的上限限幅。
            var attackerMs = atk.GetNumber(FeelFieldNames.AttackerHitstopMs);
            if (input.IsKill) attackerMs *= atk.GetNumber(FeelFieldNames.KillHitstopScale);
            var attackerTicks = Math.Min(
                Ticks(attackerMs),
                Ticks(atk.GetNumber(FeelFieldNames.AttackerHitstopCapMs)));

            var targetTicks = 0;
            if (!input.IsKill)
            {
                targetTicks = Math.Min(
                    Ticks(atk.GetNumber(FeelFieldNames.TargetHitstopMs)),
                    Ticks(tgt.GetNumber(FeelFieldNames.HitstopCapMs)));
            }

            // ---- 受击反应：死亡 → 霸体 → 韧性 → 冲击等级映射 → reaction_cap。
            HitReaction reaction;
            if (input.IsKill)
            {
                reaction = HitReaction.Death;
            }
            else if (IsSuperArmor(input.TargetId))
            {
                reaction = HitReaction.None;
                if (!_options.SuperArmorTargetHitstop) targetTicks = 0;
            }
            else
            {
                var power = atk.GetNumber(FeelFieldNames.StaggerPower);
                reaction = power <= ReadPoise(input.TargetId) ? HitReaction.Flinch : MapImpact(impact);
                reaction = ApplyCap(reaction, tgt.GetText(FeelFieldNames.ReactionCap));
            }

            return new HitFeelOutcome(impact, attackerTicks, targetTicks, reaction, ReactionDuration(reaction, tgt));
        }

        private static bool IsAvoided(HitResult result) =>
            result == HitResult.Miss || result == HitResult.Dodge || result == HitResult.Parry ||
            result == HitResult.Immune || result == HitResult.Invulnerable;

        private int Ticks(double milliseconds) => FeelCalibration.MillisecondsToTicks(milliseconds, _stepSeconds);

        private bool IsSuperArmor(Id targetId)
        {
            if (_actions != null && _actions.IsSuperArmor(targetId)) return true;
            return _options.SuperArmorAuraDef.HasValue && _auras != null && _auras.HasAura(targetId, _options.SuperArmorAuraDef.Value);
        }

        private double ReadPoise(Id targetId)
        {
            try
            {
                return _stats.GetStat(targetId, _options.PoiseStat);
            }
            catch (ArgumentException)
            {
                return 0.0;
            }
            catch (InvalidOperationException)
            {
                return 0.0;
            }
        }

        private HitReaction MapImpact(string impactClass) =>
            _options.ImpactReactions.TryGetValue(impactClass, out var reaction) ? reaction : _options.UnknownImpactReaction;

        private static HitReaction ApplyCap(HitReaction reaction, string cap)
        {
            HitReaction limit;
            switch (cap)
            {
                case "none": limit = HitReaction.None; break;
                case "flinch": limit = HitReaction.Flinch; break;
                case "stagger_light": limit = HitReaction.StaggerLight; break;
                case "stagger": limit = HitReaction.Stagger; break;
                case "knockback": limit = HitReaction.Knockback; break;
                default: return reaction; // knockdown 即不封顿
            }

            return (int)reaction > (int)limit ? limit : reaction;
        }

        private static bool IsStaggerClass(HitReaction r) =>
            r == HitReaction.StaggerLight || r == HitReaction.Stagger || r == HitReaction.Knockback || r == HitReaction.Knockdown;

        private int StunTicks(JudgingFeelView target) => Ticks(target.GetNumber(FeelFieldNames.HitStunMs));

        private int ReactionDuration(HitReaction reaction, JudgingFeelView target)
        {
            if (!IsStaggerClass(reaction)) return 0;
            var duration = StunTicks(target);
            if (reaction == HitReaction.Knockdown) duration += Ticks(target.GetNumber(FeelFieldNames.DownedMs));
            return duration;
        }

        // ================================================================== instant 适配：由结算事件合成 hit_confirmed

        private void OnDamageDealt(CombatDamageDealtEvent e)
        {
            if (!Active || !_options.InstantModeConfirmations) return;
            // 光环周期伤害（既无技能 id 也无攻击实例 id）不是一次"命中"：不顿帧、不裁决。
            if (!e.SkillId.HasValue && !e.AttackInstanceId.HasValue)
            {
                // 周期伤害的致死那一击：同样消费掉 unit.died 留下的击杀标记，不留到 tick 末（该单位本 tick 内若复活再被 instant 命中不能被误判为击杀）。
                _killPending.Remove(e.TargetId);
                return;
            }

            if (_options.IsTimelineSkill != null && _options.IsTimelineSkill(e.SkillId))
            {
                // 时间线技能的击杀由时间线路径自己判（IsAlive）并发 combat.hit_confirmed；这里只消费 unit.died 留下的击杀标记（S11）。
                _killPending.Remove(e.TargetId);
                return;
            }

            PublishInstantConfirmation(e.SourceId, e.TargetId, e.SkillId, e.AttackInstanceId, e.HitResult, e.Amount, e.IsCrit);
        }

        private void OnAttackAvoided(CombatAttackAvoidedEvent e)
        {
            if (!Active || !_options.InstantModeConfirmations) return;
            if (!e.SkillId.HasValue && !e.AttackInstanceId.HasValue) return;
            if (_options.IsTimelineSkill != null && _options.IsTimelineSkill(e.SkillId)) return;
            PublishInstantConfirmation(e.SourceId, e.TargetId, e.SkillId, e.AttackInstanceId, e.HitResult, 0.0, false);
        }

        private void PublishInstantConfirmation(
            Id sourceId, Id targetId, Id? skillId, Id? attackInstanceId, HitResult hitResult, double amount, bool isCrit)
        {
            if (!_units.Exists(sourceId) || !_units.Exists(targetId)) return;

            var avoided = IsAvoided(hitResult);
            var isKill = !avoided && _killPending.Remove(targetId);
            var outcome = Evaluate(new HitFeelInput(sourceId, targetId, hitResult, amount, isKill));

            var maxHealth = _powers != null ? SafeMaxHealth(targetId) : 0.0;
            var ratio = maxHealth > 0.0 ? amount / maxHealth : 0.0;

            var from = _units.GetPosition(sourceId);
            var to = _units.GetPosition(targetId);
            var toward = to - from;
            Vec2 direction;
            if (toward.Length > 1e-9)
            {
                direction = toward * (1.0 / toward.Length);
            }
            else
            {
                var facing = _units.GetFacing(sourceId);
                direction = new Vec2(Math.Cos(facing), Math.Sin(facing));
            }

            // instant 没有接触几何：接触点取目标登记位置，接触法线取"从目标指向攻击方"的反方向（朝向攻击方的一面），
            // 世界方向取攻击方到目标（击退方向）——与 03 第 2.4 节"无接触几何时由适配层给出明确替代，不为空"一致。
            var instanceId = attackInstanceId ?? new Id("attack.instant." + (++_syntheticAttackCounter).ToString(System.Globalization.CultureInfo.InvariantCulture));
            _bus.Enqueue(new CombatHitConfirmedEvent(
                instanceId, 0, sourceId, targetId, skillId, hitResult, amount, ratio, isCrit, isKill,
                to, -direction, direction, outcome.ImpactClass, outcome.AttackerHitStopTicks, outcome.TargetHitStopTicks, outcome.Reaction));
        }

        private double SafeMaxHealth(Id unitId)
        {
            try
            {
                return _powers!.GetPowerMax(unitId, WellKnownPowers.Health);
            }
            catch (ArgumentException)
            {
                return 0.0;
            }
            catch (InvalidOperationException)
            {
                return 0.0;
            }
        }

        // ================================================================== 落地：顿帧批次、硬直、击退

        private void OnHitConfirmed(CombatHitConfirmedEvent e)
        {
            if (!Active || IsAvoided(e.HitResult)) return;

            if (e.AttackerHitStopTicks > 0 || e.TargetHitStopTicks > 0)
            {
                // 事件里的顿帧 tick 数可能来自不经 Evaluate 的发出方：落地前按所属单位的上限再限一次（幂等）。
                _batch.Add(new BatchEntry
                {
                    AttackInstanceId = e.AttackInstanceId,
                    AttackerId = e.SourceId,
                    TargetId = e.TargetId,
                    AttackerTicks = CapTicks(e.SourceId, FeelFieldNames.AttackerHitstopCapMs, e.AttackerHitStopTicks),
                    TargetTicks = CapTicks(e.TargetId, FeelFieldNames.HitstopCapMs, e.TargetHitStopTicks),
                });
            }

            ApplyReaction(e);
        }

        private int CapTicks(Id unitId, string capField, int ticks)
        {
            if (ticks <= 0 || !_units.Exists(unitId)) return ticks;
            var cap = Ticks(_feel.ResolveJudging(unitId).GetNumber(capField));
            return ticks < cap ? ticks : cap;
        }

        private void ApplyReaction(CombatHitConfirmedEvent e)
        {
            var reaction = e.Reaction;
            if (reaction == HitReaction.None) return;

            if (reaction == HitReaction.Death)
            {
                // 死亡优先于一切：不进硬直，已有的硬直窗口与冻结当场清零（死亡事件自己也会清一次，幂等）。
                ReleaseUnit(e.TargetId);
                _bus.Enqueue(new CombatReactionAppliedEvent(e.TargetId, reaction, e.SourceId, e.AttackInstanceId, 0));
                return;
            }

            if (!IsStaggerClass(reaction))
            {
                _bus.Enqueue(new CombatReactionAppliedEvent(e.TargetId, reaction, e.SourceId, e.AttackInstanceId, 0));
                return;
            }

            var tgt = _feel.ResolveJudging(e.TargetId);
            var stun = StunTicks(tgt);
            var duration = ReactionDuration(reaction, tgt);

            for (var i = 0; i < _interruptSinks.Count; i++) _interruptSinks[i].InterruptByStagger(e.TargetId, e.SourceId);

            _staggers.TryGetValue(e.TargetId, out var rec);
            if (rec == null)
            {
                rec = new StaggerRec { Duration = duration, StunTicks = stun };
                _staggers[e.TargetId] = rec;
            }
            else if (rec.Active)
            {
                if (duration > rec.Remaining)
                {
                    rec.Remaining = duration;
                    rec.StunTicks = stun;
                    rec.Elapsed = 0;
                }
            }
            else if (duration > rec.Duration)
            {
                rec.Duration = duration;
                rec.StunTicks = stun;
            }

            if ((reaction == HitReaction.Knockback || reaction == HitReaction.Knockdown) && Knockback != null)
            {
                var distance = ComputeKnockbackDistance(e, tgt);
                var direction = KnockbackDirection(e);
                if (distance > 0.0 && direction.Length > 1e-9)
                {
                    rec.KnockbackPending = true;
                    rec.KnockbackDirection = direction;
                    rec.KnockbackDistance = distance;
                }
            }

            _bus.Enqueue(new CombatReactionAppliedEvent(e.TargetId, reaction, e.SourceId, e.AttackInstanceId, duration));
        }

        private double ComputeKnockbackDistance(CombatHitConfirmedEvent e, JudgingFeelView target)
        {
            if (!_units.Exists(e.SourceId)) return 0.0;
            var atk = _feel.ResolveJudging(e.SourceId);
            var baseDistance = atk.GetNumber(FeelFieldNames.KnockbackDistance);
            var resistance = ReadKnockbackResistance(e.TargetId, target);
            var multiplier = _options.KnockbackImpactMultipliers.TryGetValue(e.ImpactClass, out var m) ? m : 1.0;
            return baseDistance * (1.0 - resistance) * multiplier;
        }

        private double ReadKnockbackResistance(Id targetId, JudgingFeelView target)
        {
            var stat = target.GetAbsolute(FeelFieldNames.KnockbackResistanceStat);
            if (stat.IsNone) return 0.0;
            try
            {
                var r = _stats.GetStat(targetId, new Id(stat.AsText()));
                return r < 0.0 ? 0.0 : (r > 1.0 ? 1.0 : r);
            }
            catch (ArgumentException)
            {
                return 0.0;
            }
            catch (InvalidOperationException)
            {
                return 0.0;
            }
        }

        private Vec2 KnockbackDirection(CombatHitConfirmedEvent e)
        {
            if (e.WorldDirection.Length > 1e-9) return e.WorldDirection;
            if (_units.Exists(e.SourceId) && _units.Exists(e.TargetId))
            {
                return _units.GetPosition(e.TargetId) - _units.GetPosition(e.SourceId);
            }

            return Vec2.Zero;
        }

        // ================================================================== tick 边界

        private void OnTickStarted()
        {
            _killPending.Clear();
            if (_staggers.Count == 0) return;
            List<Id>? finished = null;
            foreach (var pair in _staggers)
            {
                var unit = pair.Key;
                var rec = pair.Value;
                if (!rec.Active && rec.KnockbackPending && _clock.RemainingPausedTicks(unit) <= 1)
                {
                    SubmitKnockback(unit, rec);
                }

                // 硬直从顿帧结束后起算：顿帧期间（含硬直中又被顿帧）不推进。
                if (_clock.IsPaused(unit)) continue;

                if (!rec.Active)
                {
                    rec.Active = true;
                    rec.Remaining = rec.Duration;
                    rec.Elapsed = 0;
                }

                if (rec.Remaining <= 0)
                {
                    (finished ??= new List<Id>()).Add(unit);
                    continue;
                }

                rec.Remaining--;
                rec.Elapsed++;
            }

            if (finished != null)
            {
                for (var i = 0; i < finished.Count; i++) _staggers.Remove(finished[i]);
            }
        }

        private void OnTickFinished()
        {
            FlushHitstop();

            if (_frozen.Count == 0) return;
            List<Id>? ended = null;
            foreach (var unit in _frozen)
            {
                if (!_clock.IsPaused(unit)) (ended ??= new List<Id>()).Add(unit);
            }

            if (ended == null) return;
            for (var i = 0; i < ended.Count; i++) _frozen.Remove(ended[i]);
            _bus.Enqueue(new FeelHitstopEndedEvent(ended));
        }

        /// <summary>
        /// 落地本批命中的顿帧（tick 末由 <c>sim.tick_finished</c> 触发，没有世界宿主的测试可手动调用）：同一单位取批内最大值，
        /// 冻结中再被命中取"剩余与新值之大者"，发 <c>feel.hitstop_started</c>（只在冻结时长真的变长或新开始冻结时发，
        /// 按结果时长与攻击实例分组，同组单位合成一条）；随后提交"没有顿帧可等"的击退。
        /// </summary>
        public void FlushHitstop()
        {
            if (_batch.Count > 0)
            {
                var order = new List<Id>();
                var ticks = new Dictionary<Id, int>();
                var instance = new Dictionary<Id, Id>();
                for (var i = 0; i < _batch.Count; i++)
                {
                    var b = _batch[i];
                    Merge(order, ticks, instance, b.AttackerId, b.AttackerTicks, b.AttackInstanceId);
                    Merge(order, ticks, instance, b.TargetId, b.TargetTicks, b.AttackInstanceId);
                }

                _batch.Clear();

                var groups = new List<(int Ticks, Id Instance, List<Id> Units)>();
                for (var i = 0; i < order.Count; i++)
                {
                    var unit = order[i];
                    if (!_units.Exists(unit) || !_units.IsAlive(unit)) continue;
                    var before = _clock.RemainingPausedTicks(unit);
                    _clock.Pause(unit, ticks[unit]);
                    var after = _clock.RemainingPausedTicks(unit);
                    _frozen.Add(unit);
                    if (after <= before) continue;

                    var found = false;
                    for (var g = 0; g < groups.Count; g++)
                    {
                        if (groups[g].Ticks == after && groups[g].Instance.Equals(instance[unit]))
                        {
                            groups[g].Units.Add(unit);
                            found = true;
                            break;
                        }
                    }

                    if (!found) groups.Add((after, instance[unit], new List<Id> { unit }));
                }

                for (var g = 0; g < groups.Count; g++)
                {
                    _bus.Enqueue(new FeelHitstopStartedEvent(groups[g].Units, groups[g].Ticks, groups[g].Instance));
                }
            }

            if (_staggers.Count == 0) return;
            foreach (var pair in _staggers)
            {
                if (pair.Value.KnockbackPending && _clock.RemainingPausedTicks(pair.Key) == 0)
                {
                    SubmitKnockback(pair.Key, pair.Value);
                }
            }
        }

        private static void Merge(List<Id> order, Dictionary<Id, int> ticks, Dictionary<Id, Id> instance, Id unit, int value, Id attackInstance)
        {
            if (value <= 0) return;
            if (ticks.TryGetValue(unit, out var existing))
            {
                if (value > existing)
                {
                    ticks[unit] = value;
                    instance[unit] = attackInstance;
                }

                return;
            }

            order.Add(unit);
            ticks[unit] = value;
            instance[unit] = attackInstance;
        }

        private void SubmitKnockback(Id unit, StaggerRec rec)
        {
            rec.KnockbackPending = false;
            if (Knockback == null || !_units.Exists(unit) || !_units.IsAlive(unit)) return;
            Knockback.BeginKnockback(unit, rec.KnockbackDirection, rec.KnockbackDistance, _options.KnockbackDurationSeconds);
        }

        // ================================================================== 释放保证

        /// <summary>
        /// 单位死亡/销毁：无条件解除该单位的顿帧冻结与硬直窗口（手感设计/03 第 3.2 节"释放保证"）；曾处于冻结的发
        /// <c>feel.hitstop_ended</c>。幂等。
        /// </summary>
        public void ReleaseUnit(Id unitId)
        {
            _clock.ReleaseAll(unitId);
            _staggers.Remove(unitId);
            if (_frozen.Remove(unitId)) _bus.Enqueue(new FeelHitstopEndedEvent(new[] { unitId }));
        }

        /// <summary>场景卸载、离散模式切换：无条件解除全部顿帧与硬直并清空待落地批次；曾冻结的单位统一发一条 <c>feel.hitstop_ended</c>。</summary>
        public void ReleaseAll()
        {
            _clock.ReleaseAll();
            _staggers.Clear();
            _batch.Clear();
            if (_frozen.Count == 0) return;
            var ended = new List<Id>(_frozen);
            _frozen.Clear();
            _bus.Enqueue(new FeelHitstopEndedEvent(ended));
        }
    }
}
