#nullable enable
// GameFoundationBootstrap：U2-2 引导组装根（灰盒场景 GreyBox.unity 唯一挂载的组件）。
//
// 装配顺序严格照抄 core/gameplay/tests/EndToEnd/GameWorldFixture.cs（唯一一处"从磁盘数据集到完整
// 可玩世界"的既有装配范例）+ presentation/assembly/README.md 的 PresentationAssembly 装配顺序，
// 见 BuildWorld 内部注释逐段对应。
//
// 表现层铁律落地（任务硬性规则 6）：本类型不再直接驱动模拟——固定步长节拍改经
// IClock.RequestFixedStep 注册（见下方"判断记录 1"），回调内调用 GameplayAssembly.Advance
// （连续模式下等价于原先的 World.Tick(SimStep.Continuous(...))，见该方法契约）；帧插值改经
// IClock.OnFrame 注册（ViewBinder.SyncAll/CameraHost.Update 插值同步、三个反馈接收器的 Tick）。
// 玩家操作一律先经 IInputMapHost 转成"意图"（移动/普攻/技能 1 走 IWorldSim.SubmitIntent，见
// HandleFixedInput）或窄契约调用（交互走 GameObjectHost.Interact，见类型注释判断记录 2），不直接
// 改 WorldSim/Carriers 的任何状态。
//
// 判断记录 1（固定步驱动改走 IClock.RequestFixedStep，引擎侧收口任务）：此前本类型保留
// MonoBehaviour FixedUpdate/Update 直驱写法，原因是"IClock 契约只有注册方法、没有取消注册方法"；
// ADR-0016 决策 1 已经给 RequestFixedStep/OnFrame 补上 SubscriptionHandle 返回值，02 第 1.2 节把
// requestFixedStep 定为"主循环驱动固定步长的唯一入口"，这条限制已经解除，不再有理由绕开它——
// 本类型改为在 BuildWorld 末尾用 host.Clock.RequestFixedStep(Time.fixedDeltaTime, OnFixedStep)/
// host.Clock.OnFrame(OnFrameTick) 各注册一次，持有返回的 SubscriptionHandle，
// OnDestroy 里显式 Dispose 退订（见该方法）——场景卸载时机与"组件销毁自然停止"完全一致，只是
// 现在显式表达为退订而不是隐式依赖 MonoBehaviour 生命周期，与 core/carriers/unit 等真正跨场景
// 常驻调用方使用同一套契约，不再是两套并存的驱动方式。固定步长直接取 Time.fixedDeltaTime（Unity
// 自身的固定步长本就是恒定配置值，非变帧率挂钟时间），与内部构造的 SimClockHost 的 StepSeconds
// 取同一个值，两者按相同节拍推进，误差为零（不是"≤5%"这条性能约定里退让的近似值）。
//
// 判断记录 1b（H4 收官：combatParticipantsResolver 恢复真实解析，接入真实离散战斗）：本判断记录
// 此前把 combatParticipantsResolver 固定传一个恒返回空列表的委托，理由是 core/ 侧当时有两处能力
// 缺口——(a) HandleFixedInput 提交的 cast/move 意图经 IWorldSim.SubmitIntent 进入 L0 后，离散模式
// 下无法到达 TurnScheduler、永远解除不了 awaiting_input；(b) 离散步不推进 SimTimers/冷却/光环，
// 战斗中会"冻结"。这两处缺口已在 core/foundation/sim_loop.WorldSim.AttachDiscreteRouting（意图
// 路由集中在 WorldSim.SubmitIntent 本身，本类型的 HandleFixedInput/Interact 不需要改一行）+
// core/rules/skill.SkillTickHandler/core/rules/combat.CombatTickHandler 订阅 sim.round_ended
// 统一推进（见三者判断记录）补齐——GameplayAssembly 构造函数第 10.5 步现在会在装配了 clockHost 时
// 自动调用 world.AttachDiscreteRouting，本类型不需要任何额外接线。combatParticipantsResolver 因此
// 不再传参（缺省 null，走 TimeModelSwitch 内建的"按半径+阵营解析"真实逻辑，与
// games/_template/Runtime/GameBootstrap.cs 同款默认值）；data/_sample/found/found.time_model.json
// 的 combat 行改回 mode=continuous（示例/灰盒默认连续，离散示例数据改放
// Tests/Runtime/TestData/、core 测试数据，见该文件与 core/gameplay/tests/Discrete 判断记录），
// 本类型因此在现有示例数据集下战斗仍走连续模式，行为不变；一旦游戏数据集把战斗声明为
// discrete，本类型会自动走真实离散链路（HUD、EndTurn、AI 回合、presentation.playback_finished
// 回放门已独立落地并验证，见 Tests/Runtime/DiscreteCombatTests.cs 与新增的
// Tests/Runtime/SharedBootstrapDiscreteTests.cs——后者用本类型 + 叠加离散测试根验证共享引导下的
// 真实离散链路）。
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

        /// <summary>H4 收官新增：可选的第三数据根，留空（默认）时不加载，对现有 GreyBox.unity 场景
        /// 零行为变化。供 <c>Tests/Runtime/SharedBootstrapDiscreteTests.cs</c> 叠加一份只补
        /// <c>found.time_model</c> 的 combat=discrete 行的测试根（同 <c>DiscreteCombatTests.cs</c>
        /// 使用的 <c>Tests/Runtime/TestData/</c>），验证本类型（真实共享引导，不是自建 fixture）
        /// 在数据集把战斗声明为离散时，经真实 combatParticipantsResolver（见文件顶部"判断记录
        /// 1b"）能正确驱动离散战斗——不是仅由 <c>DiscreteCombatTests.cs</c> 自建的最小
        /// GameplayAssembly 验证。</summary>
        [SerializeField] private string _extraDatasetRoot = "";

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

        /// <summary>W6-B 新增（ADR-0017 决策 e）：<c>global::Presentation.VfxSfx.Core.EquipmentWeaponStyleSource</c>
        /// 需要的"主手槽位 id"（见 09 第 4.4 节、该类型判断记录"未新增 item.template 字段"）——
        /// 09/04 未定义全局槽位登记表（同 <c>presentation/ui/README.md</c> 既有契约缺口"装备槽位 id
        /// 清单"），本字段是该口味配置项在灰盒装配根的落点。默认取
        /// <c>data/_sample/item/item.slot_definition.json</c> 已有的
        /// <c>"item.slot.sample_main_hand"</c>（与示例数据 <c>item.sample_blade.slot</c> 一致，见该
        /// 文件），不是任意新造的 id；找不到该槽位当前装备时
        /// <see cref="global::Presentation.VfxSfx.Contracts.MainHandWeaponTemplateResolver"/> 返回 null（见
        /// <see cref="BuildWorld"/> 第 5 步该委托的实现）。</summary>
        [SerializeField] private string _mainHandSlotId = "item.slot.sample_main_hand";

        /// <summary>W6 收口新增（ADR-0017 决策 d 遗留缺口收口）：命中帧同步开关，同一个布尔值驱动
        /// <see cref="Presentation.Render.RenderOptions.HitFrameSync"/> 与
        /// <see cref="Presentation.FeedbackBinder.Contracts.FeedbackOptions.HitFrameSync"/>（见
        /// <see cref="BuildWorld"/> 第 5 步）。默认 false（<c>LogicDriven</c>，与改动前行为一致）。</summary>
        [SerializeField] private bool _hitFrameSyncEnabled = false;

        [Header("模拟")]
        [SerializeField] private ulong _seed = 20260905UL;

        /// <summary>ADR-0013 节奏策略：true（默认）=等待表现层回放完毕（<see cref="WaitForPlaybackPacingPolicy"/>，
        /// 03 §3.2 离散模式默认节奏），false=不等待（<see cref="ImmediatePacingPolicy"/>）。见文件顶部
        /// "判断记录 1b"——H4 收官后 combatParticipantsResolver 已恢复真实解析，本开关在当前示例数据集
        /// （战斗仍声明为 continuous）下仍只决定 GameplayAssembly.Pacing 装配成哪一种实现，一旦数据集把
        /// 战斗声明为 discrete，本开关会真正影响离散步的节奏。</summary>
        [SerializeField] private bool _pacingWaitForPlayback = true;

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

        /// <summary>W6-B 新增：本次装配的命中帧同步注册表（见 <see cref="Presentation.FeedbackBinder.Contracts.IHitFrameSource"/>
        /// 类型注释、ADR-0017 决策 d），传给 <see cref="ViewFactory"/> 使 sprite/model 两条路线的
        /// View 创建/销毁都自动登记/注销。W6 收口：<see cref="Presentation.Assembly.PresentationAssembly"/>
        /// 已补齐 <c>hitFrameSource</c> 构造参数（见 <see cref="BuildWorld"/> 构造点），本实例已一并
        /// 接给内部 <c>FeedbackBinderCore</c>——是否真正生效由 <see cref="_hitFrameSyncEnabled"/>
        /// 开关决定（默认 false，<c>RenderOptions</c>/<c>FeedbackOptions</c> 均保持 <c>LogicDriven</c>，
        /// 行为与改动前一致）。也供测试直接构造独立的 <c>FeedbackBinderCore</c> 验证命中帧同步等待
        /// 队列本身（见 <c>Tests/Runtime/ModelIntegrationTests.cs</c>），或经本类型开关走生产装配根
        /// 端到端验证（见 <c>Tests/Runtime/HitFrameSyncEndToEndTests.cs</c>）。</summary>
        public global::Presentation.FeedbackBinder.Core.CharacterRigHitFrameSource? HitFrameSource { get; private set; }

        /// <summary>数据集加载/世界装配阶段出现阻断性错误时为真（见 <see cref="BuildWorld"/>）；
        /// 为真时不注册 <see cref="OnFixedStep"/>/<see cref="OnFrameTick"/>，已经
        /// <c>Debug.LogError</c> 过具体原因。</summary>
        public bool BootstrapFailed { get; private set; }

        private UnityEngineHost _host = null!;
        private IEventBus _bus = null!;
        private readonly Dictionary<string, bool> _wasActionActive = new Dictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>W6-B 新增：见 <see cref="BuildWorld"/> 第 5 步构造点判断记录，<see cref="OnDestroy"/>
        /// 里显式 Dispose（退订 <c>item.equipped</c>/<c>item.unequipped</c>）。</summary>
        private global::Presentation.VfxSfx.Core.EquipmentWeaponStyleSource? _weaponStyleSource;

        /// <summary>PR130-07 根治新增：默认 factory 的 equipVisual 映射入口，同批构造/Dispose，见
        /// <see cref="_weaponStyleSource"/> 同款判断记录。</summary>
        private global::Presentation.Render.EquipmentVisualSource? _equipVisualSource;

        /// <summary>W3b 新增（拍板 12"表现层异常隔离"）：本次 <see cref="OnFrameTick"/> 内某个表现
        /// 步骤抛出的异常次数累计，同 <see cref="Adapter.Unity.Shell.FrameworkResidentHost.PresentationStepExceptionCount"/>
        /// 同款判断记录。</summary>
        public int PresentationStepExceptionCount { get; private set; }

        /// <summary>供 PlayMode 测试注入一个会抛异常的假表现步骤，同
        /// <see cref="Adapter.Unity.Shell.FrameworkResidentHost.ExtraPresentationStepForTests"/>。</summary>
        public event Action<double>? ExtraPresentationStepForTests;

        /// <summary>见文件顶部"判断记录 1"：固定步/帧回调改经 <see cref="Core.Foundation.EngineAdapter.IClock"/>
        /// 注册，本类型持有返回的句柄，<see cref="OnDestroy"/> 里显式退订。</summary>
        private Core.Foundation.Common.SubscriptionHandle? _fixedStepHandle;
        private Core.Foundation.Common.SubscriptionHandle? _frameHandle;

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
            // H4 收官新增：_extraDatasetRoot 非空时叠加为第三根（见该字段判断记录），留空时行为
            // 与此前完全一致（只有 frameworkSource + source 两根）。
            var sources = string.IsNullOrEmpty(_extraDatasetRoot)
                ? new IDataSource[] { frameworkSource, source }
                : new IDataSource[] { frameworkSource, source, new FileSystemDataSource(contentFs, _extraDatasetRoot) };
            var report = registry.LoadAll(sources);
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

            // ADR-0013/02 §1.2 固定步契约：clockHost 交给 host.Clock.RequestFixedStep 注册的回调
            // （见 BuildWorld 末尾）驱动，stepSeconds 与该注册用的 Time.fixedDeltaTime 取同一个值，
            // 两者按相同节拍推进（误差为零）。pacingPolicy 按 Inspector 开关二选一；
            // combatParticipantsResolver 不传（缺省 null）——见文件顶部"判断记录 1b"：H4 收官后
            // 恢复真实解析，与 games/_template/Runtime/GameBootstrap.cs 同款默认值。
            var clockHost = new Core.Foundation.SimLoop.SimClockHost(
                world, new Core.Foundation.SimLoop.SimLoopOptions { StepSeconds = Time.fixedDeltaTime });
            IPacingPolicy pacingPolicy = _pacingWaitForPlayback
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

            // W6-B 新增：命中帧同步注册表（ADR-0017 决策 d）+ 武器风格来源（ADR-0017 决策 e）。
            // 两者均是"框架提供机制、游戏层按需接线"的可选能力（见 W6-A 判断记录 5），本类型作为灰盒
            // 装配根按 09/13 给出的默认约定接上，具体游戏可以按自己的装备宿主实现另行构造。
            HitFrameSource = new global::Presentation.FeedbackBinder.Core.CharacterRigHitFrameSource();
            var mainHandSlotId = new Id(_mainHandSlotId);
            global::Presentation.VfxSfx.Contracts.MainHandWeaponTemplateResolver mainHandResolver = unitId =>
                gameplay.Carriers.Equipment.GetAllEquippedInstances(unitId).TryGetValue(mainHandSlotId, out var instance)
                    ? instance.TemplateId
                    : (Id?)null;
            var weaponStyleSource = new global::Presentation.VfxSfx.Core.EquipmentWeaponStyleSource(_bus, mainHandResolver, viewFactoryDisplayInfo);
            _weaponStyleSource = weaponStyleSource;

            // PR130-07 根治：默认 factory 的 equipVisual 映射入口，同批构造/Dispose（见
            // global::Presentation.Render.EquipmentVisualSource 类型判断记录）。
            var equipVisualCatalog = new System.Collections.Generic.Dictionary<Id, global::Presentation.Render.EquipVisualDef>();
            foreach (var record in registry.GetAll("display.equip_visual"))
            {
                var def = global::Presentation.Render.EquipVisualDef.FromRecord(record);
                equipVisualCatalog[def.ItemId] = def;
            }
            var equipVisualSource = new global::Presentation.Render.EquipmentVisualSource(_bus, equipVisualCatalog);
            _equipVisualSource = equipVisualSource;

            // W6 收口（ADR-0017 决策 d 遗留缺口收口）：_hitFrameSyncEnabled 同时驱动 RenderOptions/
            // FeedbackOptions 两处 HitFrameSync（09/ADR-0017"同一个口味配置项的两个落点，装配层
            // 负责保持一致"）——同一个 RenderOptions 实例既要传给下面 UnityViewFactory（决定
            // SpriteCharacterRig/ModelCharacterRig 构造期实际拿到的 HitFrameSync 策略，见该工厂
            // _renderOptions 字段判断记录"比 PresentationAssembly 未暴露 hitFrameSource 更深一层的
            // 接线缺口"），也要传给下面 presentationOptions.RenderOptions（PresentationAssembly.Render
            // 对外暴露的只读投影）——两处必须是同一份取值，否则会出现"rig 侧已经切到
            // AnimKeyframeDriven，但 PresentationAssembly.Render 报告的仍是默认值"这类装配内部不一致。
            // 默认（开关 false）时两者都保持 LogicDriven，与改动前行为一致。
            var hitFrameSyncStrategy = _hitFrameSyncEnabled
                ? global::Presentation.Render.HitFrameSyncStrategy.AnimKeyframeDriven
                : global::Presentation.Render.HitFrameSyncStrategy.LogicDriven;
            var renderOptions = new global::Presentation.Render.RenderOptions { HitFrameSync = hitFrameSyncStrategy };

            // 外部审核阻塞项 3 收口（architecture/落地计划/audit-20260907/followup-2026-09-07.md
            // "外部审核阻塞项处理"一节）：传入 bus/registry 两个可选参数，使 UnityViewFactory 默认
            // 给"生物"型 sprite/model 视图挂接默认动画接线（见该类型 AttachDefaultAnimation/
            // AttachDefaultModelAnimation 判断记录），不再需要本类型自己另外接一遍。W6-B 追加传入
            // _host.Renderer3D/HitFrameSource/weaponStyleSource 三个新可选参数，见各自判断记录。
            // W6 收口追加传入 renderOptions（见上方判断记录）。
            var viewFactory = new UnityViewFactory(
                _host.Renderer2D, new RenderConventionHost(), viewFactoryDisplayInfo, _host.ResourceLoader,
                bus: _bus, dataRegistry: registry,
                renderer3D: _host.Renderer3D, hitFrameSource: HitFrameSource, weaponStyleSource: weaponStyleSource,
                renderOptions: renderOptions,
                equipVisualByItemInstanceId: equipVisualSource.VisualByItemInstanceId);
            ViewFactory = viewFactory;

            var presentationRng = new RngHost(_seed ^ 0x9E3779B97F4A7C15UL);
            // spatial/navigation 注入（ADR-0016 决策 7、场景卸载级联清理，见 SceneRouter 构造函数
            // 判断记录）：卸载旧场景时除逐实体经 entity.destroyed 同步注销外，额外整图兜底 Clear。
            var sceneRouter = new Core.Foundation.SceneRouter.SceneRouter(
                registry, _host.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, _bus,
                spatial: _host.SpatialQuery, navigation: _host.Navigation2D);
            // 外部审核阻塞项 2 收口（见 GameplayAssembly._sceneRouter 字段判断记录）：真实
            // SceneRouter 必然晚于 GameplayAssembly 构造完成（需要 gameplay.AppState/gameplay.Hooks），
            // 回填给 GameplayAssembly.RestoreFromSlot 使用，使 death.reload_save 策略读档后能在
            // 目标地图与当前地图不同时真正切场景。
            gameplay.AttachSceneRouter(sceneRouter);

            // 三个反馈接收器由 PresentationAssemblyOptions 的回调"延迟读取"（闭包捕获的是本类型
            // 的属性访问，不是构造期的值）：PresentationAssembly 构造函数内部第 3 步装配
            // feedback_binder 时只是把这三个委托存起来，真正被调用要等到后续事件触发（构造期本身
            // 不触发任何一种反馈动作），因此可以先把委托接上、稍后（见下）再把
            // FloatingText/Freeze/Flash 三个属性赋成真正的实例。
            // W6 收口：presentationOptions.RenderOptions 复用上面同一个 renderOptions 实例（不是另建
            // 一份新的），保证 UnityViewFactory 侧的 rig 构造与 PresentationAssembly.Render 对外报告的
            // 取值一致（见上方 renderOptions 判断记录）。
            var presentationOptions = new PresentationAssemblyOptions
            {
                OnFloatingText = (entityId, styleId, text) => FloatingText?.Show(entityId, styleId, text),
                // 判断记录：见 Adapter.Unity.Shell.FrameworkResidentHost 同款判断记录——
                // CompositeFeedbackSink.Freeze(double durationMs) 传入的是毫秒，
                // FreezeFrameReceiver.Freeze(double seconds) 要的是秒，这里同样需要除以 1000 换算，
                // 否则顿帧会持续把传入的毫秒数当秒数用（如 40ms 顿帧变成 40 秒）。
                OnFreeze = durationMs => Freeze?.Freeze(durationMs / 1000.0),
                OnFlash = (entityId, profileId) => Flash?.Show(entityId, profileId),
                RenderOptions = renderOptions,
                FeedbackOptions = new global::Presentation.FeedbackBinder.Contracts.FeedbackOptions { HitFrameSync = hitFrameSyncStrategy },
            };

            // GP-PRES-04 收口（architecture/落地计划/audit-20260907/gameplay-presentation.md）：
            // 传入 _host.ResourceLoader，让 VfxPlayer/SfxPlayer 具备"首次引用加载"能力（同
            // FrameworkResidentHost/games/_template.GameBootstrap 同名判断记录）。
            // PR130-06 根治：三处装配根共享同一个 _host.Renderer3D 实例——此前只传给了上面的
            // viewFactory，PresentationAssembly/内部 VfxPlayer 拿到的 renderer3D 一直是 null，socket
            // 特效固定降级 world 坐标（见 VfxPlayer.PlaySocket 判断记录）。
            var presentation = new PresentationAssembly(
                gameplay, world, registry, _bus, presentationRng,
                viewFactory, _host.Renderer2D, _host.Camera, _host.Audio, _host.FileSystem, sceneRouter,
                presentationOptions, resourceLoader: _host.ResourceLoader, hitFrameSource: HitFrameSource,
                renderer3D: _host.Renderer3D);
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

            // ---------------------------------------------------------
            // 7) ADR-0013 §9："表现层发出 presentation.playback_finished 后由调用方转发到
            //    GameplayAssembly.NotifyPlaybackFinished()"——WaitForPlaybackPacingPolicy 本身不
            //    持有 IEventBus、不会自行订阅（见该类型源码），这一步接线只能由引擎侧完成。见文件
            //    顶部"判断记录 1b"：H4 收官后 combatParticipantsResolver 已恢复真实解析，当前示例
            //    数据集下战斗仍声明为 continuous、Pacing 因此实际不会进入 playing_back 节奏门，本
            //    订阅目前是安全的空操作；一旦数据集把战斗声明为 discrete，本订阅立即生效。
            // ---------------------------------------------------------
            _bus.Subscribe(Core.Foundation.EventBus.EventKeys.PresentationPlaybackFinished,
                _ => Gameplay!.NotifyPlaybackFinished());

            // ---------------------------------------------------------
            // 8) 固定步/帧回调改经 IClock 注册（见文件顶部"判断记录 1"）：删除此前的 MonoBehaviour
            //    FixedUpdate/Update 直驱写法，改为持有 RequestFixedStep/OnFrame 返回的
            //    SubscriptionHandle，OnDestroy 里显式退订。
            // ---------------------------------------------------------
            _fixedStepHandle = _host.Clock.RequestFixedStep(Time.fixedDeltaTime, OnFixedStep);
            _frameHandle = _host.Clock.OnFrame(OnFrameTick);

            // ---------------------------------------------------------
            // 9) 引擎侧收边任务新增：自驱 AppState 主状态走到 InWorld（Boot -> MainMenu ->
            //    Loading -> InWorld，AppStateMachineConfig.Default() 登记的合法链路）。
            //
            //    判断记录（为什么本类型需要自己驱动这条链路，而不是像 FrameworkResidentHost 一样
            //    依赖外部的菜单/读档流程）：本类型是灰盒场景（GreyBox.unity）唯一挂载的组件，从
            //    Awake 到这里为止的整条装配路径里从未出现主菜单/读档流程（没有 ShellRoot，见类型
            //    注释"灰盒场景唯一挂载的引导组件"）——AppStateHost 构造后默认停在 AppState.Boot
            //    （见 core/foundation/app_lifecycle/core/AppStateHost.cs），且此前 OnFixedStep 不
            //    检查 AppState，World.Tick 无论主状态是什么都照常推进，GreyBoxTests/UiSuiteTests
            //    的既有用例（移动、施法、拾取等全部经 HandleFixedInput 或直接
            //    IWorldSim.SubmitIntent 注入）因此从未依赖过主状态。上面新增的 OnFixedStep
            //    AppState==InWorld 门槛（见该方法判断记录）若不补这一步，会让本类型的世界推进
            //    永久卡在 Boot 状态——不是"更安全的门槛"，而是让灰盒场景整体失效。与
            //    SharedBootstrapDiscreteTests.BuildInactiveBootstrapWithDiscreteOverlay 此前用反射
            //    在测试代码里手工补的同一条 Boot->MainMenu->Loading->InWorld 链路同一处理（该测试
            //    判断记录："AppStateMachineConfig.Default() 只允许 Boot->MainMenu->Loading->InWorld
            //    这条链路，主状态若停留在默认 Boot...都会因为非法转移而静默失败"）——现在改为本类型
            //    自己在装配完成时就走完这条链路，测试代码里的等价调用因此变成一次无害的
            //    InWorld->MainMenu->Loading->InWorld 循环（AppStateMachineConfig.Default() 同样
            //    登记了 InWorld->MainMenu 这条合法转移，见该类型判断记录 3 附近的转移表），不需要
            //    改动测试。RequestTransition 对非法转移只返回 false、不抛异常（IAppStateHost 契约），
            //    因此即便未来状态机配置收紧也不会让 Bootstrap() 本身失败。
            // ---------------------------------------------------------
            Gameplay.AppState.RequestTransition(Core.Foundation.AppLifecycle.AppState.MainMenu);
            Gameplay.AppState.RequestTransition(Core.Foundation.AppLifecycle.AppState.Loading);
            Gameplay.AppState.RequestTransition(Core.Foundation.AppLifecycle.AppState.InWorld);
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

        /// <summary>由 <see cref="_fixedStepHandle"/>（<c>host.Clock.RequestFixedStep</c>）驱动，
        /// 取代此前的 MonoBehaviour FixedUpdate（见文件顶部"判断记录 1"）。<paramref name="stepSeconds"/>
        /// 恒等于注册时传入的 <c>Time.fixedDeltaTime</c>。
        ///
        /// 判断记录（引擎侧收边任务：补 <c>AppState == InWorld</c> 门槛，与
        /// <c>Adapter.Unity.Shell.FrameworkResidentHost.OnFixedStep</c> 对齐）：此前本方法只判空、
        /// 不判应用级状态——场景里若残留一个已 <c>Bootstrap()</c> 成功、但应用已经离开 InWorld（例如
        /// 回到主菜单、暂停、或场景切换过程中残留的旧引导实例，见
        /// <c>SharedBootstrap_EndTurnKeyBinding_StillWorks_WithStaleSceneBootstrapLeftAlive</c> 覆盖
        /// 的"残留场景引导实例"场景）的 <see cref="GameFoundationBootstrap"/> 实例，仍会在每个固定步
        /// 消费共享输入队列（<c>Presentation.InputMap.Update</c>）并调用
        /// <see cref="GameplayAssembly.Advance"/>——与 <see cref="FrameworkResidentHost"/> 不对称，
        /// 也会让非 InWorld 状态下场景残留的引导实例"偷走"本应只由当前活跃引导消费的一次输入
        /// 边沿（<c>HandleButtonRisingEdge</c> 的 rising-edge 状态是逐实例维护的，被残留实例抢先
        /// Update 一次会导致真正处于 InWorld 的引导实例读到"已经不是上升沿"）。改为与
        /// <see cref="FrameworkResidentHost.OnFixedStep"/> 同款判断：只有 <c>Gameplay.AppState</c>
        /// 处于 <see cref="Core.Foundation.AppLifecycle.AppState.InWorld"/> 时才继续；<c>InWorld</c>
        /// 是应用级主状态（见 <c>AppState</c> 枚举），离散模式下的 <c>awaiting_input</c>/
        /// <c>playing_back</c>（表现层回放门）都是 <c>InWorld</c> 内部的节奏子状态、不是独立的
        /// <c>AppState</c> 枚举值，因此天然仍满足本判断——不需要额外识别这两个子态，紧接着的
        /// <see cref="WaitForPlaybackPacingPolicy"/> 节奏门（若上一个离散步仍在等待表现层回放完毕，
        /// <c>IsPlaybackFinished == false</c>）才是"InWorld 内部再细分 awaiting_input/playing_back
        /// 是否可以推进下一步"这一层判定——03 第 9 节"调用方在推进下一离散步之前调用一次
        /// IsPlaybackFinished 判定是否可以推进"正是这一层；见文件顶部"判断记录 1b"，本类型战斗保持
        /// 连续模式，这条门槛目前恒为真、不产生任何跳过。<c>Presentation.InputMap.Update</c> 随
        /// <c>AppState</c> 门槛一起跳过（见下方——本身就是"输入消费"，理应与其余逻辑推进同一条
        /// 门槛，不应该在非 InWorld 状态下抢占共享输入队列的边沿状态）。</summary>
        private void OnFixedStep(double stepSeconds)
        {
            if (BootstrapFailed || World == null || Presentation == null || Gameplay == null)
            {
                return;
            }

            if (Gameplay.AppState.GetState() != Core.Foundation.AppLifecycle.AppState.InWorld)
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

        /// <summary>由 <see cref="_frameHandle"/>（<c>host.Clock.OnFrame</c>）驱动，取代此前的
        /// MonoBehaviour Update（见文件顶部"判断记录 1"）：表现插值 + 三个反馈接收器的 Tick，同时
        /// 按 <c>Presentation.Feedback.Update(dt)</c> 推进离散模式的顺序播放队列（09 第 6.4 节；
        /// <see cref="Presentation.FeedbackBinder.Core.PlaybackQueue"/> 队列非空时才有实际推进，见该
        /// 类型 Immediate/Sequential 两种模式的注释）。
        /// <para>
        /// 判断记录（拍板 12，表现层异常隔离；拍板 5/DECISIONS 收口，删除"零事件兜底短路"）：两条
        /// 判断记录与 <see cref="Adapter.Unity.Shell.FrameworkResidentHost.OnFrameTick"/> 完全同款，
        /// 见该方法源码判断记录，不重复展开。<c>_interpAccumulator</c> 字段已删除，改读
        /// <c>Gameplay.InterpolationAlpha</c>（拍板 9）。
        /// </para>
        /// </summary>
        private void OnFrameTick(double unscaledDelta)
        {
            if (BootstrapFailed || Presentation == null)
            {
                return;
            }

            var alpha = Mathf.Clamp01((float)Gameplay!.InterpolationAlpha);

            // 顿帧只暂停表现层插值/相机，不影响固定步里的逻辑推进（见 FreezeFrameReceiver 顶部
            // 判断记录）。
            if (!Freeze!.IsFrozen)
            {
                RunPresentationStep(() => Presentation.ViewBinder.SyncAll(alpha));
                RunPresentationStep(() => Presentation.Camera.Update(alpha));
            }

            RunPresentationStep(() => Presentation.Feedback.Update(unscaledDelta));

            // GP-03 根治（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：同
            // FrameworkResidentHost.OnFrameTick 同款判断记录——VfxPlayer.Update(dt) 此前本类型也
            // 没有调用，循环特效不按 lifetime 停、首次异步加载超时倒计时不推进。
            RunPresentationStep(() => Presentation.Vfx.Update(unscaledDelta));

            // 拍板 5/DECISIONS 收口：原 H4"零事件步"兜底短路已删除，见
            // FrameworkResidentHost.OnFrameTick 同款判断记录（playback_finished 现只经
            // PlaybackQueue.Finished 正常发出）。根治修复（W5c）：该判断记录里记录的"零事件步骤
            // 永久卡住"已知局限已在 core/gameplay/assembly.GameplayAssembly.SetPendingPlaybackProbe
            // / Core.Foundation.SimLoop.WaitForPlaybackPacingPolicy.HasPendingPlayback 探针 +
            // presentation/assembly.PresentationAssembly 自动接线三处结构性根治，详见该判断记录。

            // GP-10 根治：同 FrameworkResidentHost.OnFrameTick 同款判断记录（拍板 6，八个程序动画
            // 原语可视化）——本类型此前也漏了这一步，Move/Stagger/Fade/Scale 等排入 sequencer 的
            // 程序动画在本引导入口下永远不会推进采样。
            RunPresentationStep(AdvanceCharacterRigs);

            RunPresentationStep(() => FloatingText!.Tick((float)unscaledDelta));
            RunPresentationStep(() => Freeze.Tick(unscaledDelta));
            RunPresentationStep(() => Flash!.Tick(unscaledDelta));

            if (ExtraPresentationStepForTests != null)
            {
                RunPresentationStep(() => ExtraPresentationStepForTests?.Invoke(unscaledDelta));
            }
        }

        /// <summary>GP-10 根治新增：同 <see cref="Adapter.Unity.Shell.FrameworkResidentHost"/>
        /// 同名方法——推进每个仍存活的 sprite 型 View 持有的 <c>ICharacterRig.ProceduralAnim</c>
        /// 时间轴。</summary>
        private void AdvanceCharacterRigs()
        {
            var views = ViewFactory!.CreatedViews;
            for (var i = 0; i < views.Count; i++)
            {
                if (views[i].IsAlive && views[i] is IHasCharacterRig hasRig)
                {
                    hasRig.Rig.Update(Time.deltaTime);
                }
            }
        }

        /// <summary>见 <see cref="OnFrameTick"/> 判断记录：单个表现步骤的异常隔离落地——记诊断、
        /// 计数，不重新抛出，同 <see cref="Adapter.Unity.Shell.FrameworkResidentHost"/> 同名方法。</summary>
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
            // 见文件顶部"判断记录 1"：固定步/帧回调改经 IClock 注册，场景卸载/组件销毁时显式退订，
            // 避免旧回调残留继续对着一个没有任何代码再读取的旧 World/Presentation 做无意义调用。
            _fixedStepHandle?.Dispose();
            _fixedStepHandle = null;
            _frameHandle?.Dispose();
            _frameHandle = null;

            // 见 Adapter.Unity.Presentation.UnityViewFactory 顶部判断记录：ViewBinder/CameraHost
            // 不支持退订是 presentation/ 的已知缺口，本方法在引擎侧尽力而为地清理——退订能退订的
            // 部分（Presentation.Dispose()），再销毁全部本次场景创建过的 View（避免其 Sprite 实例
            // 挂在 DontDestroyOnLoad 的渲染根下跨场景重进泄漏）。
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
