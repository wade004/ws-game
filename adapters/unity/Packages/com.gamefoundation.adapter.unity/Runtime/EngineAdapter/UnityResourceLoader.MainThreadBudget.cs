#nullable enable
// UnityResourceLoader（部分类）：[ADR-0109](../../../../../../../architecture/adr/0109-资源解码分帧与后台化.md)
// ——纹理类资源（ResourceKind.Image / ResourceKind.Effect）的 PNG 解码与按帧切块移到后台线程，
// 主线程 <see cref="UnityResourceLoader.Tick"/> 只做"建纹理 + 灌字节 + Apply + 建 Sprite"，并受
// <see cref="MainThreadBudgetMilliseconds"/> 时间预算约束、按工作单元续作。
//
// 判断记录（消费方反馈第五十八批，阻塞）：精灵单位本局首次转到某个方向档位时，UnityViewFactory 一次
// 发起几十个 sprite_anim 冷加载（每个方向 4 个身体剪辑图集 + 每件装备层 3 个图集）。此前后台线程
// 只读字节，全部解码（LoadImage 解 PNG、开 mip 链时每帧 new Texture2D + GetPixels/SetPixels +
// Apply(updateMipmaps)）都在 Tick 里于主线程一帧排空，单帧 725～866 ms。本类型的做法：
//   ① 后台：托管 PNG 解码（<see cref="ManagedPngDecoder"/>，行流式，逐行交给行接收器）+ frames.json 解析 +
//      开 mip 链时每一行直接按帧矩形分发到各帧像素块（堆上不保留整张图集），产出"每张最终纹理一块 RGBA32 字节"（<see cref="PreparedTexture"/>）；并发解码数由
//      <see cref="DecodeGate"/> 限制为 clamp(ProcessorCount-1, 1, 4)；已解码、尚未被主线程消费的字节
//      总量超过 <see cref="PreparedBytesSoftCap"/> 时后台暂缓再解码新的图集（背压，不让几十张大图集
//      的 RGBA 数据同时驻留）。
//   ② 主线程：一个工作单元 = 一张最终纹理（开 mip 链的逐帧动画一帧一张；关 mip 链时整张图集一张；
//      Image 一张）；用 Texture2D.SetPixelData 灌后台已切好的字节，mip 链仍由引擎
//      Apply(updateMipmaps: true) 生成（不手写 mip 滤波，像素结果与 1.87.0 一致）。建纹理不初始化像素
//      内存（createUninitialized，SetPixelData 随即写满）；建精灵一律用整矩形网格
//      （<see cref="CreateFullRectSprite"/>）——默认的"贴合轮廓"网格要在主线程描 alpha 轮廓，实测
//      消费方真实帧图每个 2～18 ms，是单元耗时里最大的一项，整矩形约 0.02 ms（第五十八批续作）。
//   ③ 一个资源的全部工作单元做完才写入 _effects/_sprites 缓存、才 _loaded.Add、才触发回调——
//      任何读取口永远看不到半成品；未做完的资源留在队首（<see cref="_activeCompletion"/>）下个
//      Tick 续作；回调只在主线程 Tick 内触发，完成顺序保持完成队列先进先出。
//   ④ 托管解码器不支持的 PNG 变体（调色板/16 位/隔行/灰度等）或托管解码抛异常：整个资源作为一个
//      不可分工作单元回退到 1.87.0 的主线程 LoadImage 路径（<see cref="TryDecodeImage"/>/
//      <see cref="TryDecodeEffect"/> 原样保留），并按资源 id 写一条 Warn；回退后仍失败才回调 false。
//   ⑤ mip 链开关（TextureSampling.MipChainForImages/MipChainForEffects）在发起请求时于主线程取快照，
//      随请求带到后台——同一请求的后台切块与主线程建纹理使用同一份取值；过滤模式/各向异性仍在建
//      纹理时（<see cref="ApplyTextureSampling"/>）读取。
//
// 已知限制（同步写进 ADR-0109 与汇报，逐条）：
//   1) <see cref="ResourceKind.MapLayers"/> 本次不改：仍在主线程一个完成项一次做完 LoadImage（计为
//      一个工作单元，共用同一份预算），单张地图分层图的解码不可分。
//   2) 单个工作单元不可再分：预算只能在工作单元之间生效，单张最终纹理（含 Apply 与 Sprite.Create）
//      本身耗时超过预算时，该 Tick 仍会完整做完它；回退路径（不支持的 PNG 变体）整个图集是一个工作
//      单元，长度与 1.87.0 相同。
//   3) 同一帧发起的多个冷加载不再保证在下一帧全部完成（行为变更）：完成时长 ≈ 总主线程工作量 ÷
//      预算占空比，调用方需要更快可调大 <see cref="MainThreadBudgetMilliseconds"/>（<= 0 表示不限，
//      即 1.87.0 的一帧排空）。
//   4) 后台背压的软上限只约束"已解码未消费"的字节，不含引擎侧已建好的纹理；纹理显存随资源数增长
//      的规律不变。
//   5) 开 mip 链的逐帧动画在切块时某帧矩形越界/尺寸非法，整个资源判定加载失败并记一条 Error（1.87.0
//      在此处由引擎 GetPixels 抛异常，异常会逸出 Tick；本次不再逸出）。
//   6) 后台线程读到的 TextureSampling 只有 mip 链开关；FilterMode/MapLayerAnisoLevel 等在建纹理
//      时读取当时取值。
//   7) 运行期解码出的 Image/Effect 精灵网格是整矩形（4 个顶点）而不是贴合轮廓：sprite.rect/pivot/
//      pixelsPerUnit/bounds 与 SpriteRenderer.bounds 与旧网格逐项相等、渲染像素逐字节相同（实测），
//      但 sprite.textureRect、顶点/三角形数据不同；透明区域也会被光栅化（GPU 填充率略增，不改变
//      画面）。依赖精灵网格形状的消费方代码（自定义网格遮罩/阴影投射/物理形状）不会得到"贴合轮廓"。
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    public sealed partial class UnityResourceLoader
    {
        /// <summary>
        /// [ADR-0109] 新增：<see cref="Tick"/> 每帧允许在纹理类资源（<see cref="ResourceKind.Image"/>/
        /// <see cref="ResourceKind.Effect"/>/<see cref="ResourceKind.MapLayers"/> 及其余走完成队列的种类）
        /// 主线程收尾上花费的时间预算，单位毫秒，默认 <c>4.0</c>。每做完一个工作单元检查一次，超预算
        /// 即停，剩余留待下一次 Tick；每次 Tick 至少推进一个工作单元（保证必然前进）。<c>&lt;= 0</c>
        /// 表示不限——一次 Tick 排空全部已就绪的完成项，即 1.87.0 的行为。字体/三维模型/动画剪辑三个
        /// 主线程专用队列不受预算约束。可随时修改，下一次 Tick 生效。
        /// </summary>
        public double MainThreadBudgetMilliseconds { get; set; } = 4.0;

        /// <summary>[ADR-0109] 诊断：上一次 <see cref="Tick"/> 在受预算约束的完成队列上花费的主线程
        /// 毫秒数（含回调）。</summary>
        public double LastTickDecodeMilliseconds { get; private set; }

        /// <summary>[ADR-0109] 诊断：自上次 <see cref="ResetTickDiagnostics"/>（或实例创建）以来，
        /// 单次 <see cref="Tick"/> 的最大 <see cref="LastTickDecodeMilliseconds"/>。</summary>
        public double PeakTickDecodeMilliseconds { get; private set; }

        /// <summary>[ADR-0109] 诊断：自上次 <see cref="ResetTickDiagnostics"/>（或实例创建）以来，
        /// 单个工作单元（含其回调）的最大主线程耗时毫秒数。预算只能在工作单元之间生效，所以单次 Tick
        /// 的耗时上界为 <see cref="MainThreadBudgetMilliseconds"/> 加本值。</summary>
        public double MaxWorkUnitMilliseconds { get; private set; }

        /// <summary>[ADR-0109] 诊断：上一次 <see cref="Tick"/> 做完的工作单元个数（0 表示该次没有
        /// 待处理的完成项）。</summary>
        public int LastTickWorkUnitCount { get; private set; }

        /// <summary>[ADR-0109] 诊断：当前已就绪、等待主线程处理的完成项数（完成队列中的项，加上
        /// 正在分帧处理、尚未做完的那一项）。</summary>
        public int PendingMainThreadCompletionCount =>
            _completions.Count + _mapLayersCompletions.Count + (_activeCompletion != null ? 1 : 0);

        /// <summary>[ADR-0109] 清零 <see cref="PeakTickDecodeMilliseconds"/> 与
        /// <see cref="MaxWorkUnitMilliseconds"/>，供调用方在某个观察窗口开始时重置。</summary>
        public void ResetTickDiagnostics()
        {
            PeakTickDecodeMilliseconds = 0.0;
            MaxWorkUnitMilliseconds = 0.0;
        }

        /// <summary>测试/诊断用：主线程开始处理、且走了后台托管解码路径的 Image/Effect 资源个数。</summary>
        internal int ManagedDecodeCount { get; private set; }

        /// <summary>测试/诊断用：回退到主线程 <c>LoadImage</c> 的 Image/Effect 资源个数。</summary>
        internal int MainThreadFallbackDecodeCount { get; private set; }

        private static readonly int DecodeParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1));

        /// <summary>后台并发解码闸门（进程内共享，限制内存峰值）。</summary>
        private static readonly System.Threading.SemaphoreSlim DecodeGate =
            new System.Threading.SemaphoreSlim(DecodeParallelism, DecodeParallelism);

        /// <summary>已后台解码、尚未被主线程消费的 RGBA 字节总量软上限：超过时后台暂缓解码新图集。</summary>
        private const long PreparedBytesSoftCap = 128L * 1024L * 1024L;

        private long _preparedBytes;

        /// <summary>正在分帧处理、尚未做完的完成项（留在队首，下个 Tick 续作）。</summary>
        private CompletionJob? _activeCompletion;

        /// <summary>托管解码回退 Warn 的按资源 id 去重集合。</summary>
        private readonly HashSet<Id> _warnedManagedDecodeFallback = new HashSet<Id>();

        /// <summary>后台准备好的一张最终纹理的像素（RGBA32，行序与引擎纹理一致）。</summary>
        private sealed class PreparedTexture
        {
            public readonly int Width;
            public readonly int Height;
            public byte[]? Rgba;

            public PreparedTexture(int width, int height, byte[] rgba)
            {
                Width = width;
                Height = height;
                Rgba = rgba;
            }
        }

        /// <summary>后台对一次 Image/Effect 请求的准备结果。</summary>
        private sealed class PreparedTextures
        {
            /// <summary>工作单元对应的纹理像素：Image 一张；Effect 关 mip 链一张（整图集）；Effect 开 mip
            /// 链一帧一张（按 frames.json 顺序）。</summary>
            public PreparedTexture[] Units = Array.Empty<PreparedTexture>();

            public EffectFramesDocument? Document;
            public int AtlasWidth;
            public int AtlasHeight;

            /// <summary>非空：托管解码器不支持/抛异常，主线程整体回退到 <c>LoadImage</c> 路径。</summary>
            public string? FallbackReason;

            /// <summary>true：后台已确认该资源加载失败（frames.json 解析失败，或帧矩形非法）。</summary>
            public bool Failed;

            /// <summary><see cref="Failed"/> 时若非空，主线程记为 Error 诊断；为空表示与 1.87.0 一样静默失败。</summary>
            public string? FailureReason;
        }

        /// <summary>主线程上一个完成项的续作状态。</summary>
        private sealed class CompletionJob
        {
            public readonly PendingCompletion Pending;
            public int NextUnit;
            public bool Started;
            public float PixelsPerUnit;
            public EffectFrame[]? Frames;
            public List<Texture2D>? FrameTextures;

            public CompletionJob(PendingCompletion pending)
            {
                Pending = pending;
            }
        }

        // -------------------------------------------------------------------------------------
        // 后台：准备纹理像素
        // -------------------------------------------------------------------------------------

        /// <summary>后台准备：托管解码 PNG，Effect 另外解析 frames.json，并按 <paramref name="mipChain"/>
        /// 决定是否按帧切块。永不抛异常：任何异常都记入 <see cref="PreparedTextures.FallbackReason"/>。</summary>
        private async Task<PreparedTextures> PrepareTexturesAsync(
            byte[] atlasBytes, string? framesJson, bool mipChain, bool isEffect)
        {
            var result = new PreparedTextures();
            try
            {
                EffectFramesDocument? document = null;
                if (isEffect)
                {
                    if (framesJson == null || !EffectFramesDocument.TryParse(framesJson, out document))
                    {
                        // 与 1.87.0 一致：frames.json 解析失败静默判定加载失败。
                        result.Failed = true;
                        return result;
                    }

                    result.Document = document;
                }

                await DecodeGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    while (System.Threading.Interlocked.Read(ref _preparedBytes) >= PreparedBytesSoftCap)
                    {
                        await Task.Delay(4).ConfigureAwait(false);
                    }

                    string reason;
                    if (!isEffect || !mipChain)
                    {
                        var sink = new SingleTextureSink();
                        if (!ManagedPngDecoder.TryDecode(atlasBytes, sink, out reason))
                        {
                            result.FallbackReason = reason;
                            return result;
                        }

                        result.AtlasWidth = sink.Width;
                        result.AtlasHeight = sink.Height;
                        result.Units = new[] { new PreparedTexture(sink.Width, sink.Height, sink.Pixels!) };
                    }
                    else
                    {
                        var sink = new FrameSliceSink(document!);
                        if (!ManagedPngDecoder.TryDecode(atlasBytes, sink, out reason))
                        {
                            if (sink.Failure != null)
                            {
                                result.Failed = true;
                                result.FailureReason = sink.Failure;
                            }
                            else
                            {
                                result.FallbackReason = reason;
                            }

                            return result;
                        }

                        result.AtlasWidth = sink.AtlasWidth;
                        result.AtlasHeight = sink.AtlasHeight;
                        result.Units = sink.Units!;
                    }

                    long total = 0;
                    for (var i = 0; i < result.Units.Length; i++)
                    {
                        total += result.Units[i].Rgba!.Length;
                    }

                    System.Threading.Interlocked.Add(ref _preparedBytes, total);
                    return result;
                }
                finally
                {
                    DecodeGate.Release();
                }
            }
            catch (Exception e)
            {
                result.Units = Array.Empty<PreparedTexture>();
                result.Failed = false;
                result.FallbackReason = "后台准备抛出异常：" + e.GetType().Name + " " + e.Message;
                return result;
            }
        }

        /// <summary>后台像素块的小型复用池（按长度精确匹配）。判断记录：一个方向 30 个 2048x2048 图集的压力
        /// 下，逐帧像素块（合计数百 MB）每次现分配、用完即弃，会不断触发整堆 GC，GC 停顿会把主线程
        /// 单个工作单元顶到数十毫秒（实测）；主线程用 SetPixelData 灌完一块后把数组还回池里，后台下一次
        /// 解码同尺寸帧直接复用（逐帧动画的帧尺寸在同一角色的各图集间通常相同）。取回的数组内容是脏的，
        /// 但每块都会被解码行完整覆盖后才交给主线程；上限 <see cref="MaxPooledBytes"/>，超出时直接丢弃。</summary>
        private static class PixelBufferPool
        {
            private const long MaxPooledBytes = 64L * 1024L * 1024L;

            private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Concurrent.ConcurrentStack<byte[]>> Stacks =
                new System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Concurrent.ConcurrentStack<byte[]>>();

            private static long _pooledBytes;

            public static byte[] Rent(int length)
            {
                if (Stacks.TryGetValue(length, out var stack) && stack.TryPop(out var array))
                {
                    System.Threading.Interlocked.Add(ref _pooledBytes, -length);
                    return array;
                }

                return new byte[length];
            }

            public static void Return(byte[] array)
            {
                if (System.Threading.Interlocked.Read(ref _pooledBytes) + array.Length > MaxPooledBytes)
                {
                    return;
                }

                Stacks.GetOrAdd(array.Length, _ => new System.Collections.Concurrent.ConcurrentStack<byte[]>()).Push(array);
                System.Threading.Interlocked.Add(ref _pooledBytes, array.Length);
            }
        }

        /// <summary>整张图作为一块最终纹理（Image；Effect 关 mip 链时的整张图集）。</summary>
        private sealed class SingleTextureSink : IRowSink
        {
            public int Width;
            public int Height;
            public byte[]? Pixels;

            public bool Begin(int width, int height, out string reason)
            {
                Width = width;
                Height = height;
                Pixels = PixelBufferPool.Rent(width * height * 4);
                reason = string.Empty;
                return true;
            }

            public void WriteRow(int rowFromBottom, byte[] rgba)
            {
                Buffer.BlockCopy(rgba, 0, Pixels!, rowFromBottom * Width * 4, Width * 4);
            }
        }

        /// <summary>Effect 开 mip 链：解码出的每一行直接按 frames.json 的帧矩形（原点左下，与
        /// <c>Sprite.Create</c> 的 Rect 同一套约定）分发到各帧的像素块，堆上不保留整张图集。</summary>
        private sealed class FrameSliceSink : IRowSink
        {
            private readonly EffectFramesDocument _document;
            private int[] _x = Array.Empty<int>();
            private int[] _y = Array.Empty<int>();

            public int AtlasWidth;
            public int AtlasHeight;
            public PreparedTexture[]? Units;

            /// <summary>非空：帧矩形非法/越界，属于确定的加载失败而不是需要回退的解码能力问题。</summary>
            public string? Failure;

            public FrameSliceSink(EffectFramesDocument document)
            {
                _document = document;
            }

            public bool Begin(int width, int height, out string reason)
            {
                AtlasWidth = width;
                AtlasHeight = height;
                var frames = _document.Frames;
                _x = new int[frames.Count];
                _y = new int[frames.Count];
                var units = new PreparedTexture[frames.Count];
                for (var i = 0; i < frames.Count; i++)
                {
                    var frameData = frames[i];
                    var w = (int)(frameData.Width ?? width);
                    var h = (int)(frameData.Height ?? height);
                    var x = (int)frameData.X;
                    var y = (int)frameData.Y;
                    if (w <= 0 || h <= 0 || x < 0 || y < 0 || (long)x + w > width || (long)y + h > height)
                    {
                        Failure = $"第 {i} 帧矩形 ({x}, {y}, {w}, {h}) 尺寸非法或超出图集 {width}x{height}";
                        reason = Failure;
                        return false;
                    }

                    _x[i] = x;
                    _y[i] = y;
                    units[i] = new PreparedTexture(w, h, PixelBufferPool.Rent(w * h * 4));
                }

                Units = units;
                reason = string.Empty;
                return true;
            }

            public void WriteRow(int rowFromBottom, byte[] rgba)
            {
                var units = Units!;
                for (var i = 0; i < units.Length; i++)
                {
                    var unit = units[i];
                    var localRow = rowFromBottom - _y[i];
                    if (localRow < 0 || localRow >= unit.Height)
                    {
                        continue;
                    }

                    Buffer.BlockCopy(rgba, _x[i] * 4, unit.Rgba!, localRow * unit.Width * 4, unit.Width * 4);
                }
            }
        }

        // -------------------------------------------------------------------------------------
        // 主线程：带预算的完成队列处理
        // -------------------------------------------------------------------------------------

        /// <summary>由 <see cref="Tick"/> 调用：在 <see cref="MainThreadBudgetMilliseconds"/> 约束下依次
        /// 处理 <c>_completions</c> 与 <c>_mapLayersCompletions</c>（先前者后者，共用同一份预算）。</summary>
        private void RunBudgetedCompletions()
        {
            var budget = MainThreadBudgetMilliseconds;
            var limited = budget > 0.0;
            var frequency = System.Diagnostics.Stopwatch.Frequency;
            var budgetTicks = limited ? (long)(budget * frequency / 1000.0) : long.MaxValue;
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var units = 0;
            try
            {
                while (true)
                {
                    if (units > 0 && limited && System.Diagnostics.Stopwatch.GetTimestamp() - start >= budgetTicks)
                    {
                        break;
                    }

                    var unitStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    var ran = false;
                    try
                    {
                        ran = RunOneWorkUnit();
                    }
                    finally
                    {
                        if (ran)
                        {
                            var unitMs = (System.Diagnostics.Stopwatch.GetTimestamp() - unitStart) * 1000.0 / frequency;
                            if (unitMs > MaxWorkUnitMilliseconds)
                            {
                                MaxWorkUnitMilliseconds = unitMs;
                            }
                        }
                    }

                    if (!ran)
                    {
                        break;
                    }

                    units++;
                }
            }
            finally
            {
                LastTickWorkUnitCount = units;
                LastTickDecodeMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / frequency;
                if (LastTickDecodeMilliseconds > PeakTickDecodeMilliseconds)
                {
                    PeakTickDecodeMilliseconds = LastTickDecodeMilliseconds;
                }
            }
        }

        /// <summary>做一个工作单元；没有待处理项返回 false。</summary>
        private bool RunOneWorkUnit()
        {
            var job = _activeCompletion;
            if (job == null)
            {
                if (_completions.TryDequeue(out var pending))
                {
                    job = new CompletionJob(pending);
                }
                else if (_mapLayersCompletions.TryDequeue(out var mapPending))
                {
                    // 地图分层图本次不改（见类型顶部已知限制 1），一个完成项算一个工作单元。
                    FinishMapLayersLoad(mapPending);
                    return true;
                }
                else
                {
                    return false;
                }
            }

            _activeCompletion = job;
            AdvanceCompletionJob(job);
            return true;
        }

        private void AdvanceCompletionJob(CompletionJob job)
        {
            var pending = job.Pending;
            var prepared = pending.Prepared;

            if (pending.ReadSuccess && prepared != null && prepared.FallbackReason == null)
            {
                if (prepared.Failed)
                {
                    FailJob(job, prepared.FailureReason);
                    return;
                }

                if (pending.Kind == ResourceKind.Image)
                {
                    RunPreparedImageUnit(job);
                    return;
                }

                if (pending.Kind == ResourceKind.Effect)
                {
                    if (pending.MipChain)
                    {
                        RunPreparedEffectFrameUnit(job);
                    }
                    else
                    {
                        RunPreparedEffectAtlasUnit(job);
                    }

                    return;
                }
            }

            // 不涉及后台准备结果的种类（音频/数据表/场景/导航/复用缓存的 Effect/读取失败），以及
            // 托管解码回退的 Image/Effect：整个资源是一个不可分工作单元，走 1.87.0 的主线程路径。
            _activeCompletion = null;
            FinishOnMainThread(pending);
        }

        private void FailJob(CompletionJob job, string? errorReason)
        {
            var pending = job.Pending;
            _activeCompletion = null;
            if (errorReason != null)
            {
                Debug.LogError($"[UnityResourceLoader] 资源 \"{pending.ResourceId.Value}\" 加载失败：{errorReason}（ADR-0109）。");
            }

            DestroyJobArtifacts(job);
            if (pending.Prepared != null)
            {
                ReleasePrepared(pending.Prepared);
            }

            CompleteCompletion(pending, false);
        }

        private static void DestroyJobArtifacts(CompletionJob job)
        {
            if (job.Frames != null)
            {
                for (var i = 0; i < job.Frames.Length; i++)
                {
                    if (job.Frames[i].Sprite != null)
                    {
                        UnityEngine.Object.Destroy(job.Frames[i].Sprite);
                    }
                }
            }

            if (job.FrameTextures != null)
            {
                for (var i = 0; i < job.FrameTextures.Count; i++)
                {
                    if (job.FrameTextures[i] != null)
                    {
                        UnityEngine.Object.Destroy(job.FrameTextures[i]);
                    }
                }
            }
        }

        /// <summary>资源收尾：与 1.87.0 <see cref="FinishOnMainThread"/> 同一顺序（先移出 _loading，成功才
        /// 记 _loaded，最后触发回调）。</summary>
        private void CompleteCompletion(in PendingCompletion pending, bool success)
        {
            _loading.Remove(pending.ResourceId);
            if (success)
            {
                _loaded.Add(pending.ResourceId);
            }

            pending.Callback(pending.ResourceId, success);
        }

        private void NoteManagedDecodeFallback(in PendingCompletion pending)
        {
            var reason = pending.Prepared?.FallbackReason;
            if (reason == null)
            {
                return;
            }

            MainThreadFallbackDecodeCount++;
            if (_warnedManagedDecodeFallback.Add(pending.ResourceId))
            {
                Debug.LogWarning(
                    $"[UnityResourceLoader] 资源 \"{pending.ResourceId.Value}\" 的图集无法后台解码（{reason}），" +
                    "回退到主线程 Texture2D.LoadImage；该图集的解码会整块占用主线程，存在长帧风险（ADR-0109）。");
            }
        }

        /// <summary>ADR-0109 决策 4：运行期解码出的图像 / 逐帧动画精灵一律用整矩形网格。<c>Sprite.Create</c>
        /// 的默认网格类型是"贴合轮廓"，引擎要在主线程扫描 alpha 描出轮廓再三角化（实测消费方真实
        /// 帧图 640x576 约 2～5 ms，832x1088 约 18 ms，是主线程单元里最大的一项）；整矩形网格只是四个顶点、
        /// 约 0.02 ms。<c>extrude</c> 取该重载的原默认值 0，其余参数不变；<c>sprite.rect / pivot /
        /// pixelsPerUnit / bounds</c> 与 <c>SpriteRenderer.bounds</c> 与贴合轮廓时逐项相等（实测），渲染像素
        /// 逐字节相同；差别只在 <c>textureRect</c> 与顶点/三角形数据，见 ADR-0109 已知限制 8。
        /// 回退路径（<c>TryDecodeImage / TryDecodeEffect</c>）同样经此方法建精灵。地图分层图不经此处。</summary>
        internal static Sprite CreateFullRectSprite(Texture2D texture, UnityEngine.Rect rect, Vector2 pivot, float pixelsPerUnit)
        {
            return Sprite.Create(texture, rect, pivot, pixelsPerUnit, 0u, SpriteMeshType.FullRect);
        }

        private static Texture2D BuildTexture(PreparedTexture unit, bool mipChain)
        {
            // 构造时不初始化像素内存（createUninitialized）：紧接着的 SetPixelData 会写满 mip0 的全部字节，
            // 多级渐远链由随后的 Apply(updateMipmaps) 从 mip0 重新生成，没有任何一个字节会被读到未初始化
            // 内容；实测每个 640x576 帧的构造耗时约 0.96 ms -> 0.44 ms，mip0 与像素输出逐字节不变。
            // mipCount: -1 = 引擎按尺寸算出完整链，与原来 mipChain=true 的链长一致；1 = 无渐远链。
            var texture = new Texture2D(unit.Width, unit.Height, TextureFormat.RGBA32,
                mipChain ? -1 : 1, linear: false, createUninitialized: true);
            try
            {
                texture.SetPixelData(unit.Rgba!, 0);
                texture.Apply(updateMipmaps: mipChain, makeNoLongerReadable: false);
            }
            catch
            {
                UnityEngine.Object.Destroy(texture);
                throw;
            }

            return texture;
        }

        private void ReleaseUnit(PreparedTextures prepared, PreparedTexture unit)
        {
            if (unit.Rgba == null)
            {
                return;
            }

            var pixels = unit.Rgba;
            unit.Rgba = null;
            System.Threading.Interlocked.Add(ref _preparedBytes, -pixels.Length);
            PixelBufferPool.Return(pixels);
        }

        private void ReleasePrepared(PreparedTextures prepared)
        {
            for (var i = 0; i < prepared.Units.Length; i++)
            {
                ReleaseUnit(prepared, prepared.Units[i]);
            }
        }

        private void RunPreparedImageUnit(CompletionJob job)
        {
            var pending = job.Pending;
            var prepared = pending.Prepared!;
            _activeCompletion = null;
            ManagedDecodeCount++;

            var success = false;
            Texture2D? texture = null;
            try
            {
                texture = BuildTexture(prepared.Units[0], pending.MipChain);
                ApplyTextureSampling(texture, pending.MipChain, $"图像资源 \"{pending.ResourceId.Value}\"");

                var sprite = CreateFullRectSprite(
                    texture,
                    new UnityEngine.Rect(0, 0, texture.width, texture.height),
                    ResolveImagePivot(pending.ResourceId, texture.width, texture.height),
                    ResolveImagePixelsPerUnit(pending.ResourceId));
                sprite.name = pending.ResourceId.Value;
                _sprites[pending.ResourceId] = sprite;
                success = true;
            }
            catch (Exception e)
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }

                Debug.LogError($"[UnityResourceLoader] 图像资源 \"{pending.ResourceId.Value}\" 建纹理失败：{e}（ADR-0109）。");
            }

            ReleasePrepared(prepared);
            CompleteCompletion(pending, success);
        }

        /// <summary>Effect 关 mip 链：整张图集一个工作单元（与 1.87.0 一致，全部帧共用同一张图集纹理）。</summary>
        private void RunPreparedEffectAtlasUnit(CompletionJob job)
        {
            var pending = job.Pending;
            var prepared = pending.Prepared!;
            var document = prepared.Document!;
            _activeCompletion = null;
            ManagedDecodeCount++;

            var success = false;
            Texture2D? atlasTexture = null;
            try
            {
                atlasTexture = BuildTexture(prepared.Units[0], mipChain: false);
                ApplyTextureSampling(atlasTexture, mipChainRequested: false, $"逐帧动画 \"{pending.ResourceId.Value}\" 图集");

                var pixelsPerUnit = ResolveEffectPixelsPerUnit(pending.SpriteSetId, document);
                var frames = new EffectFrame[document.Frames.Count];
                for (var i = 0; i < document.Frames.Count; i++)
                {
                    var frameData = document.Frames[i];
                    var w = (int)(frameData.Width ?? prepared.AtlasWidth);
                    var h = (int)(frameData.Height ?? prepared.AtlasHeight);
                    var pivot = ResolveEffectPivot(pending.ResourceId, pending.SpriteSetId, document, w, h);
                    var sprite = CreateFullRectSprite(
                        atlasTexture,
                        new UnityEngine.Rect((float)frameData.X, (float)frameData.Y, w, h),
                        pivot,
                        pixelsPerUnit);
                    sprite.name = $"{pending.ResourceId.Value}_frame{i}";
                    frames[i] = new EffectFrame(sprite, frameData.Duration);
                }

                _effects[pending.ResourceId] = new EffectAsset(frames, document.Loop);
                _effectSpriteSetIdByResource[pending.ResourceId] = pending.SpriteSetId;
                success = true;
            }
            catch (Exception e)
            {
                if (atlasTexture != null)
                {
                    UnityEngine.Object.Destroy(atlasTexture);
                }

                Debug.LogError($"[UnityResourceLoader] 逐帧动画 \"{pending.ResourceId.Value}\" 建图集纹理失败：{e}（ADR-0109）。");
            }

            ReleasePrepared(prepared);
            CompleteCompletion(pending, success);
        }

        /// <summary>Effect 开 mip 链：一帧一个工作单元；最后一帧做完才整体写入缓存并回调。</summary>
        private void RunPreparedEffectFrameUnit(CompletionJob job)
        {
            var pending = job.Pending;
            var prepared = pending.Prepared!;
            var document = prepared.Document!;
            var count = prepared.Units.Length;

            if (!job.Started)
            {
                job.Started = true;
                ManagedDecodeCount++;
                job.Frames = new EffectFrame[count];
                job.FrameTextures = new List<Texture2D>(count);
                job.PixelsPerUnit = ResolveEffectPixelsPerUnit(pending.SpriteSetId, document);
            }

            if (job.NextUnit < count)
            {
                try
                {
                    var i = job.NextUnit;
                    var unit = prepared.Units[i];
                    var frameData = document.Frames[i];
                    var pivot = ResolveEffectPivot(pending.ResourceId, pending.SpriteSetId, document, unit.Width, unit.Height);

                    var frameTexture = BuildTexture(unit, mipChain: true);
                    job.FrameTextures!.Add(frameTexture);
                    ApplyTextureSampling(frameTexture, mipChainRequested: true,
                        $"逐帧动画 \"{pending.ResourceId.Value}\" 第 {i} 帧");

                    var sprite = CreateFullRectSprite(
                        frameTexture, new UnityEngine.Rect(0, 0, unit.Width, unit.Height), pivot, job.PixelsPerUnit);
                    sprite.name = $"{pending.ResourceId.Value}_frame{i}";
                    job.Frames![i] = new EffectFrame(sprite, frameData.Duration);

                    ReleaseUnit(prepared, unit);
                    job.NextUnit = i + 1;
                }
                catch (Exception e)
                {
                    FailJob(job, $"第 {job.NextUnit} 帧建纹理失败：{e}");
                    return;
                }

                if (job.NextUnit < count)
                {
                    return;
                }
            }

            _activeCompletion = null;
            _effectFrameTextures[pending.ResourceId] = job.FrameTextures!;
            _effects[pending.ResourceId] = new EffectAsset(job.Frames!, document.Loop);
            _effectSpriteSetIdByResource[pending.ResourceId] = pending.SpriteSetId;
            ReleasePrepared(prepared);
            CompleteCompletion(pending, true);
        }
    }
}
