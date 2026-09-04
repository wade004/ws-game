namespace Core.Numbers.Faction
{
    /// <summary>阵营关系的有限枚举（见 00_架构总则.md 第 3 节"缩到小矩阵（敌对/中立/友好等
    /// 有限枚举）"）。取值与 <c>fac.faction.default_reaction</c>/<c>fac.reaction_matrix.reaction</c>
    /// 的字符串枚举值一一对应：<c>hostile</c>/<c>neutral</c>/<c>friendly</c>。</summary>
    public enum Reaction
    {
        Hostile,
        Neutral,
        Friendly,
    }
}
