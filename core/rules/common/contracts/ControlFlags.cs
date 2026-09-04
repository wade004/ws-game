using System;

namespace Core.Rules.Common
{
    /// <summary>
    /// 控制类光环施加的行为禁止标志位组合（见 06 第 3.3 节 <c>control</c> 行"施加控制状态（禁止移动/
    /// 禁止施法/禁止普通攻击等标志位组合）"）。<c>[Flags]</c> 位组合，供 <see cref="IAuraQuery.GetControlFlags"/>
    /// 合并某单位身上全部生效光环的控制标志。<see cref="NoInteract"/>（禁止与 GameObject/NPC 交互）是
    /// 06 原文"等标志位组合"暗示但未逐一列出的补充项，用于覆盖"禁止交互"这一常见控制语义。
    /// </summary>
    [Flags]
    public enum ControlFlags
    {
        None = 0,
        NoMove = 1 << 0,
        NoCast = 1 << 1,
        NoAttack = 1 << 2,

        /// <summary>补充：禁止交互（开锁、拾取、对话等），06 原文"等标志位组合"未逐一列出的常见控制语义。</summary>
        NoInteract = 1 << 3,
    }
}
