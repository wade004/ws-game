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

        /// <summary>
        /// <paramref name="position"/>（ADR-0016 决策 3 新增）为空时按无空间衰减方式播放
        /// （<c>spatialBlend = 0</c>，与既有行为一致）；非空时把音源挪到该世界坐标并启用 2D 声像
        /// （<c>spatialBlend = 1</c>，Unity 默认的对数衰减曲线），提供基本的"离得越远越小声"效果——
        /// 不要求精确匹配任何具体游戏的衰减调校，属于"允许忽略"范围内的最小合理实现（见 02 第 1.4
        /// 节"不支持空间音频的实现可以忽略该参数"）。
        /// </summary>
        public SfxHandle PlaySfx(Id soundId, double volume, double pitch, Vec2? position)
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

            if (position.HasValue)
            {
                slot.Source.transform.position = new Vector3((float)position.Value.X, (float)position.Value.Y, 0f);
                slot.Source.spatialBlend = 1f;
            }
            else
            {
                slot.Source.spatialBlend = 0f;
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
            // R02 根治（architecture/落地计划/audit-5e779c6-20260907）：_musicUsingA 记录的是"A 是否
            // 当前正在播放/淡入的音源"这一状态，见 PlayMusic 判断记录——PlayMusic 内部先按翻转前的
            // _musicUsingA 算出 incoming/outgoing，播放完 incoming 之后才把 _musicUsingA 取反，取反后
            // 的新值就等价于"_musicUsingA == true 时 A 是当前活跃音源、否则 B 是"。旧实现在这里（以及
            // 下面 Tick 的两处）沿用了 PlayMusic 内部翻转前那套"_musicUsingA ? B : A"映射，但读取的
            // 却是翻转之后的值——选出的其实是上一轮的 outgoing（正在淡出/已经 Stop 的旧音源），不是
            // 当前实际在播放的音源。表现为交叉淡入时新旧音源互换：Stop 音量为 0 的旧音源（听感上什么
            // 也没发生），真正在响的音源永远淡不进来、也停不掉。改为 "_musicUsingA ? A : B" 才是与
            // 当前 _musicUsingA 语义一致的映射。
            var current = _musicUsingA ? _musicSourceA : _musicSourceB;
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

        /// <summary>R02 测试用：当前"应该在播放/淡入"的音乐源与另一个（正在淡出/空闲）的音乐源，
        /// 按与 <see cref="StopMusic"/>/<see cref="Tick"/> 判断记录一致的映射计算——供 PlayMode 测试
        /// 直接断言 Tick/StopMusic 操作的是哪一个 <see cref="AudioSource"/>，不必在测试里重复实现一遍
        /// 内部映射逻辑（同 <see cref="PlaySfxCallCount"/>/<see cref="GetBusVolume"/> 一类"诊断/测试用
        /// 非契约成员"惯例）。</summary>
        internal AudioSource ActiveMusicSource => _musicUsingA ? _musicSourceA : _musicSourceB;

        internal AudioSource InactiveMusicSource => _musicUsingA ? _musicSourceB : _musicSourceA;

        /// <summary>由 UnityEngineHost.Update 每帧调用：推进音乐淡入淡出、把总线音量实时应用到正在
        /// 播放的音乐（SFX 总线音量只影响新播放，见类型顶部判断记录），并回收自然播放结束的 SFX 池位
        /// （R10 根治，见 <see cref="ReclaimFinishedSfxSlots"/>）。</summary>
        internal void Tick(double deltaSeconds)
        {
            ReclaimFinishedSfxSlots();

            // R02 根治：同 StopMusic 判断记录——这里的 active 必须用"_musicUsingA ? A : B"才是当前
            // 实际在播放（PlayMusic 刚设置好 clip 并 Play() 的那个 incoming）的音源，不是
            // "_musicUsingA ? B : A"选出的上一轮 outgoing。
            if (_musicFading)
            {
                _musicFadeElapsed += deltaSeconds;
                var t = _musicFadeDuration <= 0 ? 1.0 : Math.Min(1.0, _musicFadeElapsed / _musicFadeDuration);
                var factor = _musicFadeFrom + (_musicFadeTo - _musicFadeFrom) * t;
                var active = _musicUsingA ? _musicSourceA : _musicSourceB;
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
                var active = _musicUsingA ? _musicSourceA : _musicSourceB;
                if (active.isPlaying)
                {
                    active.volume = (float)(_musicBaseVolume * _busVolumes[AudioBus.Music]);
                }
            }
        }

        /// <summary>
        /// R10 根治（architecture/落地计划/audit-5e779c6-20260907）：<see cref="StopSfx"/> 是此前唯一
        /// 会把池位标回 <c>Active = false</c>、并从 <see cref="_activeByHandle"/> 摘除的地方——一次
        /// <see cref="PlaySfx"/> 若从未收到对应的显式 <see cref="StopSfx"/> 调用（游戏里绝大多数一次性
        /// 音效本就是"播完自然结束"，调用方压根不持有/不调用 Stop），clip 自然播完之后
        /// <c>AudioSource.isPlaying</c> 已经变回 <c>false</c>，但池位仍然标记为 <c>Active = true</c>、
        /// handle 仍然占着 <see cref="_activeByHandle"/> 的一条映射——<see cref="RentSlot"/> 找不到它
        /// （被跳过），每次播放都新建一个 <see cref="AudioSource"/>，池子只增不减，长时间运行后池子
        /// 无限膨胀。每帧 <see cref="Tick"/> 时扫一遍标记为 Active 的池位，clip 已经不在播放
        /// （<c>isPlaying == false</c>）的视为"自然播放结束"，按 <see cref="StopSfx"/> 同样的方式回收
        /// （标回 <c>Active = false</c>、摘除 handle 映射）——不区分"自然播完"与"clip 缺失、Play() 从
        /// 未真正被调用过"（<see cref="PlaySfx"/> 缺 clip 时 <c>isPlaying</c> 从一开始就是 false，本
        /// 方法同样会在下一帧把它回收，这类池位本就不该继续占着一个 handle，回收掉不产生任何副作用，
        /// 见 <see cref="StopSfx"/> 判断记录"停止一个已经自然播放结束/不存在的句柄是正常场景"——回收
        /// 后调用方再对该 handle 调用 StopSfx 会静默无操作，不是错误）。
        /// </summary>
        private void ReclaimFinishedSfxSlots()
        {
            for (var i = 0; i < _sfxPool.Count; i++)
            {
                var slot = _sfxPool[i];
                if (slot.Active && !slot.Source.isPlaying)
                {
                    slot.Active = false;
                    _activeByHandle.Remove(slot.Handle);
                }
            }
        }

        /// <summary>R10 测试用：SFX 对象池当前的物理 <see cref="AudioSource"/> 总数（诊断/测试用，同
        /// <see cref="PlaySfxCallCount"/> 一类"非契约成员"惯例）——用于断言池位确实被回收复用，没有
        /// 随着"播放又自然结束"的次数无限增长。</summary>
        internal int SfxPoolSize => _sfxPool.Count;

        /// <summary>R10 测试用：当前仍标记为占用（<c>Active == true</c>）的 SFX 池位数量。</summary>
        internal int ActiveSfxCount => _activeByHandle.Count;

        /// <summary>R10 测试用：按句柄取回池位实际持有的 <see cref="AudioSource"/>（找不到返回
        /// <c>null</c>）——生产路径下 <see cref="PlaySfx"/> 播放的 clip 由 <see
        /// cref="UnityResourceLoader"/> 真正解码得到（本任务不改该文件），测试需要这个访问器直接给
        /// 已经分配好的池位注入一段可播放的 clip 并手动 <c>Play()</c>，才能观察"自然播放结束"这一
        /// 真实状态迁移，不依赖资源加载管线。</summary>
        internal AudioSource? GetSfxSourceForHandle(SfxHandle handle) =>
            _activeByHandle.TryGetValue(handle.Value, out var slot) ? slot.Source : null;

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
