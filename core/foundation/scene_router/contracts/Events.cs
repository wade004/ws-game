using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Foundation.SceneRouter
{
    /// <summary>本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// data/_sample/found/found.event_catalog.json、01_分层与依赖.md L0 模块表
    /// <c>scene_router</c> 行"主要事件：scene.load_started、scene.load_finished、
    /// scene.unloaded"）。与 sim_loop 的 <c>SimEventKeys</c>、hook_registry 的
    /// <c>HookEventKeys</c> 同一惯例：本模块自持一份定义，不依赖 event_bus 生成的
    /// <c>EventKeys.g.cs</c>。</summary>
    public static class SceneRouterEventKeys
    {
        public static readonly Id LoadStarted = new Id("scene.load_started");
        public static readonly Id LoadFinished = new Id("scene.load_finished");
        public static readonly Id Unloaded = new Id("scene.unloaded");
    }

    /// <summary><see cref="ISceneRouter.LoadScene"/> 开始加载时触发（见 03_运行时骨架.md
    /// 第 6 节步骤 1、01 模块表 <c>scene_router</c> 行）。字段：<see cref="SceneId"/>，与
    /// <c>found.event_catalog.json</c> 该行登记一致（该行 description 标注"字段为建议值"）。</summary>
    public sealed class SceneLoadStartedEvent : IEvent
    {
        public Id Key => SceneRouterEventKeys.LoadStarted;

        public Id SceneId { get; }

        public SceneLoadStartedEvent(Id sceneId) => SceneId = sceneId;
    }

    /// <summary>场景加载与初始化完成、应用状态机转入 InWorld 后触发（见 03 第 6 节步骤 6）。
    /// 字段：<see cref="SceneId"/>。</summary>
    public sealed class SceneLoadFinishedEvent : IEvent
    {
        public Id Key => SceneRouterEventKeys.LoadFinished;

        public Id SceneId { get; }

        public SceneLoadFinishedEvent(Id sceneId) => SceneId = sceneId;
    }

    /// <summary>旧场景的 WorldSim 集合与全部 View 卸载完成后触发（见 03 第 6 节步骤 4）。
    /// 字段：<see cref="SceneId"/>（被卸载的旧场景 id）。</summary>
    public sealed class SceneUnloadedEvent : IEvent
    {
        public Id Key => SceneRouterEventKeys.Unloaded;

        public Id SceneId { get; }

        public SceneUnloadedEvent(Id sceneId) => SceneId = sceneId;
    }
}
