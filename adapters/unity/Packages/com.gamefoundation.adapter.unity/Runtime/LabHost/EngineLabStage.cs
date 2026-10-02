#nullable enable
// EngineLabStage：实验室引擎宿主的舞台（手感设计/06 第 4 节"引擎宿主"）。
//
// 它是内核 LabHost 的一个宿主扩展（Lab.LabHostExtension），不是第二份宿主循环：脚本、格子、逐步顺序、逻辑组与指纹定义都复用内核同一套，
// 舞台只在固定的接入点上叠加"引擎侧表现驱动"——把真实适配器（精灵/模型渲染、帧动画与动画器、镜头、特效、音频门面）按模拟时间确定性地推进，
// 并把无头宿主证明不了的东西记进 Lab.EngineRecording（内核折算成 engine 度量组）。逻辑组与无头宿主逐字节一致由跨宿主不变量证明。
//
// 判断记录（引擎侧失败不得改变逻辑）：视图工厂包装一律先走内核的记录型假视图、再走引擎视图；引擎视图创建/同步/事件的任何异常都被吞掉并记入
// EngineRecording.Errors（度量 engine_errors），始终返回内核的记录视图——否则玩家的位姿帧记录会变，逻辑指纹与无头宿主分叉。
//
// 判断记录（模拟时间，不用墙钟）：帧动画播放器、特效序列播放器、动画器、镜头都由本舞台按"帧长"手动推进（这些组件自己的 Update 在舞台里被关掉）；
// 帧动画播放器与特效序列播放器的步长经适配器公开的可注入时间源（IFrameTimeSource，M4-W4）给出，舞台只调公开的 Step()，不再碰内部推进入口。
// 所以对齐误差与镜头曲线是确定的；只有每帧驱动耗时与 GPU 帧耗时是真实时钟。
//
// 判断记录（渲染隔离）：舞台的全部物体放在专用层（EngineLabOptions.IsolationLayer），舞台相机只渲染这一层，并在舞台存续期间把这一层
// 从场内其它相机的剔除遮罩里摘掉；Dispose 时原样恢复遮罩并销毁全部物体。
//
// 判断记录（合成外形登记）：实验室数据里单位的外形行指向没有美术的占位精灵集，直接交给引擎视图工厂只能得到单帧占位。舞台给引擎视图工厂一个合成的
// 外形登记：任何生物单位都按"标准假人"外形（std_dummy_biped 的精灵或模型形态，格子的 form 决定）创建，这样真实的帧动画剪辑、命中帧事件、
// 动画器都能被驱动；内核自己的外形登记（记录视图用）不变，数据集哈希与既有基线不受影响。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Rules.Common;
using Lab;
using Presentation.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using UnityEngine;
using DisplayInfo = Core.Foundation.DisplayInfo.DisplayInfo;
using IView = Presentation.Common.IView;

namespace Adapter.Unity.LabHost
{
    public sealed class EngineLabStage : LabHostExtension, IDisposable
    {
        internal static readonly Id ProbeVfxId = new Id("vfx.lab_probe");
        internal static readonly Id ProbeAnchorId = new Id("anchor.lab_probe");
        internal static readonly Id ProbeResourceId = new Id("vfx.hit_spark");
        internal static readonly Id SwingSfxResource = new Id("sfx.swing_01");
        internal static readonly Id ImpactSfxResource = new Id("sfx.hit_01");
        internal static readonly Id StandardDisplayMapId = new Id("display.map.std_dummy_biped");

        private readonly EngineLabOptions _options;
        private readonly EngineRecording _rec = new EngineRecording();
        private readonly List<KeyValuePair<Camera, int>> _savedMasks = new List<KeyValuePair<Camera, int>>();
        private readonly Dictionary<Id, EngineEntry> _entries = new Dictionary<Id, EngineEntry>();
        private readonly Dictionary<Id, List<double>> _engineHits = new Dictionary<Id, List<double>>();
        private readonly Dictionary<Id, List<int>> _logicHits = new Dictionary<Id, List<int>>();
        private readonly Dictionary<Id, ActiveFreeze> _activeFreezes = new Dictionary<Id, ActiveFreeze>();
        private readonly List<ActiveImpulse> _activeImpulses = new List<ActiveImpulse>();
        private readonly Stopwatch _frameWatch = new Stopwatch();

        private LabHostContext? _ctx;
        private GameObject? _root;
        private GameObject? _cameraGo;
        private Camera? _camera;
        private UnityCamera? _unityCamera;
        private UnityRenderer2D? _r2d;
        private UnityRenderer3D? _r3d;
        private UnityAudio? _audio;
        private UnityResourceLoader? _loader;
        private UnityViewFactory? _unityFactory;
        private VfxPlayer? _vfx;
        private SfxPlayer? _sfx;
        private CompositeFeedbackSink? _sink;
        private CharacterRigHitFrameSource? _hitSource;
        private StageDisplayRegistry? _display;
        private bool _broken;
        private bool _disposed;
        private int _eventCursor;
        private int _lastTick = -1;
        private double _simNow;
        private double _pumpMs;
        private bool _cameraRelative;
        private double _yawRadians;
        private double _pitchDegrees;
        private readonly ManualFrameTimeSource _clock = new ManualFrameTimeSource();
        private GpuFrameProbe? _gpu;
        private double _gpuMs;
        private FallbackOrientation? _fallbackOrientation;

        public EngineLabStage(EngineLabOptions? options = null)
        {
            _options = options ?? new EngineLabOptions();
        }

        /// <summary>本次运行的引擎记录（运行结束后同时挂在 <see cref="LabRecording.Engine"/> 上）。</summary>
        public EngineRecording Record => _rec;

        /// <summary>舞台相机（只读；供测试检查渲染隔离与相机轴）。</summary>
        public Camera? StageCamera => _camera;

        public UnityCamera? StageUnityCamera => _unityCamera;

        // ───────── 接入点 ─────────

        public override void OnAttach(LabHostContext context)
        {
            _ctx = context;
            _rec.Host = "unity";
            var cell = context.Cell.Cell;
            var cut = cell.LastIndexOf('_');
            _rec.Plane = cut > 0 ? cell.Substring(0, cut) : cell;
            _rec.RigKind = context.Cell.Form;
            _rec.InputNoise = _options.NoiseReplay != null
                ? _options.NoiseReplay.ModelId
                : _options.Noise == null ? "none" : _options.Noise.Id;
            var control = _options.ControlSpaceOverride ?? context.Cell.ControlSpace;
            _cameraRelative = string.Equals(control, ControlSpace.CameraRelative, StringComparison.Ordinal);
            _yawRadians = _options.CameraYawDegrees * Math.PI / 180.0;
            _fallbackOrientation = new FallbackOrientation(_yawRadians);
            var honorCell = _options.HonorCellCameraMode
                && string.Equals(context.Cell.CameraMode, "fixed_pitch", StringComparison.Ordinal);
            _pitchDegrees = _options.CameraPitchDegrees ?? (honorCell ? _options.FixedPitchDegrees : 0.0);

            try
            {
                BuildStage(context);
            }
            catch (Exception ex)
            {
                _broken = true;
                Fail("舞台装配失败：" + ex);
            }
        }

        public override IViewFactory WrapViewFactory(IViewFactory inner, LabHostContext context) =>
            _broken ? inner : new TeeViewFactory(inner, this);

        public override IFeedbackSink? FeedbackTee => _broken ? null : _sink;

        /// <summary>
        /// 相机朝向查询（框架原生相机相对控制空间的来源）：舞台相机装配成功时是真实的 <see cref="UnityCamera"/>（偏航来自它真正的相机朝向）；
        /// 舞台装配失败（引擎侧失败不得改变逻辑）时退回按选项偏航的纯数值朝向，使逻辑与装配成功时一致。
        /// </summary>
        public override ICameraOrientation? CameraOrientation => _unityCamera != null && !_broken ? _unityCamera : _fallbackOrientation;

        public override string? ControlSpaceOverride => _options.ControlSpaceOverride;

        /// <summary>
        /// 相机相对输入的三向检验（框架原生换算由输入映射完成，宿主不再换算）：<paramref name="mapped"/> 是输入映射按相机偏航给出的世界方向，
        /// 本方法检验它与 ① 真实相机的右/上轴算出的期望方向 ② 偏航公式 一致（轴向误差），以及 ③ 世界方向经真实相机视图矩阵回到屏幕后与摇杆方向一致
        /// （屏幕误差；俯仰把屏幕"上"方向压扁 cos(俯仰) 倍，检验前先除回去）。
        /// </summary>
        public override void OnMoveAxis(int tick, Vec2 stick, Vec2 mapped)
        {
            if (!_cameraRelative || _broken || _camera == null || _unityCamera == null)
            {
                return;
            }

            try
            {
                if (Math.Abs(stick.X) + Math.Abs(stick.Y) <= 1e-9)
                {
                    return;
                }

                var tr = _camera.transform;
                var right = new Vec2(tr.right.x, tr.right.y);
                var upXy = new Vec2(tr.up.x, tr.up.y);
                var upLength = Math.Sqrt(upXy.X * upXy.X + upXy.Y * upXy.Y);
                var up = upLength > 1e-9 ? new Vec2(upXy.X / upLength, upXy.Y / upLength) : upXy;
                var expected = ControlSpace.ExpectedFromAxes(stick, right, up);
                var analytic = ControlSpace.CameraRelativeToWorld(stick, _unityCamera.YawRadians);
                var axesError = Math.Max(ControlSpace.AngleDegrees(mapped, expected), ControlSpace.AngleDegrees(mapped, analytic));
                var cameraSpace = _camera.worldToCameraMatrix.MultiplyVector(new Vector3((float)mapped.X, (float)mapped.Y, 0f));
                var squash = Math.Max(1e-6, Math.Cos(_unityCamera.EffectivePitchDegrees * Math.PI / 180.0));
                var screen = new Vec2(cameraSpace.x, cameraSpace.y / squash);
                var screenError = ControlSpace.AngleDegrees(screen, stick);
                _rec.Controls.Add(new ControlSample(tick, stick, _options.CameraYawDegrees, mapped, axesError, screenError));
            }
            catch (Exception ex)
            {
                Fail("相机相对核对失败：" + ex.Message);
            }
        }

        /// <summary>舞台装配失败时的朝向来源：只有选项里的偏航。</summary>
        private sealed class FallbackOrientation : ICameraOrientation
        {
            public FallbackOrientation(double yawRadians) => YawRadians = yawRadians;

            public double YawRadians { get; }
        }

        public override void OnFixedStepEnd(int tick)
        {
            _lastTick = tick;
            if (_broken || _ctx == null)
            {
                return;
            }

            try
            {
                var events = _ctx.World.Events;
                while (_eventCursor < events.Count)
                {
                    var evt = events[_eventCursor++];
                    if (evt is ActionMarkerEvent marker
                        && (string.Equals(marker.Name, "hit", StringComparison.Ordinal)
                            || string.Equals(marker.Name, "hit_frame", StringComparison.Ordinal)))
                    {
                        if (!_logicHits.TryGetValue(marker.ActorId, out var list))
                        {
                            list = new List<int>();
                            _logicHits[marker.ActorId] = list;
                        }

                        list.Add(tick);
                    }
                }
            }
            catch (Exception ex)
            {
                Fail("读取逻辑命中标记失败：" + ex.Message);
            }
        }

        public override void OnFrame(int frame, double alpha, double frameSeconds)
        {
            _rec.FramesDriven++;
            if (_broken)
            {
                return;
            }

            _pumpMs = 0;
            _gpuMs = 0;
            _frameWatch.Restart();
            try
            {
                DriveFrame(frame, frameSeconds);
            }
            catch (Exception ex)
            {
                Fail("帧驱动失败：" + ex);
            }

            _frameWatch.Stop();
            // GPU 渲染与等待的耗时单独记（gpu_ms_*），不计入每帧驱动耗时（frame_ms_* 保持"引擎侧驱动 CPU 耗时"的口径不变）。
            _rec.FrameMilliseconds.Add(Math.Max(0.0, _frameWatch.Elapsed.TotalMilliseconds - _pumpMs - _gpuMs));
        }

        public override void OnFinished(LabRecording recording)
        {
            recording.Engine = _rec;
            if (_broken || _ctx == null)
            {
                return;
            }

            try
            {
                FinishHitAlignment();
                CloseFreezes();
                if (recording.Equip != null)
                {
                    EquipLayerAudit.Run(recording.Equip, _ctx, _rec, _loader!, PumpLoader);
                }
            }
            catch (Exception ex)
            {
                Fail("收尾失败：" + ex);
            }
        }

        // ───────── 装配 ─────────

        private void BuildStage(LabHostContext ctx)
        {
            var layer = _options.IsolationLayer;
            if (layer < 0 || layer > 31)
            {
                throw new ArgumentOutOfRangeException(nameof(_options.IsolationLayer), layer, "隔离层必须在 0..31 之内");
            }

            _root = new GameObject("EngineLabStage") { layer = layer };
            _cameraGo = new GameObject("EngineLabCamera") { layer = layer };
            _cameraGo.transform.SetParent(_root.transform, false);
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.cullingMask = 1 << layer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _camera.enabled = false;
            _camera.depth = -100;
            _camera.targetTexture = null;
            _unityCamera = new UnityCamera(_camera);
            var honorCell = _options.HonorCellCameraMode
                && string.Equals(ctx.Cell.CameraMode, "fixed_pitch", StringComparison.Ordinal);
            _unityCamera.Configure(_pitchDegrees, _options.CameraYawDegrees, new ZoomRange(1.0, 20.0));
            _unityCamera.ApplyYawRotation = Math.Abs(_options.CameraYawDegrees) > 1e-12;
            // 俯仰与透视是显式声明才生效的可选能力（M4-W4）：选项里不给俯仰且没有按格子相机模式取用，相机仍是原来的正交俯视。
            _unityCamera.FieldOfViewDegrees = _options.CameraFieldOfViewDegrees;
            _unityCamera.ApplyPitch = _pitchDegrees > 1e-12;
            _unityCamera.Perspective = _options.CameraPerspective ?? honorCell;
            if (_options.GpuTiming)
            {
                _gpu = new GpuFrameProbe(_camera);
                _rec.GpuAvailable = _gpu.Available;
                _rec.GpuUnavailableReason = _gpu.UnavailableReason;
            }
            else
            {
                _rec.GpuUnavailableReason = "选项 GpuTiming 关闭";
            }

            foreach (var other in Camera.allCameras)
            {
                if (other == _camera)
                {
                    continue;
                }

                _savedMasks.Add(new KeyValuePair<Camera, int>(other, other.cullingMask));
                other.cullingMask &= ~(1 << layer);
            }

            _loader = UnityEngineHost.Ensure().ResourceLoader;
            _r2d = new UnityRenderer2D(_root.transform, _loader);
            _r2d.EffectTimeSource = _clock;
            _r3d = new UnityRenderer3D(_root.transform, _loader);
            _audio = new UnityAudio(_root.transform, _loader);

            _display = new StageDisplayRegistry(ctx.DisplayInfo, ctx.Cell.Form);
            var directionCount = string.Equals(ctx.Cell.Facing, "flip", StringComparison.Ordinal) ? 2 : 8;
            var renderOptions = new RenderOptions
            {
                DirectionCount = directionCount,
                HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven,
            };
            _hitSource = new CharacterRigHitFrameSource();
            _hitSource.HitFrameReached += OnEngineHitFrame;
            _unityFactory = new UnityViewFactory(
                _r2d, new RenderConventionHost(renderOptions), _display, _loader, ctx.World.Bus, ctx.World.Registry, _r3d,
                _hitSource, null, renderOptions);

            // 特效与音效：目录由实验室内核的手感音效表（LabFeedbackCatalog）与一个宿主自有的探针特效组成，资源引用换成占位美术里真实存在的文件。
            var vfxCatalog = new Dictionary<Id, VfxDef>
            {
                [ProbeVfxId] = new VfxDef(ProbeVfxId, "combat", VfxAttachMode.Anchor, null, ProbeResourceId),
            };
            var sfxCatalog = new Dictionary<Id, SfxDef>();
            // 手感音效表只在手感装配开着时存在（数据集没有带 feel_layer 的 sfx.def 行时 SfxRows 直接报错）：手感关着（目标选择式格子、换装脚本等）
            // 打击反馈流水线不存在，音效目录留空即可，舞台的其余部分（视图、相机、图标核对）照常装配。
            IEnumerable<SfxDef> sfxRows = ctx.FeelOn ? LabFeedbackCatalog.SfxRows(ctx.World.Registry) : new List<SfxDef>();
            foreach (var row in sfxRows)
            {
                var isSwing = row.FeelLayer == SfxFeelLayer.Swing || row.FeelLayer == SfxFeelLayer.Whiff;
                sfxCatalog[row.Id] = new SfxDef(
                    row.Id, row.Layer, row.Priority, row.Variants, isSwing ? SwingSfxResource : ImpactSfxResource, row.Loop,
                    row.FeelLayer, row.FeelTier, row.FeelMaterial);
            }

            Vec2? Resolve(Id entity, Id anchor)
            {
                var found = ctx.World.World.GetEntity(entity);
                return found?.Position;
            }

            _vfx = new VfxPlayer(
                _r2d, _unityCamera, vfxCatalog, null, (e, a) => Resolve(e, a), e => Resolve(e, ProbeAnchorId), null, _loader, _r3d);
            _sfx = new SfxPlayer(_audio, new RngHost(1UL), sfxCatalog, null, null, _loader);
            _sink = new CompositeFeedbackSink(
                _vfx, _sfx, (e, s, t) => { }, d => { }, p => { }, (e, p) => { }, e => Resolve(e, ProbeAnchorId));
            _sink.OnImpactCamera = OnImpactCamera;
            _sink.OnFreezePresentation = OnFreezePresentation;
            _sink.OnReleasePresentation = OnReleasePresentation;

            // 预加载：探针特效与手感音效用到的两份音频，避免第一次播放走"等待加载"的慢路径而改变观测窗口。
            _loader.LoadAsync(ProbeResourceId, ResourceKind.Effect, (id, ok) => { });
            _loader.LoadAsync(SwingSfxResource, ResourceKind.Audio, (id, ok) => { });
            _loader.LoadAsync(ImpactSfxResource, ResourceKind.Audio, (id, ok) => { });
            PumpLoader();
        }

        private void PumpLoader()
        {
            if (_loader == null)
            {
                return;
            }

            _pumpMs += PumpLoader(_loader, _options.MaxLoadWaitMs);
        }

        /// <summary>泵资源加载器直到没有挂起的加载或超时；返回花掉的毫秒数（不计入帧耗时）。</summary>
        internal static double PumpLoader(UnityResourceLoader loader, int maxWaitMs)
        {
            var watch = Stopwatch.StartNew();
            while (loader.PendingLoadCount > 0 && watch.ElapsedMilliseconds < maxWaitMs)
            {
                loader.Tick();
                Thread.Sleep(1);
            }

            loader.Tick();
            return watch.Elapsed.TotalMilliseconds;
        }

        internal void Fail(string message)
        {
            _rec.Errors.Add(message);
            if (_options.ThrowOnEngineError)
            {
                throw new InvalidOperationException("引擎宿主：" + message);
            }
        }

        // ───────── 引擎视图 ─────────

        private sealed class EngineEntry
        {
            public Id Entity;
            public IView View = null!;
            public IPresentationFreezable? Freezable;
            public UnityFrameAnimPlayer? Player;
            public Animator? Animator;
            public Id? LastClip;
            public string? LastModelClip;

            private double _animatorAccum;
            private int _prevStateHash;
            private float _prevNormalized;
            private bool _hasPrev;

            /// <summary>该 rig 的动画时间轴累计量（秒）：精灵取帧动画播放器的累计推进量；模型取动画器同一状态内归一化时间的累计增量 × 片段长度。</summary>
            public double Clock()
            {
                if (Player != null)
                {
                    return Player.AdvancedSeconds;
                }

                return _animatorAccum;
            }

            /// <summary>每帧在动画器更新之后调用：同一状态且不在过渡中的归一化时间增量计入累计量（状态切换/过渡只重设基线，不算时间推进，避免把"换了个状态"误当成"动画在走"）。</summary>
            public void SampleAnimator()
            {
                if (Player != null || Animator == null)
                {
                    return;
                }

                var info = Animator.GetCurrentAnimatorStateInfo(0);
                var transitioning = Animator.IsInTransition(0);
                if (_hasPrev && !transitioning && info.fullPathHash == _prevStateHash && info.normalizedTime > _prevNormalized)
                {
                    // 没有片段的状态长度是无穷大：退回归一化时间（无量纲，同样随动画推进单调增长，足够判断"推进了没有"）。
                    var length = float.IsInfinity(info.length) || info.length <= 0f ? 1f : info.length;
                    _animatorAccum += (info.normalizedTime - _prevNormalized) * length;
                }

                _prevStateHash = info.fullPathHash;
                _prevNormalized = info.normalizedTime;
                _hasPrev = !transitioning;
            }
        }

        internal IView? CreateEngineView(ViewKind kind, Id displayId, Id entityId)
        {
            if (_broken || _unityFactory == null)
            {
                return null;
            }

            try
            {
                var view = _unityFactory.CreateView(kind, displayId, entityId);
                SetLayerRecursive();
                PumpLoader();
                var entry = new EngineEntry { Entity = entityId, View = view };
                if (view is IHasCharacterRig hasRig && hasRig.Rig is IPresentationFreezable freezable)
                {
                    entry.Freezable = freezable;
                }

                if (view is UnitySpriteView sprite && _r2d != null)
                {
                    var layersRoot = _r2d.GetLayersRoot(sprite.EngineHandle);
                    if (layersRoot != null)
                    {
                        entry.Player = layersRoot.GetComponentInChildren<UnityFrameAnimPlayer>(true);
                        if (entry.Player != null)
                        {
                            entry.Player.enabled = false;
                            entry.Player.TimeSource = _clock;
                            // 精灵 rig 直接订阅帧动画播放器的关键帧：命中帧（攻击剪辑）或施放点 release（读条施法剪辑——手感场景的技能走的是 cast 剪辑，
                            // 数据里 cast 剪辑的关键帧是 release 而不是 hit_frame）。两者都是"逻辑命中标记在动画里的对应点"。
                            var hitEntity = entityId;
                            entry.Player.OnAnimEvent(marker =>
                            {
                                if (string.Equals(marker, FrameAnimClip.HitFrameMarker, StringComparison.Ordinal)
                                    || string.Equals(marker, ReleaseMarker, StringComparison.Ordinal))
                                {
                                    EnqueueEngineHit(hitEntity);
                                }
                            });
                        }
                    }
                }
                else if (view is UnityModelView model && _r3d != null)
                {
                    var root = _r3d.GetModelRoot(model.EngineHandle);
                    if (root != null)
                    {
                        entry.Animator = root.GetComponentInChildren<Animator>(true);
                        if (entry.Animator != null)
                        {
                            entry.Animator.enabled = false;
                        }
                    }
                }

                _entries[entityId] = entry;
                return view;
            }
            catch (Exception ex)
            {
                Fail("创建引擎视图失败（" + entityId.Value + "）：" + ex.Message);
                return null;
            }
        }

        internal void ForgetEngineView(Id entityId) => _entries.Remove(entityId);

        private void SetLayerRecursive()
        {
            if (_root == null)
            {
                return;
            }

            SetLayer(_root.transform, _options.IsolationLayer);
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (var i = 0; i < t.childCount; i++)
            {
                SetLayer(t.GetChild(i), layer);
            }
        }

        // ───────── 反馈落地 ─────────

        private void OnImpactCamera(ImpactCameraCue cue)
        {
            if (_unityCamera == null || _camera == null)
            {
                return;
            }

            var directional = cue.Direction.X * cue.Direction.X + cue.Direction.Y * cue.Direction.Y > 1e-12;
            var trace = new CameraImpulseTrace(
                _lastTick + 1, cue.Magnitude, cue.DecayMs, cue.Magnitude * 2.0 * _unityCamera.VisibleHalfHeight, directional);
            var overlapped = false;
            foreach (var other in _activeImpulses)
            {
                other.Trace.Truncated = true;
                overlapped = true;
            }

            if (overlapped)
            {
                trace.Truncated = true;
            }

            _unityCamera.Impulse(cue.Direction, cue.Magnitude, cue.DecayMs);
            _unityCamera.Tick(0.0);
            trace.Curve.Add(new KeyValuePair<double, double>(0.0, _unityCamera.CurrentImpulseOffset.magnitude));
            _rec.CameraImpulses.Add(trace);
            _activeImpulses.Add(new ActiveImpulse { Trace = trace });
        }

        private sealed class ActiveImpulse
        {
            public CameraImpulseTrace Trace = null!;
            public double Elapsed;
        }

        private sealed class ActiveFreeze
        {
            public FreezeTrace Trace = null!;
            public Id Unit;
            public ParticleHandle? Probe;
            public double ProbeLast = double.NaN;
            public double ProbeStart = double.NaN;
            public ParticleHandle? Control;
            public Id? ControlUnit;
            public HashSet<Id> EverFrozen = new HashSet<Id>();
            public double ControlLast = double.NaN;
            public double ControlStart = double.NaN;
            public double RigStart;
            public double RigLast;
            public Dictionary<Id, double> BystanderStart = new Dictionary<Id, double>();
            public Dictionary<Id, double> BystanderLast = new Dictionary<Id, double>();
        }

        private void OnFreezePresentation(IReadOnlyList<Id> unitIds, int ticks, ImpactFreezeLayers layers)
        {
            for (var i = 0; i < unitIds.Count; i++)
            {
                var unit = unitIds[i];
                if (_entries.TryGetValue(unit, out var entry))
                {
                    entry.Freezable?.FreezePresentation(layers.Trail);
                }

                if (layers.Particles)
                {
                    (_vfx as IVfxFreezable)?.SetOwnerFrozen(unit, true);
                }

                if (_activeFreezes.ContainsKey(unit))
                {
                    continue;
                }

                var active = new ActiveFreeze { Trace = new FreezeTrace(Label(unit), _lastTick + 1, ticks), Unit = unit };
                for (var k = 0; k < unitIds.Count; k++)
                {
                    active.EverFrozen.Add(unitIds[k]);
                }

                if (entry != null)
                {
                    active.RigStart = entry.Clock();
                    active.RigLast = active.RigStart;
                }

                foreach (var other in _entries)
                {
                    if (!other.Key.Equals(unit) && !IsFrozen(other.Key, unitIds))
                    {
                        active.BystanderStart[other.Key] = other.Value.Clock();
                        active.BystanderLast[other.Key] = active.BystanderStart[other.Key];
                    }
                }

                if (_options.ProbeParticles && _vfx != null)
                {
                    active.Probe = SpawnProbe(unit);
                    active.ProbeStart = SampleParticle(active.Probe);
                    active.ProbeLast = active.ProbeStart;
                    var bystander = FirstBystander(unitIds);
                    if (bystander.HasValue)
                    {
                        active.ControlUnit = bystander;
                        active.Control = SpawnProbe(bystander.Value);
                        active.ControlStart = SampleParticle(active.Control);
                        active.ControlLast = active.ControlStart;
                    }
                }

                _activeFreezes[unit] = active;
                _rec.Freezes.Add(active.Trace);
            }
        }

        private static bool IsFrozen(Id id, IReadOnlyList<Id> unitIds)
        {
            for (var i = 0; i < unitIds.Count; i++)
            {
                if (unitIds[i].Equals(id))
                {
                    return true;
                }
            }

            return false;
        }

        private Id? FirstBystander(IReadOnlyList<Id> frozen)
        {
            foreach (var pair in _entries)
            {
                if (!IsFrozen(pair.Key, frozen) && !_activeFreezes.ContainsKey(pair.Key))
                {
                    return pair.Key;
                }
            }

            return null;
        }

        private ParticleHandle? SpawnProbe(Id owner)
        {
            try
            {
                return _vfx!.Spawn(ProbeVfxId, VfxAttach.Anchor(owner, ProbeAnchorId), null);
            }
            catch (Exception ex)
            {
                Fail("探针粒子生成失败：" + ex.Message);
                return null;
            }
        }

        private double SampleParticle(ParticleHandle? handle)
        {
            if (!handle.HasValue || _r2d == null)
            {
                return double.NaN;
            }

            var played = _r2d.GetParticlePlayedSeconds(handle.Value);
            return played ?? double.NaN;
        }

        private void OnReleasePresentation(IReadOnlyList<Id> unitIds)
        {
            for (var i = 0; i < unitIds.Count; i++)
            {
                var unit = unitIds[i];
                if (_entries.TryGetValue(unit, out var entry))
                {
                    entry.Freezable?.UnfreezePresentation();
                }

                (_vfx as IVfxFreezable)?.SetOwnerFrozen(unit, false);
                if (_activeFreezes.TryGetValue(unit, out var active))
                {
                    FinalizeFreeze(active);
                    _activeFreezes.Remove(unit);
                }
            }
        }

        private void FinalizeFreeze(ActiveFreeze active)
        {
            active.Trace.RigAdvanceSeconds = Math.Max(0.0, active.RigLast - active.RigStart);
            if (!double.IsNaN(active.ProbeStart) && !double.IsNaN(active.ProbeLast))
            {
                active.Trace.ParticleAdvanceSeconds = Math.Max(0.0, active.ProbeLast - active.ProbeStart);
            }

            if (!double.IsNaN(active.ControlStart) && !double.IsNaN(active.ControlLast)
                && active.ControlUnit.HasValue && !active.EverFrozen.Contains(active.ControlUnit.Value))
            {
                active.Trace.ParticleControlAdvanceSeconds = Math.Max(0.0, active.ControlLast - active.ControlStart);
            }

            var best = double.NaN;
            foreach (var pair in active.BystanderStart)
            {
                if (active.EverFrozen.Contains(pair.Key))
                {
                    continue;
                }

                var delta = Math.Max(0.0, active.BystanderLast[pair.Key] - pair.Value);
                best = double.IsNaN(best) ? delta : Math.Max(best, delta);
            }

            active.Trace.BystanderAdvanceSeconds = best;
        }

        private void CloseFreezes()
        {
            foreach (var pair in _activeFreezes)
            {
                FinalizeFreeze(pair.Value);
            }

            _activeFreezes.Clear();
        }

        private string Label(Id id) =>
            _ctx != null && _ctx.Labels.TryGetValue(id, out var label) ? label : id.Value;

        // ───────── 每帧驱动 ─────────

        /// <summary>GPU 帧耗时采样：把舞台相机真实渲染一帧并等 GPU 完成（见 <see cref="GpuFrameProbe"/>）；没有图形设备时什么也不做（记录里标不可用）。</summary>
        private void SampleGpu()
        {
            if (_gpu == null || !_gpu.Available)
            {
                return;
            }

            if (_gpu.TryMeasure(out var ms))
            {
                _rec.GpuFrameMilliseconds.Add(ms);
                _gpuMs += ms;
            }
            else
            {
                // 渲染/回读失败：整次运行改标不可用（部分帧有数、部分帧没有的 GPU 分位数没有意义）。
                _rec.GpuAvailable = false;
                _rec.GpuUnavailableReason = _gpu.UnavailableReason;
                _rec.GpuFrameMilliseconds.Clear();
            }
        }

        private void DriveFrame(int frame, double dt)
        {
            _simNow = (frame + 1) * dt;
            var dtF = (float)dt;
            _clock.SetDelta(dt);

            // 帧动画与动画器：按模拟时间推进（组件自己的 Update 已被关掉）。
            foreach (var pair in _entries)
            {
                var entry = pair.Value;
                if (entry.Player != null)
                {
                    entry.Player.Step();
                }
                else if (entry.Animator != null)
                {
                    entry.Animator.Update(dtF);
                    entry.SampleAnimator();
                    var infos = entry.Animator.GetCurrentAnimatorClipInfo(0);
                    var clipName = infos.Length > 0 && infos[0].clip != null ? infos[0].clip.name : string.Empty;
                    if (clipName.Length > 0 && !string.Equals(clipName, entry.LastModelClip, StringComparison.Ordinal))
                    {
                        entry.LastModelClip = clipName;
                        _rec.ClipTransitions.Add(Label(pair.Key) + ":" + clipName);
                    }
                }
            }

            _r2d?.StepEffects();
            _vfx?.Update(dt);
            _sfx?.Update(dt);
            _audio?.Tick(dt);
            _r3d?.Tick();
            _loader?.Tick();

            // 镜头：推进并采样每条仍在衰减的冲量曲线。
            if (_unityCamera != null)
            {
                _unityCamera.Tick(dt);
                var magnitude = _unityCamera.CurrentImpulseOffset.magnitude;
                for (var i = _activeImpulses.Count - 1; i >= 0; i--)
                {
                    var active = _activeImpulses[i];
                    active.Elapsed += dt;
                    active.Trace.Curve.Add(new KeyValuePair<double, double>(active.Elapsed, magnitude));
                    if (active.Elapsed >= active.Trace.DecayMs / 1000.0)
                    {
                        _activeImpulses.RemoveAt(i);
                    }
                }
            }

            SampleGpu();

            foreach (var pair in _entries)
            {
                var player = pair.Value.Player;
                var clip = player?.CurrentClipId;
                if (clip.HasValue && (!pair.Value.LastClip.HasValue || !pair.Value.LastClip.Value.Equals(clip.Value)))
                {
                    pair.Value.LastClip = clip;
                    _rec.ClipTransitions.Add(Label(pair.Key) + ":" + clip.Value.Value);
                }
            }

            // 冻结观测：被冻结单位 rig / 粒子 / 旁观 rig 的时间轴读数。
            foreach (var pair in _activeFreezes)
            {
                var active = pair.Value;
                foreach (var other in _entries)
                {
                    if (other.Value.Freezable != null && other.Value.Freezable.IsPresentationFrozen)
                    {
                        active.EverFrozen.Add(other.Key);
                    }
                }

                if (_entries.TryGetValue(active.Unit, out var entry))
                {
                    active.RigLast = entry.Clock();
                }

                var probe = SampleParticle(active.Probe);
                if (!double.IsNaN(probe))
                {
                    active.ProbeLast = probe;
                }

                var control = SampleParticle(active.Control);
                if (!double.IsNaN(control))
                {
                    active.ControlLast = control;
                }

                var keys = new List<Id>(active.BystanderLast.Keys);
                foreach (var key in keys)
                {
                    if (_entries.TryGetValue(key, out var bystander))
                    {
                        active.BystanderLast[key] = bystander.Clock();
                    }
                }
            }

            SetLayerRecursive();
        }

        // ───────── 命中对齐 ─────────

        internal const string ReleaseMarker = "release";

        /// <summary>模型 rig 经命中帧注册表到达的事件（精灵 rig 由自己的关键帧订阅直接入队，避免同一个命中帧重复计数）。</summary>
        private void OnEngineHitFrame(Id entity)
        {
            if (_entries.TryGetValue(entity, out var entry) && entry.Player != null)
            {
                return;
            }

            EnqueueEngineHit(entity);
        }

        private void EnqueueEngineHit(Id entity)
        {
            if (!_engineHits.TryGetValue(entity, out var list))
            {
                list = new List<double>();
                _engineHits[entity] = list;
            }

            list.Add(_simNow);
        }

        /// <summary>
        /// 配对：引擎的命中帧事件总是滞后于它对应的逻辑命中，所以每个引擎事件配给"不晚于它的最近一个逻辑命中"；
        /// 该逻辑命中已有配对（同一次读条里事件重复）则忽略此事件。没配上事件的逻辑命中记为缺失（例如读条剪辑在到达施放点之前被下一次施法切走）。
        /// 按先后顺序做 FIFO 会在丢事件时把后面的事件错配给前面的命中，造成假的大误差，所以不用 FIFO。
        /// </summary>
        private void FinishHitAlignment()
        {
            if (_ctx == null)
            {
                return;
            }

            foreach (var pair in _logicHits)
            {
                var ticks = pair.Value;
                var matched = new double[ticks.Count];
                for (var i = 0; i < matched.Length; i++)
                {
                    matched[i] = double.NaN;
                }

                if (_engineHits.TryGetValue(pair.Key, out var events))
                {
                    foreach (var seconds in events)
                    {
                        var index = -1;
                        for (var i = 0; i < ticks.Count; i++)
                        {
                            if (ticks[i] * _ctx.StepSeconds <= seconds + 1e-9)
                            {
                                index = i;
                            }
                        }

                        if (index >= 0 && double.IsNaN(matched[index]))
                        {
                            matched[index] = seconds;
                        }
                    }
                }

                for (var i = 0; i < ticks.Count; i++)
                {
                    _rec.HitAlignments.Add(new HitAlignSample(Label(pair.Key), ticks[i], ticks[i] * _ctx.StepSeconds, matched[i]));
                }
            }
        }

        // ───────── 释放 ─────────

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (_hitSource != null)
                {
                    _hitSource.HitFrameReached -= OnEngineHitFrame;
                }

                foreach (var pair in _entries)
                {
                    try
                    {
                        pair.Value.View.Destroy();
                    }
                    catch (Exception)
                    {
                        // 释放期的异常不再记录：运行记录已定稿。
                    }
                }

                _entries.Clear();
            }
            finally
            {
                _gpu?.Dispose();
                _gpu = null;
                foreach (var saved in _savedMasks)
                {
                    if (saved.Key != null)
                    {
                        saved.Key.cullingMask = saved.Value;
                    }
                }

                _savedMasks.Clear();
                if (_root != null)
                {
                    UnityEngine.Object.DestroyImmediate(_root);
                    _root = null;
                }
            }
        }

        // ───────── 视图工厂包装 ─────────

        private sealed class TeeViewFactory : IViewFactory
        {
            private readonly IViewFactory _inner;
            private readonly EngineLabStage _stage;

            public TeeViewFactory(IViewFactory inner, EngineLabStage stage)
            {
                _inner = inner;
                _stage = stage;
            }

            public IView CreateView(ViewKind kind, Id displayId, Id entityId)
            {
                var recording = _inner.CreateView(kind, displayId, entityId);
                var engine = _stage.CreateEngineView(kind, displayId, entityId);
                return engine == null ? recording : new TeeView(recording, engine, _stage);
            }
        }

        private sealed class TeeView : IView
        {
            private readonly IView _recording;
            private readonly IView _engine;
            private readonly EngineLabStage _stage;

            public TeeView(IView recording, IView engine, EngineLabStage stage)
            {
                _recording = recording;
                _engine = engine;
                _stage = stage;
            }

            public Id EntityId => _recording.EntityId;

            public bool IsAlive => _recording.IsAlive;

            public void Bind(Id entityId)
            {
                _recording.Bind(entityId);
                Guard("Bind", () => _engine.Bind(entityId));
            }

            public void OnEvent(IEvent evt)
            {
                _recording.OnEvent(evt);
                Guard("OnEvent", () => _engine.OnEvent(evt));
            }

            public void SyncPose(Vec2 pos, Direction facing, double height)
            {
                _recording.SyncPose(pos, facing, height);
                Guard("SyncPose", () => _engine.SyncPose(pos, facing, height));
            }

            public void Destroy()
            {
                var id = _recording.EntityId;
                _recording.Destroy();
                Guard("Destroy", () => _engine.Destroy());
                _stage.ForgetEngineView(id);
            }

            private void Guard(string what, Action action)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    _stage.Fail("引擎视图 " + what + " 失败：" + ex.Message);
                }
            }
        }

        // ───────── 合成外形登记 ─────────

        private sealed class StageDisplayRegistry : IDisplayInfoRegistry
        {
            private readonly IDisplayInfoRegistry _inner;
            private readonly bool _model;

            public StageDisplayRegistry(IDisplayInfoRegistry inner, string form)
            {
                _inner = inner;
                _model = string.Equals(form, "model", StringComparison.Ordinal);
            }

            public DisplayInfo? Lookup(Id logicalId)
            {
                var found = _inner.Lookup(logicalId);
                if (found != null && found.Category != DisplayCategory.Creature)
                {
                    return found;
                }

                return Standard(logicalId);
            }

            private DisplayInfo Standard(Id logicalId)
            {
                if (_model)
                {
                    return new DisplayInfo(
                        StandardDisplayMapId, DisplayCategory.Creature, logicalId, DisplayKind.Model, null, null, null, 1.0,
                        Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null, null,
                        new ModelInfo(new Id("model.std_dummy_biped"), new Id("display.anim_set.std_dummy_biped_model")));
                }

                var mirrors = new[]
                {
                    new MirrorPair(new Id("dir.front_side_l"), new Id("dir.front_side_r"), true),
                    new MirrorPair(new Id("dir.side_l"), new Id("dir.side_r"), true),
                    new MirrorPair(new Id("dir.back_side_l"), new Id("dir.back_side_r"), true),
                };
                return new DisplayInfo(
                    StandardDisplayMapId, DisplayCategory.Creature, logicalId, DisplayKind.Sprite, null, null, null, 1.0,
                    Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null,
                    new SpriteInfo("sprite.creature.lab_std_dummy", 8, mirrors, new[] { "body" }), null);
            }

            public IReadOnlyList<DisplayInfo> LookupByCategory(DisplayCategory category) => _inner.LookupByCategory(category);

            public IReadOnlyList<DisplayInfo> All => _inner.All;

            public void Reload() => _inner.Reload();
        }
    }
}
