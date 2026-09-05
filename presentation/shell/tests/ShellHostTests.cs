using Adapters.Stub;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;
using Presentation.Shell;
using Tests.PresentationUi;
using Xunit;

namespace Tests.PresentationShell
{
    public class ShellHostTests
    {
        private sealed class Fixture
        {
            public readonly Id GameId = new Id("game.demo");
            public readonly Id EasyTier = new Id("diff.tier.easy");
            public readonly Id StartMap = new Id("world.map.starting_area");
            public readonly Id SlotId = new Id("save.slot_1");

            public readonly StubFileSystem FileSystem = new StubFileSystem();
            public readonly AppStateHost AppState;
            public readonly FakeSceneRouter SceneRouter;
            public readonly SaveSystem SaveSys;
            public readonly SettingsStore SettingsStoreInstance;
            public readonly FakeDifficultyHost Difficulty = new FakeDifficultyHost();
            public readonly FakeInputMapHost InputMap = new FakeInputMapHost();
            public readonly Core.Foundation.EventBus.IEventBus EventBus;

            public Id? StartedSlotId;
            public Id? StartedDifficultyId;
            public Id LoadedMapIdResult;

            public readonly ShellHost Shell;

            public Fixture()
            {
                EventBus = TestSupport.BuildEventBus();
                AppState = new AppStateHost(EventBus);
                SceneRouter = new FakeSceneRouter(AppState, EventBus);
                SaveSys = new SaveSystem(FileSystem, new SaveSystemOptions(GameId));
                SettingsStoreInstance = new SettingsStore(FileSystem);
                Difficulty.KnownTiers.Add(EasyTier);
                LoadedMapIdResult = StartMap;

                Shell = new ShellHost(
                    AppState,
                    SceneRouter,
                    SaveSys,
                    SettingsStoreInstance,
                    Difficulty,
                    InputMap,
                    EventBus,
                    (slotId, difficultyId, archetypeId) =>
                    {
                        StartedSlotId = slotId;
                        StartedDifficultyId = difficultyId;
                        return StartMap;
                    },
                    () => "2026-09-05T00:00:00Z",
                    loadedMapIdResolver: () => LoadedMapIdResult);
            }
        }

        [Fact]
        public void Start_transitions_boot_to_main_menu()
        {
            var f = new Fixture();

            Assert.True(f.Shell.Start());
            Assert.Equal(AppState.MainMenu, f.AppState.GetState());
            Assert.Equal(ShellPage.MainMenu, f.Shell.Page);
        }

        [Fact]
        public void Navigation_methods_switch_main_menu_sub_pages()
        {
            var f = new Fixture();
            f.Shell.Start();

            f.Shell.ShowSlots();
            Assert.Equal(ShellPage.SaveSlots, f.Shell.Page);

            f.Shell.ShowNewGameSetup();
            Assert.Equal(ShellPage.NewGameSetup, f.Shell.Page);

            f.Shell.OpenSettings();
            Assert.Equal(ShellPage.Settings, f.Shell.Page);
        }

        [Fact]
        public void NewGame_applies_difficulty_calls_starter_saves_and_loads_scene()
        {
            var f = new Fixture();
            f.Shell.Start();

            var ok = f.Shell.NewGame(f.SlotId, f.EasyTier, null);

            Assert.True(ok);
            Assert.Equal(f.EasyTier, f.Difficulty.CurrentTier);
            Assert.Equal(f.SlotId, f.StartedSlotId);
            Assert.Equal(f.EasyTier, f.StartedDifficultyId);
            Assert.Single(f.SceneRouter.LoadSceneCalls);
            Assert.Equal(f.StartMap, f.SceneRouter.LoadSceneCalls[0]);
            Assert.Equal(ShellPage.Loading, f.Shell.Page);
            Assert.True(f.SaveSys.SlotExists(f.SlotId));
        }

        [Fact]
        public void NewGame_completes_scene_loading_via_update()
        {
            var f = new Fixture();
            f.Shell.Start();
            f.Shell.NewGame(f.SlotId, f.EasyTier, null);

            Assert.True(f.Shell.IsLoading);

            f.Shell.Update();

            Assert.False(f.Shell.IsLoading);
            Assert.Equal(ShellPage.InWorld, f.Shell.Page);
            Assert.Equal(1.0, f.Shell.LoadingProgress);
        }

        [Fact]
        public void NewGame_returns_false_for_unknown_difficulty_tier()
        {
            var f = new Fixture();
            f.Shell.Start();

            var ok = f.Shell.NewGame(f.SlotId, new Id("diff.tier.unknown"), null);

            Assert.False(ok);
            Assert.Empty(f.SceneRouter.LoadSceneCalls);
            Assert.Null(f.StartedSlotId);
        }

        [Fact]
        public void NewGame_returns_false_when_scene_router_rejects_unknown_scene()
        {
            var f = new Fixture();
            f.Shell.Start();
            f.SceneRouter.ThrowUnknownSceneOnLoad = true;

            var ok = f.Shell.NewGame(f.SlotId, f.EasyTier, null);

            Assert.False(ok);
            // 难度已应用、初始存档已写入（这两步在场景加载之前），只有场景加载这一步失败；
            // 场景路由被拒绝不回滚前面已经生效的步骤，见 ShellHost.NewGame 判断记录。
            Assert.Equal(f.EasyTier, f.Difficulty.CurrentTier);
            Assert.True(f.SaveSys.SlotExists(f.SlotId));
        }

        [Fact]
        public void NewGame_returns_false_when_difficulty_apply_rejects_mid_switch()
        {
            var f = new Fixture();
            f.Shell.Start();
            f.Difficulty.AllowMidSwitch = false;
            f.Difficulty.Apply(f.EasyTier, Core.Gameplay.Difficulty.DifficultyScope.Global, null);

            var ok = f.Shell.NewGame(f.SlotId, f.EasyTier, null);

            Assert.False(ok);
            Assert.Empty(f.SceneRouter.LoadSceneCalls);
        }

        [Fact]
        public void LoadGame_reads_save_and_loads_resolved_map()
        {
            var f = new Fixture();
            f.Shell.Start();
            f.SaveSys.Save(new SaveRequest(f.SlotId, "2026-09-04T00:00:00Z"));

            var result = f.Shell.LoadGame(f.SlotId);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.Single(f.SceneRouter.LoadSceneCalls);
            Assert.Equal(f.StartMap, f.SceneRouter.LoadSceneCalls[0]);
        }

        [Fact]
        public void LoadGame_returns_not_found_for_missing_slot_and_does_not_load_scene()
        {
            var f = new Fixture();
            f.Shell.Start();

            var result = f.Shell.LoadGame(new Id("save.slot_missing"));

            Assert.Equal(LoadStatus.NotFound, result.Status);
            Assert.Empty(f.SceneRouter.LoadSceneCalls);
        }

        [Fact]
        public void OverwriteSlot_writes_a_save_and_DeleteSlot_removes_it()
        {
            var f = new Fixture();

            var saveResult = f.Shell.OverwriteSlot(f.SlotId, 120, null);
            Assert.True(saveResult.Success);
            Assert.True(f.SaveSys.SlotExists(f.SlotId));

            Assert.True(f.Shell.DeleteSlot(f.SlotId));
            Assert.False(f.SaveSys.SlotExists(f.SlotId));
            Assert.False(f.Shell.DeleteSlot(f.SlotId));
        }

        [Fact]
        public void ReturnToMainMenu_transitions_from_in_world_and_from_pause()
        {
            var f = new Fixture();
            f.Shell.Start();
            f.Shell.NewGame(f.SlotId, f.EasyTier, null);
            f.Shell.Update();
            Assert.Equal(ShellPage.InWorld, f.Shell.Page);

            Assert.True(f.Shell.ReturnToMainMenu());
            Assert.Equal(ShellPage.MainMenu, f.Shell.Page);

            // Re-enter InWorld then Pause, then return to main menu from Pause.
            f.AppState.RequestTransition(AppState.Loading);
            f.AppState.RequestTransition(AppState.InWorld);
            f.AppState.RequestTransition(AppState.Pause);
            Assert.True(f.Shell.ReturnToMainMenu());
            Assert.Equal(AppState.MainMenu, f.AppState.GetState());
        }

        [Fact]
        public void Quit_only_succeeds_from_main_menu()
        {
            var f = new Fixture();

            Assert.False(f.Shell.Quit());

            f.Shell.Start();
            Assert.True(f.Shell.Quit());
            Assert.True(f.AppState.IsExitRequested);
        }

        [Fact]
        public void SaveSettings_and_LoadSettings_round_trip_including_bindings()
        {
            var f = new Fixture();
            f.InputMap.SetBindingsForTest("input.action.move", "key:w");

            var extra = new JsonObjectBuilder().Add("master_volume", new JsonNumber(0.8)).Build();
            Assert.True(f.Shell.SaveSettings(extra));

            var freshInputMap = new FakeInputMapHost();
            var freshShell = new ShellHost(
                f.AppState, f.SceneRouter, f.SaveSys, f.SettingsStoreInstance, f.Difficulty, freshInputMap, f.EventBus,
                (slotId, difficultyId, archetypeId) => f.StartMap, () => "t", loadedMapIdResolver: () => f.StartMap);

            var loaded = freshShell.LoadSettings();

            Assert.True(loaded.TryGetValue("master_volume", out var volumeVal));
            Assert.Equal(0.8, ((JsonNumber)volumeVal).Value);
            Assert.Equal("key:w", freshInputMap.GetBindings("input.action.move")[0]);
        }

        [Fact]
        public void Scene_load_events_drive_IsLoading_and_LoadingProgress()
        {
            var f = new Fixture();
            f.Shell.Start();

            Assert.False(f.Shell.IsLoading);
            Assert.Equal(0.0, f.Shell.LoadingProgress);

            f.Shell.NewGame(f.SlotId, f.EasyTier, null);
            Assert.True(f.Shell.IsLoading);

            f.Shell.Update();
            Assert.False(f.Shell.IsLoading);
            Assert.Equal(1.0, f.Shell.LoadingProgress);
        }
    }
}
