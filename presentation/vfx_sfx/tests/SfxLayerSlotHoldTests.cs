using System.Collections.Generic;
using System.Linq;
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
    /// ADR-0105（消费方反馈第五十四批）不变量：一次性音效播完后不再占同层名额——主路径按
    /// <c>IAudio.IsSfxPlaying</c> 真实回报（<c>EngineReports*</c> 用例，桩 <c>CompleteSfx</c> 模拟自然
    /// 播完）；后端回报 null 时退回 <see cref="SfxOptions.OneShotLayerSlotHoldSeconds"/>
    /// （<c>BackendNoReport_*</c> 用例，桩 <c>ReportsPlaybackState=false</c>）。
    /// ① 保留时长内真正并发的第 N+1 个仍按原优先级规则抢占；② 到期释放只摘记账、不调用
    /// <c>IAudio.StopSfx</c>，也不计入 <see cref="ISfxPlaybackDiagnostics.PlayDroppedCount"/>；
    /// ③ 循环音效永不按时长释放；④ 冷加载补播放与热路径同一套记账，保留时长从真正开始播放那一刻起算。
    /// 桩音频后端不随时间自然播完，<c>ActiveSfxPlaybacks</c> 里句柄消失只可能是被 <c>StopSfx</c> 或测试
    /// 显式 <c>CompleteSfx</c>。
    /// </summary>
    public class SfxLayerSlotHoldTests
    {
        private static readonly Id Low = new Id("sfx.slot_low");
        private static readonly Id Mid = new Id("sfx.slot_mid");
        private static readonly Id High = new Id("sfx.slot_high");
        private static readonly Id LoopLow = new Id("sfx.slot_loop_low");
        private static readonly Id LowRes = new Id("res.slot_low");
        private static readonly Id MidRes = new Id("res.slot_mid");
        private static readonly Id HighRes = new Id("res.slot_high");
        private static readonly Id LoopLowRes = new Id("res.slot_loop_low");

        private static Dictionary<Id, SfxDef> Catalog() => new Dictionary<Id, SfxDef>
        {
            [Low] = new SfxDef(Low, "combat", 2, null, LowRes),
            [Mid] = new SfxDef(Mid, "combat", 4, null, MidRes),
            [High] = new SfxDef(High, "combat", 5, null, HighRes),
            [LoopLow] = new SfxDef(LoopLow, "combat", 1, null, LoopLowRes, loop: true),
        };

        private static SfxOptions CapOf(int cap) => new SfxOptions
        {
            MaxConcurrentPerLayer = new Dictionary<string, int> { ["combat"] = cap },
        };

        private static int HandleOf(StubAudio audio, Id resource) =>
            audio.ActiveSfxPlaybacks.Single(kv => kv.Value.SoundId.Equals(resource)).Key;

        [Fact]
        public void WithinHoldWindow_NPlusOneConcurrent_StillPreemptsLowestPriority_ThenOldestOfEqualPriority()
        {
            var audio = new StubAudio();
            var options = CapOf(2);
            var player = new SfxPlayer(audio, new RngHost(1), Catalog(), options);

            var low = player.Play(Low, null)!.Value;
            player.Update(options.OneShotLayerSlotHoldSeconds * 0.25);
            var high1 = player.Play(High, null)!.Value;
            player.Update(options.OneShotLayerSlotHoldSeconds * 0.25);
            var high2 = player.Play(High, null)!.Value; // 第 N+1=3 个，仍在保留时长内

            Assert.False(audio.ActiveSfxPlaybacks.ContainsKey(low.Value));
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(high1.Value));
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(high2.Value));

            var high3 = player.Play(High, null)!.Value; // 同优先级：停最早的那个
            Assert.False(audio.ActiveSfxPlaybacks.ContainsKey(high1.Value));
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(high2.Value));
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(high3.Value));
            Assert.Equal(0, player.PlaybackDiagnostics.PlayDroppedCount);
        }

        [Fact]
        public void BackendNoReport_OneShot_AfterHoldElapsed_ReleasesSlot_WithoutStopSfx_AndWithoutCountingDropped()
        {
            var audio = new StubAudio { ReportsPlaybackState = false };
            var options = CapOf(1);
            var player = new SfxPlayer(audio, new RngHost(1), Catalog(), options);

            var low = player.Play(Low, null)!.Value;
            player.Update(options.OneShotLayerSlotHoldSeconds);
            var high = player.Play(High, null)!.Value;

            // 修复前：层记账仍算 low 在播，名额 1 已满，low 被 StopSfx。
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(low.Value));
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(high.Value));

            // 释放的只是已到期的那一个：high 仍在保留时长内、仍占唯一名额，下一个同层播放照原规则
            // （层满时停掉层内优先级最低者，层内此刻只有 high）抢占它。ADR-0121 决策 3 之后新来者
            // 优先级须不低于在播者才能抢占，因此这里用同优先级的第二个 high（原先用更低优先级的 mid，
            // 恰是"低优先级新音顶掉高优先级旧音"的旧行为，已拍板为缺陷，见 SfxPlayerTests 的 Priority_* 用例）。
            var nextHigh = player.Play(High, null)!.Value;
            Assert.False(audio.ActiveSfxPlaybacks.ContainsKey(high.Value));
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(nextHigh.Value));

            var diag = player.PlaybackDiagnostics;
            Assert.Equal(0, diag.PlayDroppedCount);
            Assert.Equal(diag.PlayRequestedCount, diag.PlayStartedCount);

            // 对已释放记账的句柄显式 Stop 仍照常转发 IAudio.StopSfx。
            player.Stop(low);
            Assert.False(audio.ActiveSfxPlaybacks.ContainsKey(low.Value));
        }

        [Fact]
        public void LoopSfx_IsNeverReleasedByHold_StillCountsTowardLayerCap()
        {
            var audio = new StubAudio();
            var options = CapOf(1);
            var player = new SfxPlayer(audio, new RngHost(1), Catalog(), options);

            var loop = player.Play(LoopLow, null)!.Value;
            player.Update(options.OneShotLayerSlotHoldSeconds * 50);
            var high = player.Play(High, null)!.Value;

            // 循环音效显式停止前一直在播：名额 1 已被它占着，更高优先级的新播放照原规则抢占它。
            Assert.False(audio.ActiveSfxPlaybacks.ContainsKey(loop.Value));
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(high.Value));
        }

        [Fact]
        public void BackendNoReport_HoldDisabled_NonPositive_RestoresPreviousBehavior()
        {
            var audio = new StubAudio { ReportsPlaybackState = false };
            var options = CapOf(1);
            options.OneShotLayerSlotHoldSeconds = 0.0;
            var player = new SfxPlayer(audio, new RngHost(1), Catalog(), options);

            var low = player.Play(Low, null)!.Value;
            player.Update(100.0);
            player.Play(High, null);

            Assert.False(audio.ActiveSfxPlaybacks.ContainsKey(low.Value));
        }

        /// <summary>冷加载分支的复现镜像（同 <c>AutoAttackSwingSfx_FollowedBySameFrameDamageSfx_*</c>）：
        /// 较早一轮的命中类音效经冷加载补播放后早已播完；新一轮低优先级音效同样经冷加载补播放，
        /// 紧接着更高优先级音效（此时已热）开始——低优先级音效不得被当场停掉。</summary>
        [Fact]
        public void BackendNoReport_ColdLoadPath_StaleHistoryReleased_FreshLowPriorityNotPreemptedByNextHigher()
        {
            var audio = new StubAudio { ReportsPlaybackState = false };
            var loader = new StubResourceLoader { DeferCallbacks = true };
            foreach (var r in new[] { LowRes, MidRes, HighRes })
            {
                loader.Register(r);
            }
            var options = CapOf(2);
            var player = new SfxPlayer(audio, new RngHost(1), Catalog(), options, resourceLoader: loader);

            player.Play(High, null);
            player.Play(Mid, null);
            loader.CompletePending(HighRes);
            loader.CompletePending(MidRes);
            Assert.Equal(2, audio.ActiveSfxPlaybacks.Count);

            player.Update(options.OneShotLayerSlotHoldSeconds + 1.0);

            Assert.Null(player.Play(Low, null));
            loader.CompletePending(LowRes);
            var low = HandleOf(audio, LowRes);

            player.Play(High, null); // 已热，同帧立即播放
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(low));
            Assert.Equal(0, player.PlaybackDiagnostics.PlayDroppedCount);
        }

        /// <summary>冷加载路径的保留时长从真正开始播放（加载完成补播放）那一刻起算，不从排队那一刻起算：
        /// 排队后等待接近保留时长才加载完成，此后它仍是"刚开始播"的实例，照常占名额。</summary>
        [Fact]
        public void BackendNoReport_ColdLoadPath_HoldMeasuredFromActualStart_NotFromQueueTime()
        {
            var audio = new StubAudio { ReportsPlaybackState = false };
            var loader = new StubResourceLoader { DeferCallbacks = true };
            foreach (var r in new[] { LowRes, HighRes })
            {
                loader.Register(r);
            }
            var options = CapOf(1);
            var player = new SfxPlayer(audio, new RngHost(1), Catalog(), options, resourceLoader: loader);

            Assert.Null(player.Play(Low, null));
            player.Update(options.OneShotLayerSlotHoldSeconds * 0.9);
            loader.CompletePending(LowRes);
            var low = HandleOf(audio, LowRes);
            player.Update(options.OneShotLayerSlotHoldSeconds * 0.2); // 距排队已超保留时长，距开始播放未超

            Assert.Null(player.Play(High, null));
            loader.CompletePending(HighRes);

            // low 从开始播放起仍在保留时长内、仍占唯一名额：更高优先级的 high 按原规则抢占它。
            Assert.False(audio.ActiveSfxPlaybacks.ContainsKey(low));
            Assert.Single(audio.ActiveSfxPlaybacks.Values, p => p.SoundId.Equals(HighRes));
        }

        /// <summary>主路径：引擎回报已播完即释放名额，不驱动 <c>Update</c>、不等保留时长。cap=2：
        /// 已播完的高优先级 A 若仍占名额，C 到来时层满，会停掉层内最低优先级的 B。</summary>
        [Fact]
        public void EngineReportsFinished_ReleasesSlotImmediately_WithoutUpdateOrHold()
        {
            var audio = new StubAudio();
            var player = new SfxPlayer(audio, new RngHost(1), Catalog(), CapOf(2));

            var a = player.Play(High, null)!.Value;
            audio.CompleteSfx(a); // 自然播完（引擎回报 false）
            var b = player.Play(Low, null)!.Value;
            var c = player.Play(High, null)!.Value;

            Assert.Equal(false, audio.IsSfxPlaying(a));
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(b.Value));
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(c.Value));
            Assert.Equal(0, player.PlaybackDiagnostics.PlayDroppedCount);

            // 名额释放后上限仍生效：第 N+1 个真正在播的实例照原规则抢占最低优先级。
            var d = player.Play(High, null)!.Value;
            Assert.False(audio.ActiveSfxPlaybacks.ContainsKey(b.Value));
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(c.Value));
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(d.Value));
        }

        /// <summary>真实回报优先于推定：引擎回报仍在播时，即便已超过保留时长也不释放。</summary>
        [Fact]
        public void EngineReportsStillPlaying_BeyondHold_KeepsSlot()
        {
            var audio = new StubAudio();
            var options = CapOf(1);
            var player = new SfxPlayer(audio, new RngHost(1), Catalog(), options);

            var low = player.Play(Low, null)!.Value;
            player.Update(options.OneShotLayerSlotHoldSeconds * 10);
            Assert.Equal(true, audio.IsSfxPlaying(low));
            var high = player.Play(High, null)!.Value;

            Assert.False(audio.ActiveSfxPlaybacks.ContainsKey(low.Value));
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(high.Value));
        }

        /// <summary>冷加载分支（主路径）：冷加载补播放的实例与热路径同一套记账、同样按引擎回报释放。</summary>
        [Fact]
        public void EngineReportsFinished_ColdLoadPath_SameReleaseAsHotPath()
        {
            var audio = new StubAudio();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            foreach (var r in new[] { LowRes, HighRes })
            {
                loader.Register(r);
            }
            var player = new SfxPlayer(audio, new RngHost(1), Catalog(), CapOf(2), resourceLoader: loader);

            Assert.Null(player.Play(High, null));
            loader.CompletePending(HighRes);
            audio.CompleteSfx(new SfxHandle(HandleOf(audio, HighRes)));

            Assert.Null(player.Play(Low, null));
            loader.CompletePending(LowRes);
            var low = HandleOf(audio, LowRes);

            var high = player.Play(High, null)!.Value; // 已热，同帧立即播放
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(low));
            Assert.True(audio.ActiveSfxPlaybacks.ContainsKey(high.Value));
        }
    }
}
