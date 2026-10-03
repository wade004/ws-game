using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lab
{
    /// <summary>
    /// 地形感知寻路度量组（M4-W1a，ADR-0130 追加决定"寻路感知台阶"）：点击移动（<c>move_to</c>）的结局——是否到达、走了多远、绕没绕、
    /// 失败与停止的原因、地形热切换次数。
    /// <para>
    /// 判断记录（独立条件组）：同 <see cref="SpaceExtMetricGroup"/>，新度量放在独立条件组而不是往 <c>space</c>/<c>space_ext</c> 里加——那两组的度量集合决定既有基线里的指纹。
    /// 本组只在脚本含 <c>move_to</c>/<c>terrain_swap</c> 事件且格子带竖直轴时出现（<see cref="SpaceExtRecording.Nav"/> 非 null）。全部度量都是逻辑类、逐字节比较。
    /// </para>
    /// <para>
    /// 判断记录（到达口径）：到达 = 最后一个 <c>move_to</c> 之后玩家与目标的距离不超过 <see cref="ArrivalTolerance"/>（略大于移动系统的到达容差 0.01，
    /// 吸收浮点）。<c>detour_ratio</c> = 实际走过的路程 ÷ 请求时起点到目标的直线距离，大于 1 表示绕行（没有请求为 -1）。
    /// </para>
    /// </summary>
    public sealed class SpaceNavMetricGroup : IConditionalMetricGroup
    {
        /// <summary>到达判定的距离容差（世界单位）。</summary>
        public const double ArrivalTolerance = 0.02;

        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("move_requests", MetricClass.Logic, "脚本里的点击移动（move_to）请求数"),
            MetricSpec.Exact("move_failed", MetricClass.Logic, "移动失败（寻路无路等）的次数"),
            MetricSpec.Exact("move_failures", MetricClass.Logic, "移动失败的明细（tick:原因;…）"),
            MetricSpec.Exact("move_stops", MetricClass.Logic, "移动停止的明细（tick:原因;…；含到达 Arrived、被地形顶住 TerrainBlocked 等）"),
            MetricSpec.Exact("terrain_blocked_stops", MetricClass.Logic, "以 TerrainBlocked 结束的移动次数（规划之后才出现的台阶把路径顶住）"),
            MetricSpec.Exact("terrain_swaps", MetricClass.Logic, "地形热切换（terrain_swap）的次数"),
            MetricSpec.Exact("arrived", MetricClass.Logic, "最后一次 move_to 之后玩家是否到达目标（1/0；没有请求为 -1）"),
            MetricSpec.Exact("final_dist_to_target", MetricClass.Logic, "最后一步玩家到最后一个 move_to 目标的距离（没有请求为 -1）"),
            MetricSpec.Exact("travelled", MetricClass.Logic, "第一次 move_to 之后玩家走过的水平路程（逐步位移之和）"),
            MetricSpec.Exact("detour_ratio", MetricClass.Logic, "travelled ÷ 第一次请求时起点到目标的直线距离（没有请求为 -1）"),
            MetricSpec.Exact("max_abs_y", MetricClass.Logic, "整个运行里玩家世界 y 的绝对值最大值（沿 x 轴的请求走出非零 y = 绕行）"),
            MetricSpec.Exact("final_y", MetricClass.Logic, "最后一步玩家的世界 y"),
        };

        public string Name => "space_nav";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) => recording.Space?.Ext?.Nav != null;

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var nav = recording.Space?.Ext?.Nav ?? throw new InvalidOperationException("space_nav 度量组只适用于带寻路记录的运行");
            var ticks = recording.Ticks;
            sink.Add("move_requests", nav.Targets.Count);
            sink.Add("move_failed", nav.Failures.Count);
            sink.Add("move_failures", string.Join(";", nav.Failures));
            sink.Add("move_stops", string.Join(";", nav.Stops));
            var blocked = 0;
            foreach (var stop in nav.Stops)
            {
                if (stop.EndsWith(":TerrainBlocked", StringComparison.Ordinal))
                {
                    blocked++;
                }
            }

            sink.Add("terrain_blocked_stops", blocked);
            sink.Add("terrain_swaps", nav.TerrainSwaps);

            var maxAbsY = 0.0;
            foreach (var t in ticks)
            {
                maxAbsY = Math.Max(maxAbsY, Math.Abs(t.Position.Y));
            }

            sink.Add("max_abs_y", maxAbsY);
            sink.Add("final_y", ticks.Count > 0 ? ticks[ticks.Count - 1].Position.Y : 0.0);

            if (nav.Targets.Count == 0 || ticks.Count == 0)
            {
                sink.Add("arrived", -1);
                sink.Add("final_dist_to_target", -1.0);
                sink.Add("travelled", 0.0);
                sink.Add("detour_ratio", -1.0);
                return;
            }

            var last = nav.Targets[nav.Targets.Count - 1];
            var finalDistance = Core.Foundation.Common.Vec2.Distance(ticks[ticks.Count - 1].Position, last);
            sink.Add("arrived", finalDistance <= ArrivalTolerance ? 1 : 0);
            sink.Add("final_dist_to_target", finalDistance);

            var firstTick = Math.Min(Math.Max(nav.TargetTicks[0], 0), ticks.Count - 1);
            var startPosition = firstTick > 0 ? ticks[firstTick - 1].Position : ticks[firstTick].Position;
            var travelled = 0.0;
            var previous = startPosition;
            for (var i = firstTick; i < ticks.Count; i++)
            {
                travelled += Core.Foundation.Common.Vec2.Distance(previous, ticks[i].Position);
                previous = ticks[i].Position;
            }

            sink.Add("travelled", travelled);
            var straight = Core.Foundation.Common.Vec2.Distance(startPosition, nav.Targets[0]);
            sink.Add("detour_ratio", straight > 1e-9 ? travelled / straight : -1.0);
        }
    }
}
