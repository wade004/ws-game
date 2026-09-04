namespace Core.Rules.Ai
{
    /// <summary>
    /// <c>ai.patrol_path.mode</c> 的取值（见 06_规则层_属性技能战斗AI.md 第 6.3 节"结构固定为
    /// ……有序 Vec2 列表 + <c>loop</c>|<c>pingpong</c> 模式"）：<see cref="Loop"/> 到达终点后跳回
    /// 起点继续；<see cref="PingPong"/> 到达端点后折返方向。
    /// </summary>
    public enum PatrolMode
    {
        Loop,
        PingPong,
    }
}
