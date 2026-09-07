#nullable enable
using System.Collections;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class UnityAudioTests : PlayModeTestBase
    {
        private GameObject _rootGo = null!;
        private UnityAudio _audio = null!;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("AudioRoot");
            _audio = new UnityAudio(_rootGo.transform, new UnityResourceLoader());
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_rootGo);
        }

        [Test]
        public void PlaySfx_MissingClip_ReturnsHandleWithoutThrowing()
        {
            SfxHandle handle = default;
            Assert.DoesNotThrow(() => handle = _audio.PlaySfx(new Id("sfx.sample_hit"), 1.0, 1.0, null));
            Assert.Greater(handle.Value, 0);
        }

        [Test]
        public void StopSfx_UnknownHandle_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _audio.StopSfx(new SfxHandle(9999)));
        }

        [Test]
        public void PlaySfx_ThenStopSfx_DoesNotThrow()
        {
            var handle = _audio.PlaySfx(new Id("sfx.sample_hit"), 1.0, 1.0, null);
            Assert.DoesNotThrow(() => _audio.StopSfx(handle));
        }

        [Test]
        public void PlaySfx_WithPosition_SetsSpatialBlendAndPosition()
        {
            // ADR-0016 决策 3：position 非空时启用 2D 声像（见 UnityAudio.PlaySfx 判断记录）。
            SfxHandle handle = default;
            Assert.DoesNotThrow(() => handle = _audio.PlaySfx(new Id("sfx.sample_hit"), 1.0, 1.0, new Vec2(3, 4)));
            Assert.Greater(handle.Value, 0);
        }

        [Test]
        public void PlaySfx_WithoutPosition_DoesNotThrow()
        {
            SfxHandle handle = default;
            Assert.DoesNotThrow(() => handle = _audio.PlaySfx(new Id("sfx.sample_hit"), 1.0, 1.0, null));
            Assert.Greater(handle.Value, 0);
        }

        [Test]
        public void SetBusVolume_DoesNotThrow_AndPlayMusicStillWorks()
        {
            _audio.SetBusVolume(AudioBus.Music, 0.5);
            Assert.DoesNotThrow(() => _audio.PlayMusic(new Id("music.sample_theme"), 0, true));
        }

        [Test]
        public void PlayMusic_ThenStopMusic_DoesNotThrow()
        {
            _audio.PlayMusic(new Id("music.sample_theme"), 0, true);
            Assert.DoesNotThrow(() => _audio.StopMusic(0));
        }

        // -----------------------------------------------------------------
        // R02 复现与根治（architecture/落地计划/audit-5e779c6-20260907）：Tick/StopMusic 此前沿用了
        // PlayMusic 翻转 _musicUsingA 之前那套 "_musicUsingA ? B : A" 映射来挑选"当前活跃音源"，但
        // 读取的却是翻转之后的值，选出的其实是上一轮的 outgoing（淡出中/空闲的旧音源），不是真正在
        // 播放的那个——交叉淡入时新旧音源互换：Stop 停掉音量为 0 的旧音源（听感上什么也没发生），
        // 真正在响的音源永远淡不进来、也停不掉。以下用例通过 UnityAudio.ActiveMusicSource/
        // InactiveMusicSource（本次修复新增的测试用只读访问器，按修复后正确的映射计算）直接断言
        // Tick/StopMusic 操作的是哪一个物理 AudioSource。
        // -----------------------------------------------------------------

        [Test]
        public void PlayMusic_WithFadeIn_TickRaisesVolumeOnActiveSource_NotInactiveSource()
        {
            _audio.PlayMusic(new Id("music.sample_theme"), 2.0, true);
            var active = _audio.ActiveMusicSource;
            var inactive = _audio.InactiveMusicSource;
            Assert.AreEqual(0f, active.volume, 0.0001f); // PlayMusic 把 incoming.volume 显式清零，淡入从 0 开始。
            var inactiveVolumeBeforeTick = inactive.volume;

            _audio.Tick(1.0); // 2 秒淡入的一半。

            Assert.Greater(active.volume, 0f);
            Assert.Less(active.volume, 1f);
            // 核心断言（R02）：Tick 不应该动到 inactive 那个音源的音量——修复前这里会失败，因为
            // Tick 实际更新的是 inactive（旧实现选反了）。
            Assert.AreEqual(inactiveVolumeBeforeTick, inactive.volume, 0.0001f);

            _audio.Tick(1.0); // 累计 2 秒，淡入应当已经完成。

            Assert.AreEqual(1f, active.volume, 0.0001f);
        }

        [Test]
        public void StopMusic_StopsTheActiveSource_NotTheInactiveOne()
        {
            _audio.PlayMusic(new Id("music.sample_theme"), 0, true);
            var active = _audio.ActiveMusicSource;
            var inactive = _audio.InactiveMusicSource;

            // production 路径下 AudioClip 由 UnityResourceLoader 真正解码 wav 才能拿到（本任务不改
            // 该文件），测试直接给暴露出来的两个 AudioSource 注入一段可播放的空白 clip 并手动
            // Play()，模拟"音乐确实在响"这一前置状态，不依赖资源加载管线。
            var clip = AudioClip.Create("r02_silence", 1000, 1, 44100, false);
            active.clip = clip;
            active.Play();
            inactive.clip = clip;
            inactive.Play();
            Assert.IsTrue(active.isPlaying);
            Assert.IsTrue(inactive.isPlaying);

            _audio.StopMusic(0);

            // 核心断言（R02）：StopMusic 应当停止当前实际在播放的音源，不应该误停另一个已经淡出/
            // 空闲的音源——修复前这两个断言恰好相反。
            Assert.IsFalse(active.isPlaying, "StopMusic 应当停止当前实际在播放的音源");
            Assert.IsTrue(inactive.isPlaying, "StopMusic 不应该误停另一个（已经淡出/空闲）的音源");
        }

        [Test]
        public void PlayMusic_CalledTwice_CrossfadesToNewActiveSource_TickTracksCorrectOne()
        {
            _audio.PlayMusic(new Id("music.sample_theme_a"), 1.0, true);
            var firstActive = _audio.ActiveMusicSource;
            _audio.Tick(1.0); // 完成第一次淡入。
            Assert.AreEqual(1f, firstActive.volume, 0.0001f);

            _audio.PlayMusic(new Id("music.sample_theme_b"), 1.0, true);
            var secondActive = _audio.ActiveMusicSource;

            // 交叉淡入换到了另一个物理音源（不是同一个 AudioSource 被反复复用）。
            Assert.AreNotSame(firstActive, secondActive);
            Assert.AreEqual(0f, secondActive.volume, 0.0001f);

            _audio.Tick(0.5); // 第二次淡入的一半。

            // 核心断言（R02）：Tick 跟踪的是"新"的活跃音源（secondActive），不是已经切走的 firstActive。
            Assert.Greater(secondActive.volume, 0f);
            Assert.Less(secondActive.volume, 1f);
        }

        // -----------------------------------------------------------------
        // R10 复现与根治（architecture/落地计划/audit-5e779c6-20260907）：此前只有显式 StopSfx 才会
        // 把 SFX 池位标回 Active = false 并摘除 handle 映射；一次性音效自然播放结束（没有任何调用方
        // 显式 Stop）时池位永远占着，RentSlot 找不到它，每次播放都新建一个 AudioSource，池子只增不减。
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator PlaySfx_NaturalPlaybackEnd_ReclaimsPoolSlot_PoolSizeDoesNotGrowUnbounded()
        {
            // production 路径下 PlaySfx 播放的 clip 由 UnityResourceLoader 真正解码得到（本任务不改
            // 该文件，且测试用的 UnityResourceLoader 没有注册任何资源）。这里先正常调用 PlaySfx 拿到
            // 一个池位（clip 缺失分支，Play() 不会被真正调用），再用 GetSfxSourceForHandle 直接给它
            // 注入一段极短的可播放 clip 并手动 Play()，模拟"这个池位确实在自然播放"这一真实前置状态。
            var handle = _audio.PlaySfx(new Id("sfx.r10_sample"), 1.0, 1.0, null);
            var source = _audio.GetSfxSourceForHandle(handle);
            Assert.IsNotNull(source);

            var clip = AudioClip.Create("r10_short", 200, 1, 44100, false); // 约 4.5ms @ 44100Hz，很快自然播完。
            source!.clip = clip;
            source.Play();
            Assert.IsTrue(source.isPlaying);
            Assert.AreEqual(1, _audio.SfxPoolSize);
            Assert.AreEqual(1, _audio.ActiveSfxCount);

            // 等真实时间流逝到 clip 自然播完（不显式调用 StopSfx）。
            var framesWaited = 0;
            while (source.isPlaying && framesWaited < 300)
            {
                yield return null;
                framesWaited++;
            }
            Assert.IsFalse(source.isPlaying, "极短 clip 应当早已自然播放结束");

            _audio.Tick(0);

            // 核心断言（R10）：池位被回收——不再占用 handle，但物理 AudioSource 数量没有变化（复用，
            // 不是被销毁）。修复前 ActiveSfxCount 会一直是 1，永不归零。
            Assert.AreEqual(0, _audio.ActiveSfxCount, "自然播放结束后应当被回收，不再占用 handle");
            Assert.AreEqual(1, _audio.SfxPoolSize, "池子本身不应该因为一次自然结束的播放而销毁/新增音源");

            // 再播放一次：应当复用刚回收的同一个池位，而不是新建——池子大小仍然是 1。修复前这里会变成
            // 2（RentSlot 找不到"看起来还 Active"的旧池位，只能新建）。
            _audio.PlaySfx(new Id("sfx.r10_sample_2"), 1.0, 1.0, null);
            Assert.AreEqual(1, _audio.SfxPoolSize, "应当复用已回收的池位，池子大小不应该增长");
        }
    }
}
