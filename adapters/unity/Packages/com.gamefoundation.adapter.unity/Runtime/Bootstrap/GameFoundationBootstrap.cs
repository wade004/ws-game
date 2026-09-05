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
// 判断记录 1（固定步驱动为什么不用 IClock.RequestFixedStep）：UnityEngineHost 是 DontDestroyOnLoad
// 的组合根，其持有的 UnityClock 跨场景重进持续存活；IClock 契约（02 第 1.2 节）只有注册方法，没有
// 取消注册的方法。若本类型改用 host.Clock.RequestFixedStep 注册一个闭包捕获本次 World 实例的固定
// 步回调，场景重进时旧回调永远无法注销，会残留在 UnityClock 内部列表里对着一个没有任何代码再读取
// 的旧 World 做无意义的 Tick（不产生功能错误，但有累积 CPU 开销）。本类型改为直接在
// GameFoundationBootstrap 自己的 MonoBehaviour FixedUpdate/Update 里驱动（这两个方法只在本组件
// 存活期间被引擎调用，场景卸载销毁本组件后自动停止，不残留任何注册），插值 alpha 用"Update 累加、
// FixedUpdate 清零"的标准写法（与 Core.Foundation.SimLoop.SimClockHost 内部累积器算法同一思路，
// 只是不复用该类型——SimClockHost 自己内部调用 world.Tick，与"由 FixedUpdate 直接调用"是两种互斥
// 的驱动方式，不能既注册给 SimClockHost 又自己再调一次）。
//
// 判断记录 2（交互为什么是窄契约调用而不是 Intent）：core/carriers/gobj 的 GameObjectHost.Interact
// 是一个直接方法调用（见该类型签名 InteractResult Interact(Id unitId, Id gobjInstanceId)），
// found.event_catalog/WorldSim 的 tick 阶段编排里没有任何消费 Kind=="interact" 意图的处理器（勘察
// 确认，见任务汇报"契约缺口"）；presentation/assembly/README.md 对 PresentationAssembly 铁律落地的
// 表述本就是"全部用户输入经 UiIntents 转成 IWorldSim.SubmitIntent 或窄契约调用（P3）"——窄契约调用
// 同样是 P3 允许的输入落地方式，不是绕过铁律，本类型按此调用 GameObjectHost.Interact。
//
// 判断记录 3（普攻/技能 1 为什么不读 found.input_action 表）：该表的示例动作集只有
// move/confirm/cancel/interact/open_menu/pause/camera_adjust 七个动作，不含任何战斗类动作（数据
// 文件本身在 description 里声明"示例动作集，不构成任何游戏的操作定论"）；data/ 是本任务硬性规则
// 明确不动的目录。IInputMapHost.DeclareActionSet 本身是通用 API，不要求动作定义必须来自某张数据表，
// 本类型直接在代码里声明一个补充动作集（复用 move/interact 与示例表相同的绑定字符串，攻击/技能 1
// 是本类型新增的两个按钮动作），不修改任何 data/ 下的文件。
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
        // 自定义补充输入动作集（见文件顶部"判断记录 3"）。
        // -----------------------------------------------------------------
        private const string ActionSetId = "actionset.greybox";
        private const string ActionMove = "input.action.move";
        private const string ActionInteract = "input.action.interact";
        private const string ActionAttack = "input.action.greybox_attack";
        private const string ActionSkill1 = "input.action.greybox_skill_1";

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
            // 2) 数据集：只读 StreamingAssetsFileSystem（不是存档用的 host.FileSystem，见该类型
            //    顶部判断记录）+ PresentationSchemaCatalog（一次性注册 L0～L5 全部表）。
            // ---------------------------------------------------------
            var contentFs = new StreamingAssetsFileSystem();
            var source = new FileSystemDataSource(contentFs, _datasetRoot);
            var options = PresentationSchemaCatalog.CreateOptions();
            // 判断记录：found.input_action 未被任一 catalog 登记 schema（本类型改用代码直接声明
            // 动作集，见文件顶部"判断记录 3"，不读该表），保留 FailOnUnknownTable=false 兜底，
            // 避免数据集里任何一张未被登记 schema 的表阻断整套装配（惯例同 GameWorldFixture）。
            options.FailOnUnknownTable = false;

            var registry = new DataRegistry(source, _bus, options);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
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

            var gameplay = new GameplayAssembly(
                _bus, registry, rng, world, _host.SpatialQuery,
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
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, classId, raceId: null, level: _playerLevel);
            _host.SpatialQuery.Register(PlayerId, player.Position, 0.5);
            gameplay.Economy.RegisterUnit(PlayerId);

            // ---------------------------------------------------------
            // 4) 进入示例地图：触发 spawn.sample_beast_field 自动生成一只生物（见
            //    core/gameplay/tests/EndToEndTests.cs EnterAndAcceptQuest 同一模式），登记进空间
            //    索引供 skill.sample_strike 的 target.chain.sample_nearest_enemy 找到它；再手动
            //    生成一个可交互 gobj（框架当前没有"静态地图对象登记表"，见任务汇报"契约缺口"）。
            // ---------------------------------------------------------
            gameplay.EnterMap(mapId, PlayerId);

            var spawnRecord = gameplay.Spawn.GetSpawnRecord(new Id(_beastSpawnId));
            if (spawnRecord?.EntityId != null)
            {
                BeastEntityId = spawnRecord.EntityId.Value;
                var beastPos = gameplay.Carriers.Units.GetPosition(BeastEntityId.Value);
                _host.SpatialQuery.Register(BeastEntityId.Value, beastPos, 0.5);
            }
            else
            {
                Debug.LogWarning($"[GameFoundationBootstrap] spawn 记录 \"{_beastSpawnId}\" 未生成实体，攻击类用例将没有目标。");
            }

            var chestPosition = new Vec2(2, 2);
            ChestEntityId = gameplay.Carriers.GameObjects.Spawn(new Id(_chestTemplateId), mapId, chestPosition, facing: 0.0);
            // 判断记录：本 gobj 不登记进 host.SpatialQuery——该空间索引同时被
            // target.chain.sample_nearest_enemy 一类战斗目标解析策略共用，其内部（
            // Core.Rules.Targeting.BuiltinTargetStrategies.NearestInShapeStrategy）拿到查询结果后
            // 一律当"单位"处理（调用 WorldUnitAccess.GetPosition），登记一个 gobj 实体进同一个索引
            // 会在附近敌人解析时把它也捞进来，进而抛 InvalidOperationException（实测跑 PlayMode
            // 测试时复现）。交互功能不需要空间查询——本类型的 Interact() 直接持有
            // ChestEntityId 常量引用，不做"最近可交互物件"检索，因此 gobj 干脆不登记进这份
            // 面向战斗单位的空间索引，避免污染查询结果。

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
            var sceneRouter = new Core.Foundation.SceneRouter.SceneRouter(
                registry, _host.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, _bus);

            // 三个反馈接收器由 PresentationAssemblyOptions 的回调"延迟读取"（闭包捕获的是本类型
            // 的属性访问，不是构造期的值）：PresentationAssembly 构造函数内部第 3 步装配
            // feedback_binder 时只是把这三个委托存起来，真正被调用要等到后续事件触发（构造期本身
            // 不触发任何一种反馈动作），因此可以先把委托接上、稍后（见下）再把
            // FloatingText/Freeze/Flash 三个属性赋成真正的实例。
            var presentationOptions = new PresentationAssemblyOptions
            {
                SaveGameId = new Id("game.greybox_demo"),
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
            // 6) 输入：声明补充动作集（见文件顶部"判断记录 3"），复用 PresentationAssembly 已经
            //    构造好的同一个 IInputMapHost 实例（UI 设置面板等也持有这一个实例，不另建第二个）。
            // ---------------------------------------------------------
            presentation.InputMap.DeclareActionSet(new Id(ActionSetId), new[]
            {
                new ActionDefinition(new Id(ActionMove), ActionKind.Axis2D,
                    new[] { "composite2d:key:w|key:s|key:a|key:d" }, description: "灰盒演示：平面移动"),
                new ActionDefinition(new Id(ActionInteract), ActionKind.Button,
                    new[] { "key:e" }, description: "灰盒演示：与最近可交互物件互动"),
                new ActionDefinition(new Id(ActionAttack), ActionKind.Button,
                    new[] { "key:j", "mouse:MouseLeft" }, description: "灰盒演示：普攻（skill.sample_strike）"),
                new ActionDefinition(new Id(ActionSkill1), ActionKind.Button,
                    new[] { "key:digit1" }, description: "灰盒演示：技能 1（skill.sample_burn）"),
            });

            // 判断记录（U3 新增，契约缺口发现）：core/rules/ai/core/AiHost.cs 公开了
            // UnregisterUnit，但勘察 core/gameplay/assembly/GameplayAssembly.cs 与
            // core/gameplay/spawn 全文，没有任何一处在 unit.died 时调用它——AiTickHandler.Execute
            // 每 tick 无条件遍历 AiHost.RegisteredUnitIds 逐个 Step，一个已死亡但仍留在 AiHost
            // 内部注册表里的单位会在下一次 Tick 让 WorldUnitAccess.Require 抛
            // InvalidOperationException（U3 实测复现：本任务的 PlayMode 用例首次让示例生物真正
            // 战斗至死亡，暴露了这条此前从未被走通的路径；一旦复现，之后每次 FixedUpdate 都会
            // 重新抛出，直至场景重进）。这是 core/ 层面的既有缺口（core/rules/ai 不在本任务允许
            // 改动范围内），按"窄契约调用"惯例（同本文件"判断记录 2"GameObjectHost.Interact）在
            // 引擎适配层订阅 unit.died 自行补上退场清理：AiHost.UnregisterUnit +
            // ISpatialQuery.Unregister（后者是 UnitySpatialQuery 的非契约协作方法）。与
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

        /// <summary>把"交互"落成对 <c>GameObjectHost.Interact</c> 的窄契约调用（见文件顶部
        /// "判断记录 2"）。</summary>
        private void Interact()
        {
            if (ChestEntityId.HasValue)
            {
                Gameplay!.Carriers.GameObjectInteractions.Interact(PlayerId, ChestEntityId.Value);
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
