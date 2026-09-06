using System;
using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Gameplay.Death
{
    /// <summary>
    /// <see cref="IDeathPolicyHost"/> 唯一实现（见 06_规则层_属性技能战斗AI.md 第 4.6 节、
    /// DECISIONS 拍板 3"死亡复活三策略执行主体落在 L4 新模块 core/gameplay/death"）：订阅
    /// <c>unit.died</c>（仅 <see cref="EntityKinds.Player"/>，AI/生物死亡不触发任何本模块行为，
    /// 复活/掉落/尸体清理等由各自既有模块处理），按 <see cref="EffectivePolicy"/> 执行：
    /// <list type="bullet">
    /// <item><c>respawn_point</c>：取死亡事件快照的 <c>MapId</c> 对应地图的默认复活点（经
    /// <see cref="DeathPolicyOptions.ResolveDefaultSpawn"/>），延迟
    /// <see cref="DeathPolicyOptions.RespawnDelayTicks"/> 个 tick 后调用
    /// <see cref="DeathPolicyOptions.ReviveUnit"/> 复活并发布
    /// <see cref="UnitRespawnedEvent"/>。</item>
    /// <item><c>reload_save</c>：经 <see cref="DeathPolicyOptions.ReloadSave"/>（未装配时退化为直接
    /// 调用 <c>ISaveSystem.Load(AutosaveSlotId)</c>）读档——读档本身会按 10 号文档固定顺序恢复全部
    /// 已注册段（含玩家位置/存活状态/生命值——见 <c>Core.Gameplay.Assembly.PlayerVitalsPersistable</c>、
    /// 目标地图与当前地图不同时的场景切换——见 <c>Core.Gameplay.Assembly.GameplayAssembly.RestoreFromSlot</c>），
    /// 不需要本模块额外处理；读档失败（如尚无可用自动存档）时回退到 <c>respawn_point</c> 策略同一
    /// 套默认复活点逻辑，见 <see cref="OnUnitDied"/> 判断记录。</item>
    /// <item><c>permadeath</c>：删除"当前槽"（<see cref="DeathPolicyOptions.CurrentSlotIdProvider"/>
    /// 未提供时回退 <see cref="DeathPolicyOptions.AutosaveSlotId"/>）后请求
    /// <see cref="IAppStateHost.RequestTransition"/> 切到 <see cref="AppState.MainMenu"/>。</item>
    /// </list>
    /// 本类型同时是一个 <see cref="ITickPhaseHandler"/>（挂在 <see cref="TickPhase.TriggerEvaluation"/>，
    /// 惯例同 <c>Core.Gameplay.Loot.LootExpiryTickHandler</c> 等既有 L4 tick 处理器），用于推进
    /// <c>respawn_point</c> 策略的延迟复活队列——不区分 <see cref="SimStepKind.Continuous"/>/
    /// <see cref="SimStepKind.Discrete"/>，两种时间模型下死亡都可能发生，复活延迟按"tick 数"计，
    /// 不按秒数（<c>Discrete</c> 步没有秒数概念）。
    /// </summary>
    public sealed class DeathPolicyHost : IDeathPolicyHost, ITickPhaseHandler
    {
        private readonly IEventBus _bus;
        private readonly IWorldSim _world;
        private readonly ISaveSystem _saveSystem;
        private readonly IAppStateHost _appState;
        private readonly DeathPolicyOptions _options;
        private readonly IDeathPolicyDiagnostics _diagnostics;
        private readonly List<PendingRespawn> _pending = new List<PendingRespawn>();

        public RespawnPolicy EffectivePolicy { get; }

        /// <param name="defaultCombatDeathPolicy">
        /// <c>CombatOptions.DeathPolicy</c>（见 <see cref="DeathPolicyOptions.Policy"/> 判断记录：
        /// 本模块不重复声明独立默认值，未在 <paramref name="options"/> 显式覆盖时取这里传入的值）。
        /// </param>
        public DeathPolicyHost(
            IEventBus bus,
            IWorldSim world,
            ISaveSystem saveSystem,
            IAppStateHost appState,
            RespawnPolicy defaultCombatDeathPolicy,
            DeathPolicyOptions? options = null,
            IDeathPolicyDiagnostics? diagnostics = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _saveSystem = saveSystem ?? throw new ArgumentNullException(nameof(saveSystem));
            _appState = appState ?? throw new ArgumentNullException(nameof(appState));
            _options = options ?? new DeathPolicyOptions();
            _diagnostics = diagnostics ?? new InMemoryDeathPolicyDiagnostics();

            if (_options.RespawnDelayTicks < 1)
            {
                throw new ArgumentException(
                    "DeathPolicyOptions.RespawnDelayTicks 必须 >= 1（死亡结算发生在本 tick 的 " +
                    "EventDispatch 阶段，晚于 TriggerEvaluation，最早只能下一次 tick 复活）",
                    nameof(options));
            }

            EffectivePolicy = _options.Policy ?? defaultCombatDeathPolicy;

            _bus.Subscribe<UnitDiedEvent>(RulesEventKeys.UnitDied, OnUnitDied);
        }

        private void OnUnitDied(UnitDiedEvent evt)
        {
            // 仅玩家单位：AI/生物死亡不触发死亡复活策略（AI 死亡的复活/移除由各自既有模块——
            // Spawn 的 respawn_policy、Loot 的死亡掉落——处理，与本模块职责不重叠）。
            if (_world.GetEntity(evt.UnitId)?.Kind != EntityKinds.Player)
            {
                return;
            }

            switch (EffectivePolicy)
            {
                case RespawnPolicy.RespawnPoint:
                    EnqueueRespawn(evt);
                    break;

                case RespawnPolicy.ReloadSave:
                    // 外部审核阻塞项 2 收口（见 DeathPolicyOptions.ReloadSaveDelegate 判断记录）：
                    // 未装配 DeathPolicyOptions.ReloadSave（如未经 GameplayAssembly 的单元测试场景）
                    // 时退化为此前的行为——直接调用注入的 ISaveSystem.Load，不切场景、不额外处理，
                    // 不破坏任何既有测试。
                    var loadResult = _options.ReloadSave != null
                        ? _options.ReloadSave(_options.AutosaveSlotId)
                        : _saveSystem.Load(_options.AutosaveSlotId);

                    // 根治修复：此前只把 LoadStatus.Loaded 视为成功，LoadStatus.LoadedFromBackup
                    // （正式文件损坏时自动回退备份，见该枚举值注释）被误判为失败——读到备份同样是
                    // 一次完整、可用的读档结果，10 号文档第 4 节明确把它列为"读档"的两种成功形态
                    // 之一（同 ShellHost.LoadGame/GameplayAssembly.RestoreFromSlot 的既有判断口径）。
                    if (loadResult.Status != LoadStatus.Loaded && loadResult.Status != LoadStatus.LoadedFromBackup)
                    {
                        // 外部审核阻塞项 2 收口：此前读档失败（典型场景——游戏刚开始、存档点/任务
                        // 完成触发点都还没来得及写过一次自动存档，reload_save 读到的槽 id 根本不
                        // 存在，见 LoadStatus.NotFound）只记一条诊断，玩家从此永久停留在"已死亡"
                        // 状态（Unit.Alive 无人再置回 true），没有任何兜底。现回退到 respawn_point
                        // 策略同一套"死亡地图默认复活点"逻辑（见 EnqueueRespawn），保证 reload_save
                        // 在没有可用存档时仍然收敛到一个玩家能继续游玩的状态，而不是卡死。
                        _diagnostics.Error(
                            $"DeathPolicyHost（reload_save）：读取存档槽 \"{_options.AutosaveSlotId}\" 失败，状态 {loadResult.Status}，" +
                            "回退到 respawn_point 策略在死亡地图的默认复活点复活");
                        EnqueueRespawn(evt);
                    }

                    break;

                case RespawnPolicy.Permadeath:
                    var slotId = _options.CurrentSlotIdProvider?.Invoke() ?? _options.AutosaveSlotId;
                    _saveSystem.DeleteSlot(slotId);
                    if (!_appState.RequestTransition(AppState.MainMenu))
                    {
                        _diagnostics.Warn(
                            $"DeathPolicyHost（permadeath）：删除存档槽 \"{slotId}\" 后请求切回主菜单被拒绝（当前状态机不允许该转移）");
                    }

                    break;

                default:
                    _diagnostics.Error($"DeathPolicyHost：未知的死亡复活策略 \"{EffectivePolicy}\"");
                    break;
            }
        }

        private void EnqueueRespawn(UnitDiedEvent evt)
        {
            if (evt.MapId == null)
            {
                _diagnostics.Error(
                    $"DeathPolicyHost（respawn_point）：单位 \"{evt.UnitId}\" 的 unit.died 事件未携带 MapId 快照，无法解析复活点");
                return;
            }

            if (_options.ResolveDefaultSpawn == null)
            {
                _diagnostics.Error(
                    $"DeathPolicyHost（respawn_point）：未装配 ResolveDefaultSpawn，单位 \"{evt.UnitId}\" 无法复活");
                return;
            }

            var resolved = _options.ResolveDefaultSpawn(evt.MapId.Value);
            if (resolved == null)
            {
                _diagnostics.Error(
                    $"DeathPolicyHost（respawn_point）：地图 \"{evt.MapId}\" 未能解析出默认复活点，单位 \"{evt.UnitId}\" 无法复活");
                return;
            }

            _pending.Add(new PendingRespawn(evt.UnitId, resolved.Value.Position, _options.RespawnDelayTicks));
        }

        /// <summary>推进延迟复活队列，见类型注释"不区分 Continuous/Discrete"。</summary>
        public void Execute(SimStep step, IWorldSim world)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                var pending = _pending[i];
                pending.TicksRemaining--;
                if (pending.TicksRemaining > 0)
                {
                    _pending[i] = pending;
                    continue;
                }

                _pending.RemoveAt(i);

                if (_options.ReviveUnit == null)
                {
                    _diagnostics.Error(
                        $"DeathPolicyHost（respawn_point）：未装配 ReviveUnit，单位 \"{pending.UnitId}\" 复活倒计时已到但无法执行");
                    continue;
                }

                _options.ReviveUnit(pending.UnitId, pending.Position, _options.RespawnHealthFraction);
                _bus.PublishImmediate(new UnitRespawnedEvent(pending.UnitId, RespawnPolicy.RespawnPoint));
            }
        }

        private struct PendingRespawn
        {
            public readonly Id UnitId;
            public readonly Vec2 Position;
            public int TicksRemaining;

            public PendingRespawn(Id unitId, Vec2 position, int ticksRemaining)
            {
                UnitId = unitId;
                Position = position;
                TicksRemaining = ticksRemaining;
            }
        }
    }
}
