using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Foundation.HookRegistry
{
    /// <summary>
    /// 本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// data/_sample/found/found.event_catalog.json）。与 sim_loop 模块的
    /// <c>SimEventKeys</c> 同一惯例：模块自己持有一份发出事件的 key 常量，不依赖
    /// event_bus 模块的生成物 <c>EventKeys.g.cs</c>（见该文件顶部生成器警告——它是从
    /// 数据登记表批量生成的强类型入口，供跨模块引用事件已知的 key，两者值始终一致，
    /// 不产生冲突）。
    /// </summary>
    public static class HookEventKeys
    {
        public static readonly Id HookInvoked = new Id("hook.invoked");
    }

    /// <summary>
    /// <see cref="IHookRegistry.Invoke"/> 被调用时触发（调试用，见 01_分层与依赖.md L0
    /// 模块表 <c>hook_registry</c> 行事件列）。是否发出由 <see cref="HookRegistryOptions.EmitInvokedEvent"/>
    /// 控制，默认关闭。
    /// <para>
    /// 判断记录：<c>data/_sample/found/found.event_catalog.json</c> 里 <c>hook.invoked</c>
    /// 一行的 <c>fields</c> 只登记了 <c>hookId</c>，但该行 description 明确标注"字段为建议值"，
    /// 03_运行时骨架.md 第 9 节的 <c>HookRegistry</c> 接口签名本身完全没有提及 <c>invoke</c>
    /// 会发出什么事件、携带什么字段（03 只讲 <c>invoke(hookId, args)</c> 这个方法签名本身）。
    /// 任务书显式拍板本事件携带 <c>{ hookId, callbackCount }</c> 两个字段——多出的
    /// <c>callbackCount</c>（本次调用时该挂载点已注册的回调数）对调试场景有实际价值
    /// （能看出"这次 invoke 到底调了几个回调"，不必再反查 <see cref="IHookRegistry.CallbackCount"/>）。
    /// 与 sim_loop 处理 <c>sim.tick_started</c> 补充 <c>dt</c> 字段是同一类"登记表标注建议值、
    /// 03 未排他性限定字段 ⇒ 允许按需要补充"的处理，已在此记录，供设计层复核是否需要同步
    /// 更新 <c>found.event_catalog.json</c> 的 <c>hook.invoked</c> 行。
    /// </para>
    /// </summary>
    public sealed class HookInvokedEvent : IEvent
    {
        public Id Key => HookEventKeys.HookInvoked;

        /// <summary>被调用的挂载点 id。</summary>
        public Id HookId { get; }

        /// <summary>本次调用时该挂载点已注册（未取消订阅）的回调数量。</summary>
        public int CallbackCount { get; }

        public HookInvokedEvent(Id hookId, int callbackCount)
        {
            HookId = hookId;
            CallbackCount = callbackCount;
        }
    }
}
