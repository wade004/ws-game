#nullable enable
// DiscreteCombatTests：ADR-0013 离散时间模型引擎侧接线的独立验证（引擎侧收口任务验收 2）。
//
// 判断记录（为什么不复用 Adapter.Unity.Shell.FrameworkResidentHost/Adapter.Unity.Bootstrap.
// GameFoundationBootstrap 的既有场景）：见两者文件顶部同名判断记录——两者的 combatParticipantsResolver
// 恒返回空列表，刻意让战斗保持连续模式，以保护全部既有 PlayMode 用例（core/ 现有输入路径未把
// CastSkill/Movement 改经 TurnScheduler.SubmitIntent，离散模式下会让既有战斗类用例卡在
// awaiting_input；离散步也不推进 SimTimers，依赖真实时间衰减的用例——如 FullVerticalSlice 轮询
// 光环到期——会永久等不到）。这两处都是 core/ 侧的既有能力边界，不在本任务允许改动的 core/data
// 范围内解决。本类型因此自建一份完全独立的 GameplayAssembly/PresentationAssembly，
// combatParticipantsResolver 用默认真实解析（按半径 + 阵营），不经过任何既有单例场景，双方互不
// 干扰——真正验证 HUD/结束回合意图/AI 回合/回放门/连续-离散往返这条链路本身是接通的。
//
// 判断记录（数据集：直接从仓库源目录读取，不经 StreamingAssets 同步）：
// Adapter.Unity.EngineAdapter.UnityFileSystem 的内容根默认是 StreamingAssets/GameFoundation
// （build.ps1 -SyncContent 的产物），但构造函数本就支持显式传入 contentRoot（ADR-0016 决策 8）；
// 本类型改传仓库根目录本身，直接读 data/_framework、data/_sample（真实示例数据，含
// spawn.sample_beast_field 等）与本文件旁边的 TestData/（新增的测试专用第三根，只补一条
// found.time_model 的 combat=discrete 行，见该目录判断记录），不需要改动 build.ps1 的
// StreamingAssets 同步清单，也不依赖"最近一次跑没跑过 -SyncContent"这个前置条件。
//
// 判断记录（用合成 combat.entered 事件触发离散模式，不走真实攻击）：早期版本用
// World.SubmitIntent 提交一条真实 "cast" 意图（skill.sample_strike）触发战斗——但该技能
// base_value=15 相对示例生物 stat.stamina=8 换算出的生命值上限是一击即死（同
// VerticalSliceTests.FullVerticalSlice 判断记录），一旦生物在触发 combat.entered 的同一 tick 内
// 死亡，_activeCombatants 归零会让 TimeModelSwitch 立刻又切回连续模式，观察窗口极短、不稳定。
// 本类型改为与 VerticalSliceTests.Feedback_CritDamage_TriggersFreeze 同一手法——直接
// PublishImmediate 一条合成的 CombatEnteredEvent，绕开战斗结算本身，只验证"离散模式引擎侧接线"
// 这条链路（回合调度、awaiting_input、结束回合意图、AI 回合、回放门、连续-离散往返），生物全程
// 存活、不产生死亡这一变数。
//
// 判断记录（TearDown 清理跨用例状态泄漏）：本类型直接复用 UnityEngineHost.Ensure() 的共享引擎
// 适配层实现（host.SpatialQuery/host.Renderer2D/host.Audio/host.ResourceLoader 等，跨整个
// -runTests 批处理进程存活），但世界/GameplayAssembly 是本类型自建的一份独立实例——若不在用例
// 结束时把本用例注册进这些共享实现的状态清干净（空间索引里的玩家/生物、Renderer2D 建出的
// View、本用例专用的 UI GameObject），会污染同一批处理进程内排在后面的其它套件（实测复现：
// 不清理时 GreyBoxTests 的场景重进用例、UiSuiteTests 等会读到本用例残留的实体/View，触发
// "WorldUnitAccess: 单位不存在"一类跨用例异常与"There are 2 event systems in the scene"警告）。
// TearDown 里 world.ClearAll() + bus.DispatchPending() 让 core/carriers/assembly.EntitySpatialSyncHost
// （订阅本用例私有 bus 的 entity.destroyed）把玩家/生物从共享 host.SpatialQuery 里注销，
// viewFactory.DestroyAllCreatedViews() 清理共享 Renderer2D 建出的 Sprite，Presentation.Dispose()
// 退订全部事件订阅，最后销毁本用例自建的 UI GameObject。
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Adapter.Unity.Ui.Panels;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using NUnit.Framework;
using Presentation.Assembly;
using Presentation.Render;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class DiscreteCombatTests
    {
        private const string PlayerUnitId = "unit.sample_player";
        private const string PlayerFactionId = "fac.player";
        private const string PlayerClassId = "arch.class.sample_a";
        private const string PlayerTemplateId = "creature.sample_hero";
        private const string MapId = "world.sample_field";
        private const string BeastSpawnId = "spawn.sample_beast_field";

        private sealed class Fixture
        {
            public GameplayAssembly Gameplay = null!;
            public PresentationAssembly Presentation = null!;
            public UnityViewFactory ViewFactory = null!;
            public IWorldSim World = null!;
            public IEventBus Bus = null!;
            public Id PlayerId;
            public Id BeastId;
        }

        private Fixture? _fixture;
        private GameObject? _uiParent;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_fixture != null)
            {
                _fixture.World.ClearAll();
                _fixture.Bus.DispatchPending();
                _fixture.ViewFactory.DestroyAllCreatedViews();
                _fixture.Presentation.Dispose();
                _fixture = null;
            }

            if (_uiParent != null)
            {
                UnityEngine.Object.Destroy(_uiParent);
                _uiParent = null;
            }

            yield return null;
        }

        private Fixture BuildFixture()
        {
            var host = UnityEngineHost.Ensure();

            var definitions = EventKeys.All.Select(k => new EventDefinition(k, k.Domain, Array.Empty<string>())).ToList();
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            // Application.dataPath = "<repo>/adapters/unity/Assets"，向上三级即仓库根（见文件顶部
            // 判断记录）。
            var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            var contentFs = new UnityFileSystem(readOnlyContentMode: true, contentRoot: repoRoot);
            var frameworkSource = new FileSystemDataSource(contentFs, "data/_framework");
            var sampleSource = new FileSystemDataSource(contentFs, "data/_sample");
            var testSource = new FileSystemDataSource(
                contentFs, "adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/TestData");

            var options = PresentationSchemaCatalog.CreateOptions();
            options.FailOnUnknownTable = false;
            var registry = new DataRegistry(sampleSource, bus, options);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll(new IDataSource[] { frameworkSource, sampleSource, testSource });
            Assert.IsFalse(report.IsBlocking, "测试数据集应当能无阻断加载：" + string.Join("; ", report.Issues));

            var rng = new RngHost(20260905UL);
            var world = new WorldSim(bus);
            var playerId = new Id(PlayerUnitId);
            var mapId = new Id(MapId);
            var factionId = new Id(PlayerFactionId);
            var classId = new Id(PlayerClassId);

            var saveSystem = new SaveSystem(host.FileSystem, new SaveSystemOptions(new Id("game.discrete_combat_test")), bus);

            // 固定步长取 Time.fixedDeltaTime（同 GameFoundationBootstrap/FrameworkResidentHost 惯例），
            // pacingPolicy 用 WaitForPlaybackPacingPolicy（离散模式默认，见 03 第 3.2 节）；
            // combatParticipantsResolver 不传（默认 null）= 走 TimeModelSwitch 内建的真实解析。
            var clockHost = new SimClockHost(world, new SimLoopOptions { StepSeconds = Time.fixedDeltaTime });
            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, host.SpatialQuery, saveSystem,
                playerUnitProvider: () => playerId,
                playerFactionId: factionId,
                navigation: host.Navigation2D,
                clockHost: clockHost,
                pacingPolicy: new WaitForPlaybackPacingPolicy());

            var player = new PlayerUnit(playerId, mapId, factionId, classId)
            {
                Position = Vec2.Zero,
                TemplateId = new Id(PlayerTemplateId),
            };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(playerId, classId, raceId: null, level: 3);
            gameplay.Economy.RegisterUnit(playerId);

            // 判断记录（AppState 必须先走到 InWorld，离散战斗的 Combat 子状态才压得上去）：
            // AppStateMachineConfig.Default() 只允许 Boot->MainMenu->Loading->InWorld 这条链路
            // （见该类型源码），TimeModelSwitch.SwitchToDiscrete 末尾 _appState.PushSubState(Combat)
            // 要求主状态已经是 InWorld；GameplayAssembly.Advance 离散分支里的
            // PushSubStateIfNotCurrent(AwaitingInputSubState) 同理要求当前子状态是 Combat（构造函数
            // 里只放行 Combat<->AwaitingInput/PlayingBack 两组转移，见该类型第 10 步）。真实的
            // FrameworkResidentHost/GameFoundationBootstrap 都是经 Presentation.Shell.Start()+
            // NewGame() 间接完成这条链路，本类型自建的最小 GameplayAssembly 没有 Shell，需要手动
            // 走一遍，否则 PushSubState 全部静默失败（AppStateHost 对非法转移不抛异常，只返回
            // false），战斗虽然在 TimeModelSwitch/TurnScheduler 内部数据层面正确切到了离散模式，
            // 但 AppState.CurrentSubState 永远是 null，awaiting_input/playing_back 子态判断因此
            // 永远读不到——早期版本漏了这一步，实测复现：TimeModelSwitch.CurrentMode 正确变
            // Discrete、TurnScheduler.GetOrder 正确含双方，但 AppState.CurrentSubState 全程为空。
            gameplay.AppState.RequestTransition(Core.Foundation.AppLifecycle.AppState.MainMenu);
            gameplay.AppState.RequestTransition(Core.Foundation.AppLifecycle.AppState.Loading);
            gameplay.AppState.RequestTransition(Core.Foundation.AppLifecycle.AppState.InWorld);

            gameplay.EnterMap(mapId, playerId);

            var spawnRecord = gameplay.Spawn.GetSpawnRecord(new Id(BeastSpawnId));
            Assert.IsTrue(spawnRecord?.EntityId != null, "测试数据集应当能生成示例生物（spawn.sample_beast_field）");
            var beastId = spawnRecord!.EntityId!.Value;

            var viewFactoryDisplayInfo = new DisplayInfoRegistry(registry, bus);
            var viewFactory = new UnityViewFactory(host.Renderer2D, new RenderConventionHost(), viewFactoryDisplayInfo, host.ResourceLoader);
            var presentationRng = new RngHost(20260905UL ^ 0x9E3779B97F4A7C15UL);
            var sceneRouter = new Core.Foundation.SceneRouter.SceneRouter(
                registry, host.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, bus,
                spatial: host.SpatialQuery, navigation: host.Navigation2D);

            // QueueMode.Sequential：只有这个模式下 PlaybackQueue 才会真正入队并在清空时触发
            // Finished（见该类型顶部注释），WaitForPlaybackPacingPolicy 的回放门才有意义可测；
            // SequentialStepSeconds 取一个很小的值，让本用例不必真的等待接近真实的表现节奏。
            var presentationOptions = new PresentationAssemblyOptions
            {
                FeedbackOptions = new global::Presentation.FeedbackBinder.Contracts.FeedbackOptions
                {
                    QueueMode = global::Presentation.FeedbackBinder.Contracts.QueueMode.Sequential,
                    SequentialStepSeconds = 0.02,
                },
            };
            var presentation = new PresentationAssembly(
                gameplay, world, registry, bus, presentationRng,
                viewFactory, host.Renderer2D, host.Camera, host.Audio, host.FileSystem, sceneRouter,
                presentationOptions);

            // ADR-0013 §9：presentation.playback_finished -> GameplayAssembly.NotifyPlaybackFinished()，
            // 与 GameFoundationBootstrap/FrameworkResidentHost 同一行接线（本类型独立验证这条接线本身）。
            bus.Subscribe(EventKeys.PresentationPlaybackFinished, _ => gameplay.NotifyPlaybackFinished());

            return new Fixture
            {
                Gameplay = gameplay, Presentation = presentation, ViewFactory = viewFactory, World = world, Bus = bus,
                PlayerId = playerId, BeastId = beastId,
            };
        }

        /// <summary>手动驱动一次"固定步"，与 GameFoundationBootstrap.OnFixedStep 同一套节奏门：
        /// WaitForPlayback 模式下若仍在等待表现层回放，跳过 Advance，只推进播放队列
        /// （Presentation.Feedback.Update，同 FrameworkResidentHost.OnFrameTick）。额外的"零事件步
        /// 立即解除"分支落地 09 第 6.4 节/PlaybackQueue 类型注释判断记录"零事件时可以同步立即调用
        /// OnPlaybackFinished，等价于不等待"这条契约——离散步若没有产生任何需要回放的动作（如玩家
        /// 结束回合本身不经 world.Tick，AI 决策也可能没有命中任何 feedback.binding 规则），
        /// PlaybackQueue.Finished 永远不会触发，必须由调用方主动判定"这一步没有可等待的内容"并
        /// 立即解除，否则永久卡在 playing_back。</summary>
        private static void Tick(Fixture fixture, double dt)
        {
            fixture.Presentation.Feedback.Update(dt);

            var pacing = fixture.Gameplay.Pacing as WaitForPlaybackPacingPolicy;
            if (pacing != null && !pacing.IsPlaybackFinished)
            {
                if (fixture.Presentation.Feedback.Queue.PendingCount == 0)
                {
                    fixture.Gameplay.NotifyPlaybackFinished();
                }
                return;
            }

            fixture.Gameplay.Advance(dt);

            if (pacing != null && !pacing.IsPlaybackFinished && fixture.Presentation.Feedback.Queue.PendingCount == 0)
            {
                fixture.Gameplay.NotifyPlaybackFinished();
            }
        }

        [UnityTest]
        public IEnumerator EnterDiscreteCombat_AwaitingInput_EndTurn_AiActs_RoundAdvances_ExitsBackToContinuous()
        {
            var fixture = BuildFixture();
            _fixture = fixture;
            var dt = Time.fixedDeltaTime;

            // 让世界"落地"几拍（空间索引登记、View 创建等首拍延迟，见
            // GameFoundationBootstrap.BuildWorld 同款判断记录），此时仍是连续模式。
            for (var i = 0; i < 5; i++)
            {
                Tick(fixture, dt);
                yield return null;
            }
            Assert.AreEqual(TimeModelMode.Continuous, fixture.Gameplay.TimeModelSwitch!.CurrentMode, "战斗触发前应仍是连续模式");

            // ---- HUD：战斗前应显示"不在战斗中"，结束回合按钮隐藏。----
            _uiParent = new GameObject("DiscreteCombatTestUi", typeof(RectTransform));
            var uiParentRect = (RectTransform)_uiParent.transform;
            var turnStatus = new GameObject("TurnStatus", typeof(RectTransform)).AddComponent<TurnStatusPanel>();
            turnStatus.transform.SetParent(uiParentRect, false);
            turnStatus.Construct(uiParentRect, fixture.Gameplay, fixture.Presentation.UiIntents);
            Assert.IsFalse(turnStatus.IsEndTurnButtonVisible, "不在战斗中时不应显示结束回合按钮");

            // ---- 合成 combat.entered 触发进入战斗（不走真实攻击，见文件顶部判断记录）。----
            fixture.Bus.PublishImmediate(new Core.Rules.Common.CombatEnteredEvent(fixture.PlayerId));

            var enterGuard = 200;
            while (fixture.Gameplay.TimeModelSwitch!.CurrentMode != TimeModelMode.Discrete && enterGuard-- > 0)
            {
                Tick(fixture, dt);
                yield return null;
            }
            Assert.AreEqual(TimeModelMode.Discrete, fixture.Gameplay.TimeModelSwitch!.CurrentMode, "合成 combat.entered 后战斗时间模型应切到离散模式（found.time_model 的 combat 行 mode=discrete）");
            Assert.IsNotNull(fixture.Gameplay.TurnScheduler, "离散模式下 TurnScheduler 应已装配");
            CollectionAssert.Contains(fixture.Gameplay.TurnScheduler!.GetOrder(), fixture.PlayerId, "参战顺序应包含玩家");
            CollectionAssert.Contains(fixture.Gameplay.TurnScheduler.GetOrder(), fixture.BeastId, "参战顺序应包含示例生物（半径+阵营解析应能找到它）");

            // ---- 驱动至少一轮"轮到玩家 -> 结束回合 -> AI 行动 -> 轮次推进"，验证 HUD/EndTurn/回放门。----
            var initialRound = fixture.Gameplay.TurnScheduler.RoundIndex;
            var observedAwaitingInput = false;
            var driveGuard = 3000;
            while (fixture.Gameplay.TurnScheduler.RoundIndex < initialRound + 1 && driveGuard-- > 0)
            {
                var awaitingInput = fixture.Gameplay.AppState.CurrentSubState.HasValue &&
                    fixture.Gameplay.AppState.CurrentSubState.Value.Equals(fixture.Gameplay.AwaitingInputSubState);

                if (awaitingInput && fixture.Gameplay.TurnScheduler.GetCurrentActor()?.Equals(fixture.PlayerId) == true)
                {
                    observedAwaitingInput = true;
                    Assert.IsTrue(turnStatus.IsEndTurnButtonVisible, "轮到玩家等待输入时应显示结束回合按钮");

                    // 经真实点击回调（同用户点击）而不是直接调 TurnScheduler，验证
                    // UiIntents.EndTurn -> TurnStatusPanel 这条链路本身。
                    turnStatus.ClickEndTurn();
                }

                Tick(fixture, dt);
                yield return null;
            }

            Assert.IsTrue(observedAwaitingInput, "本用例期间应当至少观察到一次轮到玩家的 awaiting_input 子态");
            Assert.GreaterOrEqual(fixture.Gameplay.TurnScheduler.RoundIndex, initialRound + 1, "结束回合 + AI 行动应当能推进轮次");

            // ---- 结束战斗：合成 combat.left 让双方都脱战，验证切回连续模式。----
            fixture.Bus.PublishImmediate(new Core.Rules.Common.CombatLeftEvent(fixture.PlayerId));
            fixture.Bus.PublishImmediate(new Core.Rules.Common.CombatLeftEvent(fixture.BeastId));

            var exitGuard = 200;
            while (fixture.Gameplay.TimeModelSwitch!.CurrentMode != TimeModelMode.Continuous && exitGuard-- > 0)
            {
                Tick(fixture, dt);
                yield return null;
            }
            Assert.AreEqual(TimeModelMode.Continuous, fixture.Gameplay.TimeModelSwitch!.CurrentMode, "双方脱战后应当切回连续模式");
            Assert.IsFalse(turnStatus.IsEndTurnButtonVisible, "切回连续模式后结束回合按钮应重新隐藏");
        }
    }
}
