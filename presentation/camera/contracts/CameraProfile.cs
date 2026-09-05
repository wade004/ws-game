using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

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

        /// <summary>
        /// 从一条已加载的 <c>camera_profile</c> <see cref="DataRecord"/> 构造（契约缺口补齐，见模块
        /// README"契约缺口"一节、<c>schema/CameraSchemas.cs</c>）。字段解析惯例与
        /// <c>Core.Foundation.DisplayInfo.DisplayInfo.FromRecord</c> 一致：必填字段用 <c>GetXxx</c>
        /// （缺失时 <see cref="DataFieldException"/>），可选字段用 <c>TryGetXxx</c>。<c>bounds</c>/
        /// <c>shake_presets</c> 是嵌套 JSON 结构，04 未规定专用访问器，按 <c>DisplayInfo.FromRecord</c>
        /// 解析 <c>mirror_pairs</c>/<c>anchor_points</c> 的同一惯例手动展开 <see cref="JsonObject"/>/
        /// <see cref="JsonArray"/>。
        /// </summary>
        public static CameraProfile FromRecord(DataRecord record)
        {
            var id = record.GetId("id");
            var pitchDegrees = record.GetNumber("pitch_degrees");
            var yawDegrees = record.GetNumber("yaw_degrees");
            var zoomMin = record.GetNumber("zoom_min");
            var zoomMax = record.GetNumber("zoom_max");
            var zoomDefault = record.GetNumber("zoom_default");
            var followLerp = record.GetNumber("follow_lerp");

            CameraBounds? bounds = null;
            if (record.TryGetObject("bounds", out var boundsObj))
            {
                var min = ParseVec2Property(record, "bounds", boundsObj, "min");
                var max = ParseVec2Property(record, "bounds", boundsObj, "max");
                bounds = new CameraBounds(min, max);
            }

            IReadOnlyList<ShakePreset>? shakePresets = null;
            if (record.TryGetArray("shake_presets", out var presetsArr))
            {
                var list = new List<ShakePreset>(presetsArr.Count);
                for (var i = 0; i < presetsArr.Count; i++)
                {
                    if (!(presetsArr[i] is JsonObject presetObj))
                    {
                        throw new DataFieldException(record.Table.Name, record.Key, "shake_presets", $"第 {i} 个元素不是对象");
                    }

                    var presetId = ParseIdProperty(record, "shake_presets", presetObj, "id");
                    var amplitude = ParseNumberProperty(record, "shake_presets", presetObj, "amplitude");
                    var duration = ParseNumberProperty(record, "shake_presets", presetObj, "duration");
                    var frequency = presetObj.TryGetValue("frequency", out var freqVal) && freqVal is JsonNumber freqNum
                        ? freqNum.Value
                        : 0.0;

                    list.Add(new ShakePreset(presetId, amplitude, duration, frequency));
                }
                shakePresets = list;
            }

            return new CameraProfile(
                id, pitchDegrees, yawDegrees, zoomMin, zoomMax, zoomDefault, followLerp, bounds, shakePresets);
        }

        private static Id ParseIdProperty(DataRecord record, string field, JsonObject obj, string property)
        {
            if (!obj.TryGetValue(property, out var value) || !(value is JsonString s) || !Id.TryParse(s.Value, out var id))
            {
                throw new DataFieldException(record.Table.Name, record.Key, field, $"元素缺少合法 Id 属性 \"{property}\"");
            }
            return id;
        }

        private static double ParseNumberProperty(DataRecord record, string field, JsonObject obj, string property)
        {
            if (!obj.TryGetValue(property, out var value) || !(value is JsonNumber n))
            {
                throw new DataFieldException(record.Table.Name, record.Key, field, $"元素缺少合法 Number 属性 \"{property}\"");
            }
            return n.Value;
        }

        private static Vec2 ParseVec2Property(DataRecord record, string field, JsonObject obj, string property)
        {
            if (obj.TryGetValue(property, out var value) && value is JsonObject o
                && o.TryGetValue("x", out var xv) && xv is JsonNumber xn
                && o.TryGetValue("y", out var yv) && yv is JsonNumber yn)
            {
                return new Vec2(xn.Value, yn.Value);
            }
            throw new DataFieldException(record.Table.Name, record.Key, field, $"元素缺少合法 Vec2 属性 \"{property}\"（期望 {{\"x\": Number, \"y\": Number}}）");
        }
    }
}
