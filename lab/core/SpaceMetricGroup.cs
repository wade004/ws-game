using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lab
{
    /// <summary>
    /// 空间语义度量组（手感设计/06 第 1.2 节）：<c>plane</c>/<c>side_2d</c>/<c>volume</c> 三个空间取值在无头宿主上的运行期事实——
    /// 竖直轴是否装配、跳跃请求与起跳、脚下高度曲线（顶点、滞空、落地 tick、整段飞行步数）、靶子的出生高度与被击飞后的顶点/落地、
    /// 深度锁丢弃的输入、命中时玩家与靶子的高度差。
    /// <para>
    /// 判断记录（仅空间相关的运行适用）：只有记录带 <see cref="LabRecording.Space"/> 才计算（<see cref="IConditionalMetricGroup"/>）——
    /// 格子带竖直轴、脚本含跳跃事件或靶子声明了出生高度；既有脚本在平面格子上没有这一组，既有基线逐字不变。全部度量都是逻辑类、逐字节比较。
    /// </para>
    /// <para>
    /// 判断记录（命中高度差的时间口径）：<c>hit_height_gaps</c> 取伤害事件所在 tick 的<b>前一个</b> tick 末尾的高度——核心层 tick 内的阶段顺序里技能管线
    /// （命中判定）早于移动与导航（竖直积分），命中看到的就是上一步积分的结果。第 0 tick 的命中取 0。
    /// </para>
    /// </summary>
    public sealed class SpaceMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("model", MetricClass.Logic, "本次运行采用的空间模型：plane/side_2d/volume"),
            MetricSpec.Exact("vertical_axis", MetricClass.Logic, "世界是否装配竖直轴（1/0）"),
            MetricSpec.Exact("gravity", MetricClass.Logic, "重力加速度（世界单位/秒²；无竖直轴为 0）"),
            MetricSpec.Exact("jump_height", MetricClass.Logic, "跳跃顶点高度（世界单位；无竖直轴为 0）"),
            MetricSpec.Exact("jump_requests", MetricClass.Logic, "脚本里的跳跃请求数（含被拒绝的）"),
            MetricSpec.Exact("jumps_started", MetricClass.Logic, "被接受、真正起跳的次数"),
            MetricSpec.Exact("jumps_refused", MetricClass.Logic, "被拒绝的跳跃请求数（平面世界没有竖直轴；空中且不允许二段跳）"),
            MetricSpec.Exact("jump_start_ticks", MetricClass.Logic, "每次起跳的宿主固定步序号"),
            MetricSpec.Exact("jump_flight_steps", MetricClass.Logic, "每次起跳到落地的积分步数（起跳那一步算第 1 步；记录结束前没落地为 -1）"),
            MetricSpec.Exact("player_apex", MetricClass.Logic, "玩家脚下高度的最大值（世界单位）"),
            MetricSpec.Exact("player_apex_tick", MetricClass.Logic, "玩家首次达到最大高度的 tick（没离过地为 -1）"),
            MetricSpec.Exact("player_airborne_ticks", MetricClass.Logic, "玩家脚下高度大于 0 的 tick 数"),
            MetricSpec.Exact("player_landing_ticks", MetricClass.Logic, "玩家高度回到 0 的 tick（前一步高度大于 0）"),
            MetricSpec.Exact("depth_inputs_dropped", MetricClass.Logic, "被深度锁丢掉竖直分量的轴事件数（横版二维）"),
            MetricSpec.Exact("max_depth_drift", MetricClass.Logic, "玩家世界 y 相对出生点的最大偏移（世界单位）"),
            MetricSpec.Exact("declared_dummy_heights", MetricClass.Logic, "靶子出生时声明的高度（label=h;…；平面世界声明了也记，但被忽略）"),
            MetricSpec.Exact("dummy_apexes", MetricClass.Logic, "高度曾大于 0 的靶子的最大高度（label=h;…）"),
            MetricSpec.Exact("dummy_off_ground_ticks", MetricClass.Logic, "高度曾大于 0 的靶子离地的 tick 数（label=n;…）"),
            MetricSpec.Exact("dummy_landing_ticks", MetricClass.Logic, "靶子高度回到 0 的 tick（label=t,t;…；出生即飘浮的靶子不会落地）"),
            MetricSpec.Exact("hit_height_gaps", MetricClass.Logic, "玩家每次伤害命中时与目标脚下高度差的绝对值（世界单位；取命中 tick 前一步的高度）"),
            MetricSpec.Exact("hit_height_gap_max", MetricClass.Logic, "hit_height_gaps 的最大值（没有命中为 -1）"),
        };

        public string Name => "space";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) => recording.Space != null;

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var space = recording.Space ?? throw new InvalidOperationException("space 度量组只适用于带空间记录的运行");
            sink.Add("model", space.Model);
            sink.Add("vertical_axis", space.VerticalAxis ? 1 : 0);
            sink.Add("gravity", space.Gravity);
            sink.Add("jump_height", space.JumpHeight);
            sink.Add("jump_requests", space.JumpRequests);
            sink.Add("jumps_started", space.JumpsStarted);
            sink.Add("jumps_refused", space.JumpRequests - space.JumpsStarted);

            var starts = new List<double>();
            var flights = new List<double>();
            foreach (var start in space.JumpStartTicks)
            {
                starts.Add(start);
                var landed = -1;
                for (var t = start; t < space.PlayerHeights.Count; t++)
                {
                    if (space.PlayerHeights[t] <= 0.0)
                    {
                        landed = t;
                        break;
                    }
                }

                flights.Add(landed < 0 ? -1 : landed - start + 1);
            }

            sink.Add("jump_start_ticks", starts);
            sink.Add("jump_flight_steps", flights);

            var apex = 0.0;
            var apexTick = -1;
            var airborne = 0;
            var landings = new List<double>();
            for (var t = 0; t < space.PlayerHeights.Count; t++)
            {
                var h = space.PlayerHeights[t];
                if (h > apex)
                {
                    apex = h;
                    apexTick = t;
                }

                if (h > 0.0)
                {
                    airborne++;
                }

                if (h <= 0.0 && t > 0 && space.PlayerHeights[t - 1] > 0.0)
                {
                    landings.Add(t);
                }
            }

            sink.Add("player_apex", apex);
            sink.Add("player_apex_tick", apexTick);
            sink.Add("player_airborne_ticks", airborne);
            sink.Add("player_landing_ticks", landings);
            sink.Add("depth_inputs_dropped", space.DepthInputsDropped);

            var drift = 0.0;
            foreach (var y in space.PlayerDepths)
            {
                drift = Math.Max(drift, Math.Abs(y - recording.StartPosition.Y));
            }

            sink.Add("max_depth_drift", drift);

            var declared = new StringBuilder();
            foreach (var pair in space.DeclaredDummyHeights)
            {
                declared.Append(pair.Key).Append('=').Append(Fmt(pair.Value)).Append(';');
            }

            sink.Add("declared_dummy_heights", declared.ToString());

            var apexes = new StringBuilder();
            var offGround = new StringBuilder();
            var dummyLandings = new StringBuilder();
            foreach (var pair in space.DummyHeights)
            {
                var max = 0.0;
                var off = 0;
                var lands = new List<string>();
                for (var t = 0; t < pair.Value.Count; t++)
                {
                    var h = pair.Value[t];
                    max = Math.Max(max, h);
                    if (h > 0.0)
                    {
                        off++;
                    }

                    if (h <= 0.0 && t > 0 && pair.Value[t - 1] > 0.0)
                    {
                        lands.Add(t.ToString(CultureInfo.InvariantCulture));
                    }
                }

                if (max > 0.0)
                {
                    apexes.Append(pair.Key).Append('=').Append(Fmt(max)).Append(';');
                    offGround.Append(pair.Key).Append('=').Append(off.ToString(CultureInfo.InvariantCulture)).Append(';');
                    dummyLandings.Append(pair.Key).Append('=').Append(string.Join(",", lands)).Append(';');
                }
            }

            sink.Add("dummy_apexes", apexes.ToString());
            sink.Add("dummy_off_ground_ticks", offGround.ToString());
            sink.Add("dummy_landing_ticks", dummyLandings.ToString());

            var gaps = new List<double>();
            var gapMax = -1.0;
            foreach (var e in recording.Events)
            {
                if (!string.Equals(e.Kind, "damage", StringComparison.Ordinal) || !string.Equals(e.Source, "player", StringComparison.Ordinal)
                    || !space.DummyHeights.TryGetValue(e.Target, out var targetHeights))
                {
                    continue;
                }

                var at = e.Tick - 1;
                var playerHeight = at >= 0 && at < space.PlayerHeights.Count ? space.PlayerHeights[at] : 0.0;
                var targetHeight = at >= 0 && at < targetHeights.Count ? targetHeights[at] : 0.0;
                var gap = Math.Abs(targetHeight - playerHeight);
                gaps.Add(gap);
                gapMax = Math.Max(gapMax, gap);
            }

            sink.Add("hit_height_gaps", gaps);
            sink.Add("hit_height_gap_max", gapMax);
        }

        private static string Fmt(double value) => MetricSink.Round(value).ToString("R", CultureInfo.InvariantCulture);
    }
}
