#nullable enable
// UnityPlatform：IPlatform 的 Unity 引擎实现。
//
// - GetSystemLanguage：把 Application.systemLanguage 映射为常见的 BCP-47 风格短代码；
//   未覆盖的枚举值一律回退 "en"（可选接口，缺失时优雅降级，见 02 第 1.11 节）。
// - 剪贴板：GUIUtility.systemCopyBuffer 是 Unity 跨桌面平台读写系统剪贴板的公开 API。
// - 崩溃日志：ReportCrash 把 context/details 连同时间戳追加写入用户数据目录下的
//   crash_log.txt（用 UnityFileSystem 的同一份原子写入，追加内容后整体原子重写，避免半行写坏）；
//   另外 UnityEngineHost 在启动时会把 Application.logMessageReceived 里的
//   Exception/Error 级别日志也转发到 ReportCrash（见 UnityEngineHost.HandleUnityLogMessage），
//   本类型本身只负责落盘，不关心日志来源。
using System;
using Core.Foundation.EngineAdapter;
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityPlatform : IPlatform
    {
        private const string CrashLogFileName = "crash_log.txt";

        private readonly UnityFileSystem _fileSystem;

        public UnityPlatform(UnityFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public string GetSystemLanguage()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.ChineseSimplified: return "zh-Hans";
                case SystemLanguage.ChineseTraditional: return "zh-Hant";
                case SystemLanguage.Chinese: return "zh";
                case SystemLanguage.English: return "en";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.German: return "de";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.Russian: return "ru";
                default: return "en";
            }
        }

        public string? GetClipboardText()
        {
            var text = GUIUtility.systemCopyBuffer;
            return string.IsNullOrEmpty(text) ? null : text;
        }

        public void SetClipboardText(string text)
        {
            GUIUtility.systemCopyBuffer = text ?? string.Empty;
        }

        public void ReportCrash(string context, string details)
        {
            var line = $"[{DateTime.UtcNow:O}] {context}: {details}\n";
            var existing = _fileSystem.ReadText(CrashLogFileName) ?? string.Empty;
            _fileSystem.WriteTextAtomic(CrashLogFileName, existing + line);
        }

        public string GetPlatformName() => Application.platform.ToString();
    }
}
