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

        /// <summary>数据集加载/世界装配阶段出现阻断性错误时为真；为真时 <see cref="Update"/>/
        /// <see cref="FixedUpdate"/> 不做任何事，已经 <c>Debug.LogError</c> 过具体原因。</summary>
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
            var viewFactory = new UnityViewFactory(_host.Renderer2D, new RenderConventionHost(), viewFactoryDisplayInfo, _host.ResourceLoader);
            ViewFactory = viewFactory;

            var presentationRng = new RngHost(_options.Seed ^ 0x9E3779B97F4A7C15UL);
            var sceneRouter = new Core.Foundation.SceneRouter.SceneRouter(
                registry, _host.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, _bus,
                spatial: _host.SpatialQuery, navigation: _host.Navigation2D);

            var presentationOptions = BuildPresentationOptions();

            var presentation = new PresentationAssembly(
                gameplay, world, registry, _bus, presentationRng,
                viewFactory, _host.Renderer2D, _host.Camera, _host.Audio, _host.FileSystem, sceneRouter,
                presentationOptions);
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
            RenderOptions = _options.BuildRenderOptions(),
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

            Gameplay.EnterMap(mapId, PlayerId);

            if (!_worldEverEntered)
            {
                _bus.DispatchPending();
                _worldEverEntered = true;
            }
        }

        private void FixedUpdate()
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

            _interpAccumulator = 0.0;
            Presentation.InputMap.Update(_host.Input);
            var moveAxis = Presentation.InputMap.GetActionAxis("input.action.move");
            if (moveAxis.SqrLength > 0.0001)
            {
                Gameplay.Carriers.Movement.Request(MoveRequest.InDirection(PlayerId, moveAxis));
            }
            World.Tick(SimStep.Continuous(Time.fixedDeltaTime));
        }

        private void Update()
        {
            if (BootstrapFailed || Presentation == null)
            {
                return;
            }

            Presentation.Shell.Update();

            var state = Gameplay.AppState.GetState();
            var renderTicking = state == Core.Foundation.AppLifecycle.AppState.InWorld ||
                                 state == Core.Foundation.AppLifecycle.AppState.Pause;
            if (!renderTicking)
            {
                return;
            }

            var unscaledDelta = Time.unscaledDeltaTime;
            _interpAccumulator += unscaledDelta;
            var alpha = Mathf.Clamp01((float)(_interpAccumulator / Math.Max(Time.fixedDeltaTime, 0.0001f)));

            if (!Freeze.IsFrozen)
            {
                Presentation.ViewBinder.SyncAll(alpha);
                Presentation.Camera.Update(alpha);
            }

            FloatingText.Tick(unscaledDelta);
            Freeze.Tick(unscaledDelta);
            Flash.Tick(unscaledDelta);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            Presentation?.Dispose();
            ViewFactory?.DestroyAllCreatedViews();
        }
    }
}
