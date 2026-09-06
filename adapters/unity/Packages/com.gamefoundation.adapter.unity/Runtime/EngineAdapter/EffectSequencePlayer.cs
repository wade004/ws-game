#nullable enable
// EffectSequencePlayer：UnityRenderer2D.EmitParticle 在 ResourceKind.Effect 资源解析成功时
// 播放的最小序列帧动画组件（见 UnityResourceLoader.EffectAsset 判断记录：占位美术的"特效"是
// atlas.png + frames.json 描述的序列帧，不是预制好的 ParticleSystem）。挂一个 SpriteRenderer，
// 按 frames.json 的 fps/frame_duration/loop 逐帧切换 sprite；非循环播放完最后一帧后自动停止并
// 触发 OnFinished，供 UnityRenderer2D 回收进对象池（同 ParticleSystem 池的既有惯例）。
using System;
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class EffectSequencePlayer : MonoBehaviour
    {
        private Sprite[] _frames = Array.Empty<Sprite>();
        private double[] _durations = Array.Empty<double>();
        private bool _loop;
        private int _frameIndex;
        private double _elapsedInFrame;
        private bool _playing;
        private SpriteRenderer? _renderer;

        /// <summary>非循环播放自然结束时触发一次，供调用方回收本实例。</summary>
        public event Action? OnFinished;

        // 判断记录（W3b 修复：不用 ??/??= 惰性初始化 UnityEngine.Object 字段）：C# 的 ??/??=
        // 运算符对引用类型只做 CLR 层面的真 null 判断，不会调用 UnityEngine.Object 重载的
        // operator==（后者额外检查原生对象是否已被销毁，即"伪 null"，见 Unity 官方文档"Custom
        // == operator..."一节的一贯提醒）——本类型此前用 `_renderer ??= gameObject.GetComponent
        // <SpriteRenderer>() ?? gameObject.AddComponent<SpriteRenderer>();` 在批处理 PlayMode 测试
        // 环境下实测触发 `SpriteRenderer.set_sprite` 抛 `MissingComponentException`（首次真正驱动
        // EffectSequencePlayer.Play 的 PlayMode 用例复现，见 UnityRenderer2DTests.
        // EmitParticle_WithRealSequenceFrameResource_TriggersEffectSequencePlayer_NotFallback 判断
        // 记录）：`gameObject.AddComponent<SpriteRenderer>()` 在该场景下返回的引用未能通过后续
        // `_renderer.sprite = ...` 的原生对象存活性检查，怀疑与 `??=` 把一个"实际存活但 CLR 引用
        // 相等性判断异常"的返回值直接缓存有关。改为显式 `if (_renderer == null)`（触发
        // UnityEngine.Object 的重载 == ，真正检查原生对象是否存活）后不再复现。
        private SpriteRenderer Renderer
        {
            get
            {
                if (_renderer == null)
                {
                    _renderer = gameObject.GetComponent<SpriteRenderer>();
                    if (_renderer == null)
                    {
                        _renderer = gameObject.AddComponent<SpriteRenderer>();
                    }
                }
                return _renderer;
            }
        }

        public void Play(Sprite[] frames, double[] frameDurations, bool loop)
        {
            _frames = frames;
            _durations = frameDurations;
            _loop = loop;
            _frameIndex = 0;
            _elapsedInFrame = 0;
            _playing = frames.Length > 0;

            if (_playing)
            {
                Renderer.sprite = _frames[0];
            }
        }

        public void StopImmediately()
        {
            _playing = false;
        }

        private void Update()
        {
            if (!_playing || _frames.Length == 0)
            {
                return;
            }

            _elapsedInFrame += Time.deltaTime;
            var currentDuration = _durations[_frameIndex] > 0 ? _durations[_frameIndex] : 0.05;

            while (_elapsedInFrame >= currentDuration)
            {
                _elapsedInFrame -= currentDuration;
                _frameIndex++;

                if (_frameIndex >= _frames.Length)
                {
                    if (_loop)
                    {
                        _frameIndex = 0;
                    }
                    else
                    {
                        _frameIndex = _frames.Length - 1;
                        _playing = false;
                        Renderer.sprite = _frames[_frameIndex];
                        OnFinished?.Invoke();
                        return;
                    }
                }

                Renderer.sprite = _frames[_frameIndex];
                currentDuration = _durations[_frameIndex] > 0 ? _durations[_frameIndex] : 0.05;
            }
        }
    }
}
