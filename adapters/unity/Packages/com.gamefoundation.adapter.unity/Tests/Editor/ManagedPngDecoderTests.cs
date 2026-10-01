#nullable enable
// ManagedPngDecoderTests：测试覆盖第四批 T-L15——ManagedPngDecoder（ADR-0109 后台化解码用的纯托管 PNG
// 解码器）此前仅被贴图用例间接经过。本文件直接对着它断言：
//  ① 正确性：自带一个最小 PNG 编码器（可指定每行滤波类型 0～4、RGB/RGBA），按规则生成确定像素，解码结果
//     必须逐字节等于“期望像素按引擎纹理行序（最下面一行为第 0 行）排列”——期望值由生成规则算出，不写裸数；
//  ② 与引擎解码器的逐像素一致性（类型头注释承诺的性质）：引擎 EncodeToPNG 产出的字节、以及本文件自编码的字节，
//     交给 Texture2D.LoadImage 得到的原始像素必须与托管解码结果相同；
//  ③ 不支持/损坏输入一律返回 false 并给出非空原因，绝不抛异常（调用方据此回退主线程解码）；
//  ④ 行流式接口：Begin 恰一次且先于所有行、行号自 h-1 递减到 0、接收器可中止。
// 本类型 internal，经 AssemblyInfo 的 InternalsVisibleTo(Adapter.Unity.Tests.Editor) 访问。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Adapter.Unity.EngineAdapter;
using NUnit.Framework;
using UnityEngine;

namespace Adapter.Unity.Tests.Editor
{
    public sealed class ManagedPngDecoderTests
    {
        // ------------------------------------------------------------------
        // 最小 PNG 编码器（测试基建，不是被测代码）
        // ------------------------------------------------------------------

        private const int ColorRgb = 2;
        private const int ColorRgba = 6;

        /// <summary>像素生成规则：通道 c（0=R,1=G,2=B,3=A）在 (x,y) 处的值。故意让相邻像素/相邻行有
        /// 相关性但不相等，使 Sub/Up/Average/Paeth 各滤波的还原路径都真正参与（全零或常数图会让多种
        /// 错误实现同样“通过”）。</summary>
        private static byte Channel(int x, int y, int c) => (byte)((x * 37 + y * 91 + c * 53 + (x * y) % 7) & 0xFF);

        private static byte[] BuildRawRows(int w, int h, int bpp)
        {
            var raw = new byte[w * h * bpp];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    for (var c = 0; c < bpp; c++)
                    {
                        raw[(y * w + x) * bpp + c] = Channel(x, y, c);
                    }
                }
            }

            return raw;
        }

        /// <summary>期望的解码输出：RGBA32，行序为引擎纹理行序（第 0 行 = 图像最下面一行），RGB 输入补 alpha=255。</summary>
        private static byte[] ExpectedRgba(int w, int h, int bpp)
        {
            var expected = new byte[w * h * 4];
            for (var y = 0; y < h; y++)
            {
                var outRow = h - 1 - y;
                for (var x = 0; x < w; x++)
                {
                    for (var c = 0; c < 3; c++)
                    {
                        expected[(outRow * w + x) * 4 + c] = Channel(x, y, c);
                    }

                    expected[(outRow * w + x) * 4 + 3] = bpp == 4 ? Channel(x, y, 3) : (byte)255;
                }
            }

            return expected;
        }

        private static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            var pa = Math.Abs(p - a);
            var pb = Math.Abs(p - b);
            var pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            return pb <= pc ? b : c;
        }

        /// <summary>对一行原始字节施加前向滤波（PNG 规范 §9）。</summary>
        private static byte[] FilterRow(int filterType, byte[] cur, byte[] prev, int bpp)
        {
            var o = new byte[cur.Length];
            for (var i = 0; i < cur.Length; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0;
                int b = prev[i];
                int c = i >= bpp ? prev[i - bpp] : 0;
                int predictor;
                switch (filterType)
                {
                    case 0: predictor = 0; break;
                    case 1: predictor = a; break;
                    case 2: predictor = b; break;
                    case 3: predictor = (a + b) >> 1; break;
                    case 4: predictor = Paeth(a, b, c); break;
                    default: throw new ArgumentOutOfRangeException(nameof(filterType));
                }

                o[i] = (byte)(cur[i] - predictor);
            }

            return o;
        }

        private static uint[]? _crcTable;

        private static uint Crc32(byte[] type, byte[] data)
        {
            if (_crcTable == null)
            {
                var table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    var c = n;
                    for (var k = 0; k < 8; k++)
                    {
                        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    }

                    table[n] = c;
                }

                _crcTable = table;
            }

            var crc = 0xFFFFFFFFu;
            foreach (var b in type) crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            foreach (var b in data) crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static void WriteBe(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24));
            s.WriteByte((byte)(v >> 16));
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            var typeBytes = new[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
            WriteBe(s, (uint)data.Length);
            s.Write(typeBytes, 0, 4);
            s.Write(data, 0, data.Length);
            WriteBe(s, Crc32(typeBytes, data));
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (var x in data)
            {
                a = (a + x) % 65521;
                b = (b + a) % 65521;
            }

            return (b << 16) | a;
        }

        private static byte[] Zlib(byte[] payload)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(0x78);
            ms.WriteByte(0x9C);
            using (var deflate = new DeflateStream(ms, CompressionMode.Compress, leaveOpen: true))
            {
                deflate.Write(payload, 0, payload.Length);
            }

            WriteBe(ms, Adler32(payload));
            return ms.ToArray();
        }

        private static byte[] IhdrData(int w, int h, int bitDepth, int colorType, int interlace = 0, int compression = 0, int filterMethod = 0)
        {
            using var ms = new MemoryStream();
            WriteBe(ms, (uint)w);
            WriteBe(ms, (uint)h);
            ms.WriteByte((byte)bitDepth);
            ms.WriteByte((byte)colorType);
            ms.WriteByte((byte)compression);
            ms.WriteByte((byte)filterMethod);
            ms.WriteByte((byte)interlace);
            return ms.ToArray();
        }

        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>组装一张 PNG。<paramref name="filterForRow"/> 给出第 y 行使用的滤波类型。</summary>
        private static byte[] BuildPng(int w, int h, int colorType, Func<int, int> filterForRow,
            bool addTrns = false, int bitDepth = 8, int interlace = 0)
        {
            var bpp = colorType == ColorRgba ? 4 : 3;
            var raw = BuildRawRows(w, h, bpp);
            var stride = w * bpp;
            var filtered = new byte[h * (stride + 1)];
            var prev = new byte[stride];
            for (var y = 0; y < h; y++)
            {
                var cur = new byte[stride];
                Buffer.BlockCopy(raw, y * stride, cur, 0, stride);
                var filterType = filterForRow(y);
                filtered[y * (stride + 1)] = (byte)filterType;
                Buffer.BlockCopy(FilterRow(filterType, cur, prev, bpp), 0, filtered, y * (stride + 1) + 1, stride);
                prev = cur;
            }

            using var png = new MemoryStream();
            png.Write(Signature, 0, Signature.Length);
            WriteChunk(png, "IHDR", IhdrData(w, h, bitDepth, colorType, interlace));
            if (addTrns)
            {
                WriteChunk(png, "tRNS", new byte[] { 0, 0, 0, 0, 0, 0 });
            }

            WriteChunk(png, "IDAT", Zlib(filtered));
            WriteChunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        private static byte[] SinglePng(int w, int h, int colorType, int filterType) =>
            BuildPng(w, h, colorType, _ => filterType);

        // ------------------------------------------------------------------
        // ① 正确性
        // ------------------------------------------------------------------

        [Test]
        public void Rgba_EveryFilterType_DecodesToRuleComputedPixels_InEngineRowOrder(
            [Values(0, 1, 2, 3, 4)] int filterType)
        {
            const int w = 9, h = 7;
            var png = SinglePng(w, h, ColorRgba, filterType);

            Assert.IsTrue(ManagedPngDecoder.TryDecode(png, out var dw, out var dh, out var rgba, out var reason), reason);
            Assert.AreEqual(w, dw);
            Assert.AreEqual(h, dh);
            Assert.AreEqual(w * h * 4, rgba.Length, "输出长度恒为 width*height*4");
            CollectionAssert.AreEqual(ExpectedRgba(w, h, 4), rgba);
        }

        [Test]
        public void Rgb_EveryFilterType_DecodesWithOpaqueAlphaAdded(
            [Values(0, 1, 2, 3, 4)] int filterType)
        {
            const int w = 8, h = 6;
            var png = SinglePng(w, h, ColorRgb, filterType);

            Assert.IsTrue(ManagedPngDecoder.TryDecode(png, out var dw, out var dh, out var rgba, out var reason), reason);
            Assert.AreEqual(w, dw);
            Assert.AreEqual(h, dh);
            CollectionAssert.AreEqual(ExpectedRgba(w, h, 3), rgba);
            for (var i = 3; i < rgba.Length; i += 4)
            {
                Assert.AreEqual(255, rgba[i], $"RGB 输入的 alpha 恒补 255（像素 {i / 4}）");
            }
        }

        [Test]
        public void MixedFilterTypesAcrossRows_EachRowRestoredAgainstItsOwnPreviousRow()
        {
            const int w = 11, h = 15;   // 行数是 5 的整数倍，轮换覆盖全部滤波且相邻行类型各不相同
            var png = BuildPng(w, h, ColorRgba, y => y % 5);

            Assert.IsTrue(ManagedPngDecoder.TryDecode(png, out _, out _, out var rgba, out var reason), reason);
            CollectionAssert.AreEqual(ExpectedRgba(w, h, 4), rgba);
        }

        [Test]
        public void SinglePixelImage_AndSingleColumn_AndSingleRow_AreHandled(
            [Values(ColorRgb, ColorRgba)] int colorType,
            [Values(0, 1, 2, 3, 4)] int filterType)
        {
            foreach (var (w, h) in new[] { (1, 1), (1, 5), (5, 1) })
            {
                var png = SinglePng(w, h, colorType, filterType);
                var bpp = colorType == ColorRgba ? 4 : 3;
                Assert.IsTrue(ManagedPngDecoder.TryDecode(png, out var dw, out var dh, out var rgba, out var reason), $"{w}x{h}: {reason}");
                Assert.AreEqual((w, h), (dw, dh));
                CollectionAssert.AreEqual(ExpectedRgba(w, h, bpp), rgba, $"{w}x{h} filter {filterType}");
            }
        }

        [Test]
        public void LargerImage_WithinPixelLimit_Decodes()
        {
            const int w = 257, h = 129;   // 奇数尺寸，跨多个 deflate 块
            var png = BuildPng(w, h, ColorRgba, y => y % 5);

            Assert.IsTrue(ManagedPngDecoder.TryDecode(png, out var dw, out var dh, out var rgba, out var reason), reason);
            Assert.AreEqual((w, h), (dw, dh));
            CollectionAssert.AreEqual(ExpectedRgba(w, h, 4), rgba);
        }

        [Test]
        public void MultipleIdatChunks_AreConcatenatedBeforeInflate()
        {
            const int w = 6, h = 6;
            var whole = BuildPng(w, h, ColorRgba, y => y % 5);
            var split = SplitFirstIdatIntoTwoChunks(whole);

            Assert.Greater(split.Length, whole.Length, "拆成两个 IDAT 块后多出一个块头/尾");
            Assert.IsTrue(ManagedPngDecoder.TryDecode(split, out _, out _, out var rgba, out var reason), reason);
            CollectionAssert.AreEqual(ExpectedRgba(w, h, 4), rgba);
        }

        /// <summary>把第一个 IDAT 块的数据对半拆成两个 IDAT 块（块头/CRC 重算）。</summary>
        private static byte[] SplitFirstIdatIntoTwoChunks(byte[] png)
        {
            var pos = Signature.Length;
            using var result = new MemoryStream();
            result.Write(png, 0, Signature.Length);
            while (pos + 12 <= png.Length)
            {
                var len = (int)(((uint)png[pos] << 24) | ((uint)png[pos + 1] << 16) | ((uint)png[pos + 2] << 8) | png[pos + 3]);
                var type = "" + (char)png[pos + 4] + (char)png[pos + 5] + (char)png[pos + 6] + (char)png[pos + 7];
                var data = new byte[len];
                Buffer.BlockCopy(png, pos + 8, data, 0, len);
                if (type == "IDAT")
                {
                    var half = len / 2;
                    var a = new byte[half];
                    var b = new byte[len - half];
                    Buffer.BlockCopy(data, 0, a, 0, half);
                    Buffer.BlockCopy(data, half, b, 0, len - half);
                    WriteChunk(result, "IDAT", a);
                    WriteChunk(result, "IDAT", b);
                }
                else
                {
                    WriteChunk(result, type, data);
                }

                pos += 12 + len;
            }

            return result.ToArray();
        }

        [Test]
        public void IgnoredAncillaryChunks_BetweenHeaderAndData_DoNotAffectPixels()
        {
            const int w = 5, h = 4;
            var png = BuildPng(w, h, ColorRgba, y => y % 5);
            // 在 IHDR 之后插入 gAMA 与一个未知辅助块（类型字节小写 = 辅助，规范允许忽略）。
            using var ms = new MemoryStream();
            ms.Write(png, 0, Signature.Length);
            var pos = Signature.Length;
            var ihdrLen = 13 + 12;
            ms.Write(png, pos, ihdrLen);
            WriteChunk(ms, "gAMA", new byte[] { 0, 1, 0x86, 0xA0 });
            WriteChunk(ms, "tEXt", new byte[] { (byte)'k', 0, (byte)'v' });
            ms.Write(png, pos + ihdrLen, png.Length - pos - ihdrLen);

            Assert.IsTrue(ManagedPngDecoder.TryDecode(ms.ToArray(), out _, out _, out var rgba, out var reason), reason);
            CollectionAssert.AreEqual(ExpectedRgba(w, h, 4), rgba);
        }

        // ------------------------------------------------------------------
        // ② 与引擎解码器逐像素一致
        // ------------------------------------------------------------------

        /// <summary>引擎纹理的像素按 RGBA32 字节、引擎行序（第 0 行最下）取出；用 GetPixels32 而不是
        /// GetRawTextureData，免得依赖 LoadImage 之后纹理的内部存储格式。</summary>
        private static byte[] EnginePixels(Texture2D tex)
        {
            var pixels = tex.GetPixels32();
            var bytes = new byte[pixels.Length * 4];
            for (var i = 0; i < pixels.Length; i++)
            {
                bytes[i * 4] = pixels[i].r;
                bytes[i * 4 + 1] = pixels[i].g;
                bytes[i * 4 + 2] = pixels[i].b;
                bytes[i * 4 + 3] = pixels[i].a;
            }

            return bytes;
        }

        [Test]
        public void EngineEncodedPng_DecodesBackToTheOriginalPixels_AndMatchesEngineLoadImage()
        {
            const int w = 13, h = 9;
            var original = ExpectedRgba(w, h, 4);   // 引擎纹理行序的 RGBA32
            var source = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Texture2D? reloaded = null;
            try
            {
                source.SetPixelData(original, 0);
                source.Apply(false);
                var pngBytes = source.EncodeToPNG();
                Assert.IsNotNull(pngBytes);
                Assert.Greater(pngBytes.Length, 0);

                Assert.IsTrue(ManagedPngDecoder.TryDecode(pngBytes, out var dw, out var dh, out var managed, out var reason), reason);
                Assert.AreEqual((w, h), (dw, dh));
                CollectionAssert.AreEqual(original, managed, "托管解码结果应逐字节等于编码前的像素");

                reloaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                Assert.IsTrue(reloaded.LoadImage(pngBytes), "引擎应能解码同一份字节");
                CollectionAssert.AreEqual(EnginePixels(reloaded), managed, "托管解码应与引擎 LoadImage 逐像素一致（纹理格式 " + reloaded.format + "）");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                if (reloaded != null) UnityEngine.Object.DestroyImmediate(reloaded);
            }
        }

        [Test]
        public void SelfEncodedRgbaPng_ManagedDecodeMatchesEngineLoadImage_ForEveryFilterType(
            [Values(0, 1, 2, 3, 4)] int filterType)
        {
            const int w = 10, h = 8;
            var png = SinglePng(w, h, ColorRgba, filterType);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(tex.LoadImage(png), "引擎应能解码本文件自编码的 PNG（编码器本身正确）");
                Assert.IsTrue(ManagedPngDecoder.TryDecode(png, out _, out _, out var managed, out var reason), reason);
                CollectionAssert.AreEqual(EnginePixels(tex), managed, "纹理格式 " + tex.format);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        // ------------------------------------------------------------------
        // ③ 不支持/损坏输入：返回 false + 非空原因，不抛异常
        // ------------------------------------------------------------------

        private static void AssertRejected(byte[]? png, string reasonKeyword, string because)
        {
            Assert.DoesNotThrow(() => ManagedPngDecoder.TryDecode(png, out _, out _, out _, out _), because);
            var ok = ManagedPngDecoder.TryDecode(png, out var w, out var h, out var rgba, out var reason);
            Assert.IsFalse(ok, because);
            Assert.IsNotEmpty(reason, "失败必须给出可读原因（供回退诊断）：" + because);
            StringAssert.Contains(reasonKeyword, reason, because);
            Assert.AreEqual(0, w);
            Assert.AreEqual(0, h);
            Assert.AreEqual(0, rgba.Length, "失败时不返回部分像素");
        }

        [Test]
        public void NullAndTooShortInput_Rejected()
        {
            AssertRejected(null, "字节数不足", "null");
            AssertRejected(Array.Empty<byte>(), "字节数不足", "空数组");
            AssertRejected(new byte[Signature.Length + 11], "字节数不足", "不足一个块的最小长度");
        }

        [Test]
        public void WrongSignature_Rejected()
        {
            var png = SinglePng(2, 2, ColorRgba, 0);
            png[1] = (byte)'X';
            AssertRejected(png, "签名", "签名被破坏");
        }

        [Test]
        public void TruncatedChunk_Rejected()
        {
            var png = SinglePng(4, 4, ColorRgba, 0);
            var cut = new byte[png.Length - 20];   // 切到 IDAT 中间
            Buffer.BlockCopy(png, 0, cut, 0, cut.Length);
            AssertRejected(cut, "截断", "数据块被截断");
        }

        [Test]
        public void MissingIhdr_Rejected()
        {
            using var ms = new MemoryStream();
            ms.Write(Signature, 0, Signature.Length);
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            WriteChunk(ms, "IEND", Array.Empty<byte>());   // 凑够最小长度
            AssertRejected(ms.ToArray(), "IHDR", "缺少 IHDR");
        }

        [Test]
        public void IhdrWithWrongLength_Rejected()
        {
            using var ms = new MemoryStream();
            ms.Write(Signature, 0, Signature.Length);
            WriteChunk(ms, "IHDR", new byte[12]);
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            AssertRejected(ms.ToArray(), "IHDR", "IHDR 长度必须为 13");
        }

        [TestCase(0, 1)]
        [TestCase(1, 0)]
        public void ZeroWidthOrHeight_Rejected(int w, int h)
        {
            AssertRejected(BuildFromHeaderOnly(w, h, 8, ColorRgba), "尺寸", $"{w}x{h}");
        }

        [Test]
        public void NonStandardCompressionOrFilterMethod_Rejected()
        {
            using var a = new MemoryStream();
            a.Write(Signature, 0, Signature.Length);
            WriteChunk(a, "IHDR", IhdrData(2, 2, 8, ColorRgba, compression: 1));
            WriteChunk(a, "IEND", Array.Empty<byte>());
            AssertRejected(a.ToArray(), "非标准", "压缩方法 != 0");

            using var b = new MemoryStream();
            b.Write(Signature, 0, Signature.Length);
            WriteChunk(b, "IHDR", IhdrData(2, 2, 8, ColorRgba, filterMethod: 1));
            WriteChunk(b, "IEND", Array.Empty<byte>());
            AssertRejected(b.ToArray(), "非标准", "滤波方法 != 0");
        }

        private static byte[] BuildFromHeaderOnly(int w, int h, int bitDepth, int colorType, int interlace = 0)
        {
            using var ms = new MemoryStream();
            ms.Write(Signature, 0, Signature.Length);
            WriteChunk(ms, "IHDR", IhdrData(w, h, bitDepth, colorType, interlace));
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        [TestCase(1)]
        [TestCase(4)]
        [TestCase(16)]
        public void BitDepthOtherThan8_Rejected(int bitDepth)
        {
            AssertRejected(BuildFromHeaderOnly(2, 2, bitDepth, ColorRgba), "位深", $"bitDepth={bitDepth}");
        }

        [TestCase(0)]   // 灰度
        [TestCase(3)]   // 调色板
        [TestCase(4)]   // 灰度+alpha
        public void ColorTypesOtherThanRgbOrRgba_Rejected(int colorType)
        {
            AssertRejected(BuildFromHeaderOnly(2, 2, 8, colorType), "颜色类型", $"colorType={colorType}");
        }

        [Test]
        public void InterlacedPng_Rejected()
        {
            AssertRejected(BuildFromHeaderOnly(2, 2, 8, ColorRgba, interlace: 1), "隔行", "Adam7 隔行");
        }

        [Test]
        public void RgbWithTrnsColorKey_Rejected_ButRgbaWithTrnsChunkStillDecodes()
        {
            // 颜色类型 2 + tRNS = 颜色键透明：像素语义需要把键色改成透明，托管解码器不猜测，交回引擎。
            var rgbWithKey = BuildPng(3, 3, ColorRgb, _ => 0, addTrns: true);
            AssertRejected(rgbWithKey, "tRNS", "RGB + tRNS");

            // 颜色类型 6 已经自带 alpha，tRNS 块被忽略（规范上对 RGBA 本就无意义），不应拒绝。
            var rgbaWithTrns = BuildPng(3, 3, ColorRgba, _ => 0, addTrns: true);
            Assert.IsTrue(ManagedPngDecoder.TryDecode(rgbaWithTrns, out _, out _, out var rgba, out var reason), reason);
            CollectionAssert.AreEqual(ExpectedRgba(3, 3, 4), rgba);
        }

        [Test]
        public void PixelCountOverLimit_Rejected_BeforeAnyAllocation_AndExactlyAtLimitIsNotRejectedForSize()
        {
            // 8193 x 8192 > MaxPixels（8192 x 8192）：仅凭 IHDR 就应拒绝，不需要也不应分配像素缓冲。
            AssertRejected(BuildFromHeaderOnly(8193, 8192, 8, ColorRgba), "像素数", "超过上限");

            // 恰好等于上限的尺寸不因“像素数”被拒——它会因为缺少 IDAT 被拒（原因不同，证明过了尺寸关）。
            var ok = ManagedPngDecoder.TryDecode(BuildFromHeaderOnly(8192, 8192, 8, ColorRgba), out _, out _, out _, out var reason);
            Assert.IsFalse(ok);
            StringAssert.DoesNotContain("像素数", reason);
            StringAssert.Contains("IDAT", reason);
        }

        [Test]
        public void MissingIdat_Rejected()
        {
            AssertRejected(BuildFromHeaderOnly(2, 2, 8, ColorRgba), "IDAT", "只有 IHDR+IEND");
        }

        [Test]
        public void UnsupportedZlibHeader_Rejected()
        {
            using var ms = new MemoryStream();
            ms.Write(Signature, 0, Signature.Length);
            WriteChunk(ms, "IHDR", IhdrData(2, 2, 8, ColorRgba));
            WriteChunk(ms, "IDAT", new byte[] { 0x79, 0x9C, 0x00 });   // CMF 低 4 位 = 9，不是 deflate(8)
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            AssertRejected(ms.ToArray(), "zlib", "CMF 非 deflate");

            using var dict = new MemoryStream();
            dict.Write(Signature, 0, Signature.Length);
            WriteChunk(dict, "IHDR", IhdrData(2, 2, 8, ColorRgba));
            WriteChunk(dict, "IDAT", new byte[] { 0x78, 0x20 | 0x01, 0x00 });   // FDICT 置位
            WriteChunk(dict, "IEND", Array.Empty<byte>());
            AssertRejected(dict.ToArray(), "zlib", "FDICT 置位");
        }

        [Test]
        public void IdatTooShortToHoldZlibHeader_Rejected()
        {
            using var ms = new MemoryStream();
            ms.Write(Signature, 0, Signature.Length);
            WriteChunk(ms, "IHDR", IhdrData(2, 2, 8, ColorRgba));
            WriteChunk(ms, "IDAT", new byte[] { 0x78, 0x9C });
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            AssertRejected(ms.ToArray(), "IDAT", "IDAT 不足 3 字节");
        }

        [Test]
        public void InflatedDataShorterThanDeclaredHeight_Rejected()
        {
            // IHDR 声明 4 行，但 IDAT 只压了 2 行的数据。
            const int w = 3, declaredH = 4, realH = 2;
            var shortPng = BuildPng(w, realH, ColorRgba, _ => 0);
            var patched = PatchHeight(shortPng, declaredH);
            AssertRejected(patched, "不足", "解压后数据行数少于 IHDR 声明");
        }

        private static byte[] PatchHeight(byte[] png, int newHeight)
        {
            var copy = (byte[])png.Clone();
            var ihdrDataPos = Signature.Length + 8;
            var heightPos = ihdrDataPos + 4;
            copy[heightPos] = (byte)(newHeight >> 24);
            copy[heightPos + 1] = (byte)(newHeight >> 16);
            copy[heightPos + 2] = (byte)(newHeight >> 8);
            copy[heightPos + 3] = (byte)newHeight;
            return copy;   // 解码器不校验 CRC（类型头已知限制①），补丁后无需重算
        }

        [Test]
        public void UnknownRowFilterType_Rejected()
        {
            const int w = 3, h = 2;
            var png = BuildPng(w, h, ColorRgba, _ => 0);
            // 重新压缩一份把第 0 行滤波字节改成 5 的数据。
            var stride = w * 4;
            var payload = new byte[h * (stride + 1)];
            payload[0] = 5;
            using var ms = new MemoryStream();
            ms.Write(Signature, 0, Signature.Length);
            WriteChunk(ms, "IHDR", IhdrData(w, h, 8, ColorRgba));
            WriteChunk(ms, "IDAT", Zlib(payload));
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            Assert.IsNotNull(png);
            AssertRejected(ms.ToArray(), "滤波", "滤波类型 5 不是规范值");
        }

        [Test]
        public void CorruptDeflateStream_DoesNotThrow_ReturnsFalseWithReason()
        {
            using var ms = new MemoryStream();
            ms.Write(Signature, 0, Signature.Length);
            WriteChunk(ms, "IHDR", IhdrData(4, 4, 8, ColorRgba));
            var garbage = new byte[64];
            new System.Random(12345).NextBytes(garbage);
            garbage[0] = 0x78;
            garbage[1] = 0x9C;
            WriteChunk(ms, "IDAT", garbage);
            WriteChunk(ms, "IEND", Array.Empty<byte>());

            Assert.DoesNotThrow(() => ManagedPngDecoder.TryDecode(ms.ToArray(), out _, out _, out _, out _));
            Assert.IsFalse(ManagedPngDecoder.TryDecode(ms.ToArray(), out _, out _, out _, out var reason));
            Assert.IsNotEmpty(reason);
        }

        // ------------------------------------------------------------------
        // ④ 行流式接口
        // ------------------------------------------------------------------

        private sealed class RecordingSink : IRowSink
        {
            public readonly List<string> Events = new List<string>();
            public readonly List<int> RowNumbers = new List<int>();
            public readonly List<byte[]> RowCopies = new List<byte[]>();
            public int Width;
            public int Height;
            public bool AllowBegin = true;
            public string BeginReason = "接收器拒绝";

            public bool Begin(int width, int height, out string reason)
            {
                Events.Add("begin");
                Width = width;
                Height = height;
                reason = AllowBegin ? string.Empty : BeginReason;
                return AllowBegin;
            }

            public void WriteRow(int rowFromBottom, byte[] rgba)
            {
                Events.Add("row");
                RowNumbers.Add(rowFromBottom);
                var copy = new byte[Width * 4];
                Buffer.BlockCopy(rgba, 0, copy, 0, copy.Length);   // 缓冲会被复用，必须当场拷贝
                RowCopies.Add(copy);
            }
        }

        [Test]
        public void StreamingSink_BeginOnceBeforeRows_RowsFromTopOfEngineTextureDownTo0_EachRowMatchesExpectation()
        {
            const int w = 7, h = 6;
            var png = BuildPng(w, h, ColorRgb, y => y % 5);
            var sink = new RecordingSink();

            Assert.IsTrue(ManagedPngDecoder.TryDecode(png, sink, out var reason), reason);

            Assert.AreEqual((w, h), (sink.Width, sink.Height));
            Assert.AreEqual("begin", sink.Events[0]);
            Assert.AreEqual(1, sink.Events.FindAll(e => e == "begin").Count, "Begin 恰好一次");
            Assert.AreEqual(h, sink.RowNumbers.Count);
            var expectedOrder = new List<int>();
            for (var y = 0; y < h; y++) expectedOrder.Add(h - 1 - y);   // PNG 第 0 行 = 引擎最上面一行
            CollectionAssert.AreEqual(expectedOrder, sink.RowNumbers);

            var expected = ExpectedRgba(w, h, 3);
            for (var i = 0; i < sink.RowNumbers.Count; i++)
            {
                var rowFromBottom = sink.RowNumbers[i];
                var expectedRow = new byte[w * 4];
                Buffer.BlockCopy(expected, rowFromBottom * w * 4, expectedRow, 0, w * 4);
                CollectionAssert.AreEqual(expectedRow, sink.RowCopies[i], $"第 {i} 次回调（行号 {rowFromBottom}）");
            }
        }

        [Test]
        public void StreamingSink_RefusingBegin_AbortsBeforeAnyRow_AndItsReasonIsReturnedVerbatim()
        {
            var png = BuildPng(4, 4, ColorRgba, _ => 0);
            var sink = new RecordingSink { AllowBegin = false, BeginReason = "目标图集放不下" };

            Assert.IsFalse(ManagedPngDecoder.TryDecode(png, sink, out var reason));
            Assert.AreEqual("目标图集放不下", reason);
            Assert.AreEqual(new[] { "begin" }, sink.Events.ToArray(), "拒绝后不得再收到任何行");
        }

        [Test]
        public void StreamingSink_NotCalledAtAll_WhenHeaderIsUnsupported()
        {
            var sink = new RecordingSink();
            Assert.IsFalse(ManagedPngDecoder.TryDecode(BuildFromHeaderOnly(2, 2, 16, ColorRgba), sink, out _));
            Assert.IsEmpty(sink.Events, "头部检查失败时连 Begin 都不应调用");
        }
    }
}
