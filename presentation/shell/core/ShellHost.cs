using System;
using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;
using Core.Gameplay.Difficulty;

namespace Presentation.Shell
{
    /// <summary>
    /// 游戏外壳的默认实现（见 09_表现层.md 第 9 节、01_分层与依赖.md L5 模块表 <c>shell</c> 行）。
    /// 本类型只做编排：把"新游戏""读档""退出"这类跨越多个 L0/L4 窄契约的用户操作串成固定顺序，
    /// 具体裁决（难度是否允许应用、存档能不能写、场景能不能加载）一律交给被调用的宿主接口本身
    /// （铁律 P3：全部改变逻辑/持久化状态的操作都经窄契约，本类型自己不直接持有或篡改任何这些
    /// 状态）。
    /// </summary>
    public sealed class ShellHost : IShellHost, IDisposable
    {
        private readonly IAppStateHost _appState;
        private readonly ISceneRouter _sceneRouter;
        private readonly ISaveSystem _saveSystem;
        private readonly ISettingsStore _settingsStore;
        private readonly IDifficultyHost _difficulty;
        private readonly IInputMapHost _inputMap;
        private readonly IEventBus _eventBus;
        private readonly NewGameStarter _newGameStarter;

        /// <summary>缺口 11 恢复：G1 给 <see cref="LoadResult"/> 补了 <see cref="LoadResult.CurrentMapId"/>
        /// 后，<see cref="LoadGame"/> 改读该字段，不再需要本委托作为唯一来源——保留为可选覆盖
        /// （见该方法判断记录），未注入时为 null。</summary>
        private readonly LoadedMapIdResolver? _loadedMapIdResolver;
        private readonly Func<string> _timestampProvider;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();

        private ShellPage _mainMenuSubPage = ShellPage.MainMenu;

        public ShellHost(
            IAppStateHost appState,
            ISceneRouter sceneRouter,
            ISaveSystem saveSystem,
            ISettingsStore settingsStore,
            IDifficultyHost difficulty,
            IInputMapHost inputMap,
            IEventBus eventBus,
            NewGameStarter newGameStarter,
            Func<string> timestampProvider,
            LoadedMapIdResolver? loadedMapIdResolver = null)
        {
            _appState = appState ?? throw new ArgumentNullException(nameof(appState));
            _sceneRouter = sceneRouter ?? throw new ArgumentNullException(nameof(sceneRouter));
            _saveSystem = saveSystem ?? throw new ArgumentNullException(nameof(saveSystem));
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            _difficulty = difficulty ?? throw new ArgumentNullException(nameof(difficulty));
            _inputMap = inputMap ?? throw new ArgumentNullException(nameof(inputMap));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _newGameStarter = newGameStarter ?? throw new ArgumentNullException(nameof(newGameStarter));
            _loadedMapIdResolver = loadedMapIdResolver;
            _timestampProvider = timestampProvider ?? throw new ArgumentNullException(nameof(timestampProvider));

            _subscriptions.Add(_eventBus.Subscribe(SceneRouterEventKeys.LoadStarted, OnSceneLoadStarted));
            _subscriptions.Add(_eventBus.Subscribe(SceneRouterEventKeys.LoadFinished, OnSceneLoadFinished));
            _subscriptions.Add(_eventBus.Subscribe(AppEventKeys.StateChanged, OnAppStateChanged));
        }

        /// <summary>
        /// 当前 Shell 页面（见 <see cref="ShellPage"/>）。判断记录：<see cref="ShellPage.MainMenu"/>/
        /// <see cref="ShellPage.SaveSlots"/>/<see cref="ShellPage.NewGameSetup"/>/
        /// <see cref="ShellPage.Settings"/> 四者是"应用级主状态仍为 <see cref="AppState.MainMenu"/>
        /// 时，Shell 自己在主菜单内部导航到哪个子页面"，只在这一条件下才读取 <see cref="_mainMenuSubPage"/>；
        /// 一旦应用级主状态离开 MainMenu（Loading/InWorld/Pause），页面直接反映主状态，不受
        /// <see cref="_mainMenuSubPage"/> 里残留的旧选择影响（避免"上次在设置页离开，回到主菜单又
        /// 神秘地停在设置页"）。
        /// </summary>
        public ShellPage Page
        {
            get
            {
                switch (_appState.GetState())
                {
                    case AppState.Loading: return ShellPage.Loading;
                    case AppState.InWorld: return ShellPage.InWorld;
                    case AppState.Pause: return ShellPage.Paused;
                    default: return _mainMenuSubPage;
                }
            }
        }

        public bool IsLoading { get; private set; }

        public double LoadingProgress => _sceneRouter.LoadProgress;

        public bool Start() => _appState.RequestTransition(AppState.MainMenu);

        public void ShowSlots() => _mainMenuSubPage = ShellPage.SaveSlots;

        public void ShowNewGameSetup() => _mainMenuSubPage = ShellPage.NewGameSetup;

        public void OpenSettings() => _mainMenuSubPage = ShellPage.Settings;

        public bool NewGame(Id slotId, Id difficultyId, Id? archetypeId)
        {
            bool difficultyApplied;
            try
            {
                difficultyApplied = _difficulty.Apply(difficultyId, DifficultyScope.Global, null);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (!difficultyApplied)
            {
                return false;
            }

            var startMapId = _newGameStarter(slotId, difficultyId, archetypeId);

            var saveResult = _saveSystem.Save(new SaveRequest(slotId, _timestampProvider(), difficultyId: difficultyId));
            if (!saveResult.Success)
            {
                return false;
            }

            try
            {
                _sceneRouter.LoadScene(startMapId);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 判断记录（缺口 11 恢复）：优先用 <see cref="LoadResult.CurrentMapId"/>（G1 补的字段，读自
        /// <c>world.current_map_id</c> 段，见任务书"ShellHost 读档后用 LoadResult.CurrentMapId 进图"）；
        /// 该字段为 null（存档未登记 world 段，或该段尚未来得及在游戏层实现——见其字段注释）时才回退
        /// <see cref="_loadedMapIdResolver"/>（未注入时视为"无法确定地图 id"，跳过场景切换，读档结果
        /// 本身仍照常返回，同下方两个 catch 分支"读档本身仍然算成功"的一贯处理）。
        /// <see cref="LoadResult.CurrentPosition"/> 不在本方法内消费——ShellHost 不持有玩家实体引用
        /// （铁律 P1/P3，见类型注释），原样保留在返回值里，由拿到 <see cref="LoadResult"/> 的调用方
        /// （游戏层/表现层装配代码）在场景加载完成后自行落位玩家。
        /// </summary>
        public LoadResult LoadGame(Id slotId)
        {
            var result = _saveSystem.Load(slotId);
            if (result.Status == LoadStatus.Loaded || result.Status == LoadStatus.LoadedFromBackup)
            {
                var mapId = result.CurrentMapId ?? _loadedMapIdResolver?.Invoke();
                if (mapId.HasValue)
                {
                    try
                    {
                        _sceneRouter.LoadScene(mapId.Value);
                    }
                    catch (ArgumentException)
                    {
                        // 地图 id 未知：读档本身仍然算成功，场景切换失败留给上层诊断/重试。
                    }
                    catch (InvalidOperationException)
                    {
                        // 当前应用状态不允许切到 Loading（例如已经在 Loading 中）：同上，不吞掉读档结果。
                    }
                }
            }

            return result;
        }

        public SaveResult OverwriteSlot(Id slotId, long? playTimeSeconds, IReadOnlyDictionary<string, string>? displaySummary) =>
            _saveSystem.Save(new SaveRequest(slotId, _timestampProvider(), playTimeSeconds, displaySummary, _difficulty.CurrentTier));

        public bool DeleteSlot(Id slotId) => _saveSystem.DeleteSlot(slotId);

        public bool ReturnToMainMenu()
        {
            var state = _appState.GetState();
            if (state == AppState.InWorld || state == AppState.Pause)
            {
                if (!_appState.RequestTransition(AppState.MainMenu))
                {
                    return false;
                }
            }

            _mainMenuSubPage = ShellPage.MainMenu;
            return true;
        }

        public bool Quit() => _appState.RequestExit();

        public JsonObject LoadSettings()
        {
            var data = _settingsStore.Load();
            if (data.TryGetValue("input_bindings", out var bindingsVal) && bindingsVal is JsonObject bindingsObj)
            {
                _inputMap.ImportBindings(bindingsObj);
            }

            return data;
        }

        public bool SaveSettings(JsonObject additionalFields)
        {
            if (additionalFields == null) throw new ArgumentNullException(nameof(additionalFields));

            var builder = new JsonObjectBuilder();
            foreach (var entry in additionalFields)
            {
                builder.Add(entry.Key, entry.Value);
            }
            builder.Add("input_bindings", _inputMap.ExportBindings());

            return _settingsStore.Save(builder.Build());
        }

        public void Update()
        {
            _sceneRouter.Update();
            IsLoading = _sceneRouter.State == SceneRouterState.Loading;
        }

        private void OnSceneLoadStarted(IEvent evt) => IsLoading = true;

        private void OnSceneLoadFinished(IEvent evt) => IsLoading = false;

        private void OnAppStateChanged(IEvent evt)
        {
            // 03_运行时骨架.md 第 2 节状态机表：Loading 加载失败时回退到 MainMenu；本事件是
            // 唯一能观察到"加载中途被取消/失败"的信号来源（没有专门的"加载失败"事件）。
            if (evt is AppStateChangedEvent changed && changed.NewState == AppState.MainMenu)
            {
                IsLoading = false;
            }
        }

        public void Dispose()
        {
            foreach (var handle in _subscriptions)
            {
                handle.Dispose();
            }
            _subscriptions.Clear();
        }
    }
}
