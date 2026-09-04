namespace Core.Foundation.HookRegistry
{
    /// <summary>
    /// 挂载点回调委托（见 03_运行时骨架.md 第 9 节 <c>HookRegistry.register(hookId,
    /// callback: Callback, order: Int)</c>）。按 01_分层与依赖.md 第 8 节"策略注入回调"，
    /// 由游戏层实现并注册，运行时由 <see cref="IHookRegistry.Invoke"/> 回调。
    /// </summary>
    public delegate void HookCallback(HookArgs args);
}
