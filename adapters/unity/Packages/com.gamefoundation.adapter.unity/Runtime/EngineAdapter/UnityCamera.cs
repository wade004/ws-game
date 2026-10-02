#nullable enable
// UnityCamera：ICamera 的 Unity 引擎实现。
//
// 判断记录（坐标系与投影简化）：本框架的既有约定（见 ProjectSetup.cs 把 URP 2D Renderer 的
// Transparency Sort Axis 设为世界 Y 轴）把逻辑世界平面 Vec2(X, Y) 直接映射到 Unity 世界坐标的
// (X, Y)，"height" 经 IRenderer2D.SetTransform 的正式 height 参数（ADR-0016 决策 2，取代此前
// 借用 SetShaderParam 的 height_offset_px 工作绕）转换成纯视觉像素偏移，不是真正的第三根世界
// 坐标轴（IRenderer3D 本迭代整体声明降级，见该类型注释，因此也不存在"3D 模型摆放需要真实高度
// 轴"的真实需求）。据此，本相机保持正交投影、镜头朝向固定沿 -Z 轴看向 XY 平面：
// Configure 的 pitchDegrees/yawDegrees 只记录配置值（供未来若干接口方法演进为真正透视投影时使用，
// 当前渲染管线选型是 URP 2D Renderer，2D Renderer 不支持真正的透视俯角），不据此旋转相机，
// 避免在 2D 渲染管线上做一个"看起来歪但不产生正确透视效果"的假动作；WorldToScreen 的
// height 参数按与 IRenderer2D.SetTransform 一致的换算方向，直接作为世界 Y 方向的附加偏移量
// （等效于"抬高的物体在画面上更靠上"）。
// zoom 直接映射为正交相机的 orthographicSize（世界单位可视半高），zoomRange 即其合法区间。
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
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityCamera : ICamera, ICameraImpulse
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
        }

        private bool _applyYawRotation;

        /// <summary>
        /// 可选能力（默认关闭，关闭时行为与引入前逐位一致）：打开后 <see cref="Configure"/> 记下的 <c>yawDegrees</c> 会真正作用到相机朝向——
        /// 相机绕视线轴（世界 Z 轴）逆时针转过该角度，使相机的右轴在世界平面上是 (cos yaw, sin yaw)、上轴是 (−sin yaw, cos yaw)。
        /// 判断记录（为什么只做偏航）：2D 渲染管线不支持透视俯角（见类型顶部判断记录），本相机仍是正交投影；相机相对输入（第三人称）
        /// 只依赖相机在世界平面上的朝向，偏航足以让实验室宿主用真实相机的轴与屏幕投影去验证"摇杆 × 相机偏航 → 世界方向"。
        /// 带俯角的透视相机（<c>fixed_pitch</c>）本类型仍未实现，是已知缺口。
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
                _camera.transform.rotation = value ? Quaternion.Euler(0f, 0f, (float)_yawDegrees) : Quaternion.identity;
            }
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
            _camera.orthographicSize = (float)_zoom;
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
            var peakWorld = (float)(magnitude * 2.0 * _camera.orthographicSize);
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
