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
//     DataTable -> "data/<name>.json"
//   根目录固定为 Application.streamingAssetsPath（跨平台只读只读资源目录，桌面平台上是普通
//   文件系统路径，可直接用 System.IO 同步读取，见下）。Font 种类不走这条规则（不是
//   StreamingAssets 下的裸字节，而是 Resources/Fonts/<name> 下已被资产管线导入好的字体资产，
//   见下"判断记录（Font 资源种类）"）。
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
// 判断记录（Font 资源种类，缺口 1 已解决——约定见包 README"资源 id → 路径规则"）：Unity 运行期
// 没有公开 API 能把任意字体文件字节数组转换成可用于 TMP 渲染的字体资产
// （TMP_FontAsset.CreateFontAsset 需要一个已被 Unity 资产管线导入过的 UnityEngine.Font 对象，
// 而非裸字节），因此 Font 种类不能沿用其它种类"后台线程读字节 + Task.Run"的通用路径。约定：
// 字体资源 id 形如 "font.<name>" 时，<name> 为该 id 去掉 "font." 前缀、点号换下划线后的结果
// （与其它种类共用同一条 StripCategoryPrefix 规则），对应一个已被 Unity 资产管线预先导入好的
// Font 资产，路径固定为 "Resources/Fonts/<name>"（该资产必须实际存在于某个 Resources/Fonts/
// 目录下，才能被 Resources.Load<Font> 取到——本仓库当前由 build.ps1 -SyncContent 把
// assets/_placeholder/fonts/*.otf|*.ttf 同步进 adapters/unity/Assets/Framework/Resources/Fonts/，
// 见该脚本判断记录）。Resources.Load 只能在主线程调用，因此 LoadAsync 对 Font 种类不走
// Task.Run 后台字节读取，而是把请求排入 Tick() 处理的专用队列，在下一次 Tick（仍由
// UnityEngineHost.Update 驱动）里于主线程调用 Resources.Load<Font> 完成判定——资产存在即视为
// "已加载"（IsLoaded 返回 true），不存在则按"加载失败"回调 false，保持"回调总在 Tick 里于
// 主线程排队执行"这条既有线程约定不变。加载成功后的 UnityEngine.Font 对象供
// UnityUISurface.ResolveFontAsset 取用以生成/复用对应的 TMP_FontAsset（该步骤仍需要
// TMP_FontAsset.CreateFontAsset，逻辑见 UnityUISurface.cs）。
//
// Scene/NavMesh/Effect 三个种类（ADR-0016 决策 5 新增，取代此前 SceneRouter 借用
// ResourceKind.DataTable 的工作绕）：
//   Scene/NavMesh —— 与 DataTable 同一套"读文本、只校验存在性"处理：SceneRouter 只关心加载
//     成功/失败，从不解析内容（见 SceneRouter.cs 类型注释），本加载器分别落到
//     "scene/<name>.json"/"nav_mesh/<name>.json" 两个独立子目录（build.ps1 生成对应占位文件）。
//   Effect —— vfx.def.resource_ref 指向一个目录（"vfx/<name>/atlas.png" + "frames.json"，
//     结构同 assets/_placeholder/vfx/<name>/，见 toolchain/gen_placeholder_assets.py 产出），
//     不是单一文件；本加载器为 Effect 单独走一条"同时读 atlas 字节 + frames.json 文本"的后台
//     加载路径，主线程按 frames.json 描述的帧矩形切出多张 Sprite，装配成 EffectAsset 供
//     UnityRenderer2D.EmitParticle 优先使用（找不到时回退内建通用粒子效果）。
//
// W6-B 新增（ADR-0017 决策 a/b，ResourceKind.Model）：三维模型预制体不是"任意压缩字节数组"，
// 与 Font 同一处境——运行期没有公开 API 能把裸字节反序列化成可用的 GameObject 层级/骨骼/
// Animator 绑定，只能消费已经被 Unity 资产管线预先导入好的资源，经 Resources.Load<GameObject>
// 取用（同 Font 种类"判断记录"的同一约束，见类型顶部该节）。约定：模型资源 id 形如
// "model.<name>" 时，<name> 为该 id 去掉 "model." 前缀、点号换下划线后的结果（与其它种类共用
// 同一条 StripCategoryPrefix 规则），对应路径固定为
// "Resources/GameFoundation/models/<name>"（该预制体必须实际存在于某个
// Resources/GameFoundation/models/ 目录下才能被 Resources.Load<GameObject> 取到；本迭代由
// Editor/GeneratePlaceholderModelAssets.cs 一次性生成 placeholder_biped 并提交生成结果，见该
// 脚本与包 README"资源路径约定"一节）。Resources.Load 只能在主线程调用，因此 LoadAsync 对
// Model 种类同 Font 一样不走 Task.Run 后台字节读取路径，改为把请求排入 Tick() 处理的专用队列，
// 在下一次 Tick 里于主线程调用 Resources.Load<GameObject> 完成判定——资产存在即视为"已加载"
// （IsLoaded 返回 true，且缓存进 <see cref="_modelPrefabs"/> 供 <see cref="TryGetModelPrefab"/>/
// <see cref="UnityRenderer3D.CreateModelInstance"/> 取用），不存在则按"加载失败"回调 false，
// 保持"回调总在 Tick 里于主线程排队执行"这条既有线程约定不变。
//
// 与 Model 同一套 Resources.Load 约定的姊妹路径——动画剪辑资产（供 model 型 <c>display.anim_set</c>
// 消费，见 <see cref="ResolveAnimClipResourcesPath"/>）：<c>display.anim_set.clips[*].resource_ref</c>
// 对 model 型剪辑指向一个已导入的 <see cref="UnityEngine.AnimationClip"/> 资产，路径固定为
// "Resources/GameFoundation/anim_clips/<name>"，与 <see cref="AnimClipResolver"/>/
// <see cref="Adapter.Unity.Presentation.UnityViewFactory"/> 把 <c>events</c> 数据驱动写回该资产的
// <c>AnimationClip.events</c>（关键帧事件）配合使用，见两者判断记录——本类型不直接消费这条约定
// （不需要 LoadAsync/IsLoaded 语义，Resources.Load<AnimationClip> 由调用方直接同步调用），只提供
// 路径解析这一静态辅助方法，避免调用方各自重复实现"去类别前缀、点号换下划线"这条既有规则。
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityResourceLoader : IResourceLoader
    {
        /// <summary>解码出的一帧序列帧动画（<see cref="EffectAsset"/> 的元素）。</summary>
        public readonly struct EffectFrame
        {
            public readonly Sprite Sprite;
            public readonly double Duration;

            public EffectFrame(Sprite sprite, double duration)
            {
                Sprite = sprite;
                Duration = duration;
            }
        }

        /// <summary>一个 <c>ResourceKind.Effect</c> 资源解码后的可播放形态：按 frames.json 顺序切好
        /// 的 Sprite 序列 + 每帧时长 + 是否循环。</summary>
        public sealed class EffectAsset
        {
            public EffectFrame[] Frames { get; }
            public bool Loop { get; }

            public EffectAsset(EffectFrame[] frames, bool loop)
            {
                Frames = frames;
                Loop = loop;
            }
        }

        /// <summary>一次 Font 种类的加载请求，排队等到下一次 <see cref="Tick"/> 在主线程调用
        /// <c>Resources.Load</c> 完成判定（见类型顶部"Font 资源种类"判断记录）。</summary>
        private struct PendingFontLoad
        {
            public Id ResourceId;
            public string ResourcesFontName;
            public LoadCallback Callback;
        }

        /// <summary>一次 <see cref="ResourceKind.Model"/> 种类的加载请求，排队等到下一次
        /// <see cref="Tick"/> 在主线程调用 <see cref="TryLoadModelSync"/> 完成判定（见类型
        /// 顶部"W6-B 新增"判断记录，同 <see cref="PendingFontLoad"/> 同一套处理惯例）。PR140-02
        /// 文档漂移根治：路径解析统一收到 <see cref="TryLoadModelSync"/>/<see cref="ResolveModelResourcesPath"/>
        /// 里，本结构不再单独持有一份重复解析出来的路径。</summary>
        private struct PendingModelLoad
        {
            public Id ResourceId;
            public LoadCallback Callback;
        }

        private struct PendingCompletion
        {
            public Id ResourceId;
            public ResourceKind Kind;
            public byte[]? Bytes;
            public bool ReadSuccess;
            public LoadCallback Callback;

            /// <summary>仅 <see cref="ResourceKind.Effect"/> 使用：frames.json 的文本内容
            /// （<see cref="Bytes"/> 此时承载 atlas.png 的字节）。</summary>
            public string? EffectFramesJson;
        }

        private static readonly string RootDir = Path.Combine(Application.streamingAssetsPath, "GameFoundation");

        /// <summary>仅 <see cref="ResourceKind.Font"/> 使用：主线程专用队列（见类型顶部"Font 资源
        /// 种类"判断记录），不与 <see cref="_completions"/> 共用——后者由后台线程写入，前者只在
        /// 主线程内部排队等到下一次 <see cref="Tick"/> 处理，不需要并发安全的队列类型。</summary>
        private readonly Queue<PendingFontLoad> _pendingFontLoads = new Queue<PendingFontLoad>();

        /// <summary>仅 <see cref="ResourceKind.Model"/> 使用：主线程专用队列，同
        /// <see cref="_pendingFontLoads"/> 同一套惯例（见类型顶部"W6-B 新增"判断记录）。</summary>
        private readonly Queue<PendingModelLoad> _pendingModelLoads = new Queue<PendingModelLoad>();

        private readonly HashSet<Id> _loading = new HashSet<Id>();
        private readonly HashSet<Id> _loaded = new HashSet<Id>();
        private readonly ConcurrentQueue<PendingCompletion> _completions = new ConcurrentQueue<PendingCompletion>();

        private readonly Dictionary<Id, Sprite> _sprites = new Dictionary<Id, Sprite>();
        private readonly Dictionary<Id, AudioClip> _audioClips = new Dictionary<Id, AudioClip>();
        private readonly Dictionary<Id, Font> _fonts = new Dictionary<Id, Font>();
        private readonly Dictionary<Id, string> _dataTableText = new Dictionary<Id, string>();
        private readonly Dictionary<Id, string> _sceneText = new Dictionary<Id, string>();
        private readonly Dictionary<Id, string> _navMeshText = new Dictionary<Id, string>();
        private readonly Dictionary<Id, EffectAsset> _effects = new Dictionary<Id, EffectAsset>();

        /// <summary>W6-B 新增：<see cref="ResourceKind.Model"/> 已加载的预制体资产缓存，供
        /// <see cref="TryGetModelPrefab"/>/<see cref="UnityRenderer3D.CreateModelInstance"/> 取用。</summary>
        private readonly Dictionary<Id, GameObject> _modelPrefabs = new Dictionary<Id, GameObject>();

        /// <summary>本加载器使用的像素-单位换算比，供 Sprite.Create 使用；与
        /// architecture/14_资产规格书模板.md 第 2.2 节"pixels_per_unit"游戏填写项对应，
        /// 框架层给一个可运行的默认值 100，具体项目在其接入记录里覆盖（本加载器不读取
        /// 项目专属配置，避免 L-1 反向依赖游戏内容）。</summary>
        public float PixelsPerUnit { get; set; } = 100f;

        public void LoadAsync(Id resourceId, ResourceKind kind, LoadCallback callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            _loading.Add(resourceId);

            if (kind == ResourceKind.Font)
            {
                // Resources.Load 只能在主线程调用，不走后台 Task.Run 字节读取路径，见类型顶部
                // "Font 资源种类"判断记录。
                _pendingFontLoads.Enqueue(new PendingFontLoad
                {
                    ResourceId = resourceId,
                    ResourcesFontName = "Fonts/" + StripCategoryPrefix(resourceId.Value),
                    Callback = callback
                });
                return;
            }

            if (kind == ResourceKind.Model)
            {
                // Resources.Load 只能在主线程调用，不走后台 Task.Run 字节读取路径，见类型顶部
                // "W6-B 新增"判断记录，同 Font 种类同一套处理。
                _pendingModelLoads.Enqueue(new PendingModelLoad
                {
                    ResourceId = resourceId,
                    Callback = callback
                });
                return;
            }

            if (kind == ResourceKind.Effect)
            {
                var effectDir = ResolveEffectDir(resourceId);
                Task.Run(() =>
                {
                    byte[]? atlasBytes = null;
                    string? framesJson = null;
                    var ok = false;
                    try
                    {
                        var atlasPath = Path.Combine(effectDir, "atlas.png");
                        var framesPath = Path.Combine(effectDir, "frames.json");
                        if (File.Exists(atlasPath) && File.Exists(framesPath))
                        {
                            atlasBytes = File.ReadAllBytes(atlasPath);
                            framesJson = File.ReadAllText(framesPath);
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
                        Bytes = atlasBytes,
                        EffectFramesJson = framesJson,
                        ReadSuccess = ok,
                        Callback = callback
                    });
                });
                return;
            }

            var path = ResolvePath(resourceId, kind);

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

        /// <summary>测试/诊断用：仍在等待后台线程读取完成、尚未在主线程 <see cref="Tick"/> 处理完的
        /// 资源加载请求数（<see cref="LoadAsync"/> 里 <c>_loading.Add</c>，<see cref="FinishOnMainThread"/>/
        /// <see cref="FinishFontLoad"/> 里 <c>_loading.Remove</c>）。判断记录（PlayMode 测试根治
        /// "相邻重负载用例的迟到异步日志"用）：本加载器把实际文件读取丢进 <c>Task.Run</c> 后台线程
        /// （见类型顶部"加载方式"判断记录），结果排进 <see cref="_completions"/>，真正的解码/诊断
        /// 日志只在下一次 <see cref="Tick"/>（由 <c>UnityEngineHost.Update</c> 每帧调用）于主线程
        /// 处理完成时才发生——若某条 PlayMode 用例在后台线程尚未写回结果时就结束（场景卸载/
        /// NUnit 进入下一条用例），这次迟到的 <see cref="Tick"/> 处理会在下一条完全无关的用例执行
        /// 窗口内触发，产生的任何 <c>Debug.LogWarning</c>/<c>LogError</c> 被 Unity Test Framework
        /// 记到那条无辜用例头上。触发大量资源首次引用的重负载 PlayMode 套件（见
        /// <c>VerticalSliceTests</c>）应在收尾（<c>[UnityTearDown]</c>）轮询本属性直到归零，让全部
        /// 异步加载在本用例自己的执行窗口内落地，见该测试类判断记录。</summary>
        public int PendingLoadCount => _loading.Count;

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
            _fonts.Remove(resourceId);
            _dataTableText.Remove(resourceId);
            _sceneText.Remove(resourceId);
            _navMeshText.Remove(resourceId);
            _effects.Remove(resourceId);
            _modelPrefabs.Remove(resourceId);
        }

        /// <summary>由 UnityEngineHost.Update 每帧调用：把后台线程读完的文件字节在主线程完成
        /// 引擎侧解码并触发调用方回调。</summary>
        internal void Tick()
        {
            while (_pendingFontLoads.Count > 0)
            {
                FinishFontLoad(_pendingFontLoads.Dequeue());
            }

            while (_pendingModelLoads.Count > 0)
            {
                FinishModelLoad(_pendingModelLoads.Dequeue());
            }

            while (_completions.TryDequeue(out var pending))
            {
                FinishOnMainThread(pending);
            }
        }

        /// <summary>在主线程完成一次 Font 资源的加载判定：路径存在的已导入字体资产即视为
        /// "已加载"（见类型顶部"Font 资源种类"判断记录），不存在则按"加载失败"回调 false。</summary>
        private void FinishFontLoad(PendingFontLoad pending)
        {
            _loading.Remove(pending.ResourceId);

            var font = Resources.Load<Font>(pending.ResourcesFontName);
            if (font == null)
            {
                pending.Callback(pending.ResourceId, false);
                return;
            }

            _fonts[pending.ResourceId] = font;
            _loaded.Add(pending.ResourceId);
            pending.Callback(pending.ResourceId, true);
        }

        /// <summary>在主线程完成一次 <see cref="ResourceKind.Model"/> 资源的加载判定：路径存在的
        /// 已导入预制体资产即视为"已加载"并缓存进 <see cref="_modelPrefabs"/>（见类型顶部"W6-B 新增"
        /// 判断记录），不存在则按"加载失败"回调 false。PR140-02 文档漂移根治：复用
        /// <see cref="TryLoadModelSync"/> 完成实际解析与缓存写入，与同步路径共用同一份逻辑，不再各自
        /// 独立调用 <c>Resources.Load</c>，见该方法判断记录。</summary>
        private void FinishModelLoad(PendingModelLoad pending)
        {
            _loading.Remove(pending.ResourceId);

            var success = TryLoadModelSync(pending.ResourceId, out _);
            pending.Callback(pending.ResourceId, success);
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
                case ResourceKind.DataTable:
                    _dataTableText[pending.ResourceId] = System.Text.Encoding.UTF8.GetString(pending.Bytes);
                    success = true;
                    break;
                case ResourceKind.Scene:
                    _sceneText[pending.ResourceId] = System.Text.Encoding.UTF8.GetString(pending.Bytes);
                    success = true;
                    break;
                case ResourceKind.NavMesh:
                    _navMeshText[pending.ResourceId] = System.Text.Encoding.UTF8.GetString(pending.Bytes);
                    success = true;
                    break;
                case ResourceKind.Effect:
                    success = pending.EffectFramesJson != null &&
                        TryDecodeEffect(pending.ResourceId, pending.Bytes, pending.EffectFramesJson);
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
                new UnityEngine.Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit);
            sprite.name = resourceId.Value;
            _sprites[resourceId] = sprite;
            return true;
        }

        /// <summary>解析 <c>frames.json</c>（结构见 <c>assets/_placeholder/vfx/&lt;name&gt;/frames.json</c>：
        /// <c>{frame_w,frame_h,fps,frame_duration,loop,frames:[{index,x,y,w,h,duration}]}</c>）+
        /// <c>atlas.png</c> 字节，按每帧矩形从图集切出 Sprite，装配成 <see cref="EffectAsset"/>。
        /// <c>frames.json</c> 顶层 <c>loop</c> 缺省为 <c>false</c>；每帧 <c>duration</c> 缺省时退回顶层
        /// <c>frame_duration</c>，仍缺省时退回 <c>1/fps</c>（<c>fps</c> 缺省 12）。</summary>
        private bool TryDecodeEffect(Id resourceId, byte[] atlasBytes, string framesJson)
        {
            JsonObject root;
            try
            {
                root = (JsonObject)JsonReader.Parse(framesJson);
            }
            catch
            {
                return false;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(atlasBytes))
            {
                UnityEngine.Object.Destroy(texture);
                return false;
            }

            var loop = root.TryGetValue("loop", out var loopVal) && loopVal is JsonBool loopBool && loopBool.Value;
            var fps = root.TryGetValue("fps", out var fpsVal) && fpsVal is JsonNumber fpsNum ? fpsNum.Value : 12.0;
            var defaultDuration = root.TryGetValue("frame_duration", out var fdVal) && fdVal is JsonNumber fdNum
                ? fdNum.Value
                : (fps > 0 ? 1.0 / fps : 0.05);

            if (!root.TryGetValue("frames", out var framesVal) || !(framesVal is JsonArray framesArr))
            {
                UnityEngine.Object.Destroy(texture);
                return false;
            }

            var frames = new EffectFrame[framesArr.Count];
            for (var i = 0; i < framesArr.Count; i++)
            {
                if (!(framesArr[i] is JsonObject frameObj))
                {
                    UnityEngine.Object.Destroy(texture);
                    return false;
                }

                var x = ReadNumber(frameObj, "x", 0);
                var y = ReadNumber(frameObj, "y", 0);
                var w = ReadNumber(frameObj, "w", texture.width);
                var h = ReadNumber(frameObj, "h", texture.height);
                var duration = frameObj.TryGetValue("duration", out var durVal) && durVal is JsonNumber durNum
                    ? durNum.Value
                    : defaultDuration;

                var sprite = Sprite.Create(
                    texture,
                    new UnityEngine.Rect((float)x, (float)y, (float)w, (float)h),
                    new Vector2(0.5f, 0.5f),
                    PixelsPerUnit);
                sprite.name = $"{resourceId.Value}_frame{i}";

                frames[i] = new EffectFrame(sprite, duration);
            }

            _effects[resourceId] = new EffectAsset(frames, loop);
            return true;
        }

        private static double ReadNumber(JsonObject obj, string key, double fallback) =>
            obj.TryGetValue(key, out var val) && val is JsonNumber num ? num.Value : fallback;

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

        /// <summary>供 <see cref="Adapter.Unity.EngineAdapter.UnityUISurface"/> 按 <c>fontId</c>
        /// 取回已加载的 <see cref="UnityEngine.Font"/> 资产（缺口 1 已解决，见类型顶部"Font 资源
        /// 种类"判断记录），未加载/找不到时返回 false，调用方自行回退默认字体。</summary>
        public bool TryGetFont(Id resourceId, out Font font) => _fonts.TryGetValue(resourceId, out font!);

        public bool TryGetDataTableText(Id resourceId, out string text) => _dataTableText.TryGetValue(resourceId, out text!);

        public bool TryGetSceneText(Id resourceId, out string text) => _sceneText.TryGetValue(resourceId, out text!);

        public bool TryGetNavMeshText(Id resourceId, out string text) => _navMeshText.TryGetValue(resourceId, out text!);

        /// <summary>供 <see cref="UnityRenderer2D.EmitParticle"/> 按 <c>effectId</c> 取回已解码的
        /// 序列帧特效资产（<see cref="ResourceKind.Effect"/>，未加载/加载失败时返回 false，调用方
        /// 回退播放内建通用效果，见该方法判断记录）。</summary>
        public bool TryGetEffect(Id resourceId, out EffectAsset asset) => _effects.TryGetValue(resourceId, out asset!);

        /// <summary>W6-B 新增：供 <see cref="UnityRenderer3D.CreateModelInstance"/> 按 <c>modelId</c>
        /// 取回已加载的模型预制体（见类型顶部"W6-B 新增"判断记录）。未加载/找不到时返回 false——
        /// <see cref="UnityRenderer3D.CreateModelInstance"/> 据此回退为 <see cref="TryLoadModelSync"/>
        /// （该方法契约本身是同步的，不能等待 <see cref="LoadAsync"/> 走完 Tick 排队，见其判断记录），
        /// 两条路径共用同一个 <see cref="ResolveModelResourcesPath"/> 约定，互不冲突。</summary>
        public bool TryGetModelPrefab(Id resourceId, out GameObject prefab) => _modelPrefabs.TryGetValue(resourceId, out prefab!);

        /// <summary>
        /// 判断记录（PR140-02 文档漂移根治，取代此前"<see cref="UnityRenderer3D.CreateModelInstance"/>
        /// 缓存未命中时自己直接调用 <c>UnityEngine.Resources.Load</c>"的立场——
        /// <c>architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md</c> 决策 1"renderer 只消费
        /// <see cref="IResourceLoader"/> 已加载/占位资源，不隐式加载"）：本方法把"按 modelId 同步解析
        /// 一次 Resources 资产"这件事从 <see cref="UnityRenderer3D"/> 挪进本加载器——<c>CreateModelInstance</c>
        /// 契约本身是同步的，无法像 <see cref="LoadAsync"/> 那样排队等 <see cref="Tick"/>，仍然需要一条
        /// 同步解析路径，但"谁来碰 <c>UnityEngine.Resources</c>"应当固定只有本加载器一处，不是"renderer
        /// 也顺手自己调一次"——这正是决策 1 的字面要求（"renderer 不隐式加载"，不隐式加载不等于"渲染器
        /// 自己不调用 Resources.Load 就算了、缓存旁路可以"，而是"渲染器只应该经由 <see cref="IResourceLoader"/>
        /// 拿资源"）。命中缓存（<see cref="_modelPrefabs"/>，<see cref="LoadAsync"/>/本方法此前已经解析
        /// 成功过的同一个 <paramref name="resourceId"/>）时直接复用，不重复调用 Resources.Load；未命中
        /// 时同步调用一次并写回缓存——与 <see cref="FinishModelLoad"/>（<see cref="LoadAsync"/> 异步路径
        /// 排队处理后走到的方法）共用同一份缓存写入逻辑，本方法与
        /// <see cref="LoadAsync"/>/<see cref="Tick"/> 解析的是完全同一套状态，不是两套互相独立、可能
        /// 产生不同结果的实现。
        /// </summary>
        public bool TryLoadModelSync(Id resourceId, out GameObject prefab)
        {
            if (_modelPrefabs.TryGetValue(resourceId, out prefab!))
            {
                return true;
            }

            var path = ResolveModelResourcesPath(resourceId);
            var loaded = Resources.Load<GameObject>(path);
            if (loaded == null)
            {
                prefab = null!;
                return false;
            }

            _modelPrefabs[resourceId] = loaded;
            _loaded.Add(resourceId);
            prefab = loaded;
            return true;
        }

        /// <summary>W6-B 新增：把 <see cref="ResourceKind.Model"/> 种类资源引用 id 解析为
        /// <c>Resources.Load</c> 可消费的相对路径（不含扩展名，见类型顶部"W6-B 新增"判断记录）。</summary>
        public static string ResolveModelResourcesPath(Id resourceId) =>
            "GameFoundation/models/" + StripCategoryPrefix(resourceId.Value);

        /// <summary>W6-B 新增：把 model 型 <c>display.anim_set.clips[*].resource_ref</c> 解析为
        /// <c>Resources.Load&lt;AnimationClip&gt;</c> 可消费的相对路径，与 <see cref="ResolveModelResourcesPath"/>
        /// 同一套 <see cref="StripCategoryPrefix"/> 规则、不同子目录（见类型顶部"与 Model 同一套
        /// Resources.Load 约定的姊妹路径"判断记录）。</summary>
        public static string ResolveAnimClipResourcesPath(Id resourceId) =>
            "GameFoundation/anim_clips/" + StripCategoryPrefix(resourceId.Value);

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
                case ResourceKind.DataTable: return Path.Combine(RootDir, "data", name + ".json");
                case ResourceKind.Scene: return Path.Combine(RootDir, "scene", name + ".json");
                case ResourceKind.NavMesh: return Path.Combine(RootDir, "nav_mesh", name + ".json");
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind,
                    "未知的资源种类（Effect 走 ResolveEffectDir，Font 走 Resources.Load，均不经本方法）");
            }
        }

        /// <summary>解析 <c>ResourceKind.Effect</c> 资源 id 到目录（不是单一文件，见类型顶部
        /// 注释）：<c>GameFoundation/vfx/&lt;资源引用id去掉类别前缀、点号换下划线&gt;/</c>，
        /// 与 <c>assets/_placeholder/vfx/&lt;name&gt;/</c> 同一套命名（build.ps1 把前者整棵目录
        /// 同步到 StreamingAssets 时保持该相对路径不变）。</summary>
        public static string ResolveEffectDir(Id resourceId) =>
            Path.Combine(RootDir, "vfx", StripCategoryPrefix(resourceId.Value));

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
