#nullable enable
// WavDecoder：最小可用的标准 PCM16 WAV（RIFF/WAVE）解码器，纯 .NET 实现，不依赖任何
// UnityEngine API，可在后台线程安全调用。只支持未压缩 16 位 PCM（most common 占位音频格式），
// 见 UnityResourceLoader.cs 顶部"判断记录（音频解码）"。
using System;

namespace Adapter.Unity.EngineAdapter
{
    internal static class WavDecoder
    {
        public static bool TryDecode(byte[] bytes, out int channels, out int sampleRate, out float[] samples)
        {
            channels = 0;
            sampleRate = 0;
            samples = Array.Empty<float>();

            if (bytes.Length < 44) return false;
            if (bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F') return false;
            if (bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E') return false;

            var pos = 12;
            var bitsPerSample = 0;
            var dataOffset = -1;
            var dataLength = 0;

            while (pos + 8 <= bytes.Length)
            {
                var chunkId0 = bytes[pos];
                var chunkId1 = bytes[pos + 1];
                var chunkId2 = bytes[pos + 2];
                var chunkId3 = bytes[pos + 3];
                var chunkSize = BitConverter.ToInt32(bytes, pos + 4);
                var chunkDataStart = pos + 8;

                if (chunkId0 == 'f' && chunkId1 == 'm' && chunkId2 == 't' && chunkId3 == ' ')
                {
                    if (chunkDataStart + 16 > bytes.Length) return false;
                    var audioFormat = BitConverter.ToInt16(bytes, chunkDataStart);
                    channels = BitConverter.ToInt16(bytes, chunkDataStart + 2);
                    sampleRate = BitConverter.ToInt32(bytes, chunkDataStart + 4);
                    bitsPerSample = BitConverter.ToInt16(bytes, chunkDataStart + 14);

                    if (audioFormat != 1 || bitsPerSample != 16)
                    {
                        // 只支持 PCM(1)、16 位；其余编码一律解码失败。
                        return false;
                    }
                }
                else if (chunkId0 == 'd' && chunkId1 == 'a' && chunkId2 == 't' && chunkId3 == 'a')
                {
                    dataOffset = chunkDataStart;
                    dataLength = chunkSize;
                }

                pos = chunkDataStart + chunkSize + (chunkSize % 2); // 分块按偶数字节对齐
            }

            if (dataOffset < 0 || channels <= 0 || sampleRate <= 0 || bitsPerSample != 16)
            {
                return false;
            }

            if (dataOffset + dataLength > bytes.Length)
            {
                dataLength = bytes.Length - dataOffset;
            }

            var sampleCount = dataLength / 2;
            samples = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                var raw = BitConverter.ToInt16(bytes, dataOffset + i * 2);
                samples[i] = raw / 32768f;
            }

            return true;
        }
    }
}
