using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Presentation.FeedbackBinder.Contracts;

namespace Presentation.FeedbackBinder.Core
{
    /// <summary>
    /// <see cref="IImpactFeelSource"/> 的默认实现：经 <see cref="IFeelPresentingSource"/> 取单位的呈现型视图（
    /// <see cref="PresentingFeelView"/>），读特效组、音频组与镜头组的冲击相关字段。读判定型字段会抛
    /// <see cref="FeelHalfViolationException"/>（手感设计/05 第 5 节），所以本类在类型上就不可能把判定型手感带进表现层。
    /// <para>
    /// 单位：<c>camera_impulse_gain</c>/<c>camera_shake_cap</c> 取标定前的相对值（画面高度比例，<see cref="FeelHalfView.GetRaw"/>），
    /// 不乘参考镜头高度（比例随缩放由镜头实现换算）；其余数值取绝对值（这些字段的单位为毫秒/倍率/档位，标定不改变数值）。
    /// </para>
    /// </summary>
    public sealed class PresentingImpactFeelSource : IImpactFeelSource
    {
        private static readonly string[] TierFields =
        {
            FeelFieldNames.SfxSwingTier, FeelFieldNames.SfxWhiffTier, FeelFieldNames.SfxImpactTier,
            FeelFieldNames.SfxSweetenerTier, string.Empty /* voice：手感表无对应字段 */, FeelFieldNames.SfxFootstepTier,
        };

        private readonly IFeelPresentingSource _source;

        public PresentingImpactFeelSource(IFeelPresentingSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public ImpactFeel? Get(Id unitId)
        {
            var view = _source.ResolvePresenting(unitId);

            Id? profileRef = null;
            var refValue = view.GetAbsolute(FeelFieldNames.ImpactProfileRef);
            if (refValue.Kind != FeelValueKind.None && Id.TryParse(refValue.AsText(), out var parsed))
            {
                profileRef = parsed;
            }

            var tiers = new List<int>(TierFields.Length);
            foreach (var field in TierFields)
            {
                tiers.Add(field.Length == 0 ? 0 : (int)Math.Round(view.GetNumber(field)));
            }

            Id? trailRef = null;
            var trailValue = view.GetAbsolute(FeelFieldNames.TrailRef);
            if (trailValue.Kind != FeelValueKind.None && Id.TryParse(trailValue.AsText(), out var parsedTrail))
            {
                trailRef = parsedTrail;
            }

            string? setting = null;
            var settingValue = view.GetAbsolute(FeelFieldNames.CameraUserIntensitySetting);
            if (settingValue.Kind != FeelValueKind.None)
            {
                setting = settingValue.AsText();
            }

            return new ImpactFeel(
                profileRef,
                view.GetNumber(FeelFieldNames.ImpactVfxScale),
                tiers,
                view.GetText(FeelFieldNames.SfxMaterial),
                (int)Math.Round(view.GetNumber(FeelFieldNames.SfxMaxConcurrent)),
                view.GetRaw(FeelFieldNames.CameraImpulseGain).AsNumber(),
                view.GetNumber(FeelFieldNames.CameraImpulseMinIntervalMs),
                view.GetRaw(FeelFieldNames.CameraShakeCap).AsNumber(),
                view.GetText(FeelFieldNames.CameraDistanceAttenuation),
                setting,
                view.GetBool(FeelFieldNames.TrailEnabled),
                view.GetBool(FeelFieldNames.AfterimageEnabled),
                trailRef);
        }
    }
}
