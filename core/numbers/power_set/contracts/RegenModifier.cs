using System;

namespace Core.Numbers.PowerSet
{
    /// <summary>
    /// ADR-0108（消费方反馈第五十七批"静息回复"）新增：<see cref="RegenModifier"/> 生效的战斗状态
    /// 范围——决定它是否参与某一次 <see cref="IPowerHost.Advance"/> 推进时的有效回复速率计算
    /// （见 <see cref="IPowerHost.AddRegenModifier"/> 判断记录）。
    /// </summary>
    public enum RegenScope
    {
        /// <summary>只在脱战时生效。</summary>
        OutOfCombat,

        /// <summary>只在战斗内生效。</summary>
        InCombat,

        /// <summary>战斗内/脱战均生效。</summary>
        Both,
    }

    /// <summary>
    /// ADR-0108 新增：单位级、按资源类型的运行期回复速率修饰器（不可变值类型）——见
    /// <see cref="IPowerHost.AddRegenModifier"/> 判断记录"资源池层通用能力"。同一 <c>key</c> 下
    /// 只保留最新一次 <see cref="IPowerHost.AddRegenModifier"/> 传入的值（替换而非叠加），多个不同
    /// <c>key</c> 的修饰器按 <see cref="Core.Numbers.PowerSet.PowerHost"/>"有效速率"公式（判断记录
    /// 见该类型 <c>ComputeEffectiveRegenRate</c>）组合。
    /// </summary>
    public readonly struct RegenModifier
    {
        /// <summary>生效的战斗状态范围。</summary>
        public RegenScope Scope { get; }

        /// <summary>乘算系数，作用于"定义速率 + 全部匹配 Add 之和"这一中间结果；默认 1（不改变倍率）。
        /// 必须 <c>&gt;= 0</c>（构造期校验，负倍率没有"回复速率"这一物理量的合理解释，见判断记录）。</summary>
        public double Multiplier { get; }

        /// <summary>加算增量，作用于定义速率本身；默认 0，可为负（用于"某状态下回复变慢"这类内容）。</summary>
        public double Add { get; }

        public RegenModifier(RegenScope scope, double multiplier = 1.0, double add = 0.0)
        {
            if (multiplier < 0)
            {
                throw new ArgumentException($"multiplier 不能为负数（{multiplier}）——负倍率没有合理的回复速率语义", nameof(multiplier));
            }

            Scope = scope;
            Multiplier = multiplier;
            Add = add;
        }
    }
}
