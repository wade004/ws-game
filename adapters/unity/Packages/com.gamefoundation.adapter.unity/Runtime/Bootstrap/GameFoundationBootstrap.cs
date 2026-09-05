#nullable enable
// GameFoundationBootstrap：U2-2 引导组装根（灰盒场景 GreyBox.unity 唯一挂载的组件）。
//
// 装配顺序严格照抄 core/gameplay/tests/EndToEnd/GameWorldFixture.cs（唯一一处"从磁盘数据集到完整
// 可玩世界"的既有装配范例）+ presentation/assembly/README.md 的 PresentationAssembly 装配顺序，
// 见 BuildWorld 内部注释逐段对应。
//
// 表现层铁律落地（任务硬性规则 6）：本类型只在 FixedUpdate 里推进 WorldSim.Tick（固定步长，
// Time.fixedDeltaTime 本身是引擎的固定配置值，不是变帧率的挂钟时间，判断记录同
// Adapter.Unity.EngineAdapter.UnityClock 顶部注释"确定性判断记录"）；Update 只做表现（
// ViewBinder.SyncAll/CameraHost.Update 插值同步、三个反馈接收器的 Tick）。玩家操作一律先经
// IInputMapHost 转成"意图"（移动/普攻/技能 1 走 IWorldSim.SubmitIntent，见 HandleFixedInput）或
// 窄契约调用（交互走 GameObjectHost.Interact，见类型注释判断记录 2），不直接改 WorldSim/Carriers
// 的任何状态。
//
// 判断记录 1（固定步驱动为什么不用 IClock.RequestFixedStep——原因已由 ADR-0016 部分解决，
// 但保留现有 FixedUpdate 直驱写法）：UnityEngineHost 是 DontDestroyOnLoad 的组合根，其持有的
// UnityClock 跨场景重进持续存活；此前 IClock 契约（02 第 1.2 节）只有注册方法，没有取消注册的
// 方法，若改用 host.Clock.RequestFixedStep 注册一个闭包捕获本次 World 实例的固定步回调，场景重进
// 时旧回调永远无法注销，会残留对着一个没有任何代码再读取的旧 World 做无意义的 Tick。ADR-0016
// 决策 1 已经给 RequestFixedStep 补上 SubscriptionHandle 返回值，这条限制本身已解除；本类型仍然
//保留直接在 GameFoundationBootstrap 自己的 MonoBehaviour FixedUpdate/Update 里驱动的写法——
// 这两个方法只在本组件存活期间被引擎调用，场景卸载销毁本组件后自动停止，天然不残留任何注册，
// 与"改经 RequestFixedStep 注册 + Dispose 退订"在效果上等价，前者不需要额外持有并退订句柄，
// 属于同一结果的两种实现路径，本类型选择改动面更小的一种，不代表 RequestFixedStep 的
// SubscriptionHandle 返回值没有用武之地——core/carriers/unit 等真正跨场景常驻、需要在"重建世界"
// 这个明确时机主动退订的调用方（而非"随宿主组件销毁自然停止"）应该使用它，见该方法契约注释。
// 插值 alpha 用"Update 累加、FixedUpdate 清零"的标准写法（与 Core.Foundation.SimLoop.SimClockHost
// 内部累积器算法同一思路，只是不复用该类型——SimClockHost 自己内部调用 world.Tick，与"由
// FixedUpdate 直接调用"是两种互斥的驱动方式，不能既注册给 SimClockHost 又自己再调一次）。
//
// 判断记录 2（交互此前是窄契约调用，已由 ADR-0016 改为提交意图）：core/carriers/gobj 的
// GameObjectHost.Interact 是一个直接方法调用（见该类型签名 InteractResult Interact(Id unitId,
// Id gobjInstanceId)），此前 WorldSim 的 tick 阶段编排里没有任何消费 Kind=="interact" 意图的
// 处理器（勘察确认，属于"03 第 4.2 节步骤 6 早已写明由 GameObjectHost 消费、实现尚未跟上文档"的
// 情形，不是契约缺口），本类型因此只能按 P3 允许的另一种输入落地方式（窄契约调用）暂代。
// ADR-0016 背景一节联动补齐 Core.Carriers.Gobj.InteractIntentTickHandler（挂在
// TickPhase.TriggerEvaluation，见 core/carriers/assembly/CarriersAssembly.cs）后，本类型改为与
// CastSkill 同一套"经 IWorldSim.SubmitIntent 提交意图"落地方式，见 Interact() 方法。
//
// 判断记录 3（缺口 2 已解决——普攻/技能改读 found.input_action 表，不再代码内补充声明）：
// data/_sample/found/found.input_action.json 原先的示例动作集只有 move/confirm/cancel/interact/
// open_menu/pause/camera_adjust 七个非战斗动作；本类型此前因此在代码里额外声明一个补充动作集
// （actionset.greybox，"input.action.greybox_attack"/"input.action.greybox_skill_1" 两个本类型
// 自造的按钮动作，绕开数据表）。缺口 2 已经给该表补上 attack/skill_1~skill_4/use_item 若干战斗类
// 按钮动作（见该数据文件、schema/found.input_action.md），本类型现改为与
// core/foundation/input_map/tests/InputMapHostTests.cs
// FromRecord_LoadsSevenSampleActionsFromInMemoryTable 同一套加载方式——从已经装配好的
// registry（第 2 步 registry.LoadAll() 装载的同一份，found.input_action 的 TableSchema 由
// core/rules/assembly/RulesSchemaCatalog.RegisterAll 登记，经 PresentationSchemaCatalog 级联
// 注册，早已随第 2 步完成，不需要额外注册）读出全部行，逐行 ActionDefinition.FromRecord 转换后
// 整批 DeclareActionSet，不再在代码里另造一份重复/绕开数据表的动作声明；本类型只保留"哪些
// 动作 id 对应普攻/技能 1/交互/移动"这层数据消费逻辑（HandleFixedInput），动作本身的存在性/
// 默认绑定完全交给 found.input_action 表决定。
using System;
using System.Collections.Generic;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Presentation.Assembly;
using Presentation.Render;
using UnityEngine;

namespace Adapter.Unity.Bootstrap
{
    /// <summary>灰盒场景唯一挂载的引导组件：见文件顶部注释。</summary>
    public sealed class GameFoundationBootstrap : MonoBehaviour
    {
        [Header("数据集（Inspector 可配置，默认指向框架自带的中性示例数据）")]
        // 判断记录（数据目录框架/游戏分层任务，最小改动）：found.event_catalog/found.input_action
        // 两张表已从 data/_sample 搬到 data/_framework（框架级数据表，见 data/README.md"两类目录"
        // 一节）；本类型第 6 步无条件读取 found.input_action 整表声明动作集（见文件顶部"判断记录
        // 3"），因此数据根改为"框架级数据根 + 既有数据集根"两根合并加载（见下方 BuildWorld 第 2 步
        // registry.LoadAll(IReadOnlyList<IDataSource>)），只新增 _frameworkDatasetRoot 一个
        // Inspector 字段与对应的第二个 FileSystemDataSource，不改动其余装配逻辑。
        [SerializeField] private string _frameworkDatasetRoot = "data/_framework";
        [SerializeField] private string _datasetRoot = "data/_sample";

        [Header("示例地图与玩家（默认 id 均取自 core/gameplay/tests/EndToEnd/GameWorldFixture.cs 同一套中性示例数据）")]
        [SerializeField] private string _mapId = "world.sample_field";
        [SerializeField] private string _playerUnitId = "unit.sample_player";
        [SerializeField] private string _playerFactionId = "fac.player";
        [SerializeField] private string _playerClassId = "arch.class.sample_a";
        [SerializeField] private string _playerTemplateId = "creature.sample_hero";
        [SerializeField] private int _playerLevel = 3;

        [Header("战斗/交互（示例数据自带，见 data/_sample/skill、gobj）")]
        [SerializeField] private string _beastSpawnId = "spawn.sample_beast_field";
        [SerializeField] private string _chestTemplateId = "gobj.sample_chest";
        [SerializeField] private string _attackSkillId = "skill.sample_strike";
        [SerializeField] private string _skill1Id = "skill.sample_burn";

        [Header("模拟")]
        [SerializeField] private ulong _seed = 20260905UL;

        // -----------------------------------------------------------------
        // 消费 found.input_action 表声明的动作 id（见文件顶部"判断记录 3"，动作本身现由数据表
        // 声明，本类型不再代码内补充/绕开）。
        // -----------------------------------------------------------------
        private const string ActionSetId = "actionset.sample_input_action";
        private const string ActionMove = "input.action.move";
        private const string ActionInteract = "input.action.interact";
        private const string ActionAttack = "input.action.attack";
        private const string ActionSkill1 = "input.action.skill_1";

        public GameplayAssembly? Gameplay { get; private set; }
        public PresentationAssembly? Presentation { get; private set; }
        public IWorldSim? World { get; private set; }
        public Id PlayerId { get; private set; }
        public Id? BeastEntityId { get; private set; }
        public Id? ChestEntityId { get; private set; }
        public UnityViewFactory? ViewFactory { get; private set; }
        public FloatingTextReceiver? FloatingText { get; private set; }
        public FreezeFrameReceiver? Freeze { get; private set; }
        public FlashReceiver? Flash { get; private set; }

        /// <summary>数据集加载/世界装配阶段出现阻断性错误时为真（见 <see cref="BuildWorld"/>）；
        /// 为真时 <see cref="Update"/>/<see cref="FixedUpdate"/> 不做任何事，已经
        /// <c>Debug.LogError</c> 过具体原因。</summary>
        public bool BootstrapFailed { get; private set; }

        private UnityEngineHost _host = null!;
        private IEventBus _bus = null!;
        private double _interpAccumulator;
        private readonly Dictionary<string, bool> _wasActionActive = new Dictionary<string, bool>(StringComparer.Ordinal);

        private void Awake()
        {
            try
            {
                BuildWorld();
            }
            catch (Exception ex)
            {
                BootstrapFailed = true;
                Debug.LogError($"[GameFoundationBootstrap] 世界装配失败，已停止：{ex}");
            }
        }

        private void BuildWorld()
        {
            _host = UnityEngineHost.Ensure();

            // ---------------------------------------------------------
            // 1) 事件总线（惯例同 GameWorldFixture.Build）。
            // ---------------------------------------------------------
            var definitions = Core.Foundation.EventBus.EventKeys.All
                .Select(k => new EventDefinition(k, k.Domain, Array.Empty<string>()))
                .ToList();
            var catalog = EventCatalog.FromDefinitions(definitions);
            _bus = new Core.Foundation.EventBus.EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            // ---------------------------------------------------------
            // 2) 数据集：只读内容根 UnityFileSystem(readOnlyContentMode: true)（不是存档用的
            //    host.FileSystem，见 UnityFileSystem 类型顶部判断记录 1——此前是独立的
            //    StreamingAssetsFileSystem 类，ADR-0016 补上 GetContentRootDir 后已合并）+
            //    PresentationSchemaCatalog（一次性注册 L0～L5 全部表）。
            // ---------------------------------------------------------
            var contentFs = new UnityFileSystem(readOnlyContentMode: true);
            var frameworkSource = new FileSystemDataSource(contentFs, _frameworkDatasetRoot);
            var source = new FileSystemDataSource(contentFs, _datasetRoot);
            var options = PresentationSchemaCatalog.CreateOptions();
            // 判断记录（缺口 2 已解决后勘误）：found.input_action 的 TableSchema 其实已经由
            // core/rules/assembly/RulesSchemaCatalog.RegisterAll 登记（经本行 PresentationSchemaCatalog
            // 级联注册），本类型第 6 步会读取该表（见文件顶部"判断记录 3"）；此前这里的注释误写为
            // "未被任一 catalog 登记 schema"，与 registry.RegisterSchema(InputActionSchema.Table) 的
            // 既有事实不符，已如实更正。FailOnUnknownTable=false 仍然保留兜底（避免数据集里任何一张
            // 未被登记 schema 的表阻断整套装配，惯例同 GameWorldFixture），与本次修正无关。
            options.FailOnUnknownTable = false;

            var registry = new DataRegistry(source, _bus, options);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll(new IDataSource[] { frameworkSource, source });
            if (report.IsBlocking)
            {
                BootstrapFailed = true;
                Debug.LogError("[GameFoundationBootstrap] 数据集校验未通过，已停止：" + string.Join("; ", report.Issues));
                return;
            }

            // ---------------------------------------------------------
            // 3) L0～L4：世界模拟 + GameplayAssembly（空间查询/寻路接真实 Unity 引擎适配层实现，
            //    不是桩实现——这是本引导脚本与 GameWorldFixture 的关键差异：后者是纯逻辑测试
            //    夹具，用 StubSpatialQuery；本类型跑在真引擎里，理应接真实现）。
            // ---------------------------------------------------------
            var rng = new RngHost(_seed);
            var world = new WorldSim(_bus);
            World = world;

            PlayerId = new Id(_playerUnitId);
            var mapId = new Id(_mapId);
            var factionId = new Id(_playerFactionId);
            var classId = new Id(_playerClassId);

            // G1 遗留跟进：GameplayAssembly 构造函数新增第 6 位必填参数 ISaveSystem saveSystem
            // （此前由 PresentationAssembly 自建，见 PresentationAssemblyOptions.SaveGameId 已删除）。
            // 判断记录（bus 参数不可省略）：SaveSystem.Save/Load 经可选注入的 IEventBus 发
            // save.completed/save.loaded（见其源码 `_bus?.PublishImmediate`），不传 bus 时这两个
            // 事件被静默吞掉——SaveSlotsViewModel 正是靠订阅这两个事件才在存读档后自动刷新槽位列表
            // （见 presentation/ui/README.md），漏传 bus 会让存档槽 UI 表现为"存了档但列表看不到"。
            var saveSystem = new SaveSystem(_host.FileSystem, new SaveSystemOptions(new Id("game.greybox_demo")), _bus);
            var gameplay = new GameplayAssembly(
                _bus, registry, rng, world, _host.SpatialQuery, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: factionId,
                navigation: _host.Navigation2D);
            Gameplay = gameplay;

            var player = new PlayerUnit(PlayerId, mapId, factionId, classId)
            {
                Position = Vec2.Zero,
                TemplateId = new Id(_playerTemplateId),
            };
            world.AddEntity(player);
            // 空间索引登记改由 L3 同步（core/carriers/assembly.EntitySpatialSyncHost 订阅
            // entity.created，见 ADR-0016 决策 7），不再手工调用 host.SpatialQuery.Register。
            // 判断记录（这里不能提前 DispatchPending）：本方法第 5 步才构造 PresentationAssembly/
            // ViewBinder（同样订阅 entity.created 创建 View），此刻提前 DispatchPending 会让
            // entity.created 派发给零订阅者、事件丢失，ViewBinder 永远收不到玩家的创建通知。
            // 空间索引登记因此推迟到第一次 world.Tick 的 EventDispatch 阶段（phase 7）才生效——
            // 比 AiDecision 阶段（phase 2）晚一拍，属于可接受的一次性启动延迟（后续 tick 都已就绪），
            // 不影响本类型示例场景的验收断言（beast 的 AI 感知在第二个 tick 起才需要找到玩家）。
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, classId, raceId: null, level: _playerLevel);
            gameplay.Economy.RegisterUnit(PlayerId);

            // ---------------------------------------------------------
            // 4) 进入示例地图：触发 spawn.sample_beast_field 自动生成一只生物（见
            //    core/gameplay/tests/EndToEndTests.cs EnterAndAcceptQuest 同一模式），登记进空间
            //    索引供 skill.sample_strike 的 target.chain.sample_nearest_enemy 找到它；再手动
            //    生成一个可交互 gobj（框架当前没有"静态地图对象登记表"，见任务汇报"契约缺口"）。
            // ---------------------------------------------------------
            gameplay.EnterMap(mapId, PlayerId);
            // 见上面第 3 步同款判断记录：这里同样不能提前 DispatchPending（ViewBinder 尚未构造）。

            var spawnRecord = gameplay.Spawn.GetSpawnRecord(new Id(_beastSpawnId));
            if (spawnRecord?.EntityId != null)
            {
                BeastEntityId = spawnRecord.EntityId.Value;
            }
            else
            {
                Debug.LogWarning($"[GameFoundationBootstrap] spawn 记录 \"{_beastSpawnId}\" 未生成实体，攻击类用例将没有目标。");
            }

            var chestPosition = new Vec2(2, 2);
            ChestEntityId = gameplay.Carriers.GameObjects.Spawn(new Id(_chestTemplateId), mapId, chestPosition, facing: 0.0);
            // 判断记录（与框架默认一致，见 CarriersAssembly.DefaultSpatialSyncKinds 判断记录）：
            // 本 gobj 不会被 EntitySpatialSyncHost 登记进 host.SpatialQuery——该空间索引同时被
            // target.chain.sample_nearest_enemy 一类战斗目标解析策略共用，其内部（
            // Core.Rules.Targeting.BuiltinTargetStrategies.NearestInShapeStrategy）拿到查询结果后
            // 一律当"单位"处理（调用 WorldUnitAccess.GetPosition），若 gobj 登记进同一个索引
            // 会在附近敌人解析时把它也捞进来，进而抛 InvalidOperationException（实测跑 PlayMode
            // 测试时复现过，因此框架默认清单不含 gobj）。交互功能不需要空间查询——本类型的
            // Interact() 直接持有 ChestEntityId 常量引用，不做"最近可交互物件"检索，因此 gobj
            // 干脆不登记进这份面向战斗单位的空间索引，避免污染查询结果。

            // ---------------------------------------------------------
            // 5) L5 表现层：见 presentation/assembly/README.md 装配顺序；viewFactory 需要在
            //    PresentationAssembly 构造之前就拿到 IDisplayInfoRegistry（PresentationAssembly
            //    自己的那份要到构造函数内部第 1 步才产生，构造函数参数不能"先有鸡还是先有蛋"），
            //    本类型额外建一份独立实例喂给 UnityViewFactory——两份都是只读投影、读同一个
            //    registry/bus，语义一致，不产生状态不一致（判断记录）。
            // ---------------------------------------------------------
            var viewFactoryDisplayInfo = new DisplayInfoRegistry(registry, _bus);
            var viewFactory = new UnityViewFactory(_host.Renderer2D, new RenderConventionHost(), viewFactoryDisplayInfo, _host.ResourceLoader);
            ViewFactory = viewFactory;

            var presentationRng = new RngHost(_seed ^ 0x9E3779B97F4A7C15UL);
            // spatial/navigation 注入（ADR-0016 决策 7、场景卸载级联清理，见 SceneRouter 构造函数
            // 判断记录）：卸载旧场景时除逐实体经 entity.destroyed 同步注销外，额外整图兜底 Clear。
            var sceneRouter = new Core.Foundation.SceneRouter.SceneRouter(
                registry, _host.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, _bus,
                spatial: _host.SpatialQuery, navigation: _host.Navigation2D);

            // 三个反馈接收器由 PresentationAssemblyOptions 的回调"延迟读取"（闭包捕获的是本类型
            // 的属性访问，不是构造期的值）：PresentationAssembly 构造函数内部第 3 步装配
            // feedback_binder 时只是把这三个委托存起来，真正被调用要等到后续事件触发（构造期本身
            // 不触发任何一种反馈动作），因此可以先把委托接上、稍后（见下）再把
            // FloatingText/Freeze/Flash 三个属性赋成真正的实例。
            var presentationOptions = new PresentationAssemblyOptions
            {
                OnFloatingText = (entityId, styleId, text) => FloatingText?.Show(entityId, styleId, text),
                // 判断记录：见 Adapter.Unity.Shell.FrameworkResidentHost 同款判断记录——
                // CompositeFeedbackSink.Freeze(double durationMs) 传入的是毫秒，
                // FreezeFrameReceiver.Freeze(double seconds) 要的是秒，这里同样需要除以 1000 换算，
                // 否则顿帧会持续把传入的毫秒数当秒数用（如 40ms 顿帧变成 40 秒）。
                OnFreeze = durationMs => Freeze?.Freeze(durationMs / 1000.0),
                OnFlash = (entityId, profileId) => Flash?.Show(entityId, profileId),
            };

            var presentation = new PresentationAssembly(
                gameplay, world, registry, _bus, presentationRng,
                viewFactory, _host.Renderer2D, _host.Camera, _host.Audio, _host.FileSystem, sceneRouter,
                presentationOptions);
            Presentation = presentation;

            // 三个反馈接收器都需要 PresentationAssembly 构造完成后才能建出（FloatingText 需要
            // presentation.FloatingTextStyles，Flash 需要 presentation.ViewBinder）；上面
            // presentationOptions 里的三个回调只在真正的反馈事件触发时才会读取这三个属性
            // （见上方注释），构造期本身不会调用，顺序上没有问题。
            FloatingText = new FloatingTextReceiver(_host.transform, id => world.GetEntity(id)?.Position, presentation.FloatingTextStyles);
            Freeze = new FreezeFrameReceiver();
            Flash = new FlashReceiver(presentation.ViewBinder);

            // ---------------------------------------------------------
            // 6) 输入：声明动作集（见文件顶部"判断记录 3"）——从 found.input_action 表已加载的
            //    全部行整批声明，不再由本类型代码补充/绕开；复用 PresentationAssembly 已经构造好
            //    的同一个 IInputMapHost 实例（UI 设置面板等也持有这一个实例，不另建第二个）。
            // ---------------------------------------------------------
            var inputActionRecords = registry.GetAll("found.input_action");
            var inputActionDefinitions = new ActionDefinition[inputActionRecords.Count];
            for (var i = 0; i < inputActionRecords.Count; i++)
            {
                inputActionDefinitions[i] = ActionDefinition.FromRecord(inputActionRecords[i]);
            }
            presentation.InputMap.DeclareActionSet(new Id(ActionSetId), inputActionDefinitions);

            // 判断记录（U3 发现的核心缺口，已由 ADR-0016 背景一节联动修复一半）：
            // core/rules/ai/core/AiHost.cs 公开了 UnregisterUnit，但此前没有任何一处在实体真正
            // 销毁（entity.destroyed）时调用它——AiTickHandler.Execute 每 tick 无条件遍历
            // AiHost.RegisteredUnitIds 逐个 Step，一个已销毁但仍留在 AiHost 内部注册表里的单位会
            // 在下一次 Tick 让 WorldUnitAccess.Require 抛 InvalidOperationException（U3 实测复现）。
            // AiHost 现已订阅 entity.destroyed 自行静默清理（core/rules/ai/core/AiHost.cs 构造函数，
            // 见该处判断记录），这半个缺口已解决，不再需要引擎适配层代为清理。
            // 本处理器仍然保留——它订阅的是 unit.died（逻辑死亡，早于 entity.destroyed 的实体真正
            // 销毁，见 05 对象模型"死亡是逻辑状态，不是生命周期状态"），目的是让尸体立刻停止
            // AI 决策、从战斗目标空间索引里移除（避免"死了但还能被当目标选中/还在原地决策"的观感
            // 问题），是提前于 AiHost 自愈时机的一个体验优化，不是安全网，AiHost.UnregisterUnit/
            // ISpatialQuery.Unregister 都是契约方法（ADR-0016 决策 7 起 Unregister 也是
            // ISpatialQuery 的正式契约方法，不再是"非契约协作方法"）。与
            // Adapter.Unity.Shell.FrameworkResidentHost 的同名判断记录同一处理，两条独立的
            // 装配路径（灰盒/Shell）各自订阅一次，互不影响。
            _bus.Subscribe(Core.Rules.Common.RulesEventKeys.UnitDied, OnUnitDied);
        }

        private void OnUnitDied(IEvent evt)
        {
            if (!(evt is Core.Rules.Common.UnitDiedEvent died))
            {
                return;
            }

            var wasRegistered = Gameplay!.Carriers.Rules.Ai.RegisteredUnitIds.Contains(died.UnitId);
            if (wasRegistered)
            {
                Gameplay.Carriers.Rules.Ai.UnregisterUnit(died.UnitId);
            }

            _host.SpatialQuery.Unregister(died.UnitId);
        }

        private void FixedUpdate()
        {
            if (BootstrapFailed || World == null || Presentation == null)
            {
                return;
            }

            _interpAccumulator = 0.0;

            Presentation.InputMap.Update(_host.Input);
            HandleFixedInput();

            World.Tick(SimStep.Continuous(Time.fixedDeltaTime));
        }

        private void HandleFixedInput()
        {
            var moveAxis = Presentation!.InputMap.GetActionAxis(ActionMove);
            if (moveAxis.SqrLength > 0.0001)
            {
                Gameplay!.Carriers.Movement.Request(MoveRequest.InDirection(PlayerId, moveAxis));
            }

            HandleButtonRisingEdge(ActionAttack, () => CastSkill(new Id(_attackSkillId)));
            HandleButtonRisingEdge(ActionSkill1, () => CastSkill(new Id(_skill1Id)));
            HandleButtonRisingEdge(ActionInteract, Interact);
        }

        private void HandleButtonRisingEdge(string actionName, Action onTriggered)
        {
            var active = Presentation!.InputMap.IsActionActive(actionName);
            var wasActive = _wasActionActive.TryGetValue(actionName, out var w) && w;
            _wasActionActive[actionName] = active;
            if (active && !wasActive)
            {
                onTriggered();
            }
        }

        /// <summary>把"普攻"/"技能 1"落成一条 <c>cast</c> 意图（见文件顶部铁律说明：经
        /// <see cref="IWorldSim.SubmitIntent"/> 进入 L0，不直接调用 <c>SkillHost.CastSkill</c>）。</summary>
        private void CastSkill(Id skillId)
        {
            var args = new JsonObjectBuilder().Add("skill_id", new JsonString(skillId.Value)).Build();
            World!.SubmitIntent(new Intent(PlayerId, "cast", args));
        }

        /// <summary>把"交互"落成一条 <c>interact</c> 意图（见文件顶部"判断记录 2"：此前受限于
        /// core/carriers/gobj 尚未提供消费该意图的 <c>ITickPhaseHandler</c>，只能窄契约直接调用
        /// <c>GameObjectHost.Interact</c>；ADR-0016 背景一节联动补齐
        /// <c>Core.Carriers.Gobj.InteractIntentTickHandler</c>（挂在 <c>TickPhase.TriggerEvaluation</c>）
        /// 后，改为与 <see cref="CastSkill"/> 同一套"经 <see cref="IWorldSim.SubmitIntent"/> 进入
        /// L0"落地方式，不再直接调用 <c>GameObjectHost.Interact</c>）。</summary>
        private void Interact()
        {
            if (ChestEntityId.HasValue)
            {
                var args = new JsonObjectBuilder().Add("gobj_instance_id", new JsonString(ChestEntityId.Value.Value)).Build();
                World!.SubmitIntent(new Intent(PlayerId, "interact", args));
            }
        }

        private void Update()
        {
            if (BootstrapFailed || Presentation == null)
            {
                return;
            }

            var unscaledDelta = Time.unscaledDeltaTime;
            _interpAccumulator += unscaledDelta;
            var alpha = Mathf.Clamp01((float)(_interpAccumulator / Math.Max(Time.fixedDeltaTime, 0.0001f)));

            // 顿帧只暂停表现层插值/相机，不影响 FixedUpdate 里的逻辑 tick（见 FreezeFrameReceiver
            // 顶部判断记录）。
            if (!Freeze!.IsFrozen)
            {
                Presentation.ViewBinder.SyncAll(alpha);
                Presentation.Camera.Update(alpha);
            }

            FloatingText!.Tick(unscaledDelta);
            Freeze.Tick(unscaledDelta);
            Flash!.Tick(unscaledDelta);
        }

        private void OnDestroy()
        {
            // 见 Adapter.Unity.Presentation.UnityViewFactory 顶部判断记录：ViewBinder/CameraHost
            // 不支持退订是 presentation/ 的已知缺口，本方法在引擎侧尽力而为地清理——退订能退订的
            // 部分（Presentation.Dispose()），再销毁全部本次场景创建过的 View（避免其 Sprite 实例
            // 挂在 DontDestroyOnLoad 的渲染根下跨场景重进泄漏）。
            Presentation?.Dispose();
            ViewFactory?.DestroyAllCreatedViews();
        }
    }
}
