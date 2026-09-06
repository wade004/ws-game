using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Presentation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// <see cref="ICharacterRig"/> 的 <c>sprite</c> 型实现（见 09_表现层.md 第 4.1 节）。由
    /// <see cref="SpriteViewBase"/> 在构造期持有并委托（见该类型 <c>Rig</c> 属性判断记录），也可以由
    /// 具体游戏的 <c>IView</c> 实现直接持有使用。
    /// <para>
    /// 判断记录（层管理与 <see cref="SpriteViewBase"/> 既有逻辑的分工）：<see cref="SpriteViewBase"/>
    /// 的 <c>RebuildEquippedLayers</c>（装备覆盖按槽位合并纸娃娃层）比"给定层名顺序直接合成"复杂——
    /// 需要先按已装备槽位覆盖表改写默认层顺序对应的资源 Id，再合成——这部分业务逻辑不是"任意 sprite
    /// 型角色都需要的通用机制"，本类型不代为吸收；本类型只归并两段全部 sprite 型角色都需要的通用
    /// 步骤：① <see cref="ComposeAndApplyLayers"/>（给定层名顺序 + 朝向 + 资源 Id 解析委托，直接合成
    /// 并应用，对应 09 第 3.3.1 节最朴素的情形，<see cref="SpriteViewBase.SetPaperdollLayers"/> 现完全
    /// 委托本方法）；② <see cref="ApplyLayers"/>（已经算好资源 Id 列表后的最终 <c>SetLayers</c> 落地
    /// 调用，<c>RebuildEquippedLayers</c> 算完覆盖合并后的最后一步委托本方法，避免自己再持有一份
    /// <c>Handle</c>/<c>IRenderer2D</c> 引用）。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="ProceduralAnim"/> 的 <c>Flash</c> 特殊处理）：八个原语里只有
    /// <see cref="Flash"/> 在本类型有开箱即用的落地——受击/无敌帧闪白是最高频、最需要"不额外接线也能
    /// 看到效果"的一个（09 第 4.1 节原语清单例句"flash｜闪白（受击/无敌帧反馈）"），本类型固定把它接到
    /// <see cref="IRenderer2D.SetShaderParam"/> 的 <c>"flash_intensity"</c> 参数（具体着色器如何解释
    /// 这个参数值是引擎侧美术资源的事，09 第 4.1 节"参数留白由具体引擎适配层解释"）；其余七个原语只
    /// 转发到内部 <see cref="ProceduralAnimSequencer"/>，不预置默认落地——它们的合理默认表现（位移要
    /// 叠加到哪个变换分量、缩放是否要跟纸娃娃整体缩放叠乘等）依赖具体游戏的呈现取舍，留给调用方
    /// （通常是 W3b 引擎适配层）在 <c>onSample</c> 参数里自行决定，本类型不代为拍板。
    /// </para>
    /// </summary>
    public sealed class SpriteCharacterRig : ICharacterRig, IProceduralAnim
    {
        private readonly IRenderer2D _renderer;
        private readonly SpriteHandle _handle;
        private readonly IRenderConventionHost _conventions;
        private readonly DisplayInfo _displayInfo;
        private readonly ResourceReferenceTracker? _resourceTracker;
        private readonly HitFrameSyncStrategy _hitFrameSyncStrategy;
        private IFrameAnimPlayer? _frameAnimPlayer;
        private readonly ProceduralAnimSequencer _sequencer = new ProceduralAnimSequencer();

        /// <summary>命中帧同步（09 第 4.3 节）在 <see cref="HitFrameSyncStrategy.AnimKeyframeDriven"/>
        /// 策略下才触发：<see cref="FrameAnimClip.HitFrameMarker"/> 对应的 <see cref="IFrameAnimPlayer.OnAnimEvent"/>
        /// 到达时以本实体 id 为参数触发一次，供命中反馈的落地代码（通常是
        /// <c>Presentation.FeedbackBinder</c> 一侧的接线）订阅后延迟到这一刻才真正播放命中特效/音效/
        /// 飘字，而不是在逻辑事件广播的那一刻立即播放（后者是 <see cref="HitFrameSyncStrategy.LogicDriven"/>
        /// 策略的既有默认行为，不需要本事件参与——FeedbackBinder 本就在收到 <c>combat.damage_dealt</c>
        /// 时立即派发，见该模块）。<see cref="HitFrameSyncStrategy.LogicDriven"/> 策略下（默认）或未
        /// 注入 <see cref="IFrameAnimPlayer"/> 时恒不触发。</summary>
        public event Action<Id>? HitFrameReached;

        public Id EntityId { get; private set; }

        public AnimState CurrentAnimState { get; private set; } = AnimState.Idle;

        public IProceduralAnim ProceduralAnim => this;

        /// <summary>供 <see cref="SpriteViewBase.Bind"/> 在拿到实体 id 后回填（<see cref="SpriteViewBase"/>
        /// 构造期尚不知道自己会绑定给哪个实体——见 09 第 2 节 View 绑定协议"调用方仍需在拿到 View 后
        /// 显式调用一次 bind"）。不在 <see cref="ICharacterRig"/> 接口上暴露——绑定时机是
        /// <see cref="SpriteViewBase"/> 自己的生命周期细节，不是 rig 抽象本该关心的通用能力。</summary>
        public void Bind(Id entityId) => EntityId = entityId;

        public SpriteCharacterRig(
            Id entityId,
            IRenderer2D renderer,
            SpriteHandle handle,
            IRenderConventionHost conventions,
            DisplayInfo displayInfo,
            ResourceReferenceTracker? resourceTracker = null,
            IFrameAnimPlayer? frameAnimPlayer = null,
            RenderOptions? options = null)
        {
            EntityId = entityId;
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _handle = handle;
            _conventions = conventions ?? throw new ArgumentNullException(nameof(conventions));
            _displayInfo = displayInfo ?? throw new ArgumentNullException(nameof(displayInfo));

            if (_displayInfo.Kind != DisplayKind.Sprite || _displayInfo.Sprite == null)
            {
                throw new ArgumentException(
                    $"SpriteCharacterRig 只支持 kind=sprite 的 DisplayInfo，实际 kind={_displayInfo.Kind}", nameof(displayInfo));
            }

            _resourceTracker = resourceTracker;
            _hitFrameSyncStrategy = (options ?? new RenderOptions()).HitFrameSync;

            if (frameAnimPlayer != null)
            {
                AttachFrameAnimPlayer(frameAnimPlayer);
            }
        }

        /// <summary>
        /// 判断记录（W3b 判断记录 2 收口——<see cref="PlayClip"/> 结构性 no-op 根治）：本方法允许在
        /// 构造之后再把一个 <see cref="IFrameAnimPlayer"/> 实现补给本 rig（构造函数的同名参数只是这条
        /// 通路的"构造期已经拿得到实现"特例，两者共用同一套接线逻辑，见构造函数末尾）。存在这条
        /// 补接口的必要性：具体引擎侧的 <see cref="IFrameAnimPlayer"/> 实现常常是"挂在精灵根节点上的
        /// 组件"（如 <c>Adapter.Unity.Presentation.UnityFrameAnimPlayer</c>），而精灵根节点本身要等
        /// <see cref="SpriteViewBase"/> 构造完（<see cref="IRenderer2D.CreateSpriteInstance"/> 已经
        /// 调用过、<see cref="SpriteHandle"/> 已经产生）之后，引擎适配层的装配代码才能定位到它并挂上
        /// 组件——这一步天然晚于本 rig 的构造。<see cref="SpriteViewBase.AttachFrameAnimPlayer"/>
        /// 转发到本方法，供那类装配代码补接线；命中帧同步（<see cref="HitFrameSyncStrategy.AnimKeyframeDriven"/>
        /// 策略）的订阅逻辑与构造期分支共用，晚接线不影响该策略生效。同一 rig 生命周期内只应调用一次
        /// ——重复调用会叠加订阅 <see cref="IFrameAnimPlayer.OnAnimEvent"/>，本方法不做去重防护
        /// （调用方自行保证至多附加一次，同 09 第 4.5 节"仅 sprite 型外形"一个 rig 对应一个具体外形
        /// 实例的既有假设）。
        /// </summary>
        public void AttachFrameAnimPlayer(IFrameAnimPlayer player)
        {
            _frameAnimPlayer = player ?? throw new ArgumentNullException(nameof(player));

            if (_hitFrameSyncStrategy == HitFrameSyncStrategy.AnimKeyframeDriven)
            {
                _frameAnimPlayer.OnAnimEvent(marker =>
                {
                    if (marker == FrameAnimClip.HitFrameMarker)
                    {
                        HitFrameReached?.Invoke(EntityId);
                    }
                });
            }
        }

        public void SetAnimState(AnimState state) => CurrentAnimState = state;

        public void PlayClip(Id clipId, bool loop = false, double speed = 1.0) => _frameAnimPlayer?.Play(clipId, loop, speed);

        public Vec2? ResolveAnchorLocalOffset(Id anchorId, Direction facing)
        {
            var spriteInfo = _displayInfo.Sprite!;
            if (!spriteInfo.AnchorPoints.TryGetValue(BareName(anchorId), out var anchorDef))
            {
                return null;
            }

            var (slotId, flipX) = _conventions.ResolveDirectionSlot(facing, spriteInfo);
            // GP-PRES-07 收口：同 ViewBinder.GetAnchorWorldPosition 判断记录——按方向槽位取
            // offset_by_direction 覆盖值，镜像仍是独立于方向覆盖的第二步。
            var baseOffset = anchorDef.ResolveOffset(slotId);
            return flipX ? new Vec2(-baseOffset.X, baseOffset.Y) : baseOffset;
        }

        public void ComposeAndApplyLayers(
            IReadOnlyList<string> layerNamesInOrder, Direction facing, Func<SpriteLayerPlacement, Id> resolveResourceId)
        {
            if (layerNamesInOrder == null) throw new ArgumentNullException(nameof(layerNamesInOrder));
            if (resolveResourceId == null) throw new ArgumentNullException(nameof(resolveResourceId));

            var placements = _conventions.ComposeSpriteLayers(layerNamesInOrder, _displayInfo.Sprite!, facing);
            var resourceIds = new List<Id>(placements.Count);
            for (var i = 0; i < placements.Count; i++)
            {
                var resourceId = resolveResourceId(placements[i]);
                _resourceTracker?.EnsureLoading(resourceId, ResourceKind.Image);
                resourceIds.Add(resourceId);
            }

            ApplyLayers(resourceIds);
        }

        public void ApplyLayers(IReadOnlyList<Id> resourceIds) => _renderer.SetLayers(_handle, resourceIds);

        public void Update(double dt) => _sequencer.Update(dt);

        // --------------------------------------------------------------
        // IProceduralAnim：除 Flash 外全部直接转发内部 ProceduralAnimSequencer（见类型注释判断记录）。
        // --------------------------------------------------------------

        public void Move(MoveParams parameters, Action<Vec2>? onSample = null, Action? onComplete = null) =>
            _sequencer.Move(parameters, onSample, onComplete);

        public void Rotate(RotateParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            _sequencer.Rotate(parameters, onSample, onComplete);

        public void Scale(ScaleParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            _sequencer.Scale(parameters, onSample, onComplete);

        /// <summary>见类型注释"Flash 特殊处理"判断记录：固定接到
        /// <c>IRenderer2D.SetShaderParam(handle, "flash_intensity", value)</c>，同时把采样值转发给
        /// 调用方可选传入的 <paramref name="onSample"/>（两者都会被调用，互不排斥）。</summary>
        public void Flash(FlashParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            _sequencer.Flash(parameters, value =>
            {
                _renderer.SetShaderParam(_handle, "flash_intensity", value);
                onSample?.Invoke(value);
            }, onComplete);

        public void Trail(TrailParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            _sequencer.Trail(parameters, onSample, onComplete);

        public void Stagger(StaggerParams parameters, Action<Vec2>? onSample = null, Action? onComplete = null) =>
            _sequencer.Stagger(parameters, onSample, onComplete);

        public void Topple(ToppleParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            _sequencer.Topple(parameters, onSample, onComplete);

        public void Fade(FadeParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            _sequencer.Fade(parameters, onSample, onComplete);

        /// <summary>还原带域前缀的锚点 Id（如 <c>"anchor.hand_main"</c>）为 <c>anchor_points</c> 字典
        /// 使用的裸键名，惯例同 <c>Presentation.ViewBinding.ViewBinder.BareAnchorName</c>。</summary>
        private static string BareName(Id id)
        {
            var value = id.Value;
            var dotIndex = value.LastIndexOf('.');
            return dotIndex < 0 ? value : value.Substring(dotIndex + 1);
        }
    }

    /// <summary>
    /// <see cref="ICharacterRig"/> 的 <c>model</c> 型占位实现（见任务书"model 型 rig 只留接口与
    /// NotSupported 说明"）：sprite 专属能力（层合成、锚点查询）抛 <see cref="NotSupportedException"/>，
    /// 提示改走 09 第 3.3.2 节的槽位/挂点机制（经 <c>IRenderer3D.setSlotMesh</c>/<c>attachToSocket</c>，
    /// 见 02 第 1.12 节）——那一整套挂点/槽位应用逻辑依赖具体 <c>IRenderer3D</c> 实现细节，不属于本轮
    /// （W3a，表现层引擎无关部分）落地范围，真正的 model 型 <c>CharacterRig</c> 留给接入 3D 外形的具体
    /// 游戏在 W3b 阶段提供（09"基础架构提供/游戏层提供"表"CharacterRig 与程序动画原语的行为定义"一行：
    /// 基础架构只定义行为，不代为实现每种外形类型）。<see cref="CurrentAnimState"/>/<see cref="ProceduralAnim"/>
    /// /<see cref="PlayClip"/>/<see cref="SetAnimState"/> 四项与外形类型无关的职责仍然给出可用实现
    /// （程序动画原语"两种外形类型均适用"，见 09 第 4.1 节原语清单表头）。
    /// </summary>
    public sealed class ModelCharacterRig : ICharacterRig
    {
        private readonly ProceduralAnimSequencer _sequencer = new ProceduralAnimSequencer();
        private readonly IFrameAnimPlayer? _frameAnimPlayer;

        public Id EntityId { get; }

        public AnimState CurrentAnimState { get; private set; } = AnimState.Idle;

        public IProceduralAnim ProceduralAnim => _sequencer;

        public ModelCharacterRig(Id entityId, IFrameAnimPlayer? frameAnimPlayer = null)
        {
            EntityId = entityId;
            _frameAnimPlayer = frameAnimPlayer;
        }

        public void SetAnimState(AnimState state) => CurrentAnimState = state;

        /// <summary>model 型剪辑播放走 <c>IRenderer3D.playAnim</c>（见 09 第 4.1 节职责表），不经
        /// <see cref="IFrameAnimPlayer"/>（那是 sprite 型专属，见 09 第 4.5 节"仅 sprite 型外形"）；
        /// 本方法转发到构造期可选注入的 <see cref="IFrameAnimPlayer"/> 仅为保持接口一致可调用，未注入
        /// 时静默跳过——真正 model 型剪辑播放的接线属 W3b。</summary>
        public void PlayClip(Id clipId, bool loop = false, double speed = 1.0) => _frameAnimPlayer?.Play(clipId, loop, speed);

        public Vec2? ResolveAnchorLocalOffset(Id anchorId, Direction facing) =>
            throw new NotSupportedException(
                "model 型外形没有 sprite 锚点，挂点查询请走 IRenderer3D 的槽位/挂点机制（见 09 第 3.3.2 节）");

        public void ComposeAndApplyLayers(
            IReadOnlyList<string> layerNamesInOrder, Direction facing, Func<SpriteLayerPlacement, Id> resolveResourceId) =>
            throw new NotSupportedException(
                "model 型外形没有纸娃娃层，换装请走 display.equip_visual 的槽位网格/挂点机制（见 09 第 3.3.2 节）");

        public void ApplyLayers(IReadOnlyList<Id> resourceIds) =>
            throw new NotSupportedException(
                "model 型外形没有纸娃娃层，换装请走 display.equip_visual 的槽位网格/挂点机制（见 09 第 3.3.2 节）");

        public void Update(double dt) => _sequencer.Update(dt);
    }
}
