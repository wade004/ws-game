#nullable enable
// TextureMipChainTests：ADR-0096（运行时解码贴图带多级渐远链）收口验收——消费方反馈第四十二批，
// 阻塞。UnityResourceLoader 此前三条解码路径（DecodeMapLayerSprite/TryDecodeImage/TryDecodeEffect）
// 统一固定 `new Texture2D(2, 2, TextureFormat.RGBA32, false)`（不生成 mip 链），源资产密度高于屏幕
// 实际显示密度时（4K 分辨率下角色按 0.13～0.69 倍缩小等常见场景）欠采样锯齿明显、移动/缩放时贴图
// 闪烁。修复后默认对三条路径开启 mip 链（UnityResourceLoader.TextureSampling），过滤模式默认三线性，
// 地图分层图额外声明各向异性等级；逐帧动画开启 mip 链时每帧改为切成独立纹理（避免同一图集不同帧
// 之间"渗色"进对方的低级 mip）。
//
// 判断记录（复用现有夹具做法，不重新搭一遍装配）：静态图像用真实占位精灵集
// assets/_placeholder/sprites/placeholder_beast/front/body.png（与
// UnityResourceLoaderTests.LoadAsync_PlaceholderBeastFrontBody_PivotComesFromAnchorsRoot 同一份真实
// 资源，不依赖 RootDirOverrideForTests，验证的是"默认选项 + 真实资产管线同步出的资源"这条最贴近
// 生产的路径）；逐帧动画本仓库当前没有真实的 assets/_placeholder/sprite_anim/ 占位资源（已核实为
// 空），改用 UnityResourceLoaderTests.cs 里 ADR-0095 用例的既有夹具写法（RootDirOverrideForTests +
// WritePngFixture/WriteTextFixture，逐字复制 placeholder_beast 的 anchors.json 声明
// root=[24,44]/canvas 48x48/pixels_per_unit=32，只换一个不冲突的精灵集名），既验证了 mip 链/独立
// 纹理，又顺带证明切帧没有破坏 ADR-0095 已收口的枢轴/像素密度换算；地图分层图直接复用
// MapLayerHostPlayModeTests.cs 已验证过的真实样例地图 world.sample_field
// （assets/_sample/maps/sample_field/{ground,overlay}.png），不经完整生产装配，直接调用
// UnityResourceLoader.LoadAsync(ResourceKind.MapLayers) ——本文件验证的是加载器自身的采样参数，不是
// MapLayerHost 的调度逻辑（那部分已被 MapLayerHostPlayModeTests.cs 覆盖）。
using System.Collections;
using System.IO;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class TextureMipChainTests : PlayModeTestBase
    {
        private UnityResourceLoader _loader = null!;
        private string? _tempFixtureRoot;

        [SetUp]
        public void SetUp()
        {
            _loader = new UnityResourceLoader();
        }

        [TearDown]
        public void TearDown()
        {
            UnityResourceLoader.RootDirOverrideForTests = null;
            if (_tempFixtureRoot != null && Directory.Exists(_tempFixtureRoot))
            {
                Directory.Delete(_tempFixtureRoot, true);
            }
            _tempFixtureRoot = null;
        }

        private static IEnumerator LoadAndWait(UnityResourceLoader loader, Id resourceId, ResourceKind kind, System.Action<bool> onDone)
        {
            bool? success = null;
            loader.LoadAsync(resourceId, kind, (id, ok) => success = ok);
            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                loader.Tick();
                yield return null;
                timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
            }
            Assert.IsNotNull(success, $"资源 \"{resourceId.Value}\" 加载在超时前应当有结果，不应当悬而不决");
            onDone(success!.Value);
        }

        private static IEnumerator LoadEffectAndWait(UnityResourceLoader loader, Id resourceId, Id? spriteSetId, System.Action<bool> onDone)
        {
            bool? success = null;
            loader.LoadAsync(resourceId, ResourceKind.Effect, new ResourceLoadHints(spriteSetId), (id, ok) => success = ok);
            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                loader.Tick();
                yield return null;
                timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
            }
            Assert.IsNotNull(success, $"资源 \"{resourceId.Value}\" 加载在超时前应当有结果，不应当悬而不决");
            onDone(success!.Value);
        }

        /// <summary>同 UnityResourceLoaderTests.cs 既有惯例：写一张纯色 PNG 夹具。</summary>
        private static void WritePngFixture(string path, int width, int height)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(texture));
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static void WriteTextFixture(string path, string content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, content);
        }

        /// <summary>复现用例（修复前应失败）：默认选项下，①真实占位静态图应当生成 mip 链、过滤模式
        /// 三线性；②携带精灵集提示的逐帧动画每帧应当各自生成 mip 链、切成独立纹理（帧宽即纹理宽度，
        /// 不是整张图集宽度），且切帧没有破坏 ADR-0095 已收口的按精灵集脚底锚点换算枢轴/像素密度这
        /// 条既有行为。修复前（`new Texture2D(2,2,RGBA32,false)` 固定不生成 mip）两处
        /// mipmapCount 均恒为 1，本用例必然红。</summary>
        [UnityTest]
        public IEnumerator LoadAsync_DefaultOptions_GeneratesMipChainForImageAndEffectFrames_PreservingAdr0095Pivot()
        {
            // ① 真实占位静态图（与 UnityResourceLoaderTests 同一份真实资源，不覆盖 RootDir）。
            var imageId = new Id("layer.placeholder_beast__front__body");
            Sprite imageSprite = null!;
            yield return LoadAndWait(_loader, imageId, ResourceKind.Image, ok =>
            {
                Assert.IsTrue(ok, "占位野兽 front/body 层资源加载应当成功——若失败，请先跑一次 build.ps1 -SyncOnly 同步占位资产");
                Assert.IsTrue(_loader.TryGetSprite(imageId, out imageSprite));
            });

            Assert.Greater(imageSprite.texture.mipmapCount, 1,
                "ADR-0096 决策 1：默认选项下静态图像解码应当生成 mip 链（mipmapCount > 1），修复前固定不生成、恒为 1");
            Assert.AreEqual(FilterMode.Trilinear, imageSprite.texture.filterMode,
                "ADR-0096 决策 1：默认过滤模式应为三线性");

            // ② 逐帧动画：本仓库当前没有真实的 assets/_placeholder/sprite_anim/ 占位资源，改用
            // ADR-0095 既有用例同款夹具写法（逐字复制 placeholder_beast 的 anchors.json 声明）。
            const double rootX = 24d;
            const double rootY = 44d;
            const int frameSize = 48;
            const float declaredPixelsPerUnit = 32f;

            _tempFixtureRoot = Path.Combine(Application.temporaryCachePath, "adr0096_fixture_" + System.Guid.NewGuid().ToString("N"));
            WriteTextFixture(
                Path.Combine(_tempFixtureRoot, "sprites", "fixture_beast_0096", "anchors.json"),
                "{\"canvas\": {\"width\": 48, \"height\": 48}, \"pixels_per_unit\": 32, " +
                "\"directions\": {\"front\": {\"root\": [24, 44]}}}");
            WritePngFixture(Path.Combine(_tempFixtureRoot, "sprite_anim", "fixture_beast_0096_walk", "atlas.png"), frameSize * 2, frameSize);
            WriteTextFixture(
                Path.Combine(_tempFixtureRoot, "sprite_anim", "fixture_beast_0096_walk", "frames.json"),
                "{\"frames\": [{\"x\": 0, \"y\": 0, \"w\": 48, \"h\": 48}, {\"x\": 48, \"y\": 0, \"w\": 48, \"h\": 48}]}");
            UnityResourceLoader.RootDirOverrideForTests = _tempFixtureRoot;

            var spriteSetId = new Id("sprite.fixture_beast_0096");
            var effectId = new Id("sprite_anim.fixture_beast_0096_walk");
            UnityResourceLoader.EffectAsset asset = null!;
            yield return LoadEffectAndWait(_loader, effectId, spriteSetId, ok =>
            {
                Assert.IsTrue(ok, "带精灵集提示的逐帧动画夹具加载应当成功");
                Assert.IsTrue(_loader.TryGetEffect(effectId, out asset));
            });

            Assert.AreEqual(2, asset.Frames.Length, "frames.json 声明了 2 帧");

            var expectedPivotX = (float)(rootX / frameSize);
            var expectedPivotY = 1f - (float)(rootY / frameSize);

            for (var i = 0; i < asset.Frames.Length; i++)
            {
                var texture = asset.Frames[i].Sprite.texture;
                Assert.Greater(texture.mipmapCount, 1,
                    $"ADR-0096 决策 3：第 {i} 帧应当生成 mip 链，修复前固定不生成、恒为 1");
                Assert.AreEqual(frameSize, texture.width,
                    $"ADR-0096 决策 3：第 {i} 帧应当已切成独立纹理，纹理宽度应等于单帧宽度 {frameSize}，" +
                    "而不是整张图集宽度（图集是两帧横向拼接，宽度为帧宽的 2 倍）——若仍是整张图集，" +
                    "说明没有真正切成独立纹理");

                var sprite = asset.Frames[i].Sprite;
                Assert.AreEqual(expectedPivotX, sprite.pivot.x / sprite.rect.width, 0.001f,
                    $"切帧不应破坏 ADR-0095 已收口的枢轴换算：第 {i} 帧枢轴 X 应仍等于脚底锚点像素 X ÷ 帧宽");
                Assert.AreEqual(expectedPivotY, sprite.pivot.y / sprite.rect.height, 0.001f,
                    $"切帧不应破坏 ADR-0095 已收口的枢轴换算：第 {i} 帧枢轴 Y 应仍等于 1 - 脚底锚点像素 Y ÷ 帧高");
                Assert.AreEqual(declaredPixelsPerUnit, sprite.pixelsPerUnit, 0.001f,
                    $"切帧不应破坏 ADR-0095 已收口的像素密度换算：第 {i} 帧应仍取自所属精灵集声明的 pixels_per_unit");
            }
        }

        /// <summary>不变量（分支合一）：①全部开关关闭时，静态图/逐帧动画应与改动前逐字节一致
        /// （mipmapCount == 1、过滤模式降级为双线性、逐帧动画各帧仍共用同一张图集纹理）；②地图分层图
        /// （复用 MapLayerHostPlayModeTests.cs 已验证的真实样例地图 world.sample_field）在默认选项下
        /// 应当生成 mip 链且各向异性等级取自选项声明的默认值 4；③开启逐帧独立纹理的动画资源被
        /// Unload 后，其独立帧纹理应当已被真正销毁（Unity 假空判断 `texture == null`），不泄漏显存。</summary>
        [UnityTest]
        public IEnumerator Invariants_AllSwitchesOff_MatchesPreChangeBehavior_MapLayerAnisoDefault_UnloadDestroysFrameTextures()
        {
            // ①-a：静态图关闭 mip 链——恢复改动前行为。
            _loader.TextureSampling.MipChainForImages = false;
            _loader.TextureSampling.MipChainForEffects = false;

            var imageId = new Id("layer.placeholder_beast__front__body");
            Sprite imageSprite = null!;
            yield return LoadAndWait(_loader, imageId, ResourceKind.Image, ok =>
            {
                Assert.IsTrue(ok, "占位野兽 front/body 层资源加载应当成功");
                Assert.IsTrue(_loader.TryGetSprite(imageId, out imageSprite));
            });
            Assert.AreEqual(1, imageSprite.texture.mipmapCount,
                "关闭 MipChainForImages 时应与改动前逐字节一致，不生成 mip 链（mipmapCount == 1）");
            Assert.AreEqual(FilterMode.Bilinear, imageSprite.texture.filterMode,
                "ADR-0096 判断记录：过滤模式仍是默认值 Trilinear、但没有 mip 链时应自动降级为 Bilinear" +
                "（Trilinear 无级间可插值，等价于 Bilinear）");

            // ②：地图分层图不受①-a 的开关影响（MipChainForMapLayers 保持默认 true），复用真实样例
            // 地图 world.sample_field（与 MapLayerHostPlayModeTests.cs 同一份真实资源，不覆盖 RootDir）。
            var mapId = new Id("world.sample_field");
            UnityResourceLoader.MapLayerAsset mapAsset = null!;
            {
                bool? mapSuccess = null;
                _loader.LoadAsync(mapId, ResourceKind.MapLayers, (id, ok) => mapSuccess = ok);
                var timeout = 5f;
                while (mapSuccess == null && timeout > 0f)
                {
                    _loader.Tick();
                    yield return null;
                    timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
                }
                Assert.IsNotNull(mapSuccess, "样例地图分层图加载应当有结果，不应当悬而不决");
                Assert.IsTrue(mapSuccess!.Value, "样例地图 world.sample_field 的 ground/overlay 加载应当成功");
                Assert.IsTrue(_loader.TryGetMapLayerAsset(mapId, out mapAsset));
            }
            Assert.Greater(mapAsset.Ground.texture.mipmapCount, 1,
                "ADR-0096 决策 1：地图分层图默认应当生成 mip 链（MipChainForMapLayers 默认 true，未被本用例关闭）");
            Assert.AreEqual(4, mapAsset.Ground.texture.anisoLevel,
                "ADR-0096 决策 2：地图分层图默认各向异性等级应为选项声明的默认值 4");

            // ①-b：逐帧动画关闭 mip 链——各帧应仍共用同一张图集纹理（引用相等），与改动前逐字节一致。
            _tempFixtureRoot = Path.Combine(Application.temporaryCachePath, "adr0096_invariant_" + System.Guid.NewGuid().ToString("N"));
            WritePngFixture(Path.Combine(_tempFixtureRoot, "sprite_anim", "fixture_0096_shared", "atlas.png"), 64, 32);
            WriteTextFixture(
                Path.Combine(_tempFixtureRoot, "sprite_anim", "fixture_0096_shared", "frames.json"),
                "{\"frames\": [{\"x\": 0, \"y\": 0, \"w\": 32, \"h\": 32}, {\"x\": 32, \"y\": 0, \"w\": 32, \"h\": 32}]}");
            UnityResourceLoader.RootDirOverrideForTests = _tempFixtureRoot;

            var sharedEffectId = new Id("sprite_anim.fixture_0096_shared");
            UnityResourceLoader.EffectAsset sharedAsset = null!;
            yield return LoadEffectAndWait(_loader, sharedEffectId, spriteSetId: null, ok =>
            {
                Assert.IsTrue(ok, "关闭 MipChainForEffects 的逐帧动画夹具加载应当成功");
                Assert.IsTrue(_loader.TryGetEffect(sharedEffectId, out sharedAsset));
            });
            Assert.AreSame(sharedAsset.Frames[0].Sprite.texture, sharedAsset.Frames[1].Sprite.texture,
                "关闭 MipChainForEffects 时应与改动前逐字节一致，全部帧共用同一张图集纹理（引用相等）");
            Assert.AreEqual(1, sharedAsset.Frames[0].Sprite.texture.mipmapCount,
                "关闭 MipChainForEffects 时共享图集纹理不应生成 mip 链");

            // ③：开启 MipChainForEffects 后加载另一个动画资源，Unload 后其独立帧纹理应当已被销毁。
            _loader.TextureSampling.MipChainForEffects = true;
            WritePngFixture(Path.Combine(_tempFixtureRoot, "sprite_anim", "fixture_0096_destroy", "atlas.png"), 32, 16);
            WriteTextFixture(
                Path.Combine(_tempFixtureRoot, "sprite_anim", "fixture_0096_destroy", "frames.json"),
                "{\"frames\": [{\"x\": 0, \"y\": 0, \"w\": 16, \"h\": 16}, {\"x\": 16, \"y\": 0, \"w\": 16, \"h\": 16}]}");

            var destroyEffectId = new Id("sprite_anim.fixture_0096_destroy");
            UnityResourceLoader.EffectAsset destroyAsset = null!;
            yield return LoadEffectAndWait(_loader, destroyEffectId, spriteSetId: null, ok =>
            {
                Assert.IsTrue(ok, "开启 MipChainForEffects 的逐帧动画夹具加载应当成功");
                Assert.IsTrue(_loader.TryGetEffect(destroyEffectId, out destroyAsset));
            });

            var frameTexture0 = destroyAsset.Frames[0].Sprite.texture;
            var frameTexture1 = destroyAsset.Frames[1].Sprite.texture;
            Assert.IsNotNull(frameTexture0, "卸载前独立帧纹理应当存在");
            Assert.AreNotSame(frameTexture0, frameTexture1, "开启 MipChainForEffects 时每帧应为独立纹理，不共享");

            _loader.Unload(destroyEffectId);
            yield return null;

            Assert.IsTrue(frameTexture0 == null,
                "ADR-0096 决策 3 已知限制配套要求：Unload 后独立帧纹理应当已被真正销毁（Unity 假空判断），不泄漏显存");
            Assert.IsTrue(frameTexture1 == null,
                "ADR-0096 决策 3 已知限制配套要求：Unload 后独立帧纹理应当已被真正销毁（Unity 假空判断），不泄漏显存");
        }
    }
}
