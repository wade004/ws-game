#nullable enable
using System.Collections;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class UnityResourceLoaderTests : PlayModeTestBase
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

        [Test]
        public void ResolvePath_SceneAndNavMesh_UseDedicatedSubfolders()
        {
            // ADR-0016 决策 5：Scene/NavMesh 不再借用 DataTable 的 "data/" 子目录。
            var scenePath = UnityResourceLoader.ResolvePath(new Id("scene.sample_field"), ResourceKind.Scene);
            var navPath = UnityResourceLoader.ResolvePath(new Id("nav.sample_field"), ResourceKind.NavMesh);

            StringAssert.Contains("scene", scenePath);
            StringAssert.EndsWith("sample_field.json", scenePath);
            StringAssert.Contains("nav_mesh", navPath);
            StringAssert.EndsWith("sample_field.json", navPath);
        }

        [Test]
        public void ResolveEffectDir_StripsCategoryPrefix_UsesVfxSubfolder()
        {
            var dir = UnityResourceLoader.ResolveEffectDir(new Id("vfx.hit_spark"));

            StringAssert.Contains("vfx", dir);
            StringAssert.EndsWith(System.IO.Path.Combine("vfx", "hit_spark"), dir);
        }

        /// <summary>ADR-0038 适配层接线（2026-09-19）：sprite 型动画剪辑改用 sprite_anim 前缀后，
        /// ResolveEffectDir 必须按类别前缀分派到独立的 sprite_anim 子目录，不能像此前那样固定落进
        /// vfx 子目录（见该方法判断记录）。</summary>
        [Test]
        public void ResolveEffectDir_SpriteAnimCategory_UsesSpriteAnimSubfolder()
        {
            var dir = UnityResourceLoader.ResolveEffectDir(new Id("sprite_anim.sample_hero_idle"));

            StringAssert.Contains("sprite_anim", dir);
            StringAssert.DoesNotContain(
                System.IO.Path.Combine("vfx", "sample_hero_idle"), dir,
                "sprite_anim 类别不应落进 vfx 子目录（此前固定拼接的遗留错误行为）");
            StringAssert.EndsWith(System.IO.Path.Combine("sprite_anim", "sample_hero_idle"), dir);
        }

        /// <summary>一致性对照测试（ADR-0038 适配层接线，见 AGENTS.md"一致性守护"惯例）：
        /// UnityResourceLoader.ResolveEffectDir 现直接转发 AssetRefConventions.ResolvePathSpace，
        /// 本用例锚定"适配层产出的目录 = RootDir + 契约面产出的相对路径"这一关系本身，防止未来有人
        /// 在其中一侧改动分派规则却忘了另一侧（虽然当前是同一次调用，结构上不会漂移，但作为回归锚点
        /// 保留，同 core/foundation/engine_adapter/tests/AssetRefConventionsTests.cs 的对照测试
        /// 惯例）。</summary>
        [Test]
        public void ResolveEffectDir_MatchesAssetRefConventionsResolvePathSpace_ForVfxAndSpriteAnim()
        {
            foreach (var resourceRef in new[] { "vfx.hit_spark", "sprite_anim.sample_hero_idle" })
            {
                var id = new Id(resourceRef);
                var (_, expectedRelative) = Core.Foundation.EngineAdapter.AssetRefConventions.ResolvePathSpace(id);
                var dir = UnityResourceLoader.ResolveEffectDir(id);

                StringAssert.EndsWith(
                    expectedRelative.Replace('/', System.IO.Path.DirectorySeparatorChar), dir,
                    $"ResolveEffectDir(\"{resourceRef}\") 应以 AssetRefConventions.ResolvePathSpace 给出的相对路径结尾");
            }
        }

        /// <summary>ADR-0038 适配层接线（2026-09-19）：display.equip_visual.mesh_ref 的 sprite 型取值
        /// 改用 paperdoll 前缀后，ResolvePath 必须转发 AssetRefConventions.PaperdollLayerFile 解析为
        /// 单个扁平文件，不能像此前那样落进"sprites/&lt;name&gt;.png"的通用回退分支（该分支此后只
        /// 覆盖 sprite 类别本身，见该方法判断记录）。</summary>
        [Test]
        public void ResolvePath_PaperdollCategory_ResolvesFlatPngUnderPaperdollSubfolder()
        {
            var path = UnityResourceLoader.ResolvePath(new Id("paperdoll.item.sample_hero_hat_test"), ResourceKind.Image);

            StringAssert.Contains("paperdoll", path);
            StringAssert.EndsWith(
                System.IO.Path.Combine("paperdoll", "item_sample_hero_hat_test.png"), path);
        }

        /// <summary>ADR-0038 适配层接线：ResolveModelResourcesPath/ResolveAnimClipResourcesPath 现直接
        /// 转发 AssetRefConventions 的对应公开方法（见类型顶部"ADR-0038 适配层接线"判断记录），本用例
        /// 锚定二者字符串结果始终一致，防止未来重新分叉出第二份拼接实现。</summary>
        [Test]
        public void ResolveModelResourcesPathAndResolveAnimClipResourcesPath_MatchAssetRefConventions()
        {
            var modelId = new Id("model.placeholder_biped");
            Assert.AreEqual(
                Core.Foundation.EngineAdapter.AssetRefConventions.ModelLogicalPath(modelId),
                UnityResourceLoader.ResolveModelResourcesPath(modelId));

            var animId = new Id("anim.attack");
            Assert.AreEqual(
                Core.Foundation.EngineAdapter.AssetRefConventions.AnimClipLogicalPath(animId),
                UnityResourceLoader.ResolveAnimClipResourcesPath(animId));
        }

        [UnityTest]
        public IEnumerator LoadAsync_MissingSceneResource_InvokesCallbackWithFalse()
        {
            var resourceId = new Id("scene.sample_missing");
            bool? success = null;

            _loader.LoadAsync(resourceId, ResourceKind.Scene, (id, ok) => success = ok);

            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= 0.02f;
            }

            Assert.IsNotNull(success);
            Assert.IsFalse(success!.Value);
        }

        [UnityTest]
        public IEnumerator LoadAsync_MissingEffectResource_InvokesCallbackWithFalse()
        {
            var resourceId = new Id("vfx.sample_missing_effect");
            bool? success = null;

            _loader.LoadAsync(resourceId, ResourceKind.Effect, (id, ok) => success = ok);

            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= 0.02f;
            }

            Assert.IsNotNull(success);
            Assert.IsFalse(success!.Value);
            Assert.IsFalse(_loader.TryGetEffect(resourceId, out _));
        }

        /// <summary>依赖 build.ps1 -SyncContent 已经把 assets/_placeholder/vfx/ 同步进
        /// StreamingAssets/GameFoundation/vfx/（见包 README"内容同步"一节），同
        /// LoadAsync_PlaceholderHeroFrontBody_LoadsWithExpectedSize 判断记录。</summary>
        [UnityTest]
        public IEnumerator LoadAsync_PlaceholderHitSparkEffect_LoadsFramesSuccessfully()
        {
            var resourceId = new Id("vfx.hit_spark");
            bool? success = null;

            _loader.LoadAsync(resourceId, ResourceKind.Effect, (id, ok) => success = ok);

            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
            }

            Assert.IsNotNull(success, "加载在超时前应当有结果（成功或失败），不应当悬而不决");
            Assert.IsTrue(success!.Value,
                "占位 hit_spark 特效资源加载应当成功——若失败，请先跑一次 build.ps1 -SyncContent 同步占位资产");
            Assert.IsTrue(_loader.TryGetEffect(resourceId, out var asset));
            Assert.AreEqual(8, asset.Frames.Length, "assets/_placeholder/vfx/hit_spark/frames.json 声明了 8 帧");
            Assert.IsFalse(asset.Loop, "assets/_placeholder/vfx/hit_spark/frames.json 的 loop 为 false");
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
