using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 输入映射契约（见 01_分层与依赖.md L0 模块表 <c>input_map</c> 行、03_运行时骨架.md
    /// 第 7 节、第 9 节 <c>InputMapHost</c> 签名）：绑定解析、重绑定、冲突检测、存储、
    /// 动作状态查询。<c>declareActionSet</c>/<c>rebind</c>/<c>getConflicts</c>/
    /// <c>isActionActive</c>/<c>getActionAxis</c> 五个方法与 03 第 9 节签名一一对应；
    /// <see cref="Update"/>/<see cref="ResetBindings"/>/<see cref="ExportBindings"/>/
    /// <see cref="ImportBindings"/>/<see cref="GetBindings"/> 是任务书显式拍板的补充方法
    /// （03 第 9 节未给出"谁来推进动作状态""绑定如何落盘"的具体方法签名，只在文字里说明
    /// "存储（重绑定结果持久化到设置）"，见本模块 README 判断记录）。
    /// </summary>
    public interface IInputMapHost
    {
        /// <summary>声明一个动作集；重复声明同一 <paramref name="actionSetId"/> 抛
        /// <see cref="System.InvalidOperationException"/>。</summary>
        void DeclareActionSet(Id actionSetId, IReadOnlyList<ActionDefinition> actions);

        /// <summary>
        /// 把 <paramref name="actionName"/>（<c>ActionDefinition.ActionId.Value</c>）的绑定
        /// 替换为单一新绑定 <paramref name="newBinding"/>。<paramref name="newBinding"/> 格式非法
        /// 直接抛 <see cref="System.ArgumentException"/>（见 <see cref="BindingParser.Parse"/>）；
        /// <paramref name="actionName"/> 未声明抛 <see cref="System.InvalidOperationException"/>；
        /// 若同一 <see cref="ActionDefinition.RebindGroup"/> 内已有其它动作占用该绑定，
        /// 发出 <see cref="InputRebindConflictEvent"/> 并返回 false，不做替换；否则替换并返回 true。
        /// </summary>
        bool Rebind(string actionName, string newBinding);

        /// <summary>返回当前占用 <paramref name="binding"/>（跨全部重绑分组）的动作名列表。</summary>
        IReadOnlyList<string> GetConflicts(string binding);

        /// <summary>动作当前是否激活。仅适用于 <see cref="ActionKind.Button"/> 动作，其余
        /// 类型抛 <see cref="System.InvalidOperationException"/>（见 <see cref="GetActionAxis"/>）。</summary>
        bool IsActionActive(string actionName);

        /// <summary>动作当前轴值。仅适用于 <see cref="ActionKind.Axis1D"/>（值放 X，Y 恒为 0）/
        /// <see cref="ActionKind.Axis2D"/> 动作，<see cref="ActionKind.Button"/> 抛
        /// <see cref="System.InvalidOperationException"/>。</summary>
        Vec2 GetActionAxis(string actionName);

        /// <summary>
        /// 每帧/每 tick 调用一次：拉取 <paramref name="input"/>.PollEvents() 更新按键/轴状态、
        /// 重算全部已声明动作的状态；按下型动作出现"按下沿"（本次激活、上次未激活）时
        /// <see cref="IEventBus.Enqueue"/> 一条 <see cref="InputActionTriggeredEvent"/>。
        /// </summary>
        void Update(IInput input);

        /// <summary>把 <paramref name="actionName"/> 的当前绑定恢复为声明时的
        /// <see cref="ActionDefinition.DefaultBindings"/>。</summary>
        void ResetBindings(string actionName);

        /// <summary>导出全部"被改过"（当前绑定与声明时的默认绑定不同）的动作绑定，
        /// 供设置文件持久化（见 03 第 7 节"存储"、10_存档与持久化.md 第 7 节"按键绑定属于
        /// 设置文件，与存档分离"）。形状：<c>{ "&lt;actionName&gt;": ["&lt;binding&gt;", ...] }</c>。</summary>
        JsonObject ExportBindings();

        /// <summary>从设置文件读回的绑定覆盖对应动作的当前绑定（不做冲突检测——设置文件
        /// 视为此前已通过检测才落盘的可信状态；<paramref name="bindings"/> 里出现未声明的
        /// 动作名，或绑定字符串格式非法，均抛异常）。</summary>
        void ImportBindings(JsonObject bindings);

        /// <summary>查询 <paramref name="actionName"/> 当前生效的绑定列表（只读快照）。</summary>
        IReadOnlyList<string> GetBindings(string actionName);
    }
}
