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
    }
}
