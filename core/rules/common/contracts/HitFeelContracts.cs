using Core.Foundation.Common;
using Core.Foundation.Feel;

namespace Core.Rules.Common
{
    // 局部顿帧与受击裁决的契约（手感设计/03 第 3/4 节，ADR-0117）。实现是 core/rules/combat 的 HitFeelHost；
    // 这里只放跨模块要用的接口与值类型：动作时间线/空间命中（后续切片）经 IHitFeelArbiter 取裁决结果填 combat.hit_confirmed，
    // 运动层经 IHitReactionQuery 读硬直状态，受击裁决经 IKnockbackSink 提交击退、经 IStaggerInterruptSink 打断进行中的动作。

    /// <summary>一次命中交给受击裁决的输入（只含裁决要用的事实，不含几何）。</summary>
    public readonly struct HitFeelInput
    {
        public Id AttackerId { get; }

        public Id TargetId { get; }

        /// <summary>命中结局（回避类 = Miss/Dodge/Parry/Immune/Invulnerable，不顿帧、不裁决）。</summary>
        public HitResult HitResult { get; }

        /// <summary>本次命中的落地伤害（回避类为 0）。</summary>
        public double Amount { get; }

        /// <summary>本次命中是否致死（目标落地后生命为零）。</summary>
        public bool IsKill { get; }

        /// <summary>
        /// 攻击方的判定型手感视图。时间线动作传进行中动作的快照（<c>IFeelResolver.GetSnapshot(castInstanceId)?.Judging</c>），
        /// 使热加载不改变一次挥击；为 null 时取攻击方当前解析结果。
        /// </summary>
        public JudgingFeelView? AttackerFeel { get; }

        public HitFeelInput(Id attackerId, Id targetId, HitResult hitResult, double amount, bool isKill, JudgingFeelView? attackerFeel = null)
        {
            AttackerId = attackerId;
            TargetId = targetId;
            HitResult = hitResult;
            Amount = amount;
            IsKill = isKill;
            AttackerFeel = attackerFeel;
            AttackInstanceId = null;
            AttackerFeelOverrides = false;
            Scale = HitFeelScale.Identity;
        }

        /// <summary>
        /// 本次命中的攻击实例 id（手感落地 M5-S2a）：与 <see cref="AttackerFeelOverrides"/>/<see cref="Scale"/> 一起让受击裁决宿主把"这一击用的攻击方视图与缩放"
        /// 记下来，供稍后派发的 <c>combat.hit_confirmed</c> 落地击退/击飞时沿用。缺省 null（不记）。
        /// </summary>
        public Id? AttackInstanceId { get; }

        /// <summary>
        /// <see cref="AttackerFeel"/> 是否是"分段/技能手感覆盖"解析出的视图（M5-S2a）：为真时击退距离、击飞高度、击飞叠加读它，而不是攻击方当前的解析结果；
        /// 为假（缺省，含时间线动作开始快照）保持此前口径——落地阶段读攻击方当前解析结果。
        /// </summary>
        public bool AttackerFeelOverrides { get; }

        /// <summary>蓄力等对顿帧、击退、击飞的缩放（M5-S2a，缺省 <see cref="HitFeelScale.Identity"/> 不缩放）。</summary>
        public HitFeelScale Scale { get; }

        /// <summary>手感落地 M5-S2a 新增重载：携带攻击实例 id、攻击方视图覆盖标志与缩放（新增重载，不改既有物理签名）。</summary>
        public HitFeelInput(
            Id attackerId, Id targetId, HitResult hitResult, double amount, bool isKill, JudgingFeelView? attackerFeel,
            Id? attackInstanceId, bool attackerFeelOverrides, HitFeelScale scale)
            : this(attackerId, targetId, hitResult, amount, isKill, attackerFeel)
        {
            AttackInstanceId = attackInstanceId;
            AttackerFeelOverrides = attackerFeelOverrides;
            Scale = scale;
        }
    }

    /// <summary>
    /// 命中手感缩放（手感落地 M5-S2a）：对一次命中的攻击方顿帧、受击方顿帧、击退距离、击飞高度各乘一个倍率（来自蓄力 <c>charge.feel_scale</c>）。
    /// 倍率 1 即不缩放；<see cref="Identity"/> 是全 1。作用在档案取值之后、限幅之前。
    /// </summary>
    public readonly struct HitFeelScale
    {
        public double AttackerHitstop { get; }

        public double TargetHitstop { get; }

        public double KnockbackDistance { get; }

        public double LaunchHeight { get; }

        public HitFeelScale(double attackerHitstop, double targetHitstop, double knockbackDistance, double launchHeight)
        {
            AttackerHitstop = attackerHitstop;
            TargetHitstop = targetHitstop;
            KnockbackDistance = knockbackDistance;
            LaunchHeight = launchHeight;
        }

        /// <summary>全部倍率为 1（不缩放）。</summary>
        public static HitFeelScale Identity => new HitFeelScale(1.0, 1.0, 1.0, 1.0);

        /// <summary>是否全为 1（不缩放）。</summary>
        public bool IsIdentity => AttackerHitstop == 1.0 && TargetHitstop == 1.0 && KnockbackDistance == 1.0 && LaunchHeight == 1.0;
    }

    /// <summary>
    /// 受击反应的时长与附带决定（手感设计/03 第 4 节、ADR-0145）：由裁决一次算定，随 <c>combat.hit_confirmed</c> 带给落地方，
    /// 使攻击方档案的 <c>hit_stun_scale</c>、<c>knockback_duration_ms</c> 与攻击方快照在落地时不必重算。
    /// 不经裁决的发出方（测试替身、旧调用点）事件里没有它（<see cref="IsSet"/> 为假），落地方退回按受击方当前档案计算（既有行为）。
    /// </summary>
    public readonly struct HitReactionDetail
    {
        /// <summary>是否由裁决填写。</summary>
        public bool IsSet { get; }

        /// <summary>硬直段 tick 数（受击方 <c>hit_stun_ms</c> × 攻击方 <c>hit_stun_scale</c> × 反应类型倍率，换算后）。</summary>
        public int StunTicks { get; }

        /// <summary>倒地段 tick 数（<c>knockdown</c> 才非零，受击方 <c>downed_ms</c>）。</summary>
        public int DownedTicks { get; }

        /// <summary>起身段 tick 数（<c>knockdown</c> 才可能非零，受击方 <c>getup_ms</c>；缺省 0）。</summary>
        public int GetupTicks { get; }

        /// <summary>击退总时长（秒）：攻击方 <c>knockback_duration_ms</c> 声明时为其换算值，否则 0（用游戏级缺省）。</summary>
        public double KnockbackDurationSeconds { get; }

        /// <summary>攻击方被反弹的反应（格挡/弹反类别声明，已过攻击方的霸体与 <c>reaction_cap</c>）；<see cref="HitReaction.None"/> 即无。</summary>
        public HitReaction AttackerReaction { get; }

        /// <summary>攻击方硬直段 tick 数（攻击方被反弹成硬直类反应时，用攻击方自己的 <c>hit_stun_ms</c>）。</summary>
        public int AttackerStunTicks { get; }

        public HitReactionDetail(
            int stunTicks, int downedTicks, int getupTicks, double knockbackDurationSeconds, HitReaction attackerReaction, int attackerStunTicks)
        {
            IsSet = true;
            StunTicks = stunTicks;
            DownedTicks = downedTicks;
            GetupTicks = getupTicks;
            KnockbackDurationSeconds = knockbackDurationSeconds;
            AttackerReaction = attackerReaction;
            AttackerStunTicks = attackerStunTicks;
        }
    }

    /// <summary>受击裁决的结论：填进 <c>combat.hit_confirmed</c> 的冲击等级、两侧顿帧 tick 数、受击反应，外加反应时长。</summary>
    public readonly struct HitFeelOutcome
    {
        /// <summary>冲击等级（来自攻击方手感表受击组 <c>impact_class</c>；暴击/格挡/偏斜/弹反类别可声明替换，见 ADR-0145）。</summary>
        public string ImpactClass { get; }

        /// <summary>攻击方顿帧 tick 数（已限幅、击杀已放大）；0 即不顿帧。</summary>
        public int AttackerHitStopTicks { get; }

        /// <summary>受击方顿帧 tick 数（已限幅；致死恒为 0）；0 即不顿帧。</summary>
        public int TargetHitStopTicks { get; }

        public HitReaction Reaction { get; }

        /// <summary>
        /// 反应持续 tick 数：硬直类为硬直段（含 <c>hit_stun_scale</c> 与反应类型倍率），倒地再加倒地段与起身段；
        /// 受击反应不是硬直类（None/Flinch/Death）为 0。与顿帧分别计时，硬直从受击方顿帧结束后起算。
        /// </summary>
        public int ReactionDurationTicks { get; }

        /// <summary>反应时长与附带决定（硬直/倒地/起身分段、击退时长、攻击方被反弹）；总是由裁决填写。</summary>
        public HitReactionDetail Detail { get; }

        /// <summary>攻击方被反弹的反应（<see cref="HitReactionDetail.AttackerReaction"/> 的便捷读取）。</summary>
        public HitReaction AttackerReaction => Detail.AttackerReaction;

        public HitFeelOutcome(string impactClass, int attackerHitStopTicks, int targetHitStopTicks, HitReaction reaction, int reactionDurationTicks)
            : this(impactClass, attackerHitStopTicks, targetHitStopTicks, reaction, reactionDurationTicks, default)
        {
        }

        public HitFeelOutcome(
            string impactClass, int attackerHitStopTicks, int targetHitStopTicks, HitReaction reaction, int reactionDurationTicks,
            HitReactionDetail detail)
        {
            ImpactClass = impactClass;
            AttackerHitStopTicks = attackerHitStopTicks;
            TargetHitStopTicks = targetHitStopTicks;
            Reaction = reaction;
            ReactionDurationTicks = reactionDurationTicks;
            Detail = detail;
        }

        /// <summary>无任何顿帧与反应（手感系统未装配、离散模式、回避类结局共用）。</summary>
        public static HitFeelOutcome None(string impactClass = "") => new HitFeelOutcome(impactClass, 0, 0, HitReaction.None, 0);
    }

    /// <summary>
    /// 受击裁决入口（手感设计/03 第 4 节）：读手感表与目标韧性属性、霸体状态；除一处例外外不写状态——例外是动态韧性
    /// （手感落地 M4-L）：命中的攻击方档案声明了 <c>poise_damage</c> 时，裁决会扣减目标的韧性池（因此每次真实命中只应调用一次，
    /// 两条调用路径都满足）；不声明 <c>poise_damage</c> 的命中仍是纯读取。
    /// 空间命中切片发 <c>combat.hit_confirmed</c> 前经它取 <see cref="HitFeelOutcome"/>；目标选择式（instant）命中由
    /// 受击裁决宿主自己接 <c>combat.damage_dealt</c> 调用它。
    /// </summary>
    public interface IHitFeelArbiter
    {
        /// <summary>裁决一次命中；手感系统未装配或处于离散模式时返回 <see cref="HitFeelOutcome.None"/>。</summary>
        HitFeelOutcome Evaluate(in HitFeelInput input);
    }

    /// <summary>
    /// 硬直状态的只读查询（手感设计/03 第 4 节、02 第 3.1 节 <c>staggered</c>）：运动层（经 <c>IStaggerStateQuery</c> 适配）、
    /// 动作时间线（硬直中不接受新动作）与表现层只读它。口径是"最近一个已开始处理的 tick 里是否处于该状态"。
    /// </summary>
    public interface IHitReactionQuery
    {
        /// <summary>硬直中（含倒地）。硬直从受击方顿帧结束后起算，顿帧期间尚未处于硬直。</summary>
        bool IsStaggered(Id unitId);

        /// <summary>倒地中（<c>knockdown</c> 反应硬直之后的 <c>downed_ms</c> 段，不含起身段；仍属于 <see cref="IsStaggered"/>）。</summary>
        bool IsDowned(Id unitId);

        /// <summary>
        /// 起身中（倒地段之后的 <c>getup_ms</c> 段；仍属于 <see cref="IsStaggered"/>，ADR-0145）。C# 默认接口成员：既有实现不受影响，缺省恒假。
        /// </summary>
        bool IsGettingUp(Id unitId) => false;

        /// <summary>
        /// 起身无敌中（<c>getup_invuln_ms</c> 声明且起身段内，ADR-0145）：此刻打向该单位的命中被判为 <see cref="HitResult.Invulnerable"/> 回避。
        /// 默认接口成员，缺省恒假。
        /// </summary>
        bool IsGetupInvulnerable(Id unitId) => false;

        /// <summary>硬直剩余 tick 数（不含当前 tick；未处于硬直为 0；顿帧结束前尚未起算时为整段时长）。</summary>
        int RemainingStaggerTicks(Id unitId);
    }

    /// <summary>受击方防御裁决的类别（<see cref="IDefenseArbiter"/>）。</summary>
    public enum DefenseKind
    {
        /// <summary>无防御介入（走命中表）。</summary>
        None = 0,

        /// <summary>起身无敌：命中判为 <see cref="HitResult.Invulnerable"/> 回避。</summary>
        Invulnerable = 1,

        /// <summary>格挡：命中判为 <see cref="HitResult.Block"/>，伤害按 <see cref="DefenseVerdict.DamageScale"/> 缩放。</summary>
        Block = 2,

        /// <summary>弹反（格挡开始后的短窗口内）：命中判为 <see cref="HitResult.Parry"/> 回避，目标不受伤。</summary>
        Parry = 3,
    }

    /// <summary>受击方防御裁决的结论。</summary>
    public readonly struct DefenseVerdict
    {
        public DefenseKind Kind { get; }

        /// <summary>格挡的伤害倍率（<see cref="DefenseKind.Block"/> 才有意义，缺省声明为 1）。</summary>
        public double DamageScale { get; }

        public DefenseVerdict(DefenseKind kind, double damageScale = 1.0)
        {
            Kind = kind;
            DamageScale = damageScale;
        }

        public static DefenseVerdict None => new DefenseVerdict(DefenseKind.None);
    }

    /// <summary>
    /// 受击方防御裁决入口（手感设计/03 第 4 节，ADR-0145）：结算管线（<c>Resolver</c>）在掷命中表之前问一次——
    /// 受击方处于起身无敌、格挡（含方向判定）、弹反窗口时由它直接给出结局，不消耗命中表的随机数；
    /// 没有防御状态时返回 <see cref="DefenseVerdict.None"/>，命中表照旧（既有行为逐位不变）。
    /// 实现是 <c>HitFeelHost</c>；装配根把它挂到 <c>CombatOptions.DefenseArbiter</c>。
    /// </summary>
    public interface IDefenseArbiter
    {
        DefenseVerdict Judge(Id attackerId, Id targetId);
    }

    /// <summary>一个单位的防御（格挡）状态快照。</summary>
    public readonly struct GuardState
    {
        /// <summary>自本次格挡开始经过的动作时钟 tick 数（顿帧期间不增长）；弹反窗口按它判。</summary>
        public int ElapsedTicks { get; }

        public GuardState(int elapsedTicks)
        {
            ElapsedTicks = elapsedTicks;
        }
    }

    /// <summary>
    /// 受击方"防御状态"只读查询（手感设计/03 第 4 节，ADR-0145）：单位此刻是否在格挡。输入侧的"按住维持型动作"（手感设计/01）合并后
    /// 由它的实现接上（<c>HitFeelHost.Guard</c>）；在那之前由动作时间线的 <c>guard_start</c>～<c>guard_end</c> 标记驱动
    /// （<see cref="IActionStateQuery.IsGuarding"/> 的适配，<c>HitFeelHost</c> 在 <c>Guard</c> 未设置时自动使用）。
    /// </summary>
    public interface IGuardStateQuery
    {
        /// <summary>该单位正在格挡则返回真并给出状态；否则假。</summary>
        bool TryGetGuard(Id unitId, out GuardState state);
    }

    /// <summary>
    /// 击退提交口（手感设计/02 第 6 节）：受击裁决算出方向与世界距离（含目标击退抗性与冲击等级倍率），由运动层执行
    /// （<c>forced</c> 来源、<c>ease_out</c> 曲线、导航裁决截断）。<c>MovementHost</c> 实现本接口。
    /// </summary>
    public interface IKnockbackSink
    {
        /// <summary>
        /// 提交一次击退；<paramref name="direction"/> 为非零世界方向（实现会单位化），<paramref name="distanceWorld"/> 为正，
        /// <paramref name="durationSeconds"/> ≤ 0 表示用运动层的缺省击退时长。
        /// </summary>
        void BeginKnockback(Id unitId, Vec2 direction, double distanceWorld, double durationSeconds);
    }

    /// <summary>
    /// 击飞提交口（手感设计/03 第 4 节、06 第 10 节勘误 9）：受击裁决在 <c>knockback</c>/<c>knockdown</c> 反应上按攻击方
    /// <c>launch_height</c>（标定后世界单位，已含目标击退抗性与冲击等级倍率）算出顶点高度，由竖直运动服务把目标抛起
    /// （<c>Core.Carriers.Unit.VerticalMotionHost</c> 实现本接口）。没有竖直轴的世界（平面）里没有实现方——受击裁决的 <c>Launch</c> 为空，
    /// 击飞静默不发生（与击退口缺省为空同一惯例），行为与引入本接口之前逐位一致。
    /// </summary>
    public interface ILaunchSink
    {
        /// <summary>把单位抛起，使其升到脚下起再升高 <paramref name="apexHeightWorld"/>（世界单位，正数）的顶点后落回地面。</summary>
        void BeginLaunch(Id unitId, double apexHeightWorld);

        /// <summary>
        /// 带叠加语义的击飞（ADR-0130 追加决定"击飞叠加"，手感档案 <c>launch_stack</c>/<c>launch_stack_cap</c>）：
        /// <paramref name="stack"/> 为 <see cref="LaunchStackMode.Restart"/> 与 <see cref="BeginLaunch(Id, double)"/> 完全一致（缺省）；
        /// 为 <see cref="LaunchStackMode.Add"/> 且单位已在空中时，把这次击飞的初速<b>叠加到当前竖直速度上</b>（下落中被击飞先抵消下落速度），
        /// <paramref name="stackCapApexWorld"/>（&gt; 0 时）把叠加后的向上初速限制在"升到该顶点高度所需的初速"以内。
        /// 默认接口成员：忽略叠加语义，退化为 <see cref="BeginLaunch(Id, double)"/>（既有实现无需改动）。
        /// </summary>
        void BeginLaunch(Id unitId, double apexHeightWorld, LaunchStackMode stack, double stackCapApexWorld) =>
            BeginLaunch(unitId, apexHeightWorld);

        /// <summary>
        /// 带绝对高度上限的击飞（手感落地 M4-W1b，手感档案 <c>launch_height_cap</c>）：<paramref name="heightCapWorld"/>（&gt; 0 时）是击飞（含叠加）之后
        /// 单位脚下高度的最高点上限（世界高度，与 <see cref="Core.Rules.Common.IUnitAccess.GetHeightOffset"/> 同一坐标），在叠加上限之外再封一道；
        /// 0 表示不设。默认接口成员：忽略高度上限，退化为 4 参数重载（既有实现无需改动）。
        /// </summary>
        void BeginLaunch(Id unitId, double apexHeightWorld, LaunchStackMode stack, double stackCapApexWorld, double heightCapWorld) =>
            BeginLaunch(unitId, apexHeightWorld, stack, stackCapApexWorld);
    }

    /// <summary>
    /// 腾空查询（竖直运动服务实现；ADR-0130 追加决定"腾空受击"）：受击裁决据此决定是否应用 <c>air_hit_reaction</c>。
    /// 缺省不接——一律视为在地面，行为与 1.95.0 一致。
    /// </summary>
    public interface IAirborneQuery
    {
        /// <summary>该单位此刻是否在空中。</summary>
        bool IsAirborne(Id unitId);
    }

    /// <summary>击飞叠加方式（手感档案 <c>launch_stack</c>）。</summary>
    public enum LaunchStackMode
    {
        /// <summary>重新抛起：起点为当前高度、初速由本次顶点高度重算，不叠加速度（缺省，1.95.0 行为）。</summary>
        Restart = 0,

        /// <summary>叠加：本次击飞的初速加在当前竖直速度上，可选上限。</summary>
        Add = 1,
    }

    /// <summary>
    /// 硬直打断口（手感设计/03 第 4 节）：受击裁决落地一个硬直类反应时调用，实现方终止目标进行中的动作。
    /// 时间线动作的实现方发 <c>action.cancelled{reason: Stagger}</c> 与 <c>skill.cast_interrupted</c>；读条类（instant）
    /// 的实现方打断读条/引导。可注册多个实现，互不影响；没有进行中的动作时实现方静默。
    /// </summary>
    public interface IStaggerInterruptSink
    {
        void InterruptByStagger(Id unitId, Id sourceId);
    }
}
