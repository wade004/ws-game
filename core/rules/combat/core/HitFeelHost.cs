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
            public double LaunchApex;
            public LaunchStackMode LaunchStack;
            public double LaunchStackCap;

            /// <summary>击飞绝对高度上限（世界高度，0 = 不设；<c>launch_height_cap</c>）。</summary>
            public double LaunchHeightCap;

            /// <summary>空中硬直持续到落地（目标侧 <c>air_stun_until_land</c> 在硬直登记时的取值）。</summary>
            public bool UntilLand;
        }

        /// <summary>
        /// 一次命中的"攻击方手感覆盖"（手感落地 M5-S2a）：<see cref="Evaluate"/> 记下、稍后派发的 <c>combat.hit_confirmed</c> 落地击退/击飞时取用。
        /// 只在命中带分段/技能手感覆盖（<see cref="HitFeelInput.AttackerFeelOverrides"/>）或蓄力缩放（<see cref="HitFeelInput.Scale"/> 非全 1）时才建档，
        /// 其余命中不进这张表，落地阶段仍读攻击方当前解析结果（逐位保持此前口径）。
        /// </summary>
        private sealed class AttackerHit
        {
            public JudgingFeelView? View;
            public HitFeelScale Scale;
        }

        private readonly Dictionary<(Id AttackInstanceId, Id TargetId), AttackerHit> _attackerHits =
            new Dictionary<(Id, Id), AttackerHit>();

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

        /// <summary>动态韧性：目标已损失的韧性量与回复进度（只对被"声明了 poise_damage"的命中打过的目标建档）。</summary>
        private sealed class PoiseRec
        {
            public double Lost;
            public int DelayRemaining;
            public double RecoverPerTick;

            /// <summary>回复模式为 out_of_combat（目标侧 <c>poise_recover_mode</c>，命中那一刻取值）：处于战斗中时延迟计时与回复都暂停。</summary>
            public bool OutOfCombatOnly;

            /// <summary>破韧后自动回满的剩余 tick 数；&lt; 0 表示没有待执行的回满（<c>poise_break_reset_ms</c> 没声明，或还没破韧）。</summary>
            public int BreakResetRemaining = -1;
        }

        private readonly SortedDictionary<Id, PoiseRec> _poise = new SortedDictionary<Id, PoiseRec>();
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

        /// <summary>
        /// 击飞执行口（竖直运动服务）。缺省 null——平面世界没有竖直轴，击飞静默不发生（硬直与击退照常）。
        /// 只有攻击方档案声明了 <c>launch_height</c> 且大于 0、反应达到 <c>knockback</c>/<c>knockdown</c> 时才提交。
        /// </summary>
        public ILaunchSink? Launch { get; set; }

        /// <summary>
        /// 腾空查询（竖直运动服务）。缺省 null——一律视为在地面，<c>air_hit_reaction</c> 不生效（与 1.95.0 一致）。
        /// </summary>
        public IAirborneQuery? Airborne { get; set; }

        /// <summary>
        /// 战斗状态查询（手感落地 M4-W3，装配根接 <c>CombatHost.IsInCombat</c>）：目标侧 <c>poise_recover_mode = out_of_combat</c> 的韧性回复据此判断"脱战"。
        /// 缺省 null——视为一直不在战斗中（等价于不暂停，回复口径同 <c>delay</c>），没有战斗宿主的装配不会因为声明了该模式而卡死。
        /// </summary>
        public Func<Id, bool>? InCombat { get; set; }

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

            // M5-S2a：命中带分段/技能手感覆盖或蓄力缩放时记下，落地击退/击飞沿用同一份视图与缩放。
            var scale = input.Scale;
            if (input.AttackInstanceId.HasValue && (input.AttackerFeelOverrides || !scale.IsIdentity))
            {
                _attackerHits[(input.AttackInstanceId.Value, input.TargetId)] = new AttackerHit
                {
                    View = input.AttackerFeelOverrides ? atk : null,
                    Scale = scale,
                };
            }

            // ---- 顿帧：攻击方时长取武器为主字段（击杀放大），受击方时长同；各自按所属单位的上限限幅。
            var attackerMs = atk.GetNumber(FeelFieldNames.AttackerHitstopMs) * scale.AttackerHitstop;
            if (input.IsKill) attackerMs *= atk.GetNumber(FeelFieldNames.KillHitstopScale);
            var attackerTicks = Math.Min(
                Ticks(attackerMs),
                Ticks(atk.GetNumber(FeelFieldNames.AttackerHitstopCapMs)));

            var targetTicks = 0;
            if (!input.IsKill)
            {
                targetTicks = Math.Min(
                    Ticks(atk.GetNumber(FeelFieldNames.TargetHitstopMs) * scale.TargetHitstop),
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
                var maxPoise = ReadPoise(input.TargetId);
                if (atk.TryGetNumber(FeelFieldNames.PoiseDamage, out var poiseDamage) && poiseDamage > 0.0 && maxPoise > 0.0)
                {
                    reaction = EvaluateDynamicPoise(input, tgt, impact, power, maxPoise, poiseDamage * PoiseImpactMultiplier(impact));
                }
                else
                {
                    // 静态韧性（缺省，既有行为逐位不变）：命中没声明 poise_damage、或目标没有韧性属性。
                    reaction = power <= maxPoise ? HitReaction.Flinch : MapImpact(impact);
                }

                reaction = ApplyAirHit(reaction, input.TargetId, atk, tgt);
                reaction = ApplyCap(reaction, tgt.GetText(FeelFieldNames.ReactionCap));
                reaction = ApplyAirCap(reaction, input.TargetId, tgt);
            }

            return new HitFeelOutcome(impact, attackerTicks, targetTicks, reaction, ReactionDuration(reaction, tgt));
        }

        /// <summary>
        /// 动态韧性（手感落地 M4-L）：目标有一个韧性池（容量 = 韧性属性，已损失量 <c>Lost</c>），命中声明的 <c>poise_damage</c> 从池里扣。
        /// 有效韧性 <c>before = max(0, 容量 − Lost)</c>，扣后 <c>after = max(0, before − poise_damage)</c>。
        /// 规则：<c>after &gt; 0</c> 且 <c>stagger_power ≤ before</c> 才被韧性挡成 Flinch（沿用静态规则的"硬直强度与韧性比较"，
        /// 再加"这一击没把韧性打穿"）；否则按冲击等级映射出完整反应——其中 <c>before &gt; 0 &amp;&amp; after == 0</c> 是破韧（发
        /// <c>combat.poise_changed</c> 带 <c>Broken</c>），<c>before == 0</c>（已破、尚未回复）的后续命中同样不被挡。
        /// 本方法是 <see cref="Evaluate"/> 里唯一写状态的地方（每次真实命中恰好调用一次，见 <see cref="IHitFeelArbiter"/> 的约定说明）：
        /// 记下新的损失量、重置回复延迟，并发 <c>combat.poise_changed</c>。
        /// </summary>
        private HitReaction EvaluateDynamicPoise(
            in HitFeelInput input, JudgingFeelView target, string impact, double power, double maxPoise, double poiseDamage)
        {
            _poise.TryGetValue(input.TargetId, out var rec);
            var lost = rec != null ? rec.Lost : 0.0;
            var before = Math.Max(0.0, maxPoise - lost);
            var after = Math.Max(0.0, before - poiseDamage);
            var broken = before > 0.0 && after <= 0.0;
            var sheltered = after > 0.0 && power <= before;

            if (rec == null)
            {
                rec = new PoiseRec();
                _poise[input.TargetId] = rec;
            }

            rec.Lost = maxPoise - after;
            var delayMs = target.TryGetNumber(FeelFieldNames.PoiseRecoverDelayMs, out var d) ? d : 0.0;
            rec.DelayRemaining = Ticks(delayMs);
            var perSecond = target.TryGetNumber(FeelFieldNames.PoiseRecoverPerS, out var r) ? r : 0.0;
            rec.RecoverPerTick = perSecond > 0.0 ? perSecond * _stepSeconds : 0.0;
            var mode = target.GetAbsolute(FeelFieldNames.PoiseRecoverMode);
            rec.OutOfCombatOnly = !mode.IsNone && mode.AsText() == "out_of_combat";
            if (broken)
            {
                // 破韧才（重新）起算自动回满：已空池子上的后续命中不是新的破韧，不顺延计时。
                rec.BreakResetRemaining = target.TryGetNumber(FeelFieldNames.PoiseBreakResetMs, out var resetMs) ? Ticks(resetMs) : -1;
            }

            _bus.Enqueue(new CombatPoiseChangedEvent(input.TargetId, input.AttackerId, before, after, maxPoise, poiseDamage, broken));
            return sheltered ? HitReaction.Flinch : MapImpact(impact);
        }

        /// <summary>
        /// 动态韧性的冲击等级倍率（手感落地 M4-W3，<see cref="HitFeelOptions.PoiseDamageImpactMultipliers"/>，口径同击退的冲击等级倍率表）：
        /// 表里没有该等级（含空表，缺省）取 1，即不缩放。
        /// </summary>
        private double PoiseImpactMultiplier(string impactClass) =>
            _options.PoiseDamageImpactMultipliers.TryGetValue(impactClass, out var m) ? m : 1.0;

        /// <summary>目标此刻的有效韧性（动态韧性：容量减已损失量；没有动态损失记录等于容量）。供查询与测试。</summary>
        public double CurrentPoise(Id targetId)
        {
            var max = ReadPoise(targetId);
            return _poise.TryGetValue(targetId, out var rec) ? Math.Max(0.0, max - rec.Lost) : max;
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

        /// <summary>
        /// 腾空受击：目标在空中、<c>air_hit_reaction</c> 有生效的声明（非 same）时，把已算出的反应整体替换为该值（之后仍过 <c>reaction_cap</c>
        /// 与 <c>air_reaction_cap</c>）。<b>声明来源合成</b>（手感落地 M4-W1b）：攻击方档案声明了（非 same）就用攻击方的（"这一类攻击打中空中目标时的反应"），
        /// 否则取受击方档案的声明（"该单位在空中被命中时的反应"）；两侧都没有声明、地面受击、没有腾空查询时原样返回。
        /// </summary>
        private HitReaction ApplyAirHit(HitReaction reaction, Id targetId, JudgingFeelView attacker, JudgingFeelView target)
        {
            if (Airborne == null) return reaction;
            var v = attacker.GetAbsolute(FeelFieldNames.AirHitReaction);
            if (v.IsNone || v.AsText() == "same") v = target.GetAbsolute(FeelFieldNames.AirHitReaction);
            if (v.IsNone) return reaction;
            if (!Airborne.IsAirborne(targetId)) return reaction;
            switch (v.AsText())
            {
                case "none": return HitReaction.None;
                case "flinch": return HitReaction.Flinch;
                case "stagger_light": return HitReaction.StaggerLight;
                case "stagger": return HitReaction.Stagger;
                case "knockback": return HitReaction.Knockback;
                case "knockdown": return HitReaction.Knockdown;
                default: return reaction; // same
            }
        }

        /// <summary>
        /// 空中受击反应上限（手感落地 M4-W1b，受击方 <c>air_reaction_cap</c>）：只在目标此刻腾空且声明了该字段时，把反应再限制到该上限
        /// （与 <c>reaction_cap</c> 叠加取较低者）；死亡不受影响（调用方已排除）。
        /// </summary>
        private HitReaction ApplyAirCap(HitReaction reaction, Id targetId, JudgingFeelView target)
        {
            if (Airborne == null) return reaction;
            var v = target.GetAbsolute(FeelFieldNames.AirReactionCap);
            if (v.IsNone) return reaction;
            if (!Airborne.IsAirborne(targetId)) return reaction;
            return ApplyCap(reaction, v.AsText());
        }

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

            // 手感落地 M5-S2a：技能行声明了 feel_ref 的 instant 命中（法术等没有动作时间线的技能）以该行为动作层重算攻击方视图，不再只取武器；
            // 没声明（缺省）、没有装配 SkillFeelRef、或解析来源不支持动作层覆盖时仍走攻击方当前解析结果，与此前逐位一致。
            var instanceId = attackInstanceId ?? new Id("attack.instant." + (++_syntheticAttackCounter).ToString(System.Globalization.CultureInfo.InvariantCulture));
            var skillFeelRef = skillId.HasValue ? _options.SkillFeelRef?.Invoke(skillId) : null;
            var outcome = skillFeelRef != null
                ? Evaluate(new HitFeelInput(
                    sourceId, targetId, hitResult, amount, isKill, _feel.ResolveJudgingWithAction(sourceId, skillFeelRef, null),
                    instanceId, true, HitFeelScale.Identity))
                : Evaluate(new HitFeelInput(sourceId, targetId, hitResult, amount, isKill));

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

            _attackerHits.Remove((e.AttackInstanceId, e.TargetId), out var hit);

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

            ApplyReaction(e, hit);
        }

        private int CapTicks(Id unitId, string capField, int ticks)
        {
            if (ticks <= 0 || !_units.Exists(unitId)) return ticks;
            var cap = Ticks(_feel.ResolveJudging(unitId).GetNumber(capField));
            return ticks < cap ? ticks : cap;
        }

        private void ApplyReaction(CombatHitConfirmedEvent e, AttackerHit? hit)
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
                var distance = ComputeKnockbackDistance(e, tgt, hit);
                var direction = KnockbackDirection(e);
                if (distance > 0.0 && direction.Length > 1e-9)
                {
                    rec.KnockbackPending = true;
                    rec.KnockbackDirection = direction;
                    rec.KnockbackDistance = distance;
                }
            }

            // 击飞（竖直轴能力包）：与击退同一触发条件、同一提交时机（顿帧结束后）；不依赖击退距离，只看 launch_height。
            if ((reaction == HitReaction.Knockback || reaction == HitReaction.Knockdown) && Launch != null)
            {
                var apex = ComputeLaunchApex(e, tgt, hit);
                if (apex > 0.0)
                {
                    rec.KnockbackPending = true;
                    rec.LaunchApex = apex;
                    ReadLaunchStack(e, hit, out rec.LaunchStack, out rec.LaunchStackCap);
                    rec.LaunchHeightCap = ReadLaunchHeightCap(e, tgt, hit);
                }
            }

            // 空中硬直持续到落地（目标侧 air_stun_until_land）：硬直时长到点后目标仍在空中则保持到落地。每次登记/刷新硬直时按受击方当时的档案取值。
            if (Airborne != null)
            {
                var untilLand = tgt.GetAbsolute(FeelFieldNames.AirStunUntilLand);
                rec.UntilLand = !untilLand.IsNone && untilLand.AsBool();
            }

            _bus.Enqueue(new CombatReactionAppliedEvent(e.TargetId, reaction, e.SourceId, e.AttackInstanceId, duration));
        }

        /// <summary>落地阶段读取的攻击方视图：命中带手感覆盖（<see cref="AttackerHit.View"/>）用它，否则读攻击方当前解析结果（此前口径）。</summary>
        private JudgingFeelView AttackerViewFor(CombatHitConfirmedEvent e, AttackerHit? hit) =>
            hit?.View ?? _feel.ResolveJudging(e.SourceId);

        private double ComputeKnockbackDistance(CombatHitConfirmedEvent e, JudgingFeelView target, AttackerHit? hit)
        {
            if (!_units.Exists(e.SourceId)) return 0.0;
            var atk = AttackerViewFor(e, hit);
            var baseDistance = atk.GetNumber(FeelFieldNames.KnockbackDistance) * (hit != null ? hit.Scale.KnockbackDistance : 1.0);
            var resistance = ReadKnockbackResistance(e.TargetId, target);
            var multiplier = _options.KnockbackImpactMultipliers.TryGetValue(e.ImpactClass, out var m) ? m : 1.0;
            return baseDistance * (1.0 - resistance) * multiplier;
        }

        /// <summary>击飞顶点高度（世界单位）= 攻击方 <c>launch_height</c>（标定后）×(1 − 目标击退抗性)×冲击等级倍率（与击退距离同一套）；未声明为 0。</summary>
        private double ComputeLaunchApex(CombatHitConfirmedEvent e, JudgingFeelView target, AttackerHit? hit)
        {
            if (!_units.Exists(e.SourceId)) return 0.0;
            var atk = AttackerViewFor(e, hit);
            if (!atk.TryGetNumber(FeelFieldNames.LaunchHeight, out var baseHeight) || baseHeight <= 0.0) return 0.0;
            if (hit != null) baseHeight *= hit.Scale.LaunchHeight;
            var resistance = ReadKnockbackResistance(e.TargetId, target);
            var multiplier = _options.KnockbackImpactMultipliers.TryGetValue(e.ImpactClass, out var m) ? m : 1.0;
            var apex = baseHeight * (1.0 - resistance) * multiplier;
            // 体型缩放（手感落地 M4-W1b，受击方 launch_body_scale）：声明了才乘；缺省不动（逐位不变）。
            if (target.TryGetNumber(FeelFieldNames.LaunchBodyScale, out var bodyScale)) apex *= bodyScale;
            return apex;
        }

        /// <summary>
        /// 击飞绝对高度上限（<c>launch_height_cap</c>，标定后世界高度）：攻击方与受击方档案都可声明，两侧都声明时取较小者；都没有为 0（不设）。
        /// </summary>
        private double ReadLaunchHeightCap(CombatHitConfirmedEvent e, JudgingFeelView target, AttackerHit? hit)
        {
            var cap = 0.0;
            if (_units.Exists(e.SourceId))
            {
                var atk = AttackerViewFor(e, hit);
                if (atk.TryGetNumber(FeelFieldNames.LaunchHeightCap, out var a) && a > 0.0) cap = a;
            }

            if (target.TryGetNumber(FeelFieldNames.LaunchHeightCap, out var t) && t > 0.0 && (cap <= 0.0 || t < cap)) cap = t;
            return cap;
        }

        /// <summary>击飞叠加方式与上限（攻击方档案 <c>launch_stack</c>/<c>launch_stack_cap</c>）；未声明为 restart、无上限。</summary>
        private void ReadLaunchStack(CombatHitConfirmedEvent e, AttackerHit? hit, out LaunchStackMode mode, out double cap)
        {
            mode = LaunchStackMode.Restart;
            cap = 0.0;
            if (!_units.Exists(e.SourceId)) return;
            var atk = AttackerViewFor(e, hit);
            var m = atk.GetAbsolute(FeelFieldNames.LaunchStack);
            if (m.IsNone || m.AsText() != "add") return;
            mode = LaunchStackMode.Add;
            if (atk.TryGetNumber(FeelFieldNames.LaunchStackCap, out var c) && c > 0.0) cap = c;
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
            _attackerHits.Clear();
            AdvancePoiseRecovery();
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
                    // 空中硬直持续到落地：时长到点仍在空中则保持（不再推进已过时长，免得把空中的硬直误判成倒地）；落地后的这个 tick 才结束。
                    if (rec.UntilLand && Airborne != null && Airborne.IsAirborne(unit)) continue;
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

        /// <summary>
        /// 动态韧性回复：每 tick（先于全部阶段处理器）推进。
        /// <list type="number">
        /// <item>破韧后自动回满（<c>poise_break_reset_ms</c>）：破韧起算的计时到点，把已损失量一次清零并发 <c>combat.poise_recovered</c>，
        /// 不看回复速率、回复模式，也不受期间再受击影响。</item>
        /// <item>速率回复：先耗尽 <c>poise_recover_delay_ms</c>（每次动态命中重新计），再每 tick 回复 <c>poise_recover_per_s × 步长</c>；损失归零那一 tick 发
        /// <c>combat.poise_recovered</c> 并删档。回复模式 <c>out_of_combat</c> 时，目标处于战斗中的 tick 延迟计时与回复都暂停（脱战后才开始计延迟）。
        /// 没声明 <c>poise_recover_per_s</c> 的目标不做速率回复（档案一直留着，直到回满或单位释放）。</item>
        /// </list>
        /// </summary>
        private void AdvancePoiseRecovery()
        {
            if (_poise.Count == 0) return;
            List<Id>? recovered = null;
            foreach (var pair in _poise)
            {
                var rec = pair.Value;
                if (rec.BreakResetRemaining >= 0 && --rec.BreakResetRemaining <= 0)
                {
                    rec.Lost = 0.0;
                    (recovered ??= new List<Id>()).Add(pair.Key);
                    continue;
                }

                if (rec.Lost <= 0.0 || rec.RecoverPerTick <= 0.0) continue;
                if (rec.OutOfCombatOnly && InCombat != null && InCombat(pair.Key)) continue;
                if (rec.DelayRemaining > 0)
                {
                    rec.DelayRemaining--;
                    continue;
                }

                rec.Lost -= rec.RecoverPerTick;
                if (rec.Lost <= 1e-9) (recovered ??= new List<Id>()).Add(pair.Key);
            }

            if (recovered == null) return;
            for (var i = 0; i < recovered.Count; i++)
            {
                _poise.Remove(recovered[i]);
                _bus.Enqueue(new CombatPoiseRecoveredEvent(recovered[i], ReadPoise(recovered[i])));
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
            var apex = rec.LaunchApex;
            var stack = rec.LaunchStack;
            var stackCap = rec.LaunchStackCap;
            var heightCap = rec.LaunchHeightCap;
            rec.LaunchApex = 0.0;
            rec.LaunchStack = LaunchStackMode.Restart;
            rec.LaunchStackCap = 0.0;
            rec.LaunchHeightCap = 0.0;
            if (!_units.Exists(unit) || !_units.IsAlive(unit)) return;
            if (Knockback != null && rec.KnockbackDistance > 0.0)
            {
                Knockback.BeginKnockback(unit, rec.KnockbackDirection, rec.KnockbackDistance, _options.KnockbackDurationSeconds);
            }

            if (Launch != null && apex > 0.0)
            {
                if (heightCap > 0.0)
                {
                    Launch.BeginLaunch(unit, apex, stack, stackCap, heightCap);
                }
                else
                {
                    Launch.BeginLaunch(unit, apex, stack, stackCap);
                }
            }
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
            _poise.Remove(unitId);
            if (_frozen.Remove(unitId)) _bus.Enqueue(new FeelHitstopEndedEvent(new[] { unitId }));
        }

        /// <summary>场景卸载、离散模式切换：无条件解除全部顿帧与硬直并清空待落地批次；曾冻结的单位统一发一条 <c>feel.hitstop_ended</c>。</summary>
        public void ReleaseAll()
        {
            _clock.ReleaseAll();
            _staggers.Clear();
            _poise.Clear();
            _batch.Clear();
            if (_frozen.Count == 0) return;
            var ended = new List<Id>(_frozen);
            _frozen.Clear();
            _bus.Enqueue(new FeelHitstopEndedEvent(ended));
        }
    }
}
