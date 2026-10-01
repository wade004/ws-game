namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 动作类别的缺省手感属性（手感设计/01 第 2.1 节：<c>priority</c> 按类别缺省 dodge &gt; attack = skill &gt; item &gt;
    /// interact &gt; menu；<c>face_on_accept</c> attack/skill/dodge 缺省为真）。纯函数、无状态。
    /// </summary>
    public static class ActionClassDefaults
    {
        /// <summary>类别缺省优先级（越大越优先）：dodge 40、attack = skill 30、item 20、interact 10、menu 0；move 不入缓冲，取 0。</summary>
        public static int Priority(ActionClass actionClass)
        {
            switch (actionClass)
            {
                case ActionClass.Dodge: return 40;
                case ActionClass.Attack: return 30;
                case ActionClass.Skill: return 30;
                case ActionClass.Item: return 20;
                case ActionClass.Interact: return 10;
                default: return 0;
            }
        }

        /// <summary>类别缺省的"接受时朝向对齐到按下瞬间移动轴方向"：attack/skill/dodge 为真，其余为假。</summary>
        public static bool FaceOnAccept(ActionClass actionClass)
        {
            return actionClass == ActionClass.Attack || actionClass == ActionClass.Skill || actionClass == ActionClass.Dodge;
        }

        /// <summary>该类别的动作是否经输入缓冲：除 <see cref="ActionClass.Move"/>（轴类、连续量每 tick 直接采样）外都经缓冲。</summary>
        public static bool IsBuffered(ActionClass actionClass)
        {
            return actionClass != ActionClass.Move;
        }
    }
}
