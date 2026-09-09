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
    /// <c>scene_ref</c>/<c>nav_ref</c> 分别经 <see cref="ResourceKind.Scene"/>/
    /// <see cref="ResourceKind.NavMesh"/> 加载（ADR-0016 决策 5 已解决此前"资源种类映射"的
    /// 契约缺口，取代此前借用 <see cref="ResourceKind.DataTable"/> 的临时做法）。
    /// <para>
    /// 判断记录（加载失败路径不新增事件）：04/01 登记表里没有 <c>scene.load_failed</c> 一类
    /// 事件，新增事件按 12_扩展与变更流程.md 走审批，不是本任务能单方面决定的事。任务书就此
    /// 拍板：加载失败时只记诊断（<see cref="ISceneDiagnostics.Error"/>）、
    /// <see cref="State"/> 回落 Idle、尝试 <c>app.RequestTransition(AppState.MainMenu)</c>，
    /// 不发任何事件。该 <c>RequestTransition</c> 是否真的成功依赖调用方传入的
    /// <see cref="IAppStateHost"/> 用的 <c>AppStateMachineConfig</c> 是否登记了
    /// <c>Loading→MainMenu</c> 这条转移——勘误（外部审计 audit-c9ff301-20260909，P3；此前本节
    /// 断言 <c>AppStateMachineConfig.Default()</c> 未登记这条转移、需要调用方自行追加，与现状不
    /// 符）：<c>AppStateMachineConfig.Default()</c>（app_lifecycle 模块默认配置）现已登记
    /// <c>Loading→MainMenu</c>（连同 Boot→MainMenu、MainMenu→Loading、Loading→InWorld、
    /// InWorld→{Pause,MainMenu,Loading}、Pause→{InWorld,MainMenu}），调用方使用默认配置时本方法
    /// 的 <c>RequestTransition</c> 按预期成功，不需要额外追加。<c>RequestTransition</c> 返回
    /// false 时本模块不重复报错——<c>AppStateHost</c> 自身已经记一条诊断警告；调用方若传入自定义
    /// （非 <c>Default()</c>）且未包含这条转移的 <see cref="AppStateMachineConfig"/>，仍会遇到
    /// 加载失败无法回落主菜单的情形，属于该自定义配置自身的责任，不是本模块的缺陷。
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
        private readonly ISpatialQuery? _spatial;
        private readonly INavigation2D? _navigation;

        private Id? _currentScene;
        private SceneRouterState _state = SceneRouterState.Idle;

        private Id? _pendingSceneId;
        private readonly Dictionary<Id, bool?> _pendingResources = new Dictionary<Id, bool?>();
        private readonly List<Id> _pendingResourceOrder = new List<Id>();
        private bool _everCompletedOnce;

        /// <summary>导航代际计数器（FND-02 收口）：每次 <see cref="LoadScene"/> 调用递增。
        /// <see cref="QueueLoad"/> 把发起时的代际值捕获进异步回调闭包，回调触发时与当前代际
        /// 比对——不一致说明这是上一次（已失败/已被新导航取代的）导航请求的迟到回调，直接丢弃，
        /// 不写入 <see cref="_pendingResources"/>。修复前的行为：任何回调都无条件按资源 id 写入
        /// 共享字典，A 请求失败/结束后仍在途的旧回调会把结果写进 B 请求的字典（即便 A/B 的资源 id
        /// 不同也会新增一个 B 从未排队过的 key），导致 <see cref="AnyResourceFailed"/> 之类只读
        /// 字典 Value 的判断把 A 的迟到失败误判成 B 的失败。</summary>
        private int _navigationGeneration;

        /// <summary>
        /// <paramref name="spatial"/>/<paramref name="navigation"/> 可选：注入时，卸载旧场景
        /// （<see cref="IWorldSim.ClearAll"/> 之后）额外调用 <see cref="ISpatialQuery.Clear"/> 与
        /// <see cref="INavigation2D.Clear"/>（<c>mapId</c> 取被卸载的旧场景 id——判断记录：
        /// 05/03 未见"场景 id 与地图 id 是否同一 Id"的显式条款，本实现按既有惯例
        /// <c>Entity.MapId</c> 与所属场景 <c>world.map</c> 行 id 同值处理，供设计层复核），
        /// 作为按实体逐个 <c>ISpatialQuery.Unregister</c>（经 entity.destroyed 事件驱动）之外的
        /// 整图兜底清空，避免任何未经事件驱动同步的登记残留（见 ADR-0016 决策 7）。
        /// </summary>
        public SceneRouter(
            IDataRegistryView registry,
            IResourceLoader loader,
            IAppStateHost app,
            IWorldSim world,
            IHookRegistry hooks,
            IEventBus bus,
            ISceneDiagnostics? diagnostics = null,
            ISpatialQuery? spatial = null,
            INavigation2D? navigation = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _diagnostics = diagnostics ?? new InMemorySceneDiagnostics();
            _spatial = spatial;
            _navigation = navigation;

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

            // 开新的一代导航请求：递增代际计数器，令上一代所有已排队但尚未回调的加载请求
            // （即便晚于本次调用才触发回调）在 QueueLoad 闭包里的代际比对失败，被静默丢弃。
            _navigationGeneration++;
            var generation = _navigationGeneration;

            _pendingSceneId = sceneId;
            _pendingResources.Clear();
            _pendingResourceOrder.Clear();
            _state = SceneRouterState.Loading;

            // 步骤 3：对描述符里的资源引用逐个发起异步加载（ResourceKind.Scene/NavMesh，见
            // ADR-0016 决策 5）。
            QueueLoad(generation, Id.Parse(descriptor.SceneRef), ResourceKind.Scene);

            if (descriptor.NavRef != null)
            {
                QueueLoad(generation, Id.Parse(descriptor.NavRef), ResourceKind.NavMesh);
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

        private void QueueLoad(int generation, Id resourceId, ResourceKind kind)
        {
            _pendingResources[resourceId] = null;
            _pendingResourceOrder.Add(resourceId);

            _loader.LoadAsync(resourceId, kind, (id, success) =>
            {
                // 代际校验（FND-02）：只有仍属于"当前这次导航"的回调才允许写入
                // _pendingResources；上一代导航（已失败或已被新的 LoadScene 取代）的迟到回调
                // 在此原地丢弃，不触碰当前导航的待决字典。
                if (generation != _navigationGeneration)
                {
                    return;
                }

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

            // 终止本次导航请求：代际计数器再递增一次，让这次请求里其余仍在途、尚未回调的
            // 资源加载（例如本次失败只是多个并发资源之一）即使晚些才触发回调，也会在 QueueLoad
            // 闭包里的代际比对中被判定为"旧代际"而丢弃，不会污染下一次 LoadScene 的待决字典。
            _navigationGeneration++;

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

                // 见构造函数判断记录：整图兜底清空，配合上面 DispatchPending 已经驱动完成的
                // 逐实体 ISpatialQuery.Unregister（经 entity.destroyed），双重保证不残留登记。
                _spatial?.Clear();
                _navigation?.Clear(oldSceneId);

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
