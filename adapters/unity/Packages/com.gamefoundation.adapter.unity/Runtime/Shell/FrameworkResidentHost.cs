#nullable enable
// FrameworkResidentHost：U3-2"框架常驻部分"（任务书"GameFoundationBootstrap 拆分为'框架常驻部分'
// （数据集、装配根，DontDestroyOnLoad）与'进入地图部分'"）。
//
// 判断记录（为什么新增一个类型，不直接改造 Adapter.Unity.Bootstrap.GameFoundationBootstrap）：
// 后者是 U2-4 灰盒场景 GreyBox.unity 唯一挂载的组件，已有 27（EditMode）+42（PlayMode，其中 6 条
// GreyBoxTests 直接断言该类型的公开字段）测试覆盖；GreyBox.unity 在本任务里继续作为"框架适配层
// 本身"的独立验证场景保留（阶段 4 验收标准与本任务硬性规则均未要求删除/替换它）。本类型是
// Shell.unity 场景专属的组合根，与 GameFoundationBootstrap 各自独立、互不引用，但遵循同一套
// 数据集/GameplayAssembly/PresentationAssembly 装配顺序（见 presentation/assembly/README.md、
// core/gameplay/tests/EndToEnd/GameWorldFixture.cs）——保持"框架资源与装配根随应用生命周期常驻，
// 具体地图内容随场景切换"的关注点分离，这正是"框架常驻部分 vs 进入地图部分"这句任务书原文要
// 落地的结构，只是落在一个新类型里而不是拆分旧类型。
//
// 判断记录（世界/装配根为什么只构造一次，"新游戏"为什么不能 world.AddEntity 两次）：
// Presentation.Shell.ShellHost.NewGame 的既有实现顺序是"newGameStarter（先）→ SaveSystem.Save →
// SceneRouter.LoadScene（后）"（见 presentation/shell/core/ShellHost.cs），而
// Core.Foundation.SceneRouter.SceneRouter.LoadScene 只在"这不是本 SceneRouter 实例第一次调用
// LoadScene"时才会在 FinishLoading 内部调用 world.ClearAll()（见该类型判断记录"资源种类映射"
// 旁边的 FinishLoading 方法体）。这意味着"NewGameStarter 创建玩家实体"与"SceneRouter 清空旧实体"
// 两件事的顺序，在“同一个长期存活的 WorldSim 实例上发起第二次 LoadScene”时会颠倒——若
// NewGameStarter 自己调用 world.AddEntity，第二次新游戏会在旧实体尚未被清空前尝试重复添加同一个
// EntityId，抛 InvalidOperationException（实体 id 重复）。本类型的解法：玩家 PlayerUnit 对象本身
// 只在 Awake 构造一次（此后一直是同一个 C# 对象引用，RegisterPersistables 绑定的也是这个引用），
// 是否已经"挂在 WorldSim 里"由 HandlePostLoad（挂 SceneRouter 的 post_load 钩子）在每次进入地图时
// 按需重新 AddEntity——ClearAll 只会把对象从 WorldSim 自己的实体字典里摘除、把 Lifecycle 标记为
// Destroyed，不会清空对象自身字段，因此"读档 Load() 已经写好的位置字段"在 ClearAll 之后依然有效，
// HandlePostLoad 重新 AddEntity 时能读到正确的已恢复状态。NewGameStarter 本身不添加任何实体，只
// 重置玩家对象字段到新游戏默认值、返回起始地图 id，真正的"挂进世界"统一交给 post_load 钩子，
// 这样无论是"新游戏"还是"读档"都走同一条"HandlePostLoad 负责挂载"的路径，不重复实现两遍。
//
// 判断记录（引擎侧收口任务，固定步驱动改走 IClock.RequestFixedStep + GameplayAssembly.Advance）：
// 本类型此前在自己的 MonoBehaviour FixedUpdate/Update 里直接调用 World.Tick(SimStep.Continuous)，
// 现改为 Bootstrap() 末尾一次性注册 host.Clock.RequestFixedStep(Time.fixedDeltaTime, OnFixedStep)/
// host.Clock.OnFrame(OnFrameTick)，持有返回的 SubscriptionHandle，OnDestroy 里退订——本类型全局
// 唯一（Ensure() 幂等获取），Bootstrap() 只在首次 Awake 执行一次，因此这两个句柄也只注册一次，
// 不会因为"进入地图"（HandlePostLoad）/"离开地图"（HandlePreUnload）反复触发而重复注册（这两个
// 方法与固定步/帧回调的注册时机完全独立，见文件顶部"世界/装配根为什么只构造一次"同款设计）。
//
// 判断记录（H4 收官：combatParticipantsResolver 恢复真实解析）：与
// Adapter.Unity.Bootstrap.GameFoundationBootstrap 同款判断记录（该文件顶部"判断记录 1b"）——此前
// 固定传一个恒返回空列表的委托，理由是 HandleFixedInput 提交的 cast/move 意图经
// IWorldSim.SubmitIntent 在离散模式下到不了 TurnScheduler、解除不了 awaiting_input，且离散步不
// 推进 SimTimers/冷却/光环。两处缺口已在 core/foundation/sim_loop.WorldSim.AttachDiscreteRouting
// （路由集中在 WorldSim.SubmitIntent 本身，HandleFixedInput 不需要改一行）+
// core/rules/skill.SkillTickHandler/core/rules/combat.CombatTickHandler 订阅 sim.round_ended 统一
// 推进补齐——GameplayAssembly 构造函数第 10.5 步现在会在装配了 clockHost 时自动调用
// world.AttachDiscreteRouting，本类型不需要任何额外接线。combatParticipantsResolver 因此不再传参
// （缺省 null，走真实"按半径+阵营解析"）；data/_sample/found/found.time_model.json 的 combat 行已
// 改回 mode=continuous（示例/灰盒默认连续，离散示例数据改放 Tests/Runtime/TestData/、core 测试
// 数据），本类型在现有示例数据集下战斗仍走连续模式，VerticalSliceTests/ShellFlowTests/UiSuiteTests
// 全部既有用例不受影响；一旦游戏数据集把战斗声明为 discrete，本类型会自动走真实离散链路（HUD、
// EndTurn、AI 回合、presentation.playback_finished 回放门已独立落地并验证，见
// Tests/Runtime/DiscreteCombatTests.cs 与 Tests/Runtime/SharedBootstrapDiscreteTests.cs）。
using System;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Presentation.Assembly;
using Presentation.Render;
using Presentation.Ui;
using UnityEngine;

namespace Adapter.Unity.Shell
{
    /// <summary>框架常驻组合根：见文件顶部注释。全局唯一，<see cref="Ensure"/> 幂等获取。</summary>
    public sealed class FrameworkResidentHost : MonoBehaviour
    {
        private static FrameworkResidentHost? _instance;

        public const string SampleMapId = "world.sample_field";
        private const string PlayerUnitId = "unit.sample_player";
        private const string PlayerFactionId = "fac.player";
        private const string PlayerClassId = "arch.class.sample_a";
        private const string PlayerTemplateId = "creature.sample_hero";
        private const int PlayerLevel = 3;
        private const string BeastSpawnId = "spawn.sample_beast_field";
        private const string AttackSkillId = "skill.sample_strike";
        private const string Skill1Id = "skill.sample_burn";
        private const ulong Seed = 20260905UL;

        /// <summary>ADR-0013 节奏策略：见 Adapter.Unity.Bootstrap.GameFoundationBootstrap 同名字段
        /// 判断记录——H4 收官后 combatParticipantsResolver 已恢复真实解析，本开关在当前示例数据集
        /// （战斗仍声明为 continuous）下仍只决定 GameplayAssembly.Pacing 装配成哪一种实现。</summary>
        private const bool PacingWaitForPlayback = true;

        /// <summary>W6 收口新增（ADR-0017 决策 d 遗留缺口收口，同 Adapter.Unity.Bootstrap.
        /// GameFoundationBootstrap 同名字段判断记录）：命中帧同步开关，同一个布尔值驱动
        /// RenderOptions.HitFrameSync 与 FeedbackOptions.HitFrameSync。默认 false（LogicDriven，
        /// 与改动前行为一致）——本类型是纯 sprite 型场景（不装配 IRenderer3D/model 型外形），
        /// 但命中帧同步策略同样适用于 sprite 型外形（见 SpriteCharacterRig 判断记录），因此本类型
        /// 仍补齐这条开关与对应的 HitFrameSource 接线（同 GameFoundationBootstrap 一致的机制层）。</summary>
        private const bool HitFrameSyncEnabled = false;

        // 判断记录（缺口 2 已解决，同 Adapter.Unity.Bootstrap.GameFoundationBootstrap 同名判断
        // 记录）：动作 id 现直接对应 data/_sample/found/found.input_action.json 表里的行
        // （attack/skill_1，缺口 2 新增），不再是本类型此前自造、绕开数据表的
        // "input.action.shell_attack"/"input.action.shell_skill_1"。
        private const string ActionSetId = "actionset.sample_input_action";
        private const string ActionMove = "input.action.move";
        private const string ActionAttack = "input.action.attack";
        private const string ActionSkill1 = "input.action.skill_1";

        public GameplayAssembly Gameplay { get; private set; } = null!;
        public PresentationAssembly Presentation { get; private set; } = null!;
        public IWorldSim World { get; private set; } = null!;
        public Id PlayerId { get; private set; }
        public Id? BeastEntityId { get; private set; }
        public UnityViewFactory ViewFactory { get; private set; } = null!;
        public FloatingTextReceiver FloatingText { get; private set; } = null!;
        public FreezeFrameReceiver Freeze { get; private set; } = null!;
        public FlashReceiver Flash { get; private set; } = null!;
        public RespawnFeedbackReceiver Respawn { get; private set; } = null!;

        /// <summary>W6 收口新增：本次装配的命中帧同步注册表（见 <see cref="HitFrameSyncEnabled"/>
        /// 判断记录、<c>Presentation.FeedbackBinder.Contracts.IHitFrameSource</c> 类型注释、
        /// ADR-0017 决策 d）。本类型只装配 sprite 型 View（未传 <c>renderer3D</c>），但
        /// <see cref="UnityViewFactory"/> 的两条 View 创建路线（sprite/model）都会向已装配的
        /// <c>hitFrameSource</c> 登记 rig，本类型因此同样能受益。</summary>
        public global::Presentation.FeedbackBinder.Core.CharacterRigHitFrameSource? HitFrameSource { get; private set; }

        public bool BootstrapFailed { get; private set; }

        /// <summary>W3b 新增：本次 <see cref="OnFrameTick"/> 内某个表现步骤抛出的异常次数累计
        /// （拍板 12"表现层异常隔离：OnFrameTick 内各表现步骤 try/catch 记诊断，不影响固定步"，
        /// 见该方法判断记录）。供 PlayMode 测试断言"注入的假接收器抛异常后固定步仍照常推进"。</summary>
        public int PresentationStepExceptionCount { get; private set; }

        public IDataRegistryView Registry => _registry;

        public UnityEngineHost Host => _host;

        /// <summary>供 PlayMode 测试直接发布合成事件用（如验证 feedback.binding 对 combat.
        /// damage_dealt 的暴击分支反应，不依赖战斗随机数命中暴击这一概率事件，见
        /// VerticalSliceTests.Feedback_CritDamage_TriggersFreeze 判断记录）。不是表现层铁律允许的
        /// 生产代码用法，仅供测试代码调用。</summary>
        public IEventBus Bus => _bus;

        private UnityEngineHost _host = null!;
        private IEventBus _bus = null!;
        private IDataRegistryView _registry = null!;
        private PlayerUnit _player = null!;
        private Id _classId;
        private bool _worldEverEntered;
        private readonly System.Collections.Generic.Dictionary<string, bool> _wasActionActive =
            new System.Collections.Generic.Dictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>见文件顶部判断记录：固定步/帧回调改经 IClock 注册，本类型持有返回的句柄，
        /// <see cref="OnDestroy"/> 里显式退订。</summary>
        private Core.Foundation.Common.SubscriptionHandle? _fixedStepHandle;
        private Core.Foundation.Common.SubscriptionHandle? _frameHandle;

        /// <summary>H4 新增（独立版无人值守冒烟"-gf-smoke-discrete"分支，见
        /// <c>Adapter.Unity.Shell.SmokeRunner</c> 判断记录）：置为 true 时 <see cref="Bootstrap"/>
        /// 额外叠加一份只声明 <c>found.time_model</c> combat=discrete 行的内存数据源（见该方法），
        /// 让战斗切到离散模式，供冒烟脚本验证离散链路。用内存数据源而不是磁盘上的第三数据根——
        /// 独立版播放器构建产物不包含 <c>Packages/.../Tests/</c> 目录（Unity 构建管线本就不打包
        /// Tests 内容），<c>adapters/unity/.../Tests/Runtime/TestData/</c>（PlayMode 测试用，见
        /// <c>DiscreteCombatTests.cs</c>）在独立版里读不到；内存数据源不依赖任何文件路径，Editor
        /// PlayMode 与独立版 Player 行为一致。<see cref="Ensure"/> 只会调用一次 <see cref="Bootstrap"/>
        /// （单例，见类型判断记录"世界/装配根为什么只构造一次"），本标志必须在 <see cref="Ensure"/>
        /// 第一次被调用之前设置——<c>SmokeRunner</c> 用
        /// <c>RuntimeInitializeLoadType.BeforeSceneLoad</c>（早于任何场景 <c>Awake</c>，包括
        /// <c>ShellRoot.Awake</c> 间接触发的 <see cref="Ensure"/>）设置本标志。</summary>
        public static bool ForceDiscreteCombatForSmoke;

        public static FrameworkResidentHost Ensure()
        {
            if (_instance != null)
            {
                return _instance;
            }

            var existing = FindFirstObjectByType<FrameworkResidentHost>();
            if (existing != null)
            {
                _instance = existing;
                return existing;
            }

            var go = new GameObject("FrameworkResidentHost");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FrameworkResidentHost>();
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
                Debug.LogError($"[FrameworkResidentHost] 常驻装配失败：{ex}");
            }
        }

        private void Bootstrap()
        {
            _host = UnityEngineHost.Ensure();

            var definitions = Core.Foundation.EventBus.EventKeys.All
                .Select(k => new EventDefinition(k, k.Domain, Array.Empty<string>()))
                .ToList();
            var catalog = EventCatalog.FromDefinitions(definitions);
            _bus = new Core.Foundation.EventBus.EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var contentFs = new UnityFileSystem(readOnlyContentMode: true);
            // 判断记录（数据目录框架/游戏分层任务，最小改动）：found.event_catalog/found.input_action
            // 两张表已从 data/_sample 搬到 data/_framework（框架级数据表，见 data/README.md"两类目录"
            // 一节）；本类型下方无条件读取 found.input_action 整表声明动作集（同
            // GameFoundationBootstrap.cs 同名判断记录），数据根改为"框架级数据根 +
            // data/_sample"两根合并加载，只新增 frameworkSource 一个数据源，不改动其余装配逻辑。
            var frameworkSource = new FileSystemDataSource(contentFs, "data/_framework");
            var source = new FileSystemDataSource(contentFs, "data/_sample");
            var options = PresentationSchemaCatalog.CreateOptions();
            options.FailOnUnknownTable = false;

            var registry = new DataRegistry(source, _bus, options);
            _registry = registry;
            PresentationSchemaCatalog.RegisterAll(registry);

            // H4 新增（见 ForceDiscreteCombatForSmoke 判断记录）：冒烟脚本要求离散链路时，叠加一份
            // 内存数据源，只补一条 found.time_model 的 combat=discrete 行——与
            // Core.Gameplay.Assembly.TimeModelSwitch.LoadModel"后声明覆盖先声明"的判断记录配合，
            // 排在 source（data/_sample，已声明 combat=continuous）之后加载即可生效。
            var sources = ForceDiscreteCombatForSmoke
                ? new IDataSource[] { frameworkSource, source, BuildDiscreteOverlaySource() }
                : new IDataSource[] { frameworkSource, source };
            var report = registry.LoadAll(sources);
            if (report.IsBlocking)
            {
                BootstrapFailed = true;
                Debug.LogError("[FrameworkResidentHost] 数据集校验未通过，已停止：" + string.Join("; ", report.Issues));
                return;
            }

            PreWarmSfxResources(registry);

            var rng = new RngHost(Seed);
            var world = new WorldSim(_bus);
            World = world;

            PlayerId = new Id(PlayerUnitId);
            var factionId = new Id(PlayerFactionId);
            _classId = new Id(PlayerClassId);

            // G1 遗留跟进：GameplayAssembly 构造函数新增第 6 位必填参数 ISaveSystem saveSystem
            // （此前由 PresentationAssembly 自建，见 PresentationAssemblyOptions.SaveGameId 已删除）。
            // 复用同一个 IFileSystem/game id 惯例（此前挂在 presentationOptions.SaveGameId 上）。
            // 判断记录（bus 参数不可省略，见 GameFoundationBootstrap 同款判断记录）：SaveSystem.Save/
            // Load 经可选注入的 IEventBus 发 save.completed/save.loaded，不传会让
            // SaveSlotsViewModel（订阅这两个事件驱动自动刷新）看不到存读档结果。
            var saveSystem = new SaveSystem(_host.FileSystem, new SaveSystemOptions(new Id("game.sample")), _bus);

            // ADR-0013/02 §1.2 固定步契约：clockHost 交给 host.Clock.RequestFixedStep 注册的回调
            // （见本方法末尾）驱动，stepSeconds 与该注册用的 Time.fixedDeltaTime 取同一个值；
            // combatParticipantsResolver 不传（缺省 null，真实解析）——见文件顶部判断记录。
            var clockHost = new Core.Foundation.SimLoop.SimClockHost(
                world, new Core.Foundation.SimLoop.SimLoopOptions { StepSeconds = Time.fixedDeltaTime });
            IPacingPolicy pacingPolicy = PacingWaitForPlayback
                ? new WaitForPlaybackPacingPolicy()
                : new ImmediatePacingPolicy();

            var gameplay = new GameplayAssembly(
                _bus, registry, rng, world, _host.SpatialQuery, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: factionId,
                navigation: _host.Navigation2D,
                clockHost: clockHost,
                pacingPolicy: pacingPolicy);
            Gameplay = gameplay;

            _player = new PlayerUnit(PlayerId, new Id(SampleMapId), factionId, _classId)
            {
                Position = Vec2.Zero,
                TemplateId = new Id(PlayerTemplateId),
            };

            // 判断记录：RegisterUnit/Economy.RegisterUnit 必须在这里（PresentationAssembly 构造
            // 之前）就完成，不能延后到 HandlePostLoad（"进入地图部分"）——PresentationAssembly
            // 构造函数会立即建出十个 UI 视图模型，每个视图模型构造期都会同步调用一次 Refresh()
            // （见 presentation/ui/README.md"均实现 IDisposable...构造期完成一次 Refresh()"），
            // HudViewModel.Refresh 等会经 PlayerPathProvider 查询 player.level，底层
            // ProgressionHost.GetLevel 要求该 unitId 已经 RegisterUnit 过，否则抛
            // ArgumentException（实测：延后注册会在 PresentationAssembly 构造期直接抛异常，
            // Bootstrap 因此整体失败）。这两个注册只影响 Rules/Economy 各自的内部登记表，不受
            // WorldSim.ClearAll 影响（ClearAll 只清空 WorldSim 自己的实体字典，见该方法实现），
            // 因此只需注册一次，不需要在每次 HandlePostLoad 里重复判断。
            Gameplay.Carriers.Rules.RegisterUnit(PlayerId, _classId, raceId: null, level: PlayerLevel);
            Gameplay.Economy.RegisterUnit(PlayerId);
            // 空间索引登记改由 L3 同步（core/carriers/assembly.EntitySpatialSyncHost 订阅
            // entity.created，见 ADR-0016 决策 7）：_player 此刻尚未 World.AddEntity（见
            // HandlePostLoad），不需要在这里手工调用 host.SpatialQuery.Register——真正加入世界时
            // HandlePostLoad 会补一次 DispatchPending 让登记生效。

            // 判断记录：SkillHost._knownSkills 只经显式 LearnSkill（通常由 learn_skill 效果/任务
            // 奖励触发，见 core/rules/skill/core/EffectDispatcher.cs ApplyLearnSkill）填充，
            // RegisterUnit 只登记单位本身，不会自动"解锁"任何技能——U2 灰盒的普攻/技能 1 直接经
            // IWorldSim.SubmitIntent 提交 cast 意图施放，cast 管线本身不检查"是否已学会"（技能书
            // 与施放是两回事，见 09/06 文档），因此这条缺口此前不影响灰盒验收。本类型这里显式
            // LearnSkill 两个示例技能，让 SkillBookViewModel/技能书面板在示例数据集下有真实内容
            // 可展示（阶段 4 UI 套件验收 3 的一部分）。
            Gameplay.Carriers.Rules.Skill.LearnSkill(PlayerId, new Id(AttackSkillId));
            Gameplay.Carriers.Rules.Skill.LearnSkill(PlayerId, new Id(Skill1Id));
            // 缺口 4：动作条槽位绑定改经真实 ISkillBindingHost（此前 ResolveActionBarSlot 是硬编码
            // 的假绑定表，见 ActionBarViewModel/UiIntents 判断记录）；槽位键固定为 "slot_<i>"
            // （ActionBarViewModel.SlotKey）。Bind 要求技能已知，上面两行 LearnSkill 必须先执行。
            Gameplay.Carriers.SkillBindings.Bind(PlayerId, "slot_0", new Id(AttackSkillId));
            Gameplay.Carriers.SkillBindings.Bind(PlayerId, "slot_1", new Id(Skill1Id));

            var viewFactoryDisplayInfo = new DisplayInfoRegistry(registry, _bus);
            // 外部审核阻塞项 3 收口（architecture/落地计划/audit-20260907/followup-2026-09-07.md
            // "外部审核阻塞项处理"一节）：传入 bus/registry 两个可选参数，使 UnityViewFactory 默认
            // 给"生物"型 sprite 视图挂接 UnityFrameAnimPlayer + AnimClipResolver（见该类型
            // AttachDefaultAnimation 判断记录），不再需要本类型自己另外接一遍。
            // W6 收口：命中帧同步注册表（ADR-0017 决策 d），见 HitFrameSource 属性判断记录。
            // _renderOptions 只构造一次、同一个实例分别传给下面的 UnityViewFactory（决定
            // SpriteCharacterRig 构造期实际拿到的 HitFrameSync 策略，见该工厂 _renderOptions
            // 字段判断记录）与下方 presentationOptions.RenderOptions，避免两处各自独立构造、取值
            // 不一致（同 GameFoundationBootstrap/games/_template.GameBootstrap 同名判断记录）。
            HitFrameSource = new global::Presentation.FeedbackBinder.Core.CharacterRigHitFrameSource();
            var renderOptions = new global::Presentation.Render.RenderOptions { HitFrameSync = ResolveHitFrameSyncStrategy() };
            var viewFactory = new UnityViewFactory(
                _host.Renderer2D, new RenderConventionHost(), viewFactoryDisplayInfo, _host.ResourceLoader,
                bus: _bus, dataRegistry: registry, hitFrameSource: HitFrameSource, renderOptions: renderOptions);
            ViewFactory = viewFactory;

            var presentationRng = new RngHost(Seed ^ 0x9E3779B97F4A7C15UL);
            // spatial/navigation 注入（ADR-0016 决策 7、场景卸载级联清理，见 SceneRouter 构造函数
            // 判断记录）。
            var sceneRouter = new Core.Foundation.SceneRouter.SceneRouter(
                registry, _host.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, _bus,
                spatial: _host.SpatialQuery, navigation: _host.Navigation2D);
            // 外部审核阻塞项 2 收口（见 GameplayAssembly._sceneRouter 字段判断记录）：真实
            // SceneRouter 必然晚于 GameplayAssembly 构造完成（需要 gameplay.AppState/gameplay.Hooks），
            // 回填给 GameplayAssembly.RestoreFromSlot 使用，使 death.reload_save 策略读档后能在
            // 目标地图与当前地图不同时真正切场景。
            gameplay.AttachSceneRouter(sceneRouter);

            var presentationOptions = new PresentationAssemblyOptions
            {
                OnFloatingText = (entityId, styleId, text) => FloatingText?.Show(entityId, styleId, text),
                // 判断记录（U3 排障发现的单位换算缺口）：PresentationAssemblyOptions.OnFreeze 的
                // 契约（presentation/assembly/PresentationAssembly.cs）与实际调用来源
                // Presentation.FeedbackBinder.Core.CompositeFeedbackSink.Freeze(double durationMs)
                // 都明确点名参数是"毫秒"（09 第 6.1 节 Freeze(durationMs)），但
                // FreezeFrameReceiver.Freeze(double seconds) 要的是"秒"——此前这里直接把毫秒数
                // 原样传给 Freeze()，把 duration_ms=40（0.04 秒）当成了 40 秒，顿帧会持续 40 秒
                // 真实时间才解冻（FreezeFrameReceiver.Tick 用 Time.unscaledDeltaTime 推进，不受
                // 游戏内时间缩放影响）。此前没有任何测试暴露这个问题：
                // Feedback_CritDamage_TriggersFreeze 只断言 TriggerCount 增加、不检查画面是否卡住
                // 或者卡多久；其余用例此前从未有真实战斗持续到"随机命中一次暴击"又紧接着继续跑完
                // 若干条其它用例的场景（U3 修复死亡判断后，FullVerticalSlice 第一次把普攻循环真正
                // 跑到生物死亡为止，期间命中暴击的概率大幅提升，一旦触发就会把 40 秒的顿帧带进
                // 后续几条用例——ViewBinder.SyncAll/CameraHost.Update 在顿帧期间被跳过，实测复现
                // Paperdoll_LayerOrder_MatchesDisplayMapDeclaredOrder/
                // YSorting_TwoEntitiesWithDifferentY_SortingOrderReflectsY 两条用例断言"应有精灵
                // 渲染"/"排序应反映 Y 坐标"失败——因为顿帧期间根本没有任何 View 被 SyncPose 过，
                // 玩家纸娃娃层从未被创建）。改为除以 1000 换算成秒，与
                // CompositeFeedbackSink.Freeze/FreezeAction.DurationMs 的毫秒语义对齐。
                OnFreeze = durationMs => Freeze?.Freeze(durationMs / 1000.0),
                OnFlash = (entityId, profileId) => Flash?.Show(entityId, profileId),
                CharacterStatConfig = new[]
                {
                    (new Id("stat.strength"), new Id("l10n.stat.strength.name")),
                    (new Id("stat.stamina"), new Id("l10n.stat.stamina.name")),
                },
                PauseMenuOptions = new[]
                {
                    new PauseMenuOption(new Id("pause.resume"), new Id("l10n.pause.sample_resume")),
                    new PauseMenuOption(new Id("pause.settings"), new Id("l10n.pause.sample_settings")),
                    new PauseMenuOption(new Id("pause.main_menu"), new Id("l10n.pause.sample_main_menu")),
                    new PauseMenuOption(new Id("pause.quit"), new Id("l10n.pause.sample_quit")),
                },
                NewGameStarter = SampleNewGameStarter,
                // W6 收口：复用上面已经传给 viewFactory 的同一个 renderOptions 实例（见该处判断记录），
                // 不再另外 new 一份。
                RenderOptions = renderOptions,
                FeedbackOptions = new global::Presentation.FeedbackBinder.Contracts.FeedbackOptions { HitFrameSync = ResolveHitFrameSyncStrategy() },
            };

            // GP-PRES-04 收口（architecture/落地计划/audit-20260907/gameplay-presentation.md）：
            // 传入 _host.ResourceLoader，让 VfxPlayer 也具备"首次引用加载"能力——本类型此前只给
            // SfxPlayer 配了 PreWarmSfxResources 手工预热（见该方法判断记录，ADR-0016 决策 6 的
            // "统一预热"分支），VFX 完全没有对应机制；两者不冲突（UnityResourceLoader.LoadAsync
            // 内部按资源 id 去重，重复调用无副作用，见 PreWarmSfxResources 判断记录）。
            var presentation = new PresentationAssembly(
                gameplay, world, registry, _bus, presentationRng,
                viewFactory, _host.Renderer2D, _host.Camera, _host.Audio, _host.FileSystem, sceneRouter,
                presentationOptions, resourceLoader: _host.ResourceLoader, hitFrameSource: HitFrameSource);
            Presentation = presentation;

            FloatingText = new FloatingTextReceiver(_host.transform, id => world.GetEntity(id)?.Position, presentation.FloatingTextStyles);
            Freeze = new FreezeFrameReceiver();
            Flash = new FlashReceiver(presentation.ViewBinder);
            // W3b 新增（拍板 3/8，复活演出）：ViewBinder 已随 PresentationAssembly 构造完成，可以
            // 立即订阅 unit.respawned（同 Flash/Freeze 两个接收器构造期即订阅的既有惯例）。
            Respawn = new RespawnFeedbackReceiver(_bus, presentation.ViewBinder);

            // 判断记录：见文件顶部——玩家 IPersistable 段绑定的是本类型持有的同一个 _player 对象
            // 引用，构造期只注册一次；此后无论"新游戏"重置字段还是"读档"覆盖字段，都作用于同一份
            // 对象，HandlePostLoad 再决定何时把它重新挂进 WorldSim。
            gameplay.RegisterPersistables(presentation.SaveSystem, _player);

            sceneRouter.RegisterPreUnloadHook(HandlePreUnload);
            sceneRouter.RegisterPostLoadHook(HandlePostLoad);

            // 判断记录（发现的契约缺口"死亡单位不会自动从 AiHost/ISpatialQuery 退场"，核心一半
            // 已由 ADR-0016 背景一节联动解决）：core/rules/ai/core/AiHost.cs 现已直接订阅
            // entity.destroyed 自行静默清理登记（见该类型构造函数判断记录），一个单位被真正销毁
            // 后不会再让 AiTickHandler.Execute 遍历到它、也就不会再让 WorldUnitAccess.Require 抛
            // InvalidOperationException——这半个缺口已解决，不需要引擎适配层代为清理。
            // 本处理器仍然保留：它订阅的是 unit.died（逻辑死亡，早于 entity.destroyed 的实体真正
            // 销毁，见 05 对象模型"死亡是逻辑状态，不是生命周期状态"），目的是让尸体立刻停止
            // AI 决策、从战斗目标空间索引里移除（避免"死了但还能被当目标选中/还在原地决策"的
            // 观感问题），是提前于核心自愈时机的体验优化，不是安全网。AiHost.UnregisterUnit/
            // ISpatialQuery.Unregister 都是契约方法（ADR-0016 决策 7 起 Unregister 也是
            // ISpatialQuery 的正式契约方法，不再是"非契约协作方法"）。
            _bus.Subscribe(Core.Rules.Common.RulesEventKeys.UnitDied, OnUnitDied);

            // 缺口 2 已解决：动作声明整批读自 found.input_action 表已加载的全部行（与
            // Adapter.Unity.Bootstrap.GameFoundationBootstrap 同一套加载方式），不再由本类型代码
            // 补充/绕开——见文件顶部 ActionAttack/ActionSkill1 判断记录。
            var inputActionRecords = registry.GetAll("found.input_action");
            var inputActionDefinitions = new ActionDefinition[inputActionRecords.Count];
            for (var i = 0; i < inputActionRecords.Count; i++)
            {
                inputActionDefinitions[i] = ActionDefinition.FromRecord(inputActionRecords[i]);
            }
            presentation.InputMap.DeclareActionSet(new Id(ActionSetId), inputActionDefinitions);

            // ADR-0013 §9："表现层发出 presentation.playback_finished 后由调用方转发到
            // GameplayAssembly.NotifyPlaybackFinished()"——见文件顶部判断记录，本类型战斗保持
            // 连续模式，本订阅目前是安全的空操作，但契约上应当接好。
            _bus.Subscribe(Core.Foundation.EventBus.EventKeys.PresentationPlaybackFinished,
                _ => Gameplay.NotifyPlaybackFinished());

            // 固定步/帧回调改经 IClock 注册（见文件顶部判断记录）：Bootstrap() 全局只执行一次
            // （Ensure() 幂等单例），这两个句柄因此也只注册一次，不会因为反复进/出地图而重复注册。
            _fixedStepHandle = _host.Clock.RequestFixedStep(Time.fixedDeltaTime, OnFixedStep);
            _frameHandle = _host.Clock.OnFrame(OnFrameTick);
        }

        /// <summary>H4 新增：见 <see cref="ForceDiscreteCombatForSmoke"/> 判断记录——内存数据源，
        /// 只登记一条 <c>found.time_model</c> 记录（<c>scope=combat</c>/<c>mode=discrete</c>），
        /// 字段取值与 <c>adapters/unity/.../Tests/Runtime/TestData/found/found.time_model.json</c>
        /// 完全一致（同一份"离散示例行"，只是换成不依赖文件系统的内存表示）。</summary>
        private static IDataSource BuildDiscreteOverlaySource()
        {
            const string json = @"
            {
                ""table"": ""found.time_model"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""found.time_model.smoke_discrete_combat"", ""scope"": ""combat"", ""mode"": ""discrete"",
                      ""seconds_per_turn"": 6,
                      ""initiative_policy"": ""initiative_stat"",
                      ""initiative_stat"": ""stat.strength"",
                      ""movement_budget_rule"": ""distance"" }
                ]
            }";
            return new InMemoryDataSource().Add("found.time_model", json);
        }

        /// <summary>
        /// 判断记录（引擎侧收口任务，恢复 VerticalSliceTests.FullVerticalSlice 的
        /// LogAssert.NoUnexpectedReceived() 收尾检查前必须解决的资源加载竞态）：
        /// <c>Presentation.Common.ResourceReferenceTracker</c>（09 第 5.3 节"谁首次引用一个资源 id，
        /// 谁负责调用 LoadAsync"）只在 <c>SfxPlayer</c> 真正需要播放某个 sfx.def 时才第一次触发
        /// <see cref="IResourceLoader.LoadAsync"/>，而 <c>UnityResourceLoader.LoadAsync</c> 把实际
        /// 文件读取丢进后台 <c>Task.Run</c>（见该类型顶部"加载方式"判断记录）——若"首次引用"与
        /// "本帧就要播放"恰好同一时刻发生（战斗中第一次命中触发 play_sfx），后台线程通常还没来得及
        /// 读完，<c>UnityAudio.PlaySfx</c> 找不到已解码的 AudioClip，按既有设计记一条
        /// <c>Debug.LogWarning</c>"音效资源未加载或不存在，跳过播放"（占位 wav 文件本身其实一直
        /// 都存在，见 assets/_placeholder/sfx/，不是资源真的缺失，是"首次引用"晚于"需要播放"这一
        /// 步之差）。本方法在世界装配阶段、真正进入战斗之前，主动为 sfx.def 表登记的每个
        /// resource_ref/variants 提前触发一次 LoadAsync（幂等：UnityResourceLoader 内部
        /// _loading/_loaded 按资源 id 去重，重复调用无副作用），给后台线程留出从"世界装配"到"玩家
        /// 真正打出第一下"之间的若干帧真实时间提前完成读取，从根上避免这条时序竞争——不是"改测试
        /// 迁就偶发警告"，是让资源真的在被消费前已经加载完成。
        /// </summary>
        private void PreWarmSfxResources(IDataRegistryView registry)
        {
            foreach (var record in registry.GetAll("sfx.def"))
            {
                var def = global::Presentation.VfxSfx.Contracts.SfxDef.FromRecord(record);
                _host.ResourceLoader.LoadAsync(def.ResourceRef, ResourceKind.Audio, (_, __) => { });
                if (def.Variants != null)
                {
                    foreach (var variant in def.Variants)
                    {
                        _host.ResourceLoader.LoadAsync(variant, ResourceKind.Audio, (_, __) => { });
                    }
                }
            }
        }

        /// <summary>见 <see cref="HitFrameSyncEnabled"/> 判断记录：本方法只是把编译期常量开关换算成
        /// 对应策略枚举，两处 Options 取同一个值（09/ADR-0017"渲染侧/反馈绑定侧是同一个口味配置项的
        /// 两个落点"）。</summary>
        private static global::Presentation.Render.HitFrameSyncStrategy ResolveHitFrameSyncStrategy() =>
            HitFrameSyncEnabled
                ? global::Presentation.Render.HitFrameSyncStrategy.AnimKeyframeDriven
                : global::Presentation.Render.HitFrameSyncStrategy.LogicDriven;

        /// <summary>示例 NewGameStarter：见包 README"示例 NewGameStarter 说明"——这是灰盒验收用的
        /// 示例实现，只按固定的示例数据集重置玩家为新游戏起始状态；具体游戏必须提供自己的
        /// NewGameStarter（见 presentation/shell/contracts/ShellHostTypes.cs
        /// <c>NewGameStarter</c> 判断记录）。</summary>
        private Id SampleNewGameStarter(Id slotId, Id difficultyId, Id? archetypeId)
        {
            _player.Position = Vec2.Zero;
            _player.MapId = new Id(SampleMapId);
            return new Id(SampleMapId);
        }

        /// <summary>见 Bootstrap 内订阅处判断记录："死亡单位不会自动从 AiHost/ISpatialQuery 退场"
        /// 契约缺口的引擎适配层兜底清理。</summary>
        private void OnUnitDied(Core.Foundation.EventBus.IEvent evt)
        {
            if (!(evt is Core.Rules.Common.UnitDiedEvent died))
            {
                return;
            }

            var wasRegistered = Gameplay.Carriers.Rules.Ai.RegisteredUnitIds.Contains(died.UnitId);
            if (wasRegistered)
            {
                Gameplay.Carriers.Rules.Ai.UnregisterUnit(died.UnitId);
            }

            _host.SpatialQuery.Unregister(died.UnitId);
        }

        /// <summary>
        /// 判断记录（发现的第二个契约缺口"world.ClearAll 不会级联清理 AiHost/SpawnHost 的登记
        /// 表"，已由 ADR-0016 背景一节联动解决）：<c>Core.Foundation.SceneRouter.SceneRouter.FinishLoading</c>
        /// 在"这不是本 SceneRouter 实例第一次调用 LoadScene"时会调用 <c>world.ClearAll()</c>（把
        /// WorldSim 自己的实体字典清空、Enqueue 每个实体的 entity.destroyed，随后立即
        /// DispatchPending 派发），但此前 <see cref="Core.Rules.Ai.AiHost"/>（下一次
        /// AiTickHandler.Execute 还会继续遍历它内部保留的 RegisteredUnitIds）与
        /// <see cref="Core.Gameplay.Spawn.SpawnHost"/>（"on_map_enter" 重生策略靠自己
        /// runtime.EntityId 是否非空判断"该出生点是否已经有存活实体"，不知道 WorldSim 那边已经
        /// 清空）都对此一无所知——第二次进入地图（"新游戏"再来一局、"读档"）时，上一局遗留的
        /// AI 注册表项会在下一次 Tick 让 WorldUnitAccess.Require 抛 InvalidOperationException
        /// （U3 实测复现）。现分两处解决：<see cref="Core.Rules.Ai.AiHost"/> 已直接订阅
        /// entity.destroyed 自行静默清理（core/rules/ai/core/AiHost.cs 构造函数），
        /// <see cref="Core.Gameplay.Assembly.GameplayAssembly.LeaveMap"/> 把
        /// <c>AreaTrigger.UnloadMap</c>/<c>Spawn.UnloadMap</c> 打包成"出图"入口（对应既有的
        /// <c>EnterMap</c>"进图"入口）。本方法因此不再需要手工
        /// AiHost.UnregisterUnit/ISpatialQuery.Unregister/SpawnHost.NotifyDespawn 逐个实体退场，
        /// 改为在 pre_unload 钩子里统一调用一次 <c>Gameplay.LeaveMap(mapId)</c>
        /// （ISpatialQuery 的整图兜底 Clear 由 SceneRouter 自己在 ClearAll 之后调用，见该类型
        /// 构造函数判断记录，本方法不重复处理）。
        /// </summary>
        private void HandlePreUnload(Id mapId)
        {
            ViewFactory.DestroyAllCreatedViews();

            Gameplay.LeaveMap(mapId);

            BeastEntityId = null;
            _worldEverEntered = false;
        }

        private void HandlePostLoad(Id mapId)
        {
            if (World.GetEntity(PlayerId) == null)
            {
                World.AddEntity(_player);
                // 空间索引登记改由 L3 同步（core/carriers/assembly.EntitySpatialSyncHost 订阅
                // entity.created，见 ADR-0016 决策 7），不再手工调用 host.SpatialQuery.Register；
                // 紧接着 DispatchPending 一次立即完成登记（同 GameFoundationBootstrap 同款判断
                // 记录），避免玩家在第一个 tick 的 AiDecision 阶段之前还没被空间索引收录。
                _bus.DispatchPending();
            }

            // 外部审核阻塞项 1 收口（architecture/落地计划/audit-20260907/followup-2026-09-07.md
            // "外部审核阻塞项处理"一节）：此前本类型（框架自身的 Shell 读档入口，
            // Presentation.Shell.ShellHost.LoadGame → ISceneRouter → 本 post_load 钩子）从未调用过
            // LootHost.ReattachToWorld——读档恢复的地面掉落物随场景切换（ClearAll）静默消失，是
            // GP-PRES-01 只接进了 games/_template/Runtime/GameBootstrap、没有接进框架自身入口的
            // 那一半缺口。现改为 GameplayAssembly.EnterMap 自身第一步统一调用
            // Loot.ReattachToWorld（见该方法判断记录），本类型不需要再单独接线。
            Gameplay.EnterMap(mapId, PlayerId);

            if (!_worldEverEntered)
            {
                // EnterMap 内部经 Spawn.ApplyForMap 生成的野兽同样只是 Enqueue 了
                // entity.created，这里补一次 DispatchPending。
                _bus.DispatchPending();

                var spawnRecord = Gameplay.Spawn.GetSpawnRecord(new Id(BeastSpawnId));
                if (spawnRecord?.EntityId != null)
                {
                    BeastEntityId = spawnRecord.EntityId.Value;
                }
                _worldEverEntered = true;
            }
        }

        /// <summary>由 <see cref="_fixedStepHandle"/>（<c>host.Clock.RequestFixedStep</c>）驱动，
        /// 取代此前的 MonoBehaviour FixedUpdate（见文件顶部判断记录）。<c>AppState != InWorld</c>
        /// 时跳过（暂停等状态下不推进模拟，同 <c>Pause_StopsWorldSimTick_MovementDoesNotAdvance</c>
        /// 验收的既有行为）；<see cref="WaitForPlaybackPacingPolicy"/> 模式下若上一个离散步仍在等待
        /// 表现层回放完毕，本次回调也跳过 <see cref="GameplayAssembly.Advance"/>（03 第 9 节，见
        /// GameFoundationBootstrap.OnFixedStep 同款判断记录——本类型目前恒不触发离散步，这条门槛
        /// 恒为真）。</summary>
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

            Presentation.InputMap.Update(_host.Input);
            HandleFixedInput();
            Gameplay.Advance(stepSeconds);
        }

        private void HandleFixedInput()
        {
            var moveAxis = Presentation.InputMap.GetActionAxis(ActionMove);
            if (moveAxis.SqrLength > 0.0001)
            {
                Gameplay.Carriers.Movement.Request(MoveRequest.InDirection(PlayerId, moveAxis));
            }

            HandleButtonRisingEdge(ActionAttack, () => CastSkill(new Id(AttackSkillId)));
            HandleButtonRisingEdge(ActionSkill1, () => CastSkill(new Id(Skill1Id)));
        }

        private void HandleButtonRisingEdge(string actionName, Action onTriggered)
        {
            var active = Presentation.InputMap.IsActionActive(actionName);
            var wasActive = _wasActionActive.TryGetValue(actionName, out var w) && w;
            _wasActionActive[actionName] = active;
            if (active && !wasActive)
            {
                onTriggered();
            }
        }

        public void CastSkill(Id skillId)
        {
            var args = new JsonObjectBuilder().Add("skill_id", new JsonString(skillId.Value)).Build();
            World.SubmitIntent(new Intent(PlayerId, "cast", args));
        }

        /// <summary>W3b 新增（拍板 12"表现层异常隔离"）：供 PlayMode 测试注入一个会抛异常的假
        /// 表现步骤（见 <see cref="OnFrameTick"/> 判断记录），验证"某一步骤抛异常不影响固定步与
        /// 其余表现步骤"这一隔离效果——生产代码路径不订阅本事件，恒为 no-op。</summary>
        public event Action<double>? ExtraPresentationStepForTests;

        /// <summary>由 <see cref="_frameHandle"/>（<c>host.Clock.OnFrame</c>）驱动，取代此前的
        /// MonoBehaviour Update（见文件顶部判断记录）。
        /// <para>
        /// 判断记录（拍板 12，表现层异常隔离）：本方法此前是一串顺序调用，任何一步（尤其是具体
        /// 游戏可能重写/替换的接收器，如 <see cref="FloatingText"/>/<see cref="Flash"/>）抛出未捕获
        /// 异常会中断本方法剩余全部步骤——包括 <see cref="Presentation.FeedbackBinder.Core.FeedbackBinder.Update"/>
        /// 的顺序播放队列推进与"零事件步"回放门判定，间接可能让离散模式永久卡在
        /// <c>playing_back</c>。<see cref="Core.Foundation.SimLoop.IClock.RequestFixedStep"/> 驱动的
        /// 固定步（<see cref="OnFixedStep"/>）是完全独立的另一个回调订阅，逻辑推进本身不受本方法
        /// 影响，但本方法異常若被引擎适配层的 <c>IClock</c> 实现视为"这个订阅者本次回调失败"而不是
        /// "整条 OnFrame 分发链路失败"，其它同样订阅了 <c>OnFrame</c> 的调用方仍可能被波及——本方法
        /// 因此把内部每个表现步骤单独包一层 try/catch，异常只记诊断
        /// （<see cref="Debug.LogException"/> + <see cref="PresentationStepExceptionCount"/> 计数），
        /// 不重新抛出，确保一步失败不阻塞同一帧内后续步骤，也不会让 <see cref="OnFixedStep"/> 那条
        /// 独立回调链路收到任何波及。
        /// </para>
        /// </summary>
        private void OnFrameTick(double unscaledDelta)
        {
            if (BootstrapFailed || Presentation == null)
            {
                return;
            }

            RunPresentationStep(() => Presentation.Shell.Update());

            var state = Gameplay.AppState.GetState();
            var renderTicking = state == Core.Foundation.AppLifecycle.AppState.InWorld ||
                                 state == Core.Foundation.AppLifecycle.AppState.Pause;

            // 判断记录：Presentation.Feedback.Update(dt) 推进离散模式的顺序播放队列（09 第 6.4
            // 节），与 ViewBinder/相机插值不同——即便当前不在 InWorld/Pause（renderTicking 为假）
            // 也应继续推进（例如战斗结算后立刻切到读档画面，队列里仍有排队的表现动作应当照常清空，
            // 不应卡住 playing_back 节奏门），因此本调用不受 renderTicking 门槛限制。
            RunPresentationStep(() => Presentation.Feedback.Update(unscaledDelta));

            // GP-03 根治（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：
            // Presentation.VfxSfx.Core.VfxPlayer.Update(dt) 此前没有任何生产入口调用——循环特效的
            // 生命周期池（VfxPool）不会随时间停止，首次异步加载迟迟不回调的挂起请求
            // （_pendingSpawns 超时倒计时，见 VfxOptions.FirstLoadTimeoutSeconds）永久积压，只有
            // 单元测试直接调用 Update 才会推进。同 Feedback.Update 一样不受下面 renderTicking 门槛
            // 限制——即便当前不在 InWorld/Pause（例如战斗结算后立刻切到读档画面），已经播放中的循环
            // 特效也应该继续按自己的 lifetime 正常停止，不应该因为切到别的 AppState 就卡在"永远
            // 播放"。
            RunPresentationStep(() => Presentation.Vfx.Update(unscaledDelta));

            // C07 根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：
            // Presentation.VfxSfx.Core.SfxPlayer 此前没有任何时钟驱动入口——首次冷音效加载迟迟不
            // 回调的排队请求只在下一次 Play 调用开头惰性扫一遍（见 SfxPlayer.
            // SweepTimedOutPendingPlays 判断记录），若节奏门已关闭且此后没有新的 Play 调用，卡死的
            // 加载请求永远不会被扫到、PendingPlayCountChanged 永远不会因超时而触发。同上面
            // Presentation.Vfx.Update 一样不受下面 renderTicking 门槛限制——超时清理与是否在
            // InWorld/Pause 无关。
            RunPresentationStep(() => Presentation.Sfx.Update(unscaledDelta));

            // 拍板 5/DECISIONS 收口（离散回放门生产实测）：presentation/feedback_binder/README.md
            // 判断记录 9"此前 Unity 引导侧'靠零事件兜底短路'的临时手法已随本次收口废弃"——原 H4 在
            // 此处补的"WaitForPlaybackPacingPolicy 下 PendingCount==0 时主动调用
            // NotifyPlaybackFinished"逐帧轮询短路已删除。playing_back 节奏门现仅由
            // PlaybackQueue.Finished（队列由非空变空的边沿事件，见该类型注释）经
            // FeedbackBinder 构造函数里的 `_queue.Finished += () => _bus.PublishImmediate(new
            // PlaybackFinishedEvent())` 联动、本类型订阅 presentation.playback_finished 转发到
            // Gameplay.NotifyPlaybackFinished() 解除——这是"确有回放内容"时唯一的解除路径。
            //
            // 根治修复（W5c，第三轮审计"离散回放门‘零事件步骤’无自动通知"仍保留项收口）：此前这里
            // 记录的"已知局限"——某个离散步在 Sequential 队列模式下一个反馈动作都没有入队时，
            // PlaybackQueue.Finished 永远不会触发（"由非空变空"的边沿事件，队列若从未变过非空则
            // 不会触发），playing_back 因此永久卡住——已在 core/gameplay/assembly 侧结构性根治：
            // Core.Foundation.SimLoop.WaitForPlaybackPacingPolicy 新增可选 HasPendingPlayback 探针，
            // Core.Gameplay.Assembly.GameplayAssembly.SetPendingPlaybackProbe 提供窄回填入口，
            // Presentation.Assembly.PresentationAssembly 在装配好本类型的 Feedback 之后自动接上
            // `() => Feedback.Queue.PendingCount > 0`（见三者判断记录）——零反馈的离散步在
            // GameplayAssembly.Advance 内部 BeginStep 那一刻就立即判定 IsPlaybackFinished=true，
            // 不再进入 playing_back，本类型（Unity 引导）不需要任何轮询兜底即可正确工作，上面这段
            // OnFixedStep 的 IsPlaybackFinished 门槛判断保持不变——它本就只负责"节奏门关闭时跳过
            // Advance"，不负责判定"这一步有没有东西要回放"，后者从一开始就不该是引导层的职责。

            if (!renderTicking)
            {
                return;
            }

            // W3b 新增（拍板 9，插值系数）：core/gameplay/assembly.GameplayAssembly.InterpolationAlpha
            // 现已落地（由 Advance 内部的 ISimClockHost.Advance 返回值驱动），取代本类型此前自行
            // 用 _interpAccumulator/Time.fixedDeltaTime 重新实现的同一套"距上次固定步过去了多久"
            // 累加器——两者语义一致（都是"0~1 之间，供渲染插值"的系数），改用核心侧权威值后不再需要
            // 本类型自己维护累加器状态。已知局限（供 W5 参考）：InterpolationAlpha 只在
            // OnFixedStep→Gameplay.Advance 调用时更新（固定步节奏），本类型 OnFrameTick 按渲染帧
            // 节奏读取，两者节奏不同——渲染帧率高于物理帧率时，同一个固定步区间内的多个渲染帧会读到
            // 同一个 alpha 值直到下一次固定步更新它，不是逐渲染帧连续增长，插值平滑度弱于此前的
            // 逐帧累加实现；这是 core/gameplay/assembly 的既有行为，不在 adapters/unity 写入范围内
            // 调整。
            var alpha = Mathf.Clamp01((float)Gameplay.InterpolationAlpha);

            if (!Freeze.IsFrozen)
            {
                RunPresentationStep(() => Presentation.ViewBinder.SyncAll(alpha));
                RunPresentationStep(() => Presentation.Camera.Update(alpha));
            }

            // W3b 新增（拍板 6，八个程序动画原语可视化）：推进每个仍存活的 sprite 型 View 持有的
            // ICharacterRig.ProceduralAnim 时间轴（见 ICharacterRig.Update 判断记录"由表现帧驱动
            // 代码……每帧调用一次"——presentation/view_binding.ViewBinder 本身不调用这一步，见
            // Runtime/Presentation/UnitySpriteView.cs 类型注释，本类型是"表现帧驱动代码"的落地，
            // 补上这个此前无人调用的职责）。放在 ViewBinder.SyncAll 之后：先推进各原语的采样值
            // （更新 UnitySpriteView 内部累积的偏移/旋转/缩放字段），下一帧 SyncPose 才会读到本帧
            // 更新后的值——严格来说存在一帧延迟，可接受（同类系统里"本帧输入下一帧生效"的常见简化，
            // 09 未对程序动画原语的采样延迟拍板具体要求）。
            RunPresentationStep(AdvanceCharacterRigs);

            RunPresentationStep(() => FloatingText.Tick((float)unscaledDelta));
            RunPresentationStep(() => Freeze.Tick(unscaledDelta));
            RunPresentationStep(() => Flash.Tick(unscaledDelta));
            RunPresentationStep(() => Respawn.Tick(unscaledDelta));

            if (ExtraPresentationStepForTests != null)
            {
                RunPresentationStep(() => ExtraPresentationStepForTests?.Invoke(unscaledDelta));
            }
        }

        private void AdvanceCharacterRigs()
        {
            var views = ViewFactory.CreatedViews;
            for (var i = 0; i < views.Count; i++)
            {
                if (views[i].IsAlive && views[i] is IHasCharacterRig hasRig)
                {
                    hasRig.Rig.Update(Time.deltaTime);
                }
            }
        }

        /// <summary>见 <see cref="OnFrameTick"/> 判断记录：单个表现步骤的异常隔离落地——记诊断、
        /// 计数，不重新抛出。</summary>
        private void RunPresentationStep(Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                PresentationStepExceptionCount++;
                Debug.LogException(ex);
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
            Respawn?.Dispose();
            Presentation?.Dispose();
            ViewFactory?.DestroyAllCreatedViews();
        }
    }
}
