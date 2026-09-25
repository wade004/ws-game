#nullable enable
// SfxAttachColdLoadPlayModeTests：缺陷修复回归（2026-09-26，消费方第三十五批阻塞项）——
// SfxPlayer.PlayAttached 冷加载路径此前不登记 attach 键，导致循环音效首播（真实引擎音频解码永远
// 异步，见 UnityResourceLoader.LoadAsync 判断记录"音频解码"）之后 StopAttached/stop_sfx 永远
// miss。presentation/vfx_sfx/tests/SfxPlayerTests.cs 已用 StubResourceLoader 在核心库层面复现与
// 回归（PlayAttached_ColdLoad_QueuesAttachKey_StopAttached_StopsPlaybackAfterLoadCompletes 等），
// 本文件用真实引擎适配层（UnityResourceLoader + UnityAudio）验证同一行为在真实音频解码路径下
// 同样成立，不是只在测试桩上成立。
//
// 同 UnityAudioTests 惯例：直接构造 UnityAudio/UnityResourceLoader，不经完整
// GameplayAssembly/PresentationAssembly 装配（本用例不需要场景/事件总线，只需要真实的"冷资源
// 首次加载 -> 补播放"这条异步路径）。
using System.Collections;
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.Rng;
using NUnit.Framework;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class SfxAttachColdLoadPlayModeTests : PlayModeTestBase
    {
        private static readonly Id LoopSfxId = new Id("sfx.pres35_cold_loop_probe");

        // 真实存在的占位音频资产（adapters/unity/Assets/StreamingAssets/GameFoundation/audio/
        // sample_cast_v0.wav，同 SfxPlaybackObservabilityPlayModeTests.WarmResourceId 用的同一份
        // 文件），复用它只是为了有一份真实可解码的 wav，与该文件已有用例互不干扰——本文件自己的
        // UnityResourceLoader 实例与全局共享加载器缓存无关，每个用例都是"从未加载过"的冷启动。
        private static readonly Id LoopResourceId = new Id("sfx.sample_cast_v0");

        private static readonly Id EntityId = new Id("unit.pres35_probe");

        private GameObject _rootGo = null!;
        private UnityResourceLoader _resourceLoader = null!;
        private UnityAudio _audio = null!;
        private SfxPlayer _player = null!;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("SfxAttachColdLoadRoot");
            _resourceLoader = new UnityResourceLoader();
            _audio = new UnityAudio(_rootGo.transform, _resourceLoader);

            var catalog = new Dictionary<Id, SfxDef>
            {
                [LoopSfxId] = new SfxDef(LoopSfxId, "combat", priority: null, variants: null, resourceRef: LoopResourceId, loop: true),
            };
            _player = new SfxPlayer(_audio, new RngHost(20260926UL), catalog, resourceLoader: _resourceLoader);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_rootGo);
        }

        private IEnumerator PumpUntilLoaded(int maxFrames = 300)
        {
            var guard = maxFrames;
            while (!_resourceLoader.IsLoaded(LoopResourceId) && guard-- > 0)
            {
                _resourceLoader.Tick(); // internal，见包 AssemblyInfo.cs 的 InternalsVisibleTo 判断记录。
                yield return null;
            }
            Assert.IsTrue(_resourceLoader.IsLoaded(LoopResourceId), "冷资源应当能在有限帧数内加载完成");
        }

        private static AudioSource? FindPlayingSourceFor(Id resourceId)
        {
            // 同 SfxPlaybackObservabilityPlayModeTests.PollObservesPlaying 判断记录：按 clip.name
            // == 资源 id 定位（UnityResourceLoader.TryDecodeWav 用 resourceId.Value 给 AudioClip
            // 命名），不依赖内部句柄。
            var sources = Object.FindObjectsOfType<AudioSource>();
            foreach (var s in sources)
            {
                if (s.isPlaying && s.clip != null && s.clip.name == resourceId.Value)
                {
                    return s;
                }
            }
            return null;
        }

        [UnityTest]
        public IEnumerator PlayAttached_ColdLoopSfx_ThenStopAttached_StopsRealAudioSource()
        {
            Assert.IsFalse(_resourceLoader.IsLoaded(LoopResourceId), "本用例要求资源从未加载过，才能真正命中冷加载路径");

            var handle = _player.PlayAttached(LoopSfxId, EntityId, null);
            Assert.IsNull(handle, "冷资源尚未加载完成，不能立即拿到真实句柄（同 Play() 既有语义）");

            yield return PumpUntilLoaded();
            yield return null; // LoadAsync 回调可能在 Tick 完成的那一帧才触发，见既有 PlayMode 用例惯例，确保已经跑到 SfxPlayer.OnResourceLoadCompleted。

            // 阳性对照：Stop 前确实真正起播——不是本来就没有任何 AudioSource 起播，下面的
            // "Stop 后 isPlaying==false" 才有意义（同既有 PollObservesPlaying 判断记录"否定断言
            // 最容易因为错误的原因为真"惯例）。
            var source = FindPlayingSourceFor(LoopResourceId);
            Assert.IsNotNull(source, "冷资源加载完成后应当已经真正起播（阳性对照）");
            Assert.IsTrue(source!.isPlaying, "阳性对照：Stop 前 isPlaying 应当为 true");

            _player.StopAttached(LoopSfxId, EntityId);
            yield return null;

            // 核心断言（修复前会失败）：修复前 attach 键在冷加载路径下从未登记过，StopAttached 会
            // 因为查不到键而静默 no-op，这个真实 AudioSource 会继续循环播放，isPlaying 仍为 true。
            Assert.IsFalse(source.isPlaying, "StopAttached 应当真正停止这个冷加载起播的循环音效实例");
        }
    }
}
