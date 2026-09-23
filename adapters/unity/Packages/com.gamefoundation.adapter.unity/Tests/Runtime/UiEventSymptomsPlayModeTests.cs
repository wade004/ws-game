#nullable enable
// UiEventSymptomsPlayModeTests：消费方反馈第二十五批两个现象的框架侧真实 Unity PlayMode 复现尝试
// （见 architecture/adr/0077-ui交互域事件.md"落地缺陷与修复"一节 2026-09-23 续）。
//
// 上一刀（af675396）在核心层/表现层 xUnit 反复尝试均未能复现，已确认事件总线不同 key 之间隔离
// 正常、立即派发重入安全、条件求值异常已兜底、现有呈现组件的实体解析是防御式写法不会真抛——
// 剩余嫌疑面是这一刀能跑而上一刀跑不到的地方：真实引擎适配层（Unity）。
//
// 判断记录（为什么不经完整 FrameworkResidentHost/WowGameBootstrap 装配，而是自建
// GameplayAssembly/PresentationAssembly + 一个独立的第二个 FeedbackBinder 实例）：同
// AuditBlockersPlayModeTests.cs 顶部判断记录——本文件需要在事件 key 上精确控制"谁订阅了谁"，
// 生产装配根 PresentationAssembly.Feedback 的规则集来自 data/_sample/feedback/
// feedback.binding.json（该文件本就没有 item.equipped/ui.panel_opened 两个 key 的绑定行，不
// 需要额外修改样例数据），因此额外构造第二个独立 FeedbackBinder（复用同一个真实 IEventBus、
// 真实 IExprHostFactory、真实 SfxPlayer/VfxPlayer、真实 FloatingTextReceiver）来装载本文件自己
// 的两条复现规则，不污染任何既有样例数据文件。
//
// 判断记录（两条复现规则为什么分别绑 item.equipped 与 ui.panel_opened，不是 ui.action_invoked）：
// data/_sample/feedback/feedback.binding.json 本身已有一条真实生产规则
// feedback.sample_ui_action 绑在 ui.action_invoked -> play_sfx: sfx.sample_cast——若本文件的
// play_sfx 复现规则也绑同一个 key，一次 PublishImmediate 会同时命中生产 presentation.Feedback
// 与本文件 reproFeedback 两条独立订阅，PlaySfxCallCount 会被两条规则共同推高，无法单独归因给
// 本文件想验证的那次播放（首跑复现时已实测踩到：期望 +1，实际 +2，见 git 历史）。ui.panel_opened
// 这个 key 在样例数据里天然零订阅者，绑在这里能干净地单独观测"是否真的播放了"，且与消费方反馈
// 里 feedback.wow_ui_panel_opened_sfx 同样绑在 ui.panel_opened 的真实形态一致。
//
// 复现路径：真实按 B 键打开面板——PresentationAssembly.Panels（ADR-0077 已装配、真实三参构造、
// 装了真实 Bus 的 UiPanelRegistry 实例）.Open(panelId) 触发真实 PublishImmediate，命中本文件绑
// 在 ui.panel_opened 上的 play_sfx 规则，且该规则引用的 sfx 资源在发布前显式 Unload——确定性地
// 把它重置回"此前从未加载过"的冷启动状态（同 AuditBlockersPlayModeTests.
// VfxSfx_FirstReference_ColdStart_... 判断记录）。这一步本身就还原了消费方反馈里"发出
// ui.panel_opened 时，该 key 上恰好挂着一个仍在冷加载中的 play_sfx 订阅"这一真实前提（比消费方
// 自己的隔离实验 1"zero 订阅者"更接近他们最初、未加权宜之前的真实生产配置）。随后立即
// PublishImmediate 一个真实 ItemEquippedEvent，断言绑定在 item.equipped 上的 floating_text
// 规则确实执行（现象 1：FloatingTextReceiver.SpawnedCount 真实自增，不受 ui.panel_opened 那次
// 发布、及其挂起的冷加载状态影响）；再轮询到 sfx 资源真正加载完成，断言真实
// UnityAudio.PlaySfxCallCount 确实自增恰好一次（现象 2：不是"事件发了但没人观测到播放"）。
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
using Core.Foundation.DisplayInfo;
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
    public sealed class UiEventSymptomsPlayModeTests : PlayModeTestBase
    {
        private const string PlayerFactionId = "fac.player";
        private const string PlayerClassId = "arch.class.sample_a";
        private const string PlayerTemplateId = "creature.sample_hero";
        private const string MapAId = "world.sample_field";

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
            public FloatingTextReceiver FloatingText = null!;
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

        /// <summary>同 AuditBlockersPlayModeTests.BuildFixture：真实数据集 + 真实 GameplayAssembly/
        /// PresentationAssembly 装配，额外构造本文件自己的复现用 FeedbackBinder（见类型顶部判断
        /// 记录）。</summary>
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

            var rng = new RngHost(20260923UL);
            var world = new WorldSim(bus);
            var playerId = new Id($"unit.ui_symptoms_player_{UnityEngine.Random.Range(0, int.MaxValue)}");
            var factionId = new Id(PlayerFactionId);
            var classId = new Id(PlayerClassId);
            var mapAId = new Id(MapAId);

            var saveSystem = new SaveSystem(host.FileSystem, new SaveSystemOptions(new Id("game.ui_symptoms_test")), bus);

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

            var viewFactoryDisplayInfo = new DisplayInfoRegistry(registry, bus);
            var viewFactory = new UnityViewFactory(host.Renderer2D, new RenderConventionHost(), viewFactoryDisplayInfo, host.ResourceLoader, bus: bus, dataRegistry: registry);
            var presentationRng = new RngHost(20260923UL ^ 0x9E3779B97F4A7C15UL);
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
                timestampProvider: () => "2026-09-23T00:00:00Z");

            // ------------------------------------------------------------------
            // 复现用 FeedbackBinder：见类型顶部判断记录——独立于 presentation.Feedback（后者的规则集
            // 来自 data/_sample，天然不含 item.equipped/ui.panel_opened，本身就是"零订阅者"场景，
            // 不需要改动），只装两条本文件自己的复现规则，复用真实 IExprHostFactory/真实
            // SfxPlayer/VfxPlayer/真实 FloatingTextReceiver。
            // ------------------------------------------------------------------
            var floatingText = new FloatingTextReceiver(host.transform, id => Vec2.Zero, presentation.FloatingTextStyles);
            var reproSink = new CompositeFeedbackSink(
                presentation.Vfx, presentation.Sfx,
                onFloatingText: (entityId, styleId, text) => floatingText.Show(entityId, styleId, text),
                onFreeze: _ => { },
                onShakeCamera: _ => { },
                onFlash: (_, __) => { });

            var reproRules = new List<FeedbackRule>
            {
                new FeedbackRule(
                    new Id("feedback.repro_item_equipped_floating"),
                    CarriersEventKeys.ItemEquipped,
                    condition: null,
                    actions: new List<FeedbackAction>
                    {
                        new FloatingTextAction(
                            new Id("feedback.floating_text_style.sample_normal"),
                            TextSource.Literal(new Id("l10n.repro.item_equipped"))),
                    }),
                // 判断记录（绑在 ui.panel_opened 而不是 ui.action_invoked）：data/_sample/feedback/
                // feedback.binding.json 本身已有一条真实生产规则 feedback.sample_ui_action 绑在
                // ui.action_invoked -> play_sfx: sfx.sample_cast——若本文件的复现规则也绑同一个
                // key，一次 PublishImmediate 会同时命中两条独立规则（生产 presentation.Feedback +
                // 本文件 reproFeedback 各自的订阅），PlaySfxCallCount 会被两条规则共同推高，
                // 无法单独归因给本文件想验证的那次播放（首跑复现时已实测踩到：期望 +1，实际
                // +2）。ui.panel_opened 这个 key 在样例数据里天然零订阅者（同现象 1 的隔离前提），
                // 绑在这里能干净地把"是否真的播放了"这一件事单独观测出来，且与消费方反馈里
                // feedback.wow_ui_panel_opened_sfx 同样绑在 ui.panel_opened 的真实形态一致。
                new FeedbackRule(
                    new Id("feedback.repro_ui_panel_opened_sfx"),
                    UiEventKeys.PanelOpened,
                    condition: null,
                    actions: new List<FeedbackAction>
                    {
                        new PlaySfxAction(new Id("sfx.sample_ui_click"), fromDisplay: null),
                    }),
            };

            var reproFeedback = new FeedbackBinderCore(
                bus, gameplay.ExprHostFactory, reproRules, reproSink,
                unitAccess: gameplay.Carriers.Units);

            return new Fixture
            {
                Gameplay = gameplay, Presentation = presentation, ViewFactory = viewFactory, World = world, Bus = bus,
                PlayerId = playerId, SceneRouter = sceneRouter, Shell = shell,
                FloatingText = floatingText, ReproFeedback = reproFeedback, Host = host,
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

        // -----------------------------------------------------------------
        // 消费方反馈第二十五批复现尝试：同一冒烟窗口内先真实按 B 键打开面板（经 ADR-0077 真实三参
        // 构造的 PresentationAssembly.Panels 发布 ui.panel_opened，命中本文件绑在这个 key 上的
        // play_sfx 规则，且该规则引用的 sfx 资源已被显式 Unload、处于"从未加载过"的冷启动状态——
        // 这一步同时是现象 2 的触发点，也让 ui.panel_opened 这次发布带着一个真实挂起的冷加载，比
        // 消费方自己的隔离实验 1"zero 订阅者"更贴近他们最初未加权宜之前的真实生产配置），紧接着
        // 真实发出 item.equipped，断言绑定的 floating_text 仍然正常执行、不受前一次发布及其挂起的
        // 冷加载状态影响（现象 1）；再轮询到 sfx 资源真正加载完成，断言确实经 IAudio.PlaySfx 真实
        // 播放恰好一次（现象 2）。
        // -----------------------------------------------------------------
        [UnityTest]
        public IEnumerator PanelOpened_ThenItemEquipped_BothFeedbacksStillFire()
        {
            var fx = BuildFixture();
            _fixture = fx;

            fx.Shell.Start();
            yield return null;
            fx.SceneRouter.LoadScene(new Id(MapAId));
            yield return PumpUntilInWorld(fx);

            // 现象 2 的冷启动前提：确定性地把 sfx.sample_ui_click 的资源引用重置为"从未加载过"（同
            // AuditBlockersPlayModeTests.VfxSfx_FirstReference_ColdStart_... 判断记录），不依赖
            // "这是本批处理进程里第一次引用"这个无法保证的执行顺序假设。选 sfx.sample_ui_click 而
            // 不是 sfx.sample_hit：后者在 data/_sample/sfx/sfx.def.json 声明了 variants
            // （sample_hit_v0/sample_hit_v1 随机二选一，见该文件），resource_ref 不确定性地落在
            // 哪个具体变体上，轮询一个固定变体 id 会不可靠；sfx.sample_ui_click 是 layer=ui、无
            // variants、resource_ref 唯一确定的条目（sfx.sample_ui_click_v0），与消费方现象 2 里
            // 引用的 UI 域 sfx 形态一致，也更贴近真实复现场景。
            var sfxResourceId = new Id("sfx.sample_ui_click_v0");
            fx.Host.ResourceLoader.Unload(sfxResourceId);
            Assert.IsFalse(fx.Host.ResourceLoader.IsLoaded(sfxResourceId), "sfx 资源应当先被重置为未加载状态");

            var floatingTextBefore = fx.FloatingText.SpawnedCount;
            var playSfxCallCountBefore = fx.Host.Audio.PlaySfxCallCount;

            // 真实按 B 键打开面板：命中本文件绑在 ui.panel_opened 上的 play_sfx 规则（冷资源），
            // SfxPlayer 此刻应当只是把加载排入队列、Play() 立即返回 null，不会立即调用
            // IAudio.PlaySfx（同 AuditBlockersPlayModeTests 既有判断记录，作为对照）。
            var panelId = new Id("ui_layout_definition.repro_panel");
            fx.Presentation.Panels.Open(panelId);

            Assert.AreEqual(
                playSfxCallCountBefore, fx.Host.Audio.PlaySfxCallCount,
                "资源尚未加载完成前不应立即调用 IAudio.PlaySfx（外部审核阻塞项 4 既有约束，作为对照）");

            // 现象 1：同一调用序列内紧接着真实发出 item.equipped——此时 ui.panel_opened 那次发布
            // 触发的冷资源加载仍在异步进行中（尚未 Tick 完成），用来检验它是否会以任何方式影响到
            // 另一个不相关事件 key 上的绑定。
            fx.Bus.PublishImmediate(new ItemEquippedEvent(fx.PlayerId, new Id("item.repro_wrist"), new Id("item.slot.repro_wrist")));

            Assert.AreEqual(
                floatingTextBefore + 1, fx.FloatingText.SpawnedCount,
                "真实发出一次 ui.panel_opened（该 key 上挂着一个仍在冷加载中的 play_sfx 订阅）后，" +
                "item.equipped -> floating_text 绑定应当仍然正常触发（消费方反馈第二十五批现象 1）");

            var guard = 300;
            while (!fx.Host.ResourceLoader.IsLoaded(sfxResourceId) && guard-- > 0)
            {
                fx.Bus.DispatchPending();
                yield return null;
            }
            Assert.IsTrue(fx.Host.ResourceLoader.IsLoaded(sfxResourceId), "sfx 占位资源应当能在有限帧数内加载完成");

            // 再推进一帧，确保 LoadAsync 回调（可能在 Tick 完成的那一帧才触发）已经跑到
            // SfxPlayer.OnResourceLoadCompleted 补播放。
            yield return null;

            Assert.AreEqual(
                playSfxCallCountBefore + 1, fx.Host.Audio.PlaySfxCallCount,
                "首次引用的 sfx 资源加载完成后，经 ui.panel_opened 触发的 play_sfx 应当真实播放恰好一次（消费方反馈第二十五批现象 2）");
        }
    }
}
