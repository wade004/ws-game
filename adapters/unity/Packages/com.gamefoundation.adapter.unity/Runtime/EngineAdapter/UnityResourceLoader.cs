#nullable enable
// UnityResourceLoader：IResourceLoader 的 Unity 引擎实现。
//
// 资源 id → 相对路径映射规则（与 architecture/14_资产规格书模板.md 第 1.2 节文件名模板保持
// 同一套命名，具体做法参照 presentation/render/core/SpriteViewBase.ResolveLayerResourceId 已经
// 采用的"去掉类别前缀（第一个点分段）、剩余点号换下划线"规则）：
//   相对路径 = "GameFoundation/<kind 子目录>/<资源引用id去掉类别前缀，点号换下划线>.<扩展名>"
//   其中 <kind 子目录> 与 <扩展名> 按 ResourceKind 固定：
//     Image     -> "sprites/<name>.png"
//     Audio     -> "audio/<name>.wav"（占位音频约定为标准 PCM16 WAV，见 判断记录）
//     Font      -> "fonts/<name>.ttf"
//     DataTable -> "data/<name>.json"
//   根目录固定为 Application.streamingAssetsPath（跨平台只读只读资源目录，桌面平台上是普通
//   文件系统路径，可直接用 System.IO 同步读取，见下）。
//
// 判断记录（加载方式）：不使用 UnityWebRequest/协程，改用 System.Threading.Tasks.Task 在后台
// 线程做纯文件字节读取（不调用任何 UnityEngine API，线程安全），读取完成后把结果放进一个
// 线程安全队列；真正需要调用 UnityEngine API 的解码步骤（Texture2D/AudioClip 构造、写入内部
// 缓存、触发 LoadCallback）全部在 <see cref="Tick"/> 里于主线程完成——Tick 由
// UnityEngineHost.Update 每帧调用一次，因此回调总是在主线程的下一次 IClock.onFrame 之前排队
// 执行，满足 02 第 2 节第 2 条"由适配层实现保证回调总在主线程排队执行"的线程约定。
//
// 判断记录（音频解码）：Unity 没有跨平台的"任意压缩音频字节数组 -> AudioClip"同步公开 API
// （UnityWebRequestMultimedia 是唯一内置方案，但要求协程与 URI，且仍需假定具体编解码格式）；
// 本实现改为直接解析标准 PCM16 WAV（RIFF/WAVE 头 + 'fmt '/'data' 分块），足以覆盖占位音频与
// 大多数游戏音频制作管线导出的未压缩 WAV，具体音频格式选型见 architecture/选型/（本文档不预设
// 结论）。非 WAV/非 PCM16 数据会解析失败并按"加载失败"回调 false。
//
// 判断记录（Font 资源种类）：Unity 运行期没有公开 API 能把任意字体文件字节数组转换成可用于
// TMP 渲染的字体资产（TMP_FontAsset.CreateFontAsset 需要一个已被 Unity 资产管线导入过的
// UnityEngine.Font 对象，而非裸字节）；因此本加载器对 Font 种类只保证"文件存在性校验 + 原始
// 字节读取（TryGetFontBytes）"，不产生可直接渲染的字体资产。界面默认字体走
// UnityUISurface 专用的路径（运行期用包内预先以 Unity 资产管线导入好的 Font 对象调用
// TMP_FontAsset.CreateFontAsset，见该类型注释），不依赖本加载器的 Font 种类。此为已知契约/
// 实现能力缺口，已记录在包 README"已知契约缺口"一节。
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityResourceLoader : IResourceLoader
    {
        private struct PendingCompletion
        {
            public Id ResourceId;
            public ResourceKind Kind;
            public byte[]? Bytes;
            public bool ReadSuccess;
            public LoadCallback Callback;
        }

        private static readonly string RootDir = Path.Combine(Application.streamingAssetsPath, "GameFoundation");

        private readonly HashSet<Id> _loading = new HashSet<Id>();
        private readonly HashSet<Id> _loaded = new HashSet<Id>();
        private readonly ConcurrentQueue<PendingCompletion> _completions = new ConcurrentQueue<PendingCompletion>();

        private readonly Dictionary<Id, Sprite> _sprites = new Dictionary<Id, Sprite>();
        private readonly Dictionary<Id, AudioClip> _audioClips = new Dictionary<Id, AudioClip>();
        private readonly Dictionary<Id, byte[]> _fontBytes = new Dictionary<Id, byte[]>();
        private readonly Dictionary<Id, string> _dataTableText = new Dictionary<Id, string>();

        /// <summary>本加载器使用的像素-单位换算比，供 Sprite.Create 使用；与
        /// architecture/14_资产规格书模板.md 第 2.2 节"pixels_per_unit"游戏填写项对应，
        /// 框架层给一个可运行的默认值 100，具体项目在其接入记录里覆盖（本加载器不读取
        /// 项目专属配置，避免 L-1 反向依赖游戏内容）。</summary>
        public float PixelsPerUnit { get; set; } = 100f;

        public void LoadAsync(Id resourceId, ResourceKind kind, LoadCallback callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            var path = ResolvePath(resourceId, kind);
            _loading.Add(resourceId);

            Task.Run(() =>
            {
                byte[]? bytes = null;
                var ok = false;
                try
                {
                    if (File.Exists(path))
                    {
                        bytes = File.ReadAllBytes(path);
                        ok = true;
                    }
                }
                catch
                {
                    ok = false;
                }

                _completions.Enqueue(new PendingCompletion
                {
                    ResourceId = resourceId,
                    Kind = kind,
                    Bytes = bytes,
                    ReadSuccess = ok,
                    Callback = callback
                });
            });
        }

        public bool IsLoaded(Id resourceId) => _loaded.Contains(resourceId);

        public double GetLoadProgress(Id resourceId)
        {
            if (_loaded.Contains(resourceId)) return 1.0;
            // 粗粒度估算（02 第 1.7 节允许）：仍在后台读取中记为 0.5，未发起过加载记为 0。
            return _loading.Contains(resourceId) ? 0.5 : 0.0;
        }

        public void Unload(Id resourceId)
        {
            _loaded.Remove(resourceId);
            _sprites.Remove(resourceId);
            _audioClips.Remove(resourceId);
            _fontBytes.Remove(resourceId);
            _dataTableText.Remove(resourceId);
        }

        /// <summary>由 UnityEngineHost.Update 每帧调用：把后台线程读完的文件字节在主线程完成
        /// 引擎侧解码并触发调用方回调。</summary>
        internal void Tick()
        {
            while (_completions.TryDequeue(out var pending))
            {
                FinishOnMainThread(pending);
            }
        }

        private void FinishOnMainThread(PendingCompletion pending)
        {
            _loading.Remove(pending.ResourceId);

            if (!pending.ReadSuccess || pending.Bytes == null)
            {
                pending.Callback(pending.ResourceId, false);
                return;
            }

            bool success;
            switch (pending.Kind)
            {
                case ResourceKind.Image:
                    success = TryDecodeImage(pending.ResourceId, pending.Bytes);
                    break;
                case ResourceKind.Audio:
                    success = TryDecodeWav(pending.ResourceId, pending.Bytes);
                    break;
                case ResourceKind.Font:
                    _fontBytes[pending.ResourceId] = pending.Bytes;
                    success = true;
                    break;
                case ResourceKind.DataTable:
                    _dataTableText[pending.ResourceId] = System.Text.Encoding.UTF8.GetString(pending.Bytes);
                    success = true;
                    break;
                default:
                    success = false;
                    break;
            }

            if (success)
            {
                _loaded.Add(pending.ResourceId);
            }

            pending.Callback(pending.ResourceId, success);
        }

        private bool TryDecodeImage(Id resourceId, byte[] bytes)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(texture);
                return false;
            }

            var sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit);
            sprite.name = resourceId.Value;
            _sprites[resourceId] = sprite;
            return true;
        }

        private bool TryDecodeWav(Id resourceId, byte[] bytes)
        {
            if (!WavDecoder.TryDecode(bytes, out var channels, out var sampleRate, out var samples))
            {
                return false;
            }

            var clip = AudioClip.Create(resourceId.Value, samples.Length / channels, channels, sampleRate, false);
            clip.SetData(samples, 0);
            _audioClips[resourceId] = clip;
            return true;
        }

        /// <summary>供其它 Unity* 引擎适配层实现（渲染/音频）按资源 id 取回已解码对象，
        /// 不属于 IResourceLoader 契约本身，与 adapters/stub 的"测试专用方法"惯例同理，
        /// 这里是"引擎实现之间的内部协作方法"。</summary>
        public bool TryGetSprite(Id resourceId, out Sprite sprite) => _sprites.TryGetValue(resourceId, out sprite!);

        public bool TryGetAudioClip(Id resourceId, out AudioClip clip) => _audioClips.TryGetValue(resourceId, out clip!);

        public bool TryGetFontBytes(Id resourceId, out byte[] bytes) => _fontBytes.TryGetValue(resourceId, out bytes!);

        public bool TryGetDataTableText(Id resourceId, out string text) => _dataTableText.TryGetValue(resourceId, out text!);

        /// <summary>把资源引用 id 解析为磁盘路径，规则见类型顶部注释。</summary>
        /// <remarks>
        /// U2-1 判断记录（"layer." 类别的嵌套路径特例）：<c>presentation/render/core/
        /// SpriteViewBase.ResolveLayerResourceId</c> 产出的纸娃娃层资源 id 形如
        /// <c>"layer.&lt;spriteSetName&gt;__&lt;directionSlotName&gt;__&lt;layerName&gt;"</c>
        /// （双下划线分隔三段，见该方法判断记录：文件名部分直接复用 14 第 1.2 节命名模板，只在外面
        /// 包一层 <c>"layer."</c> 域前缀）。但 <c>toolchain/gen_placeholder_assets.py</c> 生成的占位
        /// 精灵集实际磁盘布局是"目录按方向/层分层"（<c>sprites/&lt;spriteSet&gt;/&lt;direction&gt;/
        /// &lt;layer&gt;.png</c>，见 <c>data/_sample/README.md</c>"判断记录（占位资产实际文件组织与
        /// 14 第 1.2 节命名模板的差异，如实记录不代为修正）"），不是单一扁平文件名——若按其余
        /// 资源种类的"去掉类别前缀、点号换下划线、直接拼成一个文件名"通用规则处理，会尝试查找一个
        /// 从不存在的扁平文件（如 <c>sprites/placeholder_hero__front__body.png</c>）。任务书明确
        /// 授权"若导入工具的输出布局与 UnityResourceLoader 期望的路径不一致，以 14 §1.2 模板为准
        /// 修正加载器那一侧，不改文档"；本方法据此只对 <c>"layer."</c> 这一个类别加特例：把双下划线
        /// 分隔的三段还原成三级目录，其余类别（<c>sprite.</c>/<c>icon.</c>/<c>data.</c> 等）的既有
        /// 扁平解析规则不变（既有测试 <c>ResolvePath_StripsCategoryPrefixAndUsesKindSubfolder</c>
        /// 之类的用例仍然覆盖非 layer 类别）。
        /// </remarks>
        public static string ResolvePath(Id resourceId, ResourceKind kind)
        {
            if (kind == ResourceKind.Image && IsLayerCategory(resourceId.Value))
            {
                var layerName = StripCategoryPrefix(resourceId.Value);
                var parts = layerName.Split(new[] { "__" }, StringSplitOptions.None);
                if (parts.Length == 3)
                {
                    return Path.Combine(RootDir, "sprites", parts[0], parts[1], parts[2] + ".png");
                }
                // 段数不是恰好 3 段：不是本判断记录假定的纸娃娃层资源 id 形状，退化为通用规则
                // （下方按扁平文件名解析，大概率找不到文件、按"资源缺失"处理，不抛异常）。
            }

            var name = StripCategoryPrefix(resourceId.Value);
            switch (kind)
            {
                case ResourceKind.Image: return Path.Combine(RootDir, "sprites", name + ".png");
                case ResourceKind.Audio: return Path.Combine(RootDir, "audio", name + ".wav");
                case ResourceKind.Font: return Path.Combine(RootDir, "fonts", name + ".ttf");
                case ResourceKind.DataTable: return Path.Combine(RootDir, "data", name + ".json");
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的资源种类");
            }
        }

        private static bool IsLayerCategory(string resourceRefId)
        {
            var dotIndex = resourceRefId.IndexOf('.');
            var category = dotIndex < 0 ? resourceRefId : resourceRefId.Substring(0, dotIndex);
            return category == "layer";
        }

        private static string StripCategoryPrefix(string resourceRefId)
        {
            var dotIndex = resourceRefId.IndexOf('.');
            var withoutCategory = dotIndex < 0 ? resourceRefId : resourceRefId.Substring(dotIndex + 1);
            return withoutCategory.Replace('.', '_');
        }
    }
}
