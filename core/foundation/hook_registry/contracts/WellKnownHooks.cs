using Core.Foundation.Common;

namespace Core.Foundation.HookRegistry
{
    /// <summary>
    /// 架构文档已点名的挂载点 id 常量（见 03_运行时骨架.md 第 6 节场景路由：
    /// <c>pre_unload</c>/<c>post_load</c> 是 <c>SceneRouter</c> 暴露给 <see cref="IHookRegistry"/>
    /// 的挂载点）。本类型只提供常量，不自动 <see cref="IHookRegistry.DeclareHookPoint(Id,string)"/>
    /// ——具体声明属于场景路由模块（<c>core/foundation/scene_router</c>，未来任务）组装时的职责，
    /// 不属于本模块。
    /// </summary>
    public static class WellKnownHooks
    {
        /// <summary>场景卸载前触发（见 03 第 6 节步骤 4：存档系统"切换场景时自动存档"在此完成持久化）。</summary>
        public static readonly Id ScenePreUnload = new Id("found.hook.scene_pre_unload");

        /// <summary>场景加载与初始化完成、应用状态机转入 InWorld 后触发（见 03 第 6 节步骤 6）。</summary>
        public static readonly Id ScenePostLoad = new Id("found.hook.scene_post_load");
    }
}
