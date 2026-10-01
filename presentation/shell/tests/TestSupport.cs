using System;
using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;
using Core.Foundation.EventBus;
using Core.Foundation.SceneRouter;
using Core.Gameplay.Difficulty;

namespace Tests.PresentationShell
{
    internal static class TestSupport
    {
        public static IEventBus BuildEventBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
    }

    /// <summary>
    /// 最小 <see cref="ISceneRouter"/> Fake：不做真实资源加载，<see cref="LoadScene"/> 立即把应用
    /// 状态机转入 Loading 并发出 <c>scene.load_started</c>；<see cref="Update"/> 模拟"下一次调用即
    /// 加载完成"，转入 InWorld 并发出 <c>scene.load_finished</c>。惯例同任务书"Fake 宿主"要求，只
    /// 覆盖 <c>ShellHost</c> 测试实际用到的行为，不还原真实的异步资源加载与场景卸载流程。
    /// </summary>
    internal sealed class FakeSceneRouter : ISceneRouter
    {
        private readonly IAppStateHost _appState;
        private readonly IEventBus _eventBus;

        public const string UnknownSceneMessage = "未知场景 id";
        public const string AlreadyLoadingMessage = "已在加载中";

        public readonly List<Id> LoadSceneCalls = new List<Id>();
        public bool ThrowUnknownSceneOnLoad;

        public FakeSceneRouter(IAppStateHost appState, IEventBus eventBus)
        {
            _appState = appState;
            _eventBus = eventBus;
        }

        public SceneRouterState State { get; private set; } = SceneRouterState.Idle;

        public double LoadProgress { get; private set; }

        public Id? CurrentSceneId { get; private set; }

        public void LoadScene(Id sceneId)
        {
            if (State == SceneRouterState.Loading)
            {
                throw new InvalidOperationException(AlreadyLoadingMessage);
            }

            if (ThrowUnknownSceneOnLoad)
            {
                throw new ArgumentException(UnknownSceneMessage);
            }

            if (!_appState.RequestTransition(AppState.Loading))
            {
                throw new InvalidOperationException("当前应用状态不允许转入 Loading");
            }

            LoadSceneCalls.Add(sceneId);
            State = SceneRouterState.Loading;
            LoadProgress = 0;
            _eventBus.PublishImmediate(new SceneLoadStartedEvent(sceneId));
        }

        public Id? GetCurrentScene() => CurrentSceneId;

        public SubscriptionHandle RegisterPreUnloadHook(SceneHookCallback callback) => new SubscriptionHandle(() => { });

        public SubscriptionHandle RegisterPostLoadHook(SceneHookCallback callback) => new SubscriptionHandle(() => { });

        public void Update()
        {
            if (State != SceneRouterState.Loading)
            {
                return;
            }

            var sceneId = LoadSceneCalls[LoadSceneCalls.Count - 1];
            LoadProgress = 1.0;
            CurrentSceneId = sceneId;
            _appState.RequestTransition(AppState.InWorld);
            State = SceneRouterState.Idle;
            _eventBus.PublishImmediate(new SceneLoadFinishedEvent(sceneId));
        }
    }

    internal sealed class FakeDifficultyHost : IDifficultyHost, IPersistable
    {
        /// <summary>仿 <c>DifficultyHost</c> 的自定义存档段：Save 导出 (tier, scope, mapId) 三项，Load 原样恢复
        /// （不发事件、不走 Apply 判定），供 ShellHost.NewGame 失败回滚经 IPersistable 精确还原。</summary>
        public string SectionKey => "world.difficulty";

        public int LoadCallCount;

        public JsonValue Save()
        {
            var b = new JsonObjectBuilder();
            b.Add("tierId", CurrentTier.HasValue ? (JsonValue)new JsonString(CurrentTier.Value.Value) : JsonNull.Instance);
            b.Add("scope", CurrentScope.HasValue ? (JsonValue)new JsonString(CurrentScope.Value.ToString()) : JsonNull.Instance);
            b.Add("mapId", CurrentMapId.HasValue ? (JsonValue)new JsonString(CurrentMapId.Value.Value) : JsonNull.Instance);
            return b.Build();
        }

        public void Load(JsonValue data)
        {
            LoadCallCount++;
            CurrentTier = null;
            CurrentScope = null;
            CurrentMapId = null;
            if (!(data is JsonObject obj)) return;
            if (obj.TryGetValue("tierId", out var t) && t is JsonString ts) CurrentTier = new Id(ts.Value);
            if (obj.TryGetValue("scope", out var sc) && sc is JsonString ss) CurrentScope = (DifficultyScope)Enum.Parse(typeof(DifficultyScope), ss.Value);
            if (obj.TryGetValue("mapId", out var m) && m is JsonString ms) CurrentMapId = new Id(ms.Value);
        }

        public readonly HashSet<Id> KnownTiers = new HashSet<Id>();
        public readonly List<Id> AppliedTiers = new List<Id>();

        public Id? CurrentTier { get; private set; }

        public DifficultyScope? CurrentScope { get; private set; }

        public Id? CurrentMapId { get; private set; }

        public double LootMultiplier => 1.0;

        public bool AllowMidSwitch { get; set; } = true;

        public bool Apply(Id tierId, DifficultyScope scope, Id? mapId)
        {
            if (!KnownTiers.Contains(tierId))
            {
                throw new ArgumentException($"未登记的难度档位 \"{tierId}\"");
            }

            if (CurrentTier.HasValue && !AllowMidSwitch)
            {
                return false;
            }

            CurrentTier = tierId;
            CurrentScope = scope;
            CurrentMapId = mapId;
            AppliedTiers.Add(tierId);
            return true;
        }
    }
}
