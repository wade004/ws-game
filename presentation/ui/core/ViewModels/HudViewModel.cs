using System;
using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Numbers.PowerSet;
using Core.Numbers.Progression;
using Core.Rules.Combat;
using Core.Rules.Common;

namespace Presentation.Ui
{
    /// <summary>单条资源条快照（当前值/上限）。</summary>
    public readonly struct PowerBarSnapshot
    {
        public double Current { get; }

        public double Max { get; }

        public PowerBarSnapshot(double current, double max)
        {
            Current = current;
            Max = max;
        }
    }

    /// <summary>
    /// HUD 视图模型（见 09_表现层.md 第 7.1 节 UI 组成"HUD"——"生命/资源条、等级、目标框"，见
    /// <see cref="UiPanel.Hud"/> 判断记录：状态栏与目标框并入本视图模型，不单开面板类别）。纯数据
    /// + 刷新逻辑，不含任何绘制：<see cref="Refresh"/> 经 <see cref="IUiDataSource.Query"/> 重新拉取
    /// 快照，构造期订阅 <c>power.changed</c>/<c>power.depleted</c>/<c>progression.level_up</c> 事件
    /// 触发自动刷新（铁律 P1、P2）。
    /// <para>
    /// 技术债 17 收口（2026-09-06）：回合顺序条/行动点显示/"结束回合"三个离散模式单元（09 第 7.1
    /// 节）原由引擎侧独立面板 <c>TurnStatusPanel</c> 承载（不经 <see cref="UiPanel"/>/
    /// <c>UiPanelHost</c> 登记，见 01_分层与依赖.md L5 <c>ui</c> 行、09 第 7.1 节此前的例外注记，
    /// 均已随本次收口撤销）。<see cref="UiPanel"/> 是拍板的"十个值，与十个视图模型一一对应"的封闭
    /// 枚举，"状态栏"这类零散元素按既有判断记录应"并入 Hud"而不是新增第十一个枚举值——本类型因此
    /// 追加可选的 <paramref name="turnScheduler"/>（构造参数，见下）/<c>appState</c>/
    /// <c>awaitingInputSubState</c> 三个可选参数，把回合状态并入 HUD，引擎侧改由
    /// <c>HudPanel</c>（原 <c>TurnStatusPanel</c>）渲染。<see cref="IsTurnBased"/> 为 <c>false</c>
    /// （未传入 <paramref name="turnScheduler"/>，即未装配离散模式）时既有调用方与既有测试行为完全
    /// 不变。直接持有 <see cref="Core.Foundation.SimLoop.TurnScheduler"/> 只读引用的先例是
    /// <see cref="UiIntents"/>（其 <c>_turnScheduler</c> 字段同一惯例、铁律 P1 只读——本类型同样只
    /// 读不写）。
    /// </para>
    /// <para>
    /// 判断记录（订阅时机，二选一——"只在传入了调度器/应用状态时才订阅对应事件"）：新增的
    /// <c>sim.turn_started</c>/<c>sim.turn_ended</c>/<c>sim.round_ended</c>/<c>sim.awaiting_input</c>
    /// 四个事件只在 <c>turnScheduler</c> 非空（即确实装配了离散模式）时才有意义，未装配时这些事件
    /// 从不发出，恒不订阅不产生任何行为差异，只是省一次空订阅；<c>app.state_changed</c> 同理只在
    /// <c>appState</c> 非空时订阅。选择"按需订阅"而不是"恒订阅"，避免未启用回合制的既有调用方
    /// 无谓多出四个永远不触发的订阅项。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="CanEndTurn"/> 额外经 <see cref="IAppStateHost.OnSubStateChanged"/> 直接
    /// 订阅，不只依赖 <c>app.state_changed</c>/<c>sim.awaiting_input</c>）：<c>app.state_changed</c>
    /// 只在主状态迁移时发出，子状态（<c>AwaitingInput</c> 等）的压栈/弹栈按
    /// <c>core/foundation/app_lifecycle</c> 模块判断记录"不发 app.state_changed，提供单独的
    /// OnSubStateChanged 回调订阅"，不经事件总线；而 <c>sim.awaiting_input</c>
    /// （<see cref="Core.Foundation.SimLoop.TurnScheduler.NextStep"/> 内部发出）严格早于
    /// <c>GameplayAssembly.Advance</c> 实际把 <c>AwaitingInput</c> 压入 <see cref="IAppStateHost"/>
    /// 子状态栈这一步——若只靠这两个事件触发 <see cref="Refresh"/>，<see cref="CanEndTurn"/> 会在
    /// 该事件触发的那一刻仍读到旧子状态而被错误刷成 <c>false</c>，此后又没有任何后续事件把它纠正
    /// 回来（`TurnScheduler._awaitingInputSignaled` 只发一次），造成"结束回合"按钮永久不出现的真实
    /// 缺陷。直接订阅 <see cref="IAppStateHost.OnSubStateChanged"/>（子状态变化的权威回调，本模块
    /// 判断记录明确的唯一通道）能在压栈真正发生的那一刻同步触发 <see cref="Refresh"/>，不存在这个
    /// 时序缺口；<c>app.state_changed</c> 仍保留订阅，覆盖"离开 InWorld 导致子状态栈被静默清空
    /// （同一模块判断记录：这一路径连 OnSubStateChanged 也不触发）"这一边缘场景的兜底刷新。
    /// </para>
    /// </summary>
    public sealed class HudViewModel : IDisposable
    {
        private readonly IUiDataSource _dataSource;
        private readonly IReadOnlyList<Id> _powerTypes;
        private readonly Core.Foundation.SimLoop.TurnScheduler? _turnScheduler;
        private readonly IAppStateHost? _appState;
        private readonly SubStateId? _awaitingInputSubState;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private readonly Dictionary<Id, PowerBarSnapshot> _powerBars = new Dictionary<Id, PowerBarSnapshot>();
        private readonly Dictionary<Id, PowerBarSnapshot> _targetPowerBars = new Dictionary<Id, PowerBarSnapshot>();

        /// <summary>本视图模型绑定的玩家单位 id（查询本身经 <see cref="IUiDataSource"/> 的
        /// <c>player.*</c> 路径已经隐式绑定到当前玩家，这里只做展示/断言用途）。</summary>
        public Id PlayerId { get; }

        public int Level { get; private set; }

        public IReadOnlyDictionary<Id, PowerBarSnapshot> PowerBars => _powerBars;

        /// <summary>目标框：当前有目标且至少一种配置的资源类型可查到时非空。</summary>
        public bool HasTarget { get; private set; }

        public IReadOnlyDictionary<Id, PowerBarSnapshot> TargetPowerBars => _targetPowerBars;

        /// <summary>
        /// 消费方反馈第 3 条（2026-09-20，ADR-0048）：当前目标的原始身份 Id（经
        /// <c>target.id</c> 路径，见 <see cref="TargetPathProvider"/>）。当前无目标时为 <c>null</c>。
        /// 判断记录（转发原始 Id，不解析显示名称）：本仓库 <c>presentation/ui</c> 视图模型一贯只转发
        /// 原始 <see cref="Id"/>，由表现层（Unity 侧面板）决定如何呈现——即便玩家自己的身份/名称
        /// 今天也未在 HUD 任何地方被解析成文本，只展示数值化的属性/资源；本字段延续同一惯例，不为此
        /// 新增一套"显示名称解析服务"，避免范围蔓延。
        /// </summary>
        public Id? TargetId { get; private set; }

        /// <summary>
        /// 消费方反馈第 3 条续（2026-09-21，沿用 ADR-0048 口径）：当前目标的显示名文本键（经
        /// <c>target.name</c> 路径，见 <see cref="TargetPathProvider"/>）。当前无目标、目标没有内容
        /// 模板引用、或模板未登记时为 <c>null</c>（后两种情况 <see cref="TargetPathProvider"/> 已记
        /// 一条诊断，本视图模型只转发结果，不重复诊断）。
        /// 判断记录（转发文本键，不做本地化）：同 <see cref="TargetId"/> 判断记录、同
        /// <c>ActionBarSlotSnapshot.NameKey</c> 惯例——本地化文本由接入方的文本宿主按这个键去查，
        /// 本视图模型不持有任何 <c>IL10nHost</c> 依赖。
        /// </summary>
        public Id? TargetName { get; private set; }

        /// <summary>
        /// 消费方反馈第 3 条续（2026-09-21）：当前目标的阵营原始 Id（经 <c>target.faction</c> 路径）。
        /// 当前无目标时为 <c>null</c>。判断记录同 <see cref="TargetId"/>：转发原始 Id，不解析显示
        /// 文本（阵营名称/颜色由具体游戏的表现层按这个 Id 自行映射）。
        /// </summary>
        public Id? TargetFaction { get; private set; }

        /// <summary>
        /// 消费方反馈第四批第 2 条（2026-09-21，ADR-0061）：玩家自身当前是否存活（经 <c>player.alive</c>
        /// 路径，转发 <see cref="Core.Rules.Common.IUnitAccess.Exists"/>+<see
        /// cref="Core.Rules.Common.IUnitAccess.IsAlive"/> 组合结果——权威来源见
        /// <see cref="Presentation.Ui.UnitSubQueries.Alive"/> 判断记录，不是本视图模型自行按生命值
        /// 推算）。装配未接入 <see cref="Core.Rules.Common.IUnitAccess"/>（旧构造重载）时查询恒为
        /// "无"，本属性退化为 <c>false</c>——与"确认死亡"取值相同（历史遗留直读
        /// <c>WowGameBootstrap.PlayerAlive</c> 收口前的既有口径同样把"查不到"与"死亡"收敛成同一个
        /// 布尔假值，本属性收口后延续同一退化方向，不引入第三态），不记诊断（部署选择，同
        /// <see cref="TargetCastingSkillId"/> 未装配 <c>skillBook</c> 时的既有惯例）。
        /// </summary>
        public bool PlayerAlive { get; private set; }

        /// <summary>
        /// 消费方反馈第四批第 2 条（2026-09-21，ADR-0061）：当前目标是否存活（经 <c>target.alive</c>
        /// 路径），判断记录同 <see cref="PlayerAlive"/>。无目标时取值与既有 <see cref="TargetId"/>
        /// 无目标时的口径一致——<c>null</c>，不是 <c>false</c>（"没有目标"与"目标已死亡"是两件不同
        /// 的事，收敛成同一个布尔值会让接入方无法区分"不该画目标框"与"目标框该画成灰色死亡态"）。
        /// </summary>
        public bool? TargetAlive { get; private set; }

        /// <summary>
        /// 消费方反馈第 1 条（2026-09-21，ADR-0056）：玩家自身当前正在读条/引导的技能 id（经
        /// <c>player.casting.skill</c> 路径），未在读条/引导时为 <c>null</c>——与"当前无目标"同一
        /// 既有口径（合法查询、无值，不是异常状态）。
        /// </summary>
        public Id? CastingSkillId { get; private set; }

        /// <summary>消费方反馈第 1 条：玩家自身当前读条/引导的剩余时间（经
        /// <c>player.casting.remaining</c> 路径），未在读条/引导时为 <c>null</c>。</summary>
        public double? CastingRemaining { get; private set; }

        /// <summary>消费方反馈第 1 条：玩家自身当前读条/引导的总时长（经 <c>player.casting.total</c>
        /// 路径），未在读条/引导时为 <c>null</c>。</summary>
        public double? CastingTotal { get; private set; }

        /// <summary>消费方反馈第 1 条：当前目标正在读条/引导的技能 id（经 <c>target.casting.skill</c>
        /// 路径，供表现层展示敌方读条预警），当前无目标或目标未在读条/引导时为 <c>null</c>。</summary>
        public Id? TargetCastingSkillId { get; private set; }

        /// <summary>消费方反馈第 1 条：当前目标读条/引导的剩余时间（经
        /// <c>target.casting.remaining</c> 路径）。</summary>
        public double? TargetCastingRemaining { get; private set; }

        /// <summary>消费方反馈第 1 条：当前目标读条/引导的总时长（经 <c>target.casting.total</c>
        /// 路径）。</summary>
        public double? TargetCastingTotal { get; private set; }

        /// <summary>
        /// 消费方反馈第四批第 1 条（2026-09-21，ADR-0061）：玩家自身当前的普通攻击可观测状态（经
        /// <c>player.auto_attack.state</c> 路径，原样转发 <see
        /// cref="Core.Rules.Combat.AutoAttackHost.GetState"/>——照抄施法条 <see cref="CastingSkillId"/>
        /// 那套既有惯例：数据取自权威宿主，视图模型不自行推算）。视图模型上用枚举本身，不用字符串
        /// （路径层跨越 <c>ExprValue</c> 边界时退化为文本，这里读回来再转成强类型，见
        /// <see cref="Presentation.Ui.UnitSubQueries.AutoAttack"/> 判断记录）。装配未接入
        /// <see cref="Core.Rules.Combat.AutoAttackHost"/>（旧构造重载）时查询恒为"无"，本属性退化为
        /// <see cref="Core.Rules.Combat.AutoAttackState.Off"/>——与"从未开启过"同一取值，
        /// <c>AutoAttackHost.GetState</c> 本身对未知施法者也是恒返回 <see
        /// cref="Core.Rules.Combat.AutoAttackState.Off"/>（见该方法源码），本属性的退化方向与规则层
        /// 自身的"无数据"语义一致，不引入额外的第四态。
        /// </summary>
        public AutoAttackState AutoAttackState { get; private set; }

        /// <summary>消费方反馈第四批第 1 条：当前目标自身的普通攻击可观测状态（经
        /// <c>target.auto_attack.state</c> 路径），判断记录同 <see cref="AutoAttackState"/>；当前无
        /// 目标时同样退化为 <see cref="Core.Rules.Combat.AutoAttackState.Off"/>（普通攻击状态本身没有
        /// "无目标"这一额外语义维度可以借用，不同于 <see cref="TargetAlive"/> 需要三态区分"没有目标"
        /// 与"目标已死亡"）。</summary>
        public AutoAttackState TargetAutoAttackState { get; private set; }

        private readonly List<AuraSnapshot> _auras = new List<AuraSnapshot>();
        private readonly List<AuraSnapshot> _targetAuras = new List<AuraSnapshot>();

        /// <summary>
        /// 消费方反馈第 4 条（2026-09-21，ADR-0056）：玩家自身当前生效的增益/减益列表（经
        /// <c>player.auras.count</c>/<c>player.auras[i].*</c> 路径），按光环创建顺序排列（确定性，
        /// 不依赖字典枚举顺序，见 <c>Core.Rules.Skill.AuraHost.GetActiveAuraSnapshots</c> 判断记录）。
        /// 未装配光环查询能力（构造装配未接入 <c>IAuraQuery</c>）时恒为空列表。
        /// </summary>
        public IReadOnlyList<AuraSnapshot> Auras => _auras;

        /// <summary>消费方反馈第 4 条：当前目标身上的增益/减益列表（经 <c>target.auras.count</c>/
        /// <c>target.auras[i].*</c> 路径），排序惯例同 <see cref="Auras"/>；当前无目标时恒为空
        /// 列表。</summary>
        public IReadOnlyList<AuraSnapshot> TargetAuras => _targetAuras;

        /// <summary>技术债 17：是否装配了离散（回合制）时间模型——构造期传入了非空
        /// <c>turnScheduler</c> 时为 <c>true</c>。为 <c>false</c> 时 <see cref="CurrentActorId"/>
        /// 恒为 <c>null</c>、<see cref="RoundIndex"/> 恒为 0、<see cref="CanEndTurn"/> 恒为
        /// <c>false</c>（原 <c>TurnStatusPanel</c> "（未启用回合制）"占位文案对应的语义）。</summary>
        public bool IsTurnBased => _turnScheduler != null;

        /// <summary>当前行动者（<see cref="IsTurnBased"/> 为 <c>false</c> 或战斗未开始时为
        /// <c>null</c>，对应原 <c>TurnStatusPanel</c> "（不在战斗中）"占位文案）。</summary>
        public Id? CurrentActorId { get; private set; }

        /// <summary>当前轮次（未启用回合制时恒为 0）。</summary>
        public int RoundIndex { get; private set; }

        /// <summary>
        /// GP-PRES-09 收口新增（09 第 7.1 节"回合顺序条（仅离散模式）"）：完整的、稳定排序后的
        /// 行动顺序快照（<see cref="Core.Foundation.SimLoop.TurnScheduler.GetOrder"/> 只读转发），
        /// 未启用离散模式（<see cref="IsTurnBased"/> 为 <c>false</c>）时恒为空列表。此前
        /// <see cref="CurrentActorId"/>/<see cref="RoundIndex"/>/<see cref="CanEndTurn"/> 三个字段
        /// 只覆盖"当前行动者/轮次/能否结束回合"，玩家看不到完整队列——见
        /// <c>architecture/落地计划/audit-20260907/gameplay-presentation.md</c> GP-PRES-09。
        /// </summary>
        public IReadOnlyList<Id> TurnOrder { get; private set; } = Array.Empty<Id>();

        /// <summary>
        /// GP-PRES-09 收口新增（09 第 7.1 节"行动点显示（仅离散模式且启用 action_points 先攻
        /// 策略）"）：当前行动者本轮剩余行动点（<see cref="Core.Foundation.SimLoop.TurnScheduler.GetActionPointsRemaining"/>
        /// 只读转发）。未启用离散模式，或当前没有行动者（<see cref="CurrentActorId"/> 为
        /// <c>null</c>）时恒为 0——账本现在无条件为全部参与者维护（不区分先攻策略，见
        /// <c>TurnScheduler.TryConsumeActionPoints</c> 判断记录），因此本属性在 <c>fixed_order</c>/
        /// <c>initiative_stat</c> 策略下同样有意义；是否要在 UI 上展示（09 文档字面只要求
        /// <c>action_points</c> 策略下展示）由具体游戏的面板实现按策略自行取舍，本视图模型只负责
        /// 提供数据，不做策略过滤。
        /// </summary>
        public double ActionPointsRemaining { get; private set; }

        /// <summary>是否应展示"结束回合"意图入口——语义与原 <c>TurnStatusPanel</c> 的按钮可见性
        /// 完全一致（应用状态当前子态等于 <c>awaitingInputSubState</c>），不额外附加"当前行动者是
        /// 玩家"这条件：是否真的轮到玩家由 <see cref="UiIntents.EndTurn"/> 把关，本属性只表达
        /// "现在允许尝试结束回合"。</summary>
        public bool CanEndTurn { get; private set; }

        /// <summary>
        /// 消费方反馈第九批（阻塞，2026-09-22，ADR-0066）：玩家当前所在区域的原始身份 Id（经
        /// <c>player.area.id</c> 路径，"当前区域"定义见 <see cref="Presentation.Ui.PlayerPathProvider.
        /// ResolveCurrentArea"/> 判断记录——所在的、最近进入且尚未离开、并且配置了显示名的那个触发
        /// 区域）。当前不在任何"有名"区域内时为 <c>null</c>（"无"，含"完全不在任何区域内"与"所在
        /// 区域都没有配置显示名"两种情形，收敛成同一取值——同 <see cref="TargetId"/> 判断记录一贯的
        /// "转发原始 Id，不解析显示名称"惯例，具体名称由接入方按 <see cref="CurrentAreaNameKey"/> 这个
        /// 文本键自行本地化查询）。
        /// </summary>
        public Id? CurrentAreaId { get; private set; }

        /// <summary>消费方反馈第九批：玩家当前所在区域的显示名文本键（经 <c>player.area.name_key</c>
        /// 路径），判断记录同 <see cref="CurrentAreaId"/>；两者恒同时为 <c>null</c> 或同时非
        /// <c>null</c>（同一次"当前区域"解析结果的两个分量）。</summary>
        public Id? CurrentAreaNameKey { get; private set; }

        public HudViewModel(
            IUiDataSource dataSource,
            Id playerId,
            IReadOnlyList<Id> powerTypes,
            Core.Foundation.SimLoop.TurnScheduler? turnScheduler = null,
            IAppStateHost? appState = null,
            SubStateId? awaitingInputSubState = null)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            PlayerId = playerId;
            _powerTypes = powerTypes ?? throw new ArgumentNullException(nameof(powerTypes));
            _turnScheduler = turnScheduler;
            _appState = appState;
            _awaitingInputSubState = awaitingInputSubState;

            _subscriptions.Add(_dataSource.Subscribe(PowerEventKeys.Changed, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(PowerEventKeys.Depleted, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(ProgressionEventKeys.LevelUp, OnRelevantEvent));
            // UI-111-01 根治同惯例（见 InventoryViewModel 类型注释）：同图读档的抑制作用域会连带
            // 压住 power.changed/progression.level_up 本身，只有在该作用域外正常派发的 save.loaded
            // 能保证读档后整体重建（等级、资源条、目标框）。
            _subscriptions.Add(_dataSource.Subscribe(SaveEventKeys.SaveLoaded, OnRelevantEvent));

            if (_turnScheduler != null)
            {
                _subscriptions.Add(_dataSource.Subscribe(SimEventKeys.TurnStarted, OnRelevantEvent));
                _subscriptions.Add(_dataSource.Subscribe(SimEventKeys.TurnEnded, OnRelevantEvent));
                _subscriptions.Add(_dataSource.Subscribe(SimEventKeys.RoundEnded, OnRelevantEvent));
                _subscriptions.Add(_dataSource.Subscribe(SimEventKeys.AwaitingInput, OnRelevantEvent));
            }

            if (_appState != null)
            {
                _subscriptions.Add(_dataSource.Subscribe(AppEventKeys.StateChanged, OnRelevantEvent));
                _subscriptions.Add(_appState.OnSubStateChanged(OnSubStateChanged));
            }

            Refresh();
        }

        private void OnRelevantEvent(IEvent evt) => Refresh();

        private void OnSubStateChanged(SubStateId? oldSubState, SubStateId? newSubState) => Refresh();

        public void Refresh()
        {
            var level = _dataSource.Query($"player.level");
            Level = level.HasValue ? (int)level.Value.AsInt : Level;

            _powerBars.Clear();
            _targetPowerBars.Clear();
            HasTarget = false;

            var targetIdQuery = _dataSource.Query("target.id");
            TargetId = targetIdQuery.HasValue ? targetIdQuery.Value.AsId : (Id?)null;

            var targetNameQuery = _dataSource.Query("target.name");
            TargetName = targetNameQuery.HasValue ? targetNameQuery.Value.AsId : (Id?)null;

            var targetFactionQuery = _dataSource.Query("target.faction");
            TargetFaction = targetFactionQuery.HasValue ? targetFactionQuery.Value.AsId : (Id?)null;

            var currentAreaIdQuery = _dataSource.Query("player.area.id");
            CurrentAreaId = currentAreaIdQuery.HasValue ? currentAreaIdQuery.Value.AsId : (Id?)null;

            var currentAreaNameKeyQuery = _dataSource.Query("player.area.name_key");
            CurrentAreaNameKey = currentAreaNameKeyQuery.HasValue ? currentAreaNameKeyQuery.Value.AsId : (Id?)null;

            var playerAliveQuery = _dataSource.Query("player.alive");
            PlayerAlive = playerAliveQuery.HasValue && playerAliveQuery.Value.AsBool;

            var targetAliveQuery = _dataSource.Query("target.alive");
            TargetAlive = targetAliveQuery.HasValue ? (bool?)targetAliveQuery.Value.AsBool : null;

            var castingSkillQuery = _dataSource.Query("player.casting.skill");
            CastingSkillId = castingSkillQuery.HasValue ? castingSkillQuery.Value.AsId : (Id?)null;
            var castingRemainingQuery = _dataSource.Query("player.casting.remaining");
            CastingRemaining = castingRemainingQuery.HasValue ? (double?)castingRemainingQuery.Value.AsNumber : null;
            var castingTotalQuery = _dataSource.Query("player.casting.total");
            CastingTotal = castingTotalQuery.HasValue ? (double?)castingTotalQuery.Value.AsNumber : null;

            var targetCastingSkillQuery = _dataSource.Query("target.casting.skill");
            TargetCastingSkillId = targetCastingSkillQuery.HasValue ? targetCastingSkillQuery.Value.AsId : (Id?)null;
            var targetCastingRemainingQuery = _dataSource.Query("target.casting.remaining");
            TargetCastingRemaining = targetCastingRemainingQuery.HasValue ? (double?)targetCastingRemainingQuery.Value.AsNumber : null;
            var targetCastingTotalQuery = _dataSource.Query("target.casting.total");
            TargetCastingTotal = targetCastingTotalQuery.HasValue ? (double?)targetCastingTotalQuery.Value.AsNumber : null;

            var autoAttackStateQuery = _dataSource.Query("player.auto_attack.state");
            AutoAttackState = autoAttackStateQuery.HasValue
                ? AutoAttackStateNames.Parse(autoAttackStateQuery.Value.AsString)
                : AutoAttackState.Off;

            var targetAutoAttackStateQuery = _dataSource.Query("target.auto_attack.state");
            TargetAutoAttackState = targetAutoAttackStateQuery.HasValue
                ? AutoAttackStateNames.Parse(targetAutoAttackStateQuery.Value.AsString)
                : AutoAttackState.Off;

            RefreshAuras("player.auras", _auras);
            RefreshAuras("target.auras", _targetAuras);

            foreach (var powerType in _powerTypes)
            {
                var current = _dataSource.Query($"player.power.{powerType}.current");
                var max = _dataSource.Query($"player.power.{powerType}.max");
                if (current.HasValue && max.HasValue)
                {
                    _powerBars[powerType] = new PowerBarSnapshot(current.Value.AsNumber, max.Value.AsNumber);
                }

                var targetCurrent = _dataSource.Query($"target.power.{powerType}.current");
                var targetMax = _dataSource.Query($"target.power.{powerType}.max");
                if (targetCurrent.HasValue && targetMax.HasValue)
                {
                    _targetPowerBars[powerType] = new PowerBarSnapshot(targetCurrent.Value.AsNumber, targetMax.Value.AsNumber);
                    HasTarget = true;
                }
            }

            CurrentActorId = _turnScheduler?.GetCurrentActor();
            RoundIndex = _turnScheduler?.RoundIndex ?? 0;
            TurnOrder = _turnScheduler?.GetOrder() ?? Array.Empty<Id>();
            ActionPointsRemaining = CurrentActorId.HasValue
                ? _turnScheduler!.GetActionPointsRemaining(CurrentActorId.Value)
                : 0.0;
            CanEndTurn = _appState != null && _awaitingInputSubState.HasValue &&
                _appState.CurrentSubState.HasValue &&
                _appState.CurrentSubState.Value.Equals(_awaitingInputSubState.Value);
        }

        /// <summary>
        /// 消费方反馈第 4 条（2026-09-21，ADR-0056）：把 <paramref name="root"/>（<c>player.auras</c>
        /// 或 <c>target.auras</c>）经既有 <c>count</c> + <c>[i].&lt;field&gt;</c> 路径小语法逐条重建
        /// 成 <see cref="AuraSnapshot"/> 列表，写入 <paramref name="target"/>（就地清空重填，不重新
        /// 分配列表实例）。顺序沿用查询结果原样顺序——权威排序（按光环创建顺序）已经在规则层
        /// <c>AuraHost.GetActiveAuraSnapshots</c> 完成（确定性，不依赖字典枚举顺序），本方法只是
        /// 逐下标转发，不重新排序。
        /// <para>
        /// 缺陷修复（消费方反馈第四批第 3 条，2026-09-21）：本方法此前只查询 <c>def</c>/<c>stacks</c>/
        /// <c>remaining</c>/<c>total</c>/<c>name_key</c> 五个子路径、用 <see
        /// cref="AuraSnapshot"/> 五参数构造函数重建快照——该构造函数体内把 <c>Polarity</c>/<c>IconRef</c>
        /// 硬编码为 <see cref="AuraPolarity.Undeclared"/>/<c>null</c>（见其判断记录），导致
        /// <see cref="Auras"/>/<see cref="TargetAuras"/> 的这两个字段无论生产数据是否声明都恒为缺省
        /// 值，与同一份数据经 <c>{root}[i].polarity</c>/<c>icon_ref</c> 路径查询（<see
        /// cref="Presentation.Ui.UnitSubQueries.Auras"/>，直接读 <c>AuraHost.GetActiveAuraSnapshots</c>
        /// 用七参数构造函数建出的快照）读到的真实值不一致——ADR-0060 交付 <c>Polarity</c>/<c>IconRef</c>
        /// 时遗漏同步更新本方法，是实现缺陷，不是能力未交付。现补齐这两个子路径查询，改用七参数构造
        /// 函数重建快照。<c>polarity</c> 子路径对外是字符串（<c>AuraPolarityNames.ToText</c> 转换结果，
        /// 见 <c>UnitSubQueries.Auras</c> 判断记录"这一层仍输出字符串"），这里查到非空字符串后用
        /// <see cref="AuraPolarityNames.Parse"/> 转回同一枚举值再重建快照，不再转一次文本；未声明
        /// （查询返回"无"）时退化为 <see cref="AuraPolarity.Undeclared"/>，惯例同 <c>name_key</c> 等
        /// 既有可选字段。
        /// </para>
        /// </summary>
        private void RefreshAuras(string root, List<AuraSnapshot> target)
        {
            target.Clear();

            var countQuery = _dataSource.Query($"{root}.count");
            var count = countQuery.HasValue ? (int)countQuery.Value.AsInt : 0;
            for (var i = 0; i < count; i++)
            {
                var defQuery = _dataSource.Query($"{root}[{i}].def");
                if (!defQuery.HasValue)
                {
                    continue;
                }

                var stacksQuery = _dataSource.Query($"{root}[{i}].stacks");
                var remainingQuery = _dataSource.Query($"{root}[{i}].remaining");
                var totalQuery = _dataSource.Query($"{root}[{i}].total");
                var nameKeyQuery = _dataSource.Query($"{root}[{i}].name_key");
                var polarityQuery = _dataSource.Query($"{root}[{i}].polarity");
                var iconRefQuery = _dataSource.Query($"{root}[{i}].icon_ref");

                target.Add(new AuraSnapshot(
                    defQuery.Value.AsId,
                    stacksQuery.HasValue ? (int)stacksQuery.Value.AsInt : 0,
                    remainingQuery.HasValue ? (double?)remainingQuery.Value.AsNumber : null,
                    totalQuery.HasValue ? (double?)totalQuery.Value.AsNumber : null,
                    nameKeyQuery.HasValue ? (Id?)nameKeyQuery.Value.AsId : null,
                    polarityQuery.HasValue ? AuraPolarityNames.Parse(polarityQuery.Value.AsString) : AuraPolarity.Undeclared,
                    iconRefQuery.HasValue ? (Id?)iconRefQuery.Value.AsId : null));
            }
        }

        public void Dispose()
        {
            foreach (var handle in _subscriptions)
            {
                handle.Dispose();
            }
            _subscriptions.Clear();
        }
    }
}
