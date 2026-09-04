using Core.Foundation.Common;

namespace Core.Foundation.SceneRouter
{
    /// <summary>
    /// <see cref="ISceneRouter.RegisterPreUnloadHook"/>/<see cref="ISceneRouter.RegisterPostLoadHook"/>
    /// 的回调委托：对 <c>hook_registry</c> 的 <c>HookCallback(HookArgs)</c> 做类型安全封装，
    /// 直接取出 <c>sceneId</c> 参数（见 03_运行时骨架.md 第 6 节"pre_unload/post_load 是
    /// SceneRouter 暴露给 HookRegistry 的挂载点"、任务书"回调参数 HookArgs 含 sceneId"）。
    /// </summary>
    public delegate void SceneHookCallback(Id sceneId);
}
