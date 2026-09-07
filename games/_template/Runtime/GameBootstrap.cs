#nullable enable
// GameBootstrap：游戏层组合根（对应 adapters/unity 工作台的 Adapter.Unity.Shell.FrameworkResidentHost
// 在"新游戏起步模板"里的等价物）。见 architecture/13_新游戏接入指南.md 第 1 节步骤 2"组装模块与
// 策略配置"、第 4 节口味配置项清单（本类型经 GameOptions 消费）。
//
// 判断记录（为什么不直接复用 Adapter.Unity.Shell.FrameworkResidentHost/ShellRoot）：
// FrameworkResidentHost 是工作台自己的灰盒/Shell 场景组合根，数据根（"data/_sample"）、地图 id、
// 玩家模板、职业、阵营等全部是编译期常量，且 Adapter.Unity.Shell.ShellRoot.Awake() 内部固定调用
// FrameworkResidentHost.Ensure()——两者都没有把"用什么配置组装"这件事做成可注入点，无法在不改
// adapters/unity 包代码的前提下用 GameOptions 参数化复用（本任务允许改动的 adapters/unity 文件
// 仅 GameFoundationBootstrap.cs/FrameworkResidentHost.cs 的数据根列表，不含 ShellRoot.cs）。本类型
// 因此是一个新的、独立的组合根，按同一套装配顺序（见 presentation/assembly/README.md、
// core/gameplay/tests/EndToEnd/GameWorldFixture.cs）重新组装，但全部内容 id 与策略配置改由
// GameOptions 提供，不出现任何硬编码的具体游戏内容。
//
// 判断记录（引擎侧收口任务，固定步驱动改走 IClock.RequestFixedStep + GameplayAssembly.Advance，
// 顺带修复此前 GameOptions.PacingWaitForPlayback 的死配置项缺口）：本类型此前把 pacingPolicy
// 传给 GameplayAssembly 构造函数，却从未传 clockHost——GameplayAssembly 只有 clockHost 非空时才会
// 装配 Pacing/TurnScheduler/TimeModelSwitch（见该构造函数第 10.5 步"if (clockHost != null &&
// scheduler != null)"），传了 pacingPolicy 没传 clockHost 等于这份配置被静默丢弃，
// GameOptions.PacingWaitForPlayback 开关此前对任何行为都没有影响。本次一并补上 clockHost（固定
// 步长取 host.Clock.RequestFixedStep 注册用的同一个 Time.fixedDeltaTime），combatParticipantsResolver
// 用默认值（不传 = null，走 TimeModelSwitch 内建的"按半径+阵营解析"真实逻辑，不像
// Adapter.Unity.Shell.FrameworkResidentHost/Bootstrap.GameFoundationBootstrap 那样恒返回空列表
// 神经化——模板自带的最小数据集 games/_template/data/game/found/found.time_model.json 的 combat
// 行是 mode=continuous，见该数据文件，即便真实参与者解析生效也不会触发离散模式切换，不需要那
// 两个工作台场景那样的规避）。
using System;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Presentation.Assembly;
using Presentation.Render;
using UnityEngine;

namespace Game.Template
{
    /// <summary>游戏层组合根：见文件顶部注释。全局唯一，<see cref="Ensure"/> 幂等获取，
    /// 场景切换期间常驻（<c>DontDestroyOnLoad</c>）。</summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        private static GameBootstrap? _instance;

        [Header("数据根（Inspector 可配置，默认指向框架分发包的框架级数据表 + 本游戏自己的数据）")]
        // 判断记录：见 data/README.md"两类目录"一节——data/_framework 是分发包 dist/<version>/data/_framework
        // 随 Adapter.Unity 包一起同步进 StreamingAssets 的框架级数据表（found.event_catalog/
        // found.input_action 等，行被框架代码硬引用，见该 README 判定规则）；本游戏自己的数据放
        // data/<game>（复制模板时把 "game" 改成自己的游戏目录名，并同步这里的默认值）。两者按
        // DataRegistry.LoadAll(IReadOnlyList<IDataSource>) 合并加载，同名表按主键并集合并，见
        // core/foundation/data_registry/README.md"多根加载"一节。
        [SerializeField] private string _frameworkDatasetRoot = "data/_framework";
        [SerializeField] private string _gameDatasetRoot = "data/game";

        [Header("口味配置（architecture/13_新游戏接入指南.md 第 4 节）")]
        [SerializeField] private GameOptions _options = new GameOptions();

        public GameOptions Options => _options;

        public GameplayAssembly Gameplay { get; private set; } = null!;
        public PresentationAssembly Presentation { get; private set; } = null!;
        public IWorldSim World { get; private set; } = null!;
        public Id PlayerId { get; private set; }
        public UnityViewFactory ViewFactory { get; private set; } = null!;
        public FloatingTextReceiver FloatingText { get; private set; } = null!;
        public FreezeFrameReceiver Freeze { get; private set; } = null!;
        public FlashReceiver Flash { get; private set; } = null!;

        /// <summary>W6-B 新增：本次装配的命中帧同步注册表（见
        /// <c>Presentation.FeedbackBinder.Contracts.IHitFrameSource</c> 类型注释、ADR-0017 决策 d），
        /// 传给 <see cref="ViewFactory"/> 使 sprite/model 两条路线的 View 创建/销毁都自动登记/注销。
        /// W6 收口：<c>PresentationAssembly</c> 已补齐 <c>hitFrameSource</c> 构造参数（见
        /// <see cref="Bootstrap"/> 构造点），本实例已一并接给内部 <c>FeedbackBinderCore</c>——是否
        /// 真正生效由 <see cref="GameOptions.HitFrameSyncEnabled"/> 开关决定（默认 false，
        /// 行为与改动前一致）。</summary>
        public global::Presentation.FeedbackBinder.Core.CharacterRigHitFrameSource? HitFrameSource { get; private set; }

        /// <summary>数据集加载/世界装配阶段出现阻断性错误时为真；为真时不注册
        /// <see cref="OnFixedStep"/>/<see cref="OnFrameTick"/>，已经 <c>Debug.LogError</c> 过具体
        /// 原因。</summary>
        public bool BootstrapFailed { get; private set; }

        /// <summary>本次加载完成后的校验报告（不阻断时也保留，供场景内调试面板/日志核对，见
        /// <c>data/README.md</c>"与校验器的关系"一节）。</summary>
        public ValidationReport? LoadReport { get; private set; }

        public IDataRegistryView Registry => _registry;

        public UnityEngineHost Host => _host;

        private UnityEngineHost _host = null!;
        private IEventBus _bus = null!;
        private IDataRegistryView _registry = null!;
        private PlayerUnit _player = null!;
        private Id _classId;
        private bool _worldEverEntered;
        private double _interpAccumulator;

        /// <summary>W6-B 新增：见 <see cref="Bootstrap"/>（或对应装配方法）构造点判断记录，
        /// <see cref="OnDestroy"/> 里显式 Dispose（退订 item.equipped/item.unequipped）。</summary>
        private global::Presentation.VfxSfx.Core.EquipmentWeaponStyleSource? _weaponStyleSource;

        /// <summary>PR130-07 根治新增：默认 factory 的 equipVisual 映射入口（见
        /// <see cref="global::Presentation.Render.EquipmentVisualSource"/> 类型判断记录），
        /// <see cref="OnDestroy"/> 里与 <see cref="_weaponStyleSource"/> 一并 Dispose。</summary>
        private global::Presentation.Render.EquipmentVisualSource? _equipVisualSource;

        /// <summary>W6 收口新增：见 <see cref="Bootstrap"/> 构造点判断记录——同一个 <c>RenderOptions</c>
        /// 实例既传给 <see cref="ViewFactory"/> 又传给 <see cref="BuildPresentationOptions"/> 内部的
        /// <c>PresentationAssemblyOptions.RenderOptions</c>，避免两处各自独立构造、取值不一致。</summary>
        private Presentation.Render.RenderOptions? _renderOptions;

        /// <summary>见文件顶部判断记录：固定步/帧回调改经 IClock 注册，本类型持有返回的句柄，
        /// <see cref="OnDestroy"/> 里显式退订。</summary>
        private Core.Foundation.Common.SubscriptionHandle? _fixedStepHandle;
        private Core.Foundation.Common.SubscriptionHandle? _frameHandle;

        public static GameBootstrap Ensure()
        {
            if (_instance != null)
            {
                return _instance;
            }

            var existing = FindFirstObjectByType<GameBootstrap>();
            if (existing != null)
            {
                _instance = existing;
                return existing;
            }

            var go = new GameObject("GameBootstrap");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameBootstrap>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            try
            {
                Bootstrap();
            }
            catch (Exception ex)
            {
                BootstrapFailed = true;
                Debug.LogError($"[GameBootstrap] 世界装配失败，已停止：{ex}");
            }
        }

        private void Bootstrap()
        {
            _host = UnityEngineHost.Ensure();

            // 1) 事件总线：全部事件 key 取自生成的 EventKeys.g.cs（由 found.event_catalog 生成，
            //    见 toolchain/gen_event_constants.py），惯例同 FrameworkResidentHost.Bootstrap。
            var definitions = Core.Foundation.EventBus.EventKeys.All
                .Select(k => new EventDefinition(k, k.Domain, Array.Empty<string>()))
                .ToList();
            var catalog = EventCatalog.FromDefinitions(definitions);
            _bus = new Core.Foundation.EventBus.EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            // 2) 数据集：框架级数据根 + 本游戏数据根，两根合并加载（见类型顶部判断记录）。
            var contentFs = new UnityFileSystem(readOnlyContentMode: true);
            var frameworkSource = new FileSystemDataSource(contentFs, _frameworkDatasetRoot);
            var gameSource = new FileSystemDataSource(contentFs, _gameDatasetRoot);
            var options = PresentationSchemaCatalog.CreateOptions();
            // FailOnUnknownTable=false：模板只登记最小可玩闭环所需的表 schema（经
            // PresentationSchemaCatalog 级联登记的 L0～L5 全部表），本游戏数据集里若暂时还没有
            // 某些表（如尚未接入表现层前的 vfx.def/sfx.def），不应阻断装配，惯例同
            // FrameworkResidentHost/GameFoundationBootstrap。
            options.FailOnUnknownTable = false;

            var registry = new DataRegistry(gameSource, _bus, options);
            _registry = registry;
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll(new IDataSource[] { frameworkSource, gameSource });
            LoadReport = report;
            if (report.IsBlocking)
            {
                BootstrapFailed = true;
                Debug.LogError("[GameBootstrap] 数据集校验未通过，已停止：" + string.Join("; ", report.Issues));
                return;
            }

            var rng = new RngHost(_options.Seed);
            var world = new WorldSim(_bus);
            World = world;

            PlayerId = new Id(_options.PlayerUnitId);
            var factionId = new Id(_options.PlayerFactionId);
            _classId = new Id(_options.PlayerClassId);

            var saveSystem = new SaveSystem(_host.FileSystem, BuildSaveSystemOptions(), _bus);

            // 02 §1.2 固定步契约：clockHost 交给 host.Clock.RequestFixedStep 注册的回调（见
            // BuildWorld 末尾）驱动，stepSeconds 与该注册用的 Time.fixedDeltaTime 取同一个值（误差
            // 为零）。见文件顶部判断记录：此前只传 pacingPolicy 不传 clockHost 是一处死配置。
            var clockHost = new SimClockHost(world, new SimLoopOptions { StepSeconds = Time.fixedDeltaTime });

            var gameplay = new GameplayAssembly(
                _bus, registry, rng, world, _host.SpatialQuery, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: factionId,
                navigation: _host.Navigation2D,
                combatOptions: _options.BuildCombatOptions(),
                skillOptions: _options.BuildSkillOptions(),
                targetingOptions: _options.BuildTargetingOptions(),
                movementOptions: _options.BuildMovementOptions(),
                lootOptions: _options.BuildLootOptions(),
                clockHost: clockHost,
                pacingPolicy: _options.PacingWaitForPlayback ? new WaitForPlaybackPacingPolicy() : new ImmediatePacingPolicy());
            Gameplay = gameplay;

            _player = new PlayerUnit(PlayerId, new Id(_options.StartMapId), factionId, _classId)
            {
                Position = Vec2.Zero,
                TemplateId = new Id(_options.PlayerTemplateId),
            };

            // 判断记录（同 FrameworkResidentHost 同名判断记录）：RegisterUnit/Economy.RegisterUnit
            // 必须在 PresentationAssembly 构造之前完成——其构造函数会立即建出全部 UI 视图模型，
            // 构造期同步 Refresh() 会经 ProgressionHost.GetLevel 等要求该 unitId 已注册，否则抛
            // ArgumentException。
            Gameplay.Carriers.Rules.RegisterUnit(PlayerId, _classId, raceId: null, level: _options.PlayerLevel);
            Gameplay.Economy.RegisterUnit(PlayerId);

            var viewFactoryDisplayInfo = new DisplayInfoRegistry(registry, _bus);

            // W6-B 新增：命中帧同步注册表（ADR-0017 决策 d）+ 武器风格来源（ADR-0017 决策 e），
            // 主手槽位 id 取 13 §4 新增口味配置项 GameOptions.MainHandSlotId（见该字段注释）。两者都是
            // "框架提供机制、游戏层按需接线"的可选能力（见 W6-A 判断记录 5），本模板按默认约定接上。
            HitFrameSource = new global::Presentation.FeedbackBinder.Core.CharacterRigHitFrameSource();
            var mainHandSlotId = new Id(_options.MainHandSlotId);
            global::Presentation.VfxSfx.Contracts.MainHandWeaponTemplateResolver mainHandResolver = unitId =>
                gameplay.Carriers.Equipment.GetAllEquippedInstances(unitId).TryGetValue(mainHandSlotId, out var instance)
                    ? instance.TemplateId
                    : (Id?)null;
            _weaponStyleSource = new global::Presentation.VfxSfx.Core.EquipmentWeaponStyleSource(_bus, mainHandResolver, viewFactoryDisplayInfo);

            // PR130-07 根治：默认 factory 的 equipVisual 映射入口——按 display.equip_visual 全表
            // item_id（物品模板 id）建目录，供 EquipmentVisualSource 在 item.equipped 时反查（见该
            // 类型判断记录），与 _weaponStyleSource 同一批构造、同一批 Dispose。
            var equipVisualCatalog = new System.Collections.Generic.Dictionary<Id, global::Presentation.Render.EquipVisualDef>();
            foreach (var record in registry.GetAll("display.equip_visual"))
            {
                var def = global::Presentation.Render.EquipVisualDef.FromRecord(record);
                equipVisualCatalog[def.ItemId] = def;
            }
            _equipVisualSource = new global::Presentation.Render.EquipmentVisualSource(_bus, equipVisualCatalog);

            // W6 收口（ADR-0017 决策 d 遗留缺口收口）：_renderOptions 只构造一次、同一个实例分别传给
            // 下面的 UnityViewFactory（决定 SpriteCharacterRig/ModelCharacterRig 构造期实际拿到的
            // HitFrameSync 策略，见该工厂 _renderOptions 字段判断记录）与 BuildPresentationOptions()
            // 内部的 PresentationAssemblyOptions.RenderOptions——此前两处各自独立构造，即便
            // GameOptions.HitFrameSyncEnabled 已经开启，rig 构造期实际拿到的仍是默认 LogicDriven
            // （比 PresentationAssembly 未暴露 hitFrameSource 更深一层的接线缺口，一并收口）。
            _renderOptions = _options.BuildRenderOptions();

            // 外部审核阻塞项 3 收口（architecture/落地计划/audit-20260907/followup-2026-09-07.md
            // "外部审核阻塞项处理"一节）：传入 bus/registry 两个可选参数，使 UnityViewFactory 默认
            // 给"生物"型 sprite/model 视图挂接默认动画接线（见该类型 AttachDefaultAnimation/
            // AttachDefaultModelAnimation 判断记录），本模板不再需要自己另外接一遍。W6-B 追加传入
            // _host.Renderer3D/HitFrameSource/_weaponStyleSource 三个新可选参数。W6 收口追加传入
            // _renderOptions（见上方判断记录）。
            var viewFactory = new UnityViewFactory(
                _host.Renderer2D, new RenderConventionHost(), viewFactoryDisplayInfo, _host.ResourceLoader,
                bus: _bus, dataRegistry: registry,
                renderer3D: _host.Renderer3D, hitFrameSource: HitFrameSource, weaponStyleSource: _weaponStyleSource,
                renderOptions: _renderOptions,
                equipVisualByItemInstanceId: _equipVisualSource.VisualByItemInstanceId);
            ViewFactory = viewFactory;

            var presentationRng = new RngHost(_options.Seed ^ 0x9E3779B97F4A7C15UL);
            var sceneRouter = new Core.Foundation.SceneRouter.SceneRouter(
                registry, _host.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, _bus,
                spatial: _host.SpatialQuery, navigation: _host.Navigation2D);
            // 外部审核阻塞项 2 收口（见 GameplayAssembly._sceneRouter 字段判断记录）：真实
            // SceneRouter 必然晚于 GameplayAssembly 构造完成（需要 gameplay.AppState/gameplay.Hooks），
            // 回填给 GameplayAssembly.RestoreFromSlot 使用，使 death.reload_save 策略读档后能在
            // 目标地图与当前地图不同时真正切场景。
            gameplay.AttachSceneRouter(sceneRouter);

            var presentationOptions = BuildPresentationOptions();

            // GP-PRES-04 收口：传入 _host.ResourceLoader（与上面 viewFactory/sceneRouter 同一份
            // 宿主资源加载器），让 VfxPlayer/SfxPlayer 也能在首次引用反馈资源时触发
            // IResourceLoader.LoadAsync（见 PresentationAssembly 构造函数判断记录）——此前本处未传，
            // 播放器拿到的是 null。
            // W6 收口（ADR-0017 决策 d 遗留缺口收口）：PresentationAssembly 新增 hitFrameSource
            // 构造参数，把上面已经构造好的 HitFrameSource（rig 登记表）接给内部 FeedbackBinderCore——
            // 是否真正生效仍由 presentationOptions.RenderOptions/FeedbackOptions 的 HitFrameSync
            // 开关决定（GameOptions.HitFrameSyncEnabled，见 BuildRenderOptions/BuildFeedbackOptions），
            // 本参数只负责接线，不传时（开关为 false）行为与改动前完全一致。
            // PR130-06 根治：此前只把 _host.Renderer3D 传给了上面的 viewFactory，PresentationAssembly/
            // 内部的 VfxPlayer 一直拿到 null 的 modelHandleResolver 支撑（renderer3D 参数本身
            // PresentationAssembly 早已声明为可选构造参数，见该类型判断记录"缺口 13"，只是三处装配根
            // 都从未真正传过），socket 特效因此固定降级 world 坐标（见 VfxPlayer.PlaySocket 判断
            // 记录）。三处装配根现在共享同一个 _host.Renderer3D 实例。
            var presentation = new PresentationAssembly(
                gameplay, world, registry, _bus, presentationRng,
                viewFactory, _host.Renderer2D, _host.Camera, _host.Audio, _host.FileSystem, sceneRouter,
                presentationOptions, resourceLoader: _host.ResourceLoader, hitFrameSource: HitFrameSource,
                renderer3D: _host.Renderer3D);
            Presentation = presentation;

            FloatingText = new FloatingTextReceiver(_host.transform, id => world.GetEntity(id)?.Position, presentation.FloatingTextStyles);
            Freeze = new FreezeFrameReceiver();
            Flash = new FlashReceiver(presentation.ViewBinder);

            gameplay.RegisterPersistables(presentation.SaveSystem, _player);

            sceneRouter.RegisterPreUnloadHook(HandlePreUnload);
            sceneRouter.RegisterPostLoadHook(HandlePostLoad);

            // 动作声明整批读自 found.input_action 表已加载的全部行（框架级数据表，见
            // data/README.md 判定规则），不在代码内补充/绕开，惯例同 FrameworkResidentHost/
            // GameFoundationBootstrap。
            var inputActionRecords = registry.GetAll("found.input_action");
            var inputActionDefinitions = new Core.Foundation.InputMap.ActionDefinition[inputActionRecords.Count];
            for (var i = 0; i < inputActionRecords.Count; i++)
            {
                inputActionDefinitions[i] = Core.Foundation.InputMap.ActionDefinition.FromRecord(inputActionRecords[i]);
            }
            presentation.InputMap.DeclareActionSet(new Id("actionset.game_template"), inputActionDefinitions);

            // ADR-0013 §9："表现层发出 presentation.playback_finished 后由调用方转发到
            // GameplayAssembly.NotifyPlaybackFinished()"——WaitForPlaybackPacingPolicy 本身不持有
            // IEventBus、不会自行订阅，这一步接线只能由引擎侧完成，游戏层复制本模板后不需要再补。
            _bus.Subscribe(Core.Foundation.EventBus.EventKeys.PresentationPlaybackFinished,
                _ => Gameplay.NotifyPlaybackFinished());

            // 固定步/帧回调改经 IClock 注册（见文件顶部判断记录）：Bootstrap() 全局只执行一次
            // （Ensure() 幂等单例），这两个句柄因此也只注册一次。
            _fixedStepHandle = _host.Clock.RequestFixedStep(Time.fixedDeltaTime, OnFixedStep);
            _frameHandle = _host.Clock.OnFrame(OnFrameTick);
        }

        private SaveSystemOptions BuildSaveSystemOptions()
        {
            var saveOptions = new SaveSystemOptions(new Id(_options.GameId))
            {
                AutoSave = new AutoSavePolicy
                {
                    OnSavePoint = _options.AutoSaveOnSavePoint,
                    OnMapSwitch = _options.AutoSaveOnMapSwitch,
                    OnQuestComplete = _options.AutoSaveOnQuestComplete,
                },
            };
            return saveOptions;
        }

        private PresentationAssemblyOptions BuildPresentationOptions() => new PresentationAssemblyOptions
        {
            OnFloatingText = (entityId, styleId, text) => FloatingText?.Show(entityId, styleId, text),
            OnFreeze = durationMs => Freeze?.Freeze(durationMs / 1000.0),
            OnFlash = (entityId, profileId) => Flash?.Show(entityId, profileId),
            ActionBarSlotCountFallback = _options.ActiveSkillSlotCount,
            HudPowerTypes = new[] { Core.Rules.Common.WellKnownPowers.Health },
            EquipmentSlotIds = _options.BuildEquipmentSlotIds(),
            // W6 收口：复用 Bootstrap() 里已经构造并传给 ViewFactory 的同一个 _renderOptions 实例
            // （见该字段判断记录），不再另外调用一次 _options.BuildRenderOptions()——两次调用会各自
            // 产生独立的 RenderOptions 对象，即便字段取值相同也不是"同一份"，与 UnityViewFactory
            // 侧的 rig 构造脱节。
            RenderOptions = _renderOptions,
            FeedbackOptions = _options.BuildFeedbackOptions(),
            NewGameStarter = new SampleNewGameStarter(this).Start,
        };

        /// <summary>发起一局新游戏（<see cref="Presentation.Shell.ShellHost.NewGame"/> 的薄封装，
        /// 供模板自带的最小主菜单 UI 调用；<paramref name="difficultyId"/> 省略时用
        /// <see cref="GameOptions.DefaultDifficultyId"/>）。</summary>
        public bool RequestNewGame(Id slotId, Id? difficultyId = null, Id? archetypeId = null) =>
            Presentation.Shell.NewGame(slotId, difficultyId ?? new Id(_options.DefaultDifficultyId), archetypeId);

        /// <summary>供 <see cref="SampleNewGameStarter"/> 重置玩家到新游戏起始状态时使用。</summary>
        internal PlayerUnit PlayerUnit => _player;

        internal GameOptions RuntimeOptions => _options;

        private void HandlePreUnload(Id mapId)
        {
            ViewFactory.DestroyAllCreatedViews();
            Gameplay.LeaveMap(mapId);
            _worldEverEntered = false;
        }

        private void HandlePostLoad(Id mapId)
        {
            if (World.GetEntity(PlayerId) == null)
            {
                World.AddEntity(_player);
                _bus.DispatchPending();
            }

            // GP-PRES-01 收口（architecture/落地计划/audit-20260907/gameplay-presentation.md）：
            // 读档恢复的地面掉落物在 ISceneRouter.LoadScene 触发 IWorldSim.ClearAll 时被一并清空
            // （LootHost 自己的跟踪表不受 ClearAll 影响，但 IWorldSim 侧的实体已经不在了，见
            // LootHost.RestoreDropped 判断记录）——同上面玩家实体的"缺失才重新添加"惯例，需要在
            // 场景真正就绪之后把属于当前地图的掉落物重新接回 IWorldSim。
            //
            // 外部审核阻塞项 1 收口（architecture/落地计划/audit-20260907/followup-2026-09-07.md
            // "外部审核阻塞项处理"一节）：此前这一步在本类型（模板专属的 post_load 钩子）里手工
            // 调用 Gameplay.Loot.ReattachToWorld(mapId)，是"只接进了模板"的那一半缺口——框架自身
            // 的 Shell 读档入口（Presentation.Shell.ShellHost.LoadGame）没有任何等价接线，读档后
            // 掉落物在纯框架（不经本模板）场景下仍然会随场景切换静默消失。现改为
            // GameplayAssembly.EnterMap 自身第一步就调用 Loot.ReattachToWorld（见该方法判断记录），
            // 本类型不再重复接线，直接调用 EnterMap 即可获得同样的效果——幂等（同上，
            // LootHost.ReattachToWorld 对"已经在世界里的实体"是安全的空操作），可安全每次
            // post-load 都调用，不需要额外的 DispatchPending。
            Gameplay.EnterMap(mapId, PlayerId);

            if (!_worldEverEntered)
            {
                _bus.DispatchPending();
                _worldEverEntered = true;
            }
        }

        /// <summary>由 <see cref="_fixedStepHandle"/>（<c>host.Clock.RequestFixedStep</c>）驱动，
        /// 取代此前的 MonoBehaviour FixedUpdate（见文件顶部判断记录）。</summary>
        private void OnFixedStep(double stepSeconds)
        {
            if (BootstrapFailed || Presentation == null)
            {
                return;
            }

            var state = Gameplay.AppState.GetState();
            if (state != Core.Foundation.AppLifecycle.AppState.InWorld)
            {
                return;
            }

            if (Gameplay.Pacing is WaitForPlaybackPacingPolicy waitForPlayback && !waitForPlayback.IsPlaybackFinished)
            {
                return;
            }

            _interpAccumulator = 0.0;
            Presentation.InputMap.Update(_host.Input);
            var moveAxis = Presentation.InputMap.GetActionAxis("input.action.move");
            if (moveAxis.SqrLength > 0.0001)
            {
                Gameplay.Carriers.Movement.Request(MoveRequest.InDirection(PlayerId, moveAxis));
            }
            Gameplay.Advance(stepSeconds);
        }

        /// <summary>由 <see cref="_frameHandle"/>（<c>host.Clock.OnFrame</c>）驱动，取代此前的
        /// MonoBehaviour Update（见文件顶部判断记录）。</summary>
        private void OnFrameTick(double unscaledDelta)
        {
            if (BootstrapFailed || Presentation == null)
            {
                return;
            }

            Presentation.Shell.Update();

            var state = Gameplay.AppState.GetState();
            var renderTicking = state == Core.Foundation.AppLifecycle.AppState.InWorld ||
                                 state == Core.Foundation.AppLifecycle.AppState.Pause;

            // 见 Adapter.Unity.Shell.FrameworkResidentHost.OnFrameTick 同款判断记录：播放队列的
            // 推进不受 renderTicking 门槛限制。
            Presentation.Feedback.Update(unscaledDelta);

            // GP-03 根治（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：同上，
            // VfxPlayer.Update(dt) 也不受 renderTicking 门槛限制——已在播放的循环特效应当继续按
            // lifetime 正常停止、首次异步加载的超时倒计时应当继续推进，不因为当前不在 InWorld/
            // Pause 就卡住（同 FrameworkResidentHost.OnFrameTick 同款判断记录）。
            Presentation.Vfx.Update(unscaledDelta);

            if (!renderTicking)
            {
                return;
            }

            _interpAccumulator += unscaledDelta;
            var alpha = Mathf.Clamp01((float)(_interpAccumulator / Math.Max(Time.fixedDeltaTime, 0.0001f)));

            if (!Freeze.IsFrozen)
            {
                Presentation.ViewBinder.SyncAll(alpha);
                Presentation.Camera.Update(alpha);
            }

            // GP-10 根治：同 FrameworkResidentHost.OnFrameTick 同款判断记录（拍板 6）——推进每个
            // 仍存活的 sprite 型 View 持有的 ICharacterRig.ProceduralAnim 时间轴，此前本模板也漏了
            // 这一步。
            AdvanceCharacterRigs();

            FloatingText.Tick((float)unscaledDelta);
            Freeze.Tick(unscaledDelta);
            Flash.Tick(unscaledDelta);
        }

        /// <summary>GP-10 根治新增：同 <see cref="Adapter.Unity.Shell.FrameworkResidentHost"/>
        /// 同名方法。</summary>
        private void AdvanceCharacterRigs()
        {
            var views = ViewFactory.CreatedViews;
            for (var i = 0; i < views.Count; i++)
            {
                if (views[i].IsAlive && views[i] is Presentation.Render.IHasCharacterRig hasRig)
                {
                    hasRig.Rig.Update(Time.deltaTime);
                }
            }
        }

        private void OnDestroy()
        {
            _fixedStepHandle?.Dispose();
            _fixedStepHandle = null;
            _frameHandle?.Dispose();
            _frameHandle = null;

            if (_instance == this)
            {
                _instance = null;
            }
            Presentation?.Dispose();
            ViewFactory?.DestroyAllCreatedViews();

            // W6-B 新增：退订 EquipmentWeaponStyleSource 的 item.equipped/item.unequipped 订阅。
            _weaponStyleSource?.Dispose();
            _weaponStyleSource = null;

            // PR130-07 根治：同批退订 EquipmentVisualSource 的 item.added/item.equipped/item.unequipped 订阅。
            _equipVisualSource?.Dispose();
            _equipVisualSource = null;
        }
    }
}
