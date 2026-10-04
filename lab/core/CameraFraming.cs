using System;
using System.Linq;
using Core.Foundation.DataRegistry;

namespace Lab
{
    /// <summary>
    /// 模板镜头取景（ADR-0150 已知限制修复，手感设计 06 第 4 节人手试玩）：基础预设是默认手感模板（<c>feel.preset.tpl_*</c>）时，
    /// 取同名模板的 <c>camera_profile.tpl_*</c> 行（俯仰、缩放默认值、跟随平滑），使试玩的取景随模板变化；其它预设没有对应行（<see cref="Found"/> 为假），
    /// 舞台沿用自己的缺省取景。纯数据读取，不含引擎类型。
    /// <para>
    /// 判断记录（缩放按相对值取）：占位精灵是模板无关的，模板里的 <c>zoom_default</c>（9～13）是真实相机的取景尺寸；试玩舞台自有的缺省取景是为占位精灵调过的，
    /// 所以舞台把它乘上 <c>zoom_default / <see cref="ReferenceZoom"/></c>（参照模板 classic 的 10）——模板之间的相对取景差异保留，缺省取景的尺度不变。
    /// </para>
    /// </summary>
    public sealed class CameraFraming
    {
        /// <summary>参照缩放（classic 模板的缺省缩放，相对缩放的分母）。</summary>
        public const double ReferenceZoom = 10.0;

        public const string PresetPrefix = "feel.preset.tpl_";
        public const string ProfilePrefix = "camera_profile.tpl_";

        /// <summary>是否找到对应的镜头配置行。</summary>
        public bool Found { get; }

        public string ProfileId { get; }

        public double PitchDegrees { get; }

        public double YawDegrees { get; }

        public double ZoomDefault { get; }

        public double FollowLerp { get; }

        /// <summary>相对缩放系数（<see cref="ZoomDefault"/> / <see cref="ReferenceZoom"/>；没找到为 1）。</summary>
        public double ZoomRatio => Found ? ZoomDefault / ReferenceZoom : 1.0;

        private CameraFraming(bool found, string profileId, double pitch, double yaw, double zoom, double follow)
        {
            Found = found;
            ProfileId = profileId;
            PitchDegrees = pitch;
            YawDegrees = yaw;
            ZoomDefault = zoom;
            FollowLerp = follow;
        }

        public static readonly CameraFraming None = new CameraFraming(false, string.Empty, 0.0, 0.0, ReferenceZoom, 0.0);

        /// <summary>预设 id 对应的镜头配置行 id（非模板预设返回空串）。</summary>
        public static string ProfileIdFor(string presetId) =>
            presetId.StartsWith(PresetPrefix, StringComparison.Ordinal) ? ProfilePrefix + presetId.Substring(PresetPrefix.Length) : string.Empty;

        public static CameraFraming For(IDataRegistryView registry, string presetId)
        {
            var profileId = ProfileIdFor(presetId ?? string.Empty);
            if (profileId.Length == 0 || !registry.Tables.Contains("camera_profile"))
            {
                return None;
            }

            var record = registry.Get("camera_profile", profileId);
            if (record == null)
            {
                return None;
            }

            return new CameraFraming(
                true, profileId, record.GetNumber("pitch_degrees"), record.GetNumber("yaw_degrees"),
                record.GetNumber("zoom_default"), record.GetNumber("follow_lerp"));
        }
    }
}
