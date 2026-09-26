using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Presentation.Common;
using Presentation.VfxSfx.Contracts;

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
    /// <para>
    /// 判断记录（资源加载完成后回填已渲染层，排查复盘-2026-09-19-PlayMode-全局缓存清理反例.md
    /// "教训三"根治）：此前 <see cref="ComposeAndApplyLayers"/> 只在调用当下同步把
    /// <c>resolveResourceId</c> 解出的资源 id 交给 <see cref="ApplyLayers"/>（→
    /// <see cref="IRenderer2D.SetLayers"/>），首次引用的资源尚未加载完成时引擎侧只能落地占位方块
    /// （<c>IRenderer2D</c> 实现的既有约定），但从此再没有任何机制在加载真正完成之后回头刷新画面——
    /// 除非"层集合变化"或"朝向变化"再次触发一次 <see cref="SetLayers"/>调用；隔离跑 PlayMode 用例
    /// 实测（<c>诊断记录 diag-isolation.md</c>）证实这不是偶发竞态，是结构性缺口："资源确实异步
    /// 加载成功了，但没有人通知渲染层"。现给 <see cref="Presentation.Common.ResourceReferenceTracker.EnsureLoading(Core.Foundation.Common.Id,ResourceKind,LoadCallback?)"/>
    /// 新增的 <c>onComplete</c> 回调统一接到 <see cref="HandleResourceLoadCompleted"/>：加载成功时用
    /// <see cref="_lastLayerResourceIds"/>（<see cref="ApplyLayers"/> 记住的"当前完整层列表"）重新调
    /// 一次 <see cref="ApplyLayers"/>，让占位方块有机会被迟到的真实资源替换；加载失败时不重新应用
    /// （占位方块保持不变），改记一条 <see cref="Diagnostics"/> 诊断，不静默。幂等与防重复刷新风暴：
    /// ① <see cref="Presentation.Common.ResourceReferenceTracker"/> 本身保证同一资源 id 只调用一次
    /// <c>LoadAsync</c>，因此本方法对给定 id 至多被调用一次，不会重复回填；② 多个层引用同一资源 id
    /// 只会有这一次回调，但它触发的是"整份当前层列表"重新应用一次，天然覆盖全部引用该资源的层，
    /// 不需要按层分别处理，也不需要额外去重表；③ <see cref="MarkDestroyed"/> 之后到达的回调直接
    /// 跳过，不触碰已销毁的 <see cref="SpriteHandle"/>（<c>IRenderer2D</c> 实现对已销毁句柄一律抛
    /// <see cref="InvalidOperationException"/>，见 <c>UnityRenderer2D.EnsureAlive</c>/
    /// <c>StubRenderer2D.EnsureSpriteAlive</c> 同一约定）；重新应用本身开销可忽略——
    /// <see cref="IRenderer2D.SetLayers"/> 按索引复用既有 <c>SpriteRenderer</c>，只重新解析/赋值
    /// <c>sprite</c> 字段，不销毁重建任何引擎对象（见 <c>UnityRenderer2D.SetLayers</c> 判断记录）。
    /// </para>
    /// <para>
    /// 判断记录（复用 <see cref="IPresentationDiagnostics"/>，不新造 render 专属诊断契约）：本类型
    /// 与 <c>presentation/render</c> 同属 <c>Presentation.Common</c> 单一程序集（见 presentation/
    /// README.md），<c>vfx_sfx</c>/<c>feedback_binder</c> 已用同一接口记降级/异步加载相关诊断
    /// （<c>VfxPlayer</c>/<c>SfxPlayer</c>），本类型的"资源加载失败"是同一类"表现层运行期降级"场景，
    /// 复用同一契约不新增无谓抽象（同 common/README.md 判断记录 1"不新造 IRenderSurface"一贯做法）；
    /// 该接口类型注释的适用范围已一并扩展到本模块。未走 <c>VfxPlayer</c>/<c>SfxPlayer</c> 的构造期
    /// 注入模式——本类型/<see cref="SpriteViewBase"/> 现有构造函数均不允许加参数（AGENTS.md"ABI 只
    /// 新增"），内部固定 <c>new PresentationDiagnosticsRecorder()</c>，经 <see cref="Diagnostics"/>
    /// 只读属性暴露供调用方/测试读取，是比新增构造重载更小的改动面。
    /// </para>
    /// </summary>
    public sealed class SpriteCharacterRig : ICharacterRig, IHitFrameEmitter, IProceduralAnim
    {
        private readonly IRenderer2D _renderer;
        private readonly SpriteHandle _handle;
        private readonly IRenderConventionHost _conventions;
        private readonly DisplayInfo _displayInfo;
        private readonly ResourceReferenceTracker? _resourceTracker;
        private readonly HitFrameSyncStrategy _hitFrameSyncStrategy;
        private IFrameAnimPlayer? _frameAnimPlayer;
        private readonly ProceduralAnimSequencer _sequencer = new ProceduralAnimSequencer();

        /// <summary>见类型注释"资源加载完成后回填已渲染层"判断记录：<see cref="ApplyLayers"/> 每次
        /// 调用都会记住当次的完整层 resourceId 列表，供 <see cref="HandleResourceLoadCompleted"/>
        /// 在迟到的加载完成回调到达时重新应用同一份列表——始终反映"最近一次已知的期望层集合"，即使
        /// 期间因装备变化等原因已经变了也会自动跟上最新值（不是"请求发起时刻"的旧快照）。</summary>
        private IReadOnlyList<Id>? _lastLayerResourceIds;

        /// <summary>见类型注释同一判断记录：<see cref="MarkDestroyed"/> 之后 <see cref="HandleResourceLoadCompleted"/>
        /// 直接跳过，不触碰已销毁的渲染实例。</summary>
        private bool _destroyed;

        /// <summary>见类型注释"复用 IPresentationDiagnostics"判断记录：加载失败时记一条警告，供调用方
        /// /测试读取（未走构造期注入，固定用内存实现）。</summary>
        public IPresentationDiagnostics Diagnostics { get; } = new PresentationDiagnosticsRecorder();

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
                _resourceTracker?.EnsureLoading(resourceId, ResourceKind.Image, HandleResourceLoadCompleted);
                resourceIds.Add(resourceId);
            }

            ApplyLayers(resourceIds);
        }

        public void ApplyLayers(IReadOnlyList<Id> resourceIds)
        {
            _lastLayerResourceIds = resourceIds;
            _renderer.SetLayers(_handle, resourceIds);
            LayersApplied?.Invoke(resourceIds);
        }

        /// <summary>
        /// ADR-0099 决策 2 收口（消费方反馈第四十四批，已知限制 1 根治）：本类型对渲染器的写入只有
        /// <see cref="ApplyLayers"/> 这一处（<see cref="ComposeAndApplyLayers"/> 与
        /// <see cref="HandleResourceLoadCompleted"/> 的迟到回填都委托本方法），本事件在写入之后立即
        /// 触发一次，因此两条路径天然共用同一个通知出口，不需要分别接线。<see cref="SpriteViewBase"/>
        /// 在构造期订阅本事件转发到自己的 <see cref="SpriteViewBase.OnLayersComposed"/>（见该方法判断
        /// 记录），使"首次引用的纸娃娃层资源异步加载完成"这条此前遗漏的路径与"方向槽位变化"/"装备
        /// 变化"两条既有路径待遇一致——逐层动画当前帧不必再等下一次 <c>OnFrameChanged</c> 才纠正回来。
        /// </summary>
        public event Action<IReadOnlyList<Id>>? LayersApplied;

        /// <summary>见类型注释"资源加载完成后回填已渲染层"判断记录：作为
        /// <see cref="Presentation.Common.ResourceReferenceTracker.EnsureLoading(Id,ResourceKind,LoadCallback?)"/>
        /// 的 <c>onComplete</c> 回调，绑定到本 rig 实例（不是绑定到某一次具体调用），因此同一资源 id
        /// 无论由 <see cref="ComposeAndApplyLayers"/> 还是 <see cref="SpriteViewBase.RebuildEquippedLayers"/>
        /// 首次发起加载，回调到达时都会走到同一份逻辑。公开（而非 <c>private</c>）供
        /// <see cref="SpriteViewBase"/> 把它作为 <c>LoadCallback</c> 传给自己发起的
        /// <c>EnsureLoading</c> 调用（见该类型 <c>RebuildEquippedLayers</c>）。</summary>
        public void HandleResourceLoadCompleted(Id resourceId, bool success)
        {
            if (_destroyed)
            {
                return;
            }

            if (!success)
            {
                Diagnostics.Warn($"SpriteCharacterRig（entity={EntityId}）：资源 \"{resourceId}\" 加载失败，保留占位方块");
                return;
            }

            if (_lastLayerResourceIds != null)
            {
                ApplyLayers(_lastLayerResourceIds);
            }
        }

        /// <summary>见类型注释同一判断记录：由 <see cref="SpriteViewBase.Destroy"/> 在销毁渲染实例前
        /// 调用，标记本 rig 此后不再响应迟到的 <see cref="HandleResourceLoadCompleted"/> 回调。</summary>
        public void MarkDestroyed() => _destroyed = true;

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
}
