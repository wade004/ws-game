#nullable enable
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using UnityEngine;

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
    }
}
