using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Rng;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    /// <summary>
    /// ADR-0083 第一部分定位问题 1 的实测用例："冷资源从 play_sfx 派发到真实播放开始，实测耗时
    /// 多少，会不会超过消费方 0.4 秒轮询窗口"——任务要求"不要估算，写一个能出数字的用例给出真实
    /// 测量值"，本文件即该用例。
    /// <para>
    /// 判断记录（为什么不直接跑 Unity PlayMode 测量，改为在 xUnit 里复刻同构架构）：本次改动的
    /// 工作树在系统临时目录下的 scratchpad 深层路径，按 AGENTS.md"不要在深层 scratchpad 工作树里
    /// 跑 Unity 测试步骤"，Unity 侧真实验证交由主检出补跑。真正决定这段延迟数量级的不是 Unity
    /// 引擎 API 本身的耗时，而是 <c>adapters/unity/.../UnityResourceLoader.LoadAsync</c> 采用的
    /// 架构模式——见该类型判断记录"加载方式"：后台线程 <c>Task.Run</c> 做纯文件字节读取，读完
    /// 结果放进线程安全队列，真正的解码 + 触发回调全部在下一次 <c>Tick</c>（由
    /// <c>UnityEngineHost.Update</c> 每帧调用一次）里于主线程完成。占位音效文件本身很小
    /// （<c>assets/_placeholder/sfx/ui_click_01.wav</c> 实际 PCM 数据体量 4410 字节，见
    /// <c>toolchain/gen_placeholder_assets.py</c>），文件系统层面的字节读取不是耗时主体，真正的
    /// 耗时来源是"排队等下一次按帧轮询消费"这一架构性延迟，与具体引擎 API 无关，可以在不依赖
    /// UnityEngine 的前提下用同构模型忠实复现并实测。本用例因此原样复刻这套"后台线程真实文件 IO +
    /// 并发队列 + 按帧 Tick 消费"架构（用与占位音效等字节数的临时文件，避免依赖脆弱的仓库根路径
    /// 推导），驱动 <see cref="SfxPlayer"/> 的生产代码路径，用 <see cref="Stopwatch"/> 测量从
    /// <see cref="SfxPlayer.Play"/> 调用到 <see cref="IAudio.PlaySfx"/> 真正被调用之间的真实墙钟
    /// 时间。这是"该架构模式本身"的真实下界实测，不是 Unity 引擎逐帧开销/AudioClip 解码开销的
    /// 精确复现——两者数量级一致（毫秒级线程调度+IO 等待，量级上远小于消费方 0.4 秒窗口），但绝对
    /// 数值会因引擎、硬件而有出入，如实记入 ADR-0083、不假装是 Unity 内的实测值。
    /// </para>
    /// </summary>
    public class SfxColdLoadTimingTests
    {
        private static readonly Id ColdSfxId = new Id("sfx.cold_timing_probe");
        private static readonly Id ColdResourceId = new Id("res.cold_timing_probe");

        /// <summary>复刻 <c>UnityResourceLoader</c> 的"后台线程读字节 + 并发队列 + 按帧 Tick 在主
        /// 线程消费"架构（见该类型判断记录"加载方式"），<see cref="LoadAsync"/> 内用真实
        /// <see cref="Task.Run(Action)"/> 做真实磁盘文件读取，不是内存桩——测的是这套架构本身的
        /// 真实延迟数量级，不是纯逻辑分支覆盖。</summary>
        private sealed class FrameTickResourceLoader : IResourceLoader
        {
            private readonly string _filePath;
            private readonly ConcurrentQueue<(Id ResourceId, bool Ok, LoadCallback Callback)> _completions = new ConcurrentQueue<(Id, bool, LoadCallback)>();
            private readonly HashSet<Id> _loaded = new HashSet<Id>();

            public FrameTickResourceLoader(string filePath)
            {
                _filePath = filePath;
            }

            public void LoadAsync(Id resourceId, ResourceKind kind, LoadCallback callback)
            {
                Task.Run(() =>
                {
                    byte[]? bytes = null;
                    var ok = false;
                    try
                    {
                        bytes = File.ReadAllBytes(_filePath);
                        ok = bytes.Length > 0;
                    }
                    catch
                    {
                        ok = false;
                    }

                    _completions.Enqueue((resourceId, ok, callback));
                });
            }

            public bool IsLoaded(Id resourceId) => _loaded.Contains(resourceId);

            public double GetLoadProgress(Id resourceId) => _loaded.Contains(resourceId) ? 1.0 : 0.0;

            public void Unload(Id resourceId) => _loaded.Remove(resourceId);

            /// <summary>模拟 <c>UnityEngineHost.Update</c> 每帧调用一次的 <c>Tick</c>：在"主线程"
            /// （测试里就是调用本方法的那个线程）排空完成队列并触发回调，与生产实现同一顺序约束
            /// （回调总在某次 Tick 内、不在后台线程直接触发）。</summary>
            public int Tick()
            {
                var processed = 0;
                while (_completions.TryDequeue(out var item))
                {
                    if (item.Ok)
                    {
                        _loaded.Add(item.ResourceId);
                    }
                    item.Callback(item.ResourceId, item.Ok);
                    processed++;
                }
                return processed;
            }
        }

        [Fact]
        public void ColdLoad_RequestToPlaySfx_MeasuredLatency_WellUnder400msPollingWindow()
        {
            // 与 assets/_placeholder/sfx/ui_click_01.wav 实际 PCM 数据体量相同大小的临时文件
            // （2205 帧 * 2 字节/帧 = 4410 字节，见 toolchain/gen_placeholder_assets.py synth_sweep(0.05, ...)
            // 与该文件解码结果），复刻同一 IO 体量，不读取仓库内真实资产（避免依赖 dotnet test
            // 工作目录到仓库根的脆弱相对路径推导）。
            var tempFile = Path.Combine(Path.GetTempPath(), "sfx_cold_timing_" + Guid.NewGuid().ToString("N") + ".bin");
            File.WriteAllBytes(tempFile, new byte[4410]);

            try
            {
                var audio = new StubAudio();
                var loader = new FrameTickResourceLoader(tempFile);
                var catalog = new Dictionary<Id, SfxDef>
                {
                    [ColdSfxId] = new SfxDef(ColdSfxId, "ui", priority: 0, variants: null, resourceRef: ColdResourceId),
                };
                var player = new SfxPlayer(audio, new RngHost(1), catalog, resourceLoader: loader);

                var stopwatch = Stopwatch.StartNew();
                var handle = player.Play(ColdSfxId, null);

                // 冷资源首次引用：本次调用不能立即拿到真实句柄（外部审核阻塞项 4 既有约束，见
                // SfxPlayer.Play 判断记录），与 SfxPlayerTests 既有用例断言一致，作为本用例的前提
                // 校验，不是本用例新增的行为。
                Assert.Null(handle);
                Assert.Empty(audio.ActiveSfxPlaybacks);

                // 按"每帧一次 Tick"的生产节奏轮询，直到真正观测到 IAudio.PlaySfx 被调用（对象池
                // 出现一条播放记录）或超过安全上限帧数（防止环境异常时测试死等）。Thread.Sleep(1)
                // 只是把 CPU 让给后台 Task.Run 真正完成磁盘 IO，不是本用例计时的一部分（计时的是
                // 整个 Stopwatch 区间，包含这些让出时间——这本身就是真实调用方会经历的墙钟延迟）。
                const int maxFrames = 2000;
                var frameCount = 0;
                while (audio.ActiveSfxPlaybacks.Count == 0 && frameCount < maxFrames)
                {
                    Thread.Sleep(1);
                    loader.Tick();
                    frameCount++;
                }

                stopwatch.Stop();
                var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

                Assert.True(audio.ActiveSfxPlaybacks.Count > 0,
                    $"冷资源加载应当在有限帧数内完成并补播放（frames={frameCount}, elapsedMs={elapsedMs:F2}）");
                Assert.Equal(ColdResourceId, audio.ActiveSfxPlaybacks[1].SoundId);

                // 实测数字（本次运行的具体毫秒数）记入判断记录/ADR-0083 正文，不在这里断言精确
                // 数值（不同机器/CI 负载下的绝对值会抖动）；这里只锁一个远低于消费方 0.4 秒轮询
                // 窗口的宽松上界，验证"这套架构模式本身不会系统性地突破 400ms 窗口"这一结论，
                // 留出数量级安全余量，不是脆弱的精确时间断言。
                Assert.True(elapsedMs < 400.0,
                    $"冷加载->真正播放实测耗时 {elapsedMs:F2}ms，应当远小于消费方 0.4 秒轮询窗口（frames={frameCount}）");
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
    }
}
