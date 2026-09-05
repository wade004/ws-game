using Core.Foundation.Common;

namespace Core.Foundation.SceneRouter
{
    /// <summary>
    /// 场景路由契约（见 01_分层与依赖.md L0 模块表 <c>scene_router</c> 行、
    /// 03_运行时骨架.md 第 6、9 节）：地图/场景切换的标准流程、加载画面进度、
    /// <c>pre_unload</c>/<c>post_load</c> 挂载点的便捷注册入口。<see cref="LoadScene"/> 只
    /// 启动异步加载流程本身，真正推进"资源是否就绪、何时切换到 InWorld"由调用方每帧（或每次
    /// 需要时）调用 <see cref="Update"/> 驱动——本模块不假设自己拥有主循环。
    /// </summary>
    public interface ISceneRouter
    {
        /// <summary>
        /// 发起一次场景加载（见 03 第 6 节六步流程）。<see cref="State"/> 为
        /// <see cref="SceneRouterState.Loading"/> 时调用抛 <see cref="System.InvalidOperationException"/>；
        /// <paramref name="sceneId"/> 在 <c>world.map</c> 中不存在抛
        /// <see cref="System.ArgumentException"/>；当前应用状态不允许转移到
        /// <c>AppState.Loading</c> 时抛 <see cref="System.InvalidOperationException"/>
        /// 且不发出任何事件。合法则把应用状态机转入 Loading、发出 <c>scene.load_started</c>，
        /// 并对该场景需要的资源引用逐个发起 <c>IResourceLoader.LoadAsync</c>；本方法本身不
        /// 等待加载完成，需要调用方后续调用 <see cref="Update"/> 推进。
        /// </summary>
        void LoadScene(Id sceneId);

        /// <summary>当前已完成加载并生效的场景 id；从未成功加载过任何场景时为 null。</summary>
        Id? GetCurrentScene();

        /// <summary>
        /// 在 <c>pre_unload</c> 挂载点注册回调（对 <c>IHookRegistry.Register</c> 的类型安全
        /// 封装，回调参数直接是待卸载场景的 <see cref="Id"/>，见
        /// <see cref="SceneHookCallback"/>）。返回句柄 Dispose 后取消注册。
        /// </summary>
        SubscriptionHandle RegisterPreUnloadHook(SceneHookCallback callback);

        /// <summary>在 <c>post_load</c> 挂载点注册回调（同 <see cref="RegisterPreUnloadHook"/>，
        /// 回调参数是新加载完成场景的 <see cref="Id"/>）。返回句柄 Dispose 后取消注册。</summary>
        SubscriptionHandle RegisterPostLoadHook(SceneHookCallback callback);

        /// <summary>
        /// 推进异步加载（任务书补充，03 原文签名未包含）：<see cref="State"/> 非
        /// <see cref="SceneRouterState.Loading"/> 时空操作。轮询本次加载涉及的每个资源的
        /// <c>IResourceLoader.IsLoaded</c>/<c>GetLoadProgress</c>，结合 <see cref="LoadScene"/>
        /// 发起加载时记录的回调结果（成功/失败）判定：任一资源加载失败 → 记诊断、
        /// <see cref="State"/> 回到 <see cref="SceneRouterState.Idle"/>、尝试把应用状态机转回
        /// <c>AppState.MainMenu</c>（见本模块 README"加载失败路径"判断记录）；全部资源就绪 →
        /// 若已有当前场景，先触发 <c>pre_unload</c> 钩子、再 <c>IWorldSim.ClearAll</c>（连带派发
        /// 全部 <c>entity.destroyed</c>、按实体经 <c>ISpatialQuery.Unregister</c> 同步）、若注入了
        /// <c>ISpatialQuery</c>/<c>INavigation2D</c> 则额外整图兜底 <c>Clear</c>、发出
        /// <c>scene.unloaded</c>，然后把应用状态机转入 <c>AppState.InWorld</c>、触发
        /// <c>post_load</c> 钩子、发出 <c>scene.load_finished</c>、更新
        /// <see cref="GetCurrentScene"/>、<see cref="State"/> 回到 Idle；仍有资源未就绪且无失败 →
        /// 保持 Loading，仅更新 <see cref="LoadProgress"/>。
        /// </summary>
        void Update();

        /// <summary>当前加载的整体进度 [0, 1]，取本次加载涉及的全部资源
        /// <c>IResourceLoader.GetLoadProgress</c> 的算术平均；<see cref="State"/> 为 Idle 时，
        /// 曾经成功完成过加载则为 1.0，否则为 0.0。</summary>
        double LoadProgress { get; }

        /// <summary>当前状态：<see cref="SceneRouterState.Idle"/> 或
        /// <see cref="SceneRouterState.Loading"/>。</summary>
        SceneRouterState State { get; }
    }
}
