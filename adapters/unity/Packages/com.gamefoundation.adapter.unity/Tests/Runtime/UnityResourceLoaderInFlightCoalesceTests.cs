#nullable enable
// UnityResourceLoaderInFlightCoalesceTests：NF2——同一资源（同 id、同种类）已有加载在途时，后到的请求并入在途请求。
// 旧行为：每个请求各自读文件、各自解码，后完成的覆盖先完成的缓存项。夹具全部运行期自造。
using System.Collections;
using System.IO;
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
    public sealed class UnityResourceLoaderInFlightCoalesceTests : PlayModeTestBase
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

        private void WriteMap(Id mapId)
        {
            _tempRoot = Path.Combine(Application.temporaryCachePath, "nf2_coalesce_" + System.Guid.NewGuid().ToString("N"));
            UnityResourceLoader.RootDirOverrideForTests = _tempRoot;
            void Write(string rel, byte[] png)
            {
                var full = Path.Combine(_tempRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllBytes(full, png);
            }

            Write(AssetRefConventions.MapGroundFile(mapId), UnityResourceLoaderMainThreadBudgetTests.MakePng(32, 32, PngKind.Rgb8, 11));
            Write(AssetRefConventions.MapOverlayFile(mapId), UnityResourceLoaderMainThreadBudgetTests.MakePng(32, 32, PngKind.Rgba8, 12));
        }

        private static IEnumerator TickUntil(UnityResourceLoader loader, System.Func<bool> done, string what)
        {
            var deadline = Time.realtimeSinceStartup + 60f;
            while (!done() && Time.realtimeSinceStartup < deadline)
            {
                loader.Tick();
                yield return null;
            }

            Assert.IsTrue(done(), what + "（超时）");
        }

        /// <summary>复现 + 不变量：同一资源连发三次（第一次未完成时），只读/解码一次（ManagedDecodeCount 为 1，
        /// 旧行为为 3），三个回调各恰好一次且结果一致；完成后缓存只有一份地图资产；完成后再次请求是全新请求
        /// （不会并入一个已结束的请求）。</summary>
        [UnityTest]
        public IEnumerator ThreeRequestsWhileInFlight_DecodedOnce_EachCallbackFiresOnce_LaterRequestIsFresh()
        {
            var mapId = new Id("world.nf2_coalesce");
            WriteMap(mapId);
            var loader = new UnityResourceLoader();
            var calls = new int[3];
            var oks = new bool[3];
            for (var i = 0; i < 3; i++)
            {
                var index = i;
                loader.LoadAsync(mapId, ResourceKind.MapLayers, (_, ok) => { calls[index]++; oks[index] = ok; });
            }

            Assert.AreEqual(2, loader.CoalescedLoadCount, "后两个请求并入第一个在途请求");
            Assert.AreEqual(1, loader.PendingLoadCount);

            yield return TickUntil(loader, () => calls[0] > 0, "三连发地图加载");

            CollectionAssert.AreEqual(new[] { 1, 1, 1 }, calls, "每个回调恰好一次");
            CollectionAssert.AreEqual(new[] { true, true, true }, oks);
            Assert.AreEqual(1, loader.ManagedDecodeCount, "只解码一次");
            Assert.IsTrue(loader.TryGetMapLayerAsset(mapId, out _));

            var again = 0;
            loader.LoadAsync(mapId, ResourceKind.MapLayers, (_, __) => again++);
            Assert.AreEqual(2, loader.CoalescedLoadCount, "完成后的新请求不再并入");
            yield return TickUntil(loader, () => again > 0, "完成后的再次加载");
            Assert.AreEqual(1, again);
            Assert.AreEqual(2, loader.ManagedDecodeCount, "完成后的新请求是一次全新加载");
        }

        /// <summary>不变量：并入的回调之一抛异常不影响其余回调（异常记日志）。</summary>
        [UnityTest]
        public IEnumerator CoalescedCallbackThrows_OtherCallbacksStillFire()
        {
            var mapId = new Id("world.nf2_coalesce_throw");
            WriteMap(mapId);
            var loader = new UnityResourceLoader();
            var first = 0;
            var third = 0;
            loader.LoadAsync(mapId, ResourceKind.MapLayers, (_, __) => first++);
            loader.LoadAsync(mapId, ResourceKind.MapLayers, (_, __) => throw new System.InvalidOperationException("nf2 coalesce test"));
            loader.LoadAsync(mapId, ResourceKind.MapLayers, (_, __) => third++);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("nf2 coalesce test"));

            yield return TickUntil(loader, () => first > 0 && third > 0, "并入回调抛异常");
            Assert.AreEqual(1, first);
            Assert.AreEqual(1, third);
        }
    }
}
