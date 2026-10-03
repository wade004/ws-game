namespace Core.Foundation.InputMap
{
    /// <summary>
    /// <see cref="IInputMapHost"/> 默认实现的构造期策略配置。
    /// </summary>
    public sealed class InputMapOptions
    {
        /// <summary>
        /// <c>pad:</c>/<c>pad_axis:</c>/<c>pad_stick:</c> 绑定使用的手柄索引。判断记录：
        /// 本模块绑定字符串小语法（见 README）未在语法里编码手柄索引（03/04 均未提及多手柄
        /// 分配规则），按单本地玩家场景简化为"整份动作集固定使用同一个手柄索引"，默认 0；
        /// 多手柄/多玩家分配留待后续按需扩展绑定语法（如 <c>pad0:</c>/<c>pad1:</c>）。
        /// </summary>
        public int GamepadIndex { get; set; }

        /// <summary>
        /// 相机朝向查询（可选，<c>null</c> 缺省）：声明了 <c>camera_relative</c> 控制空间的轴动作（见 <see cref="InputControlSpace"/>）每次
        /// <see cref="IInputMapHost.Update"/> 时据此取偏航，把轴值换算成世界方向。缺省 <c>null</c> 时输入映射的行为与此前逐位一致；
        /// 在 <c>null</c> 的情况下声明 <c>camera_relative</c> 动作，<see cref="IInputMapHost.DeclareActionSet"/> 抛
        /// <see cref="System.InvalidOperationException"/>（不静默当偏航 0）。
        /// </summary>
        public Core.Foundation.EngineAdapter.ICameraOrientation? CameraOrientation { get; set; }
    }
}
