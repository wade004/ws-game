using System;
using System.Collections.Generic;

namespace Lab
{
    /// <summary>
    /// 投射物结局组（条件组，M4-L）：时间线动作发射的投射物各自的结局原因与时刻。
    /// 只在本次运行里出现过"到期（<c>Expired</c>）"或"被清场（<c>Cleared</c>）"结局时出现——命中与被挡住两种结局早已由
    /// 命中确认与挥空时机度量覆盖，不进这个组，因此既有脚本（<c>feel_projectile</c>、<c>feel_projectile_miss</c>）的基线逐字不变。
    /// 挥空提示与结局原因的关系（到期补挥空、被清场只放弃等待不挥空）在 <c>presentation</c> 组的音效序列里看。
    /// </summary>
    public sealed class ProjectileMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("launched", MetricClass.Logic, "发射的投射物数（<c>action.projectile_launched</c>）"),
            MetricSpec.Exact("ended", MetricClass.Logic, "有结局的投射物数（<c>action.projectile_ended</c>，与发射一一配对）"),
            MetricSpec.Exact("ends", MetricClass.Logic, "结局序列（tick:行动者:原因，按事件顺序）"),
            MetricSpec.Exact("reason_counts", MetricClass.Logic, "按原因计数（原因:个数，按原因名排序）"),
            MetricSpec.Exact("unpaired", MetricClass.Logic, "发射数减结局数（应恒为 0：每发投射物恰有一个结局，反馈侧才不会永远等一个不会来的结局）"),
        };

        public string Name => "projectile";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording)
        {
            if (recording.Feel == null)
            {
                return false;
            }

            foreach (var e in recording.Feel.Events)
            {
                if (e.Kind == "projectile_ended"
                    && (string.Equals(e.Detail, "Expired", StringComparison.Ordinal) || string.Equals(e.Detail, "Cleared", StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var launched = 0;
            var ended = 0;
            var ends = new List<string>();
            var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in recording.Feel!.Events)
            {
                if (e.Kind == "projectile_launched")
                {
                    launched++;
                }
                else if (e.Kind == "projectile_ended")
                {
                    ended++;
                    ends.Add(FeelMetricUtil.Num(e.Tick) + ":" + e.Actor + ":" + e.Detail);
                    FeelMetricUtil.Bump(reasons, e.Detail);
                }
            }

            sink.Add("launched", launched);
            sink.Add("ended", ended);
            sink.Add("ends", FeelMetricUtil.Join(ends));
            sink.Add("reason_counts", FeelMetricUtil.Counts(reasons));
            sink.Add("unpaired", launched - ended);
        }
    }
}
