using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Assembly;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Carriers.Gobj;
using Core.Carriers.Item;
using Core.Carriers.Summon;
using Core.Carriers.Unit;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.HookRegistry;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Core.Gameplay.Achievement;
using Core.Gameplay.AreaTrigger;
using Core.Gameplay.Common;
using Core.Gameplay.Dialog;
using Core.Gameplay.Difficulty;
using Core.Gameplay.Economy;
using Core.Gameplay.Encounter;
using Core.Gameplay.Loot;
using Core.Gameplay.Quest;
using Core.Gameplay.Spawn;
using Core.Gameplay.WorldState;
using Core.Numbers.Progression;
using Core.Numbers.StatBlock;
using Core.Rules.Ai;
using Core.Rules.Combat;
using Core.Rules.Common;
using Core.Rules.ExprHost;
using Core.Rules.Skill;
using Core.Rules.Targeting;

// 判断记录：Core.Gameplay.WorldState 命名空间与其内的 WorldState 类同名，裸写 "WorldState" 在本文件
// （namespace Core.Gameplay.Assembly，与 Core.Gameplay.WorldState 属同一父命名空间 Core.Gameplay 下的
// 平级子命名空间）里会被编译器优先解析成命名空间本身而报 CS0118，惯例同
// core/rules/assembly/RulesAssembly.cs 顶部对 Progression/Archetype 同名委托的处理，用别名区分。
using WorldStateHost = Core.Gameplay.WorldState.WorldState;

namespace Core.Gameplay.Assembly
{
    /// <summary>
    /// L4（<c>core/gameplay</c> 十模块：<c>common</c>/<c>world_state</c>/<c>loot</c>/<c>economy</c>/
    /// <c>quest</c>/<c>dialog</c>/<c>encounter</c>/<c>difficulty</c>/<c>achievement</c>/
    /// <c>area_trigger</c>/<c>spawn</c>）在 <see cref="CarriersAssembly"/>（L0～L3）之上的组装根（阶段
    /// 3 集成收尾"事项一"，<see cref="CarriersAssembly"/> 在 L4 层的延续）。装配顺序、回调接线矩阵、
    /// tick 阶段挂载表、存档段顺序见 <c>core/gameplay/assembly/README.md</c>。
    /// </summary>
    public sealed class GameplayAssembly
    {
        private static readonly Id ThreatTablePlaceholderId = new Id("system.gameplay_threat_placeholder");

        /// <summary>缺口 16 拍板默认值：<c>GobjOptions.SaveRequester</c> 未显式指定
        /// <c>autosaveSlotId</c> 时写入的存档槽。</summary>
        private static readonly Id DefaultAutosaveSlotId = new Id("slot.autosave");

        /// <summary>
        /// 加固任务（05 §3.6 碰撞层落地）：<c>spatialSyncKinds</c> 构造参数未显式提供时使用的默认值——
        /// 以 <see cref="CarriersAssembly.DefaultSpatialSyncKinds"/> 为底，把其中
        /// <c>EntityKinds.AreaTrigger</c> 条目的固定半径换成按 <see cref="AreaTriggerEntity.BoundingRadius"/>
        /// 取值的 <see cref="EntitySpatialSyncHost.KindConfig.RadiusResolver"/>（判断记录见构造函数
        /// 调用点、<c>CarriersAssembly.DefaultSpatialSyncKinds</c>、<c>AreaTriggerEntity.BoundingRadius</c>
        /// 三处）。其余条目（creature/player）原样保留。
        /// </summary>
        private static IReadOnlyDictionary<string, EntitySpatialSyncHost.KindConfig> BuildDefaultSpatialSyncKinds()
        {
            var kinds = new Dictionary<string, EntitySpatialSyncHost.KindConfig>(
                CarriersAssembly.DefaultSpatialSyncKinds, StringComparer.Ordinal);

            if (kinds.TryGetValue(EntityKinds.AreaTrigger, out var existing))
            {
                kinds[EntityKinds.AreaTrigger] = new EntitySpatialSyncHost.KindConfig(
                    existing.Radius,
                    existing.Tags,
                    radiusResolver: entity => entity is AreaTriggerEntity trigger ? trigger.BoundingRadius : existing.Radius);
            }

            return kinds;
        }

        public CarriersAssembly Carriers { get; }

        public IAppStateHost AppState { get; }

        public IHookRegistry Hooks { get; }

        public WorldStateHost WorldState { get; }

        public LootHost Loot { get; }

        public EconomyHost Economy { get; }

        public QuestHost Quest { get; }

        public DialogHost Dialog { get; }

        public EncounterHost Encounter { get; }

        public LevelHost Level { get; }

        public DifficultyHost Difficulty { get; }

        public AchievementHost Achievement { get; }

        public AreaTriggerHost AreaTrigger { get; }

        public SpawnHost Spawn { get; }

        /// <summary>W2 收边补齐（DECISIONS 拍板 3）：死亡复活三策略执行主体，见该类型注释。只在
        /// 装配了 <see cref="AppState"/>/<see cref="Foundation.SaveSystem.ISaveSystem"/>（构造函数
        /// 必填参数，恒非空）时构造——本属性恒非空，不像 <see cref="TurnScheduler"/> 那样依赖可选的
        /// <c>clockHost</c>。</summary>
        public Core.Gameplay.Death.DeathPolicyHost Death { get; }

        public RewardDispatcher Reward { get; }

        /// <summary>
        /// 缺口 16（ISaveSystem 归属调整）：本装配根持有并公开唯一一份 <see cref="ISaveSystem"/>
        /// 实例（构造参数传入，调用方用 <see cref="Core.Foundation.EngineAdapter.IFileSystem"/>
        /// 构造后传入，本类不自行 <c>new</c> 一份）——此前 <c>ISaveSystem</c> 由
        /// <c>Presentation.Assembly.PresentationAssembly</c> 自行构造，<see cref="RegisterPersistables"/>
        /// 要求调用方另行传入一份"同一实例"，两处容易各建各的、悄悄产生两份存档系统。归属调整后
        /// <c>PresentationAssembly</c> 改为直接读取本属性，不再自行构造（见该类型判断记录）。
        /// </summary>
        public ISaveSystem SaveSystem { get; }

        /// <summary>
        /// P1-03 收口新增：构造函数传入的 <see cref="IRngHost"/> 实例——此前本类只把它转手传给
        /// <see cref="RulesAssembly"/>/<see cref="CarriersAssembly"/> 等下游装配根，自身没有持有，
        /// 导致 <see cref="RegisterPersistables"/> 拿不到它来注册 <see cref="RngStreamsPersistable"/>
        /// （见该方法判断记录）。全部下游装配根共享同一份实例，本属性只是额外暴露一个引用，不改变
        /// 现有装配拓扑。
        /// </summary>
        public IRngHost Rng { get; }

        /// <summary>供 <c>world</c>/<c>quest</c>/<c>player</c> 三个分组接线（extraGroups）的
        /// <see cref="IExprHostFactory"/>——与 <see cref="CarriersAssembly.Rules"/> 内部使用的那份
        /// （见该类型判断记录，无 extraGroups）是两个不同实例；L4 全部宿主构造期一律用这一份。</summary>
        public IExprHostFactory ExprHostFactory { get; }

        public Func<Id> PlayerUnitProvider { get; }

        /// <summary>ADR-0013 离散时间模型：只在构造函数传入 <c>clockHost</c> 时非空（见该构造参数
        /// 判断记录）。未传入时全部三个属性保持 null，<see cref="Advance"/> 抛异常，其余行为与本
        /// 任务之前完全一致（不破坏任何既有调用方——多数既有测试直接持有自己的
        /// <see cref="ISimClockHost"/> 并调用其 <c>Advance</c>，压根不经过本类，见
        /// <c>TimeModelSwitch</c> 判断记录 1）。</summary>
        public TimeModelSwitch? TimeModelSwitch { get; }

        /// <summary>暴露具体类型（而不是 <see cref="ITurnScheduler"/>）：<see cref="Advance"/> 需要
        /// <see cref="Core.Foundation.SimLoop.TurnScheduler.NotifyStepConsumed"/>、
        /// <see cref="Core.Foundation.SimLoop.TurnScheduler.CurrentTurnIndex"/>/
        /// <see cref="Core.Foundation.SimLoop.TurnScheduler.RoundIndex"/> 等不属于
        /// <see cref="ITurnScheduler"/> 契约的便利成员（惯例同 <c>WorldSim.DiagnosticsWarnings</c>
        /// 判断记录：不为这些编排细节新增 03 文档之外的接口原语）。</summary>
        public Core.Foundation.SimLoop.TurnScheduler? TurnScheduler { get; }

        public IPacingPolicy? Pacing { get; }

        /// <summary>
        /// W2b 收边补齐（插值系数暴露）：<see cref="Advance"/> 每次调用后的表现插值系数——连续模式下
        /// 等于本次调用 <see cref="ISimClockHost.Advance"/> 返回的 alpha（<c>[0,1)</c>，见该接口方法
        /// 注释"累积器 / 步长"）；离散模式下恒为 <c>1.0</c>（离散步之间没有"上一步/这一步"的位置差可
        /// 插值，见 <see cref="Core.Foundation.SimLoop.SimClockHost"/> 离散分支判断记录"调用方不应
        /// 像连续模式那样用它做位置线性插值"——本类型的 <see cref="Advance"/> 离散分支不调用
        /// <c>ISimClockHost.Advance</c>，因此不会拿到那份"纯视觉相位"alpha，直接固定给 1.0，表示
        /// "按当前已提交状态渲染，不做帧间插值"）；未在构造函数传入 <c>clockHost</c>（<see cref="Advance"/>
        /// 恒抛异常，从未被成功调用过）时同样为 <c>1.0</c>（默认值，见字段初始化）。
        /// 供表现层（Unity 侧 <c>ViewBinder.SyncAll(alpha)</c>）在每帧调用完 <see cref="Advance"/> 后
        /// 读取——本属性只读，只在 <see cref="Advance"/> 内部更新。
        /// </summary>
        public double InterpolationAlpha { get; private set; } = 1.0;

        public SubStateId AwaitingInputSubState { get; }

        public SubStateId PlayingBackSubState { get; }

        private readonly IEventBus _bus;
        private readonly IWorldSim _world;
        private readonly ISimClockHost? _clockHost;

        /// <summary>外部审核阻塞项 2 收口新增：<see cref="RestoreFromSlot"/> 需要它判断"读档后是否
        /// 需要切场景"，见该方法判断记录。构造函数参数本就可选（<c>sceneRouter</c> 未传入时多个既有
        /// 用途——如 <c>AreaTriggerOptions.SceneRouter</c>——已经按"未装配场景路由"静默退化，本字段
        /// 延续同一约定：为 null 时 <see cref="RestoreFromSlot"/> 只读档、不尝试切场景。
        /// <para>
        /// 判断记录（非 <c>readonly</c>，配 <see cref="AttachSceneRouter"/> 回填）：生产装配的真实
        /// <c>Core.Foundation.SceneRouter.SceneRouter</c> 构造需要 <see cref="AppState"/>/
        /// <see cref="Hooks"/> 两个属性（见其构造函数），而它们要到本类型构造函数内部第 10 步才
        /// 产生——调用方（<c>Adapter.Unity.Bootstrap.GameFoundationBootstrap</c>/
        /// <c>FrameworkResidentHost</c>/<c>games/_template/GameBootstrap</c>）因此一律在
        /// <c>new GameplayAssembly(...)</c> 完成之后才能构造真正的 <c>SceneRouter</c>，构造函数的
        /// <c>sceneRouter</c> 参数在这三处生产装配里实际传入的都是 <c>null</c>——本字段延续本类型
        /// 一贯的"先占位、后回填"惯例（同 <see cref="SetPendingPlaybackProbe"/> 判断记录），供这三处
        /// 在真正构造好 <c>SceneRouter</c> 后调用 <see cref="AttachSceneRouter"/> 补上引用。
        /// </para>
        /// </summary>
        private ISceneRouter? _sceneRouter;

        /// <summary>N14 根治新增：构造期第 16 步已经构造过一份 <see cref="TeleportTargetResolver"/>
        /// （供 <c>GobjOptions.TeleportResolver</c>/<c>DeathPolicyOptions.ResolveDefaultSpawn</c>
        /// 复用），此前只是方法局部变量、<see cref="TeleportUnit"/> 拿不到；提升为字段后
        /// <see cref="TeleportUnit"/> 才能复用同一份解析逻辑，不必另起一套。</summary>
        private TeleportTargetResolver _teleportTargetResolver = null!;

        /// <summary>W2 收边补齐（SkillOptions.IsDiscreteStep 判断记录）：仅在 <see cref="Advance"/>
        /// 内部处理某一个 Discrete 步（<c>_world.Tick(step.Value)</c> 调用期间）为 true，供
        /// <c>resolvedSkillOptions.IsDiscreteStep</c> 闭包读取——技能施放（含离散意图路由、AI
        /// tick）在离散模式下都发生在这次 <c>Tick</c> 调用范围内，因此本字段能正确圈定"当前是否
        /// 正在处理离散步"这一范围，而不是只看 <see cref="ISimClockHost.Mode"/>（那只说明"装配的
        /// 是哪种时间模型"，不说明"此刻是否真的在推进一个离散步"）。</summary>
        private bool _isProcessingDiscreteStep;
        private readonly Dictionary<Id, double> _combatStartTimes = new Dictionary<Id, double>(EqualityComparer<Id>.Default);

        public GameplayAssembly(
            IEventBus bus,
            IDataRegistryView registry,
            IRngHost rng,
            IWorldSim world,
            ISpatialQuery spatial,
            ISaveSystem saveSystem,
            Func<Id> playerUnitProvider,
            Id playerFactionId,
            INavigation2D? navigation = null,
            IReadOnlyDictionary<string, EntitySpatialSyncHost.KindConfig>? spatialSyncKinds = null,
            ISceneRouter? sceneRouter = null,
            StatHostOptions? statOptions = null,
            CombatOptions? combatOptions = null,
            SkillOptions? skillOptions = null,
            TargetingOptions? targetingOptions = null,
            AiOptions? aiOptions = null,
            InventoryOptions? inventoryOptions = null,
            ItemOptions? itemOptions = null,
            CreatureOptions? creatureOptions = null,
            SummonOptions? summonOptions = null,
            GobjOptions? gobjOptions = null,
            MovementOptions? movementOptions = null,
            WorldStateOptions? worldStateOptions = null,
            LootOptions? lootOptions = null,
            EconomyOptions? economyOptions = null,
            QuestOptions? questOptions = null,
            DifficultyOptions? difficultyOptions = null,
            AchievementOptions? achievementOptions = null,
            AreaTriggerOptions? areaTriggerOptions = null,
            SpawnOptions? spawnOptions = null,
            Id? autosaveSlotId = null,
            Func<string>? autosaveTimestampProvider = null,
            ISimClockHost? clockHost = null,
            IPacingPolicy? pacingPolicy = null,
            TimeModelSwitchOptions? timeModelSwitchOptions = null,
            Func<Id, IReadOnlyList<Id>>? combatParticipantsResolver = null,
            Core.Gameplay.Death.DeathPolicyOptions? deathPolicyOptions = null)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (spatial == null) throw new ArgumentNullException(nameof(spatial));
            SaveSystem = saveSystem ?? throw new ArgumentNullException(nameof(saveSystem));
            Rng = rng;
            PlayerUnitProvider = playerUnitProvider ?? throw new ArgumentNullException(nameof(playerUnitProvider));

            _bus = bus;
            _world = world;
            _sceneRouter = sceneRouter;

            // 判断记录（缺口 16，自动存档槽 id/时间戳来源）：提前到构造函数最前面解析（原在第 14 步
            // GobjOptions.SaveRequester 接线处才算，现第 12 步 DialogHost 构造 saveRequested 参数也
            // 要用同一份，见 G1 遗留恢复"DialogHost.saveRequested 接 GameplayAssembly.SaveSystem"）。
            // autosaveSlotId 默认 "slot.autosave"（任务书拍板）；autosaveTimestampProvider 默认取
            // 墙钟时间——本字段只写入存档 meta 段的 updated_at/created_at（10 第 2.1 节），不参与模拟
            // 状态，不属于"无系统时间"确定性铁律约束的范围（惯例同
            // Presentation.Assembly.PresentationAssemblyOptions.TimestampProvider 的同款默认值）。
            var resolvedAutosaveSlotId = autosaveSlotId ?? DefaultAutosaveSlotId;
            var resolvedAutosaveTimestampProvider = autosaveTimestampProvider ?? (() => DateTime.UtcNow.ToString("o"));

            // 本地函数（而非某个具体委托类型的变量）：DialogHost 的 SaveRequestedCallback 与
            // GobjOptions 的 SaveRequesterDelegate 是两个独立声明、签名相同（Id unitId）的委托类型，
            // 本地函数的方法组可以分别隐式转换到两者，不需要为"同一份存档逻辑"重复写两份 lambda。
            //
            // 判断记录（加固任务，自动存档开关门控）：此前本地函数无条件调用 saveSystem.Save，未经
            // ISaveSystem.ShouldAutoSave(AutoSaveTrigger) 判断（10 第 6 节"基础架构提供触发点机制，
            // 不强制具体游戏必须启用哪几个"——本类是"接线"方，理应先查策略）。存档点物件交互
            // （GobjOptions.SaveRequester）与对话中的 save 动作（DialogHost.saveRequested）在 10
            // 第 6 节自动存档表里都属于"存档点"（SavePoint）触发点，两者共用同一份门控判断。
            // 判断为 false 时不写盘、直接返回——本类没有统一的诊断汇聚点（见本文件其余全部
            // "diagnostics: null"传参与对应判断记录，各 L4 宿主各自独立持有 I*Diagnostics 接口，
            // GameplayAssembly 本身未声明任何诊断出口），因此这里不新增一个只为本处使用的诊断
            // 通道，静默返回（同 15 步 TeleportTargetResolver.onFailure 判断记录"本装配根不重复
            // 接一份诊断出口"的一贯取舍）。手动存档（菜单，调用方直接调 SaveSystem.Save，不经过
            // 本函数）不受此门控约束。
            void RequestAutosave(Id unitId)
            {
                if (!saveSystem.ShouldAutoSave(AutoSaveTrigger.SavePoint))
                {
                    return;
                }

                saveSystem.Save(new SaveRequest(resolvedAutosaveSlotId, resolvedAutosaveTimestampProvider()));
            }

            // ---------------------------------------------------------
            // 1) WorldState：只依赖 IEventBus，不依赖任何 L0～L3 宿主，可以在 CarriersAssembly 之前
            //    先造好——直接作为 IWorldFlags 注入 CarriersAssembly（依赖倒置回接第一处）。
            // ---------------------------------------------------------
            WorldState = new WorldStateHost(bus, worldStateOptions);

            // ---------------------------------------------------------
            // 2) 两个延迟绑定代理（惯例同 core/rules/assembly.RulesAssembly.DeferredAuraQuery、
            //    core/rules/assembly.DeferredEffectExtension）：
            //    - deferredLootRoller：CarriersAssembly 构造期需要一个 ILootRoller 传给
            //      GameObjectHost（chest/gather_node 掉落），但真正的 LootHost 需要
            //      CarriersAssembly.Units/Inventory 才能造——先占位，第 5 步 LootHost 造好后 Bind。
            //    - deferredQuestGroup / deferredPlayerGroup：ExprHostFactory（第 4 步）需要的
            //      extraGroups 里，world 分组可以立即绑定（WorldState 已就绪），quest/player 分组
            //      依赖尚未构造的 QuestHost/EconomyHost，同样先占位。
            // ---------------------------------------------------------
            var deferredLootRoller = new DeferredLootRoller();
            var deferredQuestGroup = new DeferredExprGroupProvider();
            var deferredPlayerGroup = new DeferredExprGroupProvider();

            // ---------------------------------------------------------
            // 3) CarriersAssembly（L0～L3）：worldFlags 直接注入 WorldState；lootRoller 注入延迟代理；
            //    extraSchemas 传完整组合 schema——L2 内部的 skill/proc/target/ai 校验与运行期解析都
            //    认识 world/quest/player/event 四个游戏层分组（即便 CarriersAssembly.Rules.ExprHostFactory
            //    本身没有对应的 extraGroups 可用，见下方判断记录"契约缺口"）。
            // ---------------------------------------------------------
            // 判断记录：resolvedGobjOptions 必须在这里就地 new 出来（不能让 CarriersAssembly 在
            // gobjOptions 为 null 时自己 new 一份）——第 16 步要在 GameObjectHost 构造完成之后回填
            // DialogOpener/TeleportResolver/SaveRequester/QuestActionDispatcher 四个 L4 回调，
            // GameObjectHost 内部持有的是构造期传入的这个实例的引用（同一份，不拷贝字段），必须是
            // 同一个对象才能"回填"生效。
            var resolvedGobjOptions = gobjOptions ?? new GobjOptions();

            // 判断记录（加固任务：05 §3.6 碰撞层落地，area_trigger 半径按形状外接半径精确计算）：
            // spatialSyncKinds 为 null（调用方未显式覆盖）时，不直接用 CarriersAssembly.
            // DefaultSpatialSyncKinds（那份固定近似半径 0.5，见其判断记录——L3 不能依赖 L4 的
            // AreaTriggerEntity 类型），而是在这里（L4，已经引用 Core.Gameplay.AreaTrigger）用
            // BuildDefaultSpatialSyncKinds 换上一份按 AreaTriggerEntity.BoundingRadius 取值的
            // RadiusResolver；调用方显式传入 spatialSyncKinds 时尊重调用方的选择，不做任何叠加。
            var resolvedSpatialSyncKinds = spatialSyncKinds ?? BuildDefaultSpatialSyncKinds();

            // W2 收边补齐（A3 审计 #4/#5/#7，DECISIONS 拍板 4）：resolvedSkillOptions 必须在这里
            // 就地 new 出来（同 resolvedGobjOptions 判断记录）——CarriersAssembly 构造期就要把它
            // 转给 RulesAssembly/CastPipeline 持有同一份引用，本方法随后（第 10.5 步）才能回填
            // TryConsumeActionPoints/IsDiscreteStep 两个委托，必须是同一个对象才能"回填"生效。
            //
            // scheduler 提前在这里声明（而不是等到下面 3.5 步）：discreteTurnIndexProvider/
            // discreteRoundIndexProvider/discreteCurrentActorProvider 三个闭包需要在
            // CarriersAssembly 构造期（本步）就传给它——CarriersAssembly 转发给
            // RulesAssembly，后者用它们构造内部感知 SkillHost 的 RulesExprHostFactory（供
            // AiHost 的 ai.rotation 条件求值 time.is_my_turn 等 key，见该类型），但 scheduler
            // 本身要等 Carriers 构造完成之后（3.5 步，需要 Carriers.Rules.Stats/Summons/Skill）
            // 才能真正 new 出来——闭包捕获局部变量 scheduler（而不是值），3.5 步赋值后，此前已经
            // 传出去的闭包在真正被调用（求值 Expr，发生在装配完成之后）时会读到赋值后的实例，
            // 惯例同 deferredLootRoller/deferredQuestGroup 等"先占位、后绑定"的延迟绑定手法。
            // 未传入 clockHost 时 scheduler 恒为 null，三个闭包分别恒返回 0/0/null，与未装配离散
            // 模式时的既有默认行为完全一致。
            Core.Foundation.SimLoop.TurnScheduler? scheduler = null;
            var resolvedSkillOptions = skillOptions ?? new SkillOptions();

            Carriers = new CarriersAssembly(
                bus, registry, rng, world, spatial, navigation, resolvedSpatialSyncKinds,
                worldFlags: WorldState, lootRoller: deferredLootRoller,
                statOptions: statOptions, combatOptions: combatOptions, skillOptions: resolvedSkillOptions,
                targetingOptions: targetingOptions, aiOptions: aiOptions, inventoryOptions: inventoryOptions,
                itemOptions: itemOptions, creatureOptions: creatureOptions, summonOptions: summonOptions,
                gobjOptions: resolvedGobjOptions, movementOptions: movementOptions,
                extraSchemas: new IExprSchema[] { GameplaySchemaCatalog.FullExprSchema },
                discreteTurnIndexProvider: () => scheduler?.CurrentTurnIndex ?? 0,
                discreteRoundIndexProvider: () => scheduler?.RoundIndex ?? 0,
                discreteCurrentActorProvider: () => scheduler?.GetCurrentActor());

            // ---------------------------------------------------------
            // 3.5) ADR-0013 离散时间模型：TurnScheduler 提前在这里构造（而不是等到第 10 步
            //     AppState 就绪后）——第 4 步 ExprHostFactory 需要把 time.turn_index/round_index/
            //     is_my_turn 三个 Expr key 接到同一个调度器实例上（见 RulesExprHostFactory 判断
            //     记录），先有 TurnScheduler 才能把访问器传给它。TimeModelSwitch（需要 AppState）
            //     仍然留到第 10.5 步再构造，两者共用这里造好的同一个 scheduler 实例。只在调用方
            //     传入 clockHost 时构造（见该构造参数判断记录）。
            // ---------------------------------------------------------
            Core.Gameplay.Assembly.TimeModelSwitch? timeModelSwitchRef = null;
            if (clockHost != null)
            {
                double InitiativeStatProvider(Id unitId)
                {
                    // 收边任务补齐：改读 TimeModelSwitch.EffectiveInitiativeStat（离散模式下若当次
                    // 遭遇声明了 initiative_override.params.initiative_stat 则取覆盖值，否则回退
                    // CombatModel 默认——见该属性判断记录），此前恒读 CombatModel?.InitiativeStat，
                    // 遭遇级先攻属性覆盖无法生效。
                    var statId = timeModelSwitchRef?.EffectiveInitiativeStat;
                    return statId.HasValue ? Carriers.Rules.Stats.GetStat(unitId, statId.Value) : 0.0;
                }

                // 收边任务补齐（缺口 (c) PlayerCanControl，判断记录"离散模式下召唤物是否独立行动者"）：
                // SummonOptions.PlayerCanControl 开启且 unitId 确实是玩家当前拥有的召唤物时，该召唤物
                // 在离散模式下与玩家本人同等对待——轮到它时 TurnScheduler.NextStep 会等待外部经
                // WorldSim.SubmitIntent 提交意图（awaiting_input），不会被 AiTickHandler 自动接管；
                // PlayerCanControl 为 false（默认）时 IsControllableByOwner 恒返回 false，行为与
                // 补齐之前完全一致。Carriers 已在第 3 步（CarriersAssembly 构造）就绪，可安全闭包
                // 引用（同 IsBusyContinuing 判断记录）。
                bool IsPlayerActor(Id unitId) =>
                    unitId.Equals(PlayerUnitProvider()) || Carriers.Summons.IsControllableByOwner(unitId, PlayerUnitProvider());

                // H4 补齐（读条跨回合，见 TurnScheduler._isBusyContinuing 字段判断记录）：
                // Carriers.Rules.Skill 已在第 3 步（CarriersAssembly 构造）就绪，此处可以安全闭包
                // 引用——真正被调用（NextStep）时战斗早已开始，Carriers 更是构造完毕已久。
                bool IsBusyContinuing(Id unitId) => Carriers.Rules.Skill.IsCasting(unitId);

                scheduler = new Core.Foundation.SimLoop.TurnScheduler(
                    world, InitiativeStatProvider, IsPlayerActor, bus, IsBusyContinuing);
            }

            // ---------------------------------------------------------
            // 4) ExprHostFactory（带 extraGroups 的第二份工厂，供 L4 全部宿主使用，见该属性判断记录）。
            //    combatStartTimeProvider：GameplayAssembly 独立订阅 combat.entered 自行追踪（不触碰
            //    RulesAssembly 私有字段，见 TrackCombatStartTimes）。turnIndexProvider/
            //    roundIndexProvider/currentActorProvider：ADR-0013，未传入 clockHost 时
            //    scheduler 为 null，三个委托整体不传（RulesExprHostFactory 未注入时保留旧占位
            //    0/0/false 返回值，见该类型判断记录）。
            // ---------------------------------------------------------
            var extraGroups = new Dictionary<string, IExprGroupProvider>(StringComparer.Ordinal)
            {
                [ExprGroups.World] = new WorldExprGroupProvider(WorldState),
                [ExprGroups.Quest] = deferredQuestGroup,
                [ExprGroups.Player] = deferredPlayerGroup,
            };

            TrackCombatStartTimes();

            ExprHostFactory = new RulesExprHostFactory(
                Carriers.Units, Carriers.Rules.Stats, Carriers.Rules.Powers, Carriers.Rules.Skill.AuraQuery,
                Carriers.Rules.Combat, Carriers.Rules.Combat.GetThreatTable(ThreatTablePlaceholderId), spatial,
                Carriers.Rules.Factions, () => Carriers.Rules.SimTime, GetCombatStartTime,
                extraGroups: extraGroups, skillHost: Carriers.Rules.Skill, diagnostics: null,
                extraSchemas: new IExprSchema[] { GameplaySchemaCatalog.FullExprSchema },
                turnIndexProvider: scheduler != null ? () => scheduler.CurrentTurnIndex : (Func<int>?)null,
                roundIndexProvider: scheduler != null ? () => scheduler.RoundIndex : (Func<int>?)null,
                currentActorProvider: scheduler != null ? () => scheduler.GetCurrentActor() : (Func<Id?>?)null);

            // ---------------------------------------------------------
            // 5) DifficultyHost：先于 LootHost/CreatureDeathLootListener 构造——后者的
            //    lootMultiplierProvider 直接读 Difficulty.LootMultiplier（见判断记录：放在赋值
            //    之后，避免闭包捕获一个"声明时尚未赋值"的非空属性触发可空引用分析警告）。
            // ---------------------------------------------------------
            Difficulty = new DifficultyHost(
                registry, bus, Carriers.Rules.Skill.EffectSink, Carriers.Rules.Factions, Carriers.Units,
                difficultyOptions ?? new DifficultyOptions(playerFactionId));

            // ---------------------------------------------------------
            // 6) LootHost（+ CreatureDeathLootListener）：造好后立即回填 deferredLootRoller。
            // ---------------------------------------------------------
            Loot = new LootHost(
                registry, rng, bus, world, Carriers.Units, Carriers.Inventory, ExprHostFactory,
                () => Carriers.Rules.SimTime, lootOptions, diagnostics: null,
                conditionSchema: GameplaySchemaCatalog.FullExprSchema);
            deferredLootRoller.Bind(Loot);

            _ = new CreatureDeathLootListener(
                bus, Loot, Carriers.Creatures, Carriers.Units, world,
                lootMultiplierProvider: () => Difficulty.LootMultiplier);

            // ---------------------------------------------------------
            // 7) RewardDispatcher：currencyGranter 用局部变量延迟闭包接到第 8 步才构造出来的
            //    EconomyHost（惯例同 RulesAssembly 第 1 步 progression 变量的写法）。
            // ---------------------------------------------------------
            EconomyHost economyForGrant = null!;
            CurrencyGranter currencyGranter = (unitId, currencyId, amount, sourceId) =>
                economyForGrant.Add(unitId, currencyId, amount, sourceId);
            // 接线跟进（Y1/Y2 侧 RC-05"装备授予技能缺来源计数"根治，见
            // core/carriers/item/contracts/SkillGranter.cs：委托签名从 (unitId, skillId, learn) 三参
            // 改为 (unitId, skillId, sourceId, learn) 四参，供 SkillHost.LearnSkill(Id,Id,Id)/
            // ForgetSkill(Id,Id,Id) 的按来源引用计数）：这里把奖励发放/剧情动作等触发的技能授予也
            // 按来源登记（sourceId 由 RewardDispatcher.GrantSkills 透传自 Grant 收到的奖励来源 id），
            // 而不是退化成不计来源的哨兵——不然这条路径授予的技能会与真正"永久学习"的技能混在一起，
            // 卸下另一件不相关的装备时可能被误撤销。
            SkillGranter skillGranter = (unitId, skillId, sourceId, learn) =>
            {
                if (learn) Carriers.Rules.Skill.LearnSkill(unitId, skillId, sourceId);
                else Carriers.Rules.Skill.ForgetSkill(unitId, skillId, sourceId);
            };

            Reward = new RewardDispatcher(
                Carriers.Inventory, Carriers.Rules.Progression, WorldState, skillGranter, currencyGranter,
                talentPointGranter: null, diagnostics: null);

            // ---------------------------------------------------------
            // 8) EconomyHost，随后回填第 7 步的闭包变量。
            // ---------------------------------------------------------
            Economy = new EconomyHost(
                registry, bus, Carriers.Inventory, ExprHostFactory, economyOptions, diagnostics: null,
                conditionSchema: GameplaySchemaCatalog.FullExprSchema);
            economyForGrant = Economy;

            deferredPlayerGroup.Bind(new ChainedExprGroupProvider(new IExprGroupProvider[]
            {
                new PlayerExprGroupProvider(Carriers.Inventory, Carriers.Rules.Progression, PlayerUnitProvider),
                new PlayerCurrencyExprGroupProvider(Economy, PlayerUnitProvider),
            }));

            // ---------------------------------------------------------
            // 9) QuestHost，随后回填 deferredQuestGroup。gobjTemplateResolver 直接查 IWorldSim 实体的
            //    TemplateId（组装层现成可用，不需要额外查询通道）。
            // ---------------------------------------------------------
            var questDefinitions = registry.GetAll(Core.Gameplay.Quest.QuestSchemas.Def.Name)
                .Select(r => QuestDefinition.FromRecord(r, GameplaySchemaCatalog.FullExprSchema))
                .ToList();
            Quest = new QuestHost(
                questDefinitions, bus, ExprHostFactory, Reward, Carriers.Inventory, Carriers.Units,
                questOptions, ownerResolver: null,
                gobjTemplateResolver: id => world.GetEntity(id)?.TemplateId, dayProvider: null,
                exprDiagnostics: null);
            deferredQuestGroup.Bind(new QuestExprGroupProvider(Quest, PlayerUnitProvider));

            // ---------------------------------------------------------
            // 10) AppStateHost / HookRegistry（DialogHost 的两个必填依赖，本类自行装配——两者均属
            //     L0，构造只需要 IEventBus）。ADR-0013：额外登记两个自定义子状态 AwaitingInput/
            //     PlayingBack（见 03 第 2 节 Combat 内子态"awaiting_input"/"playing_back"），
            //     只允许在 Combat 与二者之间互相转移——本模块 app_lifecycle 的既有判断记录明确
            //     指出这两个附加子态"不是 InWorldSubState 平级的独立子状态"，因此不改
            //     InWorldSubState 枚举（不新增原语），改用 SubStateId 的自定义子状态扩展点
            //     （AppStateMachineConfig.AddCustomSubState，专为此设计）。无论是否传入
            //     clockHost，这两个自定义子状态与转移都会登记——只是新增的合法转移，不影响任何
            //     既有转移的合法性，对不使用离散模式的调用方无副作用。
            // ---------------------------------------------------------
            // 收边任务补齐："GameplayAssembly 装配时优先用表"：found.game_state/found.hook 两张
            // 表已进 data/_framework（见 FoundGameStateSchema/FoundHookSchema 判断记录），
            // FromRegistry/LoadDefinitions 在表缺失时分别退化为 Default()/WellKnownHooks 两个
            // 内置常量，行为与本次改动之前完全一致，只是优先读表。
            var appStateConfig = AppStateMachineConfig.FromRegistry(registry);
            AwaitingInputSubState = appStateConfig.AddCustomSubState("AwaitingInput");
            PlayingBackSubState = appStateConfig.AddCustomSubState("PlayingBack");
            appStateConfig.AllowSubTransition(SubStateId.Combat, AwaitingInputSubState);
            appStateConfig.AllowSubTransition(AwaitingInputSubState, SubStateId.Combat);
            appStateConfig.AllowSubTransition(SubStateId.Combat, PlayingBackSubState);
            appStateConfig.AllowSubTransition(PlayingBackSubState, SubStateId.Combat);

            AppState = new AppStateHost(bus, appStateConfig);
            Hooks = new HookRegistry(bus);
            Hooks.DeclareFromDefinitions(FoundHookSchema.LoadDefinitions(registry));

            // ---------------------------------------------------------
            // 10.5) ADR-0013 离散时间模型：只在调用方传入 clockHost 时装配（见该构造参数判断
            //     记录）；scheduler 已在第 3.5 步造好（ExprHostFactory 需要提前拿到访问器），这里
            //     只补 TimeModelSwitch（需要 AppState，第 10 步才就绪）并回填第 3.5 步声明的
            //     timeModelSwitchRef 闭包变量（TurnScheduler 构造期传入的 InitiativeStatProvider
            //     真正被调用时——战斗中——timeModelSwitchRef 早已赋值完毕，见该处判断记录）。
            // ---------------------------------------------------------
            _clockHost = clockHost;
            if (clockHost != null && scheduler != null)
            {
                Pacing = pacingPolicy ?? new WaitForPlaybackPacingPolicy();

                var switchInstance = new Core.Gameplay.Assembly.TimeModelSwitch(
                    scheduler, clockHost, AppState, world, Carriers.Units, spatial, bus, registry,
                    timeModelSwitchOptions, combatParticipantsResolver,
                    factions: Carriers.Rules.Factions, combatOptions: Carriers.Rules.CombatOptions);
                timeModelSwitchRef = switchInstance;

                // ADR-0013 补齐：movement_budget_rule: action_points 落地——把战斗时间模型声明的
                // 移动预算规则/单位成本回填进 Carriers.MovementOptions（构造早于本处，见该属性
                // 判断记录），并把行动点账本的消耗/结束回合出口接到刚造好的 scheduler（见
                // MovementOptions.TryConsumeActionPoints/RequestEndTurn 判断记录）。CombatModel 为
                // 空（数据集只声明了探索时间模型）时保持 MovementOptions 默认值（"distance"），
                // 不做任何回填。
                if (switchInstance.CombatModel != null)
                {
                    Carriers.MovementOptions.MovementBudgetRule = switchInstance.CombatModel.MovementBudgetRule ?? "distance";
                    Carriers.MovementOptions.MovementActionCostPerUnit = switchInstance.CombatModel.MovementActionCostPerUnit ?? 0.0;
                    Carriers.MovementOptions.TryConsumeActionPoints = scheduler.TryConsumeActionPoints;
                    Carriers.MovementOptions.RequestEndTurn = scheduler.EndTurn;
                }

                TurnScheduler = scheduler;
                TimeModelSwitch = switchInstance;

                // W2 收边补齐（A3 审计 #4/#5，DECISIONS 拍板 4，SkillOptions.TryConsumeActionPoints/
                // IsDiscreteStep 判断记录）：与上面 MovementOptions 的回填手法一致——resolvedSkillOptions
                // 是第 3 步传给 CarriersAssembly（进而 RulesAssembly/CastPipeline）的同一个实例引用，
                // 这里回填两个委托后，CastPipeline 后续每次读取 _options.TryConsumeActionPoints/
                // IsDiscreteStep 都会看到装配完成后的值。与 MovementOptions 的回填不同，这里不放在
                // "CombatModel != null"分支内——技能施放的行动点检查/GCD 离散豁免不依赖战斗时间模型
                // 是否声明了移动预算规则，只要装配了离散模式（scheduler 非空）就应生效。
                // TryConsumeActionPoints 与 MovementOptions 共用同一账本（scheduler.TryConsumeActionPoints
                // 同一个方法组，见 SkillOptions.TryConsumeActionPoints 判断记录"同一账本"）。
                // IsDiscreteStep：真正处于离散模式（_clockHost.Mode == Discrete）且当前正在
                // Advance() 内处理某一个 Discrete 步（_isProcessingDiscreteStep，见该字段与
                // Advance 判断记录）两者都为真才返回 true——避免连续模式下（即便曾经装配过离散
                // 模式）误判为离散步。
                resolvedSkillOptions.TryConsumeActionPoints = scheduler.TryConsumeActionPoints;
                resolvedSkillOptions.IsDiscreteStep =
                    () => _clockHost != null && _clockHost.Mode == TimeModelMode.Discrete && _isProcessingDiscreteStep;

                // H4 补齐（意图路由缺口 1，见 WorldSim.AttachDiscreteRouting 判断记录）：只有
                // world 是具体类型 WorldSim 时才能接线（IWorldSim 接口本身不暴露这个便利方法，
                // 同 TurnScheduler/DiagnosticsWarnings 一贯的判断记录）——调用方一律传入
                // WorldSim 实例（见 03 第 9 节 IWorldSim 唯一实现），这里用 is 模式做防御性判断，
                // 不是 WorldSim 时静默跳过（不抛异常：理论上允许调用方传入自定义 IWorldSim 测试替身，
                // 此时离散模式下 SubmitIntent 路由不生效，退化为 H4 之前的行为——同一份"未接线时
                // 完全无副作用"的兼容性承诺，见该方法判断记录）。
                if (world is Core.Foundation.SimLoop.WorldSim worldSimConcrete)
                {
                    worldSimConcrete.AttachDiscreteRouting(clockHost, scheduler);
                }
            }

            // ---------------------------------------------------------
            // 11) SpawnHost（GobjSpawner 接 GameObjectFactory.Spawn）。
            // ---------------------------------------------------------
            var resolvedSpawnOptions = spawnOptions ?? new SpawnOptions();
            resolvedSpawnOptions.PlayerUnitResolver = () => PlayerUnitProvider();
            resolvedSpawnOptions.GobjSpawner ??= (templateId, mapId, position, facing) =>
                Carriers.GameObjects.Spawn(templateId, mapId, position, facing);
            // R14 根治：GobjDespawner 接 GameObjectFactory.Despawn，供 SpawnHost.Load 同图读档时清理
            // gobj 域的孤儿实体（惯例同上一行 GobjSpawner）。
            resolvedSpawnOptions.GobjDespawner ??= entityId => Carriers.GameObjects.Despawn(entityId);
            Spawn = new SpawnHost(registry, WorldState, Carriers.Creatures, bus, ExprHostFactory, resolvedSpawnOptions);

            // ---------------------------------------------------------
            // 12) EncounterHost + LevelHost。spawnRequester：缺口 14 已在 core/gameplay/spawn 补上
            //     ISpawnHost.SpawnNow(spawnId, mapId)，签名与 SpawnRequester 委托一致（见该委托类型
            //     判断记录"组装期把 (spawnId, mapId) => spawnHost.Execute(spawnId, mapId) 一类适配
            //     逻辑接到本委托签名"），本装配根直接把 Spawn.SpawnNow 接上，不再退化为空列表。
            // ---------------------------------------------------------
            SpawnRequester spawnRequester = Spawn.SpawnNow;
            Encounter = new EncounterHost(
                registry, bus, Carriers.Creatures, Carriers.Rules.Ai, Hooks, ExprHostFactory, Reward, Carriers.Units,
                spawnRequester, exprSchema: GameplaySchemaCatalog.FullExprSchema, diagnostics: null);
            Level = new LevelHost(registry, Encounter, bus);

            // ---------------------------------------------------------
            // 12.5) 收边任务补齐（缺口 (a)：EncounterDefinition.CombatModeOverride/InitiativeOverride
            //     此前只落地读取字段，TimeModelSwitch.SetPendingOverride 从无调用点，见该方法与
            //     EncounterHost.TryGetModeOverride 判断记录）：只在离散时间模型已装配
            //     （TimeModelSwitch != null，即调用方传入了 clockHost）时才订阅——未传入 clockHost
            //     的组合根（多数纯连续模式测试夹具）不受影响，encounter.started/won/lost 照常发布，
            //     只是没有订阅者读取覆盖字段。encounter.started 时经实例 id 反查该次运行对应
            //     encounter.def 的覆盖字段并转交 TimeModelSwitch；encounter.won/lost（遭遇结束的
            //     两个终点事件，08 未定义单独的"遭遇结束"事件）时清空——避免一次声明了覆盖但从未
            //     真正引发战斗（combat.entered 从未触发）的遭遇，残留的 pending 覆盖意外影响下一次
            //     不相关的战斗（08 判断记录"遭遇结束清除"）。
            // ---------------------------------------------------------
            if (TimeModelSwitch != null)
            {
                var timeModelSwitchForEncounter = TimeModelSwitch;
                var encounterHostForOverride = Encounter;

                bus.Subscribe<EncounterStartedEvent>(EncounterEventKeys.Started, evt =>
                {
                    if (encounterHostForOverride.TryGetModeOverride(evt.EncounterId, out var combatModeOverride, out var initiativeOverride))
                    {
                        timeModelSwitchForEncounter.SetPendingOverride(combatModeOverride, initiativeOverride);
                    }
                });
                bus.Subscribe<EncounterWonEvent>(EncounterEventKeys.Won, _ => timeModelSwitchForEncounter.SetPendingOverride(null));
                bus.Subscribe<EncounterLostEvent>(EncounterEventKeys.Lost, _ => timeModelSwitchForEncounter.SetPendingOverride(null));
            }

            // ---------------------------------------------------------
            // 13) AchievementHost。
            // ---------------------------------------------------------
            Achievement = new AchievementHost(
                registry, bus, Carriers.Units, ExprHostFactory, Reward,
                achievementOptions ?? new AchievementOptions(PlayerUnitProvider),
                exprSchema: GameplaySchemaCatalog.FullExprSchema, diagnostics: null);

            // ---------------------------------------------------------
            // 14) DialogHost。回调：vendor 打开由调用方（表现层/游戏层）真正处理，本装配根只记一条
            //     诊断占位不做任何事（没有 UI 概念）；teleport/save/start_encounter 接到本类自己的
            //     TeleportUnit/SaveRequested占位/Encounter.Start。
            // ---------------------------------------------------------
            var gossipMenus = registry.GetAll(DialogSchemas.GossipMenu.Name)
                .Select(r => GossipMenuDefinition.FromRecord(r, GameplaySchemaCatalog.FullExprSchema)).ToList();
            var storyTrees = registry.GetAll(DialogSchemas.StoryTree.Name)
                .Select(r => StoryTreeDefinition.FromRecord(r, GameplaySchemaCatalog.FullExprSchema)).ToList();

            TeleportRequestedCallback teleportRequested = (unitId, targetRef) => TeleportUnit(unitId, targetRef);
            EncounterStartRequestedCallback encounterStartRequested = encounterRef =>
                Encounter.Start(encounterRef, world.GetEntity(PlayerUnitProvider())?.MapId ?? default, PlayerUnitProvider());

            // G1 遗留恢复：DialogHost.saveRequested（08 第 3.1 节 gossip Action save）接同一份
            // RequestAutosave（与 GobjOptions.SaveRequester 同一委托逻辑，见构造函数最前面判断记录），
            // 不再传 null（此前"save 动作只记诊断、不真正存档"的缺口到此结束）。
            Dialog = new DialogHost(
                gossipMenus, storyTrees, bus, ExprHostFactory, AppState, Quest, Hooks, WorldState, Carriers.Rules.Skill,
                vendorOpenRequested: null, teleportRequested: teleportRequested, saveRequested: RequestAutosave,
                encounterStartRequested: encounterStartRequested, exprDiagnostics: null, diagnostics: null);

            // ---------------------------------------------------------
            // 15) AreaTriggerHost：TrapTrigger 接 GameObjectHost.TriggerTrap；EncounterStartRequested
            //     复用第 14 步同一段逻辑；SceneRouter 若注入直接接线（05 第 7 节"经场景路由"）；
            //     MapTransitionRequested 兜底同 TeleportUnit（LoadScene 只切场景资源，不移动实体，
            //     实体位置仍由本装配根落地，见判断记录）。
            // ---------------------------------------------------------
            var resolvedAreaTriggerOptions = areaTriggerOptions ?? new AreaTriggerOptions();
            resolvedAreaTriggerOptions.TrapTrigger ??= (gobjInstanceId, unitId) =>
                Carriers.GameObjectInteractions.TriggerTrap(gobjInstanceId, unitId);
            resolvedAreaTriggerOptions.EncounterStartRequested ??= (unitId, encounterRef) =>
                Encounter.Start(encounterRef, world.GetEntity(unitId)?.MapId ?? default, unitId);
            resolvedAreaTriggerOptions.MapTransitionRequested ??= (unitId, targetMap, spawnPoint) =>
                TeleportUnit(unitId, targetMap, spawnPoint);
            resolvedAreaTriggerOptions.SceneRouter ??= sceneRouter;

            // 加固任务（AreaTrigger 实体化，见 core/gameplay/area_trigger/contracts/AreaTriggerEntity.cs
            // 判断记录）：新增 world（IWorldSim）依赖，供 AreaTriggerHost 在 Register/RegisterTrap/
            // Unregister/UnloadMap 时创建/销毁对应实体。
            AreaTrigger = new AreaTriggerHost(world, WorldState, bus, ExprHostFactory, Hooks, resolvedAreaTriggerOptions);

            // ---------------------------------------------------------
            // 16) 补上 gobj 侧四个 L4 回调（GobjOptions 是 CarriersAssembly 构造时已经用过的同一个
            //     实例——CarriersAssembly 不拷贝，直接持有引用，见该类型第 7 步；此刻回填仍然生效，
            //     因为 GameObjectHost 内部按需读取 _options 字段，不是构造期一次性拷出委托值）。
            //     判断记录（DialogOpenerDelegate 契约缺口）：签名只有 (unitId, dialogRef)，不携带
            //     触发交互的 gobj 实例 id——DialogHost.OpenGossip 需要三元组 (unitId, npcId, menuId)。
            //     本装配根权宜地把 dialogRef 同时当 npcId 使用（会话的 NpcId 只用作 Expr target 与
            //     vendor/quest 回调定位，不要求是真正的生物单位）；需要更精确身份时应扩展
            //     core/carriers/gobj 的委托签名（不在本任务允许改动范围）。
            // ---------------------------------------------------------
            // 判断记录（缺口 15，TeleportTargetResolver 未接 onFailure 诊断回调）：解析失败时
            // GameObjectHost.DoTeleport 本已记一条诊断（"teleport_target_ref ... 无法解析"），本
            // 装配根不重复接一份诊断出口——本类没有统一的诊断汇聚点（各 L4 宿主各自持有独立的
            // I*Diagnostics 接口，见各自类型），onFailure 回调仅供单元测试直接构造
            // TeleportTargetResolver 时使用。
            _teleportTargetResolver = new TeleportTargetResolver(registry);

            // R04 根治（architecture/落地计划/audit-5e779c6-20260907），CR130-05 收口（架构落地计划/
            // audit-5c444f1-20260908）：core/carriers/gobj.InteractIntentTickHandler
            // （TickPhase.TriggerEvaluation，由 CarriersAssembly 注册，不在 R04 当时允许改动的目录
            // 范围内）只检查 InteractResult.Success，从未读取 GameObjectHost.DoTeleport 对跨地图目标
            // 返回的 InteractResult.DispatchedRef（见该方法判断记录"……由调用方（L4）驱动真正的场景
            // 切换"）——直接交互 teleporter 类 gobj（不经 gossip 的 teleport 动作）时，跨地图目标只在
            // DoTeleport 内部被正确解析出 (MapId, Position) 又原样丢在返回值里，从没有任何 L4 代码
            // 接手。gossip 的 teleport 动作已经通过 DialogHost → teleportRequested → TeleportUnit
            // 正确处理跨地图（见上面第 14 步），本处按同一惯例补上直接交互这条路径：订阅
            // gobj.interacted（不改内置行为本身、不重复调用 Interact，避免 chest/quest_object 等其它
            // kind 被二次触发副作用）。
            //
            // CR130-05 根治：原实现命中 GobjKind.Teleporter 就无条件按它的 teleport_target_ref 再走
            // 一遍 TeleportUnit（内部用本装配根自己的默认 _teleportTargetResolver 重新解析）——这与
            // GameObjectHost.DoTeleport 已经用（可能是调用方注入的自定义）GobjOptions.TeleportResolver
            // 做出的判定是两次独立解析，构成双重消费：DoTeleport 若判定同图并已经原地 SetPosition
            // （或 resolver 显式返回 null、判定"不该传送"），这里仍会用默认 resolver 重新解析一遍
            // 同一个 ref，把自定义结果覆盖成默认结果，或在 resolver 明确拒绝时仍然把人传送走（外部
            // 审计复现：同图自定义结果 (99,88) 被内置 (1,2) 覆盖）。现在唯一权威判定者是
            // DoTeleport——是否传送、传去哪、是否已经是同图立即生效，全部由它（经它读到的 resolver）
            // 一次性终局判定；本监听只在 GobjInteractedEvent.TeleportTargetRef 非空（即 DoTeleport
            // 判定"这是一次真正需要跨地图、本模块内部完不成"的传送）时才接手，原样使用事件携带的同一个
            // ref 触发 TeleportUnit 补完场景切换，不再独立重新解析、不再对同图/被拒绝的情形做任何
            // 事情——同图时 DoTeleport 内部的 SetPosition 就是唯一一次落地，不会被本监听二次覆盖。
            _bus.Subscribe<GobjInteractedEvent>(CarriersEventKeys.GobjInteracted,
                evt =>
                {
                    if (evt.TeleportTargetRef.HasValue)
                    {
                        TeleportUnit(evt.UnitId, evt.TeleportTargetRef.Value);
                    }
                });

            resolvedGobjOptions.DialogOpener ??= (unitId, dialogRef) => Dialog.OpenGossip(unitId, dialogRef, dialogRef);
            resolvedGobjOptions.TeleportResolver ??= _teleportTargetResolver.Resolve;
            // 自动存档槽 id/时间戳来源已在构造函数最前面解析为 RequestAutosave（缺口 16），
            // DialogHost.saveRequested 与本处共用同一份，见该处判断记录。
            resolvedGobjOptions.SaveRequester ??= RequestAutosave;
            resolvedGobjOptions.QuestActionDispatcher ??= (unitId, questActionRef) => Quest.Accept(unitId, questActionRef);

            // ---------------------------------------------------------
            // 17) tick 处理器挂载（见 README"tick 阶段挂载表"）：全部注册到 TriggerEvaluation——
            //     这是 IWorldSim 对外开放的最后一个阶段（EventDispatch/LifecycleCleanup 不可外部注册，
            //     见 TickPhase 枚举注释），顺序：区域触发（依赖本 tick 内已经完成的移动结算）→ 遭遇
            //     胜负评估（依赖本 tick 内已经完成的战斗结算）→ 掉落过期清理 → 经济/刷新计时推进。
            // ---------------------------------------------------------
            world.RegisterPhaseHandler(TickPhase.TriggerEvaluation, new AreaTriggerTickHandler(AreaTrigger, Carriers.Units));
            // 收边任务补齐（缺口 (b)：见 EncounterTickHandler 判断记录）：传入 bus 以便按
            // 离散/连续区分求值时机——离散模式下改由 sim.turn_ended/sim.round_ended 事件驱动。
            world.RegisterPhaseHandler(TickPhase.TriggerEvaluation, new EncounterTickHandler(Encounter, bus));
            world.RegisterPhaseHandler(TickPhase.TriggerEvaluation, new LootExpiryTickHandler(Loot, () => Carriers.Rules.SimTime));
            world.RegisterPhaseHandler(TickPhase.TriggerEvaluation, new EconomySpawnUpdateTickHandler(Economy, Spawn));

            // ---------------------------------------------------------
            // 18) DeathPolicyHost（W2 收边补齐，DECISIONS 拍板 3）：death 是一个新 L4 模块，本步
            //     骤惯例同第 3/16 步——resolvedDeathPolicyOptions 就地 new 出来，ReviveUnit/
            //     ResolveDefaultSpawn 两个 L4↔L3 边界委托用 ??= 只在调用方未显式覆盖时接线（同
            //     resolvedGobjOptions 四个回调的接线手法）：
            //     - ReviveUnit 接 WorldUnitAccess.Revive——只有 Carriers.Units 运行期确实是
            //       WorldUnitAccess（真实装配的唯一实现，测试替身可能不是）时才能接线，用 is 模式
            //       防御性判断（同第 10.5 步 world is WorldSim 的一贯做法），不是时静默跳过（不
            //       抛异常：respawn_point 策略此时退化为"只记诊断、不复活"，见 DeathPolicyHost
            //       判断记录）。
            //     - ResolveDefaultSpawn 接同一个 teleportTargetResolver（第 16 步已构造）的
            //       Resolve 方法组——传入地图 id 本身即命中该方法"整串即地图 id"的两段式解析路径，
            //       不需要为 death 模块另造一份地图查找逻辑。
            //     defaultCombatDeathPolicy 取 Carriers.Rules.CombatOptions.DeathPolicy（06 第 4.6
            //     节"策略来自 CombatOptions.DeathPolicy"），deathPolicyOptions.Policy 非空时覆盖。
            // ---------------------------------------------------------
            var resolvedDeathPolicyOptions = deathPolicyOptions ?? new Core.Gameplay.Death.DeathPolicyOptions();
            if (Carriers.Units is Core.Carriers.Unit.WorldUnitAccess worldUnitAccessForDeath)
            {
                resolvedDeathPolicyOptions.ReviveUnit ??= worldUnitAccessForDeath.Revive;
            }

            resolvedDeathPolicyOptions.ResolveDefaultSpawn ??= _teleportTargetResolver.Resolve;

            // 外部审核阻塞项 2 收口：reload_save 策略此前只调用 ISaveSystem.Load 本身，既不切场景、
            // 也不管玩家存活状态是否被正确覆盖（见 DeathPolicyHost.OnUnitDied 判断记录、
            // RestoreFromSlot 类型注释）——接同一份"读档 + 必要时切场景"协议，行为与
            // ShellHost.LoadGame 保持一致。
            resolvedDeathPolicyOptions.ReloadSave ??= RestoreFromSlot;

            Death = new Core.Gameplay.Death.DeathPolicyHost(
                bus, world, SaveSystem, AppState, Carriers.Rules.CombatOptions.DeathPolicy, resolvedDeathPolicyOptions);
            world.RegisterPhaseHandler(TickPhase.TriggerEvaluation, Death);
        }

        /// <summary>
        /// 一站式进入地图（03 §6 <c>post_load</c> 回调应做的事，见类型注释）：区域触发 + 刷新表
        /// 按地图装载，经济按地图补货。调用方（场景路由的 <c>post_load</c> 钩子，或测试直接调用）
        /// 在切换到新地图后调用一次。
        /// </summary>
        public void EnterMap(Id mapId, Id playerUnitId)
        {
            // 外部审核阻塞项 1 收口（见 architecture/落地计划/audit-20260907/followup-2026-09-07.md
            // "外部审核阻塞项处理"一节）：此前 LootHost.ReattachToWorld 只在
            // games/_template/Runtime/GameBootstrap.HandlePostLoad 里手工接了一次，框架自身的
            // Shell 读档入口（Presentation.Shell.ShellHost.LoadGame → ISceneRouter.LoadScene →
            // post_load 钩子）没有任何调用方保证会调用它——只要某个宿主（引擎适配层/未来其它游戏）
            // 的 post_load 钩子调用了本方法（EnterMap 本就是"一站式进入地图"的既定入口，见本方法
            // 类型注释），就自动补上这一步，不再要求每个宿主各自记得接线。放在最前面：
            // ReattachToWorld 只操作 LootHost 自己的跟踪表与 IWorldSim，不依赖本方法下面三行
            // （AreaTrigger/Spawn/Economy）任何一行的执行结果，顺序上谁先谁后都不影响正确性，放最
            // 前面只是让"进图先恢复掉落物、再处理该地图的其它进图逻辑"这一顺序更直观。幂等（见该
            // 方法判断记录"已经在世界里的会被跳过"），首次进图（从未发生过 ClearAll）时是安全的
            // 空操作，不会因为重复调用产生副作用。
            Loot.ReattachToWorld(mapId);

            AreaTrigger.LoadForMap(mapId, Carriers.Rules.Registry);
            Spawn.ApplyForMap(mapId);
            Economy.OnMapEnter(mapId);
        }

        /// <summary>
        /// 外部审核阻塞项 2 收口新增（见 architecture/落地计划/audit-20260907/followup-2026-09-07.md
        /// "外部审核阻塞项处理"一节）：<c>ISaveSystem.Load</c> + "若目标地图与当前地图不同则切场景"
        /// 这段逻辑，供 <c>Presentation.Shell.ShellHost.LoadGame</c>（L5）与
        /// <see cref="Core.Gameplay.Death.DeathPolicyHost"/>（同 L4，经
        /// <see cref="Core.Gameplay.Death.DeathPolicyOptions.ReloadSave"/> 委托接线，见构造函数第
        /// 18 步）两处共用同一份协议。
        /// <para>
        /// 判断记录（不是"同一份代码"，是"同一份协议、各自一份实现"）：<c>ShellHost</c> 是 L5，不
        /// 持有、也不允许反向依赖本类型（L4，铁律 P1/P3——L5 只编排窄契约，不直接持有 L4 具体状态），
        /// 因此不能把 <c>ShellHost.LoadGame</c> 直接改成调用本方法；<c>ShellHost.LoadGame</c> 保留
        /// 自己那份等价实现（读 <c>LoadResult.CurrentMapId</c>、必要时调用
        /// <c>ISceneRouter.LoadScene</c>，见该方法判断记录"缺口 11 恢复"）。<see cref="DeathPolicyHost"/>
        /// 与本类型同属 L4、由本类型的构造函数统一装配，可以直接接线，不需要重复实现一遍。
        /// </para>
        /// <para>
        /// 判断记录（只发起 <c>LoadScene</c>，不在本方法内同步调用 <see cref="EnterMap"/>）：
        /// <see cref="ISceneRouter.LoadScene"/> 只是发起一次异步加载（见该方法注释"本方法本身不
        /// 等待加载完成"），真正的"切场景完成 → 进图"由调用方早已注册好的
        /// <c>ISceneRouter.RegisterPostLoadHook</c> 钩子驱动（该钩子内部调用 <see cref="EnterMap"/>，
        /// 已经把 <see cref="Loot"/>.<c>ReattachToWorld</c> 接了进去，见该方法判断记录）——本方法
        /// 重复调用一次 <see cref="EnterMap"/> 只会导致同一张地图被进两次，不属于本方法职责。
        /// 目标地图与当前地图相同（无需切场景）时，<c>ISaveSystem.Load</c> 已经把全部已注册段
        /// （含本次外部审核阻塞项 2 新增的 <see cref="PlayerVitalsPersistable"/>）直接写回长期存活
        /// 的 <c>PlayerUnit</c>/<c>Unit</c> 运行期对象——该对象此前从未因"死亡"被移出
        /// <see cref="IWorldSim"/>（05 文档"死亡是逻辑状态，不是生命周期状态"），不需要任何"重新
        /// 进图"步骤即可生效。
        /// </para>
        /// </summary>
        public LoadResult RestoreFromSlot(Id slotId)
        {
            // 判断记录（必须在 SaveSystem.Load 之前取"当前地图"）：SaveSystem.Load 内部会依次调用
            // 全部已注册 IPersistable 的 Load（含 world.current_map_id 段——UnitPersistable.
            // CurrentMapId 直接把玩家实体的 MapId 字段改写成存档里的地图 id），调用完成后
            // "玩家实体当前的 MapId"已经等于"存档里的地图 id"，不再反映"读档前玩家实际在哪张
            // 地图"——若在 Load 之后才读取 _world.GetEntity(...)?.MapId 来判断"是否需要切场景"，
            // 这个比较永远是"相等"，切场景分支会被误判为不需要执行而跳过（本方法收口过程中实测
            // 复现的一处时序缺口）。
            var mapIdBeforeLoad = _world.GetEntity(PlayerUnitProvider())?.MapId;

            var result = SaveSystem.Load(slotId);

            if ((result.Status == LoadStatus.Loaded || result.Status == LoadStatus.LoadedFromBackup) &&
                result.CurrentMapId.HasValue && _sceneRouter != null &&
                (mapIdBeforeLoad == null || !mapIdBeforeLoad.Value.Equals(result.CurrentMapId.Value)))
            {
                try
                {
                    _sceneRouter.LoadScene(result.CurrentMapId.Value);
                }
                catch (ArgumentException)
                {
                    // 地图 id 未知：读档本身仍然算成功，场景切换失败留给上层诊断/重试（同
                    // ShellHost.LoadGame 同款判断记录）。
                }
                catch (InvalidOperationException)
                {
                    // 当前应用状态不允许切到 Loading（例如已经在 Loading 中）：同上，不吞掉
                    // 读档结果本身。
                }
            }

            return result;
        }

        /// <summary>
        /// <see cref="EnterMap"/> 的卸载对应物（ADR-0016 背景一节联动发现的既有缺口——本类此前只有
        /// "进图"入口，没有"出图"入口）：调用方（场景路由的 <c>pre_unload</c> 钩子，或测试直接调用）
        /// 在切换到新地图、旧地图即将卸载前调用一次，把 <see cref="AreaTrigger"/>/<see cref="Spawn"/>
        /// 两个按地图持有内容登记（触发器实例、刷新点运行时状态）的宿主提前清空——二者都早已各自
        /// 提供 <c>UnloadMap(Id)</c> 方法（<c>AreaTriggerHost</c>/<c>SpawnHost</c>），只是此前没有
        /// 任何调用方接线，属于实现未跟上既有能力的情形（同类还有 <c>Core.Rules.Ai.AiHost</c>——
        /// 它没有按地图分片的登记，改为直接订阅 <c>entity.destroyed</c> 逐单位清理，见该类型判断
        /// 记录，不需要挂在本方法）。
        /// <para>
        /// 判断记录（复现路径与验收）：不调用本方法也不会立刻抛异常——<c>IWorldSim.ClearAll</c> 会把
        /// 实体清空、<c>AiHost</c> 订阅 <c>entity.destroyed</c> 自行清理，两者都不依赖本方法；但
        /// <c>SpawnHost</c> 按 <c>spawn.table</c> 记录 id（不是实体 id）持有 <c>RuntimeState</c>，
        /// 记录 id 在同一张地图重新进图时保持不变——不调用 <c>LeaveMap</c> 会让 <c>RuntimeState</c>
        /// 里的 <c>EntityId</c> 继续指向已被 <c>ClearAll</c> 销毁的旧实体，下次 <c>ApplyForMap</c>
        /// 误判"该刷新点仍然存活"而跳过重新生成——不是崩溃，是刷新逻辑悄悄失效，任务验收用例
        /// "进图 → 生成生物 → 卸载 → 再进图 → tick 数十次不抛异常"覆盖的是 <c>AiHost</c> 那一半
        /// 缺口（不调用本方法确实会抛 <see cref="System.InvalidOperationException"/>，见
        /// <c>AiHostCascadeCleanupTests</c>），<c>SpawnHost</c> 这一半缺口由 <c>LeaveMap</c> 补上、
        /// 由 <c>GameplayAssemblyMapReloadTests</c> 覆盖"卸载重进后刷新点能再次生成"。
        /// </para>
        /// </summary>
        public void LeaveMap(Id mapId)
        {
            // 判断记录（先 DispatchPending 再清理）：调用方约定顺序是"world.ClearAll() → 本方法"
            // （同 SceneRouter.FinishLoading 的既有顺序）；ClearAll 只把每个实体的 entity.destroyed
            // Enqueue（不立即派发，见 IWorldSim.ClearAll 注释），若不在这里补一次 DispatchPending，
            // Core.Rules.Ai.AiHost（订阅 entity.destroyed 清理登记，见该类型判断记录）与
            // core/carriers/assembly.EntitySpatialSyncHost 都不会来得及在下一次 world.Tick 之前收到
            // 通知——下一次 tick 的 AiDecision 阶段（TickPhase 顺序第 2 步，早于第 7 步
            // EventDispatch）仍会用残留的旧登记推进已销毁单位，重新触发本方法要解决的那个
            // InvalidOperationException。直接调用方若已经自己 DispatchPending 过（如 SceneRouter），
            // 这里再调一次是无害的空操作。
            _bus.DispatchPending();

            AreaTrigger.UnloadMap(mapId);
            Spawn.UnloadMap(mapId);

            // GP-04 根治（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：本方法此前
            // 只卸载 AreaTrigger/Spawn，从未终止 Encounter/Level——EncounterTickHandler.EvaluateAll
            // 每次仍会枚举 IEncounterHost.ActiveInstanceIds 全部活跃实例（不按地图过滤），A 图的遭遇
            // 会在玩家已经身处 B 图时继续被求值：用 B 图的玩家位置判定 A 图遭遇的场地边界/胜负条件，
            // 可能误发波次、误判胜负发奖励、或触发"离开边界重置"把 A 图的遭遇状态在 B 图上下文里
            // 反复重置。改法：离开地图时把绑定在该地图上的遭遇/关卡运行一并终止，下次重新进图时
            // Encounter.Start/Level.StartLevel 会建立全新实例，不会复用旧实例的波次/阶段进度。
            // 顺序上先终止 Level（释放它的 Won 订阅）再终止 Encounter 本身：若反过来，Encounter 侧
            // 标记为不活跃后仍可能在同一帧内已经排队的 Won 事件回调触发 LevelHost 尝试推进下一个
            // 遭遇——虽然本方法调用期间不会有新事件派发（都是同步直接调用，不经 EventBus），但两个
            // 子系统各自独立维护状态、顺序对结果没有影响，先 Level 后 Encounter 只是让"先断开对外
            // 的编排关系，再终止被编排的具体运行"这个收尾顺序更直观。
            Level.AbortForMap(mapId);
            Encounter.AbortForMap(mapId);
            if (TimeModelSwitch != null)
            {
                // 该地图上任何遭遇留下的战斗节奏/先攻覆盖都已随遭遇终止一并失效——离开地图不是
                // "遭遇结束"（不发 Won/Lost），显式清空 pending override，避免带着上一张图的覆盖值
                // 进入下一张图（该图可能没有任何 encounter.started 事件来覆盖掉它）。
                TimeModelSwitch.SetPendingOverride(null);
            }
        }

        /// <summary>
        /// "主循环一步"（ADR-0013、03 第 3 节）：连续模式下等价于 <c>ClockHost.Advance</c>；离散
        /// 模式下驱动 <see cref="TurnScheduler"/> 直到需要停下等待——轮到玩家行动
        /// （<see cref="TurnScheduler.NextStep"/> 返回空，见该类型）或表现回放
        /// （<see cref="Pacing"/> 为 <see cref="PacingMode.WaitForPlayback"/>）——期间把
        /// <see cref="AwaitingInputSubState"/>/<see cref="PlayingBackSubState"/> 压入/弹出
        /// <see cref="AppState"/> 子状态栈（见 03 第 2 节两个附加子态的触发/解除条件）。
        /// <see cref="PacingMode.Immediate"/> 下会在一次调用内连续推进多个离散步（例如整段 AI 行动
        /// 链），直到轮到玩家或战斗结束。未在构造函数传入 <c>clockHost</c> 时抛
        /// <see cref="InvalidOperationException"/>。
        /// </summary>
        public void Advance(double realDeltaSeconds)
        {
            if (_clockHost == null)
            {
                throw new InvalidOperationException(
                    "未提供 ISimClockHost，无法调用 Advance；请在构造 GameplayAssembly 时传入 clockHost 参数");
            }

            if (_clockHost.Mode == TimeModelMode.Continuous)
            {
                InterpolationAlpha = _clockHost.Advance(realDeltaSeconds);
                return;
            }

            // 离散模式：本分支不调用 ISimClockHost.Advance（离散步改由 TurnScheduler.NextStep
            // 产生，见类型注释），因此拿不到、也不需要连续模式那份基于累积器的插值系数——固定给
            // 1.0（见 InterpolationAlpha 属性判断记录）。while 循环内两个 return 分支（轮到玩家/
            // 需要等待表现回放）都复用这个统一赋值，不需要在每个 return 前分别设置。
            InterpolationAlpha = 1.0;

            while (true)
            {
                var step = TurnScheduler!.NextStep();
                if (step == null)
                {
                    PopSubStateIfCurrent(PlayingBackSubState);
                    PushSubStateIfNotCurrent(AwaitingInputSubState);
                    return;
                }

                PopSubStateIfCurrent(AwaitingInputSubState);
                PopSubStateIfCurrent(PlayingBackSubState);

                // W2 收边补齐：本次 Tick 调用范围内（离散意图路由、AiTickHandler 驱动的技能施放都
                // 发生在这次调用期间）标记"正在处理离散步"，供 resolvedSkillOptions.IsDiscreteStep
                // 闭包读取（见 _isProcessingDiscreteStep 字段判断记录）。try/finally 确保 Tick 内部
                // 抛异常时标记也能正确复位，不会把"正在处理离散步"状态泄漏到本次 Advance 调用之外。
                _isProcessingDiscreteStep = true;
                try
                {
                    _world.Tick(step.Value);
                }
                finally
                {
                    _isProcessingDiscreteStep = false;
                }

                TurnScheduler.NotifyStepConsumed(step.Value.ActorId!.Value);

                if (Pacing!.Mode() == PacingMode.WaitForPlayback)
                {
                    if (Pacing is WaitForPlaybackPacingPolicy waitForPlayback)
                    {
                        // 根治修复（W5c，第三轮审计"仍保留项"收口）：BeginStep 内部按
                        // WaitForPlaybackPacingPolicy.HasPendingPlayback 探针（经 SetPendingPlaybackProbe
                        // 由 PresentationAssembly 接线）判定本步是否确有待回放内容——没有（探针未接
                        // 线，或接线后确认这一步没有把任何反馈动作排进播放队列）时 IsPlaybackFinished
                        // 立即变回 true，不需要再进入 playing_back 子态等待一个永远不会到来的
                        // presentation.playback_finished，continue 本循环继续推进下一离散步（同
                        // PacingMode.Immediate 分支"一次调用内连续推进直到需要停下"的语义，只是这里是
                        // 确认"这一步不需要停下"之后才继续，不是恒定不停）。
                        waitForPlayback.BeginStep();
                        if (waitForPlayback.IsPlaybackFinished)
                        {
                            continue;
                        }
                    }

                    PushSubStateIfNotCurrent(PlayingBackSubState);
                    return;
                }
            }
        }

        /// <summary>
        /// 根治修复（W5c，第三轮审计"离散回放门‘零事件步骤’无自动通知"仍保留项收口）：把"当前是否
        /// 存在尚未回放完的表现动作"探针接入 <see cref="Pacing"/>（若其具体类型是
        /// <see cref="WaitForPlaybackPacingPolicy"/>）。本类构造期（第 10.5 步）尚无法拿到这份探针
        /// ——表现层（<c>Presentation.Assembly.PresentationAssembly</c>）依赖已构造完成的本类才能
        /// 构造（L5 在 L0～L4 之上），构造顺序上必然晚于本类；调用方（<c>PresentationAssembly</c>
        /// 构造函数）在装配好 <c>FeedbackBinder</c>（播放队列随之就绪）之后调用本方法一次回填，
        /// 同 <c>resolvedGobjOptions</c> 等既有"先占位、后回填"惯例（见本类构造函数第 16 步判断
        /// 记录）。未装配离散模式（<see cref="Pacing"/> 为 <c>null</c>）或调用方构造
        /// <see cref="GameplayAssembly"/> 时显式传入了自定义 <see cref="IPacingPolicy"/>（不是
        /// <see cref="WaitForPlaybackPacingPolicy"/> 具体类型）时静默跳过，不抛异常——同本类一贯
        /// "未接线时不影响装配成功"的取舍；核心测试也可以在不装配表现层的情况下直接调用本方法传入
        /// 一个手工控制的探针，验证 <see cref="Advance"/> 的节奏门行为本身（见
        /// <c>core/gameplay/tests/Discrete/GameplayAssemblyDiscreteWiringTests.cs</c>）。
        /// </summary>
        /// <summary>
        /// 外部审核阻塞项 2 收口新增（见 <see cref="_sceneRouter"/> 字段判断记录）：补上生产装配真实
        /// <c>ISceneRouter</c> 的引用，供 <see cref="RestoreFromSlot"/> 使用。调用方应在构造好真实
        /// <c>Core.Foundation.SceneRouter.SceneRouter</c>（需要本类型的 <see cref="AppState"/>/
        /// <see cref="Hooks"/>，因此必然晚于本类型构造完成）之后立即调用一次；未调用时
        /// <see cref="RestoreFromSlot"/> 只读档、不尝试切场景（同构造函数 <c>sceneRouter</c> 参数
        /// 未传入时的既有退化行为，未调用不影响装配成功，只是场景切换这一步不生效）。
        /// </summary>
        public void AttachSceneRouter(ISceneRouter sceneRouter)
        {
            _sceneRouter = sceneRouter ?? throw new ArgumentNullException(nameof(sceneRouter));
        }

        public void SetPendingPlaybackProbe(Func<bool> hasPendingPlayback)
        {
            if (hasPendingPlayback == null) throw new ArgumentNullException(nameof(hasPendingPlayback));

            if (Pacing is WaitForPlaybackPacingPolicy waitForPlayback)
            {
                waitForPlayback.HasPendingPlayback = hasPendingPlayback;
            }
        }

        /// <summary>表现层发出 <c>presentation.playback_finished</c> 后由调用方转发到这里（见 03
        /// 第 9 节 <c>PacingPolicy.onPlaybackFinished</c>）：解除 <c>playing_back</c> 节奏门，下一次
        /// <see cref="Advance"/> 才会继续推进离散步。未装配离散模式（<see cref="Pacing"/> 为空）时
        /// 空操作。</summary>
        public void NotifyPlaybackFinished()
        {
            Pacing?.OnPlaybackFinished();
        }

        private void PushSubStateIfNotCurrent(SubStateId sub)
        {
            if (!AppState.CurrentSubState.HasValue || !AppState.CurrentSubState.Value.Equals(sub))
            {
                AppState.PushSubState(sub);
            }
        }

        private void PopSubStateIfCurrent(SubStateId sub)
        {
            if (AppState.CurrentSubState.HasValue && AppState.CurrentSubState.Value.Equals(sub))
            {
                AppState.PopSubState();
            }
        }

        /// <summary>
        /// 按 10_存档与持久化.md §3 固定顺序把全部 L3/L4 <see cref="IPersistable"/> 注册进
        /// <paramref name="saveSystem"/>：world_state → player.progression/archetype →
        /// player.inventory/equipment（按 <paramref name="player"/>）→ currencies → quest →
        /// achievement → spawn_state → dropped_loot → world.difficulty → rng.stream_states。
        /// <see cref="RngHost"/> 段放最后（10 §3 步骤 8，全序最末）。实际读写顺序由
        /// <see cref="SaveSections.KnownOrder"/> 决定，与本方法内 <c>RegisterPersistable</c>
        /// 调用顺序无关。
        /// <para>
        /// W2 收边补齐（A4 审计 F1，此前 progression/archetype 两段完全未持久化）：
        /// <c>core/numbers/progression.ProgressionHost</c> 已补 <see cref="ProgressionPersistable"/>
        /// 静态工厂（段 <see cref="SaveSections.PlayerProgression"/>），<see cref="PlayerUnit"/>
        /// 已补 <see cref="UnitPersistable.ArchetypeId"/>（段 <see cref="SaveSections.PlayerArchetype"/>，
        /// 只存 <see cref="PlayerUnit.ArchetypeId"/> 本身，同 05 第 1.2 节判断记录"种族引用在当前
        /// 框架实现下即等价于 ArchetypeId"）——本方法不再跳过这两段。
        /// </para>
        /// </summary>
        public void RegisterPersistables(ISaveSystem saveSystem, PlayerUnit player)
        {
            if (saveSystem == null) throw new ArgumentNullException(nameof(saveSystem));
            if (player == null) throw new ArgumentNullException(nameof(player));

            saveSystem.RegisterPersistable(WorldState);
            saveSystem.RegisterPersistable(ProgressionPersistable.For(Carriers.Rules.Progression, player.EntityId));
            saveSystem.RegisterPersistable(UnitPersistable.ArchetypeId(player));
            saveSystem.RegisterPersistable(UnitPersistable.CurrentMapId(player));
            saveSystem.RegisterPersistable(UnitPersistable.CurrentPosition(player));
            saveSystem.RegisterPersistable(new InventoryPersistable(player.EntityId, Carriers.Inventory));
            saveSystem.RegisterPersistable(new EquipmentPersistable(player.EntityId, Carriers.Inventory, Carriers.Equipment));
            // G1 遗留恢复：player.known_skills（10 §3 步骤 5，此前缺 IPersistable 实现，见
            // KnownSkillsPersistable 判断记录）与 player.skill_bindings（同属步骤 5，
            // SkillBindingPersistable.Load 现已改回经 ISkillBindingHost.Bind 的已知技能校验，依赖
            // known_skills 先还原完毕）。实际读写顺序由 SaveSections.KnownOrder 决定，与本方法内
            // RegisterPersistable 调用顺序无关。
            saveSystem.RegisterPersistable(Core.Rules.Skill.KnownSkillsPersistable.For(Carriers.Rules.Skill, player.EntityId));
            saveSystem.RegisterPersistable(SkillBindingPersistable.For(Carriers.SkillBindings, player));
            saveSystem.RegisterPersistable(new CurrencyPersistable(player.EntityId, Economy));
            saveSystem.RegisterPersistable(new VendorStockPersistable(Economy));
            saveSystem.RegisterPersistable(new QuestPersistable(Quest, PlayerUnitProvider));
            saveSystem.RegisterPersistable(Achievement);
            saveSystem.RegisterPersistable(Spawn);
            saveSystem.RegisterPersistable(new DroppedLootPersistable(Loot));
            saveSystem.RegisterPersistable(Difficulty);
            // 外部审核阻塞项 2 收口：player.vitals 段（存活状态 + 生命值当前值），见
            // PlayerVitalsPersistable 类型注释——放在 world.difficulty 之后、TurnScheduler/rng 之前
            // （10 号文档固定段序此前未列出本段，本次一并补录，见该文档"2026-09-07 勘误"）。
            saveSystem.RegisterPersistable(new PlayerVitalsPersistable(player, Carriers.Rules.Powers));

            // ADR-0013：TurnScheduler 全部状态可存档（见任务书"全部状态可存档"），只在装配了离散
            // 模式（构造函数传入 clockHost）时注册——段名 sim.turn_state 已登记进 10 号文档第 3 节
            // 固定段序（步骤 7b，见该文档 2026-09-05 勘误、TurnScheduler.SectionKeyConst 判断
            // 记录）。W2 收边补齐（A4 审计 F2）：sim.turn_state 与四个世界附属段（步骤 7a）现已
            // 一并登记进 SaveSections.KnownOrder（固定在 7a 之后、rng.stream_states 之前），不再
            // 落入"自定义段"分支按 key 序数排序——此前按序数排序会让 sim.turn_state 实际排在全部
            // 7a 段之前，与文档"7a 后 7b"的文字顺序不完全一致。
            if (TurnScheduler is IPersistable turnSchedulerPersistable)
            {
                saveSystem.RegisterPersistable(turnSchedulerPersistable);
            }

            // P1-03 收口：rng.stream_states（10 §3 步骤 8，全序最末）此前虽已在
            // SaveSections.KnownOrder 登记、RngStreamsPersistable 也已实现，但从未在默认生产装配
            // 里构造并注册——默认 bootstrap 保存的存档因此不含该段，读档后 RngHost 从新的主种子
            // 懒创建流，掉落/命中/proc 等分流随机序列不再是保存点的后续序列，破坏 10 号文档
            // "确定性/回放"前提。现在用同一个构造期传入的 IRngHost 实例（见 Rng 属性）注册。
            saveSystem.RegisterPersistable(new RngStreamsPersistable(Rng));
        }

        /// <summary>
        /// 判断记录（N14 根治，architecture/落地计划/audit-68c9bed-20260907/code-review.md）：旧实现
        /// 只改 <c>entity.MapId</c> 一个字段，不触碰 <see cref="Spawn"/>/<see cref="AreaTrigger"/>/
        /// <see cref="Encounter"/>/<see cref="Loot"/> 等按地图分片登记的状态，也不切换场景资源——
        /// 跨图传送后旧图刷新点/触发器/遭遇仍然"以为自己还装载着"，旧图生成的怪物/物件仍然存在于
        /// <see cref="IWorldSim"/> 里但玩家已经"离开"（逻辑上不可见/不可交互），新图对应状态从未
        /// <see cref="EnterMap"/> 因此完全空白。修复：改走与 <see cref="RestoreFromSlot"/> 跨图分支
        /// 完全一致的统一导航——<see cref="ISceneRouter.LoadScene"/> 内部本就依次触发
        /// <c>pre_unload</c>（调用方已注册指向 <see cref="LeaveMap"/>，见该方法判断记录）→
        /// <see cref="IWorldSim.ClearAll"/> → 装载新场景 → <c>post_load</c>（调用方已注册指向
        /// <see cref="EnterMap"/>）；本方法只需要在发起 <c>LoadScene</c> 之前，把玩家实体要"带"到
        /// 新地图的状态（<c>MapId</c>/位置）提前落到长期存活的 <see cref="PlayerUnit"/> 对象上——同
        /// <see cref="RestoreFromSlot"/> 判断记录"目标地图与当前地图相同时……已经把全部已注册段直接
        /// 写回长期存活的 PlayerUnit/Unit 运行期对象"这一惯例：<c>ClearAll</c> 只是把该对象从
        /// <see cref="IWorldSim"/> 摘掉又在 <c>post_load</c> 钩子里按"缺失则重新 AddEntity"补回去
        /// （见 <c>GameBootstrap.HandlePostLoad</c>），对象本身（含刚设置好的 MapId/位置）全程没有
        /// 被销毁重建。
        /// <para>
        /// 目标传送点解析复用同一个 <see cref="_teleportTargetResolver"/>（构造期第 16 步已构造，
        /// 供 <c>GobjOptions.TeleportResolver</c>/<c>DeathPolicyOptions.ResolveDefaultSpawn</c> 共用）
        /// ——<paramref name="spawnPoint"/> 为空时按 <paramref name="targetMap"/> 走
        /// <see cref="TeleportTargetResolver.Resolve"/> 的"整串即地图 id"两段式解析（gossip
        /// <c>teleport</c> 动作、05/07 文档描述的 <c>teleport_target_ref</c> 就是这个编码规则，
        /// <see cref="DialogCallbacks.TeleportRequestedCallback"/> 判断记录"传送目标的实际执行属于
        /// 05/03 文档"同一处指向）；非空时（<c>AreaTrigger</c> 的 <c>map_transition</c> 已经拆分好
        /// 地图 id 与具体点位 id，不是那种需要猜测段数的组合编码）改用
        /// <see cref="TeleportTargetResolver.ResolveExplicit"/> 按精确 id 匹配。解析失败（地图/点位
        /// 不存在）时不产生任何副作用（不改 MapId、不发起 LoadScene）——同 07
        /// <c>teleporter</c>/<c>GameObjectHost.DoTeleport</c> 解析失败"不产生位移"的既有惯例，只是
        /// 那边有自己的 <see cref="Core.Carriers.Gobj.IGameObjectDiagnostics"/> 记诊断，本装配根没有
        /// 统一诊断汇聚点（同第 16 步判断记录），调用方目前不强制要求这条诊断。
        /// </para>
        /// </summary>
        private void TeleportUnit(Id unitId, Id targetMap, Id? spawnPoint = null)
        {
            var entity = _world.GetEntity(unitId);
            if (entity == null)
            {
                return;
            }

            var resolved = spawnPoint.HasValue
                ? _teleportTargetResolver.ResolveExplicit(targetMap, spawnPoint)
                : _teleportTargetResolver.Resolve(targetMap);
            if (resolved == null)
            {
                return;
            }

            // 判断记录（必须在改 MapId 之前取"传送前所在地图"，同 RestoreFromSlot 同款判断记录）：
            // 下面几行会把 entity.MapId 直接改写成目标地图，若在那之后才比较，比较结果永远相等，
            // "是否需要切场景"分支会被误判为不需要执行而跳过。
            var mapIdBeforeMove = entity.MapId;

            var (resolvedMapId, position) = resolved.Value;
            entity.MapId = resolvedMapId;
            Carriers.Units.SetPosition(unitId, position);

            if (mapIdBeforeMove.Equals(resolvedMapId))
            {
                // 同图内传送（只挪点位、不切地图）：不需要、也不应该发起一次整场景重载——ClearAll
                // 会把全部实体（含正在交互的 NPC/其它玩家单位）一并摧毁再重建，代价与"只是走到同一
                // 张地图的另一个点位"完全不对称。MapId/位置已经落地，到此为止。
                return;
            }

            if (_sceneRouter == null)
            {
                // 未装配场景路由（测试/无场景路由的最小装配）：MapId/位置已经落地，没有场景基础
                // 设施可切，同 RestoreFromSlot 同款判断记录，静默跳过场景切换本身。
                return;
            }

            try
            {
                _sceneRouter.LoadScene(resolvedMapId);
            }
            catch (ArgumentException)
            {
                // 地图 id 未知：同 RestoreFromSlot 同款判断记录，位置/MapId 已经落地，场景切换失败
                // 留给上层诊断/重试。
            }
            catch (InvalidOperationException)
            {
                // 当前应用状态不允许切到 Loading（例如已经在 Loading 中）：同上。
            }
        }

        private double GetCombatStartTime(Id unitId) =>
            _combatStartTimes.TryGetValue(unitId, out var t) ? t : Carriers.Rules.SimTime;

        private void TrackCombatStartTimes()
        {
            _bus.Subscribe<CombatEnteredEvent>(RulesEventKeys.CombatEntered, e => _combatStartTimes[e.UnitId] = Carriers.Rules.SimTime);
        }

        /// <summary>
        /// 判断记录 2（延迟绑定 <see cref="ILootRoller"/>）：<see cref="CarriersAssembly"/> 构造期需要
        /// 一个非空 <see cref="ILootRoller"/> 传给 <see cref="GameObjectHost"/>（chest/gather_node 掉落），
        /// 而真正的 <see cref="LootHost"/> 需要 <see cref="CarriersAssembly.Units"/>/
        /// <see cref="CarriersAssembly.Inventory"/> 才能构造——同 <c>RulesAssembly.DeferredAuraQuery"</c>
        /// 惯例，本代理未绑定时返回空列表（不是抛异常——<see cref="GameObjectHost"/> 在
        /// <see cref="CarriersAssembly"/> 构造期本就可能被其它逻辑间接触碰到 <c>Interact</c>，见该
        /// 类型判断记录"未注入时……"系列的一贯保守取舍：宁可静默退化，不因组装期时序制造异常）。
        /// </summary>
        private sealed class DeferredLootRoller : ILootRoller
        {
            private ILootRoller? _real;

            public void Bind(ILootRoller real) => _real = real ?? throw new ArgumentNullException(nameof(real));

            public IReadOnlyList<Core.Carriers.Common.ItemStack> Roll(Id lootTableId, Id sourceUnitId, Id? killerId) =>
                _real?.Roll(lootTableId, sourceUnitId, killerId) ?? Array.Empty<Core.Carriers.Common.ItemStack>();
        }

        /// <summary>
        /// 判断记录 3（延迟绑定 <see cref="IExprGroupProvider"/>）：<see cref="ExprHostFactory"/>
        /// （第 4 步）需要的 <c>quest</c>/<c>player</c> 分组 provider 依赖尚未构造的
        /// <see cref="QuestHost"/>/<see cref="EconomyHost"/>，用同一惯例延迟绑定；未绑定期间按
        /// 04 第 6.3 节"引用对象暂缺"约定返回 <c>Bool(false)</c>（不影响装配期本身不会真正触发这些
        /// 查询——全部 Expr 求值都发生在装配完成之后）。
        /// </summary>
        private sealed class DeferredExprGroupProvider : IExprGroupProvider
        {
            private IExprGroupProvider? _real;

            public void Bind(IExprGroupProvider real) => _real = real ?? throw new ArgumentNullException(nameof(real));

            public ExprValue Query(string key, IReadOnlyList<ExprValue> args) =>
                _real?.Query(key, args) ?? ExprValue.OfBool(false);
        }

        /// <summary>
        /// 判断记录（<c>economy</c>/<c>spawn</c> 的 <c>Update(dt)</c> 是否为 tick 型）：
        /// <see cref="IEconomyHost.Update"/>（<c>restock_policy=timer</c> 补货倒计时）与
        /// <see cref="ISpawnHost.Update"/>（<c>respawn_policy=timer</c> 刷新倒计时）都需要按秒推进，
        /// 但两个模块都没有自带 <see cref="ITickPhaseHandler"/>（不像 loot/area_trigger/encounter 三个
        /// 模块各自导出了一个）——本类补一个最小适配器，按每个连续步的 <see cref="SimStep.Dt"/> 转发
        /// 给两者，离散步（<see cref="SimStepKind.Discrete"/>）不推进（ADR-0013 离散时间模型现已
        /// 接线，但计时型补货/刷新倒计时按设计只在连续步推进，离散步内维持不变，同
        /// <see cref="LootExpiryTickHandler"/> 惯例）。
        /// </summary>
        private sealed class EconomySpawnUpdateTickHandler : ITickPhaseHandler
        {
            private readonly IEconomyHost _economy;
            private readonly ISpawnHost _spawn;

            public EconomySpawnUpdateTickHandler(IEconomyHost economy, ISpawnHost spawn)
            {
                _economy = economy ?? throw new ArgumentNullException(nameof(economy));
                _spawn = spawn ?? throw new ArgumentNullException(nameof(spawn));
            }

            public void Execute(SimStep step, IWorldSim world)
            {
                if (step.Kind != SimStepKind.Continuous)
                {
                    return;
                }

                _economy.Update(step.Dt);
                _spawn.Update(step.Dt);
            }
        }
    }
}
