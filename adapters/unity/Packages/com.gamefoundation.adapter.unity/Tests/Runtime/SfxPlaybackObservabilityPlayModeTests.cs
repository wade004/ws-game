#nullable enable
// SfxPlaybackObservabilityPlayModeTests：ADR-0083 第二部分验收——把消费方第二十五/二十六批"真实
// 发出 ui.action_invoked 后、在 0.4 秒窗口里轮询播放状态、从未观测到冷资源音效播放"这一观测方式
// 与新增的单调累计诊断（ISfxPlaybackDiagnostics）之间的差异固化成用例：贴着消费方的黑盒轮询方式
// （按固定间隔查场景里的 AudioSource 是否在播放某个具体 clip），同时断言这种轮询方式抓不抓得到，
// 与 PresentationAssembly.SfxPlaybackDiagnostics 累计计数能不能抓到。
//
// 判断记录（未经编译与运行验证）：本文件按任务书要求"只写、不跑"——本次改动的工作树在系统临时
// 目录下的深层 scratchpad 路径，按 AGENTS.md"不要在深层 scratchpad 工作树里跑 Unity 测试步骤"，
// Unity 侧真实验证交由主检出补跑，本文件未经 Unity 批处理编译/PlayMode 实际运行确认。
//
// 判断记录（为什么复用 UiEventSymptomsPlayModeTests 的装配方式，而不是引用它）：同该文件顶部
// 判断记录——需要在事件 key 上精确控制"谁订阅了谁"，生产装配根 PresentationAssembly.Feedback
// 的规则集来自 data/_sample/feedback/feedback.binding.json，其中已有一条真实生产规则
// feedback.sample_ui_action 绑定 ui.action_invoked -> play_sfx: sfx.sample_cast（0.4 秒时长，
// 见 assets/_sample/sfx/sample_cast_v0.wav），与本文件想验证的"极短瞬态窗口"场景（消费方实际
// 报告的 wow_ui_click_01/wow_ui_open_01 对应本框架占位素材 ui_click_01.wav/ui_open_01.wav，
// 时长仅 0.05s/0.15s，样例数据里对应的是 sfx.sample_ui_click，见该表 sfx.sample_ui_click_v0
// 行，layer=ui、无 variants）不是同一条。真实按 B 键/触发 UI 动作会同时命中这两条独立规则
// （生产 presentation.Feedback 与本文件自己额外挂的一条 repro 规则），因此本文件额外构造一条
// 只绑 sfx.sample_ui_click 的 repro 规则（复用真实 IEventBus/真实 presentation.Sfx，不是另建一份
// SfxPlayer），预先把 sfx.sample_cast 的资源"暖起来"（提前触发一次加载并等待完成），避免它的冷
// 加载与本文件真正想测量的 sfx.sample_ui_click 冷加载相互干扰计时窗口。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Rules.Common;
using NUnit.Framework;
using Presentation.Assembly;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.Render;
using Presentation.Shell;
using Presentation.Ui;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using UnityEngine;
using UnityEngine.TestTools;
using FeedbackBinderCore = Presentation.FeedbackBinder.Core.FeedbackBinder;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class SfxPlaybackObservabilityPlayModeTests : PlayModeTestBase
    {
        private const string PlayerFactionId = "fac.player";
        private const string PlayerClassId = "arch.class.sample_a";
        private const string PlayerTemplateId = "creature.sample_hero";
        private const string MapAId = "world.sample_field";

        private static readonly Id ColdSfxId = new Id("sfx.sample_ui_click");
        private static readonly Id ColdResourceId = new Id("sfx.sample_ui_click_v0");
        private static readonly Id WarmSfxId = new Id("sfx.sample_cast");
        private static readonly Id WarmResourceId = new Id("sfx.sample_cast_v0");

        private sealed class Fixture
        {
            public GameplayAssembly Gameplay = null!;
            public PresentationAssembly Presentation = null!;
            public UnityViewFactory ViewFactory = null!;
            public IWorldSim World = null!;
            public IEventBus Bus = null!;
            public Id PlayerId;
            public Core.Foundation.SceneRouter.SceneRouter SceneRouter = null!;
            public ShellHost Shell = null!;
            public FeedbackBinderCore ReproFeedback = null!;
            public UnityEngineHost Host = null!;
        }

        private Fixture? _fixture;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_fixture != null)
            {
                _fixture.ReproFeedback.Dispose();
                _fixture.Shell.Dispose();
                _fixture.World.ClearAll();
                _fixture.Bus.DispatchPending();
                _fixture.ViewFactory.DestroyAllCreatedViews();
                _fixture.Presentation.Dispose();
                _fixture = null;
            }

            yield return null;
        }

        /// <summary>同 UiEventSymptomsPlayModeTests.BuildFixture：真实数据集 + 真实
        /// GameplayAssembly/PresentationAssembly 装配，额外构造本文件自己的复现用 FeedbackBinder
        /// （见类型顶部判断记录）。</summary>
        private Fixture BuildFixture()
        {
            var host = UnityEngineHost.Ensure();

            var definitions = EventKeys.All.Select(k => new EventDefinition(k, k.Domain, Array.Empty<string>())).ToList();
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            var contentFs = new UnityFileSystem(readOnlyContentMode: true, contentRoot: repoRoot);
            var frameworkSource = new FileSystemDataSource(contentFs, "data/_framework");
            var sampleSource = new FileSystemDataSource(contentFs, "data/_sample");

            var options = PresentationSchemaCatalog.CreateOptions();
            options.FailOnUnknownTable = false;
            var registry = new DataRegistry(sampleSource, bus, options);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll(new IDataSource[] { frameworkSource, sampleSource });
            Assert.IsFalse(report.IsBlocking, "测试数据集应当能无阻断加载：" + string.Join("; ", report.Issues));

            var rng = new RngHost(20260924UL);
            var world = new WorldSim(bus);
            var playerId = new Id($"unit.sfx_observability_player_{UnityEngine.Random.Range(0, int.MaxValue)}");
            var factionId = new Id(PlayerFactionId);
            var classId = new Id(PlayerClassId);
            var mapAId = new Id(MapAId);

            var saveSystem = new SaveSystem(host.FileSystem, new SaveSystemOptions(new Id("game.sfx_observability_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, host.SpatialQuery, saveSystem,
                playerUnitProvider: () => playerId,
                playerFactionId: factionId,
                navigation: host.Navigation2D);

            var player = new PlayerUnit(playerId, mapAId, factionId, classId)
            {
                Position = Vec2.Zero,
                TemplateId = new Id(PlayerTemplateId),
            };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(playerId, classId, raceId: null, level: 3);
            gameplay.Economy.RegisterUnit(playerId);

            var viewFactoryDisplayInfo = new Core.Foundation.DisplayInfo.DisplayInfoRegistry(registry, bus);
            var viewFactory = new UnityViewFactory(host.Renderer2D, new RenderConventionHost(), viewFactoryDisplayInfo, host.ResourceLoader, bus: bus, dataRegistry: registry);
            var presentationRng = new RngHost(20260924UL ^ 0x9E3779B97F4A7C15UL);
            var sceneRouter = new Core.Foundation.SceneRouter.SceneRouter(
                registry, host.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, bus,
                spatial: host.SpatialQuery, navigation: host.Navigation2D);
            gameplay.AttachSceneRouter(sceneRouter);

            var presentation = new PresentationAssembly(
                gameplay, world, registry, bus, presentationRng,
                viewFactory, host.Renderer2D, host.Camera, host.Audio, host.FileSystem, sceneRouter,
                resourceLoader: host.ResourceLoader);

            gameplay.RegisterPersistables(presentation.SaveSystem, player);

            sceneRouter.RegisterPostLoadHook(mapId =>
            {
                if (world.GetEntity(playerId) == null)
                {
                    world.AddEntity(player);
                    bus.DispatchPending();
                }
                gameplay.EnterMap(mapId, playerId);
            });

            var settingsStore = new SettingsStore(host.FileSystem);
            var inputMap = new InputMapHost(bus);
            var shell = new ShellHost(
                gameplay.AppState, sceneRouter, presentation.SaveSystem, settingsStore, gameplay.Difficulty, inputMap, bus,
                newGameStarter: (slot, diff, arch) => mapAId,
                timestampProvider: () => "2026-09-24T00:00:00Z");

            // ------------------------------------------------------------------
            // 复现用 FeedbackBinder：见类型顶部判断记录——只额外挂一条 sfx.sample_ui_click（真实
            // 短音效，0.05 秒，见 assets/_sample/sfx/sample_ui_click_v0.wav）绑在
            // ui.action_invoked 上，复用真实 presentation.Sfx（与生产 presentation.Feedback 共用
            // 同一个 SfxPlayer 实例，诊断计数因此在同一份 PlaybackDiagnostics 上叠加）。
            // ------------------------------------------------------------------
            var reproSink = new CompositeFeedbackSink(
                presentation.Vfx, presentation.Sfx,
                onFloatingText: (_, __, ___) => { },
                onFreeze: _ => { },
                onShakeCamera: _ => { },
                onFlash: (_, __) => { });

            var reproRules = new List<FeedbackRule>
            {
                new FeedbackRule(
                    new Id("feedback.repro_ui_action_short_click_sfx"),
                    UiEventKeys.ActionInvoked,
                    condition: null,
                    actions: new List<FeedbackAction>
                    {
                        new PlaySfxAction(ColdSfxId, fromDisplay: null),
                    }),
            };

            var reproFeedback = new FeedbackBinderCore(
                bus, gameplay.ExprHostFactory, reproRules, reproSink,
                unitAccess: gameplay.Carriers.Units);

            return new Fixture
            {
                Gameplay = gameplay, Presentation = presentation, ViewFactory = viewFactory, World = world, Bus = bus,
                PlayerId = playerId, SceneRouter = sceneRouter, Shell = shell,
                ReproFeedback = reproFeedback, Host = host,
            };
        }

        private static IEnumerator PumpUntilInWorld(Fixture fx, int maxFrames = 600)
        {
            var guard = maxFrames;
            while (fx.Shell.Page != ShellPage.InWorld && guard-- > 0)
            {
                fx.Shell.Update();
                fx.Bus.DispatchPending();
                yield return null;
            }
            Assert.AreEqual(ShellPage.InWorld, fx.Shell.Page, "场景加载应当在有限帧数内完成并进入 InWorld");
        }

        private static IEnumerator PumpUntilLoaded(Fixture fx, Id resourceId, int maxFrames = 300)
        {
            var guard = maxFrames;
            while (!fx.Host.ResourceLoader.IsLoaded(resourceId) && guard-- > 0)
            {
                fx.Bus.DispatchPending();
                yield return null;
            }
            Assert.IsTrue(fx.Host.ResourceLoader.IsLoaded(resourceId), $"资源 {resourceId} 应当能在有限帧数内加载完成");
        }

        /// <summary>贴着消费方的黑盒轮询方式：按固定间隔查场景里是否存在一个正在播放、且
        /// clip 名恰好等于目标资源 id 的 <see cref="AudioSource"/>（<c>UnityResourceLoader.
        /// TryDecodeWav</c> 用 <c>resourceId.Value</c> 给 <c>AudioClip</c> 命名，见该方法），
        /// 不经由本框架任何内部计数——这正是消费方拿不到 <c>internal</c> 诊断字段时唯一可行的黑盒
        /// 观测方式。
        /// <para>
        /// 判断记录（轮询间隔取 100ms、共 4 次，覆盖满 0.4 秒窗口——2026-09-24 已在真实 Unity 运行验证的
        /// 判断）：按 SfxColdLoadTimingTests 的同构架构实测，冷加载->真正播放的墙钟延迟量级在
        /// 十余毫秒（该用例某次实测 19.08ms，frames=1，见 presentation/vfx_sfx/tests/
        /// SfxColdLoadTimingTests.cs），叠加 sfx.sample_ui_click_v0 实际播放时长仅 0.05 秒
        /// （2205 帧 @44100Hz），真正"正在播放"的窗口预计落在事件派发后约
        /// [20ms, 70ms] 这个远小于 100ms 的区间内；4 次固定间隔采样点（100/200/300/400ms）均落在
        /// 该窗口之外，构造上预期"一次都不命中"。这是本文件按已实测的架构延迟数量级做的推算，
        /// 不是在真实 Unity 引擎里实测过的数字——真实引擎的帧率抖动、AudioSource.Play() 真正置位
        /// isPlaying 的确切时机等因素可能使实际窗口位置与本判断有出入，本用例是否真的"一次都不
        /// 命中"需要主检出真实跑一次 PlayMode 来验证，如与本判断不符，应先核实是否为真实回归
        /// （见 AGENTS.md"Unity PlayMode 测试失败先用 unity_test_triage.py 分诊"），而不是直接
        /// 放宽本断言。
        /// </para></summary>
        /// <para>判断记录（2026-09-24 主检出补跑时加）：同一轮采样里同时观测多个资源，而不是只观测
        /// 被测的那个冷资源。否定断言"轮询抓不到"最容易因为错误的原因为真——例如剪辑命名与资源
        /// 引用 id 对不上、或者根本没有任何 <c>AudioSource</c> 被建出来，此时本方法会恒返回 false，
        /// 断言照样通过，但结论是假的。把一个"一定在播、且时长足够长"的资源作为阳性对照一并采样，
        /// 阳性对照为真才证明本方法确实有观测能力，此时冷资源那一路的 false 才是有意义的否定。</para>
        private static IEnumerator PollObservesPlaying(Id[] resourceIds, int samples, float intervalSeconds, System.Action<bool[]> onResult)
        {
            var observed = new bool[resourceIds.Length];
            for (var i = 0; i < samples; i++)
            {
                yield return new WaitForSecondsRealtime(intervalSeconds);
                var sources = UnityEngine.Object.FindObjectsOfType<AudioSource>();
                for (var k = 0; k < resourceIds.Length; k++)
                {
                    observed[k] |= sources.Any(a => a.isPlaying && a.clip != null && a.clip.name == resourceIds[k].Value);
                }
            }
            onResult(observed);
        }

        // -----------------------------------------------------------------
        // 消费方反馈第二十五/二十六批复现：真实发出 ui.action_invoked，命中一条绑定短音效
        // （sfx.sample_ui_click，0.05 秒）的 play_sfx 规则，该资源处于"从未加载过"的冷启动状态。
        // 同时断言：(a) 消费方式的黑盒轮询（0.4 秒窗口，按 100ms 间隔查 AudioSource.isPlaying）
        // 抓不抓得到；(b) 新增的 PresentationAssembly.SfxPlaybackDiagnostics 单调累计计数能不能
        // 抓到——用同一次真实播放，把"轮询会漏"与"累计诊断不会漏"这两个结论固化成同一条用例。
        // -----------------------------------------------------------------
        [UnityTest]
        public IEnumerator UiActionInvoked_ColdShortSfx_PollingMisses_ButPlaybackDiagnosticsCatchesIt()
        {
            var fx = BuildFixture();
            _fixture = fx;

            fx.Shell.Start();
            yield return null;
            fx.SceneRouter.LoadScene(new Id(MapAId));
            yield return PumpUntilInWorld(fx);

            // 判断记录（预热 sfx.sample_cast，避免它自己的冷加载与本用例真正想测的
            // sfx.sample_ui_click 冷加载相互干扰）：data/_sample/feedback/feedback.binding.json
            // 的生产规则 feedback.sample_ui_action 同样绑在 ui.action_invoked 上（-> play_sfx:
            // sfx.sample_cast），真实发布一次 ui.action_invoked 会同时命中它——不预热的话，两个
            // 资源同时冷加载，轮询窗口内可能观测到 sample_cast（而不是本用例关心的
            // sample_ui_click）在播放，污染判断记录里"预计一次都不命中"这一推算的前提。
            var warmedUp = false;
            fx.Host.ResourceLoader.LoadAsync(WarmResourceId, ResourceKind.Audio, (_, success) => warmedUp = success);
            var warmGuard = 300;
            while (!fx.Host.ResourceLoader.IsLoaded(WarmResourceId) && warmGuard-- > 0)
            {
                yield return null;
            }
            Assert.IsTrue(fx.Host.ResourceLoader.IsLoaded(WarmResourceId) && warmedUp, "sample_cast 应当能预热加载成功");

            // 现象 2 的冷启动前提：确定性地把 sfx.sample_ui_click_v0 重置为"从未加载过"（同
            // UiEventSymptomsPlayModeTests.PanelOpened_ThenItemEquipped_BothFeedbacksStillFire
            // 判断记录），不依赖执行顺序假设。
            fx.Host.ResourceLoader.Unload(ColdResourceId);
            Assert.IsFalse(fx.Host.ResourceLoader.IsLoaded(ColdResourceId), "sfx.sample_ui_click_v0 应当先被重置为未加载状态");

            var diagnostics = fx.Presentation.SfxPlaybackDiagnostics;
            var requestedBefore = diagnostics.PlayRequestedCount;
            var startedBefore = diagnostics.PlayStartedCount;

            // 真实事件：与消费方报告完全一致的事件类型（UiActionInvokedEvent，key = ui.action_invoked），
            // 真实 PublishImmediate，不经任何测试专用旁路。
            var panelId = new Id("ui_layout_definition.repro_panel");
            fx.Bus.PublishImmediate(new UiActionInvokedEvent(panelId, "cast_skill"));

            // 请求计数必须立即反映两条规则各自的一次播放请求（sample_cast 已预热立即播放一次，
            // sample_ui_click 冷资源排队等待）——不依赖抓取任何瞬态，这正是本诊断契约的意义。
            Assert.AreEqual(requestedBefore + 2, diagnostics.PlayRequestedCount,
                "两条绑在 ui.action_invoked 上的 play_sfx 规则都应当立即计入一次播放请求");
            Assert.AreEqual(startedBefore + 1, diagnostics.PlayStartedCount,
                "已预热的 sample_cast 应当立即开始播放，冷资源 sample_ui_click 此刻还不能计入开始播放");

            // (a) 消费方式的黑盒轮询：0.4 秒窗口，100ms 间隔，共 4 次，只认 clip 名等于
            // sfx.sample_ui_click_v0 的 AudioSource（见 PollObservesPlaying 判断记录）。
            // 阳性对照（见 PollObservesPlaying 判断记录）：已预热的 sample_cast 时长 0.4 秒、在本次
            // 发布时就已开始播放，同一轮采样必须能观测到它——否则说明本轮询方法根本没有观测能力，
            // 冷资源那一路的"抓不到"就不能作为结论。
            var observed = new bool[2];
            yield return PollObservesPlaying(
                new[] { ColdResourceId, WarmResourceId }, samples: 4, intervalSeconds: 0.1f, r => observed = r);
            var observedPlaying = observed[0];
            var observedWarmControl = observed[1];

            // (b) 累计诊断不依赖抓瞬态：轮询窗口结束后，冷资源应当已经真正加载完成并补播放过——
            // 继续推进有限帧数等待加载收尾（同既有 PlayMode 用例惯例）。
            yield return PumpUntilLoaded(fx, ColdResourceId);
            yield return null; // 确保 LoadAsync 回调（可能在 Tick 完成的那一帧才触发）已经跑到 SfxPlayer.OnResourceLoadCompleted。

            Assert.AreEqual(startedBefore + 2, diagnostics.PlayStartedCount,
                "sfx.sample_ui_click 冷资源加载完成后，PlayStartedCount 应当补记这次开始播放（消费方观测不到播放≠框架没播）");
            Assert.AreEqual(0, diagnostics.PlayDroppedCount, "两次播放请求都不应该被判定为丢弃");
            Assert.NotNull(diagnostics.LastPlay);
            Assert.AreEqual(ColdResourceId, diagnostics.LastPlay!.Value.ResourceRef,
                "最近一次播放记录应当是后完成的 sfx.sample_ui_click_v0（sample_cast 更早播放完成）");

            // 判断记录（本条断言是本用例的核心假设，见 PollObservesPlaying 判断记录"尚未经真实
            // Unity 运行验证"）：0.05 秒的播放窗口应当整体落在四次 100ms 间隔采样点之间，消费方式
            // 的黑盒轮询预期抓不到——与上面已经确认的"确实播放过恰好一次"（诊断计数）形成对照，
            // 这正是消费方反馈"轮询不到≠没播放"的根因。若本条在真实 Unity 环境下断言失败，先按
            // AGENTS.md 分诊流程核实是否为真实回归，而不是直接放宽或删除本断言。
            Assert.IsTrue(observedWarmControl,
                "阳性对照失败：同一轮轮询连时长 0.4 秒、确定在播的 sample_cast 都没观测到，" +
                "说明本轮询方法没有观测能力，下面那条否定断言不成立——先修观测方法，不要放宽断言");
            Assert.IsFalse(observedPlaying,
                "消费方式的 0.4 秒黑盒轮询（100ms 间隔）预期抓不到 0.05 秒的瞬态播放窗口，" +
                "即便该次播放已经真实发生（见上方 PlayStartedCount/LastPlay 断言）");
        }
    }
}
