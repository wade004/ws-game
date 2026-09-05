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

        private SpriteRenderer Renderer =>
            _renderer ??= gameObject.GetComponent<SpriteRenderer>() ?? gameObject.AddComponent<SpriteRenderer>();

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
