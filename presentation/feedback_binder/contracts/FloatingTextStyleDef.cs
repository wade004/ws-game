using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary><c>feedback.floating_text_style</c> 一条记录的不可变运行期视图（见 09_表现层.md
    /// 第 6.2 节字段表）。</summary>
    public sealed class FloatingTextStyleDef
    {
        public Id Id { get; }

        /// <summary>颜色引用（正常伤害/暴击/治疗/闪避文案各自配色），具体色值由表现资源管理，
        /// 本类型不解释语义。</summary>
        public Id ColorRef { get; }

        public double? SizeScale { get; }

        /// <summary>飘字运动曲线引用（上浮/抖动/聚合等）；可选。</summary>
        public Id? MotionProfile { get; }

        public FloatingTextStyleDef(Id id, Id colorRef, double? sizeScale, Id? motionProfile)
        {
            Id = id;
            ColorRef = colorRef;
            SizeScale = sizeScale;
            MotionProfile = motionProfile;
        }

        public static FloatingTextStyleDef FromRecord(DataRecord record)
        {
            var id = record.GetId("id");
            var colorRef = record.GetId("color_ref");
            var sizeScale = record.TryGetNumber("size_scale", out var sizeVal) ? (double?)sizeVal : null;
            var motionProfile = record.TryGetId("motion_profile", out var motionVal) ? (Id?)motionVal : null;
            return new FloatingTextStyleDef(id, colorRef, sizeScale, motionProfile);
        }
    }
}
