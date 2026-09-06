#nullable enable
// UnityFrameAnimPlayer：Presentation.Render.IFrameAnimPlayer 的 Unity 实现（W3b 收边，拍板 6
// "IFrameAnimPlayer 契约 + Unity 实现（复用 EffectSequencePlayer 思路做角色序列帧）"）。
//
// 判断记录（组合引擎无关参考实现，不重新实现推进逻辑）：Presentation.Render.FrameAnimPlayer（见
// presentation/render/core/FrameAnimPlayer.cs 类型注释）已经是"经过的时间 → 当前帧下标"推进与
// 关键帧/播放完成事件派发的完整引擎无关实现，额外暴露 FrameChanged 事件正是"供引擎适配层订阅后
// 按帧号切换实际显示的贴图，不需要重新实现一遍计时推进逻辑"——本类型按该类型注释的设计意图，
// 内部持有一个 Presentation.Render.FrameAnimPlayer 实例并把 IFrameAnimPlayer 的四个方法
// （Play/Stop/OnComplete/OnAnimEvent）原样转发给它，本类型只多做一件事：订阅 FrameChanged，
// 按 clipId 查表取出该帧对应的 Sprite，写到自带的 SpriteRenderer 上——与
// Adapter.Unity.EngineAdapter.EffectSequencePlayer（vfx 序列帧播放器）同一套"挂一个
// SpriteRenderer，按帧号切换 sprite"思路，因此本类型不需要自己处理循环/关键帧判定这些容易出错的
// 边界情况，只处理"帧号 -> 具体贴图"这一层 Unity 特有的粘合。
//
// 判断记录（帧源解析：不采用 14 §1.2 嵌套目录/扁平命名规则，改用注册期直接传入 Sprite[]）：
// 14_资产规格书模板.md 第 1.2 节给出的是"序列帧动画（特效等）按 <name>/frame_<三位帧序号>.<扩展名>
// 逐帧落盘 + atlas.png + frames.json"（与 assets/_placeholder/vfx/hit_spark/ 结构完全一致，供
// UnityResourceLoader.TryGetEffect 解析成 EffectAsset），但该资源目录约定截至本轮只有 vfx 特效
// 一类占位资产真正落盘（toolchain/gen_placeholder_assets.py 不在本任务范围，见任务书），没有任何
// 角色序列帧占位资源可供本类型按同一套目录规则去加载——占位英雄现有资源
// （layer.placeholder_hero__front__body 等）都是单帧静态图，不是按 frame_NNN 编号的序列。本类型
// 因此不新造一套尚无落地资源可验证的"角色序列帧资源 id 规则"，改为把"clipId -> 具体 Sprite[]"的
// 解析责任交给调用方（见 RegisterClip/RegisterClipFromEffect/RegisterSingleFrameClip 三个注册
// 方法）——调用方可以是：① 已经有 vfx 风格 atlas+frames.json 落地资源的具体游戏，经
// RegisterClipFromEffect 复用 UnityResourceLoader.TryGetEffect 的解析结果；② 暂无多帧资源、只想
// 让"待机/受击/死亡"等状态在 PlayClip 时至少切换到对应静态图的游戏，经 RegisterSingleFrameClip
// 把现有单帧占位图注册成一个"1 帧剪辑"（Adapter.Unity.Shell.AnimClipResolver 现按后者接入占位
// 英雄，见该类型判断记录）；③ PlayMode 测试用内存构造的多帧 Sprite[]，经 RegisterClip 直接注入，
// 不依赖任何磁盘资源（见 Tests/Runtime/UnityFrameAnimPlayerTests.cs）。一旦具体游戏落地了真正的
// 角色序列帧占位资源（toolchain/gen_placeholder_assets.py 扩展），RegisterClipFromEffect 已经是
// 可直接使用的通路，不需要改动本类型。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.Render;
using UnityEngine;

namespace Adapter.Unity.Presentation
{
    public sealed class UnityFrameAnimPlayer : MonoBehaviour, IFrameAnimPlayer
    {
        private readonly Dictionary<Id, FrameAnimClip> _clipMetaById = new Dictionary<Id, FrameAnimClip>();
        private readonly Dictionary<Id, Sprite[]> _framesByClipId = new Dictionary<Id, Sprite[]>();

        private FrameAnimPlayer? _inner;
        private SpriteRenderer? _renderer;

        // 判断记录：不用 ??/??= 惰性初始化 UnityEngine.Object 字段，见
        // Adapter.Unity.EngineAdapter.EffectSequencePlayer 同名字段/属性判断记录（本类型复用
        // 该类型的落地思路，同一处已知 Unity 陷阱一并修正）。
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

        private FrameAnimPlayer Inner
        {
            get
            {
                if (_inner == null)
                {
                    _inner = new FrameAnimPlayer(_clipMetaById);
                    _inner.FrameChanged += OnFrameChanged;
                }
                return _inner;
            }
        }

        /// <summary>登记一个剪辑：<paramref name="frames"/> 长度即帧数，<paramref name="frameRate"/>
        /// 播放帧率（帧/秒），<paramref name="keyframes"/> 关键帧标记（见
        /// <see cref="FrameAnimClip.Keyframes"/>，命中帧用 <see cref="FrameAnimClip.HitFrameMarker"/>）。
        /// 重复登记同一 <paramref name="clipId"/> 直接覆盖（同 <see cref="Presentation.Render.FrameAnimPlayer"/>
        /// 底层字典语义）。</summary>
        public void RegisterClip(Id clipId, Sprite[] frames, double frameRate, IReadOnlyDictionary<string, int>? keyframes = null)
        {
            if (frames == null || frames.Length == 0)
            {
                throw new ArgumentException("frames 不能为空", nameof(frames));
            }

            _clipMetaById[clipId] = new FrameAnimClip(clipId, frames.Length, frameRate, keyframes);
            _framesByClipId[clipId] = frames;
        }

        /// <summary>把一张已加载的单帧静态图登记成一个"1 帧剪辑"（见类型顶部判断记录②）：
        /// <see cref="Play"/> 时立即切到该帧并停留，<paramref name="loop"/>/<paramref name="speed"/>
        /// 参数在只有一帧的情况下不产生可观察效果，但仍然会在正确的时机触发
        /// <see cref="OnComplete"/>（<c>loop=false</c> 时）——不是"什么都不做的哑实现"。</summary>
        public void RegisterSingleFrameClip(Id clipId, Sprite frame) => RegisterClip(clipId, new[] { frame }, frameRate: 1.0);

        /// <summary>从一个已经经 <see cref="Adapter.Unity.EngineAdapter.UnityResourceLoader.TryGetEffect"/>
        /// 加载成功的序列帧特效资产登记一个剪辑（见类型顶部判断记录①，复用 vfx atlas+frames.json
        /// 资源目录约定——14 §1.2 "序列帧动画（特效等）……逐帧落盘"本就不区分特效与角色，仅要求资源
        /// 已经加载完成，本方法不负责触发加载）。<paramref name="effect"/> 的 <c>Loop</c> 字段本身
        /// 只是 frames.json 声明的建议值，最终是否循环仍由调用方 <see cref="Play"/> 时的
        /// <c>loop</c> 参数决定（同 <see cref="Adapter.Unity.EngineAdapter.UnityRenderer2D.EmitParticle"/>
        /// 对 vfx 场景的既有用法一致，二者是两套独立调用方，互不影响）。</summary>
        public void RegisterClipFromEffect(Id clipId, Adapter.Unity.EngineAdapter.UnityResourceLoader.EffectAsset effect, IReadOnlyDictionary<string, int>? keyframes = null)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));

            var frames = new Sprite[effect.Frames.Length];
            double frameRate = 12.0; // frames.json 未强制携带整体帧率字段，取 EffectSequencePlayer 同款默认 12fps 兜底。
            if (effect.Frames.Length > 0 && effect.Frames[0].Duration > 0)
            {
                frameRate = 1.0 / effect.Frames[0].Duration;
            }
            for (var i = 0; i < effect.Frames.Length; i++)
            {
                frames[i] = effect.Frames[i].Sprite;
            }

            RegisterClip(clipId, frames, frameRate, keyframes);
        }

        public bool HasClip(Id clipId) => _clipMetaById.ContainsKey(clipId);

        public Id? CurrentClipId => _inner?.CurrentClipId;

        public int CurrentFrame => _inner?.CurrentFrame ?? 0;

        public void Play(Id clipId, bool loop, double speed) => Inner.Play(clipId, loop, speed);

        public void Stop() => Inner.Stop();

        public SubscriptionHandle OnComplete(Action callback) => Inner.OnComplete(callback);

        public SubscriptionHandle OnAnimEvent(Action<string> callback) => Inner.OnAnimEvent(callback);

        private void OnFrameChanged(int frameIndex)
        {
            var clipId = _inner?.CurrentClipId;
            if (clipId == null)
            {
                return;
            }

            if (_framesByClipId.TryGetValue(clipId.Value, out var frames) && frameIndex >= 0 && frameIndex < frames.Length)
            {
                Renderer.sprite = frames[frameIndex];
            }
        }

        private void Update()
        {
            _inner?.Update(Time.deltaTime);
        }
    }
}
