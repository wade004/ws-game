#nullable enable
using Adapter.Unity.EngineAdapter;
using NUnit.Framework;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class UnityWindowTests
    {
        [Test]
        public void Create_SetsFieldsAndCreatedTrue()
        {
            var window = new UnityWindow();

            window.Create("GameFoundation Test", 800, 600);

            Assert.IsTrue(window.Created);
            Assert.AreEqual("GameFoundation Test", window.Title);
            Assert.AreEqual(800, window.Width);
            Assert.AreEqual(600, window.Height);
        }

        [Test]
        public void SetFullscreen_UpdatesFullscreenFlag()
        {
            var window = new UnityWindow();
            window.Create("t", 640, 480);

            window.SetFullscreen(true);
            Assert.IsTrue(window.Fullscreen);

            window.SetFullscreen(false);
            Assert.IsFalse(window.Fullscreen);
        }

        [Test]
        public void OnCloseRequested_RequestCloseForTest_InvokesCallback()
        {
            var window = new UnityWindow();
            var invoked = false;

            window.OnCloseRequested(() => invoked = true);
            window.RequestCloseForTest();

            Assert.IsTrue(invoked);
        }
    }
}
