namespace Core.Rules.Common
{
    /// <summary>
    /// 命中判定结果（见 06 第 4.2 节命中表六项 + <see cref="Hit"/> 普通命中）。取值顺序照抄命中表
    /// 行序（miss/dodge/parry/glancing_blow/block/crit），额外补 <see cref="Hit"/> 表示"命中且未触发
    /// 以上任何特殊分支"这一最常见结果（06 命中表本身隐含这一默认分支，未显式命名）。
    /// <para>
    /// ADR-0098（消费方第四十三批反馈2，阻塞）新增 <see cref="Immune"/>：
    /// <see cref="Core.Rules.Combat.Resolver"/> 步骤 7"免疫吸收"判定为免疫时报告的结果，与命中表
    /// 六分支（步骤 1"判定"产生）是两个不同管线阶段各自的"回避类"结局，此前只有 miss/dodge/parry
    /// 三项通过 <see cref="Core.Rules.Combat.Resolver.Resolve"/> 终止分支发布
    /// <c>combat.attack_avoided</c>，immune 判定完全不落地、不发任何事件——消费方需要用统一事件驱动
    /// "未命中/闪避/招架/免疫"四类回避反馈（飘字"Miss"/"Immune"、专属音效），框架此前没有给 immune
    /// 提供任何可订阅的信号。追加到枚举末尾（ABI 只加法，不改变既有成员的数值），不影响任何按
    /// 位置/数值比较 <see cref="HitResult"/> 的既有代码（全仓核实：既有代码只按具名成员比较）。</para>
    /// </summary>
    public enum HitResult
    {
        Miss,
        Dodge,
        Parry,
        GlancingBlow,
        Block,
        Hit,
        Crit,

        /// <summary>见本类型上方 ADR-0098 判断记录：结算管线步骤 7"免疫吸收"判定为免疫时的结果，
        /// 只在 <see cref="Core.Rules.Combat.Resolver.Resolve"/> 内部产生（<see cref="ResolveResult.Immune"/>
        /// 的既有布尔字段与本枚举值语义等价，互为佐证，不是新引入的判定条件），不会作为步骤 1
        /// "判定"的六分支结果出现在 <see cref="Core.Rules.Combat.HitTableConfig"/> 里。</summary>
        Immune,
    }
}
