using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Foundation.SimLoop;
using Core.Rules.Assembly;
using Core.Rules.Common;
using Core.Rules.Skill;

namespace Core.Carriers.Assembly
{
    /// <summary>
    /// 手感系统的装配选项（手感落地 S10）。<b>缺省不启用</b>：不把本对象传给 <see cref="CarriersAssembly"/>/<c>GameplayAssembly</c>，
    /// 既有行为逐位不变。启用后：数据里必须有 <c>feel.*</c> 行（否则装配抛异常，不静默降级）；标定行恰有一行时可省略
    /// <see cref="CalibrationId"/>，有多行必须指定。
    /// </summary>
    public sealed class CarriersFeelOptions : RulesFeelOptions
    {
        /// <summary>要用的 <c>feel.calibration</c> 行 id；数据里恰有一行（如框架缺省标定）时可为空，多行（如游戏自带标定 + 框架缺省）必须指定。</summary>
        public string? CalibrationId { get; set; }

        /// <summary>主手武器槽位覆盖；<c>null</c> 取武器槽（<c>is_weapon</c>）按 id 序数的第 1 个。见 <see cref="EquippedWeaponFeelProvider"/> 判断记录。</summary>
        public Id? MainHandSlot { get; set; }

        /// <summary>副手武器槽位覆盖；<c>null</c> 取武器槽按 id 序数的第 2 个。</summary>
        public Id? OffhandSlot { get; set; }

        /// <summary>
        /// 被视为"普通攻击"的输入动作 id：这些动作的缓冲记录优先取单位当前主手武器的 <c>feel.weapon.auto_attack_timeline_ref</c> 技能，
        /// 武器没声明（或空手）时回落到 <c>skill_slot</c> 槽位绑定（<see cref="WeaponPreferredActionBinding"/>）。缺省 null = 所有类别为 <c>attack</c> 的动作；
        /// 游戏另有蓄力、重击等自带 <c>skill_slot</c> 的攻击类动作时，在这里只列出普攻动作，其余攻击类动作始终走槽位绑定。
        /// </summary>
        public IReadOnlyList<Id>? AutoAttackActions { get; set; }

        /// <summary>除 <c>found.input_action</c> 表外，另行声明给输入缓冲的动作（游戏在代码里声明的动作集）。同 id 以后者为准。</summary>
        public IReadOnlyList<ActionDefinition>? ExtraActions { get; set; }

        /// <summary>
        /// 宽限条件求值（<c>found.grace_condition</c>）覆盖。缺省 null：装配框架提供的基于 Expr 的 <see cref="ExprGraceConditionEvaluator"/>——
        /// 游戏只在数据里声明条件名与表达式即可使用，不写代码（手感落地 M3-B）；游戏有自己的求值方式时经本属性覆盖。
        /// </summary>
        public IGraceConditionEvaluator? GraceEvaluator { get; set; }

        /// <summary>
        /// 缺省 Expr 求值器的"行动者当前目标"解析覆盖（<c>target</c> 分组与 <c>self.distance_to_target</c> 的绑定对象），优先级最高：给出非空目标即用。
        /// 缺省 null：目标取<b>本次施法请求携带的目标</b>（单位目标；地面落点施法没有单位目标），没有时才回落到自动攻击的当前目标
        /// （<c>AutoAttackHost.GetTarget</c>），都没有则不绑定目标（手感落地 M4-G）。提供了覆盖时不再回落到自动攻击目标。只在没有覆盖 <see cref="GraceEvaluator"/> 时使用。
        /// </summary>
        public Func<Id, Id?>? GraceTargetResolver { get; set; }

        /// <summary>
        /// 是否让世界里的单位（<c>player</c>/<c>creature</c>，装配时已有的与之后出生的）自动登记到输入缓冲，使宽限条件从登记起就逐 tick 采样
        /// （手感落地 M3-B：非本地行动者的第一次按键不再可能早于采样）。缺省 true；单位很多、且只有本地玩家使用宽限时可关掉，非本地行动者由游戏在按键前自行
        /// 调用 <see cref="InputBufferHost.RegisterActor"/>。没有任何输入动作声明宽限条件时采样为空，登记本身不产生开销以外的行为。
        /// </summary>
        public bool AutoRegisterGraceActors { get; set; } = true;

        /// <summary>本地玩家的移动轴动作名（如 <c>input.action.move</c>）：<c>PresentationAssembly</c> 把本地输入映射接给缓冲时用来采集按下瞬间的方向快照；缺省 null 即不采集。</summary>
        public string? LocalMoveActionName { get; set; }

        /// <summary>剪辑根运动来源（运动层 <see cref="MotionServices.RootMotion"/>）；缺省 null。</summary>
        public IRootMotionSource? RootMotion { get; set; }

        /// <summary>自定义曲线解析（<see cref="MotionServices.Curves"/>）；缺省 null。</summary>
        public IMotionCurveSource? Curves { get; set; }

        /// <summary>
        /// 目标辅助候选解析：赋给运动层 <see cref="MotionServices.TargetAssist"/>，同时是时间线 <c>target_assist</c> 的候选来源。
        /// 缺省 null：运动层目标辅助关闭；时间线侧缺省用 <see cref="TargetChainAssistResolver"/>（目标选择链），但技能不声明 <c>target_assist</c> 就不生效。
        /// </summary>
        public ITargetAssistResolver? TargetAssist { get; set; }

        /// <summary>
        /// 命中几何是否使用受击半径（手感落地 M5-S2a，手感设计/03 第 2.2 节）：开启后目标命中半径 = <c>unit_body_radius</c>（标定后世界单位）× <c>hurt_radius_scale</c>（缺省 1），
        /// 形状与目标圆相交即算命中、接触点取身体圆面上的点；没有声明 <c>unit_body_radius</c> 的单位半径为 0，仍按点判定。
        /// <b>缺省 false</b>——开启会改变既有命中结果（擦边的单位多算命中、接触点移出体内），由游戏决定翻不翻；关闭时与本选项引入之前逐位一致。
        /// 游戏已经自行配置了 <see cref="Core.Rules.Targeting.TargetingOptions.TargetRadius"/> 时，本选项不覆盖它。
        /// </summary>
        public bool HitRadiusFromFeel { get; set; }

        /// <summary>运动模式规则覆盖；缺省 null 取 <see cref="MotionModeRuleSet.FromProfiles"/>（框架数据 <c>feel.motion_mode_rules</c> 的消费结果）。</summary>
        public MotionModeRuleSet? ModeRules { get; set; }

        /// <summary>返回一份步长替换为 <paramref name="stepSeconds"/> 的浅拷贝（装配根按时钟宿主步长补全，不改调用方传入的对象；<see cref="RulesFeelOptions.HitFeel"/> 等引用成员共享）。</summary>
        public CarriersFeelOptions WithStepSeconds(double stepSeconds)
        {
            var copy = (CarriersFeelOptions)MemberwiseClone();
            copy.StepSeconds = stepSeconds;
            return copy;
        }
    }

    /// <summary>已装配的手感系统（载体层汇总）：解析器、共用动作时钟、输入缓冲、时间线协作者、受击裁决、运动层接线。</summary>
    public sealed class CarriersFeelSystem : IDisposable
    {
        private readonly List<SubscriptionHandle> _subscriptions;

        /// <summary>手感装配结果（标定、档案集合、调试覆盖）。</summary>
        public FeelSystem Feel { get; }

        /// <summary>全装配唯一的解析器（动作开始/结束时自动失效缓存的包装）。</summary>
        public IFeelResolver Resolver { get; }

        /// <summary>规则层接线：动作时钟、受击裁决、时间线协作者。</summary>
        public RulesFeelSystem Rules { get; }

        /// <summary>全装配唯一的行动者动作时钟。</summary>
        public ActorActionClock Clock => Rules.Clock;

        /// <summary>输入缓冲宿主：本地输入、AI、自动战斗共用同一个入口（<see cref="InputBufferHost.Press"/>/<see cref="InputBufferHost.Submit"/>）。</summary>
        public InputBufferHost InputBuffer { get; }

        /// <summary>宽限追踪（手感落地 M3-B 起恒装配：求值器缺省为 <see cref="ExprGraceConditionEvaluator"/>，没有输入动作声明宽限条件时不采样任何东西）。</summary>
        public GraceTracker? Grace { get; }

        /// <summary>宽限条件求值器：<see cref="CarriersFeelOptions.GraceEvaluator"/> 的覆盖，缺省为框架的 <see cref="ExprGraceConditionEvaluator"/>。</summary>
        public IGraceConditionEvaluator? GraceEvaluator { get; private set; }

        /// <summary>技能槽位绑定（<c>skill_slot</c> → 技能绑定宿主）；是 <see cref="ActionBinding"/> 的回落来源。</summary>
        public ActionSlotSkillBinding Binding { get; }

        /// <summary>生产装配实际使用的输入动作 → 技能映射（武器优先、槽位回落，缓冲出口与时间线取消进入共用这一份）。</summary>
        public IActionSkillBinding ActionBinding { get; }

        /// <summary>换装链：装备变化 → 对账主手/副手武器手感引用与武器族 → 手感解析器失效 → 发布 <c>feel.weapon_changed</c>（姿势族据此刷新）。</summary>
        public EquipmentFeelChain WeaponChain { get; }

        public BufferedActionIntentSink Sink { get; }

        /// <summary>运动层服务（已赋给 <see cref="MovementHost.Motion"/>）。</summary>
        public MotionServices Motion { get; }

        /// <summary>来自选项的本地移动轴动作名；缺省 null。</summary>
        public string? LocalMoveActionName { get; }

        /// <summary>最近一次数据热加载（<c>data.load_completed</c> 触发）的结果；从未发生为 null。被拒时 <see cref="FeelReloadResult.Applied"/> 为假并给出原因。</summary>
        public FeelReloadResult? LastHotReload { get; private set; }

        /// <summary>
        /// 数据热加载（手感设计/05 第 8 节）：重读 <c>feel.*</c> 表并换入解析器（<see cref="FeelSystem.TryReload"/>）；成功后重读武器目录、刷新运动模式规则
        /// （调用方没有自带 <see cref="CarriersFeelOptions.ModeRules"/> 时）、并让换装链对账一次——武器族、普攻引用来自新数据，变化的单位照常发布
        /// <c>feel.weapon_changed</c>。进行中动作的手感快照不变，下一次动作才看到新数据。
        /// </summary>
        internal FeelReloadResult ApplyHotReload(IDataRegistryView registry, FeelWeaponCatalog catalog, bool refreshModeRules)
        {
            // 宽限条件表（found.grace_condition）与手感档案各自独立：档案被拒绝不影响条件表换入，反之亦然（新表达式写坏时 Expr 求值器自己保持旧条件）。
            (GraceEvaluator as ExprGraceConditionEvaluator)?.Reload(registry);

            var result = Feel.TryReload(registry);
            LastHotReload = result;
            if (!result.Applied) return result;

            catalog.Reload();
            if (refreshModeRules) Motion.ModeRules = MotionModeRuleSet.FromProfiles(Feel.Profiles);
            WeaponChain.ReconcileAll("data_reloaded");
            return result;
        }

        internal CarriersFeelSystem(
            FeelSystem feel, IFeelResolver resolver, RulesFeelSystem rules, InputBufferHost inputBuffer, GraceTracker? grace,
            ActionSlotSkillBinding binding, IActionSkillBinding actionBinding, EquipmentFeelChain weaponChain,
            BufferedActionIntentSink sink, MotionServices motion, string? localMoveActionName,
            List<SubscriptionHandle> subscriptions, IGraceConditionEvaluator? graceEvaluator = null)
        {
            GraceEvaluator = graceEvaluator;
            Feel = feel;
            Resolver = resolver;
            Rules = rules;
            InputBuffer = inputBuffer;
            Grace = grace;
            Binding = binding;
            ActionBinding = actionBinding;
            WeaponChain = weaponChain;
            Sink = sink;
            Motion = motion;
            LocalMoveActionName = localMoveActionName;
            _subscriptions = subscriptions;
        }

        public void Dispose()
        {
            for (var i = 0; i < _subscriptions.Count; i++) _subscriptions[i].Dispose();
            _subscriptions.Clear();
            WeaponChain.Dispose();
            Sink.Dispose();
            Rules.Dispose();
        }
    }

    /// <summary>
    /// 手感机制在生产装配里的接线（手感落地 S10）：由 <see cref="CarriersAssembly"/> 构造的最后一步调用。顺序：装配解析器（带生产提供者）→ 规则层接线
    /// （动作时钟、受击裁决、时间线协作者、移动输入通知）→ 失效订阅 → 输入缓冲（声明动作、映射、出口、tick 步骤 1 处理器、时间线拉取口）→ 运动层。
    /// <para>
    /// 判断记录（单一时钟）：动作时钟只在 <see cref="HitFeelAssembly.Attach"/> 里创建一次，输入缓冲、时间线、局部顿帧、运动层的 <c>frozen</c> 叠加态读的都是它。
    /// </para>
    /// <para>
    /// 判断记录（失效订阅）：装备变化、光环施加/移除使对应单位缓存失效，单位销毁时顺带清缓冲。订阅的事件经事件总线在步骤 7 派发，所以一次
    /// 装备变化对手感的影响从当 tick 的派发之后才可见（同 tick 内更早的步骤读到旧值），与既有事件驱动模块同一口径。数据热加载（<see cref="FeelResolver.Reload(FeelProfileSet)"/>）
    /// 由 <c>data.load_completed</c> 订阅接线（M2-B，见 <see cref="CarriersFeelSystem.LastHotReload"/>）；标定行变化同样热换（M3-B）：进行中的动作保持原快照，下一个动作用新标定。
    /// </para>
    /// </summary>
    public static class CarriersFeelAssembly
    {
        public static CarriersFeelSystem Attach(
            CarriersAssembly carriers, IDataRegistryView registry, IEventBus bus, IWorldSim world,
            CarriersFeelOptions options, double stepSeconds)
        {
            if (carriers == null) throw new ArgumentNullException(nameof(carriers));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var rules = carriers.Rules;
            var fields = FeelFields.Default;

            var providers = new FeelProviders
            {
                Body = new CreatureTemplateFeelBodyProvider(world, registry),
                Tags = new UnitTagFeelProvider(carriers.Units),
                Equipment = new EquippedWeaponFeelProvider(carriers.Equipment, registry, options.MainHandSlot, options.OffhandSlot),
                Action = new ActionStateFeelProvider(rules.Skill),
                Temporary = new AuraFeelTemporaryProvider(rules.Skill.AuraQuery, registry, fields),
            };

            var assembled = FeelAssembly.Assemble(registry, new FeelAssemblyOptions
            {
                StepSeconds = stepSeconds,
                CalibrationId = options.CalibrationId,
                Providers = providers,
                Fields = fields,
            });
            if (!assembled.IsAssembled)
            {
                throw new InvalidOperationException(
                    "手感系统已在装配选项里启用，但没有可装配的数据：" + assembled.Reason
                    + "。要么把 feel.* 数据（如 data/_feel 与游戏自己的标定行）加入数据根，要么不传手感装配选项。");
            }

            var feel = assembled.System!;
            var resolver = new InvalidatingActionFeelResolver(feel.Resolver);
            var rulesFeel = RulesFeelAssembly.Attach(rules, resolver, options, stepSeconds);

            // 受击半径（M5-S2a，缺省关闭）：形状查询与接触点共用同一个半径来源（TargetHost.Options 是装配根传入的同一个实例）。
            if (options.HitRadiusFromFeel && rules.Targeting.Options.TargetRadius == null)
            {
                var targetingOptions = rules.Targeting.Options;
                var units = carriers.Units;
                targetingOptions.TargetRadius = id => HurtRadius(resolver, id);
                targetingOptions.MaxTargetRadiusProvider = () =>
                {
                    var max = 0.0;
                    var all = units.AllUnits;
                    for (var i = 0; i < all.Count; i++)
                    {
                        var radius = HurtRadius(resolver, all[i]);
                        if (radius > max) max = radius;
                    }

                    return max;
                };
            }

            var subscriptions = new List<SubscriptionHandle>();

            // ---- 输入缓冲：声明动作、映射、出口、tick 步骤 1、时间线拉取口。
            var buffer = new InputBufferHost(bus, new InputBufferOptions
            {
                StepSeconds = stepSeconds,
                Feel = resolver,
                ActionClock = rulesFeel.Clock,
            });
            DeclareActions(buffer, registry, options.ExtraActions);

            // 输入动作 → 技能：普通攻击动作优先取当前主手武器的普攻技能（换装后自动切换），没有则回落到 skill_slot 槽位绑定；缓冲出口与时间线取消进入共用同一份。
            var slotBinding = new ActionSlotSkillBinding(buffer, carriers.SkillBindings);
            var weaponCatalog = new FeelWeaponCatalog(registry); // 动作绑定与换装链共用同一份目录，数据热加载时一次重读
            var actionBinding = new WeaponPreferredActionBinding(
                new WeaponActionBinding(providers.Equipment, weaponCatalog), slotBinding, options.AutoAttackActions);
            var sink = new BufferedActionIntentSink(
                buffer, actionBinding, rules.Skill, world, bus, rulesFeel.HitFeel.Host, stepSeconds);
            rulesFeel.Timeline.Input = buffer;
            rulesFeel.Timeline.Binding = actionBinding;

            // 换装链：装备变化时对账武器引用与武器族，变化则发布 feel.weapon_changed（表现层姿势族、反馈变体、界面订阅它）。读档在事件抑制作用域内重放装备，
            // 链额外订阅 save.loaded 对账；已知单位取世界里的全部实体（对账只读装备宿主，没有装备的实体对账结果等于初值，不发事件）。
            var weaponChain = new EquipmentFeelChain(
                bus, providers.Equipment, weaponCatalog, resolver,
                () => world.QueryEntities(default).Select(e => e.EntityId));

            // 时间线目标辅助（S11）：候选解析缺省取目标选择链（同一个 TargetHost、同一个 WorldUnitAccess——后者同时是 IUnitFacingWriter，
            // 朝向修正才能落地）；游戏层有自己的软锁定实现时经 CarriersFeelOptions.TargetAssist 覆盖。只有技能声明了 timeline.target_assist 才生效。
            rulesFeel.Timeline.TargetAssist = new ActionTargetAssistAdapter(
                options.TargetAssist ?? new TargetChainAssistResolver(rules.Targeting, carriers.Units), carriers.Units);

            // 手感落地 M3-B：求值器缺省为框架的 Expr 求值（found.grace_condition 的 expr 在行动者上下文里求值），游戏不写代码；options.GraceEvaluator 仍可覆盖。
            // 手感落地 M4-G：目标来源 = 游戏的 GraceTargetResolver 覆盖 → 本次施法请求携带的目标 → 自动攻击的当前目标；表达式还可读 event.aim_* 瞄点上下文（位置、视线、动作射程）。
            var aimServices = new GraceAimServices
            {
                Position = id => world.GetEntity(id)?.Position,
                LineOfSight = (from, to) => rules.Spatial.HasLineOfSight(from, to),
                ConditionRange = (actor, condition) => GraceConditionRange(buffer, actionBinding, rules.Skill, actor, condition),
            };
            var graceEvaluator = options.GraceEvaluator ?? new ExprGraceConditionEvaluator(
                registry, rules.ExprHostFactory, rules.ExprSchema, options.GraceTargetResolver,
                id => rules.AutoAttack.GetTarget(id), aimServices);
            var grace = new GraceTracker(graceEvaluator, resolver);
            InputBufferTickHandler.Register(world, buffer, sink, grace);
            // 手感落地 M2-B（手感设计/01 第 2.4 节）：施法管线步骤 7 经它判断"条件刚刚失效、仍在宽限内"。
            rulesFeel.Timeline.Grace = grace;

            // ---- 失效与清理订阅。
            // 装备变化不在这里无差别失效：换装链（EquipmentFeelChain）按"主手/副手武器引用与武器族是否变化"对账，只有武器相关状态变化才失效并重算
            // （换护甲、换饰品不影响手感解析，手感设计/08 第 1 节）。此前这里对每次装备/卸下都失效一次，与链的失效叠加：武器变化版本号 +2，
            // 换护甲也 +1（无谓重算，实验室 equip.stale_version_steps 指标证明）。
            subscriptions.Add(bus.Subscribe<AuraAppliedEvent>(
                RulesEventKeys.AuraApplied, e => resolver.Invalidate(e.TargetId, "aura_changed")));
            subscriptions.Add(bus.Subscribe<AuraRemovedEvent>(
                RulesEventKeys.AuraRemoved, e => resolver.Invalidate(e.TargetId, "aura_changed")));
            // 手感落地 M2-B：层数变化（叠层、掉层）使该光环的手感修饰随层数重算（手感设计/05 第 6 节）。
            subscriptions.Add(bus.Subscribe<AuraStackChangedEvent>(
                RulesEventKeys.AuraStackChanged, e => resolver.Invalidate(e.TargetId, "aura_changed")));
            subscriptions.Add(bus.Subscribe<UnitDiedEvent>(
                RulesEventKeys.UnitDied, e => buffer.Clear(e.UnitId)));
            // 手感落地 M3-B：单位从登记起就开始采样宽限条件（装配时已有的单位现在登记，之后出生的在 entity.created 派发时登记），第一次按键之前就有历史可查。
            // 手感落地 M4-G：惰性分配——没有任何动作声明宽限条件时不为任何单位建缓冲（单位很多时省下每个单位一份空缓冲）；条件声明出现的那一刻
            // （InputBufferHost.GraceConditionsDeclared）才为已有的单位补登记，之后出生的单位在出生时登记。
            if (options.AutoRegisterGraceActors)
            {
                void RegisterExistingGraceActors()
                {
                    foreach (var entity in world.QueryEntities(default))
                    {
                        if (IsGraceActorKind(entity.Kind)) buffer.RegisterActor(entity.EntityId);
                    }
                }

                if (buffer.GraceConditionNames.Count > 0) RegisterExistingGraceActors();
                buffer.GraceConditionsDeclared += RegisterExistingGraceActors;

                subscriptions.Add(bus.Subscribe<EntityCreatedEvent>(
                    SimEventKeys.EntityCreated, e =>
                    {
                        if (IsGraceActorKind(e.Kind) && buffer.GraceConditionNames.Count > 0) buffer.RegisterActor(e.EntityId);
                    }));
            }

            subscriptions.Add(bus.Subscribe<EntityDestroyedEvent>(
                SimEventKeys.EntityDestroyed, e =>
                {
                    buffer.RemoveActor(e.EntityId);
                    grace?.Unregister(e.EntityId);
                    resolver.Invalidate(e.EntityId, "entity_destroyed");
                }));

            // ---- 运动层。
            var motion = new MotionServices
            {
                Feel = resolver,
                Actions = rules.Skill.ActionStateQuery,
                RootMotion = options.RootMotion,
                Curves = options.Curves,
                TargetAssist = options.TargetAssist,
                ModeRules = options.ModeRules ?? MotionModeRuleSet.FromProfiles(feel.Profiles),
            };
            carriers.Movement.Motion = motion;
            MotionHitFeelWiring.Connect(carriers.Movement, rulesFeel.Clock, rulesFeel.HitFeel.Host);

            // 击飞口（竖直轴能力包）：世界装配了竖直运动服务（MovementOptions.Vertical 非空）才接，平面世界保持为空，击飞静默不发生。
            if (carriers.VerticalMotion is Core.Rules.Common.ILaunchSink launchSink)
            {
                rulesFeel.HitFeel.Host.Launch = launchSink;
            }

            // 腾空查询（腾空受击反应 air_hit_reaction）：同样只在有竖直运动服务时接。
            if (carriers.VerticalMotion is Core.Rules.Common.IAirborneQuery airborneQuery)
            {
                rulesFeel.HitFeel.Host.Airborne = airborneQuery;
            }

            var system = new CarriersFeelSystem(
                feel, resolver, rulesFeel, buffer, grace, slotBinding, actionBinding, weaponChain, sink, motion, options.LocalMoveActionName, subscriptions,
                graceEvaluator);

            // ---- 数据热加载（手感落地 M2-B，手感设计/05 第 8 节、ADR-0019）：宿主（模板的 DataHotReload 或测试）在 DataRegistry.Reload 之后补发
            // data.load_completed，这里据此重读 feel.* 表换入解析器。被拒时（校验有错误等）保持当前档案，原因记在 LastHotReload 上。
            subscriptions.Add(bus.Subscribe<DataLoadCompletedEvent>(
                DataRegistryEventKeys.LoadCompleted, _ => system.ApplyHotReload(registry, weaponCatalog, options.ModeRules == null)));
            return system;
        }

        /// <summary>单位的受击半径（世界单位）：<c>unit_body_radius</c>（标定后）× <c>hurt_radius_scale</c>（缺省 1）；没有体积半径恒为 0。</summary>
        private static double HurtRadius(IFeelResolver resolver, Id unitId)
        {
            var view = resolver.ResolveJudging(unitId);
            var body = view.GetAbsolute(FeelFieldNames.UnitBodyRadius);
            if (body.Kind != FeelValueKind.Number || !(body.AsNumber() > 0.0)) return 0.0;

            var scale = view.GetRaw(FeelFieldNames.HurtRadiusScale);
            return body.AsNumber() * (scale.Kind == FeelValueKind.Number ? scale.AsNumber() : 1.0);
        }

        private static bool IsGraceActorKind(string kind) => kind == EntityKinds.Player || kind == EntityKinds.Creature;

        /// <summary>
        /// 框架内置宽限条件的射程来源（手感落地 M4-G）：引用条件 <paramref name="condition"/> 的输入动作绑定的技能的射程，多个动作引用同一条件取其中最小的正射程
        /// （保守）；动作没有绑定技能或技能没有射程限制时不参与，全部没有则返回 0（没有射程限制）。
        /// </summary>
        private static double GraceConditionRange(
            InputBufferHost buffer, IActionSkillBinding binding, SkillHost skill, Id actor, Id condition)
        {
            var min = 0.0;
            var actions = buffer.ActionsWithGraceCondition(condition);
            for (var i = 0; i < actions.Count; i++)
            {
                var definition = buffer.GetDefinition(actions[i]);
                if (definition == null) continue;

                var intent = new BufferedIntent(
                    definition.ActionId, definition.Class ?? ActionClass.Menu, 0, 0, 0, null, BufferHoldState.Tap, 0, false);
                if (!binding.TryResolveSkill(actor, intent, out var skillId)) continue;

                var range = skill.GetSkillRange(skillId);
                if (range > 0 && (min <= 0 || range < min)) min = range;
            }

            return min;
        }

        private static void DeclareActions(InputBufferHost buffer, IDataRegistryView registry, IReadOnlyList<ActionDefinition>? extra)
        {
            var definitions = new List<ActionDefinition>();
            var tables = registry.Tables;
            for (var i = 0; i < tables.Count; i++)
            {
                if (tables[i] != "found.input_action") continue;
                foreach (var record in registry.GetAll("found.input_action")) definitions.Add(ActionDefinition.FromRecord(record));
                break;
            }

            if (extra != null) definitions.AddRange(extra);
            buffer.DeclareActions(definitions);
        }
    }
}
