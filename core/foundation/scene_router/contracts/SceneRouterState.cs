namespace Core.Foundation.SceneRouter
{
    /// <summary>场景路由器当前状态（任务书补充，03_运行时骨架.md 第 9 节 <c>SceneRouter</c>
    /// 签名原文未定义状态类型）。<see cref="Loading"/> 覆盖 03 第 6 节步骤 3~6 之间的整个
    /// 异步加载区间；期间调用 <see cref="ISceneRouter.LoadScene"/> 一律抛
    /// <see cref="System.InvalidOperationException"/>（见 <see cref="ISceneRouter"/> 注释）。</summary>
    public enum SceneRouterState
    {
        Idle,
        Loading
    }
}
