namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 同一动作在缓冲未过期时再次按下的处理（手感设计/01 第 2.1 节 <c>found.input_action.repeat_policy</c>）。
    /// 数值顺序是登记顺序，ABI 只加不改。
    /// </summary>
    public enum InputRepeatPolicy
    {
        /// <summary>刷新：不占新槽，把已有记录的过期时刻重置为"现在 + 缓冲窗口"（缺省）。</summary>
        Refresh,

        /// <summary>忽略：保持已有记录不变，本次按下不产生任何效果。</summary>
        Ignore,
    }
}
