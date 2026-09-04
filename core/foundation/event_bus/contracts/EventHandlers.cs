namespace Core.Foundation.EventBus
{
    /// <summary>
    /// 非泛型事件处理回调：接收任意实现 <see cref="IEvent"/> 的事件实例。
    /// 供 <see cref="IEventBus.Subscribe(Core.Foundation.Common.Id, EventHandler)"/> 使用。
    /// </summary>
    public delegate void EventHandler(IEvent evt);

    /// <summary>
    /// 泛型事件处理回调：只接收具体事件类型 <typeparamref name="T"/> 的实例。
    /// 供 <see cref="IEventBus.Subscribe{T}"/> 使用；某次派发的事件 Key 匹配但运行时类型
    /// 不是 <typeparamref name="T"/> 时，按策略跳过并记警告，不会调用本委托（见 IEventBus 注释）。
    /// </summary>
    public delegate void EventHandler<in T>(T evt) where T : IEvent;
}
