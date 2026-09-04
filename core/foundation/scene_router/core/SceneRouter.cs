using System;
using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.HookRegistry;
using Core.Foundation.SimLoop;

namespace Core.Foundation.SceneRouter
{
    /// <summary>
    /// <see cref="ISceneRouter"/> 的默认实现（见 03_运行时骨架.md 第 6 节场景路由六步流程、
    /// 第 9 节 <c>SceneRouter</c> 签名）。构造时向 <see cref="IHookRegistry"/> 声明
    /// <c>pre_unload</c>/<c>post_load</c> 两个挂载点（若已声明则复用，不重复声明）。
    /// <para>
    /// 判断记录（资源种类映射）：<see cref="IResourceLoader.LoadAsync"/> 的 <see cref="ResourceKind"/>
    /// 只有 <c>Image|Audio|Font|DataTable</c> 四种（见 02_引擎适配层.md 第 1.7 节），没有一种
    /// 精确对应"场景资源"（<c>scene_ref</c>）或"导航资源"（<c>nav_ref</c>）——这是本任务允许
    /// 修改的文件范围之外的既有契约缺口（<c>IResourceLoader.cs</c> 不在本任务可改动范围），
    /// 本实现选择用 <see cref="ResourceKind.DataTable"/> 承载这两类资源加载请求：二者都是
    /// "结构化关卡/导航数据"而非图片/音频/字体一类媒体资源，是四个既有取值里语义最接近的一个，
    /// 已在本模块 README"判断记录"详细记录，供设计层复核是否需要单独 ADR 给
    /// <see cref="ResourceKind"/> 增补 <c>Scene</c>/<c>NavMesh</c> 一类取值。
    /// </para>
    /// <para>
    /// 判断记录（加载失败路径不新增事件）：04/01 登记表里没有 <c>scene.load_failed</c> 一类
    /// 事件，新增事件按 12_扩展与变更流程.md 走审批，不是本任务能单方面决定的事。任务书就此
    /// 拍板：加载失败时只记诊断（<see cref="ISceneDiagnostics.Error"/>）、
    /// <see cref="State"/> 回落 Idle、尝试 <c>app.RequestTransition(AppState.MainMenu)</c>，
    /// 不发任何事件。该 <c>RequestTransition</c> 是否真的成功依赖调用方传入的
    /// <see cref="IAppStateHost"/> 用的 <c>AppStateMachineConfig</c> 是否登记了
    /// <c>Loading→MainMenu</c> 这条转移——<c>AppStateMachineConfig.Default()</c>（
    /// app_lifecycle 模块默认配置）目前没有登记这条转移（只有 Boot→MainMenu、MainMenu→Loading、
    /// Loading→InWorld、InWorld→{Pause,MainMenu,Loading}、Pause→{InWorld,MainMenu}），本模块
    /// 不代为修改那份默认配置（超出本任务允许改动的文件范围），只是如实调用
    /// <c>RequestTransition</c>；调用方若需要"加载失败后真的能回到主菜单"，需要自行在装配期
    /// 对传入的 <c>AppStateMachineConfig</c> 追加 <c>AllowTransition(Loading, MainMenu)</c>
    /// （已在本模块 README、测试里注明）。<c>RequestTransition</c> 返回 false 时本模块不重复
    /// 报错——<c>AppStateHost</c> 自身已经记一条诊断警告。
    /// </para>
    /// </summary>
    public sealed class SceneRouter : ISceneRouter
    {
        private readonly IDataRegistryView _registry;
        private readonly IResourceLoader _loader;
        private readonly IAppStateHost _app;
        private readonly IWorldSim _world;
        private readonly IHookRegistry _hooks;
        private readonly IEventBus _bus;
        private readonly ISceneDiagnostics _diagnostics;

        private Id? _currentScene;
        private SceneRouterState _state = SceneRouterState.Idle;

        private Id? _pendingSceneId;
        private readonly Dictionary<Id, bool?> _pendingResources = new Dictionary<Id, bool?>();
        private readonly List<Id> _pendingResourceOrder = new List<Id>();
        private bool _everCompletedOnce;

        public SceneRouter(
            IDataRegistryView registry,
            IResourceLoader loader,
            IAppStateHost app,
            IWorldSim world,
            IHookRegistry hooks,
            IEventBus bus,
            ISceneDiagnostics? diagnostics = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _diagnostics = diagnostics ?? new InMemorySceneDiagnostics();

            DeclareHookPointIfMissing(WellKnownHooks.ScenePreUnload, "sceneId: Id");
            DeclareHookPointIfMissing(WellKnownHooks.ScenePostLoad, "sceneId: Id");
        }

        public SceneRouterState State => _state;

        public double LoadProgress
        {
            get
            {
                if (_pendingResourceOrder.Count == 0)
                {
                    return _state == SceneRouterState.Idle && _everCompletedOnce ? 1.0 : 0.0;
                }

                double sum = 0;
                for (var i = 0; i < _pendingResourceOrder.Count; i++)
                {
                    sum += _loader.GetLoadProgress(_pendingResourceOrder[i]);
                }
                return sum / _pendingResourceOrder.Count;
            }
        }

        public Id? GetCurrentScene() => _currentScene;

        public void LoadScene(Id sceneId)
        {
            // 步骤 1：正在加载中 → 抛异常；world.map 记录不存在 → 抛异常。
            if (_state == SceneRouterState.Loading)
            {
                throw new InvalidOperationException($"SceneRouter 正在加载场景 \"{_pendingSceneId}\"，完成前不能再次调用 LoadScene");
            }

            var record = _registry.Get(WorldMapSchema.Table.Name, sceneId);
            if (record == null)
            {
                throw new ArgumentException($"world.map 中不存在场景 \"{sceneId}\"", nameof(sceneId));
            }

            // 步骤 2：应用状态机转入 Loading（不合法则抛异常、不发任何事件）；随后发 scene.load_started。
            if (!_app.RequestTransition(AppState.Loading))
            {
                throw new InvalidOperationException(
                    $"应用状态机不允许从当前状态 \"{_app.GetState()}\" 转移到 Loading，LoadScene 已中止");
            }

            _bus.PublishImmediate(new SceneLoadStartedEvent(sceneId));

            var descriptor = SceneDescriptor.FromRecord(record);

            _pendingSceneId = sceneId;
            _pendingResources.Clear();
            _pendingResourceOrder.Clear();
            _state = SceneRouterState.Loading;

            // 步骤 3：对描述符里的资源引用逐个发起异步加载，记录回调结果（见类型注释"资源种类映射"判断记录）。
            QueueLoad(Id.Parse(descriptor.SceneRef), ResourceKind.DataTable);

            if (descriptor.NavRef != null)
            {
                QueueLoad(Id.Parse(descriptor.NavRef), ResourceKind.DataTable);
            }
        }

        public void Update()
        {
            if (_state != SceneRouterState.Loading)
            {
                return;
            }

            if (AnyResourceFailed())
            {
                HandleLoadFailure();
                return;
            }

            if (!AllResourcesResolved())
            {
                return; // 仍在加载中，LoadProgress 属性会实时反映进度。
            }

            FinishLoading();
        }

        public SubscriptionHandle RegisterPreUnloadHook(SceneHookCallback callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            return _hooks.Register(WellKnownHooks.ScenePreUnload, args => callback(args.Get<Id>("sceneId")), order: 0);
        }

        public SubscriptionHandle RegisterPostLoadHook(SceneHookCallback callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            return _hooks.Register(WellKnownHooks.ScenePostLoad, args => callback(args.Get<Id>("sceneId")), order: 0);
        }

        // ---------------------------------------------------------------
        // 内部实现
        // ---------------------------------------------------------------

        private void QueueLoad(Id resourceId, ResourceKind kind)
        {
            _pendingResources[resourceId] = null;
            _pendingResourceOrder.Add(resourceId);

            _loader.LoadAsync(resourceId, kind, (id, success) =>
            {
                _pendingResources[id] = success;
            });
        }

        private bool AnyResourceFailed()
        {
            foreach (var kv in _pendingResources)
            {
                if (kv.Value == false)
                {
                    return true;
                }
            }
            return false;
        }

        private bool AllResourcesResolved()
        {
            foreach (var kv in _pendingResources)
            {
                if (kv.Value == null)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>见类型注释"加载失败路径不新增事件"判断记录。</summary>
        private void HandleLoadFailure()
        {
            _diagnostics.Error($"场景 \"{_pendingSceneId}\" 加载失败：一个或多个资源加载失败", null);

            _pendingResources.Clear();
            _pendingResourceOrder.Clear();
            _pendingSceneId = null;
            _state = SceneRouterState.Idle;

            _app.RequestTransition(AppState.MainMenu);
        }

        /// <summary>03 第 6 节步骤 4~6：（若有旧场景）pre_unload → ClearAll → scene.unloaded；
        /// 转入 InWorld → post_load → scene.load_finished → 更新当前场景。步骤 5"依据 Spawn
        /// 刷新规则与 WorldState 构建新场景初始状态"属于 L4，本模块不做，见本模块 README。</summary>
        private void FinishLoading()
        {
            var newSceneId = _pendingSceneId!.Value;

            if (_currentScene.HasValue)
            {
                var oldSceneId = _currentScene.Value;
                _hooks.Invoke(WellKnownHooks.ScenePreUnload, MakeSceneIdArgs(oldSceneId));
                _world.ClearAll();

                // 判断记录：IWorldSim.ClearAll 只 Enqueue 每个实体的 entity.destroyed（不立即
                // 派发，见 sim_loop 模块 IWorldSim.ClearAll 注释）。SceneRouter 不运行在
                // WorldSim.Tick 循环内，没有别的地方会替它把这批事件发出去；这里显式调用一次
                // DispatchPending，保证订阅者（如 ViewBinder）在收到 scene.unloaded 之前已经
                // 先收到全部 entity.destroyed——与任务书"pre_unload 先于 ClearAll（实体计数
                // 归零、entity.destroyed 送达）先于 scene.unloaded"的顺序要求一致。
                _bus.DispatchPending();

                _bus.PublishImmediate(new SceneUnloadedEvent(oldSceneId));
            }

            _app.RequestTransition(AppState.InWorld);

            _hooks.Invoke(WellKnownHooks.ScenePostLoad, MakeSceneIdArgs(newSceneId));
            _bus.PublishImmediate(new SceneLoadFinishedEvent(newSceneId));

            _currentScene = newSceneId;
            _everCompletedOnce = true;
            _state = SceneRouterState.Idle;
            _pendingSceneId = null;
            _pendingResources.Clear();
            _pendingResourceOrder.Clear();
        }

        private static HookArgs MakeSceneIdArgs(Id sceneId) =>
            new HookArgs(new Dictionary<string, object?> { ["sceneId"] = sceneId });

        private void DeclareHookPointIfMissing(Id hookId, string signature)
        {
            var points = _hooks.HookPoints;
            for (var i = 0; i < points.Count; i++)
            {
                if (points[i].HookId == hookId)
                {
                    return;
                }
            }
            _hooks.DeclareHookPoint(hookId, signature);
        }
    }
}
