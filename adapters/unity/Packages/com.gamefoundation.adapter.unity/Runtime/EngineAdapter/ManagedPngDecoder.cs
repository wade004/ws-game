#nullable enable
// ManagedPngDecoder：[ADR-0109](../../../../../../../architecture/adr/0109-资源解码分帧与后台化.md)
// 新增，供 UnityResourceLoader 在后台线程把 PNG 字节解成 RGBA32 像素——Texture2D.LoadImage 只能在
// 主线程调用，是首次换向长帧（消费方反馈第五十八批）里占比最大的一项；本解码器是纯托管实现，不
// 引用任何引擎 API，可在任意线程运行。
//
// 支持范围（写死，其它一律返回 false 由调用方回退主线程 LoadImage）：8 位深、非隔行、颜色类型
// RGB(2) 与 RGBA(6)。调色板/灰度/灰度加透明/16 位/隔行，以及颜色类型 2 携带 tRNS（颜色键透明）
// 的文件都不支持——这些变体的像素语义（调色板展开、位深缩放、颜色键、Adam7 重排）与引擎自带解码
// 器的取舍细节较多，宁可回退到引擎解码器保证与 1.87.0 逐像素一致，也不在这里猜测。
//
// 输出约定：RGBA32 字节，行序与引擎纹理一致（第 0 行是图像最下面一行，原点左下），RGB 输入补
// alpha=255，可直接交给 Texture2D.SetPixelData(byte[], 0)。
//
// 流式解码（判断记录）：解压与行滤波还原按"一行一行"进行，还原出的每一行 RGBA 直接交给
// <see cref="IRowSink"/>，不在托管堆上分配"整张解压缓冲 + 整张输出"两份大数组——实测（本批
// 第一版，整张缓冲）在一个方向 30 个 2048x2048 图集的压力下，大数组分配触发的整堆 GC 停顿把主线程
// 单个 Tick 顶到 45～105 ms；改为行流式并让逐帧动画的切块直接由行接收器完成后，堆上只剩最终要交给
// 主线程的像素块。
//
// 完整性校验与边界（ADR-0109）：
// ①校验：本解码器读取的每个数据块（IHDR/IDAT/tRNS/IEND）都核对 CRC-32，IDAT 拼接后的 zlib 流尾部
//   Adler-32 与解压出的全部字节（含各行滤波类型字节）逐一核对；任一不符按"损坏"返回 false，调用方
//   （UnityResourceLoader 后台解码）因此回退到引擎主线程解码，由引擎给出权威结论，不会"解出"错误像素。
//   未读取的辅助块（gAMA/sRGB/iCCP/cHRM/tEXt 等）不校验 CRC——它们不影响像素，与引擎对辅助块损坏
//   只告警不失败的处理一致。
// ②像素数上限 <see cref="MaxPixels"/>（设计决定）：超过上限不做后台托管解码、回退引擎解码。理由：后台解码
//   每行流式写出但一张图的像素块最终要驻留内存交给主线程，8192x8192 RGBA 已是 256 MB，再大的图让引擎
//   一次性解码并由其内存策略兜底更稳，不值得为超大图扩大托管解码的内存足迹。
// ③只读取 IHDR/IDAT/tRNS/IEND，其余辅助块一律忽略（设计决定，有引擎对照用例背书）：色彩管理块不改变
//   LoadImage 输出的字节，ManagedPngDecoderTests.AncillaryColorManagementChunks_ManagedDecodeMatchesEngineLoadImage
//   把带 gAMA/sRGB/iCCP/cHRM 块的 PNG 同时交给两个解码器逐像素比对。
using System;
using System.IO;
using System.IO.Compression;

namespace Adapter.Unity.EngineAdapter
{
    /// <summary>逐行接收 <see cref="ManagedPngDecoder"/> 解出的 RGBA32 行。</summary>
    internal interface IRowSink
    {
        /// <summary>图像头解析完成后、第一行之前调用一次。返回 false 中止解码（<paramref name="reason"/>
        /// 说明原因，解码器把它原样作为失败原因返回）。</summary>
        bool Begin(int width, int height, out string reason);

        /// <summary>交付一行。<paramref name="rowFromBottom"/> 是该行在引擎纹理里的行号（0 = 最下面一行）；
        /// <paramref name="rgba"/> 的前 width*4 字节是该行 RGBA32 像素，调用返回后缓冲会被复用，接收器不得
        /// 保留引用。</summary>
        void WriteRow(int rowFromBottom, byte[] rgba);
    }

    internal static class ManagedPngDecoder
    {
        /// <summary>单张图像像素数上限（8192 x 8192）。超过时不做后台托管解码，回退引擎解码。</summary>
        internal const long MaxPixels = 8192L * 8192L;

        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>整张解码：成功返回 true 并给出 <paramref name="width"/>/<paramref name="height"/>/
        /// <paramref name="rgba"/>（长度恒为 width*height*4）。失败原因与语义同
        /// <see cref="TryDecode(byte[],IRowSink,out string)"/>。不抛异常。</summary>
        internal static bool TryDecode(byte[]? png, out int width, out int height, out byte[] rgba, out string reason)
        {
            var sink = new WholeImageSink();
            if (TryDecode(png, sink, out reason))
            {
                width = sink.Width;
                height = sink.Height;
                rgba = sink.Pixels!;
                return true;
            }

            width = 0;
            height = 0;
            rgba = Array.Empty<byte>();
            return false;
        }

        /// <summary>
        /// 流式解码 PNG，每行交给 <paramref name="sink"/>。不支持的变体、数据异常、或接收器中止返回
        /// false，<paramref name="reason"/> 给出可读原因（供回退诊断）。不抛异常。
        /// </summary>
        internal static bool TryDecode(byte[]? png, IRowSink sink, out string reason)
        {
            reason = string.Empty;
            try
            {
                return DecodeCore(png, sink, out reason);
            }
            catch (Exception e)
            {
                reason = "托管 PNG 解码抛出异常：" + e.GetType().Name + " " + e.Message;
                return false;
            }
        }

        private sealed class WholeImageSink : IRowSink
        {
            public int Width;
            public int Height;
            public byte[]? Pixels;

            public bool Begin(int width, int height, out string reason)
            {
                Width = width;
                Height = height;
                Pixels = new byte[width * height * 4];
                reason = string.Empty;
                return true;
            }

            public void WriteRow(int rowFromBottom, byte[] rgba)
            {
                Buffer.BlockCopy(rgba, 0, Pixels!, rowFromBottom * Width * 4, Width * 4);
            }
        }

        private static bool DecodeCore(byte[]? png, IRowSink sink, out string reason)
        {
            reason = string.Empty;

            if (png == null || png.Length < Signature.Length + 12)
            {
                reason = "字节数不足以构成 PNG";
                return false;
            }

            for (var i = 0; i < Signature.Length; i++)
            {
                if (png[i] != Signature[i])
                {
                    reason = "PNG 文件签名不匹配";
                    return false;
                }
            }

            var haveHeader = false;
            var w = 0;
            var h = 0;
            var bitDepth = 0;
            var colorType = 0;
            var interlace = 0;
            var hasTrns = false;
            MemoryStream? idat = null;

            var pos = Signature.Length;
            while (pos + 12 <= png.Length)
            {
                var chunkLength = ReadUInt32(png, pos);
                var dataPos = pos + 8;
                if (chunkLength > int.MaxValue || dataPos + (long)chunkLength + 4 > png.Length)
                {
                    reason = "PNG 数据块被截断";
                    return false;
                }

                var len = (int)chunkLength;
                var t0 = png[pos + 4];
                var t1 = png[pos + 5];
                var t2 = png[pos + 6];
                var t3 = png[pos + 7];

                var isIhdr = t0 == 'I' && t1 == 'H' && t2 == 'D' && t3 == 'R';
                var isIdat = t0 == 'I' && t1 == 'D' && t2 == 'A' && t3 == 'T';
                var isTrns = t0 == 't' && t1 == 'R' && t2 == 'N' && t3 == 'S';
                var isIend = t0 == 'I' && t1 == 'E' && t2 == 'N' && t3 == 'D';
                if ((isIhdr || isIdat || isTrns || isIend) &&
                    Crc32(png, pos + 4, len + 4) != ReadUInt32(png, dataPos + len))
                {
                    reason = "PNG 数据块 CRC 校验失败（" + (char)t0 + (char)t1 + (char)t2 + (char)t3 + "）";
                    return false;
                }

                if (isIhdr)
                {
                    if (len != 13)
                    {
                        reason = "IHDR 长度异常";
                        return false;
                    }

                    var uw = ReadUInt32(png, dataPos);
                    var uh = ReadUInt32(png, dataPos + 4);
                    if (uw == 0 || uh == 0 || uw > int.MaxValue || uh > int.MaxValue)
                    {
                        reason = "图像尺寸非法";
                        return false;
                    }

                    w = (int)uw;
                    h = (int)uh;
                    bitDepth = png[dataPos + 8];
                    colorType = png[dataPos + 9];
                    var compression = png[dataPos + 10];
                    var filterMethod = png[dataPos + 11];
                    interlace = png[dataPos + 12];
                    if (compression != 0 || filterMethod != 0)
                    {
                        reason = "压缩/滤波方法非标准值";
                        return false;
                    }

                    haveHeader = true;
                }
                else if (isIdat)
                {
                    idat ??= new MemoryStream();
                    idat.Write(png, dataPos, len);
                }
                else if (isTrns)
                {
                    hasTrns = true;
                }
                else if (isIend)
                {
                    break;
                }

                pos = dataPos + len + 4;
            }

            if (!haveHeader)
            {
                reason = "缺少 IHDR";
                return false;
            }

            if (bitDepth != 8)
            {
                reason = "位深 " + bitDepth + " 不受支持（仅支持 8 位）";
                return false;
            }

            if (colorType != 2 && colorType != 6)
            {
                reason = "颜色类型 " + colorType + " 不受支持（仅支持 RGB(2)/RGBA(6)）";
                return false;
            }

            if (interlace != 0)
            {
                reason = "隔行扫描 PNG 不受支持";
                return false;
            }

            if (colorType == 2 && hasTrns)
            {
                reason = "RGB PNG 携带 tRNS（颜色键透明）不受支持";
                return false;
            }

            if ((long)w * h > MaxPixels)
            {
                reason = "像素数超过后台解码上限";
                return false;
            }

            if (idat == null || idat.Length < 3)
            {
                reason = "缺少 IDAT 数据";
                return false;
            }

            var idatBuffer = idat.GetBuffer();
            var idatLength = (int)idat.Length;

            // zlib 头：CMF 低 4 位必须是 8（deflate），FLG 的 FDICT 位（0x20）不能置位；头两字节之后
            // 直接是 deflate 数据；尾部 4 字节是 Adler-32，解压完成后与解压出的全部字节核对（见类型顶部①）。
            if ((idatBuffer[0] & 0x0F) != 8 || (idatBuffer[1] & 0x20) != 0)
            {
                reason = "zlib 头不受支持";
                return false;
            }

            if (idatLength < 6)
            {
                reason = "IDAT 不足以容纳 zlib 尾部校验和";
                return false;
            }

            if (!sink.Begin(w, h, out var sinkReason))
            {
                reason = sinkReason;
                return false;
            }

            var bpp = colorType == 6 ? 4 : 3;
            var stride = w * bpp;

            // 行流式：cur/prev 各一行（含前一行用于滤波），rgbaRow 仅 RGB 输入用于补 alpha。
            var cur = new byte[stride];
            var prev = new byte[stride];
            var rgbaRow = bpp == 3 ? new byte[w * 4] : cur;

            uint adlerA = 1, adlerB = 0;
            using (var source = new MemoryStream(idatBuffer, 2, idatLength - 2, writable: false))
            using (var inflate = new DeflateStream(source, CompressionMode.Decompress))
            {
                for (var y = 0; y < h; y++)
                {
                    var filterByte = inflate.ReadByte();
                    if (filterByte < 0 || !ReadExactly(inflate, cur, stride))
                    {
                        reason = "IDAT 解压后数据不足";
                        return false;
                    }

                    // Adler-32 按解压出的原始字节（滤波类型字节 + 滤波后的行字节）累加，必须在 Unfilter 原地改写之前。
                    AdlerUpdate(ref adlerA, ref adlerB, (byte)filterByte);
                    AdlerUpdate(ref adlerA, ref adlerB, cur, stride);

                    if (!Unfilter((byte)filterByte, cur, prev, stride, bpp))
                    {
                        reason = "未知的行滤波类型 " + filterByte;
                        return false;
                    }

                    if (bpp == 3)
                    {
                        var s = 0;
                        var d = 0;
                        for (var x = 0; x < w; x++)
                        {
                            rgbaRow[d] = cur[s];
                            rgbaRow[d + 1] = cur[s + 1];
                            rgbaRow[d + 2] = cur[s + 2];
                            rgbaRow[d + 3] = 255;
                            s += 3;
                            d += 4;
                        }
                    }

                    // 引擎纹理第 0 行是最下面一行，PNG 第 0 行是最上面一行。
                    sink.WriteRow(h - 1 - y, rgbaRow);

                    var swap = prev;
                    prev = cur;
                    cur = swap;
                    if (bpp == 4)
                    {
                        rgbaRow = cur;
                    }
                }
            }

            // zlib 流尾部 Adler-32（大端，位于 IDAT 拼接数据最后 4 字节）。
            var expectedAdler = ReadUInt32(idatBuffer, idatLength - 4);
            var actualAdler = (adlerB << 16) | adlerA;
            if (expectedAdler != actualAdler)
            {
                reason = "zlib Adler-32 校验失败（像素数据损坏）";
                return false;
            }

            return true;
        }

        private static void AdlerUpdate(ref uint a, ref uint b, byte value)
        {
            a = (a + value) % 65521u;
            b = (b + a) % 65521u;
        }

        /// <summary>对 <paramref name="data"/> 前 <paramref name="count"/> 字节累加 Adler-32。按不超过 5552 字节
        /// 一段延迟取模（zlib 标准做法，段内 uint 不溢出）。</summary>
        private static void AdlerUpdate(ref uint a, ref uint b, byte[] data, int count)
        {
            var offset = 0;
            while (offset < count)
            {
                var n = Math.Min(5552, count - offset);
                for (var i = 0; i < n; i++)
                {
                    a += data[offset + i];
                    b += a;
                }

                a %= 65521u;
                b %= 65521u;
                offset += n;
            }
        }

        private static uint[]? _crcTable;

        /// <summary>CRC-32（PNG 规范，多项式 0xEDB88320）：对 <paramref name="data"/> 从 <paramref name="offset"/> 起
        /// <paramref name="count"/> 字节（含块类型 4 字节 + 数据）。表惰性建立；并发首次建表产生的是同一份
        /// 内容的两份拷贝，赋值是引用原子写，无害。</summary>
        private static uint Crc32(byte[] data, int offset, int count)
        {
            var table = _crcTable;
            if (table == null)
            {
                table = new uint[256];
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
            for (var i = 0; i < count; i++)
            {
                crc = table[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFFu;
        }

        private static bool ReadExactly(Stream stream, byte[] buffer, int count)
        {
            var filled = 0;
            while (filled < count)
            {
                var read = stream.Read(buffer, filled, count - filled);
                if (read <= 0)
                {
                    return false;
                }

                filled += read;
            }

            return true;
        }

        /// <summary>原地还原一行：<paramref name="row"/> 是当前行（滤波后的字节），<paramref name="prev"/>
        /// 是上一行还原后的字节（首行时全零）。</summary>
        private static bool Unfilter(byte filterType, byte[] row, byte[] prev, int stride, int bpp)
        {
            switch (filterType)
            {
                case 0:
                    return true;

                case 1:
                    for (var i = bpp; i < stride; i++)
                    {
                        row[i] = (byte)(row[i] + row[i - bpp]);
                    }

                    return true;

                case 2:
                    for (var i = 0; i < stride; i++)
                    {
                        row[i] = (byte)(row[i] + prev[i]);
                    }

                    return true;

                case 3:
                    for (var i = 0; i < bpp && i < stride; i++)
                    {
                        row[i] = (byte)(row[i] + (prev[i] >> 1));
                    }

                    for (var i = bpp; i < stride; i++)
                    {
                        row[i] = (byte)(row[i] + ((row[i - bpp] + prev[i]) >> 1));
                    }

                    return true;

                case 4:
                    for (var i = 0; i < bpp && i < stride; i++)
                    {
                        // a = c = 0 时 Paeth 预测值恒等于 b。
                        row[i] = (byte)(row[i] + prev[i]);
                    }

                    for (var i = bpp; i < stride; i++)
                    {
                        int a = row[i - bpp];
                        int b = prev[i];
                        int c = prev[i - bpp];
                        var p = a + b - c;
                        var pa = p > a ? p - a : a - p;
                        var pb = p > b ? p - b : b - p;
                        var pc = p > c ? p - c : c - p;
                        int predictor;
                        if (pa <= pb && pa <= pc)
                        {
                            predictor = a;
                        }
                        else if (pb <= pc)
                        {
                            predictor = b;
                        }
                        else
                        {
                            predictor = c;
                        }

                        row[i] = (byte)(row[i] + predictor);
                    }

                    return true;

                default:
                    return false;
            }
        }

        private static uint ReadUInt32(byte[] data, int offset) =>
            ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
    }
}
