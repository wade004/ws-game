#nullable enable
// UnityCamera：ICamera 的 Unity 引擎实现。
//
// 判断记录（坐标系与投影简化）：本框架的既有约定（见 ProjectSetup.cs 把 URP 2D Renderer 的
// Transparency Sort Axis 设为世界 Y 轴）把逻辑世界平面 Vec2(X, Y) 直接映射到 Unity 世界坐标的
// (X, Y)，"height" 经 IRenderer2D.SetTransform 的正式 height 参数（ADR-0016 决策 2，取代此前
// 借用 SetShaderParam 的 height_offset_px 工作绕）转换成纯视觉像素偏移，不是真正的第三根世界
// 坐标轴（IRenderer3D 本迭代整体声明降级，见该类型注释，因此也不存在"3D 模型摆放需要真实高度
// 轴"的真实需求）。据此，本相机保持正交投影、镜头朝向固定沿 -Z 轴看向 XY 平面：
// Configure 的 pitchDegrees/yawDegrees 缺省只记录配置值，不据此旋转相机（缺省行为与引入俯仰与透视之前逐位一致）；
// 偏航、俯仰与透视都是显式打开的可选能力（ApplyYawRotation / ApplyPitch / Perspective，见各属性的判断记录）：
// 声明了才生效，没声明就是原来的正交俯视。WorldToScreen 的
// height 参数按与 IRenderer2D.SetTransform 一致的换算方向，直接作为世界 Y 方向的附加偏移量
// （等效于"抬高的物体在画面上更靠上"）。
// zoom 直接映射为正交相机的 orthographicSize（世界单位可视半高），zoomRange 即其合法区间；透视模式下 zoom 仍是"焦点处地面的可视半高"
// （相机距离 = zoom / tan(视场角/2)），所以按画面高度比例计的镜头冲击幅度在两种投影下语义一致。
//
// 判断记录（俯仰与透视，M4-W4；取代此前"2D 渲染管线不支持透视俯角、fixed_pitch 未实现"的缺口）：
//   - 约定：俯仰角 pitchDegrees 以"正俯视 = 0"为基准（视线沿 +Z 垂直看向世界平面 XY），增大则相机向后倾（相机在焦点的 -Y 侧上方，
//     视线朝 +Y 方向压低），范围夹在 [0, 89]；俯仰只改相机姿态与位置，偏航仍是绕世界 Z 轴的逆时针角度，最终姿态 = Rz(偏航) * Rx(-俯仰)。
//     相机右轴恒在世界平面上（(cos yaw, sin yaw)，俯仰绕右轴转）；"屏幕上方"在世界平面上的投影方向恒为 (-sin yaw, cos yaw)，
//     所以相机相对输入只需要偏航（见 ICameraOrientation），俯仰只把世界平面在屏幕上沿"上"方向压扁 cos(俯仰) 倍。
//   - 渲染物仍然躺在世界平面（XY，Z = 0）上：精灵、特效是平面四边形，俯仰相机看到的是它们在地面上的透视投影（近大远小、纵向压扁），
//     不做 billboard（让精灵朝向相机站起来）——那是渲染层的取舍，不在相机里偷偷做。
//   - ScreenToWorld 对任意姿态都是"射线与 Z = 0 平面求交"；射线与平面平行（指向天空）或在相机身后返回 null，与契约一致。
//
// 判断记录（镜头冲击 ICameraImpulse，手感落地 M2-A，手感设计/07 第 2 节、05 第 7 节）：本类型同时实现可选能力接口
// ICameraImpulse 且恒声明支持，表现层 CameraHost.Impulse 因此直接转发，不再退化为 Shake。
//   - 幅度单位：magnitude 是"画面高度比例"，实际位移 = magnitude × 画面可视高度（2 × orthographicSize，按触发那一刻的缩放换算，
//     不跨投影直接复用世界距离）。
//   - 方向与衰减：镜头沿 direction（世界平面单位方向）被推开，位移从峰值线性衰减回零，历时 decayMs；多次冲击的位移按向量相加
//     （合并、限频、上限截断由反馈包流水线负责，本类型不二次处理）；零方向表示无方向，取各向同性——按 Perlin 噪声采样的二维偏移，
//     幅度同样线性衰减（同 Shake 的噪声做法，纯表现、不回流逻辑层）。
//   - 与 Shake 独立叠加：冲击位移单独维护（_impulseOffset），每帧最终写回 Transform 的值是
//     _basePosition + _shakeOffset + _impulseOffset，不污染跟随基准位置（同 _basePosition 判断记录的理由）。
//   - 位移的落地时刻：Tick(dt) 先把全部冲击推进 dt 再求和，所以 Tick(0) 得到的就是刚触发时的峰值位移（测试据此确定性取峰值）。
//
// 判断记录（缩放脉冲 ICameraZoomPunch，ADR-0148，手感设计/07 第 2 节）：本类型同时实现可选能力接口 ICameraZoomPunch 且恒声明支持。
//   - 语义：magnitude 是可视范围收窄的比例（0.04 = 峰值时可视半高缩到 96%），从峰值线性衰减回零，历时 decayMs；多次脉冲按比例相加，
//     合计收窄不超过 50%（防止叠加后画面塌缩）。纯表现、不回流逻辑层。
//   - 与缩放的关系：SetZoom 的缩放值仍是"基准缩放"（CurrentZoom 不变），脉冲只乘一个 [0.5, 1] 的收窄因子得到实际可视半高
//     （EffectiveZoom）；脉冲结束后因子恢复 1，实际可视半高逐位回到基准缩放。镜头冲击的"画面高度比例"幅度按基准缩放换算
//     （VisibleHalfHeight 不含脉冲），所以同时发生的冲击位移不被脉冲改变。
//   - 不影响 shake_cap：缩放脉冲是缩放量、不是位移，不计入相机侧的合成位移幅度（CameraHost 判断记录）。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityCamera : ICamera, ICameraImpulse, ICameraZoomPunch, ICameraOrientation
    {
        private struct ImpulseState
        {
            public Vector2 Direction;
            public bool Isotropic;
            public float PeakWorld;
            public double DurationSeconds;
            public double Elapsed;
        }

        /// <summary>零方向冲击的各向同性噪声采样频率（次/秒）。</summary>
        private const double ImpulseIsotropicFrequency = 30.0;
        private const float ImpulseNoiseSeedX = 71.3f;
        private const float ImpulseNoiseSeedY = 113.9f;

        private struct ZoomPunchState
        {
            public double Magnitude;
            public double DurationSeconds;
            public double Elapsed;
        }

        /// <summary>缩放脉冲叠加后的最大合计收窄比例。</summary>
        private const double MaxZoomPunchTotal = 0.5;

        private readonly List<ZoomPunchState> _zoomPunches = new List<ZoomPunchState>();
        private double _zoomPunchScale = 1.0;
        private readonly List<ImpulseState> _impulses = new List<ImpulseState>();
        private Vector3 _impulseOffset;
        private readonly Camera _camera;

        private double _pitchDegrees;
        private double _yawDegrees;
        private ZoomRange _zoomRange = new ZoomRange(1, 1);
        private double _zoom = 5.0;

        private bool _following;
        private Vec2 _followTarget;
        private double _followSmoothing;

        private bool _shaking;
        private double _shakeIntensity;
        private double _shakeDuration;
        private double _shakeFrequency;
        private double _shakeElapsed;
        private Vector3 _shakeOffset;

        /// <summary>Perlin 噪声在 X/Y 两个方向的采样偏移量，避免两个方向用同一条噪声曲线导致抖动
        /// 轨迹退化成一条直线（见 <see cref="Tick"/> 判断记录）。</summary>
        private const float ShakeNoiseSeedX = 0f;
        private const float ShakeNoiseSeedY = 37.1f;

        /// <summary>不含震屏偏移的"真实"相机位置。判断记录：Tick 不能每帧从
        /// <c>_camera.transform.position</c> 反推基准位置——那个值在上一帧已经叠加过震屏偏移，
        /// 如果再拿它当基准会把上一帧的抖动"焼"进下一帧的基准里，导致震屏结束后相机停在
        /// 偏移后的位置回不去（曾经的真实 bug，由 PlayMode 测试
        /// UnityCameraTests.Shake_AppliesTemporaryOffset_ThenSettles 捕获）。因此基准位置必须
        /// 单独维护，只由 Follow 的插值结果更新，Shake 只影响 <see cref="_shakeOffset"/>，
        /// 每帧最终写回 Transform 的值永远是 <c>_basePosition + _shakeOffset</c>。</summary>
        private Vector3 _basePosition;

        /// <summary>相机沿 -Z 方向与地面平面（世界 Z = 0，即全部 2D 精灵所在的平面，见类型顶部
        /// 判断记录）保持的固定距离；只影响透视裁剪与 WorldToScreenPoint 的内部计算，不代表任何
        /// 真实的逻辑坐标含义。</summary>
        private const float CameraDistanceFromGroundPlane = 10f;

        public UnityCamera(Camera camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _camera.orthographic = true;
            _camera.orthographicSize = (float)_zoom;
            _camera.transform.rotation = Quaternion.identity;
            _basePosition = new Vector3(0f, 0f, -CameraDistanceFromGroundPlane);
            _camera.transform.position = _basePosition;
        }

        public void Configure(double pitchDegrees, double yawDegrees, ZoomRange zoomRange)
        {
            _pitchDegrees = pitchDegrees;
            _yawDegrees = yawDegrees;
            _zoomRange = zoomRange;
            SetZoom(_zoom); // 重新夹紧到新的 zoomRange
            if (_applyPitch || _perspective)
            {
                RefreshOrientation();
            }
        }

        private bool _applyYawRotation;
        private bool _applyPitch;
        private bool _perspective;
        private double _fieldOfViewDegrees = 40.0;

        /// <summary>透视模式视场角（垂直，度）的合法范围；超出夹紧。</summary>
        private const double MinFieldOfView = 10.0;
        private const double MaxFieldOfView = 120.0;

        /// <summary>俯仰角合法范围上限（度）：0 = 正俯视，上限留出余量避免视线与世界平面平行。</summary>
        private const double MaxPitchDegrees = 89.0;

        /// <summary>
        /// 可选能力（默认关闭，关闭时行为与引入前逐位一致）：打开后 <see cref="Configure"/> 记下的 <c>yawDegrees</c> 会真正作用到相机朝向——
        /// 相机绕视线轴（世界 Z 轴）逆时针转过该角度，使相机的右轴在世界平面上是 (cos yaw, sin yaw)、上轴是 (−sin yaw, cos yaw)。
        /// 相机相对输入（第三人称）只依赖相机在世界平面上的朝向（见 <see cref="YawRadians"/>）。
        /// </summary>
        public bool ApplyYawRotation
        {
            get => _applyYawRotation;
            set
            {
                if (_applyYawRotation == value)
                {
                    return;
                }

                _applyYawRotation = value;
                RefreshOrientation();
            }
        }

        /// <summary>
        /// 可选能力（默认关闭，关闭时行为与引入前逐位一致）：打开后 <see cref="Configure"/> 记下的 <c>pitchDegrees</c> 真正作用到相机姿态
        /// （固定俯角，约定与范围见类型顶部判断记录：0 = 正俯视、夹在 [0, 89]）。相机相对输入不受影响（仍只用偏航），
        /// 世界平面在屏幕上沿"上"方向被压扁 cos(俯仰) 倍。
        /// </summary>
        public bool ApplyPitch
        {
            get => _applyPitch;
            set
            {
                if (_applyPitch == value)
                {
                    return;
                }

                _applyPitch = value;
                RefreshOrientation();
            }
        }

        /// <summary>
        /// 可选能力（默认关闭 = 正交投影，与引入前逐位一致）：打开后改用透视投影，视场角见 <see cref="FieldOfViewDegrees"/>；缩放 <see cref="SetZoom"/>
        /// 仍表示"焦点处地面的可视半高"（相机距离 = zoom / tan(视场角/2)）。可以与 <see cref="ApplyPitch"/> 独立打开（正俯视透视、俯角正交都合法）。
        /// </summary>
        public bool Perspective
        {
            get => _perspective;
            set
            {
                if (_perspective == value)
                {
                    return;
                }

                _perspective = value;
                _camera.orthographic = !value;
                if (value)
                {
                    _camera.fieldOfView = (float)_fieldOfViewDegrees;
                }

                RefreshOrientation();
            }
        }

        /// <summary>透视模式的垂直视场角（度，缺省 40，夹在 [10, 120]）。</summary>
        public double FieldOfViewDegrees
        {
            get => _fieldOfViewDegrees;
            set
            {
                _fieldOfViewDegrees = Math.Max(MinFieldOfView, Math.Min(MaxFieldOfView, value));
                if (_perspective)
                {
                    _camera.fieldOfView = (float)_fieldOfViewDegrees;
                    RefreshOrientation();
                }
            }
        }

        /// <summary>当前生效的俯仰角（度）：没有打开 <see cref="ApplyPitch"/> 为 0，否则是配置值夹进 [0, 89]。</summary>
        public double EffectivePitchDegrees => _applyPitch ? Math.Max(0.0, Math.Min(MaxPitchDegrees, _pitchDegrees)) : 0.0;

        /// <summary>
        /// <see cref="ICameraOrientation.YawRadians"/>：相机在世界平面上的实际偏航（弧度，逆时针为正）。<see cref="ApplyYawRotation"/> 没打开时相机
        /// 物理上没有转，如实报 0（配置的偏航只是记录，不是相机的真实朝向）。
        /// 打开 <see cref="SampleYawAtCommit"/> 后改报"已提交偏航"（<see cref="CommitYaw"/>）：画面偏航可以逐帧平滑转，逻辑侧每次更新取样的偏航只在固定步边界变（ADR-0159）。
        /// </summary>
        public double YawRadians => _sampleYawAtCommit
            ? _committedYawDegrees * Math.PI / 180.0
            : (_applyYawRotation ? _yawDegrees * Math.PI / 180.0 : 0.0);

        private bool _sampleYawAtCommit;
        private double _committedYawDegrees;

        /// <summary>
        /// 可选能力（默认关闭 = <see cref="YawRadians"/> 报相机实时偏航，与引入前逐位一致）：打开后 <see cref="YawRadians"/> 报最近一次 <see cref="CommitYaw"/> 的值，
        /// 与画面上的相机偏航（<see cref="SetView"/>/<see cref="Configure"/>，可以逐帧平滑）脱钩。判断记录（ADR-0135 后果"偏航每次更新取样，相机在同一 tick 内的偏航变化要由
        /// 相机实现自己保证与模拟步对齐"）：输入映射每次更新都读 <see cref="YawRadians"/>，宿主在固定步边界（脚本事件应用处）提交偏航，同一个固定步内的所有读数一致，
        /// 一次渲染帧里跑几个固定步也互相一致；提交的偏航由宿主决定怎么来（实验室宿主把它录进脚本，回放逐步复现）。
        /// 打开时已提交偏航取当前实际偏航，不引起读数跳变。
        /// </summary>
        public bool SampleYawAtCommit
        {
            get => _sampleYawAtCommit;
            set
            {
                if (_sampleYawAtCommit == value)
                {
                    return;
                }

                if (value)
                {
                    _committedYawDegrees = _applyYawRotation ? _yawDegrees : 0.0;
                }

                _sampleYawAtCommit = value;
            }
        }

        /// <summary>已提交偏航（度；<see cref="SampleYawAtCommit"/> 关闭时无意义，测试/诊断用）。</summary>
        public double CommittedYawDegrees => _committedYawDegrees;

        /// <summary>提交输入映射取样的偏航（度，逆时针为正，不限范围）；只在 <see cref="SampleYawAtCommit"/> 打开时影响 <see cref="YawRadians"/>，不转动画面。</summary>
        public void CommitYaw(double yawDegrees) => _committedYawDegrees = yawDegrees;

        /// <summary>
        /// 一次设置画面偏航与俯仰（度）并立刻刷新相机姿态；缩放走 <see cref="SetZoom"/>。与 <see cref="Configure"/> 的区别：不碰缩放区间，也不重新夹缩放，
        /// 供每帧驱动的环绕镜头使用。偏航只在 <see cref="ApplyYawRotation"/> 打开时作用到相机，俯仰只在 <see cref="ApplyPitch"/> 打开时作用（俯仰夹在 [0, 89]）。
        /// </summary>
        public void SetView(double yawDegrees, double pitchDegrees)
        {
            _yawDegrees = yawDegrees;
            _pitchDegrees = pitchDegrees;
            RefreshOrientation();
        }

        /// <summary>
        /// 焦点处地面的可视半高（世界单位）：正交为 orthographicSize，透视为 <see cref="SetZoom"/> 的缩放值（相机距离按它与视场角算出）。
        /// 镜头冲击的"画面高度比例"幅度按它换算。
        /// </summary>
        public float VisibleHalfHeight => (float)_zoom;

        /// <summary>相机到焦点的距离：透视由缩放与视场角决定，正交固定 <see cref="CameraDistanceFromGroundPlane"/>。</summary>
        private double CameraDistance =>
            _perspective ? EffectiveZoom / Math.Tan(_fieldOfViewDegrees * Math.PI / 360.0) : CameraDistanceFromGroundPlane;

        /// <summary>含缩放脉冲收窄的实际可视半高（世界单位）；没有脉冲时等于基准缩放 <see cref="CurrentZoom"/>。</summary>
        public double EffectiveZoom => _zoom * _zoomPunchScale;

        /// <summary><see cref="ICameraZoomPunch.SupportsCameraZoomPunch"/>：本实现恒支持（见类型顶部判断记录）。</summary>
        public bool SupportsCameraZoomPunch => true;

        /// <summary>迄今收到的有效缩放脉冲次数（测试/诊断用）。</summary>
        public int ZoomPunchCount { get; private set; }

        /// <summary><see cref="ICameraZoomPunch.ZoomPunch"/>：见类型顶部判断记录。幅度非正或衰减非正的调用忽略（不计数）。</summary>
        public void ZoomPunch(double magnitude, double decayMs)
        {
            if (!(magnitude > 0) || !(decayMs > 0))
            {
                return;
            }

            _zoomPunches.Add(new ZoomPunchState { Magnitude = magnitude, DurationSeconds = decayMs / 1000.0, Elapsed = 0.0 });
            ZoomPunchCount++;
            ApplyZoomPunchScale();
        }

        private void ApplyZoomPunchScale()
        {
            double total = 0;
            for (var i = 0; i < _zoomPunches.Count; i++)
            {
                var punch = _zoomPunches[i];
                total += punch.Magnitude * (1.0 - punch.Elapsed / punch.DurationSeconds);
            }

            var scale = 1.0 - Math.Min(total, MaxZoomPunchTotal);
            if (scale == _zoomPunchScale && !_perspective)
            {
                return;
            }

            _zoomPunchScale = scale;
            _camera.orthographicSize = (float)EffectiveZoom;
        }

        /// <summary>
        /// 按当前开关写相机姿态与位置。俯仰与透视开关都没开时只处理偏航（与引入俯仰之前的行为逐位一致：偏航开关关闭恢复恒等朝向，
        /// 位置按基准位置叠加震屏与冲击偏移）；任一开关打开后按"焦点 − 视线 × 距离"重算位置。
        /// </summary>
        private void RefreshOrientation()
        {
            if (!_applyPitch && !_perspective)
            {
                _camera.transform.rotation = _applyYawRotation ? Quaternion.Euler(0f, 0f, (float)_yawDegrees) : Quaternion.identity;
                return;
            }

            var yaw = _applyYawRotation ? (float)_yawDegrees : 0f;
            var rotation = Quaternion.Euler(0f, 0f, yaw) * Quaternion.Euler(-(float)EffectivePitchDegrees, 0f, 0f);
            _camera.transform.rotation = rotation;
            var forward = rotation * Vector3.forward;
            var focus = new Vector3(
                _basePosition.x + _shakeOffset.x + _impulseOffset.x, _basePosition.y + _shakeOffset.y + _impulseOffset.y, 0f);
            _camera.transform.position = focus - forward * (float)CameraDistance;
        }

        public void Follow(Vec2 planePos, double smoothing)
        {
            _following = true;
            _followTarget = planePos;
            _followSmoothing = smoothing;
        }

        public void SetZoom(double zoom)
        {
            _zoom = Math.Max(_zoomRange.Min, Math.Min(_zoomRange.Max, zoom));
            _camera.orthographicSize = (float)EffectiveZoom;
            if (_perspective)
            {
                RefreshOrientation(); // 透视下缩放 = 改相机距离
            }
        }

        public Vec2 WorldToScreen(Vec2 planePos, double height)
        {
            // 地面平面固定为世界 Z = 0（与 IRenderer2D 的精灵摆放平面一致，见类型顶部判断记录）。
            var worldPoint = new Vector3((float)planePos.X, (float)(planePos.Y + height), 0f);
            var screen = _camera.WorldToScreenPoint(worldPoint);
            return new Vec2(screen.x, screen.y);
        }

        public Vec2? ScreenToWorld(Vec2 screen)
        {
            // 地面平面固定为 z = 0（见类型顶部判断记录：世界平面直接是 Unity XY 平面）。
            var ray = _camera.ScreenPointToRay(new Vector3((float)screen.X, (float)screen.Y, 0));
            if (Mathf.Approximately(ray.direction.z, 0f))
            {
                return null; // 射线与地面平行，找不到交点。
            }

            var t = (0f - ray.origin.z) / ray.direction.z;
            if (t < 0f)
            {
                return null;
            }

            var hit = ray.origin + ray.direction * t;
            return new Vec2(hit.x, hit.y);
        }

        /// <summary>
        /// <paramref name="frequency"/>（ADR-0016 决策 4 新增）控制抖动轨迹的振荡速度：
        /// 用 Perlin 噪声按 <c>elapsed * frequency</c> 采样（见 <see cref="Tick"/>）取代此前的
        /// <see cref="UnityEngine.Random.Range(float,float)"/>逐帧独立采样——后者没有"频率"这个
        /// 概念可以对应（纯白噪声，帧间无相关性），换成 Perlin 噪声后 frequency 越高、抖动轨迹
        /// 振荡越快，越低则越接近缓慢的漂移，这是 <c>frequency</c> 语义在视觉上的直接体现。
        /// </summary>
        public void Shake(double intensity, double durationSeconds, double frequency)
        {
            _shaking = true;
            _shakeIntensity = intensity;
            _shakeDuration = Math.Max(durationSeconds, 0.0001);
            _shakeFrequency = Math.Max(frequency, 0.0001);
            _shakeElapsed = 0;
        }

        /// <summary><see cref="ICameraImpulse.SupportsCameraImpulse"/>：本实现恒支持（见类型顶部判断记录）。</summary>
        public bool SupportsCameraImpulse => true;

        /// <summary>迄今收到的有效冲击次数（幅度为正者；测试/诊断用）。</summary>
        public int ImpulseCount { get; private set; }

        /// <summary>最近一次有效冲击的参数（方向、幅度[画面高度比例]、衰减毫秒）；尚无时为 null（测试/诊断用）。</summary>
        public (Vec2 Direction, double Magnitude, double DecayMs)? LastImpulse { get; private set; }

        /// <summary>当前冲击叠加的世界位移（XY；测试/诊断用，<see cref="Tick"/> 之后更新）。</summary>
        public Vector2 CurrentImpulseOffset => new Vector2(_impulseOffset.x, _impulseOffset.y);

        /// <summary><see cref="ICameraImpulse.Impulse"/>：见类型顶部判断记录。幅度非正或衰减非正的调用忽略（不计数）。</summary>
        public void Impulse(Vec2 direction, double magnitude, double decayMs)
        {
            if (!(magnitude > 0) || !(decayMs > 0))
            {
                return;
            }

            var sqr = direction.X * direction.X + direction.Y * direction.Y;
            var isotropic = !(sqr > 1e-12);
            var dir = isotropic ? Vector2.zero : new Vector2((float)(direction.X / Math.Sqrt(sqr)), (float)(direction.Y / Math.Sqrt(sqr)));
            // 画面可视高度 = 2 × 正交半高（世界单位）；按触发时刻的缩放换算。
            var peakWorld = (float)(magnitude * 2.0 * VisibleHalfHeight);
            _impulses.Add(new ImpulseState
            {
                Direction = dir,
                Isotropic = isotropic,
                PeakWorld = peakWorld,
                DurationSeconds = decayMs / 1000.0,
                Elapsed = 0.0,
            });
            ImpulseCount++;
            LastImpulse = (direction, magnitude, decayMs);
        }

        /// <summary>由 UnityEngineHost.Update 每帧调用：推进跟随平滑与震屏偏移，
        /// 并把最终结果写入相机 Transform。震屏用 Perlin 噪声按 frequency 采样生成偏移
        /// （见 <see cref="Shake"/> 判断记录）——纯表现层抖动，不回流进逻辑层，不违反
        /// "确定性铁律"（见任务书硬性规则 8）。</summary>
        internal void Tick(double deltaSeconds)
        {
            if (_following)
            {
                var target = new Vector3((float)_followTarget.X, (float)_followTarget.Y, _basePosition.z);
                var t = _followSmoothing <= 0
                    ? 1f
                    : 1f - Mathf.Exp((float)(-deltaSeconds / Math.Max(_followSmoothing, 0.0001)));
                _basePosition = Vector3.Lerp(_basePosition, target, t);
            }

            if (_shaking)
            {
                _shakeElapsed += deltaSeconds;
                if (_shakeElapsed >= _shakeDuration)
                {
                    _shaking = false;
                    _shakeOffset = Vector3.zero;
                }
                else
                {
                    var falloff = 1.0 - _shakeElapsed / _shakeDuration;
                    var magnitude = (float)(_shakeIntensity * falloff);
                    var sampleT = (float)(_shakeElapsed * _shakeFrequency);
                    // Mathf.PerlinNoise 返回 [0,1]，映射到 [-magnitude, magnitude]。
                    var noiseX = Mathf.PerlinNoise(ShakeNoiseSeedX, sampleT) * 2f - 1f;
                    var noiseY = Mathf.PerlinNoise(ShakeNoiseSeedY, sampleT) * 2f - 1f;
                    _shakeOffset = new Vector3(noiseX * magnitude, noiseY * magnitude, 0f);
                }
            }

            var impulseSum = Vector3.zero;
            for (var i = _impulses.Count - 1; i >= 0; i--)
            {
                var impulse = _impulses[i];
                impulse.Elapsed += deltaSeconds;
                if (impulse.Elapsed >= impulse.DurationSeconds)
                {
                    _impulses.RemoveAt(i);
                    continue;
                }

                _impulses[i] = impulse;
                var falloff = (float)(1.0 - impulse.Elapsed / impulse.DurationSeconds);
                if (impulse.Isotropic)
                {
                    var sampleT = (float)(impulse.Elapsed * ImpulseIsotropicFrequency);
                    var nx = Mathf.PerlinNoise(ImpulseNoiseSeedX, sampleT) * 2f - 1f;
                    var ny = Mathf.PerlinNoise(ImpulseNoiseSeedY, sampleT) * 2f - 1f;
                    impulseSum += new Vector3(nx, ny, 0f) * (impulse.PeakWorld * falloff);
                }
                else
                {
                    impulseSum += new Vector3(impulse.Direction.x, impulse.Direction.y, 0f) * (impulse.PeakWorld * falloff);
                }
            }

            _impulseOffset = impulseSum;
            if (_zoomPunches.Count > 0)
            {
                for (var i = _zoomPunches.Count - 1; i >= 0; i--)
                {
                    var punch = _zoomPunches[i];
                    punch.Elapsed += deltaSeconds;
                    if (punch.Elapsed >= punch.DurationSeconds)
                    {
                        _zoomPunches.RemoveAt(i);
                    }
                    else
                    {
                        _zoomPunches[i] = punch;
                    }
                }

                ApplyZoomPunchScale();
            }

            if (_applyPitch || _perspective)
            {
                RefreshOrientation();
                return;
            }

            if (_applyYawRotation)
            {
                _camera.transform.rotation = Quaternion.Euler(0f, 0f, (float)_yawDegrees);
            }

            _camera.transform.position = _basePosition + _shakeOffset + _impulseOffset;
        }

        /// <summary>测试/诊断用：当前配置值。</summary>
        public double PitchDegrees => _pitchDegrees;
        public double YawDegrees => _yawDegrees;
        public ZoomRange ZoomRangeValue => _zoomRange;
        public double CurrentZoom => _zoom;
    }
}
