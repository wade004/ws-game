namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 动作类型（见 01_分层与依赖.md L0 模块表 <c>input_map</c> 行、03_运行时骨架.md 第 7 节
    /// 示例动作集"类型"列：按下 / 轴（一维）/ 轴（二维）三种）。对应 <c>found.input_action</c>
    /// 表字段 <c>kind</c> 的枚举取值 <c>button</c>/<c>axis1d</c>/<c>axis2d</c>
    /// （见 <see cref="InputActionSchema"/>）。
    /// </summary>
    public enum ActionKind
    {
        /// <summary>按下型动作：只有"是否激活"这一维状态，见 <see cref="IInputMapHost.IsActionActive"/>。</summary>
        Button,

        /// <summary>一维轴动作（如镜头缩放）：值放在 <see cref="Common.Vec2.X"/>，
        /// <see cref="Common.Vec2.Y"/> 恒为 0（见 <c>IInputMapHost.GetActionAxis</c> 判断记录：
        /// 03 第 9 节签名统一用 Vec2 承载轴值，未区分一维/二维，本模块按此约定复用同一返回类型）。</summary>
        Axis1D,

        /// <summary>二维轴动作（如平面移动方向意图），见 <see cref="IInputMapHost.GetActionAxis"/>。</summary>
        Axis2D,
    }
}
