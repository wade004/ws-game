namespace Core.Rules.Ai
{
    /// <summary>
    /// <c>ai.behavior_profile.combat_return_policy</c> 的取值（见 06_规则层_属性技能战斗AI.md
    /// 第 6.1 节 <c>return</c> 状态"到达后 → <c>idle</c> 或 <c>patrol</c>"，本任务书拍板补充
    /// 第三档 <see cref="Stay"/>）：
    /// <list type="bullet">
    /// <item><see cref="ReturnToSpawn"/>：脱战后走 <c>return</c> 状态返回出生点，到达后转 <c>idle</c>。</item>
    /// <item><see cref="Patrol"/>：脱战后走 <c>return</c> 状态返回巡逻路径起点，到达后转 <c>patrol</c>
    /// 并从头开始巡逻。</item>
    /// <item><see cref="Stay"/>：判断记录——06 原文只给出 <c>idle</c>/<c>patrol</c> 两个 <c>return</c>
    /// 终点，任务书拍板的枚举多出第三档 <c>stay</c>，字面意为"原地留守"：不经过 <c>return</c> 状态的
    /// 行进过程，脱战瞬间直接在当前位置转 <c>idle</c>（区别于 <see cref="ReturnToSpawn"/> 仍需先走
    /// 回出生点）。见本模块 README"判断记录"一节。</item>
    /// </list>
    /// </summary>
    public enum CombatReturnPolicy
    {
        ReturnToSpawn,
        Stay,
        Patrol,
    }
}
