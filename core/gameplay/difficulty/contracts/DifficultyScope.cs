namespace Core.Gameplay.Difficulty
{
    /// <summary>
    /// 难度档位生效的作用域（见 08_玩法层_掉落任务对话关卡.md 第 5 节、01_分层与依赖.md L4
    /// 模块表 <c>difficulty</c> 行）：<see cref="Global"/> 对整局生效；<see cref="Map"/> 只对
    /// 指定地图生效（供多地图分别选择难度的场景，如"本层地图选残酷模式，其它层不受影响"）。
    /// </summary>
    public enum DifficultyScope
    {
        Global,
        Map,
    }
}
