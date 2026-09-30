using System.Collections.Concurrent;
using System.Collections.Generic;
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
    /// ADR-0083 第一部分定位问题 1 的用例："冷资源从 play_sfx 派发到真实播放开始，会不会超过消费方
    /// 0.4 秒轮询窗口"。
    /// <para>
    /// 判断记录（ADR-0121 决策 2，D2：改为 dt 驱动的确定性用例）：原用例用真实 <c>Task.Run</c> 文件 IO +
    /// <c>Thread.Sleep</c> + <c>Stopwatch</c> 测墙钟毫秒数，结果依赖机器与调度、不可复现，违反 AGENTS
    /// 第 3 节确定性（不依赖系统时间）。真正决定这段延迟的是 <c>adapters/unity/.../UnityResourceLoader</c>
    /// 的架构模式（见该类型判断记录"加载方式"）：后台读字节完成后入并发队列，回调总在下一次按帧
    /// <c>Tick</c>（主线程）里触发——延迟的量纲是"帧数 × 帧时长"，可以用表现层自己的 <c>dt</c> 时钟
    /// 确定性地复刻：本用例的加载器仍是"入并发队列 + 按帧 Tick 消费"的同构模型，但后台读取这一步用
    /// "请求当帧就绪"表示（真实 IO 耗时属于引擎适配层，不在本模块可断言范围），每帧以固定
    /// <c>FrameDt</c> 驱动 <see cref="SfxPlayer.Update"/> 与加载器 <c>Tick</c>，用累计 dt（而非墙钟）
    /// 计量"派发 → 真正 <c>IAudio.PlaySfx</c>"的延迟，并断言它小于消费方 0.4 秒窗口。期望帧数由规则
    /// 算出（回调在第一次 Tick 内触发，即 1 帧），不写裸数。
    /// </para>
    /// </summary>
    public class SfxColdLoadTimingTests
    {
        private static readonly Id ColdSfxId = new Id("sfx.cold_timing_probe");
        private static readonly Id ColdResourceId = new Id("res.cold_timing_probe");

        /// <summary>消费方轮询窗口（秒），见 ADR-0083。</summary>
        private const double ConsumerPollingWindowSeconds = 0.4;

        /// <summary>一帧的时长（秒），60 帧/秒。</summary>
        private const double FrameDt = 1.0 / 60.0;

        /// <summary>复刻 <c>UnityResourceLoader</c> 的"完成结果入并发队列 + 按帧 Tick 在主线程消费"架构
        /// （见该类型判断记录"加载方式"），确定性版本：<see cref="LoadAsync"/> 只把完成项入队（表示
        /// 后台读取在请求当帧已就绪），回调只在 <see cref="Tick"/> 内触发，不在 <see cref="LoadAsync"/>
        /// 调用栈内同步触发。</summary>
        private sealed class FrameTickResourceLoader : IResourceLoader
        {
            private readonly ConcurrentQueue<(Id ResourceId, LoadCallback Callback)> _completions = new ConcurrentQueue<(Id, LoadCallback)>();
            private readonly HashSet<Id> _loaded = new HashSet<Id>();

            public void LoadAsync(Id resourceId, ResourceKind kind, LoadCallback callback)
            {
                _completions.Enqueue((resourceId, callback));
            }

            public bool IsLoaded(Id resourceId) => _loaded.Contains(resourceId);

            public double GetLoadProgress(Id resourceId) => _loaded.Contains(resourceId) ? 1.0 : 0.0;

            public void Unload(Id resourceId) => _loaded.Remove(resourceId);

            /// <summary>模拟 <c>UnityEngineHost.Update</c> 每帧调用一次的 <c>Tick</c>：排空完成队列并触发
            /// 回调（回调总在某次 Tick 内）。</summary>
            public int Tick()
            {
                var processed = 0;
                while (_completions.TryDequeue(out var item))
                {
                    _loaded.Add(item.ResourceId);
                    item.Callback(item.ResourceId, true);
                    processed++;
                }
                return processed;
            }
        }

        [Fact]
        public void ColdLoad_RequestToPlaySfx_AccumulatedFrameDt_WellUnderConsumerPollingWindow()
        {
            var audio = new StubAudio();
            var loader = new FrameTickResourceLoader();
            var catalog = new Dictionary<Id, SfxDef>
            {
                [ColdSfxId] = new SfxDef(ColdSfxId, "ui", priority: 0, variants: null, resourceRef: ColdResourceId),
            };
            var player = new SfxPlayer(audio, new RngHost(1), catalog, resourceLoader: loader);

            var handle = player.Play(ColdSfxId, null);

            // 冷资源首次引用：本次调用不能立即拿到真实句柄（外部审核阻塞项 4 既有约束，见
            // SfxPlayer.Play 判断记录），与 SfxPlayerTests 既有用例断言一致，作为本用例的前提校验。
            Assert.Null(handle);
            Assert.Empty(audio.ActiveSfxPlaybacks);
            Assert.Equal(1, player.PendingPlayCount);

            // 按"每帧一次 Update + 一次 Tick"的生产节奏推进，直到真正观测到 IAudio.PlaySfx 被调用，
            // 或超过消费方窗口对应的帧数上限（防止实现回归时死等）。
            var maxFrames = (int)System.Math.Ceiling(ConsumerPollingWindowSeconds / FrameDt);
            var frames = 0;
            var elapsedSeconds = 0.0;
            while (audio.ActiveSfxPlaybacks.Count == 0 && frames < maxFrames)
            {
                player.Update(FrameDt);
                elapsedSeconds += FrameDt;
                loader.Tick();
                frames++;
            }

            Assert.True(audio.ActiveSfxPlaybacks.Count > 0,
                $"冷资源加载应当在消费方窗口对应的帧数内完成并补播放（frames={frames}, elapsed={elapsedSeconds:F4}s）");
            Assert.Equal(ColdResourceId, audio.ActiveSfxPlaybacks[1].SoundId);
            Assert.Equal(0, player.PendingPlayCount);

            // 期望由规则算出：回调只在 Tick 内触发，Tick 每帧一次、且请求当帧已就绪，因此第一帧就完成。
            Assert.Equal(1, frames);
            Assert.True(elapsedSeconds < ConsumerPollingWindowSeconds,
                $"冷加载->真正播放的累计 dt {elapsedSeconds:F4}s 应当小于消费方 {ConsumerPollingWindowSeconds}s 轮询窗口");
        }

        /// <summary>同一架构下的反向一支：加载迟迟不回调（本加载器永不 Tick），累计 dt 达到
        /// <see cref="SfxOptions.FirstLoadTimeoutSeconds"/> 即被丢弃，与墙钟无关（ADR-0121 决策 2）。</summary>
        [Fact]
        public void ColdLoad_NeverTicked_IsDroppedWhenAccumulatedFrameDtReachesFirstLoadTimeout()
        {
            var audio = new StubAudio();
            var loader = new FrameTickResourceLoader();
            var options = new SfxOptions { FirstLoadTimeoutSeconds = 0.5 };
            var catalog = new Dictionary<Id, SfxDef>
            {
                [ColdSfxId] = new SfxDef(ColdSfxId, "ui", priority: 0, variants: null, resourceRef: ColdResourceId),
            };
            var player = new SfxPlayer(audio, new RngHost(1), catalog, options, resourceLoader: loader);

            player.Play(ColdSfxId, null);

            var elapsedSeconds = 0.0;
            var frames = 0;
            var guard = (int)System.Math.Ceiling(options.FirstLoadTimeoutSeconds / FrameDt) + 2;
            while (player.PendingPlayCount > 0 && frames < guard)
            {
                player.Update(FrameDt);
                elapsedSeconds += FrameDt;
                frames++;
            }

            Assert.Equal(0, player.PendingPlayCount);
            Assert.True(elapsedSeconds >= options.FirstLoadTimeoutSeconds - 1e-9,
                $"累计 dt {elapsedSeconds:F4}s 未到超时 {options.FirstLoadTimeoutSeconds}s 就被丢弃");
            Assert.True(elapsedSeconds < options.FirstLoadTimeoutSeconds + FrameDt + 1e-9,
                $"累计 dt {elapsedSeconds:F4}s 超过超时 {options.FirstLoadTimeoutSeconds}s 一帧以上才丢弃");
            Assert.Equal(1, player.PlaybackDiagnostics.PlayDroppedCount);
        }
    }
}
