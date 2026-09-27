#nullable enable
// SfxEngineReportedFinishPlayModeTests：ADR-0105（消费方反馈第五十四批）引擎侧验收——真实
// AudioSource 上验证 UnityAudio.IsSfxPlaying 的回报口径（播放中 true、自然播完/停止/回收/未知句柄
// false），以及 SfxPlayer 的同层并发记账随真实回报释放：已自然播完的一次性音效不再占名额，同层
// 低优先级的新音效不会被紧随其后的高优先级音效抢占停止。
//
// 判断记录（clip 注入方式）：同 UnityAudioTests R10 用例——测试用 UnityResourceLoader 未注册任何
// 资源，PlaySfx 走 clip 缺失分支拿到池位后，经 GetSfxSourceForHandle 注入 AudioClip.Create 生成的
// 静音 clip 并手动 Play()，模拟"这个池位确实在播"的真实前置状态，不依赖资源加载管线。本文件只读
// AudioSource 状态，不读像素/场景对象/相机，不需要专属 Layer 渲染隔离。
using System.Collections;
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Rng;
using NUnit.Framework;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class SfxEngineReportedFinishPlayModeTests : PlayModeTestBase
    {
        private static readonly Id HighFirst = new Id("sfx.adr0105_high_first");
        private static readonly Id LowFresh = new Id("sfx.adr0105_low_fresh");
        private static readonly Id HighNext = new Id("sfx.adr0105_high_next");

        private GameObject _rootGo = null!;
        private UnityAudio _audio = null!;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("Adr0105AudioRoot");
            _audio = new UnityAudio(_rootGo.transform, new UnityResourceLoader());
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_rootGo);
        }

        private static AudioClip ShortClip() => AudioClip.Create("adr0105_short", 200, 1, 44100, false); // 约 4.5ms

        private static AudioClip LongClip() => AudioClip.Create("adr0105_long", 44100 * 10, 1, 44100, false); // 10 秒静音

        private AudioSource InjectAndPlay(SfxHandle handle, AudioClip clip)
        {
            var source = _audio.GetSfxSourceForHandle(handle);
            Assert.IsNotNull(source, "句柄应当仍占着池位");
            source!.clip = clip;
            source.Play();
            Assert.IsTrue(source.isPlaying);
            return source;
        }

        private static IEnumerator WaitUntilNaturallyFinished(AudioSource source)
        {
            var frames = 0;
            while (source.isPlaying && frames < 300)
            {
                yield return null;
                frames++;
            }
            Assert.IsFalse(source.isPlaying, "极短 clip 应当早已自然播放结束");
        }

        [Test]
        public void IsSfxPlaying_UnknownHandle_ReturnsFalse()
        {
            Assert.AreEqual(false, _audio.IsSfxPlaying(new SfxHandle(987654)));
        }

        /// <summary>clip 缺失分支从未真正 Play()。实测：新建（AddComponent 当帧）的 AudioSource 即便
        /// clip 为 null、从未 Play()，<c>isPlaying</c> 在创建当帧也读到 true，下一帧才变 false——实现因此
        /// 额外要求 clip 非空，本用例在创建当帧断言，钉住这一点。</summary>
        [Test]
        public void IsSfxPlaying_MissingClip_NeverStarted_ReturnsFalse_EvenOnSourceCreationFrame()
        {
            var handle = _audio.PlaySfx(new Id("sfx.adr0105_missing"), 1.0, 1.0, null);
            Assert.AreEqual(1, _audio.SfxPoolSize, "本用例要求池位是当帧新建的音源");
            Assert.AreEqual(false, _audio.IsSfxPlaying(handle));
        }

        [Test]
        public void IsSfxPlaying_AfterStopSfx_ReturnsFalse()
        {
            var handle = _audio.PlaySfx(new Id("sfx.adr0105_stop"), 1.0, 1.0, null);
            InjectAndPlay(handle, LongClip());
            Assert.AreEqual(true, _audio.IsSfxPlaying(handle));

            _audio.StopSfx(handle);
            Assert.AreEqual(false, _audio.IsSfxPlaying(handle));
        }

        [UnityTest]
        public IEnumerator IsSfxPlaying_ShortClip_TrueWhilePlaying_FalseAfterNaturalEnd_BeforeAndAfterReclaim()
        {
            var handle = _audio.PlaySfx(new Id("sfx.adr0105_short"), 1.0, 1.0, null);
            var source = InjectAndPlay(handle, ShortClip());
            Assert.AreEqual(true, _audio.IsSfxPlaying(handle));

            yield return WaitUntilNaturallyFinished(source);

            // 本帧 Tick 尚未回收池位：直接读音源状态，已回报 false。
            Assert.AreEqual(1, _audio.ActiveSfxCount);
            Assert.AreEqual(false, _audio.IsSfxPlaying(handle));

            _audio.Tick(0);
            Assert.AreEqual(0, _audio.ActiveSfxCount);
            Assert.AreEqual(false, _audio.IsSfxPlaying(handle));

            // 池位被新句柄复用后，旧句柄仍回报 false，不误报成新播放的状态。
            var reused = _audio.PlaySfx(new Id("sfx.adr0105_reuse"), 1.0, 1.0, null);
            InjectAndPlay(reused, LongClip());
            Assert.AreEqual(1, _audio.SfxPoolSize);
            Assert.AreEqual(false, _audio.IsSfxPlaying(handle));
            Assert.AreEqual(true, _audio.IsSfxPlaying(reused));
        }

        private SfxPlayer CreatePlayer()
        {
            var catalog = new Dictionary<Id, SfxDef>
            {
                [HighFirst] = new SfxDef(HighFirst, "combat", 5, null, new Id("res.adr0105_high_first")),
                [LowFresh] = new SfxDef(LowFresh, "combat", 2, null, new Id("res.adr0105_low_fresh")),
                [HighNext] = new SfxDef(HighNext, "combat", 5, null, new Id("res.adr0105_high_next")),
            };
            var options = new SfxOptions
            {
                MaxConcurrentPerLayer = new Dictionary<string, int> { ["combat"] = 2 },
                // 保留时长设得极长且本用例从不驱动 SfxPlayer.Update：名额释放只可能来自引擎真实回报。
                OneShotLayerSlotHoldSeconds = 1000.0,
            };
            return new SfxPlayer(_audio, new RngHost(1), catalog, options);
        }

        /// <summary>复现（引擎侧）：同层名额 2。先播的高优先级音效自然播完后，低优先级新音效开始，
        /// 紧接着第二个高优先级音效开始——低优先级音效必须仍在播。修复前 SfxPlayer 仍把已播完的那条
        /// 算作占名额，层满，停掉层内最低优先级的低优先级新音效。</summary>
        [UnityTest]
        public IEnumerator SfxPlayer_FirstFinishedNaturally_LowPriorityFreshNotPreemptedByNextHigher()
        {
            var player = CreatePlayer();

            var first = player.Play(HighFirst, null)!.Value;
            var firstSource = InjectAndPlay(first, ShortClip());
            yield return WaitUntilNaturallyFinished(firstSource);
            Assert.AreEqual(false, _audio.IsSfxPlaying(first));

            var low = player.Play(LowFresh, null)!.Value;
            var lowSource = InjectAndPlay(low, LongClip());
            player.Play(HighNext, null);

            Assert.IsTrue(lowSource.isPlaying, "已自然播完的音效不应再占同层名额，低优先级新音效不应被抢占停止");
            Assert.AreEqual(true, _audio.IsSfxPlaying(low));
            Assert.AreEqual(0, player.PlaybackDiagnostics.PlayDroppedCount);
        }

        /// <summary>阳性对照：先播的高优先级音效仍在播（引擎回报 true）时，层确实已满，低优先级音效照原
        /// 规则被抢占停止——证明上一条用例观测的差异来自"是否已播完"，上限本身仍生效。</summary>
        [UnityTest]
        public IEnumerator SfxPlayer_FirstStillPlaying_LayerFull_LowPriorityPreempted()
        {
            var player = CreatePlayer();

            var first = player.Play(HighFirst, null)!.Value;
            InjectAndPlay(first, LongClip());
            yield return null;
            Assert.AreEqual(true, _audio.IsSfxPlaying(first));

            var low = player.Play(LowFresh, null)!.Value;
            var lowSource = InjectAndPlay(low, LongClip());
            player.Play(HighNext, null);

            Assert.IsFalse(lowSource.isPlaying, "层内两个实例都在播时，第三个应抢占层内最低优先级者");
            Assert.AreEqual(false, _audio.IsSfxPlaying(low));
            Assert.AreEqual(true, _audio.IsSfxPlaying(first));
        }
    }
}
