namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 语言、剪贴板、崩溃日志（见 02_引擎适配层.md 第 1.11 节）。可选接口——缺失时功能应
    /// 优雅降级（例如无剪贴板能力时禁用相关按钮），不得阻塞核心玩法运行。
    /// ReportCrash 是统一的崩溃/严重错误上报入口，供 L0 等其它层在捕获到不可恢复错误时调用。
    /// </summary>
    public interface IPlatform
    {
        string GetSystemLanguage();

        string? GetClipboardText();

        void SetClipboardText(string text);

        void ReportCrash(string context, string details);

        string GetPlatformName();
    }
}
