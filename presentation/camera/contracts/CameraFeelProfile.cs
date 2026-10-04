using System;
using Core.Foundation.Feel;

namespace Presentation.Camera
{
    /// <summary>
    /// 镜头手感档案的不可变运行期视图（手感设计/07 第 2 节，十四个呈现型 <c>camera_*</c> 字段）。只经
    /// <see cref="PresentingFeelView"/> 读取（读判定型字段会抛 <see cref="FeelHalfViolationException"/>，手感设计/05 第 5 节），
    /// 因此镜头系统在类型上就读不到判定型字段。
    /// <para>
    /// 单位（手感设计/00 第 6 节）：<c>LookAhead</c>/<c>DeadZone*</c> 是世界距离（身高倍数经标定换算后的绝对值）；
    /// <c>ImpulseGain</c>/<c>ShakeCap</c> 是<b>画面高度比例</b>（取标定前的相对值，<see cref="PresentingFeelView.GetRaw"/>，
    /// 不乘参考镜头高度——比例随缩放由镜头实现自行换算，07 第 2 节末段）；毫秒字段保持毫秒。
    /// </para>
    /// <para>
    /// 判断记录（缺省档案逐位一致）：<see cref="IsFollowNeutral"/>——跟随滞后、前瞻、前瞻滞后、死区宽高、两轴阻尼全为 0——
    /// 时 <see cref="CameraHost"/> 完全旁路本档案的跟随计算，沿用 <see cref="CameraProfile.FollowLerp"/> 直接
    /// <see cref="Core.Foundation.EngineAdapter.ICamera.Follow"/>，逐位等于改动前；<see cref="IsZoomNeutral"/>
    /// （<c>camera_combat_zoom_delta == 1</c>）时不发任何战斗缩放调用。<c>rpg_classic</c> 预设的镜头组字段全部取中性值。
    /// </para>
    /// </summary>
    public sealed class CameraFeelProfile
    {
        public double FollowLagMs { get; }

        public double LookAhead { get; }

        public double LookAheadLagMs { get; }

        public double DeadZoneWidth { get; }

        public double DeadZoneHeight { get; }

        public double DampingXMs { get; }

        public double DampingYMs { get; }

        /// <summary>X 轴实际阻尼毫秒数：<c>damping_x_ms &gt; 0</c> 取它，否则取（已弃用的）<c>follow_lag_ms</c>。阻尼是跟随滞后的唯一权威（ADR-0148）。</summary>
        public double EffectiveDampingXMs => DampingXMs > 0 ? DampingXMs : FollowLagMs;

        /// <summary>Y 轴实际阻尼毫秒数，口径同 <see cref="EffectiveDampingXMs"/>。</summary>
        public double EffectiveDampingYMs => DampingYMs > 0 ? DampingYMs : FollowLagMs;

        public double CombatZoomDelta { get; }

        public double CombatZoomBlendMs { get; }

        /// <summary>镜头冲击基准幅度（画面高度比例）。</summary>
        public double ImpulseGain { get; }

        public double ImpulseMinIntervalMs { get; }

        /// <summary>震屏与冲击叠加后的上限（画面高度比例）。</summary>
        public double ShakeCap { get; }

        /// <summary>距离衰减取值（<c>none</c>、<c>linear:跨度</c> 或曲线 id；旧写法裸 <c>linear</c> 等同 none，见 <see cref="Presentation.Camera.DistanceAttenuation"/>）。</summary>
        public string DistanceAttenuation { get; }

        /// <summary>玩家强度设置项引用；null 表示不受玩家设置影响。</summary>
        public string? UserIntensitySetting { get; }

        public CameraFeelProfile(
            double followLagMs, double lookAhead, double lookAheadLagMs, double deadZoneWidth, double deadZoneHeight,
            double dampingXMs, double dampingYMs, double combatZoomDelta, double combatZoomBlendMs,
            double impulseGain, double impulseMinIntervalMs, double shakeCap, string distanceAttenuation, string? userIntensitySetting)
        {
            if (distanceAttenuation == null) throw new ArgumentNullException(nameof(distanceAttenuation));
            FollowLagMs = NonNegative(followLagMs, nameof(followLagMs));
            LookAhead = NonNegative(lookAhead, nameof(lookAhead));
            LookAheadLagMs = NonNegative(lookAheadLagMs, nameof(lookAheadLagMs));
            DeadZoneWidth = NonNegative(deadZoneWidth, nameof(deadZoneWidth));
            DeadZoneHeight = NonNegative(deadZoneHeight, nameof(deadZoneHeight));
            DampingXMs = NonNegative(dampingXMs, nameof(dampingXMs));
            DampingYMs = NonNegative(dampingYMs, nameof(dampingYMs));
            if (!(combatZoomDelta > 0) || double.IsInfinity(combatZoomDelta))
            {
                throw new ArgumentOutOfRangeException(nameof(combatZoomDelta), "战斗缩放倍率必须是正的有限数");
            }
            CombatZoomDelta = combatZoomDelta;
            CombatZoomBlendMs = NonNegative(combatZoomBlendMs, nameof(combatZoomBlendMs));
            ImpulseGain = NonNegative(impulseGain, nameof(impulseGain));
            ImpulseMinIntervalMs = NonNegative(impulseMinIntervalMs, nameof(impulseMinIntervalMs));
            ShakeCap = NonNegative(shakeCap, nameof(shakeCap));
            DistanceAttenuation = distanceAttenuation;
            UserIntensitySetting = userIntensitySetting;
        }

        private static double NonNegative(double value, string name)
        {
            if (!(value >= 0) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(name, "必须是非负有限数");
            }
            return value;
        }

        /// <summary>中性档案（不改变任何镜头行为）。</summary>
        public static CameraFeelProfile Neutral { get; } =
            new CameraFeelProfile(0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, Presentation.Camera.DistanceAttenuation.None, null);

        /// <summary>跟随相关字段全为中性：<see cref="CameraHost"/> 旁路档案跟随计算。</summary>
        public bool IsFollowNeutral =>
            FollowLagMs == 0 && LookAhead == 0 && LookAheadLagMs == 0 && DeadZoneWidth == 0 && DeadZoneHeight == 0
            && DampingXMs == 0 && DampingYMs == 0;

        /// <summary>战斗缩放中性（倍率 1）：不发战斗缩放调用。</summary>
        public bool IsZoomNeutral => CombatZoomDelta == 1.0;

        /// <summary>从呈现型手感视图读取。单位换算见类型注释。</summary>
        public static CameraFeelProfile FromPresenting(PresentingFeelView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));

            string? setting = null;
            var settingValue = view.GetAbsolute(FeelFieldNames.CameraUserIntensitySetting);
            if (settingValue.Kind != FeelValueKind.None)
            {
                setting = settingValue.AsText();
            }

            return new CameraFeelProfile(
                view.GetNumber(FeelFieldNames.CameraFollowLagMs),
                view.GetNumber(FeelFieldNames.CameraLookAhead),
                view.GetNumber(FeelFieldNames.CameraLookAheadLagMs),
                view.GetNumber(FeelFieldNames.CameraDeadZoneWidth),
                view.GetNumber(FeelFieldNames.CameraDeadZoneHeight),
                view.GetNumber(FeelFieldNames.CameraDampingXMs),
                view.GetNumber(FeelFieldNames.CameraDampingYMs),
                view.GetNumber(FeelFieldNames.CameraCombatZoomDelta),
                view.GetNumber(FeelFieldNames.CameraCombatZoomBlendMs),
                view.GetRaw(FeelFieldNames.CameraImpulseGain).AsNumber(),
                view.GetNumber(FeelFieldNames.CameraImpulseMinIntervalMs),
                view.GetRaw(FeelFieldNames.CameraShakeCap).AsNumber(),
                view.GetText(FeelFieldNames.CameraDistanceAttenuation),
                setting);
        }
    }
}
