// StubAudio：IAudio 的最小可用桩实现——只记录调用，不播放任何真实声音。
// 用途：测试断言"某个音效/音乐被播放了、总线音量被设置成了多少"，而不启动任何音频设备。
// 与真实实现的差异：句柄自增分配；StopSfx 对已停止或不存在的句柄直接返回（不抛异常，
// 因为音效自然播放结束后停止一个"已经不存在"的句柄是正常场景，不视为逻辑错误，这点与
// 精灵/模型实例的"用了已销毁实例即报错"语义不同——由渲染器负责显式生命周期管理，
// 而音效的生命周期本就允许自然结束）。
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Stub
{
    public sealed class StubAudio : IAudio
    {
        public readonly struct SfxPlayback
        {
            public readonly Id SoundId;
            public readonly double Volume;
            public readonly double Pitch;

            public SfxPlayback(Id soundId, double volume, double pitch)
            {
                SoundId = soundId;
                Volume = volume;
                Pitch = pitch;
            }
        }

        public readonly struct MusicPlayback
        {
            public readonly Id TrackId;
            public readonly double FadeInSeconds;
            public readonly bool Loop;

            public MusicPlayback(Id trackId, double fadeInSeconds, bool loop)
            {
                TrackId = trackId;
                FadeInSeconds = fadeInSeconds;
                Loop = loop;
            }
        }

        private int _nextSfxHandle = 1;
        private readonly HashSet<int> _activeSfx = new HashSet<int>();

        public readonly Dictionary<int, SfxPlayback> ActiveSfxPlaybacks = new Dictionary<int, SfxPlayback>();
        public readonly Dictionary<AudioBus, double> BusVolumes = new Dictionary<AudioBus, double>();
        public MusicPlayback? CurrentMusic { get; private set; }
        public double? LastStopMusicFadeOutSeconds { get; private set; }

        public SfxHandle PlaySfx(Id soundId, double volume, double pitch)
        {
            var handle = new SfxHandle(_nextSfxHandle++);
            _activeSfx.Add(handle.Value);
            ActiveSfxPlaybacks[handle.Value] = new SfxPlayback(soundId, volume, pitch);
            return handle;
        }

        public void StopSfx(SfxHandle handle)
        {
            _activeSfx.Remove(handle.Value);
            ActiveSfxPlaybacks.Remove(handle.Value);
        }

        public void PlayMusic(Id trackId, double fadeInSeconds, bool loop)
        {
            CurrentMusic = new MusicPlayback(trackId, fadeInSeconds, loop);
        }

        public void StopMusic(double fadeOutSeconds)
        {
            LastStopMusicFadeOutSeconds = fadeOutSeconds;
            CurrentMusic = null;
        }

        public void SetBusVolume(AudioBus bus, double volume)
        {
            BusVolumes[bus] = volume;
        }
    }
}
