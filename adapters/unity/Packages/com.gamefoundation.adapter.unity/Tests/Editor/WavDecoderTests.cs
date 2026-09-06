#nullable enable
// WavDecoderTests：WavDecoder（内部类型，见 Runtime/AssemblyInfo.cs 的 InternalsVisibleTo）此前
// 零测试覆盖（W3b 审计发现），用真实占位 wav 文件（assets/_placeholder/sfx/*.wav，见
// UnityResourceLoader 音频解码判断记录）而不是手工拼字节数组，验证声道/采样率/样本数三项。
using System.IO;
using Adapter.Unity.EngineAdapter;
using NUnit.Framework;
using UnityEngine;

namespace Adapter.Unity.Tests.Editor
{
    public sealed class WavDecoderTests
    {
        private static string RepoRoot() =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        [Test]
        public void TryDecode_PlaceholderHitWav_ReturnsValidChannelsSampleRateAndSamples()
        {
            var path = Path.Combine(RepoRoot(), "assets", "_placeholder", "sfx", "hit_01.wav");
            Assert.IsTrue(File.Exists(path), $"占位音频应当存在：{path}");
            var bytes = File.ReadAllBytes(path);

            var ok = WavDecoder.TryDecode(bytes, out var channels, out var sampleRate, out var samples);

            Assert.IsTrue(ok, "占位 wav（标准 PCM16）应当解码成功");
            Assert.GreaterOrEqual(channels, 1, "声道数应当至少为 1");
            Assert.LessOrEqual(channels, 2, "占位音频应当是单声道或立体声");
            Assert.Greater(sampleRate, 0, "采样率应当为正数");
            Assert.Greater(samples.Length, 0, "样本数应当为正数");
            Assert.AreEqual(0, samples.Length % channels, "样本总数应当能被声道数整除（交错排列）");

            foreach (var sample in samples)
            {
                Assert.LessOrEqual(sample, 1.0f, "PCM16 归一化样本值不应超过 1.0");
                Assert.GreaterOrEqual(sample, -1.0f, "PCM16 归一化样本值不应低于 -1.0");
            }
        }

        [Test]
        public void TryDecode_AllPlaceholderSfxFiles_AllDecodeSuccessfully()
        {
            var dir = Path.Combine(RepoRoot(), "assets", "_placeholder", "sfx");
            var files = Directory.GetFiles(dir, "*.wav");
            Assert.Greater(files.Length, 0, "占位音频目录应当至少有一个 wav 文件");

            foreach (var file in files)
            {
                var bytes = File.ReadAllBytes(file);
                var ok = WavDecoder.TryDecode(bytes, out var channels, out var sampleRate, out var samples);
                Assert.IsTrue(ok, $"{Path.GetFileName(file)} 应当解码成功");
                Assert.Greater(channels, 0, $"{Path.GetFileName(file)} 声道数应当为正数");
                Assert.Greater(sampleRate, 0, $"{Path.GetFileName(file)} 采样率应当为正数");
                Assert.Greater(samples.Length, 0, $"{Path.GetFileName(file)} 样本数应当为正数");
            }
        }

        [Test]
        public void TryDecode_TooShortByteArray_ReturnsFalse()
        {
            var ok = WavDecoder.TryDecode(new byte[10], out _, out _, out var samples);
            Assert.IsFalse(ok, "不足 44 字节（RIFF 头最小长度）应当解码失败");
            Assert.AreEqual(0, samples.Length);
        }

        [Test]
        public void TryDecode_NotRiffHeader_ReturnsFalse()
        {
            var bytes = new byte[64];
            for (var i = 0; i < bytes.Length; i++) bytes[i] = 0xFF;
            var ok = WavDecoder.TryDecode(bytes, out _, out _, out _);
            Assert.IsFalse(ok, "不是合法 RIFF/WAVE 头的字节流应当解码失败");
        }
    }
}
