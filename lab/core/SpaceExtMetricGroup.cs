using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lab
{
    /// <summary>
    /// 空间语义扩展度量组（ADR-0130 追加决定，竖直轴能力包补完）：空中控制与多段跳、地形（落地高度、斜坡、台阶阻挡、天花板）、
    /// 空中姿势请求（<c>jump.rise/fall/land</c>、<c>hit.air</c>、<c>attack.air[.族]</c> 与固定回落链）的运行期事实。
    /// <para>
    /// 判断记录（独立条件组）：新度量放在独立的条件组而不是 <c>space</c> 组里——<c>space</c> 组的度量集合决定既有空间脚本基线里的指纹，
    /// 往里加度量会让既有基线全部失效。本组只在脚本声明了 <c>meta.spaceExt</c> 且格子带竖直轴时出现（<see cref="SpaceRecording.Ext"/> 非 null），
    /// 既有脚本、平面格子都没有它。全部度量都是逻辑类、逐字节比较。
    /// </para>
    /// <para>
    /// 判断记录（步长统计）：<c>ground_step_max</c>/<c>air_step_mean</c> 取"移动请求中、该步前后都在同一种状态"的步的水平位移，避开起跳/落地那一步
    /// （该步一半在地面一半在空中，不属于任一种）。地面取最大值而不是均值，是因为起步有加速段，最大值才是稳态速度。
    /// </para>
    /// </summary>
    public sealed class SpaceExtMetricGroup : IConditionalMetricGroup
    {
        private const double Eps = 1e-9;

        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("options", MetricClass.Logic, "本次运行装配的空间扩展选项（air_control;max_air_jumps;step_height;terrain;spatial_range；未设为 -）"),
            MetricSpec.Exact("air_jump_starts", MetricClass.Logic, "请求发生时已腾空、被接受的跳跃次数（空中跳跃）"),
            MetricSpec.Exact("air_jump_refusals", MetricClass.Logic, "请求发生时已腾空、被拒绝的跳跃请求数（空中跳跃次数用尽）"),
            MetricSpec.Exact("airborne_ticks", MetricClass.Logic, "玩家腾空的 tick 数（竖直运动服务口径，不是高度大于 0）"),
            MetricSpec.Exact("ground_step_max", MetricClass.Logic, "移动请求中、前后都在地面的步的水平位移最大值（世界单位/步；没有为 -1）"),
            MetricSpec.Exact("air_step_mean", MetricClass.Logic, "移动请求中、前后都在空中的步的水平位移均值（世界单位/步；没有为 -1）"),
            MetricSpec.Exact("air_ground_ratio", MetricClass.Logic, "air_step_mean / ground_step_max（任一缺失为 -1）"),
            MetricSpec.Exact("landing_heights", MetricClass.Logic, "每次落地（腾空转地面）那一步末尾的脚下高度（世界单位）"),
            MetricSpec.Exact("ground_height_max", MetricClass.Logic, "不在空中的步里脚下高度的最大值（站在最高的地面/平台上的高度）"),
            MetricSpec.Exact("ground_height_final", MetricClass.Logic, "最后一步的脚下高度"),
            MetricSpec.Exact("airborne_height_max", MetricClass.Logic, "腾空的步里脚下高度的最大值（没腾空为 0）"),
            MetricSpec.Exact("ceiling_clamp_ticks", MetricClass.Logic, "撞天花板的 tick（腾空、竖直速度恰为 0 且上一步在上升）"),
            MetricSpec.Exact("move_blocked_ticks", MetricClass.Logic, "移动请求中、不在空中、该步水平位移为 0 的 tick 数（台阶/阻挡顶住）"),
            MetricSpec.Exact("final_x", MetricClass.Logic, "最后一步玩家的世界 x"),
            MetricSpec.Exact("air_phases", MetricClass.Logic, "玩家空中阶段的连续区间（阶段:起 tick-止 tick;…）；没装姿势装置为空"),
            MetricSpec.Exact("air_pose_requests", MetricClass.Logic, "空中姿势请求的解析（tick:来源:实体:请求键->命中键@回落深度;…；来源 phase 为玩家空中阶段切换，hit 为目标受击，attack 为玩家出手）；链上无一命中记为 -"),
            MetricSpec.Exact("air_pose_fallbacks", MetricClass.Logic, "回落深度大于 0 的空中姿势请求数"),
        };

        public string Name => "space_ext";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) => recording.Space?.Ext != null;

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var space = recording.Space ?? throw new InvalidOperationException("space_ext 度量组只适用于带空间记录的运行");
            var ext = space.Ext ?? throw new InvalidOperationException("space_ext 度量组只适用于带空间扩展记录的运行");
            var o = ext.Options;
            sink.Add("options", string.Join(";", new[]
            {
                o.AirControl.HasValue ? Fmt(o.AirControl.Value) : "-",
                o.MaxAirJumps.HasValue ? o.MaxAirJumps.Value.ToString(CultureInfo.InvariantCulture) : "-",
                o.StepHeight.HasValue ? Fmt(o.StepHeight.Value) : "-",
                o.Terrain ? "1" : "0",
                o.SpatialRange ? "1" : "0",
            }));
            sink.Add("air_jump_starts", ext.AirJumpStarts);
            sink.Add("air_jump_refusals", ext.AirJumpRefusals);

            var ticks = recording.Ticks;
            var heights = space.PlayerHeights;
            var airborneTicks = 0;
            var groundMax = -1.0;
            var airSum = 0.0;
            var airCount = 0;
            var landings = new List<double>();
            var groundHeightMax = 0.0;
            var airHeightMax = 0.0;
            var clamps = new List<double>();
            var blocked = 0;
            for (var t = 0; t < ext.PlayerAirborne.Count && t < ticks.Count && t < heights.Count; t++)
            {
                var air = ext.PlayerAirborne[t];
                var wasAir = t > 0 && ext.PlayerAirborne[t - 1];
                if (air)
                {
                    airborneTicks++;
                    airHeightMax = Math.Max(airHeightMax, heights[t]);
                    if (t > 0 && Math.Abs(ext.PlayerVerticalSpeeds[t]) < Eps && ext.PlayerVerticalSpeeds[t - 1] > 0.0)
                    {
                        clamps.Add(t);
                    }
                }
                else
                {
                    groundHeightMax = Math.Max(groundHeightMax, heights[t]);
                    if (wasAir)
                    {
                        landings.Add(MetricSink.Round(heights[t]));
                    }
                }

                var dx = t > 0 ? Math.Abs(ticks[t].Position.X - ticks[t - 1].Position.X) : 0.0;
                if (t > 0 && ext.PlayerMoveRequested[t])
                {
                    if (!air && !wasAir)
                    {
                        groundMax = Math.Max(groundMax, dx);
                        if (dx < Eps)
                        {
                            blocked++;
                        }
                    }
                    else if (air && wasAir)
                    {
                        airSum += dx;
                        airCount++;
                    }
                }
            }

            var airMean = airCount > 0 ? airSum / airCount : -1.0;
            sink.Add("airborne_ticks", airborneTicks);
            sink.Add("ground_step_max", groundMax);
            sink.Add("air_step_mean", airMean);
            sink.Add("air_ground_ratio", groundMax > Eps && airCount > 0 ? airMean / groundMax : -1.0);
            sink.Add("landing_heights", landings);
            sink.Add("ground_height_max", groundHeightMax);
            sink.Add("ground_height_final", heights.Count > 0 ? heights[heights.Count - 1] : 0.0);
            sink.Add("airborne_height_max", airHeightMax);
            sink.Add("ceiling_clamp_ticks", clamps);
            sink.Add("move_blocked_ticks", blocked);
            sink.Add("final_x", ticks.Count > 0 ? ticks[ticks.Count - 1].Position.X : 0.0);

            var phases = new StringBuilder();
            var runPhase = string.Empty;
            var runStart = 0;
            var phaseTicks = ext.PlayerAirPhases;
            for (var t = 0; t <= phaseTicks.Count; t++)
            {
                var phase = t < phaseTicks.Count ? phaseTicks[t] : string.Empty;
                if (phase == runPhase)
                {
                    continue;
                }

                if (runPhase.Length > 0)
                {
                    phases.Append(runPhase).Append(':').Append(runStart.ToString(CultureInfo.InvariantCulture)).Append('-')
                        .Append((t - 1).ToString(CultureInfo.InvariantCulture)).Append(';');
                }

                runPhase = phase;
                runStart = t;
            }

            sink.Add("air_phases", phases.ToString());

            var requests = new StringBuilder();
            var fallbacks = 0;
            foreach (var r in ext.AirPoses)
            {
                requests.Append(r.Tick.ToString(CultureInfo.InvariantCulture)).Append(':').Append(r.Kind).Append(':').Append(r.Entity).Append(':')
                    .Append(r.Requested).Append("->").Append(r.Resolved.Length > 0 ? r.Resolved : "-").Append('@')
                    .Append(r.Depth.ToString(CultureInfo.InvariantCulture)).Append(';');
                if (r.Depth > 0)
                {
                    fallbacks++;
                }
            }

            sink.Add("air_pose_requests", requests.ToString());
            sink.Add("air_pose_fallbacks", fallbacks);
        }

        private static string Fmt(double value) => MetricSink.Round(value).ToString("R", CultureInfo.InvariantCulture);
    }
}
