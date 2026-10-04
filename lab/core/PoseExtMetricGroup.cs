using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lab
{
    /// <summary>
    /// 姿势与动画扩展组（条件组，M5-S4，ADR-0147）：只在脚本声明了 <c>meta.poseExt</c>（记录里有 <c>pose_state</c> / <c>pose_rate</c> 事件）时出现，
    /// 既有脚本的指纹没有这个组，既有基线逐字不变。
    /// <para>
    /// 度量类别全部是<b>表现类</b>：目标选择式格子剥掉了时间线（瞬发、没有倒地/起身段），各格子的反应与姿势请求本来就不同
    /// （见 <see cref="ScriptPoseOptions"/>），跨格子不比较，每个格子各自基线。
    /// </para>
    /// </summary>
    public sealed class PoseExtMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("pose_requests", MetricClass.Presentation, "动画状态进入/再触发的姿势请求（tick:标签:状态[:受击子键]，按事件顺序）"),
            MetricSpec.Exact("hit_poses", MetricClass.Presentation, "受击姿势请求（tick:标签:子键，没有子键写 -），按事件顺序；反应为 none 的命中不产生条目"),
            MetricSpec.Exact("hit_pose_total", MetricClass.Presentation, "受击姿势请求总数（反应为 none 的命中不计；动作式格子里霸体窗口内的命中恒不产生条目）"),
            MetricSpec.Exact("hit_pose_counts", MetricClass.Presentation, "每个标签的受击姿势请求数（标签=次数），只列非零"),
            MetricSpec.Exact("rates", MetricClass.Presentation, "剪辑播放速率变化（tick:标签:状态:速率，按事件顺序；速率四舍五入到 6 位）"),
            MetricSpec.Exact("rate_max", MetricClass.Presentation, "玩家剪辑播放速率的最大值（脚本里没有玩家速率变化时为 1）"),
            MetricSpec.Exact("rate_min", MetricClass.Presentation, "玩家剪辑播放速率的最小值（脚本里没有玩家速率变化时为 1）"),
        };

        public string Name => "poseext";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording)
        {
            // 只有脚本声明了姿势观测选项（meta.poseExt）才算：声明 latency 度量组的脚本也会装姿势观测装置，但不因此多出本组（M5-S7）。
            if (recording.Feel == null || recording.Script.Meta.PoseExt == null)
            {
                return false;
            }

            foreach (var e in recording.Feel.Events)
            {
                if (e.Kind == "pose_state" || e.Kind == "pose_rate")
                {
                    return true;
                }
            }

            return false;
        }

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var requests = new List<string>();
            var hits = new List<string>();
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var rates = new List<string>();
            var max = 1.0;
            var min = 1.0;

            foreach (var e in recording.Feel!.Events)
            {
                var tick = e.Tick.ToString(CultureInfo.InvariantCulture);
                if (e.Kind == "pose_state")
                {
                    requests.Add(tick + ":" + e.Target + ":" + e.Detail + (e.Detail2.Length > 0 ? ":" + e.Detail2 : string.Empty));
                    if (e.Detail == "Hit")
                    {
                        hits.Add(tick + ":" + e.Target + ":" + (e.Detail2.Length > 0 ? e.Detail2 : "-"));
                        counts[e.Target] = counts.TryGetValue(e.Target, out var n) ? n + 1 : 1;
                    }
                }
                else if (e.Kind == "pose_rate")
                {
                    rates.Add(tick + ":" + e.Target + ":" + e.Detail + ":" + FeelMetricUtil.Num(MetricSink.Round(e.D)));
                    if (e.Target == "player")
                    {
                        max = Math.Max(max, e.D);
                        min = Math.Min(min, e.D);
                    }
                }
            }

            var countParts = new List<string>();
            foreach (var pair in counts)
            {
                countParts.Add(pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture));
            }

            sink.Add("pose_requests", FeelMetricUtil.Join(requests));
            sink.Add("hit_poses", FeelMetricUtil.Join(hits));
            sink.Add("hit_pose_total", hits.Count);
            sink.Add("hit_pose_counts", FeelMetricUtil.Join(countParts));
            sink.Add("rates", FeelMetricUtil.Join(rates));
            sink.Add("rate_max", max);
            sink.Add("rate_min", min);
        }
    }
}
