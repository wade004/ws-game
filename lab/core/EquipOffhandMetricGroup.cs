using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lab
{
    /// <summary>
    /// 副手与挂点装备度量组（手感设计/06 第 3.6 节、08 第 2 节，ADR-0153）：换装脚本里穿过副手武器或非 2D 外观（挂点附着型）的装备时，
    /// 记录每步之后的副手武器手感引用、副手叠加后解析出的两个可叠加字段（增味层强度档、命中特效强度倍率）、装备面板的占用数/纸娃娃图层数/非 2D 外观数。
    /// <para>
    /// 判断记录（条件组，既有基线逐字不变）：只有记录带换装记录且至少有一步出现副手武器或非 2D 外观时才计算（<see cref="IConditionalMetricGroup"/>）。
    /// 占位装备集（<c>data/_equip</c>）没有副手槽位也没有挂点装备，<c>equip_cycle</c> 与 <c>equip_cycle_tick30</c> 的指纹里没有这一组。
    /// </para>
    /// <para>
    /// 判断记录（只记事实，期望由规则在测试里算）：叠加后的解析值是数据（主手 <c>writes</c> 与副手 <c>offhand_writes</c>）经解析器算出来的，
    /// 度量组只记运行期读数，不在此重算一遍规则；"读数等于规则算出的值"由 <c>EquipExtSceneTests</c> 从数据行推期望后核对。
    /// <c>panel_count_mismatch</c> 是恒为 0 的零差异度量：面板里已装备槽位数应等于 纸娃娃图层 + 非 2D 外观 + 无外观 三者之和。
    /// </para>
    /// </summary>
    public sealed class EquipOffhandMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("offhand_refs", MetricClass.Logic, "每步之后的副手武器手感引用（分号分隔，没有副手武器为 -）"),
            MetricSpec.Exact("offhand_changes", MetricClass.Logic, "副手武器引用发生变化的步数"),
            MetricSpec.Exact("sweetener_tiers", MetricClass.Logic, "每步之后解析出的增味层强度档（sfx_sweetener_tier，副手可叠加）"),
            MetricSpec.Exact("impact_vfx_scales", MetricClass.Logic, "每步之后解析出的命中特效强度倍率（impact_vfx_scale，副手可叠加）"),
            MetricSpec.Exact("panel_occupied", MetricClass.Logic, "每步之后装备面板里已装备槽位数"),
            MetricSpec.Exact("panel_layers", MetricClass.Logic, "每步之后装备面板里纸娃娃图层数"),
            MetricSpec.Exact("panel_non_sprite", MetricClass.Logic, "每步之后装备面板里已装备但外观不是 2D 图层（挂点附着等 model 型）的槽位数"),
            MetricSpec.Exact("panel_count_mismatch", MetricClass.Logic, "装备面板占用数不等于 图层 + 非 2D 外观 + 无外观 之和的步数（应恒为 0）"),
        };

        public string Name => "equip_offhand";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording)
        {
            if (recording.Equip == null)
            {
                return false;
            }

            foreach (var s in recording.Equip.Steps)
            {
                if (s.OffhandRef.Length > 0 || s.PanelNonSpriteVisuals > 0)
                {
                    return true;
                }
            }

            return false;
        }

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var equip = recording.Equip ?? throw new InvalidOperationException("equip_offhand 度量组只适用于带换装记录的运行");
            var refs = new List<string>();
            var tiers = new List<string>();
            var scales = new List<string>();
            var occupied = new List<string>();
            var layers = new List<string>();
            var nonSprite = new List<string>();
            var changes = 0;
            var mismatch = 0;
            foreach (var s in equip.Steps)
            {
                refs.Add(s.OffhandRef.Length == 0 ? "-" : s.OffhandRef);
                if (s.OffhandChanged) changes++;
                tiers.Add(s.SweetenerTier.ToString(CultureInfo.InvariantCulture));
                scales.Add(MetricSink.Round(s.ImpactVfxScale).ToString(CultureInfo.InvariantCulture));
                occupied.Add(s.PanelOccupied.ToString(CultureInfo.InvariantCulture));
                layers.Add(s.PanelLayers.ToString(CultureInfo.InvariantCulture));
                nonSprite.Add(s.PanelNonSpriteVisuals.ToString(CultureInfo.InvariantCulture));
                if (s.PanelOccupied != s.PanelLayers + s.PanelNonSpriteVisuals + s.PanelNoVisuals) mismatch++;
            }

            sink.Add("offhand_refs", string.Join(";", refs));
            sink.Add("offhand_changes", changes);
            sink.Add("sweetener_tiers", string.Join(";", tiers));
            sink.Add("impact_vfx_scales", string.Join(";", scales));
            sink.Add("panel_occupied", string.Join(";", occupied));
            sink.Add("panel_layers", string.Join(";", layers));
            sink.Add("panel_non_sprite", string.Join(";", nonSprite));
            sink.Add("panel_count_mismatch", mismatch);
        }
    }
}
