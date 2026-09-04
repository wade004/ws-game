namespace Core.Foundation.AppLifecycle
{
    /// <summary>
    /// 应用级主状态（见 03_运行时骨架.md 第 1、2 节、01_分层与依赖.md L0 模块表
    /// <c>app_lifecycle</c> 行）。合法转移见 <see cref="AppStateMachineConfig.Default"/>。
    /// </summary>
    public enum AppState
    {
        Boot,
        MainMenu,
        Loading,
        InWorld,
        Pause
    }
}
