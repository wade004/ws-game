namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 输入动作的类别（手感设计/01 第 2.1 节 <c>found.input_action.class</c>）：取消窗口、优先级、连招都按类别工作。
    /// 数值顺序是登记顺序，ABI 只加不改。
    /// </summary>
    public enum ActionClass
    {
        /// <summary>移动（轴类动作，不入缓冲）。</summary>
        Move,

        /// <summary>攻击。</summary>
        Attack,

        /// <summary>技能。</summary>
        Skill,

        /// <summary>闪避。</summary>
        Dodge,

        /// <summary>交互。</summary>
        Interact,

        /// <summary>物品使用。</summary>
        Item,

        /// <summary>菜单。</summary>
        Menu,
    }
}
