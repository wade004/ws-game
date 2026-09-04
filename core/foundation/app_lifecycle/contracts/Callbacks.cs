namespace Core.Foundation.AppLifecycle
{
    /// <summary>
    /// 主状态变化回调（见 03_运行时骨架.md 第 9 节 <c>AppStateHost.onStateChanged</c>）。
    /// 携带参数的回调按各自接口语义定义具名委托（见 common/contracts/Callbacks.cs 注释里的
    /// 约定：不复用通用 <c>Callback</c>、不使用裸 <c>Action</c>）。
    /// </summary>
    public delegate void StateChangedCallback(AppState oldState, AppState newState);

    /// <summary>
    /// InWorld 子状态变化回调。任务书拍板"子状态变化不发 app.state_changed 事件（该事件字段
    /// 是主状态 oldState/newState），提供单独的 OnSubStateChanged 回调订阅"。
    /// <paramref name="oldSubState"/> 在"进入 InWorld 自动置 Explore"这一初次进入场景下为
    /// null（没有"进入前"的子状态）；<paramref name="newSubState"/> 在"离开 InWorld 清空栈"
    /// 场景下为 null（没有"退出后"的子状态）。
    /// </summary>
    public delegate void SubStateChangedCallback(SubStateId? oldSubState, SubStateId? newSubState);
}
