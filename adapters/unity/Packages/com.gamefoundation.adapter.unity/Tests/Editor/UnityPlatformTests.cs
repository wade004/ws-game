#nullable enable
using Adapter.Unity.EngineAdapter;
using NUnit.Framework;

namespace Adapter.Unity.Tests.Editor
{
    public sealed class UnityPlatformTests
    {
        private UnityFileSystem _fs = null!;
        private UnityPlatform _platform = null!;

        [SetUp]
        public void SetUp()
        {
            _fs = new UnityFileSystem();
            _platform = new UnityPlatform(_fs);
        }

        [Test]
        public void SetClipboardText_ThenGetClipboardText_RoundTrips()
        {
            _platform.SetClipboardText("hello-gamefoundation");
            Assert.AreEqual("hello-gamefoundation", _platform.GetClipboardText());
        }

        [Test]
        public void ReportCrash_WritesCrashLogFile_Readable()
        {
            _platform.ReportCrash("test-context", "test-details");

            var log = _fs.ReadText("crash_log.txt");

            Assert.IsNotNull(log);
            StringAssert.Contains("test-context", log);
            StringAssert.Contains("test-details", log);
        }

        [Test]
        public void GetPlatformName_ReturnsNonEmptyString()
        {
            Assert.IsNotEmpty(_platform.GetPlatformName());
        }

        [Test]
        public void GetSystemLanguage_ReturnsNonEmptyString()
        {
            Assert.IsNotEmpty(_platform.GetSystemLanguage());
        }
    }
}
