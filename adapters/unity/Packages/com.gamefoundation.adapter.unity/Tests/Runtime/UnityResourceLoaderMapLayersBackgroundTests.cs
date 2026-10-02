#nullable enable
// UnityResourceLoaderMapLayersBackgroundTests：NF2——ResourceKind.MapLayers 的 PNG 解码移到后台线程，
// 主线程 Tick 一层一个工作单元（此前 ground/overlay/decal 三张图在主线程一个完成项里整块 LoadImage 做完，
// 是 ADR-0109 登记的"地图分层图不改"限制）。夹具全部运行期自造。
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PngKind = Adapter.Unity.Tests.Runtime.UnityResourceLoaderMainThreadBudgetTests.PngKind;

namespace Adapter.Unity.Tests.Runtime
{
    [Category("module:engine_adapter")]
    public sealed class UnityResourceLoaderMapLayersBackgroundTests : PlayModeTestBase
    {
        private string? _tempRoot;

        [TearDown]
        public void TearDown()
        {
            UnityResourceLoader.RootDirOverrideForTests = null;
            if (_tempRoot != null && Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }

            _tempRoot = null;
        }

        private static byte[] Png(int w, int h, PngKind kind, int seed) =>
            UnityResourceLoaderMainThreadBudgetTests.MakePng(w, h, kind, seed);

        private static void WriteLayer(string root, string relativePath, byte[] png)
        {
            var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, png);
        }

        private string NewRoot()
        {
            _tempRoot = Path.Combine(Application.temporaryCachePath, "nf2_maplayers_" + System.Guid.NewGuid().ToString("N"));
            UnityResourceLoader.RootDirOverrideForTests = _tempRoot;
            return _tempRoot;
        }

        /// <summary>后台读取+解码完成后完成项入队：等到 PendingMainThreadCompletionCount 非零（期间不 Tick，
        /// 避免把等待算进工作单元数）。</summary>
        private static IEnumerator WaitUntilQueued(UnityResourceLoader loader)
        {
            var deadline = Time.realtimeSinceStartup + 60f;
            while (loader.PendingMainThreadCompletionCount == 0 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(1, loader.PendingMainThreadCompletionCount, "后台完成后应有且只有一个待处理完成项");
        }

        private static IEnumerator TickUntil(UnityResourceLoader loader, System.Func<bool> done, string what, System.Action? onTick = null)
        {
            var deadline = Time.realtimeSinceStartup + 60f;
            while (!done() && Time.realtimeSinceStartup < deadline)
            {
                loader.Tick();
                onTick?.Invoke();
                yield return null;
            }

            Assert.IsTrue(done(), what + "（超时）");
        }

        /// <summary>复现 + 不变量：三层（ground/overlay/decal）。预算压到极小（每个 Tick 恰好推进一个工作单元）时，
        /// 期望"做了工作的 Tick 数"= 层数（改动前整个资源是一个工作单元、1 个 Tick 做完，期望值按层数算出，
        /// 所以修复前红）；回调只在最后一层做完那次触发恰好一次；最后一层做完前 TryGetMapLayerAsset 读不到
        /// 半成品；各层 mip0 像素与源 PNG 经引擎 LoadImage 的结果逐像素相同；走的是后台托管解码
        /// （ManagedDecodeCount 为 1、没有回退）；精灵是整矩形网格（4 个顶点）。</summary>
        [UnityTest]
        public IEnumerator ThreeLayers_BackgroundDecoded_OneWorkUnitPerLayer_PixelsMatchEngine_NoHalfBuiltAssetVisible()
        {
            var root = NewRoot();
            var mapId = new Id("world.nf2_three_layers");
            var ground = Png(96, 64, PngKind.Rgb8, 1);
            var overlay = Png(96, 64, PngKind.Rgba8, 2);
            var decal = Png(48, 32, PngKind.Rgba8, 3);
            WriteLayer(root, AssetRefConventions.MapGroundFile(mapId), ground);
            WriteLayer(root, AssetRefConventions.MapOverlayFile(mapId), overlay);
            WriteLayer(root, AssetRefConventions.MapDecalFile(mapId), decal);
            const int layerCount = 3;

            var loader = new UnityResourceLoader { MainThreadBudgetMilliseconds = 0.0001 };
            var callbacks = 0;
            var success = false;
            loader.LoadAsync(mapId, ResourceKind.MapLayers, (_, ok) => { callbacks++; success = ok; });
            yield return WaitUntilQueued(loader);

            var workTicks = 0;
            var halfBuiltVisible = false;
            yield return TickUntil(loader, () => callbacks > 0, "地图分层图加载", () =>
            {
                if (loader.LastTickWorkUnitCount > 0)
                {
                    workTicks++;
                }

                if (callbacks == 0 && loader.TryGetMapLayerAsset(mapId, out _))
                {
                    halfBuiltVisible = true;
                }
            });

            Assert.IsTrue(success);
            Assert.AreEqual(1, callbacks, "回调恰好一次");
            Assert.AreEqual(layerCount, workTicks, "预算极小时每个 Tick 推进一个工作单元：做完 N 层需要 N 个做了工作的 Tick");
            Assert.IsFalse(halfBuiltVisible, "最后一层做完前读取口读不到半成品");
            Assert.AreEqual(1, loader.ManagedDecodeCount, "走后台托管解码");
            Assert.AreEqual(0, loader.MainThreadFallbackDecodeCount);
            Assert.IsTrue(loader.TryGetMapLayerAsset(mapId, out var asset));
            Assert.IsNotNull(asset.Decal);

            var layers = new[] { (asset.Ground, ground, "ground"), (asset.Overlay, overlay, "overlay"), (asset.Decal!, decal, "decal") };
            foreach (var (sprite, png, what) in layers)
            {
                var reference = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                Assert.IsTrue(reference.LoadImage(png), what);
                Assert.AreEqual(reference.width, sprite.texture.width, what);
                Assert.AreEqual(reference.height, sprite.texture.height, what);
                CollectionAssert.AreEqual(reference.GetPixels32(0), ((Texture2D)sprite.texture).GetPixels32(0), what + " mip0 与引擎 LoadImage 逐像素一致");
                Assert.AreEqual(4, sprite.vertices.Length, what + " 整矩形网格 4 个顶点");
                Object.DestroyImmediate(reference);
            }
        }

        /// <summary>不变量：可选层 decal 缺失只建两层（两个工作单元），Decal 为 null，不算失败。</summary>
        [UnityTest]
        public IEnumerator DecalMissing_TwoUnits_DecalNull_Succeeds()
        {
            var root = NewRoot();
            var mapId = new Id("world.nf2_two_layers");
            WriteLayer(root, AssetRefConventions.MapGroundFile(mapId), Png(32, 32, PngKind.Rgb8, 4));
            WriteLayer(root, AssetRefConventions.MapOverlayFile(mapId), Png(32, 32, PngKind.Rgba8, 5));

            var loader = new UnityResourceLoader { MainThreadBudgetMilliseconds = 0.0001 };
            var callbacks = 0;
            var success = false;
            loader.LoadAsync(mapId, ResourceKind.MapLayers, (_, ok) => { callbacks++; success = ok; });
            yield return WaitUntilQueued(loader);

            var workTicks = 0;
            yield return TickUntil(loader, () => callbacks > 0, "两层地图加载", () =>
            {
                if (loader.LastTickWorkUnitCount > 0)
                {
                    workTicks++;
                }
            });

            Assert.IsTrue(success);
            Assert.AreEqual(2, workTicks);
            Assert.IsTrue(loader.TryGetMapLayerAsset(mapId, out var asset));
            Assert.IsNull(asset.Decal);
        }

        /// <summary>不变量（冷 = 回退）：某一层是托管解码器不支持的变体（16 位 RGBA）时，整个地图资源回退到主线程
        /// LoadImage 路径（一个不可分工作单元）、记一条 Warn、结果仍加载成功。</summary>
        [UnityTest]
        public IEnumerator UnsupportedVariantLayer_FallsBackToMainThread_WarnsOnce_StillLoads()
        {
            var root = NewRoot();
            var mapId = new Id("world.nf2_fallback");
            WriteLayer(root, AssetRefConventions.MapGroundFile(mapId), Png(32, 32, PngKind.Rgba16, 6));
            WriteLayer(root, AssetRefConventions.MapOverlayFile(mapId), Png(32, 32, PngKind.Rgba8, 7));
            LogAssert.Expect(LogType.Warning, new Regex("nf2_fallback.*无法后台解码"));

            var loader = new UnityResourceLoader { MainThreadBudgetMilliseconds = 0.0001 };
            var callbacks = 0;
            var success = false;
            loader.LoadAsync(mapId, ResourceKind.MapLayers, (_, ok) => { callbacks++; success = ok; });
            yield return WaitUntilQueued(loader);

            var workTicks = 0;
            yield return TickUntil(loader, () => callbacks > 0, "回退地图加载", () =>
            {
                if (loader.LastTickWorkUnitCount > 0)
                {
                    workTicks++;
                }
            });

            Assert.IsTrue(success);
            Assert.AreEqual(1, workTicks, "回退路径整个资源是一个不可分工作单元");
            Assert.AreEqual(1, loader.MainThreadFallbackDecodeCount);
            Assert.AreEqual(0, loader.ManagedDecodeCount);
            Assert.IsTrue(loader.TryGetMapLayerAsset(mapId, out _));
        }

        /// <summary>不变量：必需层缺失（只有 ground）整次加载失败，回调 false，没有半成品缓存。</summary>
        [UnityTest]
        public IEnumerator RequiredLayerMissing_FailsWithFalseCallback_NoAssetCached()
        {
            var root = NewRoot();
            var mapId = new Id("world.nf2_missing_overlay");
            WriteLayer(root, AssetRefConventions.MapGroundFile(mapId), Png(16, 16, PngKind.Rgb8, 8));

            var loader = new UnityResourceLoader();
            var callbacks = 0;
            var success = true;
            loader.LoadAsync(mapId, ResourceKind.MapLayers, (_, ok) => { callbacks++; success = ok; });

            yield return TickUntil(loader, () => callbacks > 0, "缺层地图加载");

            Assert.IsFalse(success);
            Assert.IsFalse(loader.TryGetMapLayerAsset(mapId, out _));
            Assert.IsFalse(loader.IsLoaded(mapId));
        }
    }
}
