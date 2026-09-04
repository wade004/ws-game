namespace Core.Foundation.HookRegistry
{
    /// <summary>
    /// <see cref="HookRegistry"/> 的策略配置项（见 01_分层与依赖.md L0 模块表
    /// <c>hook_registry</c> 行"策略配置项"列："挂载点是否允许多个回调、执行顺序"——
    /// "是否允许多个回调"落在 <see cref="HookPointDefinition.AllowMultiple"/>，"执行顺序"
    /// 落在 <see cref="IHookRegistry.Register"/> 的 <c>order</c> 参数；本类型只承载
    /// <c>hook.invoked</c> 调试事件是否发出这一项）。
    /// </summary>
    public sealed class HookRegistryOptions
    {
        /// <summary>
        /// 为 true 时，每次 <see cref="IHookRegistry.Invoke"/> 调用后额外
        /// <c>PublishImmediate</c> 一个 <see cref="HookInvokedEvent"/>（key
        /// <c>hook.invoked</c>，调试用，见 01 模块表 hook_registry 行事件列）。默认 false。
        /// 为 true 时构造 <see cref="HookRegistry"/> 必须传入非空 <c>IEventBus</c>，否则在
        /// 构造期抛 <see cref="System.ArgumentException"/>。
        /// </summary>
        public bool EmitInvokedEvent { get; set; }
    }
}
