using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// <see cref="IFrameAnimPlayer"/> 的引擎无关参考实现（见任务书"桩实现用于测试；Unity 实现归
    /// W3b"）：只做"经过的时间 → 当前帧下标"的推进与关键帧/播放完成事件派发，不持有、不调用任何具体
    /// 引擎的精灵/纹理 API（P4）——真正"把第几帧对应的贴图换上去"是 W3b 的事，本类型额外暴露
    /// <see cref="FrameChanged"/>，供引擎适配层订阅后按帧号切换实际显示的贴图，不需要重新实现一遍
    /// 计时推进逻辑。
    /// <para>
    /// 判断记录（跨帧关键帧不补发）：<see cref="Update"/> 一次跨越多帧时（大 <c>dt</c>/低帧率场景），
    /// 只对"推进后落在的那一帧"精确匹配的关键帧标记触发一次，中途跳过的帧上的标记不补发——序列帧
    /// 动画不是逻辑判定的来源（09 第 4.3 节"表现层的关键帧只调节反馈动作播放的呈现时刻，不得回退去
    /// 改变已经结算的战斗结果"），偶发的大 <c>dt</c> 丢失一次视觉关键帧回调是可接受的已知简化，不需要
    /// 为此引入"追赶"逻辑增加复杂度。</para>
    /// </summary>
    public sealed class FrameAnimPlayer : IFrameAnimPlayer
    {
        private readonly IReadOnlyDictionary<Id, FrameAnimClip> _clips;

        private FrameAnimClip? _current;
        private bool _loop;
        private double _speed = 1.0;
        private double _elapsedSeconds;
        private int _lastFrame = -1;

        private readonly List<Action> _onComplete = new List<Action>();
        private readonly List<Action<string>> _onAnimEvent = new List<Action<string>>();

        /// <summary>当前帧下标变化时触发（见类型注释），供引擎适配层订阅切换实际显示贴图；播放
        /// 开始（<see cref="Play"/>）时立即以第 0 帧触发一次。</summary>
        public event Action<int>? FrameChanged;

        public FrameAnimPlayer(IReadOnlyDictionary<Id, FrameAnimClip> clips)
        {
            _clips = clips ?? throw new ArgumentNullException(nameof(clips));
        }

        public int CurrentFrame => _lastFrame < 0 ? 0 : _lastFrame;

        public Id? CurrentClipId => _current?.ClipId;

        public void Play(Id clipId, bool loop, double speed)
        {
            if (speed <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(speed), "speed 必须为正数");
            }
            if (!_clips.TryGetValue(clipId, out var clip))
            {
                throw new ArgumentException($"未登记的序列帧剪辑 \"{clipId}\"", nameof(clipId));
            }

            _current = clip;
            _loop = loop;
            _speed = speed;
            _elapsedSeconds = 0.0;
            _lastFrame = 0;
            FrameChanged?.Invoke(0);
            FireKeyframesAt(0);
        }

        public void Stop()
        {
            _current = null;
            _lastFrame = -1;
        }

        public SubscriptionHandle OnComplete(Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            _onComplete.Add(callback);
            return new SubscriptionHandle(() => _onComplete.Remove(callback));
        }

        public SubscriptionHandle OnAnimEvent(Action<string> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            _onAnimEvent.Add(callback);
            return new SubscriptionHandle(() => _onAnimEvent.Remove(callback));
        }

        /// <summary>按 <paramref name="dt"/>（秒）推进当前播放中的剪辑；空闲（未 <see cref="Play"/>
        /// 或已 <see cref="Stop"/>）时是空操作。</summary>
        public void Update(double dt)
        {
            if (_current == null || dt <= 0)
            {
                return;
            }

            _elapsedSeconds += dt * _speed;
            var totalFrames = _current.FrameCount;
            var durationSeconds = totalFrames / _current.FrameRate;

            if (_elapsedSeconds >= durationSeconds)
            {
                if (_loop)
                {
                    _elapsedSeconds %= durationSeconds;
                }
                else
                {
                    SetFrame(totalFrames - 1);
                    var clip = _current;
                    _current = null;
                    _lastFrame = -1;
                    InvokeAll(_onComplete);
                    _ = clip; // 仅用于表达"剪辑自然播完"这一事实已被消费，避免未使用变量告警噪音。
                    return;
                }
            }

            var frame = (int)(_elapsedSeconds * _current.FrameRate);
            if (frame >= totalFrames)
            {
                frame = totalFrames - 1;
            }
            SetFrame(frame);
        }

        private void SetFrame(int frame)
        {
            if (frame == _lastFrame)
            {
                return;
            }
            _lastFrame = frame;
            FrameChanged?.Invoke(frame);
            FireKeyframesAt(frame);
        }

        private void FireKeyframesAt(int frame)
        {
            if (_current == null || _onAnimEvent.Count == 0)
            {
                return;
            }

            foreach (var kv in _current.Keyframes)
            {
                if (kv.Value == frame)
                {
                    InvokeAll(_onAnimEvent, kv.Key);
                }
            }
        }

        private static void InvokeAll(List<Action> handlers)
        {
            // 复制快照：允许回调内部取消订阅而不影响本次遍历（惯例同 core/foundation/event_bus.EventBus）。
            var snapshot = handlers.ToArray();
            foreach (var handler in snapshot)
            {
                handler();
            }
        }

        private static void InvokeAll(List<Action<string>> handlers, string arg)
        {
            var snapshot = handlers.ToArray();
            foreach (var handler in snapshot)
            {
                handler(arg);
            }
        }
    }
}
