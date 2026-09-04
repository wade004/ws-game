namespace Core.Rules.Common
{
    /// <summary>
    /// AI 行为外壳状态机的状态集合（见 06 第 6.1 节状态图
    /// <c>idle → patrol → chase → combat → return → flee</c>，<c>dead</c> 任意状态可进入）。
    /// 取值与顺序照抄 06 第 6.1 节表格行序。
    /// </summary>
    public enum BehaviorState
    {
        Idle,
        Patrol,
        Chase,
        Combat,
        Return,
        Flee,
        Dead,
    }
}
