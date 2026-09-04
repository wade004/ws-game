namespace Core.Rules.Common
{
    /// <summary>
    /// 命中判定结果（见 06 第 4.2 节命中表六项 + <see cref="Hit"/> 普通命中）。取值顺序照抄命中表
    /// 行序（miss/dodge/parry/glancing_blow/block/crit），额外补 <see cref="Hit"/> 表示"命中且未触发
    /// 以上任何特殊分支"这一最常见结果（06 命中表本身隐含这一默认分支，未显式命名）。
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
    }
}
