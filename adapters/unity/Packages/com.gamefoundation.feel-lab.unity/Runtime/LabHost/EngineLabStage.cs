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
using Adapter.Unity;
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

namespace FeelLab.Unity
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
        private IDisplayInfoRegistry? _display;
        private EngineStageExtension? _ext;
        private IStageProjection? _projection;
        private bool _broken;
        private bool _disposed;
        private int _eventCursor;
        private int _lastTick = -1;
        private double _simNow;
        private double _pumpMs;
        private bool _cameraRelative;
        private bool _scriptYawDriven;
        private double _yawRadians;
        private double _pitchDegrees;
        private OrbitCameraController? _orbit;
        private double _baseZoom;
        private double _basePitch;
        private readonly ManualFrameTimeSource _clock = new ManualFrameTimeSource();
        private GpuFrameProbe? _gpu;
        private double _gpuMs;
        private FallbackOrientation? _fallbackOrientation;
        private LabEffectFilter? _effects;
        private IFeedbackSink? _gate;
        private readonly List<FlashFx> _flashFx = new List<FlashFx>();

        private sealed class FlashFx
        {
            public Action Clear = null!;
            public double Remaining;
        }

        public EngineLabStage(EngineLabOptions? options = null)
        {
            _options = options ?? new EngineLabOptions();
            _ext = _options.StageExtension;
        }

        /// <summary>本次运行的引擎记录（运行结束后同时挂在 <see cref="LabRecording.Engine"/> 上）。</summary>
        public EngineRecording Record => _rec;

        /// <summary>舞台相机（只读；供测试检查渲染隔离与相机轴）。</summary>
        public Camera? StageCamera => _camera;

        public UnityCamera? StageUnityCamera => _unityCamera;

        /// <summary>鼠标环绕镜头控制器（ADR-0159）；选项 <see cref="EngineLabOptions.OrbitCamera"/> 没开或舞台相机不带俯仰时为 null。</summary>
        public OrbitCameraController? Orbit => _orbit;

        /// <summary>舞台扩展（选项 <see cref="EngineLabOptions.StageExtension"/>，ADR-0160）；没有扩展时为 null。</summary>
        public EngineStageExtension? Extension => _ext;

        /// <summary>当前生效的模板取景（基础预设是 <c>feel.preset.tpl_*</c> 时取同名 <c>camera_profile.tpl_*</c> 行；否则 <see cref="CameraFraming.None"/>）。试玩模式才会更新。</summary>
        public CameraFraming TemplateFraming { get; private set; } = CameraFraming.None;

        /// <summary>试玩模式当前的相机跟随平滑时间常数（秒）：缺省取选项；模板取景生效时由模板的 follow_lerp 折算。</summary>
        public double FollowSmoothingSeconds => _followSmoothing;

        private double _followSmoothing;
        private string? _framingPreset;

        /// <summary>
        /// 试玩模式的模板取景（ADR-0150，修复"试玩相机不随模板变"的已知限制）：每帧读当前基础预设，变化时按它对应的相机配置行重设取景——
        /// 缩放 = 试玩缺省缩放 × zoom_default / 参照缩放（占位精灵无关模板，只保留模板间的相对取景差）；跟随平滑 = 把每个 60 Hz 帧的 follow_lerp
        /// 折成等价的时间常数 −(1/60)/ln(1−lerp)；俯仰只在舞台相机本来就带俯仰（格子相机模式 fixed_pitch）时取模板值。
        /// 非模板预设恢复舞台缺省。只改引擎侧相机，不回流逻辑。
        /// </summary>
        private void ApplyTemplateFraming()
        {
            if (_ctx == null || _unityCamera == null)
            {
                return;
            }

            var preset = _ctx.World.Gameplay.Feel?.Feel.Resolver.Calibration.BasePresetId ?? string.Empty;
            if (string.Equals(preset, _framingPreset, StringComparison.Ordinal))
            {
                return;
            }

            _framingPreset = preset;
            var framing = CameraFraming.For(_ctx.World.Registry, preset);
            TemplateFraming = framing;
            _baseZoom = _options.InteractiveZoom * framing.ZoomRatio;
            _basePitch = framing.Found ? framing.PitchDegrees : _pitchDegrees;
            _followSmoothing = _options.InteractiveFollowSmoothing;
            if (framing.Found && framing.FollowLerp > 1e-9 && framing.FollowLerp < 1.0)
            {
                _followSmoothing = -(1.0 / 60.0) / Math.Log(1.0 - framing.FollowLerp);
            }

            if (_orbit != null && _orbit.Enabled)
            {
                // 环绕镜头开着：模板只换"基准"取景（基准俯角与基准缩放），实际姿态 = 基准 + 用户转出来的偏移（偏航不被模板重置）。
                ApplyOrbitView();
                return;
            }

            _unityCamera.SetZoom(_baseZoom);
            if (_unityCamera.ApplyPitch)
            {
                _unityCamera.Configure(_basePitch, _options.CameraYawDegrees, _unityCamera.ZoomRangeValue);
            }
        }

        /// <summary>把环绕镜头的当前值写到相机：偏航、俯角（基准 + 偏移，夹区间）、缩放（基准 × 系数）。</summary>
        private void ApplyOrbitView()
        {
            if (_orbit == null || _unityCamera == null)
            {
                return;
            }

            _unityCamera.SetView(_orbit.Yaw, _orbit.EffectivePitch(_basePitch));
            _unityCamera.SetZoom(_baseZoom * _orbit.ZoomFactor);
        }

        /// <summary>
        /// 推进环绕镜头一个控制器帧（ADR-0159）：当前值逼近目标值，相机姿态与全部广告牌即刻跟上（暂停时也能转镜头）；返回当前偏航（度）。
        /// 宿主在固定步之前调用，并把偏航变化录成脚本标记（<see cref="ScriptYawMarker"/>），固定步边界上由 <see cref="OnMarker"/> 提交给相机的朝向查询。
        /// 没有环绕镜头时返回 0 且什么也不做。
        /// </summary>
        public double StepOrbit(double seconds)
        {
            if (_orbit == null || _unityCamera == null || _broken)
            {
                return 0.0;
            }

            _orbit.Step(seconds);
            if (_orbit.Enabled)
            {
                ApplyOrbitView();
                ApplyBillboards();
            }

            return _orbit.Yaw;
        }

        /// <summary>
        /// 打开/关闭鼠标环绕。关闭 = 回到固定镜头：姿态逐位复位到缺省（偏航 0、基准俯角、基准缩放），道具的遮挡次序回到固定镜头的算法。
        /// 舞台没有环绕镜头（见 <see cref="Orbit"/>）时什么也不做。
        /// </summary>
        public void SetOrbitEnabled(bool enabled)
        {
            if (_orbit == null || _unityCamera == null || _broken || _orbit.Enabled == enabled)
            {
                return;
            }

            _orbit.SetEnabled(enabled);
            if (enabled)
            {
                ApplyOrbitView();
            }
            else
            {
                _unityCamera.SetView(_options.CameraYawDegrees, _basePitch);
                _unityCamera.SetZoom(_baseZoom);
            }

            if (_projection != null)
            {
                _projection.FineDepthSort = enabled;
            }

            ApplyBillboards();
        }

        /// <summary>脚本标记名：环绕镜头的偏航（度）变了；<c>Value.X</c> 是偏航。逻辑读它（相机相对移动），所以宿主把它录进脚本。</summary>
        public const string ScriptYawMarker = ControlSpace.YawMarker;

        public override void OnMarker(ScriptEvent marker)
        {
            if (_unityCamera != null && !_broken && string.Equals(marker.Action, ScriptYawMarker, StringComparison.Ordinal))
            {
                // 固定步边界上提交：输入映射在同一个固定步里取样的偏航由此确定（见 UnityCamera.SampleYawAtCommit）。
                _unityCamera.CommitYaw(marker.Value.X);
                if (_scriptYawDriven && _orbit == null)
                {
                    // 脚本回放（没有环绕镜头驱动画面）：画面偏航跟着脚本的偏航流转（内核已按同一个标记提交了朝向查询的偏航）。
                    _unityCamera.ApplyYawRotation = true;
                    _unityCamera.SetView(marker.Value.X, _unityCamera.PitchDegrees);
                }
            }
        }

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
            // 脚本声明的控制空间（ADR-0161）与内核同一条规则：扩展选项的显式覆盖 > 脚本声明 > 格子声明。
            var declared = context.Script.Meta.ControlSpace;
            var control = _options.ControlSpaceOverride ?? (declared.Length > 0 ? declared : context.Cell.ControlSpace);
            _cameraRelative = string.Equals(control, ControlSpace.CameraRelative, StringComparison.Ordinal);
            // 声明了 camera_relative 的脚本自带偏航流（camera_yaw 标记）：内核按它驱动朝向查询。没有环绕镜头驱动画面时（脚本回放），舞台相机跟着偏航流转，
            // 这样画面、真实相机的右/上轴与输入映射用的偏航是同一个量，三向检验照常成立。
            _scriptYawDriven = _cameraRelative && string.Equals(declared, ControlSpace.CameraRelative, StringComparison.Ordinal);
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

        public override IFeedbackSink? FeedbackTee => _broken ? null : (_gate ?? _sink);

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
                // 摇杆"向上"的方向 = 相机水平视线方向（ADR-0161）：俯仰小于 90 度时它与"屏幕上轴"的世界平面投影同向；越过水平后上轴投影翻到背面（含 cos 俯仰因子），
                // 所以按俯仰的符号取（越过水平取反），正好水平（上轴投影为零）时直接取视线的水平分量——整个范围内都是同一个量。
                var cosPitch = Math.Cos(_unityCamera.EffectivePitchDegrees * Math.PI / 180.0);
                var basis = cosPitch >= 1e-6 ? tr.up : (cosPitch <= -1e-6 ? -tr.up : tr.forward);
                var upXy = new Vec2(basis.x, basis.y);
                var upLength = Math.Sqrt(upXy.X * upXy.X + upXy.Y * upXy.Y);
                var up = upLength > 1e-9 ? new Vec2(upXy.X / upLength, upXy.Y / upLength) : upXy;
                var expected = ControlSpace.ExpectedFromAxes(stick, right, up);
                var analytic = ControlSpace.CameraRelativeToWorld(stick, _unityCamera.YawRadians);
                var axesError = Math.Max(ControlSpace.AngleDegrees(mapped, expected), ControlSpace.AngleDegrees(mapped, analytic));
                var cameraSpace = _camera.worldToCameraMatrix.MultiplyVector(new Vector3((float)mapped.X, (float)mapped.Y, 0f));
                // 回到屏幕：俯仰把屏幕"上"方向压扁 cosPitch 倍（越过水平后符号翻转），除回去再与摇杆比；正好水平时屏幕纵向没有信息（地面沿视线方向压成一条线），
                // 只比横向（纵向置零后两者夹角无意义，记 0）。
                var screenError = 0.0;
                if (Math.Abs(cosPitch) >= 1e-3)
                {
                    screenError = ControlSpace.AngleDegrees(new Vec2(cameraSpace.x, cameraSpace.y / cosPitch), stick);
                }
                var sampleYawDegrees = _orbit != null ? _unityCamera.YawRadians * 180.0 / Math.PI : _options.CameraYawDegrees;
                _rec.Controls.Add(new ControlSample(tick, stick, sampleYawDegrees, mapped, axesError, screenError));
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
                    _ext?.OnLogicEvent(evt, tick);
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
            // 自由镜头的可选声明（ADR-0161，缺省都不声明 = 相机与此前逐位一致）：俯仰范围越过水平、绕头部高度转、贴地拉近。
            if (_options.CameraPitchRange.HasValue)
            {
                _unityCamera.DeclarePitchRange(_options.CameraPitchRange.Value.MinDegrees, _options.CameraPitchRange.Value.MaxDegrees);
            }

            _unityCamera.FocusHeight = _options.CameraFocusHeight;
            _unityCamera.GroundAvoidanceMargin = _options.CameraGroundAvoidanceMargin;
            _unityCamera.GroundAvoidance = _options.CameraGroundAvoidance;
            _unityCamera.ApplyPitch = _pitchDegrees > 1e-12;
            _unityCamera.Perspective = _options.CameraPerspective ?? honorCell;
            if (_options.GpuTiming && !_options.Interactive)
            {
                _gpu = new GpuFrameProbe(_camera);
                _rec.GpuAvailable = _gpu.Available;
                _rec.GpuUnavailableReason = _gpu.UnavailableReason;
            }
            else
            {
                _rec.GpuUnavailableReason = _options.Interactive ? "试玩模式不测 GPU 帧耗时" : "选项 GpuTiming 关闭";
            }

            if (_options.Interactive)
            {
                // 人手试玩：舞台相机真正渲染到屏幕（缺省是关着、只在测 GPU 时离屏渲染一次）；带音频监听器；背景用深灰，地面网格在下面建。
                _camera.enabled = true;
                // 判断记录（相机次序，ADR-0154 顺带修复）：宿主相机（深度 0、清屏）会在舞台相机之后清掉整个屏幕，所以试玩时舞台相机排在所有相机之后画
                // （深度 100）；宿主相机的遮罩已摘掉隔离层，不会重复画舞台内容。
                _camera.depth = 100;
                _camera.backgroundColor = new Color(0.09f, 0.10f, 0.12f);
                _cameraGo.AddComponent<AudioListener>();
                _unityCamera.SetZoom(_options.InteractiveZoom);
                _followSmoothing = _options.InteractiveFollowSmoothing;
                _baseZoom = _options.InteractiveZoom;
                _basePitch = _pitchDegrees;
                if (_options.OrbitCamera && _unityCamera.ApplyPitch)
                {
                    // 鼠标环绕镜头（ADR-0159）：偏航开关常开（偏航 0 时姿态与不开逐位相同），朝向查询改为固定步边界提交（见 OnMarker）。
                    _orbit = new OrbitCameraController(_options.OrbitOptions);
                    // 环绕镜头的绝对俯角区间必须落在相机已声明的俯仰范围内：否则相机会悄悄夹紧，控制器报的俯角与画面不一致——这是装配期声明错误，不静默。
                    if (_orbit.Options.PitchMinDegrees < _unityCamera.PitchMinDegrees - 1e-9 || _orbit.Options.PitchMaxDegrees > _unityCamera.PitchMaxDegrees + 1e-9)
                    {
                        throw new ArgumentException(
                            $"环绕镜头的俯角区间 [{_orbit.Options.PitchMinDegrees}, {_orbit.Options.PitchMaxDegrees}] 超出相机声明的俯仰范围 "
                            + $"[{_unityCamera.PitchMinDegrees}, {_unityCamera.PitchMaxDegrees}]（EngineLabOptions.CameraPitchRange）");
                    }

                    _unityCamera.ApplyYawRotation = true;
                    _unityCamera.SampleYawAtCommit = true;
                    _orbit.SetEnabled(_options.OrbitCameraStartsEnabled);
                }
            }

            // 判断记录（宿主相机先于遮罩摘除，ADR-0154 顺带修复）：引擎宿主（UnityEngineHost）第一次 Ensure 时才创建它自己的相机
            // （GameFoundation.Camera，剔除遮罩 = 全部层）。此前这一步排在摘遮罩循环之后，空场景里宿主相机晚于循环出生，
            // 遮罩里仍带着隔离层，于是宿主相机（可视半高 5、不跟随）把舞台相机画好的画面整个盖掉——试玩画面被缩成一半、也不跟随玩家。
            // 现在先确保宿主存在，再对包括宿主相机在内的全部其它相机摘掉隔离层。
            _loader = UnityEngineHost.Ensure().ResourceLoader;
            foreach (var other in Camera.allCameras)
            {
                if (other == _camera)
                {
                    continue;
                }

                _savedMasks.Add(new KeyValuePair<Camera, int>(other, other.cullingMask));
                other.cullingMask &= ~(1 << layer);
            }

            _r2d = new UnityRenderer2D(_root.transform, _loader);
            _r2d.EffectTimeSource = _clock;
            _r3d = new UnityRenderer3D(_root.transform, _loader);
            _audio = new UnityAudio(_root.transform, _loader);

            // 判断记录（舞台扩展，ADR-0160）：扩展只换呈现——外形登记按单位种类给真实美术外形，武器风格行换成扩展指定的一行；逻辑一概不动。
            _display = _ext?.CreateDisplayRegistry(ctx.DisplayInfo, ctx.Cell.Form) ?? new StageDisplayRegistry(ctx.DisplayInfo, ctx.Cell.Form);

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
                _hitSource, new LabWeaponStyleSource(ctx.World.Registry, ctx.Cell.Form, _ext?.WeaponStylePrefix ?? "lab_"), renderOptions);

            // 判断记录（ADR-0154）：生产装配里受击反应查询由表现装配交给视图工厂，动画状态机据此进反应驱动模式
            // （受击姿势按裁决事件选子键 hit.light/heavy/knockback/knockdown/getup，反应为 none/霸体不播受击动画）；
            // 舞台自己建视图工厂，原先漏了这一步，只会播基础 hit——实验室舞台与生产表现不一致是舞台自己的缺陷。
            // 所以所有舞台（原试玩场景与演示场景）都交付，口径与 PresentationAssembly 相同（手感受击裁决已装配且处于活动状态才交付）；
            // 只读查询，不碰逻辑。
            var hitReactions = ctx.World.Gameplay.Feel?.Rules.HitFeel.Host;
            if (hitReactions != null && hitReactions.Active)
            {
                _unityFactory.SetHitReactionQuery(hitReactions);
            }

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
                _vfx, _sfx, (e, s, t) => { }, d => { }, p => OnShake(), (e, p) => OnFlash(e, p), e => Resolve(e, ProbeAnchorId));
            _sink.OnImpactCamera = OnImpactCamera;
            _sink.OnFreezePresentation = OnFreezePresentation;
            _sink.OnReleasePresentation = OnReleasePresentation;
            _effects = _options.Effects;
            if (_effects != null)
            {
                _gate = new GatedSink(_sink, _effects, () => _lastTick + 1);
            }

            if (_options.Interactive)
            {
                var sceneContext = new StageSceneContext(
                    _root.transform, _options.IsolationLayer, _loader, ctx, _camera, _unityCamera.ApplyPitch,
                    string.Equals(ctx.Cell.Form, "model", StringComparison.Ordinal),
                    _orbit != null ? (double?)_orbit.Options.FloorSize : null, _orbit != null && _orbit.Enabled, FlashEntity);
                if (_ext != null && _ext.BuildScene(sceneContext))
                {
                    // 扩展自己建了场景（地面、道具、特效）；它的呈现投影（固定俯角相机下的直立广告牌）由舞台读一次。
                    _projection = _ext.Projection;
                    if (_projection != null)
                    {
                        _projection.FineDepthSort = _orbit != null && _orbit.Enabled;
                    }
                }
                else
                {
                    BuildGround(ctx);
                }
            }

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
        public static double PumpLoader(UnityResourceLoader loader, int maxWaitMs)
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

            /// <summary>精灵视图的整身渲染根（2.5D 演示场景把它摆成直立广告牌；其它情形为 null）。</summary>
            public Transform? LayersRoot;

            /// <summary>视图收到的抬高（世界单位）：2.5D 广告牌沿相机上轴抬，2D 由渲染器自己沿世界 Y 抬。</summary>
            public float HeightUnits;

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
                if (_ext != null && _ctx != null)
                {
                    _ext.OnDisplayBound(entityId, displayId, entityId.Equals(_ctx.PlayerId));
                }

                var view = _unityFactory.CreateView(kind, displayId, entityId);
                if (_options.Interactive)
                {
                    // 试玩：一出场就播待机（否则视图停在静态占位图，直到第一次状态切换才有动画；见 UnityViewFactory.PlayLocomotionClip）。
                    _unityFactory.PlayLocomotionClip(entityId);
                }

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
                        if (_projection != null && _projection.Upright)
                        {
                            entry.LayersRoot = layersRoot;
                            layersRoot.rotation = _projection.Facing(0f);
                        }

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
                                    _ext?.OnSwing(hitEntity);
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

        /// <summary>试玩模式的震屏落地：缺省（非试玩）保持空实现；震屏通道关着时不落地（计数在反馈转发闸里）。</summary>
        private void OnShake()
        {
            if (!_options.Interactive || _unityCamera == null || (_effects != null && !_effects.IsOn(LabEffectFilter.Shake)))
            {
                return;
            }

            // 实验室数据没有震屏档表，固定一个可感知的短震：幅度取取景半高的 3%、0.18 秒、30 Hz 抖动（与闪白同为"默认值"口径，见 FlashReceiver）。
            _unityCamera.Shake(_unityCamera.VisibleHalfHeight * 0.03, 0.18, 30.0);
        }

        /// <summary>试玩模式下实际落到精灵与模型视图上的闪白次数（测试/诊断用）。</summary>
        public int FlashesApplied { get; private set; }

        /// <summary>默认闪白配置行 id（打击反馈档案的闪白动作引用它）；演示导演的闪白没有带配置引用时用它。</summary>
        public static readonly Id DefaultFlashProfile = new Id("vfx.placeholder_hit_flash");

        /// <summary>闪白强度：与精灵管线的默认值同口径（FlashReceiver 的 DefaultIntensity = 1，着色器里颜色过曝到 (1 + 强度) 倍，不是整块填白）。</summary>
        public const double FlashAmount = 1.0;

        /// <summary>最近一次落地的闪白所乘的玩家强度系数（测试/诊断用；从未闪白为 1）。</summary>
        public double LastFlashScale { get; private set; } = 1.0;

        /// <summary>最近一次落到模型视图上的闪白时长（秒，取自闪白配置行的 lifetime；测试用来核对"时长跟随数据"）。</summary>
        public double LastModelFlashSeconds { get; private set; }

        private double FlashSeconds(Id profileId)
        {
            var registry = _ctx?.World.Registry;
            if (registry != null)
            {
                foreach (var row in registry.GetAll("vfx.def"))
                {
                    if (row.GetId("id").Equals(profileId) && row.TryGetNumber("lifetime", out var lifetime) && lifetime > 0.0)
                    {
                        return lifetime;
                    }
                }
            }

            return DefaultFlashSeconds;
        }

        private const double DefaultFlashSeconds = 0.15;

        /// <summary>每个精灵单位当前渲染的精灵名（资源加载器给的名字是 <c>&lt;资源引用&gt;_frame&lt;序号&gt;</c>）；测试用来核对"画出来的是演示美术、不是占位"。</summary>
        public List<KeyValuePair<Id, string>> RenderedSprites()
        {
            var list = new List<KeyValuePair<Id, string>>();
            if (_r2d == null)
            {
                return list;
            }

            foreach (var pair in _entries)
            {
                if (!(pair.Value.View is UnitySpriteView sv))
                {
                    continue;
                }

                var layers = _r2d.GetLayersRoot(sv.EngineHandle);
                if (layers == null)
                {
                    continue;
                }

                foreach (var r in layers.GetComponentsInChildren<SpriteRenderer>(false))
                {
                    if (r.enabled && r.sprite != null)
                    {
                        list.Add(new KeyValuePair<Id, string>(pair.Key, r.sprite.name));
                    }
                }
            }

            return list;
        }

        /// <summary>某个精灵单位已显示方向的槽位与镜像（测试/诊断用）；不是精灵视图时为 null。</summary>
        public (string Slot, bool FlipX)? DisplayedDirectionOf(Id entity)
        {
            if (_entries.TryGetValue(entity, out var entry) && entry.View is UnitySpriteView sv)
            {
                var d = sv.DisplayedDirection;
                return (d.SlotId.Value, d.FlipX);
            }

            return null;
        }

        /// <summary>每个精灵单位当前渲染的精灵名与水平翻转（测试用：核对敌人选用的方向视图与镜像）。</summary>
        public List<(Id Entity, string Sprite, bool FlipX)> RenderedSpriteFlips()
        {
            var list = new List<(Id Entity, string Sprite, bool FlipX)>();
            if (_r2d == null)
            {
                return list;
            }

            foreach (var pair in _entries)
            {
                if (!(pair.Value.View is UnitySpriteView sv))
                {
                    continue;
                }

                var layers = _r2d.GetLayersRoot(sv.EngineHandle);
                if (layers == null)
                {
                    continue;
                }

                foreach (var r in layers.GetComponentsInChildren<SpriteRenderer>(false))
                {
                    if (r.enabled && r.sprite != null)
                    {
                        list.Add((pair.Key, r.sprite.name, r.flipX));
                    }
                }
            }

            return list;
        }

        /// <summary>每个模型单位当前的锚点根与是否仍是占位模型（测试用：核对每个实体恰有一个可见模型、没有回退到占位）。</summary>
        public List<(Id Entity, GameObject Root, bool Placeholder)> RenderedModels()
        {
            var list = new List<(Id Entity, GameObject Root, bool Placeholder)>();
            if (_r3d == null)
            {
                return list;
            }

            foreach (var pair in _entries)
            {
                if (!(pair.Value.View is UnityModelView mv))
                {
                    continue;
                }

                var root = _r3d.GetModelRoot(mv.EngineHandle);
                if (root != null)
                {
                    list.Add((pair.Key, root, _r3d.IsShowingPlaceholder(mv.EngineHandle)));
                }
            }

            return list;
        }

        /// <summary>扩展触发的受击闪白（交给扩展的场景上下文）：同样受试玩面板的"闪白"效果开关管（关 = 不闪）。</summary>
        private void FlashEntity(Id entityId)
        {
            if (_effects == null || _effects.IsOn(LabEffectFilter.Flash))
            {
                OnFlash(entityId);
            }
        }

        private void OnFlash(Id entityId) => OnFlash(entityId, DefaultFlashProfile);

        private void OnFlash(Id entityId, Id profileId)
        {
            if (!_options.Interactive || !_entries.TryGetValue(entityId, out var entry))
            {
                return;
            }

            // 玩家闪白强度（ADR-0148，与 FlashReceiver 同一条规则）：系数 0 = 关闭闪白（光敏类无障碍），不落地、不计数；否则强度 = 默认强度 × 系数。
            var flashScale = _options.FlashIntensityScale != null ? _options.FlashIntensityScale() : 1.0;
            if (!(flashScale > 0))
            {
                return;
            }

            LastFlashScale = flashScale;
            if (entry.View is UnityModelView model && _r3d != null)
            {
                // 模型型外形（ADR-0158）：经框架 3D 渲染器的命名材质参数广播闪白（IRenderer3D.SetMaterialParam，与精灵的 flash_intensity 同名同义），
                // 由模型包着色器消费（过曝到 (1 + 强度) 倍，与精灵一致，保留明暗）；强度与精灵同为 FlashAmount，时长取闪白配置行（vfx.def 的 lifetime），
                // 到时复原为 0（阶跃，与精灵的闪白曲线一致）。
                var handle = model.EngineHandle;
                var renderer3d = _r3d;
                var seconds = FlashSeconds(profileId);
                LastModelFlashSeconds = seconds;
                renderer3d.SetMaterialParam(handle, UnitySpriteView.FlashIntensityShaderParam, FlashAmount * flashScale);
                FlashesApplied++;
                _flashFx.Add(new FlashFx { Clear = () => renderer3d.SetMaterialParam(handle, UnitySpriteView.FlashIntensityShaderParam, 0.0), Remaining = seconds });
                return;
            }

            if (!(entry.View is UnitySpriteView sprite))
            {
                return;
            }

            sprite.SetFlash(1.0 * flashScale);
            FlashesApplied++;
            _flashFx.Add(new FlashFx { Clear = sprite.ClearFlash, Remaining = 0.15 });
        }

        /// <summary>
        /// 地面网格与场地阻挡（只在试玩模式建）：一张程序生成的平铺网格精灵铺满场地，阻挡矩形画成深色块；全部放在隔离层、挂在舞台根下，随舞台销毁。
        /// 只用程序生成的占位（无任何美术资源）。
        /// </summary>
        private void BuildGround(LabHostContext ctx)
        {
            if (_root == null)
            {
                return;
            }

            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            var fill = new Color32(34, 38, 46, 255);
            var line = new Color32(52, 58, 70, 255);
            var axis = new Color32(70, 80, 98, 255);
            for (var y = 0; y < 64; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    var edge = x == 0 || y == 0;
                    tex.SetPixel(x, y, edge ? line : fill);
                }
            }

            tex.Apply(false);
            var sprite = Sprite.Create(tex, new UnityEngine.Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 64f, 0, SpriteMeshType.FullRect);
            var go = new GameObject("LabGround") { layer = _options.IsolationLayer };
            go.transform.SetParent(_root.transform, false);
            go.transform.position = new Vector3(0f, 0f, 0.05f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.size = new Vector2(60f, 60f);
            renderer.sortingOrder = -10000;

            // 世界原点十字（x 轴偏亮）：方便判断朝向与位移。
            var tex2 = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex2.SetPixel(0, 0, axis);
            tex2.Apply(false);
            var dot = Sprite.Create(tex2, new UnityEngine.Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
            AddBar(go.transform, dot, new Vector2(60f, 0.06f), -9999);
            AddBar(go.transform, dot, new Vector2(0.06f, 60f), -9999);

            try
            {
                var arena = new LabCatalog(ctx.World.Registry).GetArena(ctx.Cell.ArenaId);
                var wall = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                wall.SetPixel(0, 0, new Color32(18, 20, 26, 255));
                wall.Apply(false);
                var wallSprite = Sprite.Create(wall, new UnityEngine.Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
                foreach (var block in arena.Blocks)
                {
                    var size = new Vector2((float)(block.Max.X - block.Min.X), (float)(block.Max.Y - block.Min.Y));
                    var center = new Vector2((float)((block.Max.X + block.Min.X) * 0.5), (float)((block.Max.Y + block.Min.Y) * 0.5));
                    var bar = AddBar(go.transform, wallSprite, size, -9990);
                    bar.transform.localPosition = new Vector3(center.x, center.y, -0.01f);
                }
            }
            catch (Exception ex)
            {
                Fail("场地阻挡绘制失败：" + ex.Message);
            }

            SetLayerRecursive();
        }

        private GameObject AddBar(Transform parent, Sprite sprite, Vector2 size, int order)
        {
            var bar = new GameObject("LabGroundBar") { layer = _options.IsolationLayer };
            bar.transform.SetParent(parent, false);
            var r = bar.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.drawMode = SpriteDrawMode.Sliced;
            r.size = size;
            r.sortingOrder = order;
            return bar;
        }

        /// <summary>
        /// 反馈转发闸（只在传入了 <see cref="LabEffectFilter"/> 时存在）：记每个通道的提交次数，按开关放行或拦下音效、闪白、镜头冲击；
        /// 震屏的开关在落地处（<see cref="OnShake"/>）判，因为打击反馈包里的震屏档随镜头冲击提示一起走。其余通道原样转发。
        /// </summary>
        private sealed class GatedSink : IFeedbackSink
        {
            private readonly IFeedbackSink _inner;
            private readonly LabEffectFilter _filter;
            private readonly Func<int> _tick;

            public GatedSink(IFeedbackSink inner, LabEffectFilter filter, Func<int> tick)
            {
                _inner = inner;
                _filter = filter;
                _tick = tick;
            }

            public void FloatingText(Id entityId, Id styleId, string text) => _inner.FloatingText(entityId, styleId, text);

            public void PlayVfx(Id vfxId, FeedbackAttachSpec attach)
            {
                _filter.Admit("vfx", _tick());
                _inner.PlayVfx(vfxId, attach);
            }

            public void PlayVfx(Id vfxId, FeedbackAttachSpec attach, IReadOnlyDictionary<string, double>? parameters)
            {
                _filter.Admit("vfx", _tick());
                _inner.PlayVfx(vfxId, attach, parameters);
            }

            public void StopVfx(Id vfxId, FeedbackAttachSpec attach) => _inner.StopVfx(vfxId, attach);

            public void PlaySfx(Id sfxId, Vec2? at)
            {
                if (_filter.Admit(LabEffectFilter.Sfx, _tick()))
                {
                    _inner.PlaySfx(sfxId, at);
                }
            }

            public void PlaySfx(Id sfxId, Vec2? at, FeedbackAttachSpec attach)
            {
                if (_filter.Admit(LabEffectFilter.Sfx, _tick()))
                {
                    _inner.PlaySfx(sfxId, at, attach);
                }
            }

            public void StopSfx(Id sfxId, FeedbackAttachSpec attach) => _inner.StopSfx(sfxId, attach);

            public void Freeze(double durationMs) => _inner.Freeze(durationMs);

            public void ShakeCamera(Id profileId)
            {
                _filter.Admit(LabEffectFilter.Shake, _tick());
                _inner.ShakeCamera(profileId);
            }

            public void Flash(Id entityId, Id profileId)
            {
                if (_filter.Admit(LabEffectFilter.Flash, _tick()))
                {
                    _inner.Flash(entityId, profileId);
                }
            }

            public void ImpactCamera(ImpactCameraCue cue)
            {
                var on = _filter.Admit(LabEffectFilter.CameraImpulse, _tick());
                if (cue.ShakeProfileId.HasValue)
                {
                    _filter.Admit(LabEffectFilter.Shake, _tick());
                }

                // 关着镜头冲击时把幅度置零再转发：组合器只在幅度为正时落地冲击，震屏档照常交给震屏通道判断。
                _inner.ImpactCamera(on
                    ? cue
                    : new ImpactCameraCue(cue.Direction, 0.0, cue.UncappedMagnitude, cue.DecayMs, cue.DurationTicks, cue.ShakeProfileId, cue.HitCount));
            }

            public void FreezePresentation(IReadOnlyList<Id> unitIds, int ticks, ImpactFreezeLayers layers)
            {
                _filter.Admit("freeze", _tick());
                _inner.FreezePresentation(unitIds, ticks, layers);
            }

            public void ReleasePresentation(IReadOnlyList<Id> unitIds) => _inner.ReleasePresentation(unitIds);

            public bool HasPendingPlayback => _inner.HasPendingPlayback;

            public event Action? PendingPlaybackChanged
            {
                add { _inner.PendingPlaybackChanged += value; }
                remove { _inner.PendingPlaybackChanged -= value; }
            }
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
                        _rec.ClipTransitionSeconds.Add(_simNow);
                    }
                }
            }

            _r2d?.StepEffects();
            _vfx?.Update(dt);
            _ext?.OnStep(dt);
            _sfx?.Update(dt);
            _audio?.Tick(dt);
            _r3d?.Tick();
            _loader?.Tick();

            if (_options.Interactive)
            {
                for (var i = _flashFx.Count - 1; i >= 0; i--)
                {
                    var fx = _flashFx[i];
                    fx.Remaining -= dt;
                    if (fx.Remaining <= 0.0)
                    {
                        try
                        {
                            fx.Clear();
                        }
                        catch (Exception)
                        {
                            // 视图可能已销毁（靶子被清掉）；闪白只是呈现，忽略。
                        }

                        _flashFx.RemoveAt(i);
                    }
                }

                if (_ctx != null && _unityCamera != null)
                {
                    ApplyTemplateFraming();
                    _unityCamera.Follow(_ctx.World.Player.Position, _followSmoothing);
                }
            }

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

            ApplyBillboards();
            SampleGpu();

            foreach (var pair in _entries)
            {
                var player = pair.Value.Player;
                var clip = player?.CurrentClipId;
                if (clip.HasValue && (!pair.Value.LastClip.HasValue || !pair.Value.LastClip.Value.Equals(clip.Value)))
                {
                    pair.Value.LastClip = clip;
                    _rec.ClipTransitions.Add(Label(pair.Key) + ":" + clip.Value.Value);
                    _rec.ClipTransitionSeconds.Add(_simNow);
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

        /// <summary>
        /// 2.5D 演示场景的广告牌朝向（ADR-0157）：相机推进之后，把每个单位的整身渲染根与场景道具转到与相机平面平行（脚底枢轴留在地面点上，
        /// 视图收到的抬高沿相机上轴抬）。读的是相机的实时姿态，模板预设改俯角后下一帧自动跟上。阴影挂在单位根下、不在渲染根里，仍躺在地面。
        /// 平面模式（2D）不调用。只写引擎侧物体，不碰逻辑。
        /// </summary>
        private void ApplyBillboards()
        {
            if (_projection == null || !_projection.Upright)
            {
                return;
            }

            var up = _projection.Up;
            foreach (var pair in _entries)
            {
                var layers = pair.Value.LayersRoot;
                if (layers == null)
                {
                    continue;
                }

                var root = layers.parent;
                layers.rotation = _projection.Facing(root != null ? root.eulerAngles.z : 0f);
                layers.position = (root != null ? root.position : Vector3.zero) + up * pair.Value.HeightUnits;
            }

            _ext?.OnBillboardsApplied();
        }

        /// <summary>记下视图收到的抬高（SyncPose 的高度参数按渲染器的像素密度折成世界单位）；只有 2.5D 广告牌用它。</summary>
        internal void NoteHeight(Id entity, double height)
        {
            if (_entries.TryGetValue(entity, out var entry) && entry.LayersRoot != null && _r2d != null)
            {
                entry.HeightUnits = (float)(height / Math.Max(_r2d.PixelsPerUnit, 0.0001));
            }
        }

        /// <summary>某个精灵单位的整身渲染根（测试用：2.5D 下应与舞台相机平行、脚底落在单位的地面点上）；没有则为 null。</summary>
        public Transform? LayersRootOf(Id entity) =>
            _entries.TryGetValue(entity, out var entry) ? entry.LayersRoot : null;

        // ───────── 命中对齐 ─────────

        internal const string ReleaseMarker = "release";

        /// <summary>模型 rig 经命中帧注册表到达的事件（精灵 rig 由自己的关键帧订阅直接入队，避免同一个命中帧重复计数）。</summary>
        private void OnEngineHitFrame(Id entity)
        {
            if (_entries.TryGetValue(entity, out var entry) && entry.Player != null)
            {
                return;
            }

            // 模型型外形（ADR-0158）：出手动画的命中帧关键帧到达就是挥砍拖影的出手点（精灵一侧在自己的关键帧回调里做同一件事）。
            _ext?.OnSwing(entity);
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
                _ext?.Dispose();
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
                var engineFacing = _stage._ext != null ? _stage._ext.FacingFor(_recording.EntityId, pos, facing) : facing;
                Guard("SyncPose", () => _engine.SyncPose(pos, engineFacing, height));
                _stage.NoteHeight(_recording.EntityId, height);
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

        // ───────── 武器风格（施法剪辑覆盖） ─────────

        /// <summary>
        /// 实验室的武器风格来源：数据集里有 <c>display.weapon_style.lab_&lt;外形&gt;</c> 行（<c>lab_sprite</c> / <c>lab_model</c>，值前缀随外形不同）时，
        /// 所有实体都套用它；没有这一行（数据集不声明）时返回 null，剪辑解析退回默认剪辑表，与没有武器风格来源时逐位一致。
        /// 该行只声明 <c>cast_anim_override</c>（按技能 id 把命中点在 100 / 300 ms 的实验室技能指向 <c>cast.quick</c> / <c>cast.heavy</c> 剪辑，
        /// 让引擎侧释放事件对上逻辑命中标记）；实验室没有普攻挥击事件，<c>auto_attack_anim</c> 只是必填字段，不会被播放。
        /// </summary>
        private sealed class LabWeaponStyleSource : IWeaponStyleSource
        {
            private readonly Id? _row;

            public LabWeaponStyleSource(Core.Foundation.DataRegistry.IDataRegistryView registry, string form, string prefix = "lab_")
            {
                var id = new Id("display.weapon_style." + prefix + (string.Equals(form, "model", StringComparison.Ordinal) ? "model" : "sprite"));
                _row = registry.Get("display.weapon_style", id) != null ? id : (Id?)null;
            }

            public Id? GetWeaponStyleRef(Id entityId) => _row;
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
