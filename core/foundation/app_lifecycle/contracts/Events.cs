using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Foundation.AppLifecycle
{
    /// <summary>
    /// 本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// data/_sample/found/found.event_catalog.json）。与 sim_loop 的 <c>SimEventKeys</c>、
    /// hook_registry 的 <c>HookEventKeys</c> 同一惯例：模块自己持有一份发出事件的 key
    /// 常量，不依赖 event_bus 模块的生成物 <c>EventKeys.g.cs</c>。
    /// </summary>
    public static class AppEventKeys
    {
        public static readonly Id StateChanged = new Id("app.state_changed");
    }

    /// <summary>
    /// 应用级状态机主状态迁移完成时触发（见 01_分层与依赖.md L0 模块表 <c>app_lifecycle</c>
    /// 行、03_运行时骨架.md 第 1、2 节、第 9 节 <c>AppStateHost.onStateChanged</c>）。字段与
    /// <c>found.event_catalog.json</c> 登记一致：<see cref="OldState"/>、<see cref="NewState"/>。
    /// 只在主状态转移时发出；InWorld 子状态变化不发本事件（见
    /// <see cref="SubStateChangedCallback"/> 注释、本模块 README 判断记录）。
    /// </summary>
    public sealed class AppStateChangedEvent : IEvent
    {
        public Id Key => AppEventKeys.StateChanged;

        public AppState OldState { get; }

        public AppState NewState { get; }

        public AppStateChangedEvent(AppState oldState, AppState newState)
        {
            OldState = oldState;
            NewState = newState;
        }
    }
}
