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

        [Test]
        public void ResolvePath_LayerCategory_ResolvesNestedDirectionLayerPath()
        {
            var path = UnityResourceLoader.ResolvePath(new Id("layer.placeholder_hero__front__body"), ResourceKind.Image);

            var expectedSuffix = System.IO.Path.Combine("sprites", "placeholder_hero", "front", "body.png");
            StringAssert.EndsWith(expectedSuffix, path);
        }

        /// <summary>U2-1 验收要求（任务书原句）："加载占位英雄 front/body 成功、宽高 64×96"。依赖
        /// build.ps1 -SyncContent 已经把 assets/_placeholder/sprites/ 同步进
        /// StreamingAssets/GameFoundation/sprites/（见包 README"内容同步"一节）；命令行跑测试前
        /// 需要先跑过一次同步，测试本身不负责触发同步。</summary>
        [UnityTest]
        public IEnumerator LoadAsync_PlaceholderHeroFrontBody_LoadsWithExpectedSize()
        {
            var resourceId = new Id("layer.placeholder_hero__front__body");
            bool? success = null;

            _loader.LoadAsync(resourceId, ResourceKind.Image, (id, ok) => success = ok);

            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
            }

            Assert.IsNotNull(success, "加载在超时前应当有结果（成功或失败），不应当悬而不决");
            Assert.IsTrue(success!.Value,
                "占位英雄 front/body 层资源加载应当成功——若失败，请先跑一次 build.ps1 -SyncContent 同步占位资产");
            Assert.IsTrue(_loader.TryGetSprite(resourceId, out var sprite));
            Assert.AreEqual(64, sprite.rect.width, 0.01f, "占位英雄精灵宽度应为 64 像素（见 assets/_placeholder/MANIFEST.json）");
            Assert.AreEqual(96, sprite.rect.height, 0.01f, "占位英雄精灵高度应为 96 像素（见 assets/_placeholder/MANIFEST.json）");
        }
    }
}
