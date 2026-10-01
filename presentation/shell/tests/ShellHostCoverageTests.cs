using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;
using Core.Gameplay.Difficulty;
using Presentation.Shell;
using Presentation.VfxSfx.Contracts;
using Tests.PresentationUi;
using Xunit;

namespace Tests.PresentationShell
{
    /// <summary>
    /// 测试覆盖剩余项 T-M40：<see cref="ShellHost"/> / <see cref="ShellViewModel"/> 余下缺口。
    /// 失败注入全部走真实机制：<c>StubFileSystem.FailNextWrite</c>（存档/设置写入失败）、自定义 <see cref="AppStateMachineConfig"/>
    /// （主状态转移被拒）、真实 <see cref="InputMapHost"/>（<c>ImportBindings</c> 抛异常）、包装真实 <see cref="SaveSystem"/> 的
    /// 读档结果改写（<c>LoadedFromBackup</c> / <c>CurrentMapId</c>）。期望值由宿主查询结果与测试里的规则算出。
    /// </summary>
    public sealed class ShellHostCoverageTests
    {
        private static readonly Id Slot = new Id("save.slot_1");
        private static readonly Id Easy = new Id("diff.tier.easy");
        private static readonly Id StartMap = new Id("world.map.starting_area");
        private static readonly Id SavedMap = new Id("world.map.saved_area");
        private static readonly Id GameId = new Id("game.demo");

        /// <summary>包装真实 <see cref="SaveSystem"/>，仅可选地改写 <see cref="Load(Id)"/> 的结果（其余成员全部转发）。</summary>
        private sealed class ScriptedSaveSystem : ISaveSystem
        {
            private readonly SaveSystem _inner;
            public Func<LoadResult, LoadResult>? RewriteLoad;
            public LoadResult? ForcedLoad;

            public ScriptedSaveSystem(SaveSystem inner) => _inner = inner;

            public void RegisterPersistable(IPersistable persistable) => _inner.RegisterPersistable(persistable);

            public void RegisterMigration(ISaveMigration migration) => _inner.RegisterMigration(migration);

            public int CurrentSaveVersion => _inner.CurrentSaveVersion;

            public SaveResult Save(SaveRequest request) => _inner.Save(request);

            public LoadResult Load(Id slotId)
            {
                if (ForcedLoad != null)
                {
                    return ForcedLoad;
                }

                var real = _inner.Load(slotId);
                return RewriteLoad != null ? RewriteLoad(real) : real;
            }

            public IReadOnlyList<SaveSlotInfo> ListSlots() => _inner.ListSlots();

            public bool DeleteSlot(Id slotId) => _inner.DeleteSlot(slotId);

            public bool SlotExists(Id slotId) => _inner.SlotExists(slotId);

            public bool ShouldAutoSave(AutoSaveTrigger trigger) => _inner.ShouldAutoSave(trigger);
        }

        private sealed class Rig
        {
            public readonly StubFileSystem Fs = new StubFileSystem();
            public IEventBus Bus = null!;
            public AppStateHost AppState = null!;
            public FakeSceneRouter Router = null!;
            public SaveSystem RealSave = null!;
            public ScriptedSaveSystem SaveSys = null!;
            public SettingsStore Settings = null!;
            public readonly FakeDifficultyHost Difficulty = new FakeDifficultyHost();
            public IInputMapHost InputMap = null!;
            public readonly PresentationDiagnosticsRecorder Diagnostics = new PresentationDiagnosticsRecorder();
            public ShellHost Shell = null!;

            public readonly List<(Id Slot, Id Difficulty, Id? Archetype)> StarterCalls = new List<(Id, Id, Id?)>();
            public Func<Id> StarterBehavior = () => StartMap;
            public int ResolverCalls;
            public readonly List<(Id Slot, Id Difficulty, Id? Archetype)> RollbackCalls = new List<(Id, Id, Id?)>();
            public Action? RollbackBehavior;
            public Id? ResolverResult = StartMap;
        }

        private static Rig NewRig(AppStateMachineConfig? appConfig = null, IInputMapHost? inputMap = null, bool withResolver = true, bool withRollback = false)
        {
            var rig = new Rig();
            rig.Bus = TestSupport.BuildEventBus();
            rig.AppState = appConfig == null ? new AppStateHost(rig.Bus) : new AppStateHost(rig.Bus, appConfig);
            rig.Router = new FakeSceneRouter(rig.AppState, rig.Bus);
            rig.RealSave = new SaveSystem(rig.Fs, new SaveSystemOptions(GameId));
            rig.SaveSys = new ScriptedSaveSystem(rig.RealSave);
            rig.Settings = new SettingsStore(rig.Fs);
            rig.Difficulty.KnownTiers.Add(Easy);
            rig.InputMap = inputMap ?? new FakeInputMapHost();
            rig.Shell = new ShellHost(
                rig.AppState, rig.Router, rig.SaveSys, rig.Settings, rig.Difficulty, rig.InputMap, rig.Bus,
                (slot, difficulty, archetype) =>
                {
                    rig.StarterCalls.Add((slot, difficulty, archetype));
                    return rig.StarterBehavior();
                },
                () => "2026-10-01T00:00:00Z",
                withResolver
                    ? () =>
                    {
                        rig.ResolverCalls++;
                        return rig.ResolverResult ?? throw new InvalidOperationException("resolver 不应被调用");
                    }
                    : (LoadedMapIdResolver?)null,
                rig.Diagnostics,
                withRollback
                    ? (slot, difficulty, archetype) =>
                    {
                        rig.RollbackCalls.Add((slot, difficulty, archetype));
                        rig.RollbackBehavior?.Invoke();
                    }
                    : (NewGameRollback?)null);
            Assert.True(rig.Shell.Start());
            return rig;
        }

        // ------------------------------------------------------------------ NewGame 失败路径

        [Fact]
        public void NewGame_StarterReceivesSlotDifficultyAndArchetype_ThenSceneLoadsAndSlotIsSaved()
        {
            var rig = NewRig();
            var archetype = new Id("arch.class.sample_warrior");

            Assert.True(rig.Shell.NewGame(Slot, Easy, archetype));

            Assert.Equal((Slot, Easy, (Id?)archetype), Assert.Single(rig.StarterCalls));
            Assert.Equal(StartMap, Assert.Single(rig.Router.LoadSceneCalls));
            Assert.True(rig.RealSave.SlotExists(Slot));
            Assert.Equal(ShellPage.Loading, rig.Shell.Page);
            Assert.True(rig.Shell.IsLoading);
        }

        [Fact]
        public void NewGame_SaveWriteFails_ReturnsFalse_NoSceneLoad_PageAndLoadingStateUnchanged()
        {
            var rig = NewRig();
            rig.Fs.FailNextWrite();

            var ok = rig.Shell.NewGame(Slot, Easy, null);

            Assert.False(ok);
            Assert.Empty(rig.Router.LoadSceneCalls);
            Assert.False(rig.RealSave.SlotExists(Slot));
            Assert.Equal(AppState.MainMenu, rig.AppState.GetState()); // 接口契约：不改变应用状态
            Assert.Equal(ShellPage.MainMenu, rig.Shell.Page);
            Assert.False(rig.Shell.IsLoading);
        }

        // 收口遗留修复 A5：存档写入失败时回滚已设置的难度，返回 false 后宿主状态与调用前逐项相等。
        [Fact]
        public void NewGame_SaveWriteFails_FromFreshHost_DifficultyRolledBackToUnset()
        {
            var rig = NewRig();
            Assert.Null(rig.Difficulty.CurrentTier);
            rig.Fs.FailNextWrite();

            Assert.False(rig.Shell.NewGame(Slot, Easy, null));

            Assert.Null(rig.Difficulty.CurrentTier);
            Assert.Null(rig.Difficulty.CurrentScope);
            Assert.Null(rig.Difficulty.CurrentMapId);
        }

        [Fact]
        public void NewGame_SaveWriteFails_AfterEarlierDifficulty_RestoresPreviousTierScopeAndMap()
        {
            var rig = NewRig();
            var hard = new Id("diff.tier.hard");
            var mapX = new Id("world.map.x");
            rig.Difficulty.KnownTiers.Add(hard);
            Assert.True(rig.Difficulty.Apply(hard, DifficultyScope.Map, mapX));
            rig.Fs.FailNextWrite();

            Assert.False(rig.Shell.NewGame(Slot, Easy, null));

            Assert.Equal(hard, rig.Difficulty.CurrentTier);
            Assert.Equal(DifficultyScope.Map, rig.Difficulty.CurrentScope);
            Assert.Equal(mapX, rig.Difficulty.CurrentMapId);
        }

        [Fact]
        public void NewGame_SaveWriteFails_WithRollbackInjected_CallsItOnceWithSameArguments_AndRestoresDifficulty()
        {
            var rig = NewRig(withRollback: true);
            var archetype = new Id("arch.knight");
            rig.Fs.FailNextWrite();

            Assert.False(rig.Shell.NewGame(Slot, Easy, archetype));

            Assert.Equal(rig.StarterCalls, rig.RollbackCalls);
            Assert.Null(rig.Difficulty.CurrentTier);
            Assert.Empty(rig.Diagnostics.Warnings);
            Assert.Empty(rig.Router.LoadSceneCalls);
            Assert.Equal(ShellPage.MainMenu, rig.Shell.Page);
        }

        [Fact]
        public void NewGame_SaveWriteFails_WithoutRollbackInjected_WarnsThatStarterStateWasNotRolledBack()
        {
            var rig = NewRig();
            rig.Fs.FailNextWrite();

            Assert.False(rig.Shell.NewGame(Slot, Easy, null));

            var warning = Assert.Single(rig.Diagnostics.Warnings);
            Assert.Contains("NewGameRollback", warning);
        }

        [Fact]
        public void NewGame_SaveWriteFails_RollbackThrows_RecordedAsDiagnostic_StillReturnsFalse_DifficultyStillRestored()
        {
            var rig = NewRig(withRollback: true);
            rig.RollbackBehavior = () => throw new InvalidOperationException("rollback boom");
            rig.Fs.FailNextWrite();

            Assert.False(rig.Shell.NewGame(Slot, Easy, null));

            Assert.Contains(rig.Diagnostics.Warnings, w => w.Contains("rollback boom"));
            Assert.Null(rig.Difficulty.CurrentTier);
        }

        [Fact]
        public void NewGame_Success_DoesNotRollBack_AndKeepsAppliedDifficulty()
        {
            var rig = NewRig(withRollback: true);

            Assert.True(rig.Shell.NewGame(Slot, Easy, null));

            Assert.Empty(rig.RollbackCalls);
            Assert.Equal(Easy, rig.Difficulty.CurrentTier);
            Assert.Equal(0, rig.Difficulty.LoadCallCount);
        }

        [Fact]
        public void NewGame_StarterThrows_ExceptionPropagates_NothingSaved_NoSceneLoad_StateUnchanged()
        {
            var rig = NewRig();
            rig.StarterBehavior = () => throw new InvalidOperationException("starter boom");

            var ex = Assert.Throws<InvalidOperationException>(() => rig.Shell.NewGame(Slot, Easy, null));

            Assert.Equal("starter boom", ex.Message);
            Assert.False(rig.RealSave.SlotExists(Slot));
            Assert.Empty(rig.Router.LoadSceneCalls);
            Assert.Equal(AppState.MainMenu, rig.AppState.GetState());
            Assert.Equal(ShellPage.MainMenu, rig.Shell.Page);
            Assert.False(rig.Shell.IsLoading);
        }

        [Fact]
        public void NewGame_SceneRouterRejectsUnknownScene_ReturnsFalse_PageAndIsLoadingStayOnMainMenu()
        {
            var rig = NewRig();
            rig.Router.ThrowUnknownSceneOnLoad = true;

            Assert.False(rig.Shell.NewGame(Slot, Easy, null));

            Assert.Equal(ShellPage.MainMenu, rig.Shell.Page);
            Assert.False(rig.Shell.IsLoading);
            Assert.Equal(AppState.MainMenu, rig.AppState.GetState());
            // 现行为：场景被拒发生在存档写入之后，初始存档已落盘。
            Assert.True(rig.RealSave.SlotExists(Slot));
        }

        [Fact]
        public void NewGame_WhileAnotherLoadIsInProgress_ReturnsFalse_AndKeepsTheFirstLoadsState()
        {
            var rig = NewRig();
            Assert.True(rig.Shell.NewGame(Slot, Easy, null));
            Assert.True(rig.Shell.IsLoading);

            var second = rig.Shell.NewGame(new Id("save.slot_2"), Easy, null); // FakeSceneRouter：加载中再 LoadScene -> InvalidOperationException

            Assert.False(second);
            Assert.Single(rig.Router.LoadSceneCalls);
            Assert.Equal(ShellPage.Loading, rig.Shell.Page);
            Assert.True(rig.Shell.IsLoading);
        }

        [Fact]
        public void AppStateBackToMainMenu_ClearsIsLoading_WhenLoadingIsAbandoned()
        {
            var rig = NewRig();
            Assert.True(rig.Shell.NewGame(Slot, Easy, null));
            Assert.True(rig.Shell.IsLoading);

            Assert.True(rig.AppState.RequestTransition(AppState.MainMenu)); // Loading -> MainMenu：加载失败回退

            Assert.False(rig.Shell.IsLoading);
            Assert.Equal(ShellPage.MainMenu, rig.Shell.Page);
        }

        // ------------------------------------------------------------------ LoadGame

        private static void SaveSlotOnce(Rig rig) =>
            Assert.True(rig.RealSave.Save(new SaveRequest(Slot, "2026-10-01T00:00:00Z")).Success);

        [Fact]
        public void LoadGame_LoadedFromBackup_StillRoutesTheScene_AndKeepsTheBackupStatus()
        {
            var rig = NewRig();
            SaveSlotOnce(rig);
            rig.SaveSys.RewriteLoad = real =>
                LoadResult.Loaded(real.Meta!, real.MigratedFromVersion, LoadStatus.LoadedFromBackup, SavedMap);

            var result = rig.Shell.LoadGame(Slot);

            Assert.Equal(LoadStatus.LoadedFromBackup, result.Status);
            Assert.Equal(SavedMap, Assert.Single(rig.Router.LoadSceneCalls));
            Assert.False(result.SceneRouteFailed);
        }

        [Fact]
        public void LoadGame_CurrentMapIdPresent_TakesPrecedenceOverResolver_ResolverNeverCalled()
        {
            var rig = NewRig();
            SaveSlotOnce(rig);
            rig.SaveSys.RewriteLoad = real =>
                LoadResult.Loaded(real.Meta!, real.MigratedFromVersion, LoadStatus.Loaded, SavedMap);

            rig.Shell.LoadGame(Slot);

            Assert.Equal(SavedMap, Assert.Single(rig.Router.LoadSceneCalls));
            Assert.Equal(0, rig.ResolverCalls);
        }

        [Fact]
        public void LoadGame_CurrentMapIdMissing_FallsBackToResolver()
        {
            var rig = NewRig();
            SaveSlotOnce(rig);
            Assert.Null(rig.SaveSys.Load(Slot).CurrentMapId); // 前置：真实存档没有 world 段，CurrentMapId 为空
            rig.ResolverResult = new Id("world.map.resolver_area");

            rig.Shell.LoadGame(Slot);

            Assert.Equal(rig.ResolverResult, Assert.Single(rig.Router.LoadSceneCalls));
            Assert.Equal(1, rig.ResolverCalls);
        }

        [Fact]
        public void LoadGame_NoCurrentMapIdAndNoResolver_SkipsSceneRoute_ButStillReturnsTheLoadedResult()
        {
            var rig = NewRig(withResolver: false);
            SaveSlotOnce(rig);

            var result = rig.Shell.LoadGame(Slot);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.False(result.SceneRouteFailed);
            Assert.Null(result.SceneRouteError);
            Assert.Empty(rig.Router.LoadSceneCalls);
            Assert.Empty(rig.Diagnostics.Warnings);
        }

        [Theory]
        [InlineData(LoadStatus.Corrupted)]
        [InlineData(LoadStatus.MigrationFailed)]
        public void LoadGame_FailureStatuses_ReturnedVerbatim_NoSceneRoute_NoDiagnostic(LoadStatus status)
        {
            var rig = NewRig();
            rig.SaveSys.ForcedLoad = status == LoadStatus.Corrupted
                ? LoadResult.Corrupted("bad bytes")
                : LoadResult.MigrationFailed("no path");

            var result = rig.Shell.LoadGame(Slot);

            Assert.Equal(status, result.Status);
            Assert.Empty(rig.Router.LoadSceneCalls);
            Assert.Empty(rig.Diagnostics.Warnings);
            Assert.Equal(0, rig.ResolverCalls);
        }

        // ------------------------------------------------------------------ ReturnToMainMenu

        [Fact]
        public void ReturnToMainMenu_TransitionRejectedByTheStateMachine_ReturnsFalse_AndKeepsPageAndSubPage()
        {
            // 自定义状态机：没有 InWorld -> MainMenu，主菜单子页不应被改写。
            var config = new AppStateMachineConfig();
            config.AllowTransition(AppState.Boot, AppState.MainMenu);
            config.AllowTransition(AppState.MainMenu, AppState.Loading);
            config.AllowTransition(AppState.Loading, AppState.InWorld);
            var rig = NewRig(config);
            rig.Shell.OpenSettings();
            Assert.True(rig.Shell.NewGame(Slot, Easy, null));
            rig.Shell.Update(); // -> InWorld
            Assert.Equal(ShellPage.InWorld, rig.Shell.Page);

            var ok = rig.Shell.ReturnToMainMenu();

            Assert.False(ok);
            Assert.Equal(AppState.InWorld, rig.AppState.GetState());
            Assert.Equal(ShellPage.InWorld, rig.Shell.Page);
        }

        [Fact]
        public void ReturnToMainMenu_AlreadyOnMainMenuSubPage_ResetsSubPageWithoutAnyStateChange()
        {
            var rig = NewRig();
            var stateChanges = 0;
            rig.Bus.Subscribe(AppEventKeys.StateChanged, _ => stateChanges++);
            rig.Shell.OpenSettings();
            Assert.Equal(ShellPage.Settings, rig.Shell.Page);

            Assert.True(rig.Shell.ReturnToMainMenu());

            Assert.Equal(ShellPage.MainMenu, rig.Shell.Page);
            Assert.Equal(AppState.MainMenu, rig.AppState.GetState());
            Assert.Equal(0, stateChanges);
        }

        /// <summary>特征化现行为（待设计层确认，见汇报）：Loading 中调用 <c>ReturnToMainMenu</c> 返回 true，但应用状态仍是
        /// Loading（只有 InWorld/Pause 才会发起转移）。</summary>
        [Fact]
        public void ReturnToMainMenu_DuringLoading_ReturnsTrueButAppStateStaysLoading_CurrentBehavior()
        {
            var rig = NewRig();
            Assert.True(rig.Shell.NewGame(Slot, Easy, null));
            Assert.Equal(AppState.Loading, rig.AppState.GetState());

            var ok = rig.Shell.ReturnToMainMenu();

            Assert.True(ok);
            Assert.Equal(AppState.Loading, rig.AppState.GetState());
            Assert.Equal(ShellPage.Loading, rig.Shell.Page);
        }

        // ------------------------------------------------------------------ 设置

        [Fact]
        public void SaveSettings_Null_ThrowsArgumentNull()
        {
            var rig = NewRig();

            var ex = Assert.Throws<ArgumentNullException>(() => rig.Shell.SaveSettings(null!));

            Assert.Equal("additionalFields", ex.ParamName);
        }

        /// <summary>特征化现行为（待设计层确认，见汇报）：<c>input_bindings</c> 是保留键；调用方把 <c>LoadSettings()</c> 的结果原样
        /// 回传（其中已含该键）会因 <c>JsonObjectBuilder</c> 键重复抛 <see cref="ArgumentException"/>。</summary>
        [Fact]
        public void SaveSettings_ReservedKeyInputBindingsInAdditionalFields_ThrowsArgumentException_CurrentBehavior()
        {
            var rig = NewRig();
            var fields = new JsonObjectBuilder().Add("input_bindings", new JsonObjectBuilder().Build()).Build();

            Assert.Throws<ArgumentException>(() => rig.Shell.SaveSettings(fields));
        }

        [Fact]
        public void SaveSettings_StoreWriteFails_ReturnsFalse_AndPreviousSettingsRemain()
        {
            var rig = NewRig();
            var first = new JsonObjectBuilder().Add("music_volume", new JsonNumber(0.5)).Build();
            Assert.True(rig.Shell.SaveSettings(first));
            var second = new JsonObjectBuilder().Add("music_volume", new JsonNumber(0.9)).Build();
            rig.Fs.FailNextWrite();

            var ok = rig.Shell.SaveSettings(second);

            Assert.False(ok);
            var loaded = rig.Shell.LoadSettings();
            Assert.True(loaded.TryGetValue("music_volume", out var volume));
            Assert.Equal(0.5, ((JsonNumber)volume).Value);
        }

        [Fact]
        public void LoadSettings_NoFileYet_ReturnsEmptySettings_AndDoesNotTouchBindings()
        {
            var inputMap = new FakeInputMapHost();
            inputMap.SetBindingsForTest("input.action.jump", "key:space");
            var rig = NewRig(inputMap: inputMap);

            var data = rig.Shell.LoadSettings();

            Assert.False(data.TryGetValue("input_bindings", out _));
            Assert.Equal(new[] { "key:space" }, inputMap.GetBindings("input.action.jump").ToArray());
        }

        [Theory]
        [InlineData("string")]
        [InlineData("array")]
        [InlineData("number")]
        public void LoadSettings_InputBindingsOfWrongType_IsNotImported_AndRawValueStaysInTheReturnedObject(string kind)
        {
            var inputMap = new FakeInputMapHost();
            inputMap.SetBindingsForTest("input.action.jump", "key:space");
            var rig = NewRig(inputMap: inputMap);
            JsonValue wrong = kind switch
            {
                "string" => new JsonString("not an object"),
                "array" => new JsonArray(new List<JsonValue> { new JsonString("key:w") }),
                _ => new JsonNumber(7),
            };
            Assert.True(rig.Settings.Save(new JsonObjectBuilder().Add("input_bindings", wrong).Add("music_volume", new JsonNumber(0.3)).Build()));

            var data = rig.Shell.LoadSettings();

            Assert.Equal(new[] { "key:space" }, inputMap.GetBindings("input.action.jump").ToArray()); // 未导入
            Assert.True(data.TryGetValue("input_bindings", out var raw));
            Assert.Equal(wrong.GetType(), raw.GetType());
            Assert.True(data.TryGetValue("music_volume", out var volume)); // 其余设置项不受影响
            Assert.Equal(0.3, ((JsonNumber)volume).Value);
        }

        [Fact]
        public void LoadSettings_ImportBindingsThrows_ExceptionPropagates_AndRealHostBindingsStayUnchanged()
        {
            var bus = TestSupport.BuildEventBus();
            var inputMap = new InputMapHost(bus);
            inputMap.DeclareActionSet(new Id("input.set.sample"), new[]
            {
                new ActionDefinition(new Id("input.action.jump"), ActionKind.Button, new[] { "key:space" }),
            });
            var rig = NewRig(inputMap: inputMap);
            var bindings = new JsonObjectBuilder()
                .Add("input.action.jump", new JsonArray(new List<JsonValue> { new JsonString("key:x") }))
                .Add("input.action.no_such_action", new JsonArray(new List<JsonValue> { new JsonString("key:y") }))
                .Build();
            Assert.True(rig.Settings.Save(new JsonObjectBuilder().Add("input_bindings", bindings).Build()));

            Assert.ThrowsAny<Exception>(() => rig.Shell.LoadSettings());

            // 真实宿主的导入是“先全部校验再落地”：整批被拒，已声明动作的绑定不变。
            Assert.Equal(new[] { "key:space" }, inputMap.GetBindings("input.action.jump").ToArray());
        }

        [Fact]
        public void SettingsRoundTrip_ExportsLiveBindings_AndLoadSettingsReappliesThem()
        {
            var bus = TestSupport.BuildEventBus();
            var inputMap = new InputMapHost(bus);
            inputMap.DeclareActionSet(new Id("input.set.sample"), new[]
            {
                new ActionDefinition(new Id("input.action.jump"), ActionKind.Button, new[] { "key:space" }),
            });
            var rig = NewRig(inputMap: inputMap);
            Assert.True(inputMap.Rebind("input.action.jump", "key:j"));
            Assert.True(rig.Shell.SaveSettings(new JsonObjectBuilder().Add("music_volume", new JsonNumber(0.4)).Build()));
            Assert.True(inputMap.Rebind("input.action.jump", "key:k")); // 保存之后又改了

            rig.Shell.LoadSettings();

            Assert.Equal(new[] { "key:j" }, inputMap.GetBindings("input.action.jump").ToArray()); // 回到保存时刻
        }

        // ------------------------------------------------------------------ Dispose

        [Fact]
        public void Dispose_StopsSceneLoadStartedFromSettingIsLoading_AndIsIdempotent()
        {
            var rig = NewRig();
            rig.Shell.Dispose();
            rig.Shell.Dispose(); // 幂等

            rig.Bus.PublishImmediate(new SceneLoadStartedEvent(StartMap));

            Assert.False(rig.Shell.IsLoading); // 未订阅：load_started 不再置位
        }

        [Fact]
        public void Dispose_StopsLoadFinishedAndAppStateChangeFromClearingIsLoading()
        {
            var rig = NewRig();
            Assert.True(rig.Shell.NewGame(Slot, Easy, null));
            Assert.True(rig.Shell.IsLoading);
            rig.Shell.Dispose();

            Assert.True(rig.AppState.RequestTransition(AppState.MainMenu)); // Loading -> MainMenu：订阅存在时会清 IsLoading
            rig.Bus.PublishImmediate(new SceneLoadFinishedEvent(StartMap));

            Assert.True(rig.Shell.IsLoading);
        }

        // ------------------------------------------------------------------ ShellViewModel

        private static ShellViewModel NewViewModel(Rig rig, out ShellMenuDefinition menu)
        {
            var entries = new List<ShellMenuEntry>
            {
                new ShellMenuEntry(new Id("shell.entry.new_game"), new Id("l10n.shell.new_game"), ShellMenuAction.NewGame, null),
                new ShellMenuEntry(new Id("shell.entry.continue"), new Id("l10n.shell.continue"), ShellMenuAction.LoadGame, null),
                new ShellMenuEntry(new Id("shell.entry.quit"), new Id("l10n.shell.quit"), ShellMenuAction.Quit, null),
            };
            menu = new ShellMenuDefinition(new Id("shell.menu.main"), entries);
            return new ShellViewModel(rig.Shell, rig.SaveSys, rig.Bus, menu);
        }

        [Fact]
        public void ViewModel_MenuEntries_ExposeTheInjectedDefinitionInOrder_AndDoNotChangeOnRefresh()
        {
            var rig = NewRig();
            using var vm = NewViewModel(rig, out var menu);

            Assert.Equal(menu.Entries.Select(e => e.Id).ToArray(), vm.MenuEntries.Select(e => e.Id).ToArray());
            Assert.Equal(3, vm.MenuEntries.Count);

            vm.Refresh();
            rig.Shell.ShowSlots();
            vm.Refresh();

            Assert.Equal(menu.Entries.Select(e => e.Id).ToArray(), vm.MenuEntries.Select(e => e.Id).ToArray());
        }

        public static IEnumerable<object[]> ViewModelEventKeys() => new[]
        {
            AppEventKeys.StateChanged, SceneRouterEventKeys.LoadStarted, SceneRouterEventKeys.LoadFinished,
            SaveEventKeys.SaveCompleted, SaveEventKeys.SaveLoaded,
        }.Select(k => new object[] { k.Value });

        /// <summary>每个订阅键独立触发刷新：静默改变外壳页面/存档槽（不发事件），仅发布这一个键，视图模型快照随之更新。</summary>
        [Theory]
        [MemberData(nameof(ViewModelEventKeys))]
        public void ViewModel_EachSubscribedKey_AloneTriggersARefresh(string keyValue)
        {
            var rig = NewRig();
            using var vm = NewViewModel(rig, out _);
            Assert.Equal(ShellPage.MainMenu, vm.CurrentPage);
            Assert.Empty(vm.SlotSummaries);

            rig.Shell.ShowSlots(); // 静默：只改外壳的子页面
            Assert.True(rig.RealSave.Save(new SaveRequest(Slot, "2026-10-01T00:00:00Z")).Success); // 静默：真实存档系统未接总线
            Assert.Equal(ShellPage.MainMenu, vm.CurrentPage);
            Assert.Empty(vm.SlotSummaries);

            rig.Bus.PublishImmediate(new GenericEvent(new Id(keyValue)));

            Assert.Equal(ShellPage.SaveSlots, vm.CurrentPage);
            Assert.Contains(vm.SlotSummaries, s => s.SlotId.Equals(Slot));
        }

        [Fact]
        public void ViewModel_Dispose_UnsubscribesEveryKey_IsIdempotent_AndManualRefreshStillWorks()
        {
            var rig = NewRig();
            var vm = NewViewModel(rig, out _);
            vm.Dispose();
            vm.Dispose();

            rig.Shell.ShowSlots();
            foreach (var key in ViewModelEventKeys().Select(a => new Id((string)a[0])))
            {
                rig.Bus.PublishImmediate(new GenericEvent(key));
            }

            Assert.Equal(ShellPage.MainMenu, vm.CurrentPage); // 事件不再触发刷新

            vm.Refresh();
            Assert.Equal(ShellPage.SaveSlots, vm.CurrentPage); // 手动刷新仍可用
        }

        [Fact]
        public void ViewModel_ReflectsLoadingProgressAndIsLoading_ThroughARealLoadCycle()
        {
            var rig = NewRig();
            using var vm = NewViewModel(rig, out _);

            Assert.True(rig.Shell.NewGame(Slot, Easy, null)); // load_started
            Assert.True(vm.IsLoading);
            Assert.Equal(rig.Router.LoadProgress, vm.LoadingProgress);
            Assert.Equal(ShellPage.Loading, vm.CurrentPage);

            rig.Shell.Update(); // 路由器完成：进度 1，load_finished
            Assert.False(vm.IsLoading);
            Assert.Equal(rig.Router.LoadProgress, vm.LoadingProgress);
            Assert.Equal(ShellPage.InWorld, vm.CurrentPage);
        }
    }
}
