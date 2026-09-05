#nullable enable
// UnityAudio：IAudio 的 Unity 引擎实现。
//
// 判断记录（总线音量方案，任务书要求二选一并记录）：选用"简单分组乘算"而非 AudioMixer——
// AudioMixer 需要一份预先在编辑器里手工创建、提交进版本库的 .mixer 资产，与本框架"L-1 引擎
// 实现不依赖任何手工编辑的场景/预制体资产"的一贯做法不符；简单乘算（最终音量 = 调用方传入的
// volume × 对应总线的当前音量）在代码里就能完整表达 02 第 1.4 节"总线音量分别控制音量"的语义，
// 且更容易被 EditMode/PlayMode 测试直接断言。已知限制：SFX 是一次性播放，播放期间总线音量变化
// 不会更新"已经在播的那次"音量（只影响之后新播放的），与 StubAudio 记录调用参数、不做进一步
// 混音的最小实现精神一致；音乐总线音量变化会实时生效（见 Tick）。
//
// 对象池：SFX 用 AudioSource 对象池（父物体下的子 GameObject，用完不销毁、下次复用），
// 音乐用两个专用 AudioSource（用于 fadeIn/fadeOut 交叉淡入淡出）。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityAudio : IAudio
    {
        private sealed class SfxSlot
        {
            public AudioSource Source = null!;
            public int Handle;
            public bool Active;
        }

        private readonly Transform _root;
        private readonly UnityResourceLoader _resourceLoader;
        private readonly List<SfxSlot> _sfxPool = new List<SfxSlot>();
        private readonly Dictionary<int, SfxSlot> _activeByHandle = new Dictionary<int, SfxSlot>();
        private int _nextSfxHandle = 1;

        private readonly Dictionary<AudioBus, double> _busVolumes = new Dictionary<AudioBus, double>
        {
            { AudioBus.Music, 1.0 },
            { AudioBus.Sfx, 1.0 },
            { AudioBus.Ui, 1.0 },
            { AudioBus.Ambient, 1.0 }
        };

        private AudioSource _musicSourceA = null!;
        private AudioSource _musicSourceB = null!;
        private bool _musicUsingA = true;
        private double _musicBaseVolume;
        private double _musicFadeElapsed;
        private double _musicFadeDuration;
        private double _musicFadeFrom;
        private double _musicFadeTo;
        private bool _musicFading;
        private bool _musicStopFade;

        public UnityAudio(Transform root, UnityResourceLoader resourceLoader)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));

            _musicSourceA = CreateSource("MusicSourceA");
            _musicSourceA.loop = true;
            _musicSourceB = CreateSource("MusicSourceB");
            _musicSourceB.loop = true;
        }

        /// <summary>累计 <see cref="PlaySfx"/> 调用次数（诊断/测试用，不属于 <see cref="IAudio"/>
        /// 契约本身——与 <c>UnityResourceLoader.TryGetSprite</c> 之类"引擎实现之间的内部协作/诊断
        /// 方法"同一惯例）。即便对应音效资源尚未加载（见下方 Debug.LogWarning 分支）也计数，因为
        /// 本计数衡量的是"表现层→引擎适配层的播放调用链路是否被触发"，不是"是否真的听到了声音"。</summary>
        public int PlaySfxCallCount { get; private set; }

        public SfxHandle PlaySfx(Id soundId, double volume, double pitch)
        {
            PlaySfxCallCount++;
            var slot = RentSlot();
            var handle = _nextSfxHandle++;
            slot.Handle = handle;
            slot.Active = true;
            _activeByHandle[handle] = slot;

            if (_resourceLoader.TryGetAudioClip(soundId, out var clip))
            {
                slot.Source.clip = clip;
            }
            else
            {
                slot.Source.clip = null;
                Debug.LogWarning($"[UnityAudio] 音效资源未加载或不存在，跳过播放：{soundId}");
            }

            slot.Source.volume = (float)(volume * _busVolumes[AudioBus.Sfx]);
            slot.Source.pitch = (float)pitch;
            if (slot.Source.clip != null)
            {
                slot.Source.Play();
            }

            return new SfxHandle(handle);
        }

        public void StopSfx(SfxHandle handle)
        {
            // 契约语义：停止一个已经自然播放结束/不存在的句柄是正常场景，不视为错误（与
            // adapters/stub/StubAudio 的 StopSfx 语义一致），因此静默返回而不抛异常。
            if (_activeByHandle.TryGetValue(handle.Value, out var slot))
            {
                slot.Source.Stop();
                slot.Active = false;
                _activeByHandle.Remove(handle.Value);
            }
        }

        public void PlayMusic(Id trackId, double fadeInSeconds, bool loop)
        {
            var incoming = _musicUsingA ? _musicSourceB : _musicSourceA;
            var outgoing = _musicUsingA ? _musicSourceA : _musicSourceB;

            if (_resourceLoader.TryGetAudioClip(trackId, out var clip))
            {
                incoming.clip = clip;
            }
            else
            {
                incoming.clip = null;
                Debug.LogWarning($"[UnityAudio] 音乐资源未加载或不存在，跳过播放：{trackId}");
            }

            incoming.loop = loop;
            incoming.volume = 0f;
            if (incoming.clip != null)
            {
                incoming.Play();
            }

            _musicBaseVolume = 1.0;
            _musicUsingA = !_musicUsingA;

            if (fadeInSeconds <= 0)
            {
                _musicFading = false;
                incoming.volume = (float)(_busVolumes[AudioBus.Music]);
                outgoing.Stop();
            }
            else
            {
                _musicFading = true;
                _musicStopFade = false;
                _musicFadeElapsed = 0;
                _musicFadeDuration = fadeInSeconds;
                _musicFadeFrom = 0;
                _musicFadeTo = 1.0;
                // outgoing 由 Tick 里的淡出分支处理：这里直接快速淡出旧曲目，避免叠音过久。
                outgoing.Stop();
            }
        }

        public void StopMusic(double fadeOutSeconds)
        {
            var current = _musicUsingA ? _musicSourceB : _musicSourceA; // 上一次 PlayMusic 已切换 _musicUsingA
            if (fadeOutSeconds <= 0)
            {
                current.Stop();
                _musicFading = false;
                return;
            }

            _musicFading = true;
            _musicStopFade = true;
            _musicFadeElapsed = 0;
            _musicFadeDuration = fadeOutSeconds;
            _musicFadeFrom = current.volume / Mathf.Max((float)_busVolumes[AudioBus.Music], 0.0001f);
            _musicFadeTo = 0;
        }

        public void SetBusVolume(AudioBus bus, double volume)
        {
            _busVolumes[bus] = volume;
        }

        /// <summary>U3 新增：非契约诊断读取（同 <see cref="PlaySfxCallCount"/> 一类"引擎实现之间的
        /// 内部协作方法"惯例，见包 README）——<see cref="IAudio"/> 契约本身只有 <see cref="SetBusVolume"/>
        /// 没有对应的读取方法（见类型顶部"总线音量方案"判断记录），Unity 设置面板/测试需要一个只读
        /// 途径确认"总线音量确实已按设置面板的输入变化"，因此补一个只读属性访问器，不改变
        /// <see cref="IAudio"/> 契约本身。</summary>
        public double GetBusVolume(AudioBus bus) => _busVolumes.TryGetValue(bus, out var v) ? v : 1.0;

        /// <summary>由 UnityEngineHost.Update 每帧调用：推进音乐淡入淡出并把总线音量实时应用到
        /// 正在播放的音乐（SFX 总线音量只影响新播放，见类型顶部判断记录）。</summary>
        internal void Tick(double deltaSeconds)
        {
            if (_musicFading)
            {
                _musicFadeElapsed += deltaSeconds;
                var t = _musicFadeDuration <= 0 ? 1.0 : Math.Min(1.0, _musicFadeElapsed / _musicFadeDuration);
                var factor = _musicFadeFrom + (_musicFadeTo - _musicFadeFrom) * t;
                var active = _musicUsingA ? _musicSourceB : _musicSourceA;
                active.volume = (float)(factor * _busVolumes[AudioBus.Music]);

                if (t >= 1.0)
                {
                    _musicFading = false;
                    if (_musicStopFade)
                    {
                        active.Stop();
                    }
                }
            }
            else
            {
                var active = _musicUsingA ? _musicSourceB : _musicSourceA;
                if (active.isPlaying)
                {
                    active.volume = (float)(_musicBaseVolume * _busVolumes[AudioBus.Music]);
                }
            }
        }

        private SfxSlot RentSlot()
        {
            for (var i = 0; i < _sfxPool.Count; i++)
            {
                if (!_sfxPool[i].Active)
                {
                    return _sfxPool[i];
                }
            }

            var slot = new SfxSlot { Source = CreateSource($"SfxSource_{_sfxPool.Count}") };
            _sfxPool.Add(slot);
            return slot;
        }

        private AudioSource CreateSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, worldPositionStays: false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            return source;
        }
    }
}
