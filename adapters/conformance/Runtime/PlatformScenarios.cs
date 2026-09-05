#nullable enable
// PlatformScenarios：IPlatform 契约一致性场景（见 02_引擎适配层.md 第 1.11 节）。可选接口——
// 缺失时应优雅降级，本场景组只覆盖两侧都已实现的部分。
using System.Collections;
using System.Collections.Generic;
using Core.Foundation.EngineAdapter;

namespace Adapters.Conformance
{
    public static class PlatformScenarios
    {
        public static readonly IReadOnlyList<ConformanceScenario<IPlatform>> All = new[]
        {
            new ConformanceScenario<IPlatform>("GetSystemLanguage_返回非空字符串", GetSystemLanguage_ReturnsNonEmptyString),
            new ConformanceScenario<IPlatform>("剪贴板_写入后读回一致", Clipboard_WriteThenRead_RoundTrips),
            new ConformanceScenario<IPlatform>("ReportCrash_不抛异常", ReportCrash_DoesNotThrow),
        };

        private static IEnumerator GetSystemLanguage_ReturnsNonEmptyString(IPlatform platform, IConformanceAssert assert, ConformanceContext ctx)
        {
            var language = platform.GetSystemLanguage();
            assert.NotNull(language, "GetSystemLanguage 不应返回 null");
            assert.True(language.Length > 0, "GetSystemLanguage 不应返回空字符串");

            var platformName = platform.GetPlatformName();
            assert.NotNull(platformName, "GetPlatformName 不应返回 null");
            assert.True(platformName.Length > 0, "GetPlatformName 不应返回空字符串");
            yield break;
        }

        private static IEnumerator Clipboard_WriteThenRead_RoundTrips(IPlatform platform, IConformanceAssert assert, ConformanceContext ctx)
        {
            const string probe = "conformance-clipboard-probe";
            assert.DoesNotThrow(() => platform.SetClipboardText(probe), "SetClipboardText 不应抛异常");
            var readBack = platform.GetClipboardText();
            assert.Equal(probe, readBack, "SetClipboardText 之后 GetClipboardText 应读回同一段文本");
            yield break;
        }

        private static IEnumerator ReportCrash_DoesNotThrow(IPlatform platform, IConformanceAssert assert, ConformanceContext ctx)
        {
            assert.DoesNotThrow(
                () => platform.ReportCrash("conformance-context", "conformance-details"),
                "ReportCrash 不应抛异常（统一的崩溃/严重错误上报入口，见 02 §1.11）");
            yield break;
        }
    }
}
