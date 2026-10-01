using Adapters.Stub;
using Xunit;

namespace Tests.StubAdapters
{
    public class StubPlatformTests
    {
        [Fact]
        public void Defaults_PlatformNameStub_LanguageEn_NoClipboard_EmptyCrashLog()
        {
            var platform = new StubPlatform();
            Assert.Equal("stub", platform.GetPlatformName());
            Assert.Equal("en", platform.GetSystemLanguage());
            Assert.Null(platform.GetClipboardText());
            Assert.Empty(platform.CrashLog);
        }

        [Fact]
        public void SetLanguage_ChangesSystemLanguage_LatestWins()
        {
            var platform = new StubPlatform();
            platform.SetLanguage("zh-CN");
            Assert.Equal("zh-CN", platform.GetSystemLanguage());
            platform.SetLanguage("ja");
            Assert.Equal("ja", platform.GetSystemLanguage());
        }

        [Fact]
        public void ReportCrash_AppendsContextAndDetails_InCallOrder_NeverDropsRepeats()
        {
            var platform = new StubPlatform();
            platform.ReportCrash("ctx1", "boom");
            platform.ReportCrash("ctx2", "bang");
            platform.ReportCrash("ctx1", "boom");

            // 条目格式 "<context>: <details>"（桩侧约定，供上层测试按子串断言）；重复上报逐条保留。
            Assert.Equal(new[] { "ctx1: boom", "ctx2: bang", "ctx1: boom" }, platform.CrashLog);
        }

        [Fact]
        public void ReportCrash_EmptyArguments_StillRecordsOneEntry()
        {
            var platform = new StubPlatform();
            platform.ReportCrash(string.Empty, string.Empty);
            Assert.Single(platform.CrashLog);
        }

        [Fact]
        public void Clipboard_RoundTrips_AndLatestWins_AndEmptyStringIsNotNull()
        {
            var platform = new StubPlatform();
            platform.SetClipboardText("first");
            platform.SetClipboardText("second");
            Assert.Equal("second", platform.GetClipboardText());

            platform.SetClipboardText(string.Empty);
            Assert.Equal(string.Empty, platform.GetClipboardText());
        }
    }
}
