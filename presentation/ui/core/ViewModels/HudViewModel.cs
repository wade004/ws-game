using System;
using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Numbers.PowerSet;
using Core.Numbers.Progression;

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
