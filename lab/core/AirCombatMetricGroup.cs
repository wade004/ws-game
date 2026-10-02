using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lab
{
    /// <summary>一次落地事件（<c>unit.landed</c>）的记录。</summary>
    public sealed class LandedRecord
    {
        public int Tick { get; }

        public string Entity { get; }

        /// <summary>落地后的脚下高度（落点的地面高度）。</summary>
        public double Height { get; }

        /// <summary>本次离地以来的累计空中时间（秒）。</summary>
        public double AirSeconds { get; }

        /// <summary>落地瞬间的下落速度。</summary>
        public double ImpactSpeed { get; }

        public LandedRecord(int tick, string entity, double height, double airSeconds, double impactSpeed)
        {
            Tick = tick;
            Entity = entity;
            Height = height;
            AirSeconds = airSeconds;
            ImpactSpeed = impactSpeed;
        }
    }

    /// <summary>
    /// 空中战斗的运行期记录（手感落地 M4-W1b；脚本 <c>meta.spaceExt.airCombat</c> 为真且格子带竖直轴时才有，否则 <see cref="SpaceRecording.AirCombat"/>
    /// 为 null、度量组 <c>air_combat</c> 不出现）：落地事件、靶子逐步的竖直状态与硬直、靶子的脚本化主动行为（跳跃/空中移动）。
    /// 采样口径同 <see cref="SpaceRecording"/>：每个宿主固定步末尾一份。
    /// </summary>
    public sealed class AirCombatRecording
    {
        /// <summary>落地事件（按派发顺序）。</summary>
        public List<LandedRecord> Landed { get; } = new List<LandedRecord>();

        /// <summary>每个靶子逐步是否腾空（竖直运动服务口径）。</summary>
        public SortedDictionary<string, List<bool>> DummyAirborne { get; } = new SortedDictionary<string, List<bool>>(StringComparer.Ordinal);

        /// <summary>每个靶子逐步的竖直速度。</summary>
        public SortedDictionary<string, List<double>> DummySpeeds { get; } = new SortedDictionary<string, List<double>>(StringComparer.Ordinal);

        /// <summary>每个靶子逐步是否处于受击硬直（受击裁决宿主口径）。</summary>
        public SortedDictionary<string, List<bool>> DummyStaggered { get; } = new SortedDictionary<string, List<bool>>(StringComparer.Ordinal);

        /// <summary>每个靶子逐步的世界位置。</summary>
        public SortedDictionary<string, List<Core.Foundation.Common.Vec2>> DummyPositions { get; } =
            new SortedDictionary<string, List<Core.Foundation.Common.Vec2>>(StringComparer.Ordinal);

        /// <summary>每个靶子逐步是否有脚本化的移动在持续（<c>move</c> 事件设了非零方向）。</summary>
        public SortedDictionary<string, List<bool>> DummyMoveActive { get; } = new SortedDictionary<string, List<bool>>(StringComparer.Ordinal);

        /// <summary>靶子被脚本要求起跳后被竖直运动服务接受的次数（标签 → 次数）。</summary>
        public SortedDictionary<string, int> DummyJumpsAccepted { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>靶子起跳请求被拒绝的次数（标签 → 次数）。</summary>
        public SortedDictionary<string, int> DummyJumpsRefused { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

        public void Register(string label)
        {
            DummyAirborne[label] = new List<bool>();
            DummySpeeds[label] = new List<double>();
            DummyStaggered[label] = new List<bool>();
            DummyPositions[label] = new List<Core.Foundation.Common.Vec2>();
            DummyMoveActive[label] = new List<bool>();
        }
    }

    /// <summary>
    /// 空中战斗度量组（手感落地 M4-W1b）：落地事件（高度、空中时长、落地速度）、靶子被击飞的次数与顶点、空中硬直（含"空中硬直持续到落地"）、
    /// 靶子的主动跳跃与空中移动、受控位移的深度漂移。
    /// <para>
    /// 判断记录（独立条件组）：同 <see cref="SpaceExtMetricGroup"/>——新度量放在独立的条件组里，只在 <see cref="SpaceRecording.AirCombat"/> 非 null 时出现，
    /// 既有脚本与基线逐字不变。全部度量都是逻辑类、逐字节比较。
    /// </para>
    /// <para>
    /// 判断记录（击飞次数的口径）：<c>dummy_launches</c> 数"该靶子腾空且（上一步不在空中，或竖直速度比上一步大）"的步——重力只会让竖直速度单调下降，
    /// 速度上升只能是被重新抛起（再次击飞、空中跳跃）；因此它同时数击飞与靶子自己的起跳，两者在脚本里分得开（跳跃次数另有 <c>dummy_jumps</c>）。
    /// </para>
    /// </summary>
    public sealed class AirCombatMetricGroup : IConditionalMetricGroup
    {
        private const double Eps = 1e-9;

        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("landed_count", MetricClass.Logic, "unit.landed 事件数（玩家与靶子合计）"),
            MetricSpec.Exact("landings", MetricClass.Logic, "落地事件（tick:实体:高度:空中时长秒:落地速度;…）"),
            MetricSpec.Exact("dummy_launches", MetricClass.Logic, "靶子被抛起的次数（label=n;…）：腾空且上一步不在空中、或竖直速度比上一步大的步数"),
            MetricSpec.Exact("dummy_jumps", MetricClass.Logic, "脚本要求靶子起跳的结果（label=接受/拒绝;…）"),
            MetricSpec.Exact("dummy_peak_heights", MetricClass.Logic, "靶子脚下高度的最大值（label=h;…）"),
            MetricSpec.Exact("dummy_air_ticks", MetricClass.Logic, "靶子腾空的 tick 数（label=n;…）"),
            MetricSpec.Exact("dummy_stagger_ticks", MetricClass.Logic, "靶子处于受击硬直的 tick 数（label=n;…）"),
            MetricSpec.Exact("dummy_stagger_air_ticks", MetricClass.Logic, "靶子既腾空又处于硬直的 tick 数（label=n;…）"),
            MetricSpec.Exact("dummy_stagger_end_vs_land", MetricClass.Logic, "硬直结束的 tick 减首次落地的 tick（label=d;…；≥ 0 表示硬直撑到了落地，< 0 表示落地前就结束；没有硬直或没落地的靶子不列）"),
            MetricSpec.Exact("dummy_air_move", MetricClass.Logic, "靶子在空中且脚本移动持续的步数与这些步的水平位移合计（label=步数/位移;…）"),
            MetricSpec.Exact("dummy_final_position", MetricClass.Logic, "靶子最后一步的位置（label=x/y;…）"),
            MetricSpec.Exact("dummy_depth_drift", MetricClass.Logic, "靶子世界 y 相对首步的最大偏移（label=d;…；横版二维深度锁下击退不应产生）"),
            MetricSpec.Exact("landed_height_max", MetricClass.Logic, "落地事件里落地高度的最大值（没有落地事件为 -1）"),
            MetricSpec.Exact("landed_air_seconds_max", MetricClass.Logic, "落地事件里空中时长（秒）的最大值（没有落地事件为 -1）"),
            MetricSpec.Exact("dummy_peak_max", MetricClass.Logic, "全部靶子脚下高度的最大值"),
            MetricSpec.Exact("dummy_launches_max", MetricClass.Logic, "全部靶子被抛起次数的最大值"),
            MetricSpec.Exact("dummy_stagger_air_ticks_max", MetricClass.Logic, "全部靶子既腾空又处于硬直的 tick 数的最大值"),
            MetricSpec.Exact("dummy_stagger_end_vs_land_min", MetricClass.Logic, "全部靶子里硬直结束 tick 减首次落地 tick 的最小值（没有这样的靶子为 -9999）"),
            MetricSpec.Exact("dummy_depth_drift_max", MetricClass.Logic, "全部靶子世界 y 相对首步的最大偏移的最大值"),
            MetricSpec.Exact("dummy_air_ground_step_ratio", MetricClass.Logic, "靶子在空中移动的平均步长 / 在地面移动的最大步长（同 space_ext.air_ground_ratio 口径；任一缺失为 -1；全部靶子合计）"),
            MetricSpec.Exact("dummy_displacement_x_max", MetricClass.Logic, "全部靶子最后位置相对首步位置的横向位移（带符号）的最大值"),
        };

        public string Name => "air_combat";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) => recording.Space?.AirCombat != null;

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var air = recording.Space?.AirCombat ?? throw new InvalidOperationException("air_combat 度量组只适用于带空中战斗记录的运行");
            sink.Add("landed_count", air.Landed.Count);

            var landings = new StringBuilder();
            foreach (var l in air.Landed)
            {
                landings.Append(l.Tick.ToString(CultureInfo.InvariantCulture)).Append(':').Append(l.Entity).Append(':')
                    .Append(Fmt(l.Height)).Append(':').Append(Fmt(l.AirSeconds)).Append(':').Append(Fmt(l.ImpactSpeed)).Append(';');
            }

            sink.Add("landings", landings.ToString());

            var launches = new StringBuilder();
            var jumps = new StringBuilder();
            var peaks = new StringBuilder();
            var airTicks = new StringBuilder();
            var staggerTicks = new StringBuilder();
            var staggerAir = new StringBuilder();
            var endVsLand = new StringBuilder();
            var airMove = new StringBuilder();
            var finals = new StringBuilder();
            var drift = new StringBuilder();
            var space = recording.Space!;
            var landedHeightMax = -1.0;
            var landedAirMax = -1.0;
            foreach (var l in air.Landed)
            {
                landedHeightMax = Math.Max(landedHeightMax, l.Height);
                landedAirMax = Math.Max(landedAirMax, l.AirSeconds);
            }

            var peakMax = 0.0;
            var launchesMax = 0;
            var staggerAirMax = 0;
            var endVsLandMin = int.MaxValue;
            var driftMax = 0.0;
            var airStepSum = 0.0;
            var airStepCount = 0;
            var groundStepMax = 0.0;
            var displacementMax = double.NegativeInfinity;

            foreach (var pair in air.DummyAirborne)
            {
                var label = pair.Key;
                var airborne = pair.Value;
                var speeds = air.DummySpeeds[label];
                var staggered = air.DummyStaggered[label];
                var positions = air.DummyPositions[label];
                var moving = air.DummyMoveActive[label];

                var launchCount = 0;
                var airCount = 0;
                var staggerCount = 0;
                var staggerAirCount = 0;
                var moveSteps = 0;
                var moveDx = 0.0;
                var firstLand = -1;
                var staggerEnd = -1;
                var everStaggered = false;
                for (var t = 0; t < airborne.Count; t++)
                {
                    var a = airborne[t];
                    var was = t > 0 && airborne[t - 1];
                    if (a)
                    {
                        airCount++;
                        if (!was || (t > 0 && speeds[t] > speeds[t - 1] + Eps))
                        {
                            launchCount++;
                        }
                    }
                    else if (was && firstLand < 0)
                    {
                        firstLand = t;
                    }

                    if (staggered[t])
                    {
                        staggerCount++;
                        everStaggered = true;
                        if (a)
                        {
                            staggerAirCount++;
                        }
                    }
                    else if (everStaggered && staggerEnd < 0)
                    {
                        staggerEnd = t;
                    }

                    if (a && was && t > 0 && moving[t])
                    {
                        moveSteps++;
                        moveDx += Math.Abs(positions[t].X - positions[t - 1].X);
                    }
                }

                launchesMax = Math.Max(launchesMax, launchCount);
                staggerAirMax = Math.Max(staggerAirMax, staggerAirCount);
                for (var t = 1; t < airborne.Count; t++)
                {
                    if (!moving[t])
                    {
                        continue;
                    }

                    var step = Math.Abs(positions[t].X - positions[t - 1].X);
                    if (airborne[t] && airborne[t - 1])
                    {
                        airStepSum += step;
                        airStepCount++;
                    }
                    else if (!airborne[t] && !airborne[t - 1] && !staggered[t])
                    {
                        groundStepMax = Math.Max(groundStepMax, step);
                    }
                }

                if (positions.Count > 0)
                {
                    displacementMax = Math.Max(displacementMax, positions[positions.Count - 1].X - positions[0].X);
                }

                Append(launches, label, launchCount.ToString(CultureInfo.InvariantCulture));
                var accepted = air.DummyJumpsAccepted.TryGetValue(label, out var ja) ? ja : 0;
                var refused = air.DummyJumpsRefused.TryGetValue(label, out var jr) ? jr : 0;
                if (accepted + refused > 0)
                {
                    Append(jumps, label, accepted.ToString(CultureInfo.InvariantCulture) + "/" + refused.ToString(CultureInfo.InvariantCulture));
                }

                if (space.DummyHeights.TryGetValue(label, out var heights) && heights.Count > 0)
                {
                    var peak = 0.0;
                    foreach (var h in heights)
                    {
                        peak = Math.Max(peak, h);
                    }

                    Append(peaks, label, Fmt(peak));
                    peakMax = Math.Max(peakMax, peak);
                }

                Append(airTicks, label, airCount.ToString(CultureInfo.InvariantCulture));
                Append(staggerTicks, label, staggerCount.ToString(CultureInfo.InvariantCulture));
                Append(staggerAir, label, staggerAirCount.ToString(CultureInfo.InvariantCulture));
                if (everStaggered && firstLand >= 0)
                {
                    var end = staggerEnd < 0 ? airborne.Count : staggerEnd;
                    Append(endVsLand, label, (end - firstLand).ToString(CultureInfo.InvariantCulture));
                    endVsLandMin = Math.Min(endVsLandMin, end - firstLand);
                }

                if (moveSteps > 0)
                {
                    Append(airMove, label, moveSteps.ToString(CultureInfo.InvariantCulture) + "/" + Fmt(moveDx));
                }

                if (positions.Count > 0)
                {
                    var last = positions[positions.Count - 1];
                    Append(finals, label, Fmt(last.X) + "/" + Fmt(last.Y));
                    var maxDrift = 0.0;
                    foreach (var p in positions)
                    {
                        maxDrift = Math.Max(maxDrift, Math.Abs(p.Y - positions[0].Y));
                    }

                    Append(drift, label, Fmt(maxDrift));
                    driftMax = Math.Max(driftMax, maxDrift);
                }
            }

            sink.Add("dummy_launches", launches.ToString());
            sink.Add("dummy_jumps", jumps.ToString());
            sink.Add("dummy_peak_heights", peaks.ToString());
            sink.Add("dummy_air_ticks", airTicks.ToString());
            sink.Add("dummy_stagger_ticks", staggerTicks.ToString());
            sink.Add("dummy_stagger_air_ticks", staggerAir.ToString());
            sink.Add("dummy_stagger_end_vs_land", endVsLand.ToString());
            sink.Add("dummy_air_move", airMove.ToString());
            sink.Add("dummy_final_position", finals.ToString());
            sink.Add("dummy_depth_drift", drift.ToString());
            sink.Add("landed_height_max", landedHeightMax);
            sink.Add("landed_air_seconds_max", landedAirMax);
            sink.Add("dummy_peak_max", peakMax);
            sink.Add("dummy_launches_max", launchesMax);
            sink.Add("dummy_stagger_air_ticks_max", staggerAirMax);
            sink.Add("dummy_stagger_end_vs_land_min", endVsLandMin == int.MaxValue ? -9999 : endVsLandMin);
            sink.Add("dummy_depth_drift_max", driftMax);
            sink.Add(
                "dummy_air_ground_step_ratio",
                airStepCount > 0 && groundStepMax > Eps
                    ? (airStepSum / airStepCount) / groundStepMax
                    : -1.0);
            sink.Add("dummy_displacement_x_max", double.IsNegativeInfinity(displacementMax) ? 0.0 : displacementMax);
        }

        private static void Append(StringBuilder sb, string label, string value) => sb.Append(label).Append('=').Append(value).Append(';');

        private static string Fmt(double value) => MetricSink.Round(value).ToString("R", CultureInfo.InvariantCulture);
    }
}
