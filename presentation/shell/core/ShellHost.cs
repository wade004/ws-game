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
using Presentation.VfxSfx.Contracts;

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
        private readonly NewGameRollback? _newGameRollback;

        /// <summary>缺口 11 恢复：G1 给 <see cref="LoadResult"/> 补了 <see cref="LoadResult.CurrentMapId"/>
        /// 后，<see cref="LoadGame"/> 改读该字段，不再需要本委托作为唯一来源——保留为可选覆盖
        /// （见该方法判断记录），未注入时为 null。</summary>
        private readonly LoadedMapIdResolver? _loadedMapIdResolver;
        private readonly Func<string> _timestampProvider;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();

        private ShellPage _mainMenuSubPage = ShellPage.MainMenu;

        /// <summary>ADR-0121 第 10 条（D10）：本外壳的诊断出口（<see cref="LoadGame"/> 场景路由被拒绝时
        /// 记警告）；供装配根/宿主轮询转发到控制台，或由测试直接检查。</summary>
        public IPresentationDiagnostics Diagnostics { get; }

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
            : this(
                appState, sceneRouter, saveSystem, settingsStore, difficulty, inputMap, eventBus, newGameStarter,
                timestampProvider, loadedMapIdResolver, new PresentationDiagnosticsRecorder())
        {
        }

        /// <summary>ADR-0121 第 10 条（D10）新增重载：可注入诊断出口（<see cref="LoadGame"/> 场景路由失败时
        /// 记一条警告）。旧构造函数签名原样保留并转调本重载（诊断默认自建一份
        /// <see cref="PresentationDiagnosticsRecorder"/>，经 <see cref="Diagnostics"/> 读取），
        /// 遵守 ABI 只新增。</summary>
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
            LoadedMapIdResolver? loadedMapIdResolver,
            IPresentationDiagnostics diagnostics)
            : this(
                appState, sceneRouter, saveSystem, settingsStore, difficulty, inputMap, eventBus, newGameStarter,
                timestampProvider, loadedMapIdResolver, diagnostics, null)
        {
        }

        /// <summary>收口遗留修复 A5 新增重载：可注入 <see cref="NewGameRollback"/>（初始存档写入失败时撤销
        /// <see cref="NewGameStarter"/> 的效果，见 <see cref="NewGame"/> 判断记录）。上一个重载签名原样保留并转调。</summary>
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
            LoadedMapIdResolver? loadedMapIdResolver,
            IPresentationDiagnostics diagnostics,
            NewGameRollback? newGameRollback)
        {
            _newGameRollback = newGameRollback;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
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

        /// <summary>
        /// 判断记录（收口遗留修复 A5，初始存档写入失败时回滚）：<see cref="IDifficultyHost.Apply"/> 与
        /// <see cref="NewGameStarter"/> 都已改变了宿主状态，而存档写入失败后本方法返回 false——此前这两处
        /// 改动原样残留（难度已切、游戏层起始状态已建，却没有存档也没有进图）。现改为：先快照难度宿主
        /// （经其 <see cref="IPersistable"/> 存档段，<c>DifficultyHost</c> 实现之；这是难度三项状态
        /// （档位/作用域/地图）的既有精确导出/恢复口径，恢复不发事件、不走 Apply 判定），写档失败时
        /// 依次 ① 调 <see cref="NewGameRollback"/>（游戏层撤销起始状态；未注入则记警告，框架无法替游戏层
        /// 撤销）② 把难度宿主恢复到快照（宿主不是 <see cref="IPersistable"/> 则记警告）。回滚本身出错只记
        /// 诊断、不外抛，返回值仍为 false。不回滚的情形（行为不变）：难度未登记/不允许切换（Apply 阶段尚未
        /// 改动任何状态）；<see cref="NewGameStarter"/> 抛异常（异常原样外抛）；场景加载被拒绝（此时初始存档
        /// 已落盘，现状保持）。已发出的 <see cref="DifficultyAppliedEvent"/> 无法撤回。
        /// </summary>
        public bool NewGame(Id slotId, Id difficultyId, Id? archetypeId)
        {
            var difficultyPersistable = _difficulty as IPersistable;
            var difficultySnapshot = difficultyPersistable?.Save();

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
                RollbackNewGame(slotId, difficultyId, archetypeId, difficultyPersistable, difficultySnapshot);
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

        private void RollbackNewGame(
            Id slotId, Id difficultyId, Id? archetypeId, IPersistable? difficultyPersistable, JsonValue? difficultySnapshot)
        {
            if (_newGameRollback != null)
            {
                try
                {
                    _newGameRollback(slotId, difficultyId, archetypeId);
                }
                catch (Exception ex)
                {
                    Diagnostics.Warn($"ShellHost.NewGame：存档写入失败后 NewGameRollback 抛异常（{ex.GetType().Name}）：{ex.Message}");
                }
            }
            else
            {
                Diagnostics.Warn(
                    "ShellHost.NewGame：初始存档写入失败，但未注入 NewGameRollback，NewGameStarter 已创建的游戏层起始状态未回滚");
            }

            if (difficultyPersistable == null || difficultySnapshot == null)
            {
                Diagnostics.Warn("ShellHost.NewGame：初始存档写入失败，难度宿主不支持快照恢复（未实现 IPersistable），已应用的难度未回滚");
                return;
            }

            try
            {
                difficultyPersistable.Load(difficultySnapshot);
            }
            catch (Exception ex)
            {
                Diagnostics.Warn($"ShellHost.NewGame：存档写入失败后恢复难度快照抛异常（{ex.GetType().Name}）：{ex.Message}");
            }
        }

        /// <summary>
        /// 判断记录（缺口 11 恢复）：优先用 <see cref="LoadResult.CurrentMapId"/>（G1 补的字段，读自
        /// <c>world.current_map_id</c> 段，见任务书"ShellHost 读档后用 LoadResult.CurrentMapId 进图"）；
        /// 该字段为 null（存档未登记 world 段，或该段尚未来得及在游戏层实现——见其字段注释）时才回退
        /// <see cref="_loadedMapIdResolver"/>（未注入时视为"无法确定地图 id"，跳过场景切换，读档结果
        /// 本身仍照常返回）。场景路由抛 <see cref="ArgumentException"/>/<see cref="InvalidOperationException"/>
        /// 时（ADR-0121 第 10 条）：读档仍算成功，但返回值的 <see cref="LoadResult.SceneRouteFailed"/> 置位、
        /// <see cref="LoadResult.SceneRouteError"/> 带原因，并向 <see cref="Diagnostics"/> 记一条警告。
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
                    catch (ArgumentException ex)
                    {
                        // 地图 id 未知：读档本身仍然算成功，但场景切换失败要显式暴露（ADR-0121 第 10 条）。
                        result = ReportSceneRouteFailure(result, mapId.Value, ex);
                    }
                    catch (InvalidOperationException ex)
                    {
                        // 当前应用状态不允许切到 Loading（例如已经在 Loading 中）：同上，读档结果照常返回。
                        result = ReportSceneRouteFailure(result, mapId.Value, ex);
                    }
                }
            }

            return result;
        }

        /// <summary>ADR-0121 第 10 条（D10）：读档成功后场景路由被拒绝——读档结果保持成功（存档已加载），
        /// 但返回的 <see cref="LoadResult"/> 置位 <see cref="LoadResult.SceneRouteFailed"/> 并携带原因，
        /// 同时向 <see cref="Diagnostics"/> 记一条警告；不再静默吞掉（AGENTS 第 3 节"运行时路径不静默降级"）。</summary>
        private LoadResult ReportSceneRouteFailure(LoadResult result, Id mapId, Exception ex)
        {
            var error = $"读档成功但场景路由拒绝加载地图 \"{mapId}\"（{ex.GetType().Name}）：{ex.Message}";
            Diagnostics.Warn($"ShellHost.LoadGame：{error}");
            return result.WithSceneRouteFailure(error);
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

            // ADR-0143：玩家的摇杆处理覆盖（死区/曲线/平滑）与键位同一条设置通道；文件里没有这一项即不覆盖（保持动作定义的声明值）。
            if (data.TryGetValue("input_axis_settings", out var axisVal) && axisVal is JsonObject axisObj)
            {
                _inputMap.ImportAxisSettings(axisObj);
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
            var axisSettings = _inputMap.ExportAxisSettings();
            if (axisSettings.Count > 0 && !additionalFields.ContainsKey("input_axis_settings"))
            {
                builder.Add("input_axis_settings", axisSettings);
            }

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
