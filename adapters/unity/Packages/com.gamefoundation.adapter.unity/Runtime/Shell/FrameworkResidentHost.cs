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
using System;
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

        private const string ActionSetId = "actionset.shell";
        private const string ActionMove = "input.action.move";
        private const string ActionAttack = "input.action.shell_attack";
        private const string ActionSkill1 = "input.action.shell_skill_1";

        public GameplayAssembly Gameplay { get; private set; } = null!;
        public PresentationAssembly Presentation { get; private set; } = null!;
        public IWorldSim World { get; private set; } = null!;
        public Id PlayerId { get; private set; }
        public Id? BeastEntityId { get; private set; }
        public UnityViewFactory ViewFactory { get; private set; } = null!;
        public FloatingTextReceiver FloatingText { get; private set; } = null!;
        public FreezeFrameReceiver Freeze { get; private set; } = null!;
        public FlashReceiver Flash { get; private set; } = null!;
        public bool BootstrapFailed { get; private set; }

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
        private double _interpAccumulator;
        private readonly System.Collections.Generic.Dictionary<string, bool> _wasActionActive =
            new System.Collections.Generic.Dictionary<string, bool>(StringComparer.Ordinal);

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
            var source = new FileSystemDataSource(contentFs, "data/_sample");
            var options = PresentationSchemaCatalog.CreateOptions();
            options.FailOnUnknownTable = false;

            var registry = new DataRegistry(source, _bus, options);
            _registry = registry;
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                BootstrapFailed = true;
                Debug.LogError("[FrameworkResidentHost] 数据集校验未通过，已停止：" + string.Join("; ", report.Issues));
                return;
            }

            var rng = new RngHost(Seed);
            var world = new WorldSim(_bus);
            World = world;

            PlayerId = new Id(PlayerUnitId);
            var factionId = new Id(PlayerFactionId);
            _classId = new Id(PlayerClassId);

            var gameplay = new GameplayAssembly(
                _bus, registry, rng, world, _host.SpatialQuery,
                playerUnitProvider: () => PlayerId,
                playerFactionId: factionId,
                navigation: _host.Navigation2D);
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

            var viewFactoryDisplayInfo = new DisplayInfoRegistry(registry, _bus);
            var viewFactory = new UnityViewFactory(_host.Renderer2D, new RenderConventionHost(), viewFactoryDisplayInfo, _host.ResourceLoader);
            ViewFactory = viewFactory;

            var presentationRng = new RngHost(Seed ^ 0x9E3779B97F4A7C15UL);
            // spatial/navigation 注入（ADR-0016 决策 7、场景卸载级联清理，见 SceneRouter 构造函数
            // 判断记录）。
            var sceneRouter = new Core.Foundation.SceneRouter.SceneRouter(
                registry, _host.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, _bus,
                spatial: _host.SpatialQuery, navigation: _host.Navigation2D);

            var presentationOptions = new PresentationAssemblyOptions
            {
                SaveGameId = new Id("game.sample"),
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
                ActionBarSlotBindingResolver = ResolveActionBarSlot,
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
            };

            var presentation = new PresentationAssembly(
                gameplay, world, registry, _bus, presentationRng,
                viewFactory, _host.Renderer2D, _host.Camera, _host.Audio, _host.FileSystem, sceneRouter,
                presentationOptions);
            Presentation = presentation;

            FloatingText = new FloatingTextReceiver(_host.transform, id => world.GetEntity(id)?.Position, presentation.FloatingTextStyles);
            Freeze = new FreezeFrameReceiver();
            Flash = new FlashReceiver(presentation.ViewBinder);

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

            presentation.InputMap.DeclareActionSet(new Id(ActionSetId), new[]
            {
                new ActionDefinition(new Id(ActionMove), ActionKind.Axis2D,
                    new[] { "composite2d:key:w|key:s|key:a|key:d" }, description: "Shell 演示：平面移动"),
                new ActionDefinition(new Id(ActionAttack), ActionKind.Button,
                    new[] { "key:j", "mouse:MouseLeft" }, description: "Shell 演示：普攻（skill.sample_strike）"),
                new ActionDefinition(new Id(ActionSkill1), ActionKind.Button,
                    new[] { "key:digit1" }, description: "Shell 演示：技能 1（skill.sample_burn）"),
            });
        }

        private Id? ResolveActionBarSlot(int slotIndex) => slotIndex switch
        {
            0 => new Id(AttackSkillId),
            1 => new Id(Skill1Id),
            _ => (Id?)null,
        };

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
            HandleFixedInput();
            World.Tick(SimStep.Continuous(Time.fixedDeltaTime));
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
