using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.AppLifecycle
{
    /// <summary>
    /// 应用级状态机契约（见 01_分层与依赖.md L0 模块表 <c>app_lifecycle</c> 行、
    /// 03_运行时骨架.md 第 1、2、9 节 <c>AppStateHost</c> 签名）。驱动第 1、2 节所述的主状态
    /// 迁移与 InWorld 子状态栈。
    /// <para>
    /// 判断记录：03 第 9 节伪代码 <c>pushSubState(sub): void</c>/<c>popSubState(): void</c>
    /// 是无返回值签名，但任务书明确拍板"PushSubState/PopSubState 检查合法性，非法返回 false
    /// 并记诊断（与 RequestTransition 一致）"，本接口按任务书拍板把两者改为 <c>bool</c>
    /// 返回值——这是任务书对 03 伪代码的显式覆盖指示，不是本模块自行猜测的结果，供设计层
    /// 复核是否需要同步更新 03 第 9 节伪代码。
    /// </para>
    /// </summary>
    public interface IAppStateHost
    {
        /// <summary>当前主状态。初始为 <see cref="AppState.Boot"/>。</summary>
        AppState GetState();

        /// <summary>
        /// 请求转移到 <paramref name="target"/>。不合法（不在当前 <c>AppStateMachineConfig</c>
        /// 允许集合内）返回 false 且不改变状态，记一条诊断警告；合法则切换状态、
        /// <c>PublishImmediate</c> 一个 <see cref="AppStateChangedEvent"/>（key
        /// <c>app.state_changed</c>），再依次调用 <see cref="OnStateChanged"/> 的订阅者，
        /// 返回 true。
        /// </summary>
        bool RequestTransition(AppState target);

        /// <summary>
        /// 把 <paramref name="sub"/> 压入 InWorld 子状态栈。只有当前主状态为
        /// <see cref="AppState.InWorld"/> 时才允许操作；非 InWorld、或"当前子状态→
        /// <paramref name="sub"/>"不在允许集合内，返回 false 且不改变栈，记一条诊断警告。
        /// 合法则压栈、调用 <see cref="OnSubStateChanged"/> 的订阅者，返回 true。
        /// </summary>
        bool PushSubState(SubStateId sub);

        /// <summary>
        /// 弹出栈顶子状态，回落到栈内上一个子状态。非 InWorld、或栈内只剩栈底
        /// <see cref="SubStateId.Explore"/>（不可弹出）时返回 false 且不改变栈，记一条诊断
        /// 警告。合法则弹栈、调用 <see cref="OnSubStateChanged"/> 的订阅者，返回 true。
        /// </summary>
        bool PopSubState();

        /// <summary>当前栈顶子状态；非 InWorld 主状态下为 null。</summary>
        SubStateId? CurrentSubState { get; }

        /// <summary>完整子状态栈，栈底在前、栈顶在后，只读；非 InWorld 主状态下为空。</summary>
        IReadOnlyList<SubStateId> SubStateStack { get; }

        /// <summary>订阅主状态变化，返回可用于取消订阅的句柄。</summary>
        SubscriptionHandle OnStateChanged(StateChangedCallback callback);

        /// <summary>订阅 InWorld 子状态变化，返回可用于取消订阅的句柄。</summary>
        SubscriptionHandle OnSubStateChanged(SubStateChangedCallback callback);

        /// <summary>
        /// 请求退出应用。只允许在 <see cref="AppState.MainMenu"/> 下调用，其它状态返回
        /// false 且记一条诊断警告。成功则置 <see cref="IsExitRequested"/> 为 true 并返回
        /// true——"退出应用"不是一个 <see cref="AppState"/>，触发后续 <c>ExitRequested</c>
        /// 回调/事件由更上层（游戏外壳 Shell）处理，本模块只记录这个标志位。
        /// </summary>
        bool RequestExit();

        /// <summary>是否已经调用过一次成功的 <see cref="RequestExit"/>。</summary>
        bool IsExitRequested { get; }
    }
}
