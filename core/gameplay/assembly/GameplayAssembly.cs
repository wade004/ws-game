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

        public SubStateId AwaitingInputSubState { get; }

        public SubStateId PlayingBackSubState { get; }

        private readonly IEventBus _bus;
        private readonly IWorldSim _world;
        private readonly ISimClockHost? _clockHost;
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
            Func<Id, IReadOnlyList<Id>>? combatParticipantsResolver = null)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (spatial == null) throw new ArgumentNullException(nameof(spatial));
            SaveSystem = saveSystem ?? throw new ArgumentNullException(nameof(saveSystem));
            PlayerUnitProvider = playerUnitProvider ?? throw new ArgumentNullException(nameof(playerUnitProvider));

            _bus = bus;
            _world = world;

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
            void RequestAutosave(Id unitId) =>
                saveSystem.Save(new SaveRequest(resolvedAutosaveSlotId, resolvedAutosaveTimestampProvider()));

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

            Carriers = new CarriersAssembly(
                bus, registry, rng, world, spatial, navigation, spatialSyncKinds,
                worldFlags: WorldState, lootRoller: deferredLootRoller,
                statOptions: statOptions, combatOptions: combatOptions, skillOptions: skillOptions,
                targetingOptions: targetingOptions, aiOptions: aiOptions, inventoryOptions: inventoryOptions,
                itemOptions: itemOptions, creatureOptions: creatureOptions, summonOptions: summonOptions,
                gobjOptions: resolvedGobjOptions, movementOptions: movementOptions,
                extraSchemas: new IExprSchema[] { GameplaySchemaCatalog.FullExprSchema });

            // ---------------------------------------------------------
            // 3.5) ADR-0013 离散时间模型：TurnScheduler 提前在这里构造（而不是等到第 10 步
            //     AppState 就绪后）——第 4 步 ExprHostFactory 需要把 time.turn_index/round_index/
            //     is_my_turn 三个 Expr key 接到同一个调度器实例上（见 RulesExprHostFactory 判断
            //     记录），先有 TurnScheduler 才能把访问器传给它。TimeModelSwitch（需要 AppState）
            //     仍然留到第 10.5 步再构造，两者共用这里造好的同一个 scheduler 实例。只在调用方
            //     传入 clockHost 时构造（见该构造参数判断记录）。
            // ---------------------------------------------------------
            Core.Foundation.SimLoop.TurnScheduler? scheduler = null;
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
            SkillGranter skillGranter = (unitId, skillId, learn) =>
            {
                if (learn) Carriers.Rules.Skill.LearnSkill(unitId, skillId);
                else Carriers.Rules.Skill.ForgetSkill(unitId, skillId);
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

            AreaTrigger = new AreaTriggerHost(WorldState, bus, ExprHostFactory, Hooks, resolvedAreaTriggerOptions);

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
            var teleportTargetResolver = new TeleportTargetResolver(registry);

            resolvedGobjOptions.DialogOpener ??= (unitId, dialogRef) => Dialog.OpenGossip(unitId, dialogRef, dialogRef);
            resolvedGobjOptions.TeleportResolver ??= teleportTargetResolver.Resolve;
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
        }

        /// <summary>
        /// 一站式进入地图（03 §6 <c>post_load</c> 回调应做的事，见类型注释）：区域触发 + 刷新表
        /// 按地图装载，经济按地图补货。调用方（场景路由的 <c>post_load</c> 钩子，或测试直接调用）
        /// 在切换到新地图后调用一次。
        /// </summary>
        public void EnterMap(Id mapId, Id playerUnitId)
        {
            AreaTrigger.LoadForMap(mapId, Carriers.Rules.Registry);
            Spawn.ApplyForMap(mapId);
            Economy.OnMapEnter(mapId);
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
                _clockHost.Advance(realDeltaSeconds);
                return;
            }

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

                _world.Tick(step.Value);
                TurnScheduler.NotifyStepConsumed(step.Value.ActorId!.Value);

                if (Pacing!.Mode() == PacingMode.WaitForPlayback)
                {
                    if (Pacing is WaitForPlaybackPacingPolicy waitForPlayback)
                    {
                        waitForPlayback.BeginStep();
                    }

                    PushSubStateIfNotCurrent(PlayingBackSubState);
                    return;
                }
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
        /// <paramref name="saveSystem"/>：world_state → player.inventory/equipment（按
        /// <paramref name="player"/>）→ currencies → quest → achievement → spawn_state →
        /// dropped_loot → world.difficulty → rng.stream_states。<see cref="RngHost"/> 段放最后
        /// （10 §3 步骤 8，全序最末）。
        /// <para>
        /// 判断记录（progression 段缺席）：<c>core/numbers/progression.ProgressionHost</c> 未实现
        /// <see cref="IPersistable"/>（阶段 3 集成收尾勘察确认，见任务汇报"契约缺口"），10 §2.2
        /// <c>player.progression</c> 段因此暂无持久化实现可挂——本方法不假装注册一个不存在的
        /// 段，如实跳过并在此记录；真正需要落盘等级/经验时，需要先在
        /// <c>core/numbers/progression</c> 补一个 <c>IPersistable</c> 实现（不在本任务允许改动范围）。
        /// </para>
        /// </summary>
        public void RegisterPersistables(ISaveSystem saveSystem, PlayerUnit player)
        {
            if (saveSystem == null) throw new ArgumentNullException(nameof(saveSystem));
            if (player == null) throw new ArgumentNullException(nameof(player));

            saveSystem.RegisterPersistable(WorldState);
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

            // ADR-0013：TurnScheduler 全部状态可存档（见任务书"全部状态可存档"），只在装配了离散
            // 模式（构造函数传入 clockHost）时注册——段名 sim.turn_state 已登记进 10 号文档第 3 节
            // 固定段序（步骤 7b，见该文档 2026-09-05 勘误、TurnScheduler.SectionKeyConst 判断
            // 记录），但同世界附属段一样不登记进 SaveSections.KnownOrder，只是"自定义段"（按 key
            // 序数排在已知段之后，见 SaveSections 注释）。
            if (TurnScheduler is IPersistable turnSchedulerPersistable)
            {
                saveSystem.RegisterPersistable(turnSchedulerPersistable);
            }
        }

        private void TeleportUnit(Id unitId, Id targetMap, Id? spawnPoint = null)
        {
            var entity = _world.GetEntity(unitId);
            if (entity == null)
            {
                return;
            }

            entity.MapId = targetMap;
            // 判断记录：spawn_point 的具体落点解析不在本装配根范围（见 AreaTriggerOptions.MapTransitionRequested
            // 判断记录"具体传送到哪个出生点，由组装层结合 post_load 钩子完成实际落点"）——本方法只
            // 落地"切换地图"本身，位置保持不变（若调用方需要精确落点，应在 spawnPoint 非空时另行
            // 查询 world.map.spawn_points 并调用 Carriers.Units.SetPosition，本类不越权代劳）。
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
        /// 给两者，离散步（<see cref="SimStepKind.Discrete"/>）不推进（本项目未启用离散模式，
        /// ADR-0013，同 <see cref="LootExpiryTickHandler"/> 惯例）。
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
