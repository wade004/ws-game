using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Presentation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// <see cref="ICharacterRig"/> 的 <c>model</c> 型真实实现（ADR-0017 决策 b：model 型外形的默认
    /// 路线是框架职责——本类型取代此前"sprite 专属能力抛 <see cref="NotSupportedException"/>"的占位
    /// 实现，持有 <see cref="IRenderer3D"/> 与构造期注入的 <see cref="ModelHandle"/>，把 09 第 4.1 节
    /// CharacterRig 四项职责（层/槽位管理、锚点/挂点查询、动画状态机驱动、程序动画原语调用）落到
    /// <see cref="IRenderer3D"/> 具体接口上，与 <see cref="SpriteCharacterRig"/> 是同一职责的两套外形
    /// 类型实现（本类型是引擎无关的——真正的三维渲染/骨骼动画由构造期注入的 <see cref="IRenderer3D"/>
    /// 实现承担，见 02 第 1.12 节；引擎适配层是否提供该实现是 ADR-0017 决策 b 的另一半，不属于本类型
    /// 范围）。
    /// <para>
    /// 判断记录（<see cref="ModelHandle"/> 由构造期注入而不是本类型自行创建）：与
    /// <see cref="SpriteCharacterRig"/> 持有构造期已创建好的 <see cref="Presentation.Common.SpriteHandle"/>
    /// 同一惯例——模型实例的创建时机（<c>IRenderer3D.CreateModelInstance</c>）与生命周期归属通常在
    /// View/ViewFactory 装配代码那一层决定（该层还要决定是否需要额外的资源加载等待，见
    /// <c>SpriteViewBase</c> 构造流程），不属于"把逻辑单位画成可动角色"这一项职责本身；本类型额外实现
    /// <see cref="IModelHandleProvider"/>，把构造期拿到的这个句柄重新暴露出去，供
    /// <c>Presentation.VfxSfx.Core.VfxPlayer</c> 的 <c>attach_mode: socket</c> 挂接使用（09 第 3.3.2
    /// 节），一并满足任务书"由 IModelHandleProvider 或构造注入"两种表述。
    /// </para>
    /// <para>
    /// 判断记录（八原语到 <c>SetPlacement</c>/<c>SetMaterialParam</c> 的映射）：<see cref="IRenderer3D.SetPlacement"/>
    /// 一次调用携带全部放置参数（平面坐标、高度、朝向、缩放、sortY），不像 sprite 型有独立的
    /// <c>SetShaderParam</c> 通道可以只改一个数值；本类型因此缓存"最近一次由 <see cref="SyncPlacement"/>
    /// 写入的基准姿态"，move/rotate/scale/stagger/topple 五个与位置/朝向/缩放相关的原语各自在基准姿态
    /// 上叠加一份偏移量（跨类型简单相加，同类型互相替换，见 <see cref="ProceduralAnimSequencer"/> 类型
    /// 注释"叠加/互斥规则"判断记录——本类型据此拍板"消费方自行决定如何合成多个偏移量"的合成方式为加法），
    /// 每次任一叠加量变化都重新调用一次 <see cref="IRenderer3D.SetPlacement"/>；flash/trail/fade 三个
    /// 与位置无关的原语直接对应到 <see cref="IRenderer3D.SetMaterialParam"/> 的三个固定参数名
    /// （<c>flash_intensity</c>/<c>trail_intensity</c>/<c>fade_alpha</c>，具体着色器如何解释这三个参数
    /// 值是引擎侧美术资源的事，09 第 4.1 节"参数留白由具体引擎适配层解释"），与
    /// <see cref="SpriteCharacterRig"/> 把 <see cref="Flash"/> 接到 <c>flash_intensity</c> 同一惯例的
    /// 自然扩展。<see cref="SyncPlacement"/> 调用前（尚无基准姿态）任何原语的采样不产生
    /// <see cref="IRenderer3D.SetPlacement"/> 调用——没有基准姿态无法合成出一个有意义的绝对放置参数，
    /// 这是已知简化，等价于"View 尚未同步过第一次姿态之前不渲染放置变化"。
    /// </para>
    /// </summary>
    public sealed class ModelCharacterRig : ICharacterRig, IProceduralAnim, IModelHandleProvider, IDisposable
    {
        /// <summary>命中帧事件 id（09 第 4.3 节 <c>anim_keyframe_driven</c> 策略 model 型一侧：经
        /// <see cref="IRenderer3D.OnAnimEvent"/> 触发）。<c>display.anim_set.clips[*].events</c>（04
        /// 第 7.1.1 节）登记的事件 <c>name</c> 是不要求点分格式的裸字符串（如 <c>"hit_frame"</c>，见
        /// <see cref="Core.Foundation.DisplayInfo.AnimClipEventSpec.Name"/>），而 <see cref="AnimEventCallback"/>
        /// 的 <c>eventId</c> 参数要求 <see cref="Id"/> 点分格式；具体 <see cref="IRenderer3D"/> 实现把
        /// 剪辑关键帧登记进自己的动画事件系统时，命中帧一律映射到本常量（真正的登记/映射逻辑属于
        /// W6-B 引擎适配层，见落地计划"W6-B 接入清单"）。</summary>
        public static readonly Id HitFrameEventId = new Id("anim_event.hit_frame");

        /// <summary><see cref="PlayClip"/> 未提供混合时长参数（<see cref="ICharacterRig.PlayClip"/>
        /// 固定签名只有 clipId/loop/speed，见该接口方法判断记录"解析工作留给调用方"），本类型拍板一个
        /// 保守的默认混合时长，不产生生硬的瞬切；需要按剪辑/状态定制混合时长的调用方应改为直接调用
        /// <see cref="IRenderer3D.PlayAnim"/>，不经本方法。</summary>
        public const double DefaultBlendSeconds = 0.15;

        private readonly IRenderer3D _renderer;
        private readonly ModelHandle _handle;
        private readonly DisplayInfo _displayInfo;
        private readonly HitFrameSyncStrategy _hitFrameSyncStrategy;
        private readonly ProceduralAnimSequencer _sequencer = new ProceduralAnimSequencer();
        private readonly Dictionary<Id, ModelHandle> _socketAttachments = new Dictionary<Id, ModelHandle>();
        private readonly SubscriptionHandle? _animEventSubscription;

        // 最近一次由 SyncPlacement 写入的基准姿态；八原语的位置/朝向/缩放叠加量在此基础上合成。
        private bool _hasBasePlacement;
        private Vec2 _basePlanePos;
        private double _baseHeight;
        private double _baseFacing;
        private double _baseScale = 1.0;
        private double _baseSortY;

        // 叠加态（见类型注释"八原语映射"判断记录）。
        private Vec2 _moveOffset = Vec2.Zero;
        private Vec2 _staggerOffset = Vec2.Zero;
        private double _rotateOffset;
        private double _toppleOffset;
        private double _scaleMultiplier = 1.0;

        public event Action<Id>? HitFrameReached;

        public Id EntityId { get; }

        public AnimState CurrentAnimState { get; private set; } = AnimState.Idle;

        public IProceduralAnim ProceduralAnim => this;

        public ModelCharacterRig(
            Id entityId,
            IRenderer3D renderer,
            ModelHandle handle,
            DisplayInfo displayInfo,
            RenderOptions? options = null)
        {
            EntityId = entityId;
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _handle = handle;
            _displayInfo = displayInfo ?? throw new ArgumentNullException(nameof(displayInfo));

            if (_displayInfo.Kind != DisplayKind.Model || _displayInfo.Model == null)
            {
                throw new ArgumentException(
                    $"ModelCharacterRig 只支持 kind=model 的 DisplayInfo，实际 kind={_displayInfo.Kind}", nameof(displayInfo));
            }

            _hitFrameSyncStrategy = (options ?? new RenderOptions()).HitFrameSync;

            if (_hitFrameSyncStrategy == HitFrameSyncStrategy.AnimKeyframeDriven)
            {
                _animEventSubscription = _renderer.OnAnimEvent(_handle, OnRendererAnimEvent);
            }
        }

        private void OnRendererAnimEvent(ModelHandle handle, Id eventId)
        {
            if (eventId == HitFrameEventId)
            {
                HitFrameReached?.Invoke(EntityId);
            }
        }

        public void SetAnimState(AnimState state) => CurrentAnimState = state;

        public void PlayClip(Id clipId, bool loop = false, double speed = 1.0) =>
            _renderer.PlayAnim(_handle, clipId, loop, speed, DefaultBlendSeconds);

        /// <summary>见类型注释判断记录：model 型没有可返回精确偏移的挂点数据，仅确认
        /// <paramref name="anchorId"/>（去掉域前缀后的裸名字，如 <c>"anchor.hand_main"</c> → 查
        /// <c>"hand_main"</c>）是否在 <c>ModelInfo.Sockets</c> 声明——声明存在返回
        /// <see cref="Vec2.Zero"/>，否则返回 null。<paramref name="facing"/> 对 model 型没有镜像换算
        /// 意义（挂点朝向由骨骼系统决定），本方法忽略该参数。</summary>
        public Vec2? ResolveAnchorLocalOffset(Id anchorId, Direction facing)
        {
            var bareName = BareName(anchorId);
            var sockets = _displayInfo.Model!.Sockets;
            for (var i = 0; i < sockets.Count; i++)
            {
                if (string.Equals(BareName(sockets[i]), bareName, StringComparison.Ordinal))
                {
                    return Vec2.Zero;
                }
            }
            return null;
        }

        /// <summary>见 <see cref="ICharacterRig.ComposeAndApplyLayers"/> 判断记录：model 型不适用纸娃娃
        /// 层顺序合成，no-op。</summary>
        public void ComposeAndApplyLayers(
            IReadOnlyList<string> layerNamesInOrder, Direction facing, Func<SpriteLayerPlacement, Id> resolveResourceId)
        {
            // 见接口判断记录：model 型没有纸娃娃层，装备外观改走 ApplyEquipVisual，本方法保持 no-op。
        }

        /// <summary>见 <see cref="ICharacterRig.ApplyLayers"/> 判断记录：no-op。</summary>
        public void ApplyLayers(IReadOnlyList<Id> resourceIds)
        {
        }

        /// <summary>应用一条 <c>display.equip_visual</c> 记录（04 第 7.1.2 节、09 第 3.3.2 节）：
        /// <see cref="EquipVisualMode.SlotMesh"/> 经 <see cref="IRenderer3D.SetSlotMesh"/> 替换槽位
        /// 网格；<see cref="EquipVisualMode.SocketAttach"/> 创建一个新的子模型实例并经
        /// <see cref="IRenderer3D.AttachToSocket"/> 挂接到挂点（同一挂点重复应用时先自动
        /// <see cref="ClearSocket"/> 卸下旧的子模型实例，不产生重叠挂接与句柄泄漏）。<paramref name="def"/>
        /// 缺少该模式必需的字段（<c>slot_id</c>/<c>mesh_ref</c> 或 <c>socket_id</c>/<c>model_ref</c>）
        /// 时 no-op，不抛异常（同 09 第 1 节表现层"缺表现资源不阻断游戏"一贯宽容策略）。</summary>
        public void ApplyEquipVisual(EquipVisualDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));

            switch (def.Mode)
            {
                case EquipVisualMode.SlotMesh:
                    if (def.SlotId == null)
                    {
                        return;
                    }
                    _renderer.SetSlotMesh(_handle, def.SlotId.Value, def.MeshRef);
                    return;

                case EquipVisualMode.SocketAttach:
                    if (def.SocketId == null || def.ModelRef == null)
                    {
                        return;
                    }
                    ClearSocket(def.SocketId.Value);
                    var child = _renderer.CreateModelInstance(def.ModelRef.Value);
                    _renderer.AttachToSocket(_handle, def.SocketId.Value, child);
                    _socketAttachments[def.SocketId.Value] = child;
                    return;
            }
        }

        /// <summary>卸下 <paramref name="slotId"/> 槽位的网格（<c>meshId: null</c>，见
        /// <see cref="IRenderer3D.SetSlotMesh"/> 语义）。</summary>
        public void ClearSlot(Id slotId) => _renderer.SetSlotMesh(_handle, slotId, null);

        /// <summary>卸下并销毁 <paramref name="socketId"/> 挂点当前挂接的子模型实例（若有，见
        /// <see cref="ApplyEquipVisual"/> 判断记录）；该挂点当前无挂接时 no-op。</summary>
        public void ClearSocket(Id socketId)
        {
            if (_socketAttachments.TryGetValue(socketId, out var child))
            {
                _renderer.Detach(child);
                _renderer.DestroyModelInstance(child);
                _socketAttachments.Remove(socketId);
            }
        }

        public ModelHandle? TryGetModelHandle() => _handle;

        /// <summary>由（W6-B 提供的）model 型 View 的 <c>SyncPose</c> 落地代码每帧调用，写入本次的
        /// 基准姿态并叠加当前生效的八原语偏移量后转发一次 <see cref="IRenderer3D.SetPlacement"/>（见
        /// 类型注释"八原语映射"判断记录）。<paramref name="height"/>/<paramref name="sortY"/> 与
        /// <see cref="IRenderer3D.SetPlacement"/> 同名参数语义一致，不经任何原语叠加（八项原语清单
        /// 均未声明作用于这两个轴，见 09 第 4.1 节）。</summary>
        public void SyncPlacement(Vec2 planePos, double height, double facing, double scale, double sortY)
        {
            _hasBasePlacement = true;
            _basePlanePos = planePos;
            _baseHeight = height;
            _baseFacing = facing;
            _baseScale = scale;
            _baseSortY = sortY;
            ApplyComposedPlacement();
        }

        public void Update(double dt) => _sequencer.Update(dt);

        public void Dispose()
        {
            _animEventSubscription?.Dispose();
            foreach (var child in _socketAttachments.Values)
            {
                _renderer.Detach(child);
                _renderer.DestroyModelInstance(child);
            }
            _socketAttachments.Clear();
        }

        // --------------------------------------------------------------
        // IProceduralAnim
        // --------------------------------------------------------------

        public void Move(MoveParams parameters, Action<Vec2>? onSample = null, Action? onComplete = null) =>
            _sequencer.Move(parameters, offset =>
            {
                _moveOffset = offset;
                ApplyComposedPlacement();
                onSample?.Invoke(offset);
            }, onComplete);

        public void Rotate(RotateParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            _sequencer.Rotate(parameters, delta =>
            {
                _rotateOffset = delta;
                ApplyComposedPlacement();
                onSample?.Invoke(delta);
            }, onComplete);

        public void Scale(ScaleParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            _sequencer.Scale(parameters, multiplier =>
            {
                _scaleMultiplier = multiplier;
                ApplyComposedPlacement();
                onSample?.Invoke(multiplier);
            }, onComplete);

        /// <summary>见类型注释"八原语映射"判断记录：固定接到
        /// <c>IRenderer3D.SetMaterialParam(handle, "flash_intensity", value)</c>。</summary>
        public void Flash(FlashParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            _sequencer.Flash(parameters, value =>
            {
                _renderer.SetMaterialParam(_handle, "flash_intensity", value);
                onSample?.Invoke(value);
            }, onComplete);

        /// <summary>见类型注释判断记录：固定接到
        /// <c>IRenderer3D.SetMaterialParam(handle, "trail_intensity", value)</c>。</summary>
        public void Trail(TrailParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            _sequencer.Trail(parameters, value =>
            {
                _renderer.SetMaterialParam(_handle, "trail_intensity", value);
                onSample?.Invoke(value);
            }, onComplete);

        public void Stagger(StaggerParams parameters, Action<Vec2>? onSample = null, Action? onComplete = null) =>
            _sequencer.Stagger(parameters, offset =>
            {
                _staggerOffset = offset;
                ApplyComposedPlacement();
                onSample?.Invoke(offset);
            }, onComplete);

        public void Topple(ToppleParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            _sequencer.Topple(parameters, delta =>
            {
                _toppleOffset = delta;
                ApplyComposedPlacement();
                onSample?.Invoke(delta);
            }, onComplete);

        /// <summary>见类型注释判断记录：固定接到
        /// <c>IRenderer3D.SetMaterialParam(handle, "fade_alpha", value)</c>。</summary>
        public void Fade(FadeParams parameters, Action<double>? onSample = null, Action? onComplete = null) =>
            _sequencer.Fade(parameters, value =>
            {
                _renderer.SetMaterialParam(_handle, "fade_alpha", value);
                onSample?.Invoke(value);
            }, onComplete);

        // --------------------------------------------------------------

        private void ApplyComposedPlacement()
        {
            if (!_hasBasePlacement)
            {
                return;
            }

            var pos = _basePlanePos + _moveOffset + _staggerOffset;
            var facing = _baseFacing + _rotateOffset + _toppleOffset;
            var scale = _baseScale * _scaleMultiplier;
            _renderer.SetPlacement(_handle, pos, _baseHeight, facing, scale, _baseSortY);
        }

        /// <summary>还原带域前缀的 Id（如 <c>"anchor.hand_main"</c>/<c>"socket.hand_main"</c>）为裸名字，
        /// 惯例同 <see cref="SpriteCharacterRig"/> 的同名私有方法。</summary>
        private static string BareName(Id id)
        {
            var value = id.Value;
            var dotIndex = value.LastIndexOf('.');
            return dotIndex < 0 ? value : value.Substring(dotIndex + 1);
        }
    }
}
