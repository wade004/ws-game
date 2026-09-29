#nullable enable
// UnityResourceLoaderMainThreadBudgetTests：ADR-0109（资源解码后台化与主线程分帧）收口验收——
// 消费方反馈第五十八批，阻塞。精灵单位本局首次转到某个方向档位时，UnityViewFactory 一次发起几十个
// sprite_anim 冷加载，UnityResourceLoader.Tick 此前在主线程一帧内把全部解码（LoadImage 解 PNG、
// 逐帧独立纹理的取块写块、Apply 生成 mip 链）排空，单帧 725～866 ms。修复后 PNG 解码与按帧切块在
// 后台线程完成，主线程 Tick 受 MainThreadBudgetMilliseconds 预算约束、按"一张最终纹理"为工作单元
// 续作。
//
// 测试尺度（任务书写死）：一条复现用例 + 一条不变量用例。夹具全部运行期自造（生成 PNG 写到临时
// 资源根，经 RootDirOverrideForTests 指过去），不依赖检出目录里碰巧存在的文件。
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class UnityResourceLoaderMainThreadBudgetTests : PlayModeTestBase
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

        // ---------------------------------------------------------------------------------------
        // 夹具
        // ---------------------------------------------------------------------------------------

        private enum PngKind
        {
            Rgba8,
            Rgb8,
            Rgba16,
        }

        /// <summary>造一张带噪声的图（含 alpha=0 但 RGB 非零的像素，检验解码器不会顺手清零 RGB），按
        /// <paramref name="kind"/> 编码成 PNG 字节，并核对 IHDR 确实是预期的颜色类型/位深——否则夹具
        /// 没有测到想测的变体，直接失败而不是静默通过。</summary>
        private static byte[] MakePng(int width, int height, PngKind kind, int seed)
        {
            var rng = new System.Random(seed);
            byte[] png;
            if (kind == PngKind.Rgba16)
            {
                // Unity 的 EncodeToPNG 不产出 16 位 PNG（RGBA64 会被写成 8 位），这里手写一个最小 PNG 编码器
                // （zlib 存储块，不压缩）来造托管解码器不支持的 16 位 RGBA 夹具；LoadImage 能正常读它。
                var raw = new byte[height * (1 + width * 8)];
                var pos = 0;
                for (var y = 0; y < height; y++)
                {
                    raw[pos++] = 0; // 滤波类型 None
                    for (var i = 0; i < width * 8; i++)
                    {
                        raw[pos++] = (byte)rng.Next(0, 256);
                    }
                }

                png = EncodeStoredPng(width, height, bitDepth: 16, colorType: 6, raw);
            }
            else
            {
                var format = kind == PngKind.Rgb8 ? TextureFormat.RGB24 : TextureFormat.RGBA32;
                var texture = new Texture2D(width, height, format, false);
                var pixels = new Color32[width * height];
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var baseValue = (x * 5 + y * 3) & 0xFF;
                        var a = kind == PngKind.Rgba8 ? (byte)(rng.Next(0, 6) == 0 ? 0 : rng.Next(0, 256)) : (byte)255;
                        pixels[y * width + x] = new Color32(
                            (byte)((baseValue + rng.Next(0, 32)) & 0xFF),
                            (byte)((baseValue * 2 + rng.Next(0, 32)) & 0xFF),
                            (byte)((255 - baseValue + rng.Next(0, 32)) & 0xFF),
                            a);
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply();
                png = ImageConversion.EncodeToPNG(texture);
                Object.DestroyImmediate(texture);
                Assert.AreEqual(8, png[24], "夹具应当是 8 位 PNG");
                Assert.AreEqual(kind == PngKind.Rgb8 ? 2 : 6, png[25],
                    "夹具颜色类型应当与 PngKind 一致（RGB=2，RGBA=6）");
            }

            return png;
        }

        private static byte[] EncodeStoredPng(int width, int height, int bitDepth, int colorType, byte[] rawScanlines)
        {
            using var ms = new MemoryStream();
            ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

            var ihdr = new byte[13];
            WriteBigEndian(ihdr, 0, (uint)width);
            WriteBigEndian(ihdr, 4, (uint)height);
            ihdr[8] = (byte)bitDepth;
            ihdr[9] = (byte)colorType;
            WriteChunk(ms, "IHDR", ihdr);

            using var zlib = new MemoryStream();
            zlib.WriteByte(0x78);
            zlib.WriteByte(0x01);
            var offset = 0;
            while (offset < rawScanlines.Length)
            {
                var n = System.Math.Min(65535, rawScanlines.Length - offset);
                var final = offset + n >= rawScanlines.Length;
                zlib.WriteByte((byte)(final ? 1 : 0));
                zlib.WriteByte((byte)(n & 0xFF));
                zlib.WriteByte((byte)(n >> 8));
                zlib.WriteByte((byte)(~n & 0xFF));
                zlib.WriteByte((byte)((~n >> 8) & 0xFF));
                zlib.Write(rawScanlines, offset, n);
                offset += n;
            }

            uint s1 = 1, s2 = 0;
            foreach (var b in rawScanlines)
            {
                s1 = (s1 + b) % 65521;
                s2 = (s2 + s1) % 65521;
            }

            var adler = new byte[4];
            WriteBigEndian(adler, 0, (s2 << 16) | s1);
            zlib.Write(adler, 0, 4);
            WriteChunk(ms, "IDAT", zlib.ToArray());
            WriteChunk(ms, "IEND", new byte[0]);
            return ms.ToArray();
        }

        private static void WriteBigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            var header = new byte[8];
            WriteBigEndian(header, 0, (uint)data.Length);
            for (var i = 0; i < 4; i++)
            {
                header[4 + i] = (byte)type[i];
            }

            stream.Write(header, 0, 8);
            stream.Write(data, 0, data.Length);

            uint crc = 0xFFFFFFFFu;
            for (var i = 4; i < 8; i++)
            {
                crc = Crc32Step(crc, header[i]);
            }

            foreach (var b in data)
            {
                crc = Crc32Step(crc, b);
            }

            var tail = new byte[4];
            WriteBigEndian(tail, 0, ~crc);
            stream.Write(tail, 0, 4);
        }

        private static uint Crc32Step(uint crc, byte b)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }

            return crc;
        }

        private static string FramesJson(IReadOnlyList<(int X, int Y, int W, int H)> rects)
        {
            var sb = new System.Text.StringBuilder("{\"loop\": true, \"fps\": 12, \"frames\": [");
            for (var i = 0; i < rects.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append("{\"x\": ").Append(rects[i].X).Append(", \"y\": ").Append(rects[i].Y)
                    .Append(", \"w\": ").Append(rects[i].W).Append(", \"h\": ").Append(rects[i].H).Append('}');
            }

            return sb.Append("]}").ToString();
        }

        private static void WriteEffect(string root, string name, byte[] atlasPng, string framesJson)
        {
            var dir = Path.Combine(root, "sprite_anim", name);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "atlas.png"), atlasPng);
            File.WriteAllText(Path.Combine(dir, "frames.json"), framesJson);
        }

        private static void WriteImage(string root, string name, byte[] png)
        {
            var dir = Path.Combine(root, "sprites");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), png);
        }

        private static List<(int X, int Y, int W, int H)> GridRects(int atlasWidth, int atlasHeight, int columns, int rows)
        {
            var list = new List<(int, int, int, int)>();
            var w = atlasWidth / columns;
            var h = atlasHeight / rows;
            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < columns; c++)
                {
                    list.Add((c * w, r * h, w, h));
                }
            }

            return list;
        }

        private IEnumerator WaitFor(System.Func<bool> condition, string what, UnityResourceLoader? tickWith, float timeoutSeconds = 60f)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                tickWith?.Tick();
                yield return null;
            }

            Assert.IsTrue(condition(), what + "（超时）");
        }

        // ---------------------------------------------------------------------------------------
        // 复现用例
        // ---------------------------------------------------------------------------------------

        /// <summary>复现用例（修复前必须红）：同一帧发起 24 个 sprite_anim 冷加载（每个图集 1024x1024、
        /// 8 帧，开 mip 链，带精灵集提示）加 4 个 Image 冷加载，之后逐帧 Tick 并用 Stopwatch 采样。
        /// 期望值由规则算出：单次 Tick 耗时不超过"预算 + 加载器报告的单个工作单元最大耗时 + 固定
        /// 调度余量"；做了实际工作的 Tick 至少 2 次（不是一帧排空）；全部资源成功、回调恰好各一次。
        /// 修复前（Tick 一帧排空全部解码）单次 Tick 远超该上界。</summary>
        [UnityTest]
        public IEnumerator Tick_ManyColdAtlasesRequestedInOneFrame_EachTickStaysWithinBudget()
        {
            const int atlasCount = 24;
            const int imageCount = 4;
            const int atlasSize = 1024;

            // 固定调度余量：Tick 之外的 Stopwatch 起停、GC 停顿、编辑器抖动。分帧后每个工作单元是
            // 毫秒级，10 ms 余量足够宽，又远小于修复前的数百毫秒长帧，不会把真红洗白。
            const double schedulingSlackMilliseconds = 10.0;

            _tempRoot = Path.Combine(Application.temporaryCachePath, "adr0109_repro_" + System.Guid.NewGuid().ToString("N"));
            var atlasPng = MakePng(atlasSize, atlasSize, PngKind.Rgba8, seed: 1);
            var framesJson = FramesJson(GridRects(atlasSize, atlasSize, 4, 2));
            for (var i = 0; i < atlasCount; i++)
            {
                WriteEffect(_tempRoot, "adr0109_repro_" + i, atlasPng, framesJson);
            }

            var imagePng = MakePng(atlasSize, atlasSize, PngKind.Rgba8, seed: 2);
            for (var i = 0; i < imageCount; i++)
            {
                WriteImage(_tempRoot, "adr0109_repro_img_" + i, imagePng);
            }

            UnityResourceLoader.RootDirOverrideForTests = _tempRoot;

            var loader = new UnityResourceLoader();
            Assert.AreEqual(4.0, loader.MainThreadBudgetMilliseconds, 1e-9, "ADR-0109：默认主线程预算应为 4 ms");

            var callbackCounts = new Dictionary<string, int>();
            var failures = new List<string>();
            LoadCallback callback = (id, ok) =>
            {
                callbackCounts[id.Value] = callbackCounts.TryGetValue(id.Value, out var n) ? n + 1 : 1;
                if (!ok)
                {
                    failures.Add(id.Value);
                }
            };

            var spriteSet = new ResourceLoadHints(new Id("sprite.adr0109_repro_set"));
            var expectedIds = new List<string>();
            for (var i = 0; i < atlasCount; i++)
            {
                var id = new Id("sprite_anim.adr0109_repro_" + i);
                expectedIds.Add(id.Value);
                loader.LoadAsync(id, ResourceKind.Effect, spriteSet, callback);
            }

            for (var i = 0; i < imageCount; i++)
            {
                var id = new Id("sprite.adr0109_repro_img_" + i);
                expectedIds.Add(id.Value);
                loader.LoadAsync(id, ResourceKind.Image, callback);
            }

            // 先不 Tick，给后台线程一小段真实时间读完字节：复现"游戏里后台读取比主线程快、Tick 面对
            // 一整批待处理项"的现场。修复前这一批会在第一次有效 Tick 里被一帧排空。
            var settleUntil = Time.realtimeSinceStartup + 0.3f;
            while (Time.realtimeSinceStartup < settleUntil)
            {
                yield return null;
            }

            var stopwatch = new Stopwatch();
            var workTicks = 0;
            var maxWorkTickMilliseconds = 0.0;
            var deadline = Time.realtimeSinceStartup + 120f;
            while (callbackCounts.Count < expectedIds.Count && Time.realtimeSinceStartup < deadline)
            {
                stopwatch.Restart();
                loader.Tick();
                stopwatch.Stop();
                if (loader.LastTickWorkUnitCount > 0)
                {
                    workTicks++;
                    maxWorkTickMilliseconds = System.Math.Max(maxWorkTickMilliseconds, stopwatch.Elapsed.TotalMilliseconds);
                }

                yield return null;
            }

            var maxUnit = loader.MaxWorkUnitMilliseconds;
            var allowed = loader.MainThreadBudgetMilliseconds + maxUnit + schedulingSlackMilliseconds;
            Debug.Log($"[ADR-0109 repro] workTicks={workTicks} maxWorkTickMs={maxWorkTickMilliseconds:F1} " +
                      $"maxUnitMs={maxUnit:F1} allowedMs={allowed:F1} peakTickMs={loader.PeakTickDecodeMilliseconds:F1}");

            Assert.AreEqual(expectedIds.Count, callbackCounts.Count, "全部请求都应当在超时前收到回调");
            foreach (var id in expectedIds)
            {
                Assert.AreEqual(1, callbackCounts[id], $"资源 \"{id}\" 的回调次数应恰为一次");
            }

            Assert.IsEmpty(failures, "全部资源应当加载成功");
            Assert.GreaterOrEqual(workTicks, 2,
                "做了实际工作的 Tick 应至少 2 次：一批冷加载不再要求在下一帧一次排空");
            Assert.LessOrEqual(maxWorkTickMilliseconds, allowed,
                $"单次 Tick 耗时应不超过 预算({loader.MainThreadBudgetMilliseconds} ms) + 单个工作单元最大耗时" +
                $"({maxUnit:F1} ms) + 调度余量({schedulingSlackMilliseconds} ms) = {allowed:F1} ms；" +
                $"实测最大 {maxWorkTickMilliseconds:F1} ms");
            Assert.AreEqual(0, loader.PendingMainThreadCompletionCount, "全部完成后不应有待主线程处理的完成项");
        }

        // ---------------------------------------------------------------------------------------
        // 不变量用例
        // ---------------------------------------------------------------------------------------

        /// <summary>不变量（分支合一）：①同一份 PNG（RGBA 与 RGB 各一张），后台托管解码 + 后台切块路径
        /// 产出的每帧纹理 mip0 像素与"主线程 LoadImage + 按帧矩形取块"的参照结果逐字节相等，Image 路径
        /// 同理；②16 位 PNG（托管解码器不支持的变体）走回退仍加载成功、写了 Warn、并被计入回退计数；
        /// ③MainThreadBudgetMilliseconds = 0 时一次 Tick 排空全部完成项，预算极小时每次 Tick 恰好
        /// 推进一个工作单元；④热路径：EffectReuseCache 复用不重新解码、返回同一个 EffectAsset，
        /// TryGetEffect 命中读取不受影响；⑤精灵网格改为整矩形后（第五十八批续作）：同一资源的
        /// sprite.rect/pivot/pixelsPerUnit/bounds/SpriteRenderer.bounds 与"贴合轮廓"参照精灵逐项相等、
        /// 渲染到隔离层 RenderTexture 的像素逐字节相同，且 textureRect 为整矩形、顶点数为 4
        /// （覆盖 Image、Effect 开 mip、Effect 关 mip 共用图集、回退路径四个分支）；mip1 与 1.87.0 做法逐字节相等。</summary>
        [UnityTest]
        public IEnumerator Invariants_ManagedDecodeMatchesLoadImage_FallbackWarns_ZeroBudgetDrains_HotPathReusesCache()
        {
            _tempRoot = Path.Combine(Application.temporaryCachePath, "adr0109_invariant_" + System.Guid.NewGuid().ToString("N"));
            UnityResourceLoader.RootDirOverrideForTests = _tempRoot;
            var loader = new UnityResourceLoader();

            // ---- ① 逐字节对拍 -------------------------------------------------------------------
            const int atlasWidth = 128;
            const int atlasHeight = 96;
            var rects = new List<(int X, int Y, int W, int H)>
            {
                (0, 0, 64, 48), (64, 0, 64, 48), (0, 48, 64, 48), (64, 48, 64, 48),
                (10, 20, 30, 17), // 非对齐、非方形的帧，检验行序与偏移
            };
            var expectedManagedCount = 0;
            foreach (var kind in new[] { PngKind.Rgba8, PngKind.Rgb8 })
            {
                var name = "adr0109_parity_" + kind.ToString().ToLowerInvariant();
                var png = MakePng(atlasWidth, atlasHeight, kind, seed: 7);
                WriteEffect(_tempRoot, name, png, FramesJson(rects));
                WriteImage(_tempRoot, name + "_img", png);

                var effectId = new Id("sprite_anim." + name);
                var imageId = new Id("sprite." + name + "_img");
                var done = 0;
                var allOk = true;
                loader.LoadAsync(effectId, ResourceKind.Effect, (_, ok) => { done++; allOk &= ok; });
                loader.LoadAsync(imageId, ResourceKind.Image, (_, ok) => { done++; allOk &= ok; });
                yield return WaitFor(() => done == 2, $"{kind} 对拍夹具加载", loader);
                Assert.IsTrue(allOk, $"{kind} 对拍夹具应当加载成功");
                expectedManagedCount += 2;

                // 参照：主线程 LoadImage + 按帧矩形 GetPixels(x, y, w, h)（1.87.0 的做法）。
                var reference = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                Assert.IsTrue(reference.LoadImage(png));
                Assert.AreEqual(atlasWidth, reference.width);
                Assert.AreEqual(atlasHeight, reference.height);

                Assert.IsTrue(loader.TryGetEffect(effectId, out var asset));
                Assert.AreEqual(rects.Count, asset.Frames.Length);
                for (var i = 0; i < rects.Count; i++)
                {
                    var r = rects[i];
                    var expected = ToColor32(reference.GetPixels(r.X, r.Y, r.W, r.H));
                    var frameTexture = asset.Frames[i].Sprite.texture;
                    Assert.AreEqual(r.W, frameTexture.width);
                    Assert.AreEqual(r.H, frameTexture.height);
                    Assert.Greater(frameTexture.mipmapCount, 1, "开 mip 链时每帧仍由引擎 Apply(updateMipmaps) 生成 mip 链");
                    AssertPixelsEqual(expected, frameTexture.GetPixels32(0), $"{kind} 逐帧动画第 {i} 帧 mip0");

                    // 建纹理改为不初始化像素内存后，mip1 仍须与 1.87.0 的做法（new + SetPixels + Apply(mip)）逐字节相等。
                    var referenceFrame = new Texture2D(r.W, r.H, TextureFormat.RGBA32, true);
                    referenceFrame.SetPixels(reference.GetPixels(r.X, r.Y, r.W, r.H));
                    referenceFrame.Apply(updateMipmaps: true, makeNoLongerReadable: false);
                    Assert.AreEqual(referenceFrame.mipmapCount, frameTexture.mipmapCount, $"{kind} 第 {i} 帧 mip 链长度");
                    AssertPixelsEqual(referenceFrame.GetPixels32(1), frameTexture.GetPixels32(1), $"{kind} 逐帧动画第 {i} 帧 mip1");
                    Object.DestroyImmediate(referenceFrame);
                }

                Assert.IsTrue(loader.TryGetSprite(imageId, out var imageSprite));
                AssertPixelsEqual(reference.GetPixels32(0), imageSprite.texture.GetPixels32(0), $"{kind} Image mip0");
                Object.DestroyImmediate(reference);
            }

            Assert.AreEqual(expectedManagedCount, loader.ManagedDecodeCount, "两种 PNG 变体都应当走后台托管解码，没有回退");
            Assert.AreEqual(0, loader.MainThreadFallbackDecodeCount);

            // ---- ② 不支持的变体回退 -------------------------------------------------------------
            {
                var png16 = MakePng(atlasWidth, atlasHeight, PngKind.Rgba16, seed: 9);
                WriteEffect(_tempRoot, "adr0109_fallback", png16, FramesJson(rects));
                WriteImage(_tempRoot, "adr0109_fallback_img", png16);
                var effectId = new Id("sprite_anim.adr0109_fallback");
                var imageId = new Id("sprite.adr0109_fallback_img");

                // 两条 Warn 的到达先后取决于后台完成顺序，两条期望用同一个正则（资源 id 都含该前缀）。
                LogAssert.Expect(LogType.Warning, new Regex("adr0109_fallback.*无法后台解码"));
                LogAssert.Expect(LogType.Warning, new Regex("adr0109_fallback.*无法后台解码"));

                var done = 0;
                var allOk = true;
                loader.LoadAsync(effectId, ResourceKind.Effect, (_, ok) => { done++; allOk &= ok; });
                loader.LoadAsync(imageId, ResourceKind.Image, (_, ok) => { done++; allOk &= ok; });
                yield return WaitFor(() => done == 2, "16 位 PNG 回退夹具加载", loader);
                Assert.IsTrue(allOk, "托管解码器不支持的变体应当回退主线程 LoadImage 仍加载成功");
                Assert.AreEqual(2, loader.MainThreadFallbackDecodeCount, "两次回退都应当计数");
                Assert.IsTrue(loader.TryGetEffect(effectId, out var fallbackAsset));
                Assert.AreEqual(rects.Count, fallbackAsset.Frames.Length);
                Assert.IsTrue(loader.TryGetSprite(imageId, out var fallbackSprite));
                AssertFullRectMeshOnly(fallbackAsset.Frames[0].Sprite, "回退路径 Effect 帧");
                AssertFullRectMeshOnly(fallbackSprite, "回退路径 Image");
            }

            // ---- ⑤ 精灵网格整矩形：与"贴合轮廓"参照逐项相等，渲染像素逐字节相同 ---------------------------
            {
                // 带透明边距的夹具（中间不透明圆盘，四周 alpha=0），保证"贴合轮廓"网格确实比整矩形小——否则本
                // 检查对不上号。
                var marginPng = MakeMarginPng(96, 80);
                var marginRects = new List<(int X, int Y, int W, int H)> { (0, 0, 48, 40), (48, 40, 48, 40) };
                WriteEffect(_tempRoot, "adr0109_margin", marginPng, FramesJson(marginRects));
                WriteImage(_tempRoot, "adr0109_margin_img", marginPng);
                WriteEffect(_tempRoot, "adr0109_margin_atlas", marginPng, FramesJson(marginRects));

                var done = 0;
                loader.LoadAsync(new Id("sprite_anim.adr0109_margin"), ResourceKind.Effect, (_, ok) => { Assert.IsTrue(ok); done++; });
                loader.LoadAsync(new Id("sprite.adr0109_margin_img"), ResourceKind.Image, (_, ok) => { Assert.IsTrue(ok); done++; });
                yield return WaitFor(() => done == 2, "⑤ 透明边距夹具（开 mip）加载", loader);

                Assert.IsTrue(loader.TryGetEffect(new Id("sprite_anim.adr0109_margin"), out var marginAsset));
                foreach (var frame in marginAsset.Frames)
                {
                    AssertSpriteMatchesTightReference(frame.Sprite, "Effect 开 mip 帧");
                }

                Assert.IsTrue(loader.TryGetSprite(new Id("sprite.adr0109_margin_img"), out var marginImage));
                AssertSpriteMatchesTightReference(marginImage, "Image");

                // 关 mip 链：全部帧共用同一张图集纹理，精灵矩形是图集内的子矩形。
                loader.TextureSampling.MipChainForEffects = false;
                var atlasDone = false;
                loader.LoadAsync(new Id("sprite_anim.adr0109_margin_atlas"), ResourceKind.Effect, (_, ok) => { Assert.IsTrue(ok); atlasDone = true; });
                loader.TextureSampling.MipChainForEffects = true; // 请求时已取快照，恢复不影响本次
                yield return WaitFor(() => atlasDone, "⑤ 透明边距夹具（关 mip、共用图集）加载", loader);

                Assert.IsTrue(loader.TryGetEffect(new Id("sprite_anim.adr0109_margin_atlas"), out var atlasAsset));
                Assert.AreSame(atlasAsset.Frames[0].Sprite.texture, atlasAsset.Frames[1].Sprite.texture, "关 mip 链各帧应共用同一张图集");
                foreach (var frame in atlasAsset.Frames)
                {
                    AssertSpriteMatchesTightReference(frame.Sprite, "Effect 关 mip 共用图集帧");
                }
            }

            // ---- ③ 预算 0 一次排空；预算极小每次 Tick 恰好一个工作单元 ------------------------------
            {
                const int count = 6;
                var png = MakePng(64, 64, PngKind.Rgba8, seed: 11);
                var smallRects = GridRects(64, 64, 2, 2);
                var ids = new List<Id>();
                for (var i = 0; i < count; i++)
                {
                    WriteEffect(_tempRoot, "adr0109_drain_" + i, png, FramesJson(smallRects));
                    ids.Add(new Id("sprite_anim.adr0109_drain_" + i));
                }

                var fired = 0;
                foreach (var id in ids)
                {
                    loader.LoadAsync(id, ResourceKind.Effect, (_, ok) =>
                    {
                        Assert.IsTrue(ok);
                        fired++;
                    });
                }

                // 不 Tick，等后台准备好全部完成项。
                yield return WaitFor(() => loader.PendingMainThreadCompletionCount == count,
                    "后台应当把全部完成项准备好", tickWith: null);

                loader.MainThreadBudgetMilliseconds = 1e-9;
                loader.Tick();
                Assert.AreEqual(1, loader.LastTickWorkUnitCount, "预算极小时每次 Tick 至少且至多推进一个工作单元");
                Assert.AreEqual(0, fired, "多帧资源的第一个工作单元做完时资源尚未完成，回调不得提前触发");
                Assert.IsFalse(loader.TryGetEffect(ids[0], out _), "未做完的资源对任何读取口都不可见（无半成品）");

                loader.MainThreadBudgetMilliseconds = 0;
                loader.Tick();
                Assert.AreEqual(count, fired, "预算 <= 0 表示不限：一次 Tick 排空全部完成项");
                Assert.AreEqual(0, loader.PendingMainThreadCompletionCount);
                foreach (var id in ids)
                {
                    Assert.IsTrue(loader.TryGetEffect(id, out _));
                }

                loader.MainThreadBudgetMilliseconds = 4.0;
            }

            // ---- ④ 热路径 --------------------------------------------------------------------------
            {
                var id = new Id("sprite_anim.adr0109_parity_" + PngKind.Rgba8.ToString().ToLowerInvariant());
                Assert.IsTrue(loader.TryGetEffect(id, out var before));
                var textureBefore = before.Frames[0].Sprite.texture;
                var decodedBefore = loader.ManagedDecodeCount + loader.MainThreadFallbackDecodeCount;

                // 该资源此前以"无提示"解码；这次带不同提示重新请求 => 复用缓存（ADR-0095 决策 5），
                // 不重新解码，并写一条冲突 Warn。
                LogAssert.Expect(LogType.Warning, new Regex("adr0109_parity_rgba8.*不同提示"));
                var reuseDone = false;
                loader.LoadAsync(id, ResourceKind.Effect, new ResourceLoadHints(new Id("sprite.adr0109_other_set")),
                    (_, ok) =>
                    {
                        Assert.IsTrue(ok);
                        reuseDone = true;
                    });
                yield return WaitFor(() => reuseDone, "复用缓存的请求应当回调", loader);

                Assert.IsTrue(loader.TryGetEffect(id, out var after));
                Assert.AreSame(before, after, "复用缓存不得产生新的 EffectAsset");
                Assert.AreSame(textureBefore, after.Frames[0].Sprite.texture, "复用缓存不得重新解码出新纹理");
                Assert.AreEqual(decodedBefore, loader.ManagedDecodeCount + loader.MainThreadFallbackDecodeCount,
                    "复用缓存不经过任何解码路径");
            }
        }

        /// <summary>中间一个不透明圆盘、其余全透明（alpha=0，RGB 非零）的 RGBA PNG。</summary>
        private static byte[] MakeMarginPng(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            var cx = (width - 1) / 2f;
            var cy = (height - 1) / 2f;
            var radius = System.Math.Min(width, height) * 0.2f;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var inside = (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius;
                    pixels[y * width + x] = inside
                        ? new Color32((byte)(40 + x * 2), (byte)(200 - y), (byte)(90 + x + y), 255)
                        : new Color32(200, 30, 60, 0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            var png = ImageConversion.EncodeToPNG(texture);
            Object.DestroyImmediate(texture);
            return png;
        }

        /// <summary>只核对"整矩形网格"这一点：textureRect 等于 rect、四个顶点。</summary>
        private static void AssertFullRectMeshOnly(Sprite sprite, string what)
        {
            Assert.AreEqual(sprite.rect.x, sprite.textureRect.x, 1e-3f, what + "：textureRect.x 应等于 rect.x（整矩形）");
            Assert.AreEqual(sprite.rect.y, sprite.textureRect.y, 1e-3f, what + "：textureRect.y");
            Assert.AreEqual(sprite.rect.width, sprite.textureRect.width, 1e-3f, what + "：textureRect.width");
            Assert.AreEqual(sprite.rect.height, sprite.textureRect.height, 1e-3f, what + "：textureRect.height");
            Assert.AreEqual(4, sprite.vertices.Length, what + "：整矩形网格应恰有 4 个顶点");
        }

        /// <summary>用同一张纹理、同一 rect/pivot/pixelsPerUnit 建一个"贴合轮廓"（<c>Sprite.Create</c> 默认网格，
        /// 即 1.87.0 的做法）参照精灵，逐项对拍，并把两者渲染到隔离层 RenderTexture 逐字节比像素。</summary>
        private static void AssertSpriteMatchesTightReference(Sprite sprite, string what)
        {
            const int isolationLayer = 30;
            var rect = sprite.rect;
            var normalizedPivot = new Vector2(sprite.pivot.x / rect.width, sprite.pivot.y / rect.height);
            var reference = Sprite.Create(sprite.texture, rect, normalizedPivot, sprite.pixelsPerUnit);
            var cameraGo = new GameObject("adr0109_probe_camera");
            var spriteGo = new GameObject("adr0109_probe_sprite") { layer = isolationLayer };
            var referenceGo = new GameObject("adr0109_probe_reference") { layer = isolationLayer };
            var renderTexture = new RenderTexture((int)rect.width, (int)rect.height, 0, RenderTextureFormat.ARGB32);
            try
            {
                AssertFullRectMeshOnly(sprite, what);
                Assert.Less(reference.textureRect.width * reference.textureRect.height, rect.width * rect.height,
                    what + "：夹具应使贴合轮廓参照网格小于整矩形，否则本对拍没有意义");

                Assert.AreEqual(reference.rect, sprite.rect, what + "：sprite.rect");
                Assert.AreEqual(reference.pivot.x, sprite.pivot.x, 1e-3f, what + "：pivot.x");
                Assert.AreEqual(reference.pivot.y, sprite.pivot.y, 1e-3f, what + "：pivot.y");
                Assert.AreEqual(reference.pixelsPerUnit, sprite.pixelsPerUnit, 1e-6f, what + "：pixelsPerUnit");
                Assert.AreEqual(reference.bounds.center.x, sprite.bounds.center.x, 1e-4f, what + "：bounds.center.x");
                Assert.AreEqual(reference.bounds.center.y, sprite.bounds.center.y, 1e-4f, what + "：bounds.center.y");
                Assert.AreEqual(reference.bounds.size.x, sprite.bounds.size.x, 1e-4f, what + "：bounds.size.x");
                Assert.AreEqual(reference.bounds.size.y, sprite.bounds.size.y, 1e-4f, what + "：bounds.size.y");

                var spriteRenderer = spriteGo.AddComponent<SpriteRenderer>();
                spriteRenderer.sprite = sprite;
                var referenceRenderer = referenceGo.AddComponent<SpriteRenderer>();
                referenceRenderer.sprite = reference;
                Assert.AreEqual(referenceRenderer.bounds.size.x, spriteRenderer.bounds.size.x, 1e-4f, what + "：SpriteRenderer.bounds.size.x");
                Assert.AreEqual(referenceRenderer.bounds.size.y, spriteRenderer.bounds.size.y, 1e-4f, what + "：SpriteRenderer.bounds.size.y");
                Assert.AreEqual(referenceRenderer.bounds.center.x, spriteRenderer.bounds.center.x, 1e-4f, what + "：SpriteRenderer.bounds.center.x");
                Assert.AreEqual(referenceRenderer.bounds.center.y, spriteRenderer.bounds.center.y, 1e-4f, what + "：SpriteRenderer.bounds.center.y");

                var camera = cameraGo.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = rect.height / sprite.pixelsPerUnit / 2f;
                camera.transform.position = new Vector3(
                    (0.5f - normalizedPivot.x) * rect.width / sprite.pixelsPerUnit,
                    (0.5f - normalizedPivot.y) * rect.height / sprite.pixelsPerUnit,
                    -10f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.cullingMask = 1 << isolationLayer;
                camera.targetTexture = renderTexture;
                camera.enabled = false;

                Color32[] Render(GameObject show, GameObject hide)
                {
                    show.SetActive(true);
                    hide.SetActive(false);
                    camera.Render();
                    var previous = RenderTexture.active;
                    RenderTexture.active = renderTexture;
                    var readback = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
                    readback.ReadPixels(new UnityEngine.Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                    readback.Apply();
                    RenderTexture.active = previous;
                    var pixels = readback.GetPixels32();
                    Object.DestroyImmediate(readback);
                    return pixels;
                }

                var actual = Render(spriteGo, referenceGo);
                var expected = Render(referenceGo, spriteGo);
                var visible = 0;
                foreach (var p in expected)
                {
                    if (p.a != 0)
                    {
                        visible++;
                    }
                }

                Assert.Greater(visible, 0, what + "：参照精灵渲染结果不应为空，否则像素对拍没有意义");
                AssertPixelsEqual(expected, actual, what + " 渲染像素");
            }
            finally
            {
                Object.DestroyImmediate(renderTexture);
                Object.DestroyImmediate(cameraGo);
                Object.DestroyImmediate(spriteGo);
                Object.DestroyImmediate(referenceGo);
                Object.DestroyImmediate(reference);
            }
        }

        private static Color32[] ToColor32(Color[] colors)
        {
            var result = new Color32[colors.Length];
            for (var i = 0; i < colors.Length; i++)
            {
                result[i] = colors[i];
            }

            return result;
        }

        private static void AssertPixelsEqual(Color32[] expected, Color32[] actual, string what)
        {
            Assert.AreEqual(expected.Length, actual.Length, what + "：像素数应一致");
            for (var i = 0; i < expected.Length; i++)
            {
                if (expected[i].r != actual[i].r || expected[i].g != actual[i].g ||
                    expected[i].b != actual[i].b || expected[i].a != actual[i].a)
                {
                    Assert.Fail($"{what}：第 {i} 个像素不一致，期望 {expected[i]}，实际 {actual[i]}");
                }
            }
        }
    }
}
