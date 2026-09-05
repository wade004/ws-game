#nullable enable
using System.Collections;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class UnityResourceLoaderTests
    {
        private UnityResourceLoader _loader = null!;

        [SetUp]
        public void SetUp()
        {
            _loader = new UnityResourceLoader();
        }

        [Test]
        public void IsLoaded_BeforeAnyLoad_ReturnsFalse()
        {
            Assert.IsFalse(_loader.IsLoaded(new Id("sprite.sample_unknown")));
            Assert.AreEqual(0.0, _loader.GetLoadProgress(new Id("sprite.sample_unknown")));
        }

        [UnityTest]
        public IEnumerator LoadAsync_MissingResource_InvokesCallbackWithFalse()
        {
            var resourceId = new Id("sprite.sample_missing");
            bool? success = null;

            _loader.LoadAsync(resourceId, ResourceKind.Image, (id, ok) => success = ok);

            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
            }

            Assert.IsNotNull(success);
            Assert.IsFalse(success!.Value);
            Assert.IsFalse(_loader.IsLoaded(resourceId));
        }

        [UnityTest]
        public IEnumerator LoadAsync_GetLoadProgress_IsInProgressOrDoneAfterCallback()
        {
            var resourceId = new Id("data.sample_table");
            bool? success = null;

            _loader.LoadAsync(resourceId, ResourceKind.DataTable, (id, ok) => success = ok);

            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= 0.02f;
            }

            Assert.IsNotNull(success);
            // 资源不存在，最终进度应回落到 0（未加载）。
            Assert.AreEqual(0.0, _loader.GetLoadProgress(resourceId));
        }

        [Test]
        public void ResolvePath_StripsCategoryPrefixAndUsesKindSubfolder()
        {
            var path = UnityResourceLoader.ResolvePath(new Id("sprite.creature.sample_wolf"), ResourceKind.Image);

            StringAssert.Contains("sprites", path);
            StringAssert.Contains("creature_sample_wolf.png", path);
        }
    }
}
