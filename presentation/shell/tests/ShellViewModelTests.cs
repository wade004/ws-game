using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.SaveSystem;
using Presentation.Shell;
using Tests.PresentationUi;
using Xunit;

namespace Tests.PresentationShell
{
    public class ShellViewModelTests
    {
        [Fact]
        public void Refresh_reads_current_page_and_slot_summaries()
        {
            var eventBus = TestSupport.BuildEventBus();
            var appState = new AppStateHost(eventBus);
            var sceneRouter = new FakeSceneRouter(appState, eventBus);
            var fileSystem = new StubFileSystem();
            var saveSystem = new SaveSystem(fileSystem, new SaveSystemOptions(new Id("game.demo")));
            var settingsStore = new SettingsStore(fileSystem);
            var difficulty = new FakeDifficultyHost();
            var inputMap = new FakeInputMapHost();
            var slotId = new Id("save.slot_1");

            var shell = new ShellHost(
                appState, sceneRouter, saveSystem, settingsStore, difficulty, inputMap, eventBus,
                (s, d, a) => new Id("world.map.starting_area"), () => new Id("world.map.starting_area"), () => "t");
            shell.Start();
            saveSystem.Save(new SaveRequest(slotId, "2026-09-05T00:00:00Z"));

            var menuEntry = new ShellMenuEntry(new Id("shell.entry.new_game"), new Id("l10n.shell.new_game"), ShellMenuAction.NewGame, null);
            var menu = new ShellMenuDefinition(new Id("shell.menu.main"), new List<ShellMenuEntry> { menuEntry });

            using var vm = new ShellViewModel(shell, saveSystem, eventBus, menu);

            Assert.Equal(ShellPage.MainMenu, vm.CurrentPage);
            Assert.Contains(vm.SlotSummaries, s => s.SlotId.Equals(slotId));
            Assert.Single(vm.MenuEntries);
        }

        [Fact]
        public void Refresh_is_triggered_by_scene_load_events()
        {
            var eventBus = TestSupport.BuildEventBus();
            var appState = new AppStateHost(eventBus);
            var sceneRouter = new FakeSceneRouter(appState, eventBus);
            var fileSystem = new StubFileSystem();
            var saveSystem = new SaveSystem(fileSystem, new SaveSystemOptions(new Id("game.demo")));
            var settingsStore = new SettingsStore(fileSystem);
            var difficulty = new FakeDifficultyHost();
            var tier = new Id("diff.tier.easy");
            difficulty.KnownTiers.Add(tier);
            var inputMap = new FakeInputMapHost();
            var slotId = new Id("save.slot_1");
            var mapId = new Id("world.map.starting_area");

            var shell = new ShellHost(
                appState, sceneRouter, saveSystem, settingsStore, difficulty, inputMap, eventBus,
                (s, d, a) => mapId, () => mapId, () => "t");
            shell.Start();

            var menu = new ShellMenuDefinition(new Id("shell.menu.main"), new List<ShellMenuEntry>());
            using var vm = new ShellViewModel(shell, saveSystem, eventBus, menu);

            shell.NewGame(slotId, tier, null);
            Assert.Equal(ShellPage.Loading, vm.CurrentPage);

            shell.Update();
            Assert.Equal(ShellPage.InWorld, vm.CurrentPage);
        }
    }
}
