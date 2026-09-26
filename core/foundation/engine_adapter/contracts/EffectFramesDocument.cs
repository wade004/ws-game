using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 消费方反馈第 77 条（[ADR-0054](../../../../architecture/adr/0054-资产数据根目录与目录内固定文件名纳入公开契约.md)）：
    /// <see cref="AssetRefConventions.VfxFramesFile"/>/<see cref="AssetRefConventions.SpriteAnimFramesFile"/>
    /// 指向的 <c>frames.json</c>（结构：<c>frame_w</c>/<c>frame_h</c>/<c>fps</c>/<c>frame_duration</c>/
    /// <c>loop</c>/<c>frames:[{index,x,y,w,h,duration}]</c>，见 <c>toolchain/asset_import/vfx_cmd.py</c>
    /// 模块文档字符串）的结构化只读读取契约。此前该结构只有唯一一处解码实现——
    /// <c>Adapter.Unity.EngineAdapter.UnityResourceLoader.TryDecodeEffect</c>（Unity 引擎适配层
    /// 内部私有方法，不对外公开），非 Unity 消费方（如随游戏走的内容编辑器）要显示"帧数/帧尺寸/
    /// 首帧裁剪区"只能自行按字段名解析 JSON，属于自行实现框架格式（消费方规则不允许）。本类型收口
    /// 该结构，供任何持有 <c>frames.json</c> 文本的消费方解析；<c>TryDecodeEffect</c> 改为调用
    /// <see cref="TryParse"/> 取得结构化数据后，只做"按每帧矩形从图集 <c>Texture2D</c> 切出
    /// <c>Sprite</c>"这一步 Unity 特有的收尾工作，不再自行解析 JSON。
    /// <para>
    /// 判断记录（<see cref="EffectFrameData.Width"/>/<see cref="EffectFrameData.Height"/> 为何是
    /// <c>double?</c> 而不是解析期直接落实成非空 <c>double</c>）：<c>TryDecodeEffect</c> 原实现里
    /// 单帧矩形缺省 <c>w</c>/<c>h</c> 时的兜底值是"已解码图集纹理的整宽/整高"
    /// （<c>ReadNumber(frameObj, "w", texture.width)</c>）——这个兜底值依赖 Unity
    /// <c>Texture2D.LoadImage</c> 解码 <c>atlas.png</c> 之后才能拿到，是纯 JSON 解析阶段（本类型
    /// 职责范围）无法得知的信息；本类型只解析 JSON 里显式写出的值，缺省时留 <c>null</c>，由调用方
    /// （如 <c>TryDecodeEffect</c>）按自己能拿到的兜底信息（图集尺寸）二次决定，不在本类型内替调用方
    /// 猜测一个可能不对的默认值。<c>frames.json</c> 顶层的 <c>frame_w</c>/<c>frame_h</c> 字段虽然
    /// 已经是"名义帧尺寸"，但 <c>TryDecodeEffect</c> 原实现从未读取这两个字段作为单帧缺省值来源
    /// （只用纹理整宽/整高兜底）——本类型忠实保留这一没有被使用到的既有行为，不趁机"顺手"改成更
    /// 合理的兜底链（那是另一个话题，不在本次消费方反馈范围内，见验收标准"证明是纯抽取，没有借机
    /// 改动格式/行为"）。<see cref="EffectFrameData.Index"/> 则不读取 JSON 里的 <c>index</c> 字段——
    /// 原实现同样只用数组下标本身（<c>for (var i = 0; ...) { ...; sprite.name =
    /// $"{resourceId.Value}_frame{i}"; }</c>），从未读取该字段，本类型同样忠实保留：
    /// <see cref="EffectFrameData.Index"/> 固定等于该元素在 <see cref="Frames"/> 数组里的下标。
    /// </para>
    /// <para>
    /// 判断记录（<c>loop</c>/<c>fps</c>/<c>frame_duration</c>/单帧 <c>duration</c> 四组缺省值与
    /// <c>TryDecodeEffect</c> 原实现逐字节一致，收口时未改动）：<c>loop</c> 缺省
    /// <c>false</c>；<c>fps</c> 缺省 <c>12</c>；顶层 <see cref="DefaultFrameDuration"/>
    /// 缺省时按 <c>fps &gt; 0 ? 1.0 / fps : 0.05</c> 推算；单帧 <see cref="EffectFrameData.Duration"/>
    /// 缺省时退回 <see cref="DefaultFrameDuration"/>。见类型顶部验收标准："与改动前逐字节对齐"一节。
    /// </para>
    /// <para>
    /// [ADR-0095](../../../../architecture/adr/0095-逐帧动画枢轴与像素密度取自所属精灵集.md) 新增
    /// 两个可选顶层字段——<see cref="PixelsPerUnit"/>（顶层 <c>pixels_per_unit</c>）与
    /// <see cref="Root"/>（顶层 <c>root: [x, y]</c>，像素坐标，原点左上，与 anchors.json 的
    /// <c>root</c> 同一语义）：内容侧显式声明时优先于调用方按所属精灵集给出的加载提示（决策 4，
    /// "内容侧对特殊画布的动画有出口"）；未声明时两者均为 <c>null</c>，不影响未使用这两个字段的
    /// 既有 <c>frames.json</c> 文件。<c>pixels_per_unit</c> 非正数视为未声明（与 anchors.json
    /// 顶层同名字段同一条校验规则）。
    /// </para>
    /// </summary>
    public sealed class EffectFramesDocument
    {
        /// <summary>顶层 <c>frame_w</c>（名义帧宽度）；JSON 未写出该字段时为 <c>null</c>。</summary>
        public double? FrameWidth { get; }

        /// <summary>顶层 <c>frame_h</c>（名义帧高度）；JSON 未写出该字段时为 <c>null</c>。</summary>
        public double? FrameHeight { get; }

        /// <summary>顶层 <c>fps</c>；JSON 未写出该字段时缺省 <c>12</c>。</summary>
        public double Fps { get; }

        /// <summary>顶层 <c>frame_duration</c>（各帧 <c>duration</c> 缺省时的退回值）；JSON 未写出
        /// 该字段时按 <see cref="Fps"/> 推算（见类型顶部判断记录）。</summary>
        public double DefaultFrameDuration { get; }

        /// <summary>顶层 <c>loop</c>；JSON 未写出该字段时缺省 <c>false</c>。</summary>
        public bool Loop { get; }

        /// <summary>[ADR-0095] 顶层可选 <c>pixels_per_unit</c>；未声明或不是正数时为 <c>null</c>，
        /// 见本类型 <see cref="FrameWidth"/> 之前的判断记录。</summary>
        public double? PixelsPerUnit { get; }

        /// <summary>[ADR-0095] 顶层可选 <c>root: [x, y]</c>（像素坐标，原点左上）；未声明、不是
        /// 二元数字数组时为 <c>null</c>，见本类型 <see cref="FrameWidth"/> 之前的判断记录。</summary>
        public (double X, double Y)? Root { get; }

        /// <summary>顶层 <c>frames</c> 数组，按 JSON 原始顺序排列（<see cref="EffectFrameData.Index"/>
        /// 即数组下标，见类型顶部判断记录）。</summary>
        public IReadOnlyList<EffectFrameData> Frames { get; }

        private EffectFramesDocument(
            double? frameWidth,
            double? frameHeight,
            double fps,
            double defaultFrameDuration,
            bool loop,
            double? pixelsPerUnit,
            (double X, double Y)? root,
            IReadOnlyList<EffectFrameData> frames)
        {
            FrameWidth = frameWidth;
            FrameHeight = frameHeight;
            Fps = fps;
            DefaultFrameDuration = defaultFrameDuration;
            Loop = loop;
            PixelsPerUnit = pixelsPerUnit;
            Root = root;
            Frames = frames;
        }

        /// <summary>
        /// 解析 <c>frames.json</c> 文本。解析失败（JSON 语法错误、顶层不是对象、<c>frames</c>
        /// 字段缺失/不是数组、数组内某元素不是对象）时返回 <c>false</c>，不抛异常——与
        /// <c>TryDecodeEffect</c> 原实现"解析失败即回退到内建占位特效，不让调用方处理异常"的既有
        /// 容错行为一致。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="json"/> 为 <c>null</c>。</exception>
        public static bool TryParse(string json, out EffectFramesDocument document)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            document = null!;

            JsonValue root;
            try
            {
                root = JsonReader.Parse(json);
            }
            catch (JsonParseException)
            {
                return false;
            }

            if (!(root is JsonObject obj))
            {
                return false;
            }

            var frameWidth = ReadOptionalNumber(obj, "frame_w");
            var frameHeight = ReadOptionalNumber(obj, "frame_h");
            var loop = obj.TryGetValue("loop", out var loopVal) && loopVal is JsonBool loopBool && loopBool.Value;
            var fps = obj.TryGetValue("fps", out var fpsVal) && fpsVal is JsonNumber fpsNum ? fpsNum.Value : 12.0;
            var defaultDuration = obj.TryGetValue("frame_duration", out var fdVal) && fdVal is JsonNumber fdNum
                ? fdNum.Value
                : (fps > 0 ? 1.0 / fps : 0.05);

            // ADR-0095 决策 4：可选顶层 pixels_per_unit（非正数视为未声明，同 anchors.json 顶层
            // 同名字段一致的校验规则）与可选顶层 root:[x,y]（二元数字数组，其余形状视为未声明）。
            double? pixelsPerUnit = null;
            if (obj.TryGetValue("pixels_per_unit", out var ppuVal) && ppuVal is JsonNumber ppuNum && ppuNum.Value > 0)
            {
                pixelsPerUnit = ppuNum.Value;
            }

            (double X, double Y)? explicitRoot = null;
            if (obj.TryGetValue("root", out var rootVal) && rootVal is JsonArray rootArr && rootArr.Count >= 2 &&
                rootArr[0] is JsonNumber rootX && rootArr[1] is JsonNumber rootY)
            {
                explicitRoot = (rootX.Value, rootY.Value);
            }

            if (!obj.TryGetValue("frames", out var framesVal) || !(framesVal is JsonArray framesArr))
            {
                return false;
            }

            var frames = new EffectFrameData[framesArr.Count];
            for (var i = 0; i < framesArr.Count; i++)
            {
                if (!(framesArr[i] is JsonObject frameObj))
                {
                    return false;
                }

                var x = ReadNumberOrDefault(frameObj, "x", 0);
                var y = ReadNumberOrDefault(frameObj, "y", 0);
                var w = ReadOptionalNumber(frameObj, "w");
                var h = ReadOptionalNumber(frameObj, "h");
                var duration = frameObj.TryGetValue("duration", out var durVal) && durVal is JsonNumber durNum
                    ? durNum.Value
                    : defaultDuration;

                frames[i] = new EffectFrameData(i, x, y, w, h, duration);
            }

            document = new EffectFramesDocument(
                frameWidth, frameHeight, fps, defaultDuration, loop, pixelsPerUnit, explicitRoot, frames);
            return true;
        }

        private static double? ReadOptionalNumber(JsonObject obj, string key) =>
            obj.TryGetValue(key, out var val) && val is JsonNumber num ? num.Value : (double?)null;

        private static double ReadNumberOrDefault(JsonObject obj, string key, double fallback) =>
            obj.TryGetValue(key, out var val) && val is JsonNumber num ? num.Value : fallback;
    }

    /// <summary><see cref="EffectFramesDocument.Frames"/> 数组内单个元素：一帧在图集内的裁剪矩形
    /// （<see cref="X"/>/<see cref="Y"/>/<see cref="Width"/>/<see cref="Height"/>，图集内像素坐标）
    /// 与播放时长（<see cref="Duration"/>，秒）。</summary>
    public readonly struct EffectFrameData
    {
        /// <summary>该元素在 <see cref="EffectFramesDocument.Frames"/> 数组里的下标（不读取 JSON
        /// 里的 <c>index</c> 字段，见 <see cref="EffectFramesDocument"/> 类型顶部判断记录）。</summary>
        public int Index { get; }

        /// <summary>裁剪矩形左上角 X（图集内像素坐标）；JSON 未写出该字段时缺省 <c>0</c>。</summary>
        public double X { get; }

        /// <summary>裁剪矩形左上角 Y（图集内像素坐标）；JSON 未写出该字段时缺省 <c>0</c>。</summary>
        public double Y { get; }

        /// <summary>裁剪矩形宽度；JSON 未写出该字段时为 <c>null</c>（历史兜底值依赖已解码图集纹理
        /// 的整宽，见 <see cref="EffectFramesDocument"/> 类型顶部判断记录，由调用方自行决定）。</summary>
        public double? Width { get; }

        /// <summary>裁剪矩形高度；JSON 未写出该字段时为 <c>null</c>（同 <see cref="Width"/>）。</summary>
        public double? Height { get; }

        /// <summary>本帧播放时长（秒）；JSON 未写出该帧的 <c>duration</c> 字段时退回
        /// <see cref="EffectFramesDocument.DefaultFrameDuration"/>。</summary>
        public double Duration { get; }

        public EffectFrameData(int index, double x, double y, double? width, double? height, double duration)
        {
            Index = index;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Duration = duration;
        }
    }
}
