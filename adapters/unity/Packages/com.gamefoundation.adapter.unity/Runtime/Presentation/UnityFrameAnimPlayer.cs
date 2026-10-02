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

        // ADR-0111：登记内容是"单帧占位"（RegisterSingleFrameClip，资源尚未加载/根本没有美术）的剪辑 id
        // 集合——真实内容（RegisterClipFromEffect）到达时移出。供 HasRealContent 回答"这条剪辑的真实
        // 美术是否已经到位"。
        private readonly HashSet<Id> _placeholderClipIds = new HashSet<Id>();

        // ADR-0112：登记内容的方向标签——剪辑 id -> 该内容来自哪个方向档位的美术（方向裸档位名）。没有条目
        // 表示"与方向无关"（单帧占位、无方向段的兜底资源、显式登记的剪辑），任何已显示方向下都可展示；
        // 有条目表示这是某个方向档位专属的美术。供整身兜底渲染器的方向不变量判断"它上面登记的内容是不是
        // 已显示方向的"（见 UnityViewFactory.ContentMatchesDisplayedDirection）。任何一次 RegisterClip 都会
        // 清掉旧标签（内容被换掉了，旧标签不再成立），带标签的登记重载在同一次调用里重新写上。
        private readonly Dictionary<Id, string> _contentDirectionByClip = new Dictionary<Id, string>();

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

        /// <summary>U04 根治新增（第五轮外部审核 audit-5e779c6-20260907/AUDIT_REPORT.md）：公开本组件
        /// 自带的 <see cref="SpriteRenderer"/>，供 <c>UnityViewFactory.AttachDefaultAnimation</c> 经
        /// <c>Adapter.Unity.EngineAdapter.UnityRenderer2D.RegisterAnimRootRenderer</c> 登记进精灵实例，
        /// 使这个渲染器与纸娃娃层一样参与 height 偏移、flash/fade 颜色和 flipX——本属性只是
        /// <see cref="Renderer"/> 私有 get-or-add 访问器的公开转发，不改变其懒创建语义。</summary>
        public SpriteRenderer SpriteRenderer => Renderer;

        private FrameAnimPlayer Inner
        {
            get
            {
                if (_inner == null)
                {
                    _inner = new FrameAnimPlayer(_clipMetaById);
                    _inner.FrameChanged += OnFrameChanged;
                    _inner.SetPaused(_paused);
                }
                return _inner;
            }
        }

        // 手感落地 M2-A（顿帧表现冻结，手感设计/07 第 5 节）：暂停标志存在本组件上而不是只转发给内部播放器——内部播放器是懒创建的，
        // 暂停期间才第一次 Play 的剪辑（含异步加载完成后才首次播放的冷路径）创建出来时仍按当前暂停状态启动，与热路径同一出口。
        private bool _paused;

        /// <summary>见 <see cref="IFrameAnimPlayer.IsPaused"/>。</summary>
        public bool IsPaused => _paused;

        /// <summary>见 <see cref="IFrameAnimPlayer.SetPaused"/>：暂停期间帧下标、关键帧与播放完成回调都不推进，恢复后从暂停点继续。</summary>
        public void SetPaused(bool paused)
        {
            _paused = paused;
            _inner?.SetPaused(paused);
        }

        /// <summary>按 <paramref name="deltaSeconds"/> 推进内部播放器（暂停时内部播放器自己忽略）；<see cref="Update"/> 以 <see cref="Time.deltaTime"/> 调用，
        /// 测试经此确定性地推进，不依赖真实帧时间。</summary>
        internal void Advance(double deltaSeconds)
        {
            if (_inner == null)
            {
                return;
            }

            // 实验室引擎宿主观测用：只记"确实在播放且未暂停"的推进量（暂停期间内部播放器忽略推进，这里同样不计）。
            if (!_paused && _inner.CurrentClipId.HasValue)
            {
                AdvancedSeconds += deltaSeconds;
            }

            _inner.Update(deltaSeconds);
        }

        /// <summary>累计推进的播放时间（秒；暂停期间与没有在播剪辑时不增长），供实验室引擎宿主观测"顿帧期间 rig 的动画时间确实没推进"。</summary>
        internal double AdvancedSeconds { get; private set; }

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
            _placeholderClipIds.Remove(clipId);
            _contentDirectionByClip.Remove(clipId);
        }

        /// <summary>把一张已加载的单帧静态图登记成一个"1 帧剪辑"（见类型顶部判断记录②）：
        /// <see cref="Play"/> 时立即切到该帧并停留，<paramref name="loop"/>/<paramref name="speed"/>
        /// 参数在只有一帧的情况下不产生可观察效果，但仍然会在正确的时机触发
        /// <see cref="OnComplete"/>（<c>loop=false</c> 时）——不是"什么都不做的哑实现"。</summary>
        public void RegisterSingleFrameClip(Id clipId, Sprite frame)
        {
            RegisterClip(clipId, new[] { frame }, frameRate: 1.0);
            _placeholderClipIds.Add(clipId);
        }

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
            ClipContentRegistered?.Invoke(clipId);
        }

        /// <summary>ADR-0112：同 <see cref="RegisterClipFromEffect(Id, Adapter.Unity.EngineAdapter.UnityResourceLoader.EffectAsset, IReadOnlyDictionary{string,int}?)"/>，
        /// 额外在触发 <see cref="ClipContentRegistered"/> 之前写入内容的方向标签（<paramref name="contentDirection"/>
        /// 为方向裸档位名；<c>null</c> 表示与方向无关）——订阅方在事件里读到的标签已经是新内容的。</summary>
        public void RegisterClipFromEffect(
            Id clipId, Adapter.Unity.EngineAdapter.UnityResourceLoader.EffectAsset effect,
            IReadOnlyDictionary<string, int>? keyframes, string? contentDirection)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));

            var frames = new Sprite[effect.Frames.Length];
            double frameRate = 12.0;
            if (effect.Frames.Length > 0 && effect.Frames[0].Duration > 0)
            {
                frameRate = 1.0 / effect.Frames[0].Duration;
            }
            for (var i = 0; i < effect.Frames.Length; i++)
            {
                frames[i] = effect.Frames[i].Sprite;
            }

            RegisterClip(clipId, frames, frameRate, keyframes);
            if (contentDirection != null)
            {
                _contentDirectionByClip[clipId] = contentDirection;
            }
            ClipContentRegistered?.Invoke(clipId);
        }

        /// <summary>ADR-0112：<paramref name="clipId"/> 当前登记内容的方向标签（方向裸档位名）；与方向无关或未
        /// 登记时为 <c>null</c>。</summary>
        internal string? GetClipContentDirection(Id clipId) =>
            _contentDirectionByClip.TryGetValue(clipId, out var dir) ? dir : null;

        /// <summary>ADR-0112：把整身渲染器立即刷新为当前剪辑的当前帧（内容被原地换掉后，渲染器上的贴图要等
        /// 下一次帧号变化才会更新，最多滞后一个帧间隔——方向提交时不能容忍这一段旧方向画面）。没有正在播放的
        /// 剪辑或帧序列缺失时什么都不做。</summary>
        internal void RefreshDisplayedFrame()
        {
            var clipId = CurrentClipId;
            if (clipId == null)
            {
                return;
            }

            if (_framesByClipId.TryGetValue(clipId.Value, out var frames) && frames.Length > 0)
            {
                var index = CurrentFrame % frames.Length;
                if (index < 0)
                {
                    index += frames.Length;
                }
                Renderer.sprite = frames[index];
            }
        }

        /// <summary>ADR-0111：经 <see cref="RegisterClipFromEffect"/> 登记了真实序列帧内容（不是
        /// <see cref="RegisterSingleFrameClip"/> 的单帧占位）之后触发，参数是被登记（或被原地覆盖）的
        /// clipId。供 <c>UnityViewFactory</c> 在战斗姿态变体剪辑的冷加载内容到位时补切（见
        /// <c>AnimClipResolver.Refresh</c>）。</summary>
        public event Action<Id>? ClipContentRegistered;

        public bool HasClip(Id clipId) => _clipMetaById.ContainsKey(clipId);

        /// <summary>ADR-0111：<paramref name="clipId"/> 已登记且内容是真实序列帧（不是单帧占位，见
        /// <see cref="RegisterSingleFrameClip"/>）。</summary>
        public bool HasRealContent(Id clipId) => _clipMetaById.ContainsKey(clipId) && !_placeholderClipIds.Contains(clipId);

        public Id? CurrentClipId => _inner?.CurrentClipId;

        public int CurrentFrame => _inner?.CurrentFrame ?? 0;

        public void Play(Id clipId, bool loop, double speed) => Inner.Play(clipId, loop, speed);

        public void Stop() => Inner.Stop();

        public SubscriptionHandle OnComplete(Action callback) => Inner.OnComplete(callback);

        public SubscriptionHandle OnAnimEvent(Action<string> callback) => Inner.OnAnimEvent(callback);

        /// <summary>ADR-0072 决策 2：转发到内部 <see cref="Presentation.Render.FrameAnimPlayer.OnFrameChanged"/>
        /// ——与 <see cref="Play"/>/<see cref="Stop"/>/<see cref="OnComplete"/>/<see cref="OnAnimEvent"/>
        /// 四个既有方法同一套"原样转发给 <see cref="Inner"/>"惯例（见类型顶部判断记录），本类型自己的
        /// <see cref="OnFrameChanged(int)"/> 私有回调（按帧号切换 <see cref="Renderer"/> 自身贴图）是
        /// 另一路独立订阅，互不影响——纸娃娃层逐层动画驱动（<c>UnityViewFactory</c>）经本方法拿到的是
        /// 同一个帧号推进时机，但落地到的是纸娃娃层各自的 <c>SpriteRenderer</c>（经
        /// <c>UnityRenderer2D.SetLayerSprite</c>），不是本组件自带的整身 <see cref="Renderer"/>。</summary>
        public SubscriptionHandle OnFrameChanged(Action<int> callback) => Inner.OnFrameChanged(callback);

        /// <summary>ADR-0072 决策 2 新增：按 <paramref name="clipId"/> 查表取出已注册的帧序列，用
        /// <paramref name="rawFrameIndex"/> 对帧数取模后返回对应 <see cref="Sprite"/>——"索引取模"
        /// 是决策 2 判断记录"共享单一时间轴，逐层各自按自己的帧数取模/夹取"里选定的具体规则（见
        /// ADR-0072 决策 2 备选方案一节"为什么是取模不是夹取"）。<paramref name="clipId"/> 未登记
        /// （该层这一状态没有解析到逐层剪辑，或还在冷启动异步加载中）或帧数为 0 时返回 <c>null</c>，
        /// 调用方据此保持该层当前已经在显示的贴图不变（不覆盖，见 <c>UnityViewFactory</c> 逐层动画
        /// 回调判断记录），不是"清空/占位方块"。</summary>
        public Sprite? GetFrame(Id clipId, int rawFrameIndex)
        {
            if (!_framesByClipId.TryGetValue(clipId, out var frames) || frames.Length == 0)
            {
                return null;
            }

            var index = rawFrameIndex % frames.Length;
            if (index < 0)
            {
                index += frames.Length;
            }
            return frames[index];
        }

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
            Advance(Time.deltaTime);
        }
    }
}
