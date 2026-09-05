using System;
using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Gameplay.Assembly
{
    /// <summary>策略配置项：<see cref="TimeModelSwitch"/> 默认参与者解析（<see cref="ParticipantSearchRadius"/>）
    /// 使用的搜索半径（见类型判断记录）。</summary>
    public sealed class TimeModelSwitchOptions
    {
        public double ParticipantSearchRadius { get; set; } = 30.0;
    }

    /// <summary>
    /// 主循环模式切换（见 03_运行时骨架.md 第 3.3 节、ADR-0013 决策 5）：订阅 <c>combat.entered</c>/
    /// <c>combat.left</c>，按 <c>found.time_model</c> 的 <c>exploration_time_model</c>/
    /// <c>combat_time_model</c> 决定是否在连续/离散之间切换；切换时驱动
    /// <see cref="ITurnScheduler.BeginCombat"/>/<see cref="ITurnScheduler.EndCombat"/>、
    /// <see cref="ISimClockHost.Mode"/>、全局计时器的时间单位换算（<see cref="SimTimers.RescaleAll"/>）
    /// 与 <see cref="IAppStateHost"/> 的 <c>Combat</c> 子状态。
    /// <para>
    /// 判断记录（参与者解析）：<c>combat.entered</c>/<c>combat.left</c>（见
    /// <c>Core.Rules.Combat.CombatHost</c>）按单位逐个触发，不是"一次战斗"的整体事件，事件本身
    /// 只携带 <c>unitId</c>，不携带"这次战斗还有哪些参战方"。本类型默认按"触发单位为圆心、
    /// <see cref="TimeModelSwitchOptions.ParticipantSearchRadius"/> 半径内全部存活单位"近似"该次
    /// 战斗的敌对双方单位"（03 第 3.3 节步骤 1 用语"参与者 = 该次战斗的敌对双方单位，取自 combat
    /// 事件字段或 IUnitAccess"——本类型走的正是"或 IUnitAccess"这条路径，经
    /// <see cref="ISpatialQuery"/> 实现）；调用方（<c>GameplayAssembly</c>）如需更精确的参与者来源
    /// （如遭遇系统 <c>encounter.def.units</c> 登记的完整名单），可经构造参数
    /// <c>participantsResolver</c> 注入自定义解析逻辑覆盖默认行为。
    /// </para>
    /// <para>
    /// 判断记录（只在"首个进战单位"这一刻切换）：<see cref="_activeCombatants"/> 从 0 变 1 时才
    /// 触发 <see cref="SwitchToDiscrete"/>（此时一次性解析出全部参与者，见上一条），后续同一场
    /// 战斗里再有单位加入战斗（<c>combat.entered</c>）不会二次调用 <see cref="ITurnScheduler.BeginCombat"/>
    /// ——<c>ITurnScheduler</c> 契约没有"追加参与者"的原语（03 第 9 节签名只有
    /// <c>beginCombat(participants)</c> 一次性传入整份名单），中途追加不在本次任务范围内，已在
    /// 交付报告"做不了的事"列出。
    /// </para>
    /// </summary>
    public sealed class TimeModelSwitch
    {
        private readonly ITurnScheduler _scheduler;
        private readonly ISimClockHost _clockHost;
        private readonly IAppStateHost _appState;
        private readonly IWorldSim _world;
        private readonly IUnitAccess _units;
        private readonly ISpatialQuery _spatial;
        private readonly IEventBus _bus;
        private readonly TimeModelSwitchOptions _options;
        private readonly Func<Id, IReadOnlyList<Id>>? _participantsResolver;

        private readonly HashSet<Id> _activeCombatants = new HashSet<Id>();

        public TimeModelDefinition ExplorationModel { get; }

        /// <summary>可能为空：某些游戏数据集可能只声明探索时间模型（理论上 04 要求"探索与战斗各自
        /// 登记一条"，但本类型不因数据缺失而抛异常——缺失战斗时间模型时恒不切换，行为等价于
        /// "战斗沿用连续模式"，见 <see cref="OnCombatEntered"/>）。</summary>
        public TimeModelDefinition? CombatModel { get; }

        public TimeModelMode CurrentMode { get; private set; } = TimeModelMode.Continuous;

        public TimeModelSwitch(
            ITurnScheduler scheduler,
            ISimClockHost clockHost,
            IAppStateHost appState,
            IWorldSim world,
            IUnitAccess units,
            ISpatialQuery spatial,
            IEventBus bus,
            IDataRegistryView registry,
            TimeModelSwitchOptions? options = null,
            Func<Id, IReadOnlyList<Id>>? participantsResolver = null)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _clockHost = clockHost ?? throw new ArgumentNullException(nameof(clockHost));
            _appState = appState ?? throw new ArgumentNullException(nameof(appState));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            _options = options ?? new TimeModelSwitchOptions();
            _participantsResolver = participantsResolver;

            ExplorationModel = LoadModel(registry, "exploration")
                ?? throw new InvalidOperationException("found.time_model 未登记 scope=exploration 的记录");
            CombatModel = LoadModel(registry, "combat");

            _bus.Subscribe<CombatEnteredEvent>(RulesEventKeys.CombatEntered, OnCombatEntered);
            _bus.Subscribe<CombatLeftEvent>(RulesEventKeys.CombatLeft, OnCombatLeft);

            // 判断记录：死亡单位也要从活跃参战集合移除，不能只依赖 combat.left——06 第 4.5 节脱战
            // 判定只检查"周边无存活的敌对仇恨来源"，不检查"这个单位自己是否还活着"，一个已死亡的
            // 单位若其仇恨表里的敌对来源仍然存活，combat.left 永远不会为它触发（见
            // Core.Rules.Combat.CombatHost.Update 的 HasLivingHostileThreatSource 逻辑，只判断
            // 来源是否存活，不判断本单位是否存活）。不在这里去重会让"只剩一方存活"的战斗永远卡在
            // 离散模式（活跃集合里那个已死亡的单位永远不会被移除），本类型因此额外订阅
            // unit.died 主动移除，是对 06 既有脱战判定的一处必要补充，不改动 06/CombatHost 本身。
            _bus.Subscribe<UnitDiedEvent>(RulesEventKeys.UnitDied, OnUnitDied);
        }

        /// <summary>遭遇定义的 <c>combat_mode_override</c>（见 08_玩法层_掉落任务对话关卡.md）可以
        /// 在某次具体遭遇开始前调用本方法，临时覆盖 <see cref="CombatModel"/> 的判断——本类型不
        /// 直接依赖 <c>core/gameplay/encounter</c>（避免 assembly 内部循环依赖同一目录下另一模块的
        /// 具体宿主类型），调用方（<c>GameplayAssembly</c>，持有 <c>Encounter</c>）在遭遇开始时读取
        /// <c>EncounterDefinition.CombatModeOverride</c> 并据此调用本方法。</summary>
        public bool PendingOverrideIsDiscrete { get; set; }

        public void SetPendingOverride(string? combatModeOverride)
        {
            PendingOverrideIsDiscrete = combatModeOverride == "discrete";
        }

        private void OnCombatEntered(CombatEnteredEvent evt)
        {
            _activeCombatants.Add(evt.UnitId);

            if (CurrentMode != TimeModelMode.Continuous || _activeCombatants.Count != 1)
            {
                return;
            }

            var wantsDiscrete = PendingOverrideIsDiscrete || (CombatModel != null && CombatModel.Mode == TimeModelMode.Discrete);
            if (!wantsDiscrete || CombatModel == null)
            {
                return;
            }

            SwitchToDiscrete(evt.UnitId);
        }

        private void OnCombatLeft(CombatLeftEvent evt)
        {
            _activeCombatants.Remove(evt.UnitId);
            SwitchToContinuousIfNoneActive();
        }

        private void OnUnitDied(UnitDiedEvent evt)
        {
            _activeCombatants.Remove(evt.UnitId);
            SwitchToContinuousIfNoneActive();
        }

        private void SwitchToContinuousIfNoneActive()
        {
            if (CurrentMode == TimeModelMode.Discrete && _activeCombatants.Count == 0)
            {
                SwitchToContinuous();
            }
        }

        private void SwitchToDiscrete(Id triggerUnit)
        {
            var participants = ResolveParticipants(triggerUnit);
            if (participants.Count == 0)
            {
                return;
            }

            // 时间单位换算（03 第 3.3 节步骤 2）：以秒计的剩余时长按 seconds_per_turn 折算为
            // 剩余回合数，即乘以 1/seconds_per_turn。
            RescaleTimers(1.0 / CombatModel!.SecondsPerTurn);

            var parameters = new Dictionary<string, object>();
            _scheduler.Configure(CombatModel.InitiativePolicy, parameters);
            _scheduler.BeginCombat(participants);

            _clockHost.Mode = TimeModelMode.Discrete;
            CurrentMode = TimeModelMode.Discrete;
            PendingOverrideIsDiscrete = false;

            _appState.PushSubState(SubStateId.Combat);
        }

        private void SwitchToContinuous()
        {
            _scheduler.EndCombat();
            _clockHost.Mode = TimeModelMode.Continuous;

            // 反向换算：剩余回合数按 seconds_per_turn 折算回秒。
            RescaleTimers(CombatModel!.SecondsPerTurn);

            CurrentMode = TimeModelMode.Continuous;

            if (_appState.CurrentSubState == SubStateId.Combat)
            {
                _appState.PopSubState();
            }
        }

        private void RescaleTimers(double factor)
        {
            if (_world.Timers is SimTimers timers)
            {
                timers.RescaleAll(factor);
            }
        }

        private IReadOnlyList<Id> ResolveParticipants(Id triggerUnit)
        {
            if (_participantsResolver != null)
            {
                return _participantsResolver(triggerUnit);
            }

            var nearby = _spatial.QueryRadius(_units.GetPosition(triggerUnit), _options.ParticipantSearchRadius, QueryFilter.None);
            var set = new SortedSet<Id>();
            set.Add(triggerUnit);
            for (var i = 0; i < nearby.Count; i++)
            {
                var id = nearby[i];
                if (_units.Exists(id) && _units.IsAlive(id))
                {
                    set.Add(id);
                }
            }

            var result = new List<Id>(set.Count);
            foreach (var id in set)
            {
                result.Add(id);
            }
            return result;
        }

        private static TimeModelDefinition? LoadModel(IDataRegistryView registry, string scope)
        {
            var records = registry.GetAll("found.time_model");
            for (var i = 0; i < records.Count; i++)
            {
                var def = TimeModelDefinition.FromRecord(records[i]);
                if (def.Scope == scope)
                {
                    return def;
                }
            }
            return null;
        }
    }
}
