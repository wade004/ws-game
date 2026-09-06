using System;
using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Numbers.Faction;
using Core.Rules.Combat;
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
    /// 判断记录（参与者解析，ADR-0013 补齐任务修订；收边任务更新）：<c>combat.entered</c>/
    /// <c>combat.left</c>（见 <c>Core.Rules.Combat.CombatHost</c>）按单位逐个触发，不是"一次战斗"
    /// 的整体事件——06 第 8 节事件词汇表此前只给 <c>combat.entered</c> 登记 <c>unitId</c> 一个
    /// 字段，收边任务已勘误补齐 <c>hostileId</c>（首个敌对目标，见
    /// <see cref="Core.Rules.Common.Events.CombatEnteredEvent.HostileId"/>；事件字段是登记表内容，
    /// 不是 12 第 2 节"新增原语"，不需要走该审批流程）。本类型因此走 03 第 3.3 节步骤 1
    /// "参与者 = 该次战斗的敌对双方单位，取自 combat 事件字段<b>或</b> IUnitAccess"里的<b>前一条</b>
    /// 路径为主：<see cref="ResolveParticipants"/> 收到非空 <c>hostileId</c> 时优先直接把它纳入
    /// 参与者集合（精确值，不必猜）；半径查询 + 阵营近似从"唯一判定来源"降级为"回退/补充来源"，
    /// 继续执行只是为了发现 <c>hostileId</c> 之外的其余参与者（如 AoE 命中的多个敌人）——
    /// <see cref="_factions"/> 非空时，半径内候选单位按"与触发单位同阵营（友军协同作战）或与触发
    /// 单位阵营互为敌对"过滤，排除半径内的中立/无关旁观者；<see cref="_factions"/> 为空时半径内
    /// 全部存活单位都算参与者（不做阵营过滤）。调用方（<c>GameplayAssembly</c>）如需更精确的参与者
    /// 来源（如遭遇系统 <c>encounter.def.units</c> 登记的完整名单），仍可经构造参数
    /// <c>participantsResolver</c> 注入自定义解析逻辑，优先级最高、完全覆盖默认行为（含
    /// <c>hostileId</c> 在内的默认解析都不会被调用）。
    /// </para>
    /// <para>
    /// 判断记录（中途加入/离场，ADR-0013 补齐任务新增）：<see cref="_activeCombatants"/> 从 0 变 1
    /// 时仍然触发 <see cref="SwitchToDiscrete"/>（此时一次性解析出全部参与者，见上一条）；但
    /// 已处于离散模式后，同一场战斗里再有单位加入（<c>combat.entered</c>）或死亡（<c>unit.died</c>）
    /// 不再是"整场战斗结束前的死信息"——本类型改用
    /// <see cref="Core.Foundation.SimLoop.TurnScheduler.AddParticipant"/>/
    /// <see cref="Core.Foundation.SimLoop.TurnScheduler.RemoveParticipant"/>（这两个方法不在
    /// <see cref="ITurnScheduler"/> 契约里，属于该具体类型的便利成员，见其类型判断记录）实时同步
    /// 到当前轮的行动顺序，本类型的 <see cref="_scheduler"/> 字段类型因此从 <see cref="ITurnScheduler"/>
    /// 收紧为具体类型 <see cref="Core.Foundation.SimLoop.TurnScheduler"/>（惯例同
    /// <c>GameplayAssembly.TurnScheduler</c> 属性判断记录）。<c>combat.left</c>（个体脱战，非死亡）
    /// 补齐后同样触发移除（见 <see cref="OnCombatLeft"/>）：离散模式下收到某单位的
    /// <c>combat.left</c> 时，先把该单位从当前轮的行动顺序里 <c>RemoveParticipant</c>，再判断整场
    /// 战斗是否清空——脱战单位不应继续占据行动顺序被轮到（此前版本只在死亡时移除，脱战不移除，
    /// 属已知限制，本次任务补齐，与 <see cref="OnUnitDied"/> 走同一套移除路径）。
    /// </para>
    /// </summary>
    public sealed class TimeModelSwitch
    {
        private readonly Core.Foundation.SimLoop.TurnScheduler _scheduler;
        private readonly ISimClockHost _clockHost;
        private readonly IAppStateHost _appState;
        private readonly IWorldSim _world;
        private readonly IUnitAccess _units;
        private readonly ISpatialQuery _spatial;
        private readonly IEventBus _bus;
        private readonly TimeModelSwitchOptions _options;
        private readonly Func<Id, IReadOnlyList<Id>>? _participantsResolver;
        private readonly IFactionMatrix? _factions;
        private readonly CombatOptions? _combatOptions;

        /// <summary><see cref="_combatOptions"/> 非空时，构造期缓存的"连续模式基准值"——切入离散
        /// 模式时按 <see cref="TimeModelDefinition.SecondsPerTurn"/> 从这个基准换算轮数（见
        /// <see cref="SwitchToDiscrete"/>），切回连续模式时精确恢复到这个基准（见
        /// <see cref="SwitchToContinuous"/>），不从"当前已经被换算过的值"再次换算，避免反复切换
        /// 造成的舍入漂移。</summary>
        private readonly double _continuousLeaveCombatDelay;

        private readonly HashSet<Id> _activeCombatants = new HashSet<Id>();

        public TimeModelDefinition ExplorationModel { get; }

        /// <summary>可能为空：某些游戏数据集可能只声明探索时间模型（理论上 04 要求"探索与战斗各自
        /// 登记一条"，但本类型不因数据缺失而抛异常——缺失战斗时间模型时恒不切换，行为等价于
        /// "战斗沿用连续模式"，见 <see cref="OnCombatEntered"/>）。</summary>
        public TimeModelDefinition? CombatModel { get; }

        public TimeModelMode CurrentMode { get; private set; } = TimeModelMode.Continuous;

        public TimeModelSwitch(
            Core.Foundation.SimLoop.TurnScheduler scheduler,
            ISimClockHost clockHost,
            IAppStateHost appState,
            IWorldSim world,
            IUnitAccess units,
            ISpatialQuery spatial,
            IEventBus bus,
            IDataRegistryView registry,
            TimeModelSwitchOptions? options = null,
            Func<Id, IReadOnlyList<Id>>? participantsResolver = null,
            IFactionMatrix? factions = null,
            CombatOptions? combatOptions = null)
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
            _factions = factions;
            _combatOptions = combatOptions;
            _continuousLeaveCombatDelay = combatOptions?.LeaveCombatDelay ?? 0.0;

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

        /// <summary>遭遇定义的 <c>combat_mode_override</c>（见 08_玩法层_掉落任务对话关卡.md 第 4.1
        /// 节字段表）可以在某次具体遭遇开始前调用 <see cref="SetPendingOverride"/> 临时覆盖
        /// <see cref="CombatModel"/> 的判断——本类型不直接依赖 <c>core/gameplay/encounter</c>（避免
        /// assembly 内部循环依赖同一目录下另一模块的具体宿主类型），调用方（<c>GameplayAssembly</c>，
        /// 持有 <c>Encounter</c>）订阅 <c>encounter.started</c> 时经 <c>EncounterHost.
        /// TryGetModeOverride</c> 读取 <c>EncounterDefinition.CombatModeOverride</c>/
        /// <c>InitiativeOverride</c> 并据此调用本方法（收边任务补齐：此前只落地读取字段，从无调用点，
        /// 见该方法与 <c>EncounterHost.TryGetModeOverride</c> 判断记录）。
        /// <para>
        /// 判断记录（<c>bool?</c> 而非收边任务之前的 <c>bool</c>）：08 字段表 <c>combat_mode_override</c>
        /// 取值为 <c>Optional&lt;continuous|discrete&gt;</c>——旧实现用 <c>bool
        /// PendingOverrideIsDiscrete</c> 表示"是否强制离散"，把"显式覆盖为 continuous"与"完全未
        /// 声明覆盖"两种语义都折叠成 <c>false</c>，导致覆盖只能单向生效（能强制切到离散，不能在
        /// 场景默认已是 discrete 时强制维持连续）——<see cref="OnCombatEntered"/> 原判断
        /// <c>PendingOverrideIsDiscrete || CombatModel.Mode == Discrete</c> 一旦默认模型是
        /// discrete，覆盖字段传 <c>"continuous"</c> 也无法真正生效。改为三态 <c>bool?</c>
        /// （<c>true</c>=强制离散，<c>false</c>=强制连续，<c>null</c>=未声明覆盖、按
        /// <see cref="CombatModel"/> 默认判断）后，覆盖完全取代默认判断（不再是"或"关系），双向都能
        /// 生效。</para>
        /// </summary>
        public bool? PendingCombatModeOverride { get; private set; }

        /// <summary>收边任务补齐：<see cref="SetPendingOverride"/> 消费 <c>initiative_override.policy</c>
        /// 后暂存的先攻策略覆盖值，供 <see cref="SwitchToDiscrete"/> 使用；一次性（切入离散模式时
        /// 连同 <see cref="PendingCombatModeOverride"/> 一并清空，见该方法）。</summary>
        private InitiativePolicy? _pendingInitiativePolicyOverride;

        /// <summary>收边任务补齐：<c>initiative_override.params.initiative_stat</c>（一次性，同上）。</summary>
        private Id? _pendingInitiativeStatOverride;

        /// <summary>收边任务补齐：<c>initiative_override.params.action_points_per_turn</c>（一次性，同上）。</summary>
        private double? _pendingActionPointsPerTurnOverride;

        /// <summary>收边任务补齐：离散模式期间生效的先攻属性覆盖（非一次性——从
        /// <see cref="SwitchToDiscrete"/> 落定到 <see cref="SwitchToContinuous"/> 清空，覆盖整场离散
        /// 战斗，见 <see cref="EffectiveInitiativeStat"/>）。</summary>
        private Id? _activeInitiativeStatOverride;

        /// <summary>供 <c>GameplayAssembly</c> 的 <c>InitiativeStatProvider</c> 闭包读取（收边任务
        /// 补齐，替换此前恒读 <c>CombatModel?.InitiativeStat</c> 的实现）：离散模式下若当次遭遇声明了
        /// <c>initiative_override.params.initiative_stat</c> 则取该值，否则回退到
        /// <see cref="CombatModel"/> 的默认先攻属性；不在离散覆盖期间（未开始遭遇、遭遇未声明覆盖、
        /// 或已切回连续）恒等于 <c>CombatModel?.InitiativeStat</c>。</summary>
        public Id? EffectiveInitiativeStat => _activeInitiativeStatOverride ?? CombatModel?.InitiativeStat;

        /// <summary>
        /// 供 <c>GameplayAssembly</c> 在收到 <c>encounter.started</c>/<c>encounter.won</c>/
        /// <c>encounter.lost</c> 时调用（见 <see cref="PendingCombatModeOverride"/> 判断记录）。
        /// <paramref name="combatModeOverride"/> 为 <c>null</c>（或非 <c>"discrete"</c>/<c>"continuous"</c>
        /// 的未知取值，按防御性处理为"未声明覆盖"，不抛异常——同 <c>EncounterHost.ApplyPhase</c>
        /// "无效键不报错"的一贯宽容惯例）时清空覆盖，恢复"按 <see cref="CombatModel"/> 默认判断"，
        /// 供遭遇结束时调用（08 判断记录"遭遇结束清除"：避免一次未真正引发战斗的遭遇残留的覆盖
        /// 意外影响下一次不相关的战斗）。<paramref name="initiativeOverride"/> 原样对应 08 第 4.1 节
        /// <c>initiative_override</c> 字段结构 <c>{policy, params}</c>；<c>policy</c> 未知取值抛
        /// <see cref="InvalidOperationException"/>（与 <c>combat_mode_override</c> 不同——
        /// <c>initiative_override.policy</c> 直接决定 <see cref="Core.Foundation.SimLoop.
        /// TurnScheduler.Configure"/> 的策略参数，取值错误若静默忽略会让战斗用一个数据作者没有
        /// 预期的策略运行，风险高于"覆盖字段被忽略"）。
        /// </summary>
        public void SetPendingOverride(string? combatModeOverride, JsonObject? initiativeOverride = null)
        {
            PendingCombatModeOverride = combatModeOverride switch
            {
                "discrete" => true,
                "continuous" => false,
                _ => (bool?)null,
            };

            _pendingInitiativePolicyOverride = null;
            _pendingInitiativeStatOverride = null;
            _pendingActionPointsPerTurnOverride = null;

            if (initiativeOverride == null)
            {
                return;
            }

            if (initiativeOverride.TryGetValue("policy", out var policyRaw) && policyRaw is JsonString policyStr)
            {
                _pendingInitiativePolicyOverride = policyStr.Value switch
                {
                    "initiative_stat" => InitiativePolicy.InitiativeStat,
                    "action_points" => InitiativePolicy.ActionPoints,
                    "fixed_order" => InitiativePolicy.FixedOrder,
                    _ => throw new InvalidOperationException(
                        $"encounter.def.initiative_override.policy 未知取值 \"{policyStr.Value}\"（见 08 第 4.1 节字段表，只允许 initiative_stat/action_points/fixed_order）"),
                };
            }

            if (initiativeOverride.TryGetValue("params", out var paramsRaw) && paramsRaw is JsonObject paramsObj)
            {
                if (paramsObj.TryGetValue("initiative_stat", out var statRaw) && statRaw is JsonString statStr
                    && Id.TryParse(statStr.Value, out var statId))
                {
                    _pendingInitiativeStatOverride = statId;
                }

                if (paramsObj.TryGetValue("action_points_per_turn", out var apRaw) && apRaw is JsonNumber apNum)
                {
                    _pendingActionPointsPerTurnOverride = apNum.Value;
                }
            }
        }

        private void OnCombatEntered(CombatEnteredEvent evt)
        {
            _activeCombatants.Add(evt.UnitId);

            if (CurrentMode == TimeModelMode.Discrete)
            {
                // 中途加入（见类型判断记录"中途加入/离场"）：整场战斗已经在离散模式下进行，
                // 新进战单位实时插入当前轮尚未行动的序列，不重新调用 BeginCombat。
                _scheduler.AddParticipant(evt.UnitId);
                return;
            }

            if (_activeCombatants.Count != 1)
            {
                return;
            }

            var wantsDiscrete = PendingCombatModeOverride ?? (CombatModel != null && CombatModel.Mode == TimeModelMode.Discrete);
            if (!wantsDiscrete || CombatModel == null)
            {
                return;
            }

            SwitchToDiscrete(evt.UnitId, evt.HostileId);
        }

        private void OnCombatLeft(CombatLeftEvent evt)
        {
            _activeCombatants.Remove(evt.UnitId);

            if (CurrentMode == TimeModelMode.Discrete)
            {
                // 个体脱战移除（见类型判断记录"中途加入/离场"补齐段落）：脱战单位不是死亡，但同样
                // 不应继续占据当前轮的行动顺序被轮到——与 OnUnitDied 走同一套 RemoveParticipant
                // 路径；若脱战单位恰好是整场战斗最后一个活跃单位，紧随其后的 SwitchToContinuousIfNoneActive
                // 会整体 EndCombat 清空顺序，这里的单个移除仍然安全（TurnScheduler.RemoveParticipant
                // 对不在当前轮次里的单位是幂等的，见其类型判断记录）。
                _scheduler.RemoveParticipant(evt.UnitId);
            }

            SwitchToContinuousIfNoneActive();
        }

        private void OnUnitDied(UnitDiedEvent evt)
        {
            _activeCombatants.Remove(evt.UnitId);

            if (CurrentMode == TimeModelMode.Discrete)
            {
                // 死亡移除（见类型判断记录"中途加入/离场"）：死亡单位不应继续占据本轮行动顺序。
                _scheduler.RemoveParticipant(evt.UnitId);
            }

            SwitchToContinuousIfNoneActive();
        }

        private void SwitchToContinuousIfNoneActive()
        {
            if (CurrentMode == TimeModelMode.Discrete && _activeCombatants.Count == 0)
            {
                SwitchToContinuous();
            }
        }

        private void SwitchToDiscrete(Id triggerUnit, Id? hostileId)
        {
            var participants = ResolveParticipants(triggerUnit, hostileId);
            if (participants.Count == 0)
            {
                return;
            }

            // 时间单位换算（03 第 3.3 节步骤 2）：以秒计的剩余时长按 seconds_per_turn 折算为
            // 剩余回合数，即乘以 1/seconds_per_turn。
            RescaleTimers(1.0 / CombatModel!.SecondsPerTurn);

            // 06 第 4.5 节脱战判定时长同样"以数据集声明的时间单位计"（见 CombatOptions.LeaveCombatDelay
            // 注释）：离散模式下换算为等效轮数——向上取整（Math.Ceiling）而不是四舍五入/截断，
            // 保证离散模式下的脱战判定不会比换算前的连续模式秒数更快触发（任务书拍板：按
            // seconds_per_turn 换算为轮数），至少 1 轮（避免 seconds_per_turn 远大于原延迟时换出 0，
            // 0 轮意味着"当轮立即脱战"，与"一段时间内未产生新战斗事件"的定义矛盾）。
            if (_combatOptions != null)
            {
                var rounds = Math.Ceiling(_continuousLeaveCombatDelay / CombatModel.SecondsPerTurn);
                _combatOptions.LeaveCombatDelay = Math.Max(1.0, rounds);
            }

            // ADR-0013 补齐：action_points_per_turn 此前恒不从 found.time_model 读取（本方法此前
            // 传空字典，TurnScheduler.Configure 因此总是回退到默认值 1.0，见该方法参数注释），现
            // 按任务书"每回合行动点来自 found.time_model 参数"改为透传 CombatModel.ActionPointsPerTurn。
            //
            // 收边任务补齐（initiative_override 消费）：_activeInitiativeStatOverride 必须先于
            // BeginCombat 落定——BeginCombat 内部按先攻值排序参与者（OrderParticipants），
            // GameplayAssembly.InitiativeStatProvider 闭包读取的正是 EffectiveInitiativeStat，
            // 排序发生前必须已经能看到本次遭遇的覆盖值。effectivePolicy/effectiveActionPointsPerTurn
            // 同理：遭遇声明了 initiative_override 时取覆盖值，否则回退 CombatModel 默认。
            _activeInitiativeStatOverride = _pendingInitiativeStatOverride;
            var effectivePolicy = _pendingInitiativePolicyOverride ?? CombatModel.InitiativePolicy;
            var effectiveActionPointsPerTurn = _pendingActionPointsPerTurnOverride ?? CombatModel.ActionPointsPerTurn;

            var parameters = new Dictionary<string, object>
            {
                ["action_points_per_turn"] = effectiveActionPointsPerTurn,
            };
            _scheduler.Configure(effectivePolicy, parameters);
            _scheduler.BeginCombat(participants);

            _clockHost.Mode = TimeModelMode.Discrete;
            CurrentMode = TimeModelMode.Discrete;
            PendingCombatModeOverride = null;
            _pendingInitiativePolicyOverride = null;
            _pendingInitiativeStatOverride = null;
            _pendingActionPointsPerTurnOverride = null;

            _appState.PushSubState(SubStateId.Combat);
        }

        private void SwitchToContinuous()
        {
            _scheduler.EndCombat();
            _clockHost.Mode = TimeModelMode.Continuous;

            // 反向换算：剩余回合数按 seconds_per_turn 折算回秒。
            RescaleTimers(CombatModel!.SecondsPerTurn);

            // 脱战判定时长精确恢复到构造期缓存的连续模式基准值（不用"当前值 × seconds_per_turn"
            // 反向换算——SwitchToDiscrete 用了 Math.Ceiling，反向换算无法精确复原，见
            // _continuousLeaveCombatDelay 字段注释）。
            if (_combatOptions != null)
            {
                _combatOptions.LeaveCombatDelay = _continuousLeaveCombatDelay;
            }

            CurrentMode = TimeModelMode.Continuous;

            // 收边任务补齐：离散覆盖只在本场离散战斗期间生效（见 EffectiveInitiativeStat 判断记录），
            // 切回连续模式后清空，避免残留到下一场战斗（下一场若重新进入离散模式，会由那时的
            // SetPendingOverride/SwitchToDiscrete 重新落定）。
            _activeInitiativeStatOverride = null;

            PopSubStatesThroughCombat();
        }

        /// <summary>
        /// 根治修复（W5c，PlayMode 门实测暴露的既有缺陷，与本轮"离散回放门零事件步骤"根治任务同批
        /// 发现）：<see cref="SwitchToContinuous"/> 此前只在 <c>_appState.CurrentSubState == SubStateId.Combat</c>
        /// 严格相等时才弹出一次——但 <see cref="SwitchToDiscrete"/> 压入的只是 <c>Combat</c> 本身，
        /// <c>GameplayAssembly.Advance</c> 在离散步驱动期间还会把 <c>AwaitingInput</c>/<c>PlayingBack</c>
        /// 两个自定义子状态嵌套压在 <c>Combat</c> 之上（见该方法判断记录）；<c>combat.left</c> 完全
        /// 可能恰好在"轮到玩家等待输入"（栈顶是 <c>AwaitingInput</c>，不是 <c>Combat</c>）那一刻触发
        /// ——此时严格相等检查失手，<c>Combat</c>（连同其上的 <c>AwaitingInput</c>）永久残留在子状态
        /// 栈里，<see cref="Presentation.Ui.HudViewModel.CanEndTurn"/> 之类依赖
        /// <c>AppState.CurrentSubState</c> 的下游会持续读到"仍在等待输入"的假状态（复现：
        /// <c>adapters/unity/.../Tests/Runtime/DiscreteCombatTests.cs</c> 的
        /// <c>EnterDiscreteCombat_AwaitingInput_EndTurn_AiActs_RoundAdvances_ExitsBackToContinuous</c>，
        /// 断言"切回连续模式后结束回合按钮应重新隐藏"失败——该失败在本类型未改动前的既有代码上就能
        /// 复现，与本轮回放门修复无因果关系，是同一批 PlayMode 门实测顺带发现的独立缺陷，一并根治）。
        /// 改为循环弹出：先弹掉 <c>Combat</c> 之上的任意嵌套子状态，直到栈顶回到 <c>Combat</c> 本身
        /// 再弹出它——不需要本类型知道 <c>AwaitingInput</c>/<c>PlayingBack</c> 具体是哪两个
        /// <see cref="SubStateId"/>（它们由 <c>GameplayAssembly</c> 动态注册，本类型不持有其值，也
        /// 没必要持有：只要不是 <c>Combat</c> 本身，一律弹出）。<see cref="IAppStateHost.PopSubState"/>
        /// 在栈底 <c>Explore</c> 不可弹出时返回 <c>false</c>（见该方法契约），本方法据此防御性退出，
        /// 不会死循环——即便 <c>Combat</c> 因某种异常原因不在栈内（理论上不会发生：<see cref="CurrentMode"/>
        /// 只在 <see cref="SwitchToDiscrete"/> 压入 <c>Combat</c> 之后才置为 <see cref="TimeModelMode.Discrete"/>，
        /// 本方法只在 <see cref="CurrentMode"/> 为 <c>Discrete</c> 时被调用，见 <see cref="OnCombatLeft"/>/
        /// <see cref="SwitchToContinuousIfNoneActive"/>）。
        /// </summary>
        private void PopSubStatesThroughCombat()
        {
            while (_appState.CurrentSubState.HasValue && !_appState.CurrentSubState.Value.Equals(SubStateId.Combat))
            {
                if (!_appState.PopSubState())
                {
                    return;
                }
            }

            if (_appState.CurrentSubState.HasValue && _appState.CurrentSubState.Value.Equals(SubStateId.Combat))
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

        /// <summary>加固任务（05 §3.6 碰撞层落地）：排除 <see cref="CollisionLayers.TriggerOnly"/>
        /// 标签——区域触发实体现在会登记进空间索引（见 <c>CarriersAssembly.DefaultSpatialSyncKinds</c>
        /// 判断记录）。下方循环已有 <c>_units.Exists(id)</c> 防御性检查，即便不排除也不会因触发体
        /// 混入而把它错当"战斗参与者"，这里排除是为了避免无谓地把触发体拉进候选集合。</summary>
        private static readonly QueryFilter ExcludeTriggerOnly =
            new QueryFilter(excludedTags: new[] { CollisionLayers.TriggerOnly });

        private IReadOnlyList<Id> ResolveParticipants(Id triggerUnit, Id? hostileId)
        {
            if (_participantsResolver != null)
            {
                return _participantsResolver(triggerUnit);
            }

            var nearby = _spatial.QueryRadius(_units.GetPosition(triggerUnit), _options.ParticipantSearchRadius, ExcludeTriggerOnly);
            var set = new SortedSet<Id>();
            set.Add(triggerUnit);

            // 收边任务补齐（06 第 8 节 combat.entered.hostileId 勘误）：本次触发进战事件携带的
            // "首个敌对目标" 是精确值（来自 Resolver 结算时的 sourceId/targetId 配对，见
            // CombatEnteredEvent.HostileId 判断记录），优先直接纳入参与者集合，不必经过下面
            // 半径查询 + 阵营近似这条本就是"猜"的路径——半径 + 阵营近似从"唯一判定来源"降级为
            // "回退/补充来源"，继续执行是为了发现 hostileId 之外的其余参与者（如 AoE 命中的
            // 多个敌人），不代表 hostileId 的可靠性依赖它。
            if (hostileId.HasValue && _units.Exists(hostileId.Value) && _units.IsAlive(hostileId.Value))
            {
                set.Add(hostileId.Value);
            }

            var triggerFaction = _factions != null ? _units.GetFaction(triggerUnit) : default;

            for (var i = 0; i < nearby.Count; i++)
            {
                var id = nearby[i];
                if (!_units.Exists(id) || !_units.IsAlive(id))
                {
                    continue;
                }

                // 阵营过滤（见类型判断记录"参与者解析"）：_factions 非空时只收"与触发单位同阵营
                // （友军）或互为敌对"的单位，排除半径内的中立/无关旁观者；_factions 为空时保留
                // 本任务之前的行为（半径内全部存活单位）。
                if (_factions != null)
                {
                    var otherFaction = _units.GetFaction(id);
                    if (!otherFaction.Equals(triggerFaction) && !_factions.IsHostile(triggerFaction, otherFaction))
                    {
                        continue;
                    }
                }

                set.Add(id);
            }

            var result = new List<Id>(set.Count);
            foreach (var id in set)
            {
                result.Add(id);
            }
            return result;
        }

        /// <summary>
        /// H4 补齐（判断记录：多根合并时"后声明覆盖先声明"，不是"先声明生效"）：
        /// <see cref="IDataRegistry.LoadAll(System.Collections.Generic.IReadOnlyList{IDataSource})"/>
        /// 按传入的根顺序合并同名表记录（<c>DataRegistry.LoadMergedTable</c> 按 <c>partials</c>
        /// 顺序 <c>mergedRecords.Add</c>，见该方法源码），主键不同（同一 <c>scope</c> 允许多条不同
        /// id 的记录并存，schema 只约束 <c>id</c> 唯一，不约束 <c>scope</c> 唯一）时不会触发跨根主键
        /// 冲突阻断。本方法因此需要自己决定"同一 scope 出现多条记录时该用哪一条"——按"后加载的根
        /// 优先"选取最后一条匹配记录（遍历不提前返回，持续覆盖），而不是历史实现"遇到第一条匹配
        /// 就返回"：后者会让"框架级/示例级数据根 + 一个专门叠加测试用 discrete 行的测试根"这种
        /// 常见的多根叠加测试手法失效（示例数据根声明 <c>continuous</c>、测试根想叠加声明
        /// <c>discrete</c> 时，测试根若排在示例根之后加载却因为"先来先得"被忽略）——
        /// <c>adapters/unity/.../Tests/Runtime/TestData/found/found.time_model.json</c> 与
        /// <c>Tests/Runtime/DiscreteCombatTests.cs</c>/<c>SharedBootstrapDiscreteTests.cs</c> 正是
        /// 这个手法的实际使用方，"后来者覆盖"是这些测试原本设计时假定的语义。
        /// </summary>
        private static TimeModelDefinition? LoadModel(IDataRegistryView registry, string scope)
        {
            var records = registry.GetAll("found.time_model");
            TimeModelDefinition? result = null;
            for (var i = 0; i < records.Count; i++)
            {
                var def = TimeModelDefinition.FromRecord(records[i]);
                if (def.Scope == scope)
                {
                    result = def;
                }
            }
            return result;
        }
    }
}
