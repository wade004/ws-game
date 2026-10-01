// StubAudio 专项测试（测试覆盖第四批 T-M11）：对着桩本体断言它的记录语义与测试专用 API，
// 不再只靠一致性套件与上层远处用例间接覆盖。命名空间刻意不用 Tests.Adapters.*：在其内部
// `using Adapters.Stub;` 会被解析成 Tests.Adapters 而非顶层 Adapters 命名空间。
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.StubAdapters
{
    public class StubAudioTests
    {
        private static readonly Id Sound = new Id("sfx.probe_a");
        private static readonly Id OtherSound = new Id("sfx.probe_b");

        [Fact]
        public void PlaySfx_AssignsDistinctStrictlyIncreasingHandles()
        {
            var audio = new StubAudio();
            var handles = Enumerable.Range(0, 5).Select(_ => audio.PlaySfx(Sound, 1.0, 1.0, null)).ToList();

            Assert.Equal(handles.Count, handles.Select(h => h.Value).Distinct().Count());
            for (var i = 1; i < handles.Count; i++)
            {
                Assert.True(handles[i].Value > handles[i - 1].Value);
            }
            Assert.Equal(handles.Count, audio.ActiveSfxPlaybacks.Count);
        }

        [Fact]
        public void PlaySfx_RecordsEveryArgument_AndFourArgOverloadMeansNonLooping()
        {
            var audio = new StubAudio();
            var position = new Vec2(3, -4);
            var h = audio.PlaySfx(Sound, 0.5, 1.25, position);

            var record = audio.ActiveSfxPlaybacks[h.Value];
            Assert.Equal(Sound, record.SoundId);
            Assert.Equal(0.5, record.Volume);
            Assert.Equal(1.25, record.Pitch);
            Assert.Equal(position, record.Position);
            Assert.False(record.Loop);
        }

        [Fact]
        public void PlaySfx_FiveArgOverload_RecordsLoopFlag_AndNullPosition()
        {
            var audio = new StubAudio();
            var looping = audio.PlaySfx(Sound, 1.0, 1.0, null, loop: true);
            var oneShot = audio.PlaySfx(OtherSound, 1.0, 1.0, null, loop: false);

            Assert.True(audio.ActiveSfxPlaybacks[looping.Value].Loop);
            Assert.False(audio.ActiveSfxPlaybacks[oneShot.Value].Loop);
            Assert.Null(audio.ActiveSfxPlaybacks[looping.Value].Position);
        }

        [Fact]
        public void SfxPlayback_FourArgConstructor_DefaultsLoopToFalse()
        {
            var record = new StubAudio.SfxPlayback(Sound, 1.0, 1.0, null);
            Assert.False(record.Loop);
        }

        [Fact]
        public void StopSfx_RemovesOnlyThatHandle_AndUnknownOrRepeatedStopDoesNotThrow()
        {
            var audio = new StubAudio();
            var a = audio.PlaySfx(Sound, 1, 1, null);
            var b = audio.PlaySfx(OtherSound, 1, 1, null);

            audio.StopSfx(a);
            Assert.DoesNotContain(a.Value, audio.ActiveSfxPlaybacks.Keys);
            Assert.Contains(b.Value, audio.ActiveSfxPlaybacks.Keys);
            Assert.False(audio.IsSfxPlaying(a));
            Assert.True(audio.IsSfxPlaying(b));

            var ex = Record.Exception(() =>
            {
                audio.StopSfx(a);
                audio.StopSfx(new SfxHandle(int.MaxValue));
            });
            Assert.Null(ex);
            Assert.Single(audio.ActiveSfxPlaybacks);
        }

        [Fact]
        public void CompleteSfx_MarksNaturalFinish_SameVisibleEffectAsStop_AndIgnoresUnknownHandle()
        {
            var audio = new StubAudio();
            var finished = audio.PlaySfx(Sound, 1, 1, null);
            var running = audio.PlaySfx(Sound, 1, 1, null);

            Assert.True(audio.IsSfxPlaying(finished));
            audio.CompleteSfx(finished);

            Assert.False(audio.IsSfxPlaying(finished));
            Assert.DoesNotContain(finished.Value, audio.ActiveSfxPlaybacks.Keys);
            Assert.True(audio.IsSfxPlaying(running));

            var ex = Record.Exception(() => audio.CompleteSfx(new SfxHandle(int.MaxValue)));
            Assert.Null(ex);
            Assert.Single(audio.ActiveSfxPlaybacks);
        }

        [Fact]
        public void IsSfxPlaying_UnknownHandle_IsFalseNotNull_WhenPlaybackStateIsReported()
        {
            var audio = new StubAudio();
            Assert.True(audio.ReportsPlaybackState);
            Assert.False(audio.IsSfxPlaying(new SfxHandle(12345)));
        }

        [Fact]
        public void ReportsPlaybackState_False_MakesIsSfxPlayingNullForEveryHandle_EvenWhileActive()
        {
            var audio = new StubAudio { ReportsPlaybackState = false };
            var active = audio.PlaySfx(Sound, 1, 1, null);
            var stopped = audio.PlaySfx(Sound, 1, 1, null);
            audio.StopSfx(stopped);

            Assert.Null(audio.IsSfxPlaying(active));
            Assert.Null(audio.IsSfxPlaying(stopped));
            Assert.Null(audio.IsSfxPlaying(new SfxHandle(999)));
            // 开关只影响回报口径，不影响内部记录。
            Assert.Contains(active.Value, audio.ActiveSfxPlaybacks.Keys);

            audio.ReportsPlaybackState = true;
            Assert.True(audio.IsSfxPlaying(active));
        }

        [Fact]
        public void PlayMusic_ReplacesCurrentTrack_AndRecordsFadeAndLoop()
        {
            var audio = new StubAudio();
            Assert.Null(audio.CurrentMusic);

            audio.PlayMusic(new Id("mus.first"), 1.5, loop: true);
            audio.PlayMusic(new Id("mus.second"), 0.0, loop: false);

            var current = audio.CurrentMusic;
            Assert.NotNull(current);
            Assert.Equal(new Id("mus.second"), current!.Value.TrackId);
            Assert.Equal(0.0, current.Value.FadeInSeconds);
            Assert.False(current.Value.Loop);
        }

        [Fact]
        public void StopMusic_ClearsCurrent_AndRecordsFadeOutOfLatestCall()
        {
            var audio = new StubAudio();
            Assert.Null(audio.LastStopMusicFadeOutSeconds);

            audio.PlayMusic(new Id("mus.first"), 0, loop: true);
            audio.StopMusic(2.0);
            Assert.Null(audio.CurrentMusic);
            Assert.Equal(2.0, audio.LastStopMusicFadeOutSeconds);

            audio.StopMusic(0.25);
            Assert.Equal(0.25, audio.LastStopMusicFadeOutSeconds);
        }

        [Fact]
        public void SetBusVolume_KeepsLatestValuePerBus_Independently()
        {
            var audio = new StubAudio();
            audio.SetBusVolume(AudioBus.Music, 0.2);
            audio.SetBusVolume(AudioBus.Sfx, 0.7);
            audio.SetBusVolume(AudioBus.Music, 0.9);

            Assert.Equal(0.9, audio.BusVolumes[AudioBus.Music]);
            Assert.Equal(0.7, audio.BusVolumes[AudioBus.Sfx]);
            Assert.DoesNotContain(AudioBus.Ui, audio.BusVolumes.Keys);
            Assert.Equal(2, audio.BusVolumes.Count);
        }
    }
}
