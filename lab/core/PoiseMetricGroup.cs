using System;
using System.Collections.Generic;

namespace Lab
{
    /// <summary>
    /// 动态韧性组（条件组，M4-L）：目标韧性池被"声明了 <c>poise_damage</c> 的命中"扣减的轨迹——每次扣减前后的有效韧性、是否破韧，
    /// 以及回满事件。只在本次运行里出现过 <c>combat.poise_changed</c> 时出现，因此只有动态韧性脚本的指纹有这个组，
    /// 既有脚本（含静态韧性的 <c>feel_stake_poise</c>）的基线逐字不变。受击反应本身（Flinch 或完整反应）在 <c>reaction</c> 组里看。
    /// </summary>
    public sealed class PoiseMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("hits", MetricClass.Logic, "扣减了韧性池的命中数（<c>combat.poise_changed</c>）"),
            MetricSpec.Exact("changes", MetricClass.Logic, "每次扣减（tick:受击方:扣前>扣后，破韧的末尾加 !）"),
            MetricSpec.Exact("breaks", MetricClass.Logic, "破韧次数（韧性从正数被打到 0）"),
            MetricSpec.Exact("recoveries", MetricClass.Logic, "回满事件（tick:受击方）"),
            MetricSpec.Exact("out_of_range", MetricClass.Logic, "扣前/扣后落在 [0, 容量] 之外的记录数（应恒为 0）"),
            MetricSpec.Exact("accounting_mismatch", MetricClass.Logic, "扣后 ≠ max(0, 扣前 − 本次伤害) 或破韧标记与规则不符的记录数（应恒为 0）"),
        };

        public string Name => "poise";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording)
        {
            if (recording.Feel == null)
            {
                return false;
            }

            foreach (var e in recording.Feel.Events)
            {
                if (e.Kind == "poise_changed")
                {
                    return true;
                }
            }

            return false;
        }

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var hits = 0;
            var breaks = 0;
            var outOfRange = 0;
            var mismatch = 0;
            var changes = new List<string>();
            var recoveries = new List<string>();
            foreach (var e in recording.Feel!.Events)
            {
                if (e.Kind == "poise_recovered")
                {
                    recoveries.Add(FeelMetricUtil.Num(e.Tick) + ":" + e.Target);
                    continue;
                }

                if (e.Kind != "poise_changed")
                {
                    continue;
                }

                hits++;
                // 记录布局（见 FeelRig）：A = 破韧标记，D = 这一击声明的韧性伤害；Detail2 = before/after/max。
                var before = double.Parse(FeelMetricUtil.Field(e.Detail2, "before"), System.Globalization.CultureInfo.InvariantCulture);
                var after = double.Parse(FeelMetricUtil.Field(e.Detail2, "after"), System.Globalization.CultureInfo.InvariantCulture);
                var max = double.Parse(FeelMetricUtil.Field(e.Detail2, "max"), System.Globalization.CultureInfo.InvariantCulture);
                var broken = e.A != 0;
                if (broken)
                {
                    breaks++;
                }

                const double eps = 1e-6;
                if (before < -eps || before > max + eps || after < -eps || after > max + eps)
                {
                    outOfRange++;
                }

                var expectedAfter = Math.Max(0.0, before - e.D);
                var expectedBroken = before > eps && expectedAfter <= eps;
                if (Math.Abs(after - expectedAfter) > eps || broken != expectedBroken)
                {
                    mismatch++;
                }

                changes.Add(
                    FeelMetricUtil.Num(e.Tick) + ":" + e.Target + ":" + FeelMetricUtil.Num(before) + ">" + FeelMetricUtil.Num(after)
                    + (broken ? "!" : string.Empty));
            }

            sink.Add("hits", hits);
            sink.Add("changes", FeelMetricUtil.Join(changes));
            sink.Add("breaks", breaks);
            sink.Add("recoveries", FeelMetricUtil.Join(recoveries));
            sink.Add("out_of_range", outOfRange);
            sink.Add("accounting_mismatch", mismatch);
        }
    }
}
