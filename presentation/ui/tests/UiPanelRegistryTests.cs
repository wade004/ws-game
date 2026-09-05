using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Presentation.Ui;
using Xunit;

namespace Tests.PresentationUi
{
    public class UiPanelRegistryTests
    {
        private static (UiPanelRegistry Registry, AppStateHost AppState, Id Inventory, Id Settings, Id Hud) Build()
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
            var registry = new UiPanelRegistry(intents, new[] { inventory, settings });

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
    }
}
