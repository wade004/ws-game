using System;
using System.Collections.Generic;
using Core.Carriers.Unit;
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
    /// 目标地图与当前地图不同时的场景切换——见 <c>Core.Gameplay.Assembly.GameplayAssembly.RestoreFromSlot</c>）。
    /// C12 根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：读档成功后本模块
    /// 仍需额外发布一次 <see cref="UnitRespawnedEvent"/>（见 <see cref="OnUnitDied"/> 判断记录）——
    /// 同图读档（不触发场景切换）不会销毁重建任何实体/View，表现层依赖这个事件才能清理"死亡"这一
    /// 终态锁，否则逻辑层早已恢复存活、表现层却还停在死亡姿态；读档失败（如尚无可用自动存档）时
    /// 回退到 <c>respawn_point</c> 策略同一套默认复活点逻辑，见 <see cref="OnUnitDied"/> 判断
    /// 记录。</item>
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
    public sealed class DeathPolicyHost : IDeathPolicyHost, ITickPhaseHandler, IDisposable
    {
        private readonly IEventBus _bus;
        private readonly IWorldSim _world;
        private readonly ISaveSystem _saveSystem;
        private readonly IAppStateHost _appState;
        private readonly DeathPolicyOptions _options;
        private readonly IDeathPolicyDiagnostics _diagnostics;
        private readonly List<PendingRespawn> _pending = new List<PendingRespawn>();
        private readonly SubscriptionHandle _unitDiedSubscription;
        private readonly SubscriptionHandle _entityDestroyedSubscription;

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

            _unitDiedSubscription = _bus.Subscribe<UnitDiedEvent>(RulesEventKeys.UnitDied, OnUnitDied);

            // C11-CLEANUP 根治新增（architecture/落地计划/消费方反馈-2026-09-11-读档空间索引与复活
            // 生命周期.md 第 3 项）：订阅 entity.destroyed，任一 pending 复活记录对应的单位被销毁
            // （含 IWorldSim.ClearAll——见该方法判断记录"按 EntityId 序数顺序 Enqueue 全部实体各一次
            // entity.destroyed"）时立即摘除对应记录，见 OnEntityDestroyedForPending 判断记录。本订阅
            // 只覆盖"事件已经真正派发"之后的时机；ClearAll 与派发之间的窗口（下一次 tick 的
            // TriggerEvaluation 阶段早于 EventDispatch 阶段，见 Execute 判断记录）由 Execute 自身的
            // 存在性校验兜底，两者合起来才是完整修复，见 Execute 判断记录。
            _entityDestroyedSubscription = _bus.Subscribe<EntityDestroyedEvent>(
                SimEventKeys.EntityDestroyed, OnEntityDestroyedForPending);
        }

        /// <summary>
        /// C11-CLEANUP 根治新增：某个单位被销毁时，若它在延迟复活队列（<see cref="_pending"/>）里有
        /// 待执行的记录，立即摘除——避免 <see cref="Execute"/> 在倒计时到期后仍尝试对一个已经不存在
        /// 的单位调用 <see cref="DeathPolicyOptions.ReviveUnit"/>（真实实现
        /// <c>Core.Carriers.Unit.WorldUnitAccess.Revive</c> 会抛 <see
        /// cref="InvalidOperationException"/>，见消费方反馈第 3 项 B 条）。倒序遍历 + RemoveAt：
        /// 理论上同一单位不会在 <see cref="_pending"/> 里出现两次（<see cref="OnUnitDied"/> 只在
        /// <see cref="RespawnPolicy.RespawnPoint"/> 策略下才 Enqueue，且玩家单位一次只会死亡一次、
        /// 复活一次），这里按"允许多条"的宽松写法防御性遍历，不假设至多一条。
        /// </summary>
        private void OnEntityDestroyedForPending(EntityDestroyedEvent evt)
        {
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].UnitId.Equals(evt.EntityId))
                {
                    _pending.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// C11-PENDING-LOAD 根治新增（消费方反馈第 3 项 A 条）：清空延迟复活队列，不触碰任何其它
        /// 状态、不发布任何事件。供读档（<c>Core.Gameplay.Assembly.GameplayAssembly</c> 经其
        /// <c>IDerivedStateRebuilder.BeforeLoad</c>——早于任何存档段真正 Load，见该接口类型判断
        /// 记录——在真正开始恢复存档状态之前调用一次，见该类型判断记录）场景使用：玩家死亡后留有
        /// 尚未执行的延迟复活记录，此时若发生一次读档（同图或跨图），读档本身已经按存档内容把玩家
        /// 存活状态/生命值/位置等全部恢复到位（<c>player.vitals</c> 段，见 <c>PlayerVitalsPersistable</c>
        /// 判断记录），若不清空这条旧的延迟复活记录，它会在读档完成后的某次 <see cref="Execute"/>
        /// 到期时用死亡地图的默认复活点/复活血量把刚恢复好的读档状态原地覆盖掉（真实探针复现：
        /// 读档后下一 tick 位置被覆盖为默认复活点、HP 被覆盖为满血）。
        /// <para>
        /// 判断记录（为什么不判断"存档里有没有 pending"）：本模块当前不持久化延迟复活队列本身（未
        /// 找到既有段/字段），存档语义因此简化为"若存档时刻已经处于死亡未复活状态，读档后玩家按
        /// 存档的 <c>alive=false</c>/HP 值原样停留在死亡态，不会凭空复活"——<see
        /// cref="PlayerVitalsPersistable"/> 已经完整覆盖这一语义（见该类型 <c>Load</c>），本方法只
        /// 需要确保读档前遗留的旧队列不会在读档之后继续生效，不需要额外读一份"存档里的 pending"。
        /// </para>
        /// </summary>
        public void ClearPending()
        {
            _pending.Clear();
        }

        /// <summary>
        /// C11-CLEANUP 根治新增：宿主释放时取消两个事件订阅并清空延迟复活队列——幂等（<see
        /// cref="SubscriptionHandle.Dispose"/> 本身幂等，<see cref="List{T}.Clear"/> 对空表安全），
        /// 多次调用/未装配任何 pending 记录时都是安全空操作。
        /// </summary>
        public void Dispose()
        {
            _unitDiedSubscription.Dispose();
            _entityDestroyedSubscription.Dispose();
            _pending.Clear();
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
                    else
                    {
                        // C12 根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：
                        // 读档成功（含 LoadedFromBackup）分支此前只调用 Load 本身——读档确实已经把
                        // 存活状态/生命值/位置等全部已注册段恢复到位（见 PlayerVitalsPersistable、
                        // GameplayAssembly.RestoreFromSlot 判断记录"同图不切场景时……直接把全部已
                        // 注册段写回长期存活的 PlayerUnit/Unit 运行期对象，不需要任何重新进图步骤
                        // 即可生效"），但从未发布 UnitRespawnedEvent——表现层的动画状态机把
                        // UnitDiedEvent 当成优先级最高的终态锁，只认明确的复活信号才会解锁（见 08
                        // 第 10 节本次同款判断记录），没有这个信号，同图读档（不触发场景切换、不
                        // 销毁重建 View）的玩家会一直卡在死亡动画姿态，即便已经可以正常行动。
                        // 跨地图读档（触发场景切换）不受影响——ClearAll 销毁重建过程本身就会让旧
                        // View 连同其动画状态机记账一起被清理，新 View 从默认待机姿态开始。
                        //
                        // 判断记录（Enqueue 而不是 PublishImmediate）：本方法正在处理的是
                        // UnitDiedEvent 自己的订阅回调，仍处于那一次 unit.died PublishImmediate
                        // 派发的调用栈内——若在这里同步 PublishImmediate 一个 UnitRespawnedEvent，
                        // 其它同样订阅了 unit.died、但订阅顺序晚于本处理器的下游（如表现层的动画
                        // 状态机）还没轮到处理这次死亡，随后才会把状态置为死亡，会把这里刚发的复活
                        // 信号原地覆盖掉。改用 Enqueue：入队等当前这一批 unit.died 全部订阅方都处理
                        // 完毕之后，由外层下一次 DispatchPending 才真正派发，保证复活信号严格晚于
                        // 本次死亡结算的全部下游处理，不会被后到的死亡处理覆盖。
                        _bus.Enqueue(new UnitRespawnedEvent(evt.UnitId, RespawnPolicy.ReloadSave));
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

                // C11-CLEANUP 根治新增（消费方反馈第 3 项 B 条）：执行前校验目标单位仍然存在且是
                // Unit——覆盖 OnEntityDestroyedForPending 订阅来不及生效的窗口（IWorldSim.ClearAll
                // 只是把 entity.destroyed 排入待处理队列，不立即派发，见该方法判断记录；本
                // Execute 挂在 TickPhase.TriggerEvaluation，早于 TickPhase.EventDispatch，ClearAll
                // 之后的下一个 tick 里，本方法先于对应的 entity.destroyed 被派发执行——见类型顶部
                // 判断记录"AiDecision 阶段早于 EventDispatch"同款时序）。不存在时静默丢弃这条记录、
                // 只记一条诊断，不抛异常——同 <see cref="NotifyCombatEvent"/>（CombatHost 同款场景）
                // 判断记录"进战通知对不存在单位静默跳过"的既有惯例。
                if (!(_world.GetEntity(pending.UnitId) is Unit))
                {
                    _diagnostics.Warn(
                        $"DeathPolicyHost（respawn_point）：单位 \"{pending.UnitId}\" 复活倒计时已到，" +
                        "但该单位已不存在于世界模拟中（可能已被 WorldSim.ClearAll 或其它方式销毁），丢弃这条待复活记录，不执行复活");
                    continue;
                }

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
