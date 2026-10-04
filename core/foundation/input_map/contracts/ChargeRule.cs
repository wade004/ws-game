using Core.Foundation.Common;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 一个蓄力类动作的输入侧规则（手感设计/01 第 2.2/3.3 节，ADR-0143）：由规则层从技能的 <c>timeline.charge</c> 块换算成 tick 后交给输入缓冲，
    /// 缓冲据此在按住期间自动释放（到上限）与在抬起时判门槛（不足下限）。
    /// </summary>
    public readonly struct ChargeRule
    {
        /// <summary>蓄力下限（tick，0 = 没有下限）：抬起时按住时长不足它即"蓄力不足"。</summary>
        public int MinTicks { get; }

        /// <summary>蓄力上限（tick，0 = 没有上限）：按住满这么久缓冲自动释放（发 <c>input.charge_ready</c>，记录转 <see cref="BufferHoldState.HoldReleased"/>）。</summary>
        public int MaxTicks { get; }

        /// <summary>蓄力不足时的处理：真 = 取消记录（<see cref="BufferDropReason.ChargeBelowMin"/>）；假（缺省）= 按最低档释放（蓄力比例 0，既有行为）。</summary>
        public bool CancelBelowMin { get; }

        public ChargeRule(int minTicks, int maxTicks, bool cancelBelowMin)
        {
            MinTicks = minTicks < 0 ? 0 : minTicks;
            MaxTicks = maxTicks < 0 ? 0 : maxTicks;
            CancelBelowMin = cancelBelowMin;
        }
    }

    /// <summary>
    /// 蓄力规则来源（ADR-0143）：输入缓冲在 L0，不知道输入动作映射到哪个技能、技能有没有蓄力块——这些是规则层与装配层的事，经本接口倒置注入
    /// （生产装配的实现从"输入动作 → 技能"映射与技能的 <c>timeline.charge</c> 块取值）。没有注入时蓄力动作保持既有行为（只在抬起时转换，不自动释放、没有门槛）。
    /// </summary>
    public interface IChargeRuleSource
    {
        /// <summary>行动者按下 <paramref name="actionId"/> 时适用的蓄力规则；该动作此刻不对应蓄力技能返回 false。</summary>
        bool TryGetChargeRule(Id actorId, Id actionId, out ChargeRule rule);
    }
}
