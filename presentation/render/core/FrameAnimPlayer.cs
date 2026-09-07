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
    /// 判断记录（跨帧关键帧不补发，R11 根治后废止——见 <see cref="Update"/> 判断记录）：本段此前的
    /// 结论是"<see cref="Update"/> 一次跨越多帧时……中途跳过的帧上的标记不补发……是可接受的已知
    /// 简化"。R11（architecture/落地计划/audit-5e779c6-20260907）指出这个简化的代价被低估了：
    /// 命中帧/事件帧驱动的是打击 VFX/SFX 一类反馈动作的触发时刻本身（不是数值结算，09 第 4.3 节的
    /// 边界依然成立——本类型仍然不回退改变已经结算的战斗结果），一次卡顿/低帧率跨过命中帧就等于
    /// 这次攻击的打击特效完全不播，是玩家能直接感知到的可见缺陷，不是无关紧要的"偶发丢失"。改为
    /// <see cref="Update"/> 按顺序补发被跨过的每一帧（含关键帧判定），循环动画跨越回绕时也一并覆盖，
    /// 见该方法判断记录（含"补发范围有上限，不会重放整段跨越的每一整圈"这一有意的边界）。</para>
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
        /// 或已 <see cref="Stop"/>）时是空操作。
        /// <para>
        /// 判断记录（N18 根治，architecture/落地计划/audit-68c9bed-20260907/code-review.md）：
        /// <see cref="_current"/> 此前只在 <see cref="Play"/> 那一刻从 <see cref="_clips"/> 查一次、
        /// 此后整段播放期间缓存同一个对象引用——若调用方在播放中途用同一个 <c>clipId</c> 重新登记了
        /// 一份新的 <see cref="FrameAnimClip"/>（<see cref="_clips"/> 是外部共享的可写字典，见构造
        /// 函数），本类型对此一无所知，仍然按开始播放那一刻的旧帧数/帧率/关键帧继续推进，视觉上
        /// 表现为"永远停在旧内容"，必须调用方手工再 <see cref="Play"/> 一次才会用上新剪辑。
        /// 修复：每次 <see cref="Update"/> 开头都按当前 <c>clipId</c> 重新从 <see cref="_clips"/>
        /// 查一次最新版本——<see cref="_elapsedSeconds"/> 是纯时间累加量，不依赖具体帧数/帧率，
        /// 天然实现"保留播放进度（已经经过的真实时间），按新剪辑的时长/帧率重新解释这段时间对应
        /// 第几帧"，不需要额外的比例换算。剪辑对象引用确实发生变化（<c>clipSwapped</c>）时，即便
        /// 按新剪辑算出来的帧下标恰好与之前相同，也强制重新触发 <see cref="FrameChanged"/>——旧的
        /// "帧下标不变就跳过通知"优化（<see cref="SetFrame"/> 的 <c>frame == _lastFrame</c> 判断）
        /// 会让引擎适配层以为"这一帧没变化、不需要重新取贴图"，但实际上同一个帧下标在新剪辑里对应
        /// 的可能是完全不同的贴图（引擎适配层按 clipId+frame 下标查表取贴图，见
        /// <c>Adapter.Unity.Presentation.UnityFrameAnimPlayer.OnFrameChanged</c>）。
        /// </para>
        /// <para>
        /// 判断记录（R11 根治，architecture/落地计划/audit-5e779c6-20260907）：剪辑对象引用没有变化
        /// （<c>clipSwapped == false</c>，同一剪辑继续播放）时，把这次 <paramref name="dt"/> 跨越的
        /// 每一个帧边界依次 <see cref="SetFrame"/>（从 <c><see cref="_lastFrame"/> + 1</c> 到本次算出
        /// 的目标帧，含端点），不再只对"落地的最终帧"触发一次——中途被跨过的帧上的 <see
        /// cref="FrameAnimClip.Keyframes"/> 标记与 <see cref="FrameChanged"/> 都会按顺序补发。循环
        /// （<see cref="_loop"/>）跨越回绕（本次推进后到达/越过一轮时长）时先补完这一圈剩余的尾部帧
        /// （<c><see cref="_lastFrame"/> + 1</c> 到 <c>totalFrames - 1</c>），再从帧 0 补到回绕后的
        /// 目标帧——回绕当次也会重新触发帧 0 上的标记（同 <see cref="Play"/> 立即以第 0 帧触发一次的
        /// 既有惯例，循环动画每一圈开头本就该重新触发）。非循环自然播完时同样先补完尾部帧再触发
        /// <see cref="_onComplete"/>，不会跳过临近结尾的关键帧。
        /// </para>
        /// <para>
        /// 补发范围有意设了上限（同"允许一个不受控制的多余实体短暂存在"一类判断记录的克制风格）：
        /// 即便本次 <paramref name="dt"/> 大到跨越了不止一整圈（长时间挂起后恢复一类极端场景），也
        /// 只追赶"这一圈剩余尾部 + 回绕后的目标帧"这一份，不会把中途完整跨过的每一整圈都重放一遍
        /// （那样一次巨大的 <paramref name="dt"/> 可能瞬间触发成百上千次关键帧回调，本身就是另一种
        /// 不合理的表现），这是"不丢关键帧"与"不引入无界的补发风暴"之间的权衡。
        /// </para>
        /// <para>
        /// 剪辑对象引用发生变化（<c>clipSwapped == true</c>）时不做追赶补发：旧剪辑的帧序列在新剪辑
        /// 里没有确定的对应关系，强行按新剪辑补发旧区间会产生无意义的关键帧，维持 N18 既有行为——
        /// 直接跳到按新剪辑重新解释后的目标帧，只强制触发一次 <see cref="FrameChanged"/>（<see
        /// cref="SetFrame"/> 的 <c>forceNotify</c>）。
        /// </para>
        /// </summary>
        public void Update(double dt)
        {
            if (_current == null || dt <= 0)
            {
                return;
            }

            var previousClip = _current;
            if (_clips.TryGetValue(_current.ClipId, out var latest))
            {
                _current = latest;
            }
            var clipSwapped = !ReferenceEquals(previousClip, _current);

            _elapsedSeconds += dt * _speed;
            var totalFrames = _current.FrameCount;
            var durationSeconds = totalFrames / _current.FrameRate;

            if (clipSwapped)
            {
                if (_elapsedSeconds >= durationSeconds)
                {
                    if (_loop)
                    {
                        _elapsedSeconds %= durationSeconds;
                    }
                    else
                    {
                        SetFrame(totalFrames - 1, forceNotify: true);
                        _current = null;
                        _lastFrame = -1;
                        InvokeAll(_onComplete);
                        return;
                    }
                }

                var swappedFrame = ClampFrame((int)(_elapsedSeconds * _current.FrameRate), totalFrames);
                SetFrame(swappedFrame, forceNotify: true);
                return;
            }

            if (_elapsedSeconds >= durationSeconds)
            {
                // 先补完这一圈（或这一段非循环播放）剩余的尾部帧，含端点 totalFrames - 1。
                for (var f = _lastFrame + 1; f < totalFrames; f++)
                {
                    SetFrame(f);
                }

                if (_loop)
                {
                    _elapsedSeconds %= durationSeconds;
                    var wrappedFrame = ClampFrame((int)(_elapsedSeconds * _current.FrameRate), totalFrames);
                    for (var f = 0; f <= wrappedFrame; f++)
                    {
                        SetFrame(f);
                    }
                    return;
                }

                _current = null;
                _lastFrame = -1;
                InvokeAll(_onComplete);
                return;
            }

            var frame = ClampFrame((int)(_elapsedSeconds * _current.FrameRate), totalFrames);
            for (var f = _lastFrame + 1; f <= frame; f++)
            {
                SetFrame(f);
            }
        }

        private static int ClampFrame(int frame, int totalFrames) => frame >= totalFrames ? totalFrames - 1 : frame;

        private void SetFrame(int frame, bool forceNotify = false)
        {
            if (frame == _lastFrame && !forceNotify)
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
