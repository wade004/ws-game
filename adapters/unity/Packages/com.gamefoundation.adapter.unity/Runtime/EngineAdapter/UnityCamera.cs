#nullable enable
// UnityCamera：ICamera 的 Unity 引擎实现。
//
// 判断记录（坐标系与投影简化）：本框架的既有约定（见 presentation/render/core/SpriteViewBase.cs
// 顶部"契约缺口"注释与 ProjectSetup.cs 把 URP 2D Renderer 的 Transparency Sort Axis 设为世界
// Y 轴）把逻辑世界平面 Vec2(X, Y) 直接映射到 Unity 世界坐标的 (X, Y)，"height" 一律通过
// IRenderer2D.SetShaderParam 的 height_offset_px 通道转换成纯视觉像素偏移，不是真正的第三根
// 世界坐标轴（IRenderer3D 本迭代整体声明降级，见该类型注释，因此也不存在"3D 模型摆放需要真实
// 高度轴"的真实需求）。据此，本相机保持正交投影、镜头朝向固定沿 -Z 轴看向 XY 平面：
// Configure 的 pitchDegrees/yawDegrees 只记录配置值（供未来若干接口方法演进为真正透视投影时使用，
// 当前渲染管线选型是 URP 2D Renderer，2D Renderer 不支持真正的透视俯角），不据此旋转相机，
// 避免在 2D 渲染管线上做一个"看起来歪但不产生正确透视效果"的假动作；WorldToScreen 的
// height 参数按与 SpriteViewBase 一致的换算方向，直接作为世界 Y 方向的附加偏移量（等效于
// "抬高的物体在画面上更靠上"），与 IRenderer2D 的 height_offset_px 视觉语义保持一致。
// zoom 直接映射为正交相机的 orthographicSize（世界单位可视半高），zoomRange 即其合法区间。
using System;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityCamera : ICamera
    {
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
        private double _shakeElapsed;
        private Vector3 _shakeOffset;

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

        public void Shake(double intensity, double durationSeconds)
        {
            _shaking = true;
            _shakeIntensity = intensity;
            _shakeDuration = Math.Max(durationSeconds, 0.0001);
            _shakeElapsed = 0;
        }

        /// <summary>由 UnityEngineHost.Update 每帧调用：推进跟随平滑与震屏偏移，
        /// 并把最终结果写入相机 Transform。震屏用 UnityEngine.Random 生成偏移——纯表现层抖动，
        /// 不回流进逻辑层，不违反"确定性铁律"（见任务书硬性规则 8）。</summary>
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
                    _shakeOffset = new Vector3(
                        UnityEngine.Random.Range(-magnitude, magnitude),
                        UnityEngine.Random.Range(-magnitude, magnitude),
                        0f);
                }
            }

            _camera.transform.position = _basePosition + _shakeOffset;
        }

        /// <summary>测试/诊断用：当前配置值。</summary>
        public double PitchDegrees => _pitchDegrees;
        public double YawDegrees => _yawDegrees;
        public ZoomRange ZoomRangeValue => _zoomRange;
        public double CurrentZoom => _zoom;
    }
}
