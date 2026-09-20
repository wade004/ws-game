using System;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.Foundation.EngineAdapter
{
    /// <summary>
    /// 消费方反馈第 77 条（ADR-0054）验收测试：<see cref="EffectFramesDocument"/> 的 <c>frames.json</c>
    /// 结构化解析。
    /// <para>
    /// 判断记录（与"Unity 适配层原实现"一致性的验证方式）：<c>UnityResourceLoader.TryDecodeEffect</c>
    /// 原本内联的 JSON 解析逻辑已整体搬进本类型（原私有方法体不再独立存在，见该方法当前源码的
    /// 判断记录），因此不能像 <c>toolchain/map_ref_probe</c> 那样"跑两份独立实现互相对照"；本文件
    /// 改为在每个测试方法内直接按原实现的算法公式（逐字照抄自改动前 <c>TryDecodeEffect</c> 源码，
    /// 见各测试方法上的注释）手工写出期望值，与 <see cref="EffectFramesDocument.TryParse"/> 的
    /// 输出逐字段比对——与 <c>toolchain/tests/test_ref_conventions.py</c>
    /// <c>test_map_layer_paths_match_pre_refactor_map_cmd_py_format</c> 的"钉住历史算法、手工复算、
    /// 逐字节比对"是同一手法，只是比对对象从另一个进程的真实运行结果换成了测试内联的手工复算
    /// （本类型与原实现同语言、同进程，原private 方法体已被替换，无法像跨语言场景那样保留一份
    /// 不动的旧实现供子进程调用）。
    /// </para>
    /// </summary>
    public class EffectFramesDocumentTests
    {
        // 与 assets/_placeholder/vfx/burn/frames.json 逐字节一致（消费方反馈第 77 条示例文件，见
        // vfx_cmd.py 模块文档字符串"示例见 assets/_placeholder/vfx/burn/frames.json"）。
        private const string BurnFramesJson = @"{
  ""frame_w"": 32,
  ""frame_h"": 32,
  ""fps"": 20,
  ""frame_duration"": 0.05,
  ""loop"": true,
  ""frames"": [
    { ""index"": 0, ""x"": 0, ""y"": 0, ""w"": 32, ""h"": 32, ""duration"": 0.05 },
    { ""index"": 1, ""x"": 32, ""y"": 0, ""w"": 32, ""h"": 32, ""duration"": 0.05 },
    { ""index"": 2, ""x"": 64, ""y"": 0, ""w"": 32, ""h"": 32, ""duration"": 0.05 },
    { ""index"": 3, ""x"": 96, ""y"": 0, ""w"": 32, ""h"": 32, ""duration"": 0.05 },
    { ""index"": 4, ""x"": 128, ""y"": 0, ""w"": 32, ""h"": 32, ""duration"": 0.05 },
    { ""index"": 5, ""x"": 160, ""y"": 0, ""w"": 32, ""h"": 32, ""duration"": 0.05 },
    { ""index"": 6, ""x"": 192, ""y"": 0, ""w"": 32, ""h"": 32, ""duration"": 0.05 },
    { ""index"": 7, ""x"": 224, ""y"": 0, ""w"": 32, ""h"": 32, ""duration"": 0.05 }
  ]
}";

        [Fact]
        public void TryParse_RealBurnFramesJsonFixture_MatchesFileContent()
        {
            Assert.True(EffectFramesDocument.TryParse(BurnFramesJson, out var document));
            Assert.Equal(32, document.FrameWidth);
            Assert.Equal(32, document.FrameHeight);
            Assert.Equal(20, document.Fps);
            Assert.Equal(0.05, document.DefaultFrameDuration);
            Assert.True(document.Loop);
            Assert.Equal(8, document.Frames.Count);

            for (var i = 0; i < document.Frames.Count; i++)
            {
                var frame = document.Frames[i];
                Assert.Equal(i, frame.Index);
                Assert.Equal(i * 32, frame.X);
                Assert.Equal(0, frame.Y);
                Assert.Equal(32, frame.Width);
                Assert.Equal(32, frame.Height);
                Assert.Equal(0.05, frame.Duration);
            }
        }

        // --- 缺省值分支：与改动前 TryDecodeEffect 源码逐一对应 ---
        // loop 缺省 false；fps 缺省 12；frame_duration 缺省时按 fps>0 ? 1/fps : 0.05 推算；
        // 单帧 duration 缺省时退回 frame_duration；单帧 x/y 缺省 0。

        [Fact]
        public void TryParse_MinimalDocument_LoopDefaultsFalse()
        {
            const string json = @"{""frames"":[{""x"":0,""y"":0,""w"":10,""h"":10}]}";
            Assert.True(EffectFramesDocument.TryParse(json, out var document));
            Assert.False(document.Loop);
        }

        [Fact]
        public void TryParse_MissingFps_DefaultsTo12()
        {
            const string json = @"{""frames"":[{""x"":0,""y"":0,""w"":10,""h"":10}]}";
            Assert.True(EffectFramesDocument.TryParse(json, out var document));
            Assert.Equal(12.0, document.Fps);
        }

        [Fact]
        public void TryParse_MissingFrameDuration_DerivesFromFps()
        {
            const string json = @"{""fps"":25,""frames"":[{""x"":0,""y"":0,""w"":10,""h"":10}]}";
            Assert.True(EffectFramesDocument.TryParse(json, out var document));
            Assert.Equal(1.0 / 25.0, document.DefaultFrameDuration);
        }

        [Fact]
        public void TryParse_MissingFrameDurationAndFpsIsZero_DefaultsToPointZeroFive()
        {
            const string json = @"{""fps"":0,""frames"":[{""x"":0,""y"":0,""w"":10,""h"":10}]}";
            Assert.True(EffectFramesDocument.TryParse(json, out var document));
            Assert.Equal(0.05, document.DefaultFrameDuration);
        }

        [Fact]
        public void TryParse_PerFrameDurationMissing_FallsBackToDefaultFrameDuration()
        {
            const string json = @"{""frame_duration"":0.08,""frames"":[{""x"":0,""y"":0,""w"":10,""h"":10}]}";
            Assert.True(EffectFramesDocument.TryParse(json, out var document));
            Assert.Equal(0.08, document.Frames[0].Duration);
        }

        [Fact]
        public void TryParse_PerFrameXYMissing_DefaultsToZero()
        {
            const string json = @"{""frames"":[{""w"":10,""h"":10}]}";
            Assert.True(EffectFramesDocument.TryParse(json, out var document));
            Assert.Equal(0, document.Frames[0].X);
            Assert.Equal(0, document.Frames[0].Y);
        }

        [Fact]
        public void TryParse_PerFrameWidthHeightMissing_LeftNullForCallerToFallBack()
        {
            // 判断记录：原实现此时兜底为已解码图集纹理的整宽/整高——纯 JSON 解析阶段不知道纹理
            // 尺寸，本类型如实留 null，由调用方（TryDecodeEffect）自行兜底，见类型顶部判断记录。
            const string json = @"{""frames"":[{""x"":0,""y"":0}]}";
            Assert.True(EffectFramesDocument.TryParse(json, out var document));
            Assert.Null(document.Frames[0].Width);
            Assert.Null(document.Frames[0].Height);
        }

        [Fact]
        public void TryParse_TopLevelFrameWidthHeightMissing_LeftNull()
        {
            const string json = @"{""frames"":[{""x"":0,""y"":0,""w"":10,""h"":10}]}";
            Assert.True(EffectFramesDocument.TryParse(json, out var document));
            Assert.Null(document.FrameWidth);
            Assert.Null(document.FrameHeight);
        }

        // --- 解析失败分支：与改动前 TryDecodeEffect 源码逐一对应，均返回 false，不抛异常 ---

        [Fact]
        public void TryParse_InvalidJsonSyntax_ReturnsFalse()
        {
            Assert.False(EffectFramesDocument.TryParse("not json", out _));
        }

        [Fact]
        public void TryParse_RootIsArrayNotObject_ReturnsFalse()
        {
            Assert.False(EffectFramesDocument.TryParse("[1,2,3]", out _));
        }

        [Fact]
        public void TryParse_FramesFieldMissing_ReturnsFalse()
        {
            Assert.False(EffectFramesDocument.TryParse(@"{""loop"":true}", out _));
        }

        [Fact]
        public void TryParse_FramesFieldNotArray_ReturnsFalse()
        {
            Assert.False(EffectFramesDocument.TryParse(@"{""frames"":""nope""}", out _));
        }

        [Fact]
        public void TryParse_FrameElementNotObject_ReturnsFalse()
        {
            Assert.False(EffectFramesDocument.TryParse(@"{""frames"":[1,2]}", out _));
        }

        [Fact]
        public void TryParse_NullJson_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => EffectFramesDocument.TryParse(null!, out _));
        }

        [Fact]
        public void TryParse_EmptyFramesArray_SucceedsWithZeroFrames()
        {
            Assert.True(EffectFramesDocument.TryParse(@"{""frames"":[]}", out var document));
            Assert.Empty(document.Frames);
        }

        [Fact]
        public void TryParse_FrameIndexIgnoresJsonIndexField_UsesArrayPosition()
        {
            // 判断记录：原实现从未读取 JSON 里的 "index" 字段，只用数组下标本身（sprite.name 用循环
            // 变量 i 拼接）；本类型忠实保留——即便 JSON 显式写了不连续/乱序的 index 字面量，
            // EffectFrameData.Index 仍固定等于数组下标。
            const string json = @"{""frames"":[{""index"":99,""x"":0,""y"":0,""w"":1,""h"":1},
                                                {""index"":1,""x"":1,""y"":0,""w"":1,""h"":1}]}";
            Assert.True(EffectFramesDocument.TryParse(json, out var document));
            Assert.Equal(0, document.Frames[0].Index);
            Assert.Equal(1, document.Frames[1].Index);
        }
    }
}
