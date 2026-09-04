using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Presentation.Camera
{
    /// <summary>矩形跟随边界（见 01 L5 模块表 <c>camera</c> 行"策略配置项：跟随策略、边界规则"、
    /// 09 第 3.5 节"边界 Bounds? 夹取"）。<see cref="Clamp"/> 把一个世界平面坐标夹到边界内。</summary>
    public readonly struct CameraBounds
    {
        public Vec2 Min { get; }

        public Vec2 Max { get; }

        public CameraBounds(Vec2 min, Vec2 max)
        {
            if (min.X > max.X || min.Y > max.Y)
            {
                throw new ArgumentException("CameraBounds 的 min 不能大于 max");
            }

            Min = min;
            Max = max;
        }

        public Vec2 Clamp(Vec2 pos) => new Vec2(
            Math.Clamp(pos.X, Min.X, Max.X),
            Math.Clamp(pos.Y, Min.Y, Max.Y));
    }

    /// <summary>震屏档位（见 09 第 3.5 节 <c>shake</c>、<c>camera_profile.shake_presets</c> 字段）。</summary>
    public readonly struct ShakePreset
    {
        public Id Id { get; }

        public double Amplitude { get; }

        public double Duration { get; }

        /// <summary>震屏频率；<c>ICamera.Shake(intensity, durationSeconds)</c>（见 02 第 1.13 节）
        /// 本身没有频率参数，本字段随 <c>camera_profile</c> 表结构一并登记，供未来接口扩展或
        /// 引擎侧按需自行解释，当前 <see cref="Presentation.Camera.CameraHost.Shake"/> 调用
        /// <c>ICamera.Shake</c> 时不传递本字段（见模块 README 契约缺口）。</summary>
        public double Frequency { get; }

        public ShakePreset(Id id, double amplitude, double duration, double frequency)
        {
            Id = id;
            Amplitude = amplitude;
            Duration = duration;
            Frequency = frequency;
        }
    }

    /// <summary>
    /// <c>camera_profile</c> 表一行的强类型表示（见 01 L5 模块表 <c>camera</c> 行"主要数据表：
    /// camera_profile"、09 第 3.5、8 节）。不可变值对象。
    /// </summary>
    public sealed class CameraProfile
    {
        public Id Id { get; }

        public double PitchDegrees { get; }

        public double YawDegrees { get; }

        public double ZoomMin { get; }

        public double ZoomMax { get; }

        public double ZoomDefault { get; }

        /// <summary>跟随平滑系数，传给 <c>ICamera.Follow(planePos, smoothing)</c>。</summary>
        public double FollowLerp { get; }

        public CameraBounds? Bounds { get; }

        public IReadOnlyList<ShakePreset> ShakePresets { get; }

        public CameraProfile(
            Id id,
            double pitchDegrees,
            double yawDegrees,
            double zoomMin,
            double zoomMax,
            double zoomDefault,
            double followLerp,
            CameraBounds? bounds = null,
            IReadOnlyList<ShakePreset>? shakePresets = null)
        {
            if (zoomMin > zoomMax)
            {
                throw new ArgumentException("zoomMin 不能大于 zoomMax");
            }

            if (zoomDefault < zoomMin || zoomDefault > zoomMax)
            {
                throw new ArgumentException("zoomDefault 必须落在 [zoomMin, zoomMax] 范围内");
            }

            Id = id;
            PitchDegrees = pitchDegrees;
            YawDegrees = yawDegrees;
            ZoomMin = zoomMin;
            ZoomMax = zoomMax;
            ZoomDefault = zoomDefault;
            FollowLerp = followLerp;
            Bounds = bounds;
            ShakePresets = shakePresets ?? Array.Empty<ShakePreset>();
        }
    }
}
