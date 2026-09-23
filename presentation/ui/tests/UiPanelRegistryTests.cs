using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Presentation.Ui;
using Xunit;

namespace Tests.PresentationUi
{
    public class UiPanelRegistryTests
    {
        private static (UiPanelRegistry Registry, AppStateHost AppState, Id Inventory, Id Settings, Id Hud) Build()
            => Build(eventBus: null);

        /// <summary>ADR-0077 用例专用重载：可传入真实 <see cref="IEventBus"/> 以断言事件发布，
        /// 不带事件总线的既有 Build() 行为逐字节不变（转发到本方法，eventBus: null）。</summary>
        private static (UiPanelRegistry Registry, AppStateHost AppState, Id Inventory, Id Settings, Id Hud) Build(
            IEventBus? eventBus)
        {
            var playerId = new Id("unit.hero");
            var worldSim = new RecordingWorldSim();
            var equipment = new FakeEquipmentHost();
            var quest = new FakeQuestHost();
            var dialog = new FakeDialogHost();
            var economy = new FakeEconomyHost();
            var inputMap = new FakeInputMapHost();
            var l10n = new FakeL10nHost(new Id("l10n.en_us"), new[] { new Id("l10n.en_us") });
            var appState = new AppStateHost(TestSupport.BuildEventBus());
            appState.RequestTransition(AppState.MainMenu);
            appState.RequestTransition(AppState.Loading);
            appState.RequestTransition(AppState.InWorld);

            var audioVolume = new FakeAudioLayerVolumeHost();
            var skillBindings = new Core.Carriers.Unit.SkillBindingHost(TestSupport.BuildEventBus(), (_, __) => true);
            var intents = new UiIntents(playerId, worldSim, equipment, quest, dialog, economy, inputMap, l10n, appState,
                audioVolume, skillBindings);

            var inventory = new Id("ui.panel.inventory");
            var settings = new Id("ui.panel.settings");
            var hud = new Id("ui.panel.hud");
            var registry = new UiPanelRegistry(intents, new[] { inventory, settings }, eventBus);

            return (registry, appState, inventory, settings, hud);
        }

        [Fact]
        public void Open_menu_overlay_panel_pushes_menu_overlay_substate_once()
        {
            var (registry, appState, inventory, settings, _) = Build();

            registry.Open(inventory);
            Assert.True(registry.IsOpen(inventory));
            Assert.Equal(SubStateId.MenuOverlay, appState.CurrentSubState);

            // Opening a second menu-overlay panel while one is already open must not push twice.
            registry.Open(settings);
            Assert.Equal(SubStateId.MenuOverlay, appState.CurrentSubState);
            Assert.Single(appState.SubStateStack, s => s == SubStateId.MenuOverlay);
        }

        [Fact]
        public void Close_last_menu_overlay_panel_pops_menu_overlay_substate()
        {
            var (registry, appState, inventory, settings, _) = Build();
            registry.Open(inventory);
            registry.Open(settings);

            registry.Close(inventory);
            Assert.Equal(SubStateId.MenuOverlay, appState.CurrentSubState);

            registry.Close(settings);
            Assert.Equal(SubStateId.Explore, appState.CurrentSubState);
        }

        [Fact]
        public void Non_menu_overlay_panel_does_not_touch_substate_stack()
        {
            var (registry, appState, _, _, hud) = Build();

            registry.Open(hud);
            Assert.True(registry.IsOpen(hud));
            Assert.Equal(SubStateId.Explore, appState.CurrentSubState);

            registry.Close(hud);
            Assert.False(registry.IsOpen(hud));
        }

        [Fact]
        public void Open_and_close_are_idempotent()
        {
            var (registry, _, inventory, _, _) = Build();

            registry.Open(inventory);
            registry.Open(inventory);
            Assert.True(registry.IsOpen(inventory));

            registry.Close(inventory);
            registry.Close(inventory);
            Assert.False(registry.IsOpen(inventory));
        }

        /// <summary>ADR-0077：构造时带真实 <see cref="IEventBus"/>，Open/Close 的 0→1/1→0 边沿各自
        /// 发出一次 <see cref="UiPanelOpenedEvent"/>/<see cref="UiPanelClosedEvent"/>，携带正确的
        /// panelId；已打开/已关闭时的幂等重复调用不重复发事件。</summary>
        [Fact]
        public void Open_and_Close_WithEventBus_PublishPanelEvents_OnlyOnStateEdges()
        {
            var bus = TestSupport.BuildEventBus();
            var (registry, _, inventory, _, _) = Build(bus);

            var opened = new List<Id>();
            var closed = new List<Id>();
            bus.Subscribe<UiPanelOpenedEvent>(UiEventKeys.PanelOpened, e => opened.Add(e.PanelId));
            bus.Subscribe<UiPanelClosedEvent>(UiEventKeys.PanelClosed, e => closed.Add(e.PanelId));

            registry.Open(inventory);
            registry.Open(inventory); // 已打开，幂等，不重复发
            Assert.Equal(new[] { inventory }, opened);
            Assert.Empty(closed);

            registry.Close(inventory);
            registry.Close(inventory); // 已关闭，幂等，不重复发
            Assert.Equal(new[] { inventory }, opened);
            Assert.Equal(new[] { inventory }, closed);
        }

        /// <summary>ADR-0077：不带 <see cref="IEventBus"/> 构造（既有 2 参重载，向后兼容既有调用方）
        /// 时 Open/Close 不持有任何事件总线引用、不抛异常，行为与改动前逐字节一致——
        /// <see cref="Open_menu_overlay_panel_pushes_menu_overlay_substate_once"/> 等既有四条用例
        /// 全部经本文件顶部的 <c>Build()</c>（转发 eventBus: null）跑过，本用例只补一条显式使用
        /// 既有 2 参构造函数（不经过新增的 3 参重载）的直接证据。</summary>
        [Fact]
        public void Open_and_Close_WithoutEventBus_DoesNotThrow_UsingLegacyTwoArgConstructor()
        {
            var playerId = new Id("unit.hero");
            var worldSim = new RecordingWorldSim();
            var intents = new UiIntents(
                playerId, worldSim, new FakeEquipmentHost(), new FakeQuestHost(), new FakeDialogHost(),
                new FakeEconomyHost(), new FakeInputMapHost(),
                new FakeL10nHost(new Id("l10n.en_us"), new[] { new Id("l10n.en_us") }),
                new AppStateHost(TestSupport.BuildEventBus()), new FakeAudioLayerVolumeHost(),
                new Core.Carriers.Unit.SkillBindingHost(TestSupport.BuildEventBus(), (_, __) => true));
            var inventory = new Id("ui.panel.inventory");
            var registry = new UiPanelRegistry(intents, new[] { inventory }); // 既有 2 参重载

            registry.Open(inventory);
            Assert.True(registry.IsOpen(inventory));
            registry.Close(inventory);
            Assert.False(registry.IsOpen(inventory));
        }
    }
}
