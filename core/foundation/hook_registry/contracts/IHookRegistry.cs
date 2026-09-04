using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.HookRegistry
{
    /// <summary>
    /// 脚本钩子注册表契约（见 01_分层与依赖.md L0 模块表 <c>hook_registry</c> 行、
    /// 03_运行时骨架.md 第 9 节 <c>HookRegistry</c> 签名）。供游戏层在架构声明的挂载点上
    /// 注册自定义回调（见 01 第 5 节游戏层边界"允许做"第 4 项、第 8 节跨层调用第 3 种
    /// 合法方式"策略注入回调"）。
    /// </summary>
    public interface IHookRegistry
    {
        /// <summary>
        /// 登记一个挂载点及其参数签名（等价于 <c>DeclareHookPoint(new HookPointDefinition(hookId,
        /// signature))</c>，<c>AllowMultiple</c> 取默认值 true）。同一 <paramref name="hookId"/>
        /// 重复声明抛 <see cref="System.InvalidOperationException"/>。
        /// </summary>
        void DeclareHookPoint(Id hookId, string signature);

        /// <summary>登记一个挂载点（完整定义形式，可指定 <see cref="HookPointDefinition.AllowMultiple"/>）。
        /// 同一 <see cref="HookPointDefinition.HookId"/> 重复声明抛 <see cref="System.InvalidOperationException"/>。</summary>
        void DeclareHookPoint(HookPointDefinition definition);

        /// <summary>
        /// 从一组挂载点定义批量声明（供数据注册表从 <c>found.hook</c> 表加载后对接，见
        /// schema/found.hook.md）。逐条调用 <see cref="DeclareHookPoint(HookPointDefinition)"/>，
        /// 重复 id 时同样抛 <see cref="System.InvalidOperationException"/>。
        /// </summary>
        void DeclareFromDefinitions(IEnumerable<HookPointDefinition> definitions);

        /// <summary>
        /// 在 <paramref name="hookId"/> 挂载点注册回调，同一挂载点上多个回调按
        /// <paramref name="order"/> 升序执行，<paramref name="order"/> 相同则按注册先后执行。
        /// <paramref name="hookId"/> 未声明抛 <see cref="System.InvalidOperationException"/>；
        /// 挂载点 <see cref="HookPointDefinition.AllowMultiple"/> 为 false 且已有回调时也抛
        /// <see cref="System.InvalidOperationException"/>。返回的句柄 Dispose 后取消本次注册。
        /// </summary>
        SubscriptionHandle Register(Id hookId, HookCallback callback, int order);

        /// <summary>
        /// 调用 <paramref name="hookId"/> 挂载点上全部已注册回调（按注册时确定的顺序）。
        /// <paramref name="hookId"/> 未声明抛 <see cref="System.InvalidOperationException"/>；
        /// 无回调时空操作。单个回调抛出的异常被捕获并记入诊断，不中断其余回调的执行，
        /// 也不向调用方抛出。
        /// </summary>
        void Invoke(Id hookId, HookArgs args);

        /// <summary>全部已声明的挂载点定义，按声明顺序，只读。</summary>
        IReadOnlyList<HookPointDefinition> HookPoints { get; }

        /// <summary>某挂载点当前已注册（未取消订阅）的回调数量；未声明的挂载点返回 0。</summary>
        int CallbackCount(Id hookId);
    }
}
