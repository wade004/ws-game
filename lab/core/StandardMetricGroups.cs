using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Lab
{
    /// <summary>
    /// 度量计算共用的逐 tick 位移视图：<c>Distance[t]</c> 是第 t 步相对上一步（第 0 步相对出生点）的位移长度。
    /// </summary>
    internal sealed class TickMotion
    {
        public const double Eps = 1e-9;

        public int Count { get; }

        public double[] Distance { get; }

        public Vec2[] Displacement { get; }

        public bool[] Requested { get; }

        public Vec2[] Axis { get; }

        public double StepSeconds { get; }

        public TickMotion(LabRecording recording)
        {
            Count = recording.Ticks.Count;
            StepSeconds = recording.StepSeconds;
            Distance = new double[Count];
            Displacement = new Vec2[Count];
            Requested = new bool[Count];
            Axis = new Vec2[Count];
            var previous = recording.StartPosition;
            for (var t = 0; t < Count; t++)
            {
                var sample = recording.Ticks[t];
                var disp = sample.Position - previous;
                Displacement[t] = disp;
                Distance[t] = disp.Length;
                Requested[t] = sample.MoveRequested;
                Axis[t] = sample.MoveAxis;
                previous = sample.Position;
            }
        }

        /// <summary>全部请求移动的步里的最大单步位移（自由移动的满步长）。</summary>
        public double FullStep()
        {
            var max = 0.0;
            for (var t = 0; t < Count; t++)
            {
                if (Requested[t] && Distance[t] > max)
                {
                    max = Distance[t];
                }
            }

            return max;
        }

        public int FirstRequested()
        {
            for (var t = 0; t < Count; t++)
            {
                if (Requested[t])
                {
                    return t;
                }
            }

            return -1;
        }

        public int LastRequested()
        {
            for (var t = Count - 1; t >= 0; t--)
            {
                if (Requested[t])
                {
                    return t;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// 响应组：输入到效果的延迟。逻辑类：从"宿主第一次提交移动请求"到"玩家位置第一次改变"的 tick 数、
    /// 从"施放意图提交"到"施放成功/失败事件落地"的 tick 数；表现类：从输入时刻到假 View 位姿第一次变化的帧数与毫秒数
    /// （模拟时间，不是墙钟）。取不到（脚本没有对应输入或没有响应）统一记 -1。
    /// </summary>
    public sealed class ResponseMetricGroup : IMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("move_response_ticks", MetricClass.Logic, "第一次移动请求到玩家位置第一次改变的 tick 数"),
            MetricSpec.Exact("cast_response_ticks", MetricClass.Logic, "每条施放意图到其施放成功/失败事件的 tick 数（按提交顺序配对）"),
            MetricSpec.Exact("cast_response_ticks_max", MetricClass.Logic, "上一项的最大值"),
            MetricSpec.Absolute("visible_move_frames", MetricClass.Presentation, 1, "第一次移动输入到假 View 位姿第一次变化的帧数"),
            MetricSpec.Absolute("visible_move_ms", MetricClass.Presentation, 17, "同上，换算成模拟毫秒（输入时刻到该帧时刻）"),
        };

        public string Name => "response";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var motion = new TickMotion(recording);
            var first = motion.FirstRequested();
            var moveResponse = -1;
            if (first >= 0)
            {
                for (var t = first; t < motion.Count; t++)
                {
                    if (motion.Distance[t] > TickMotion.Eps)
                    {
                        moveResponse = t - first;
                        break;
                    }
                }
            }

            sink.Add("move_response_ticks", moveResponse);

            var outcomes = new List<LogicEventRecord>();
            foreach (var e in recording.Events)
            {
                if (e.Kind == "cast_success" || e.Kind == "cast_failed")
                {
                    outcomes.Add(e);
                }
            }

            var latencies = new List<double>();
            var max = -1.0;
            for (var i = 0; i < recording.Intents.Count && i < outcomes.Count; i++)
            {
                var latency = outcomes[i].Tick - recording.Intents[i].Tick;
                latencies.Add(latency);
                max = Math.Max(max, latency);
            }

            sink.Add("cast_response_ticks", latencies);
            sink.Add("cast_response_ticks_max", max);

            var visibleFrames = -1;
            var visibleMs = -1.0;
            if (first >= 0 && recording.Frames.Count > 0)
            {
                var inputTime = first * recording.StepSeconds;
                var startFrame = -1;
                var basePos = default(Vec2);
                var haveBase = false;
                for (var i = 0; i < recording.Frames.Count; i++)
                {
                    var f = recording.Frames[i];
                    if (f.Time <= inputTime && f.HasPose)
                    {
                        basePos = f.ViewPosition;
                        haveBase = true;
                    }

                    if (startFrame < 0 && f.Time >= inputTime)
                    {
                        startFrame = i;
                    }
                }

                if (!haveBase)
                {
                    basePos = recording.StartPosition;
                }

                if (startFrame >= 0)
                {
                    for (var i = startFrame; i < recording.Frames.Count; i++)
                    {
                        var f = recording.Frames[i];
                        if (f.HasPose && (f.ViewPosition - basePos).Length > TickMotion.Eps)
                        {
                            visibleFrames = i - startFrame;
                            visibleMs = (f.Time - inputTime) * 1000.0;
                            break;
                        }
                    }
                }
            }

            sink.Add("visible_move_frames", visibleFrames);
            sink.Add("visible_move_ms", visibleMs);
        }
    }

    /// <summary>
    /// 移动组：速度曲线、起停、反向、斜向、贴墙。全部是逻辑类度量（只读逐 tick 位置快照）。
    /// "受阻"指宿主提交了移动请求但本步位移明显小于自由步长（小于满步长的一半）；被墙截短但未停的步记入"局部步"。
    /// </summary>
    public sealed class MovementMetricGroup : IMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("ticks_requested", MetricClass.Logic, "宿主提交了移动请求的 tick 数"),
            MetricSpec.Exact("path_length", MetricClass.Logic, "逐步位移长度之和"),
            MetricSpec.Exact("net_displacement", MetricClass.Logic, "终点到出生点的直线距离"),
            MetricSpec.Exact("speed_steady", MetricClass.Logic, "自由移动速度（最大单步位移 / 步长）"),
            MetricSpec.Exact("accel_ticks", MetricClass.Logic, "第一次移动请求到达到满步长的 tick 数（0 表示起步即满速）"),
            MetricSpec.Exact("stop_ticks", MetricClass.Logic, "最后一次移动请求之后继续滑行的 tick 数（-1 表示脚本没有留出停止段）"),
            MetricSpec.Exact("stop_distance", MetricClass.Logic, "最后一次移动请求之后继续滑行的距离"),
            MetricSpec.Exact("reverse_ticks", MetricClass.Logic, "输入反向后位移方向跟上新输入的 tick 数（-1 表示脚本没有反向）"),
            MetricSpec.Exact("reversal_count", MetricClass.Logic, "相邻两步位移方向相反的次数"),
            MetricSpec.Exact("diag_speed_ratio", MetricClass.Logic, "斜向最大单步位移 / 正向最大单步位移（-1 表示脚本缺其一）"),
            MetricSpec.Exact("min_axis_magnitude_moved", MetricClass.Logic, "产生了位移的步里最小的轴模长（反映死区/阈值，-1 表示没有移动）"),
            MetricSpec.Exact("sub_threshold_ticks", MetricClass.Logic, "脚本轴非零但低于宿主阈值、没有提交移动请求的 tick 数"),
            MetricSpec.Exact("partial_step_ticks", MetricClass.Logic, "位移大于零但小于满步长的请求步数（被墙/障碍截短）"),
            MetricSpec.Exact("blocked_ticks", MetricClass.Logic, "提交了移动请求但位移为零的 tick 数"),
            MetricSpec.Exact("obstructed_speed_fraction", MetricClass.Logic, "受阻期间平均速度占满速的比例（0 表示不沿墙滑动，-1 表示没有受阻）"),
            MetricSpec.Exact("obstructed_jitter_ticks", MetricClass.Logic, "受阻期间仍有位移的步数（贴墙抖动/蠕动）"),
        };

        public string Name => "movement";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var m = new TickMotion(recording);
            var full = m.FullStep();
            var step = recording.StepSeconds;

            var requestedTicks = 0;
            var path = 0.0;
            var partial = 0;
            var blocked = 0;
            var obstructed = 0;
            var obstructedDistance = 0.0;
            var jitter = 0;
            var maxDiag = 0.0;
            var maxCard = 0.0;
            var minAxis = double.MaxValue;
            for (var t = 0; t < m.Count; t++)
            {
                path += m.Distance[t];
                if (!m.Requested[t])
                {
                    continue;
                }

                requestedTicks++;
                var d = m.Distance[t];
                if (d <= TickMotion.Eps)
                {
                    blocked++;
                }
                else
                {
                    minAxis = Math.Min(minAxis, m.Axis[t].Length);
                    if (d < 0.999 * full)
                    {
                        partial++;
                    }
                }

                if (d < 0.5 * full)
                {
                    obstructed++;
                    obstructedDistance += d;
                    if (d > TickMotion.Eps)
                    {
                        jitter++;
                    }
                }

                var diagonal = Math.Abs(m.Axis[t].X) > 0.01 && Math.Abs(m.Axis[t].Y) > 0.01;
                if (diagonal)
                {
                    maxDiag = Math.Max(maxDiag, d);
                }
                else
                {
                    maxCard = Math.Max(maxCard, d);
                }
            }

            var net = m.Count == 0 ? 0.0 : (recording.Ticks[m.Count - 1].Position - recording.StartPosition).Length;
            sink.Add("ticks_requested", requestedTicks);
            sink.Add("path_length", path);
            sink.Add("net_displacement", net);
            sink.Add("speed_steady", full / step);

            var firstReq = m.FirstRequested();
            var accel = -1;
            if (firstReq >= 0)
            {
                for (var t = firstReq; t < m.Count; t++)
                {
                    if (m.Requested[t] && m.Distance[t] >= 0.999 * full && full > 0)
                    {
                        accel = t - firstReq;
                        break;
                    }
                }
            }

            sink.Add("accel_ticks", accel);

            var lastReq = m.LastRequested();
            if (lastReq < 0 || lastReq >= m.Count - 1)
            {
                sink.Add("stop_ticks", -1);
                sink.Add("stop_distance", -1.0);
            }
            else
            {
                var stopTicks = 0;
                while (lastReq + 1 + stopTicks < m.Count && m.Distance[lastReq + 1 + stopTicks] > TickMotion.Eps)
                {
                    stopTicks++;
                }

                var stopDistance = 0.0;
                for (var t = lastReq + 1; t < m.Count; t++)
                {
                    stopDistance += m.Distance[t];
                }

                sink.Add("stop_ticks", stopTicks);
                sink.Add("stop_distance", stopDistance);
            }

            var reverse = -1;
            for (var t = 1; t < m.Count && reverse < 0; t++)
            {
                if (m.Requested[t] && m.Requested[t - 1] && m.Axis[t - 1].Dot(m.Axis[t]) < 0)
                {
                    reverse = int.MaxValue;
                    for (var u = t; u < m.Count; u++)
                    {
                        if (m.Displacement[u].Dot(m.Axis[t]) > TickMotion.Eps)
                        {
                            reverse = u - t;
                            break;
                        }
                    }

                    if (reverse == int.MaxValue)
                    {
                        reverse = -1;
                        break;
                    }
                }
            }

            sink.Add("reverse_ticks", reverse);

            var reversals = 0;
            for (var t = 1; t < m.Count; t++)
            {
                if (m.Distance[t - 1] > TickMotion.Eps && m.Distance[t] > TickMotion.Eps
                    && m.Displacement[t - 1].Dot(m.Displacement[t]) < 0)
                {
                    reversals++;
                }
            }

            sink.Add("reversal_count", reversals);
            sink.Add("diag_speed_ratio", maxDiag > 0 && maxCard > 0 ? maxDiag / maxCard : -1.0);
            sink.Add("min_axis_magnitude_moved", minAxis == double.MaxValue ? -1.0 : minAxis);
            sink.Add("sub_threshold_ticks", CountSubThreshold(recording, m));
            sink.Add("partial_step_ticks", partial);
            sink.Add("blocked_ticks", blocked);
            sink.Add("obstructed_speed_fraction", obstructed > 0 && full > 0 ? obstructedDistance / (obstructed * full) : -1.0);
            sink.Add("obstructed_jitter_ticks", jitter);
        }

        private static int CountSubThreshold(LabRecording recording, TickMotion m)
        {
            // 把注入的轴事件展开成逐 tick 的"脚本轴值"（事件之间保持不变）。
            var current = Vec2.Zero;
            var cursor = 0;
            var events = new List<ScriptEvent>();
            foreach (var e in recording.InjectedInputs)
            {
                if (e.Kind == ScriptEventKind.Axis)
                {
                    events.Add(e);
                }
            }

            var count = 0;
            for (var t = 0; t < m.Count; t++)
            {
                while (cursor < events.Count && events[cursor].Tick <= t)
                {
                    current = events[cursor].Value;
                    cursor++;
                }

                if (current.SqrLength > 0 && !m.Requested[t])
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// 攻击组：施放与结算。攻击实例按首次出现顺序编号；"结算 tick"是该实例第一次伤害事件相对其施放意图的
    /// tick 差（实例与成功施放按顺序配对）；"去重违例"是同一实例对同一目标重复结算的多余次数。
    /// </summary>
    public sealed class AttackMetricGroup : IMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("casts_submitted", MetricClass.Logic, "宿主提交的施放意图数"),
            MetricSpec.Exact("cast_success", MetricClass.Logic, "施放成功事件数"),
            MetricSpec.Exact("cast_failed", MetricClass.Logic, "施放失败事件数"),
            MetricSpec.Exact("cast_failed_reasons", MetricClass.Logic, "失败原因计数（按原因名排序，名:个数;…）"),
            MetricSpec.Exact("attack_instances", MetricClass.Logic, "产生了伤害或回避结果的攻击实例数"),
            MetricSpec.Exact("damage_events", MetricClass.Logic, "伤害事件数"),
            MetricSpec.Exact("damage_total", MetricClass.Logic, "伤害总量"),
            MetricSpec.Exact("targets_hit", MetricClass.Logic, "被击中的不同靶子数"),
            MetricSpec.Exact("kills", MetricClass.Logic, "死亡事件数"),
            MetricSpec.Exact("settle_ticks", MetricClass.Logic, "每个攻击实例的结算 tick 差（施放意图到首次伤害）"),
            MetricSpec.Exact("settle_ticks_max", MetricClass.Logic, "上一项最大值（-1 表示没有实例）"),
            MetricSpec.Exact("hits_per_instance_max", MetricClass.Logic, "单个实例的伤害事件数上限"),
            MetricSpec.Exact("dedupe_violations", MetricClass.Logic, "同一实例对同一目标的重复结算次数（应恒为 0）"),
        };

        public string Name => "attack";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var success = 0;
            var failed = 0;
            var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var damageEvents = 0;
            var damageTotal = 0.0;
            var kills = 0;
            var targets = new HashSet<string>(StringComparer.Ordinal);
            var instanceHits = new Dictionary<int, int>();
            var instanceFirstTick = new Dictionary<int, int>();
            var instanceOrder = new List<int>();
            var perInstanceTarget = new Dictionary<string, int>(StringComparer.Ordinal);
            var dedupe = 0;
            var outcomes = new List<LogicEventRecord>();

            foreach (var e in recording.Events)
            {
                switch (e.Kind)
                {
                    case "cast_success":
                        success++;
                        outcomes.Add(e);
                        break;
                    case "cast_failed":
                        failed++;
                        outcomes.Add(e);
                        reasons.TryGetValue(e.Detail, out var n);
                        reasons[e.Detail] = n + 1;
                        break;
                    case "damage":
                    case "avoided":
                        if (e.Kind == "damage")
                        {
                            damageEvents++;
                            damageTotal += e.Amount;
                            targets.Add(e.Target);
                        }

                        if (!instanceHits.ContainsKey(e.Instance))
                        {
                            instanceHits[e.Instance] = 0;
                            instanceFirstTick[e.Instance] = e.Tick;
                            instanceOrder.Add(e.Instance);
                        }

                        instanceHits[e.Instance]++;
                        var key = e.Instance + "|" + e.Target;
                        perInstanceTarget.TryGetValue(key, out var seen);
                        if (seen > 0)
                        {
                            dedupe++;
                        }

                        perInstanceTarget[key] = seen + 1;
                        break;
                    case "died":
                        kills++;
                        break;
                }
            }

            // 实例与"成功施放对应的意图"按顺序配对。
            var successIntentTicks = new List<int>();
            for (var i = 0; i < recording.Intents.Count && i < outcomes.Count; i++)
            {
                if (outcomes[i].Kind == "cast_success")
                {
                    successIntentTicks.Add(recording.Intents[i].Tick);
                }
            }

            var settle = new List<double>();
            for (var i = 0; i < instanceOrder.Count && i < successIntentTicks.Count; i++)
            {
                settle.Add(instanceFirstTick[instanceOrder[i]] - successIntentTicks[i]);
            }

            var maxSettle = -1.0;
            foreach (var s in settle)
            {
                maxSettle = Math.Max(maxSettle, s);
            }

            var hitsMax = 0;
            foreach (var pair in instanceHits)
            {
                hitsMax = Math.Max(hitsMax, pair.Value);
            }

            var reasonParts = new List<string>();
            foreach (var pair in reasons)
            {
                reasonParts.Add(pair.Key + ":" + pair.Value);
            }

            sink.Add("casts_submitted", recording.Intents.Count);
            sink.Add("cast_success", success);
            sink.Add("cast_failed", failed);
            sink.Add("cast_failed_reasons", string.Join(";", reasonParts));
            sink.Add("attack_instances", instanceOrder.Count);
            sink.Add("damage_events", damageEvents);
            sink.Add("damage_total", damageTotal);
            sink.Add("targets_hit", targets.Count);
            sink.Add("kills", kills);
            sink.Add("settle_ticks", settle);
            sink.Add("settle_ticks_max", maxSettle);
            sink.Add("hits_per_instance_max", hitsMax);
            sink.Add("dedupe_violations", dedupe);
        }
    }

    /// <summary>
    /// 性能组：tick 数与总线事件总数是逻辑类（逐字节一致）；帧数是表现类；帧耗时与分配是实时类，
    /// 只检查"不超过基线若干倍"的上限——墙钟与分配计数依赖机器与即时编译，不能字节比较。
    /// </summary>
    public sealed class PerformanceMetricGroup : IMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("ticks", MetricClass.Logic, "宿主固定步总数"),
            MetricSpec.Exact("event_count_total", MetricClass.Logic, "总线派发的事件总数"),
            MetricSpec.Absolute("frames", MetricClass.Presentation, 1, "表现帧总数"),
            MetricSpec.RatioCeiling("frame_ms_p50", MetricClass.RealTime, 10, 50, "每帧（含固定步与表现同步）耗时中位数，毫秒（10 倍数量级桶上界）"),
            MetricSpec.RatioCeiling("frame_ms_p95", MetricClass.RealTime, 10, 50, "每帧耗时 95 分位，毫秒（10 倍数量级桶上界）"),
            MetricSpec.RatioCeiling("alloc_bytes_per_frame_p95", MetricClass.RealTime, 4, 200000, "每帧分配字节数 95 分位（2 倍桶上界）"),
        };

        public string Name => "performance";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public void Compute(LabRecording recording, MetricSink sink)
        {
            sink.Add("ticks", recording.Ticks.Count);
            sink.Add("event_count_total", recording.TotalEventCount);
            sink.Add("frames", recording.Frames.Count);
            var ms = new List<double>(recording.Real.FrameMilliseconds);
            var alloc = new List<double>();
            foreach (var a in recording.Real.FrameAllocatedBytes)
            {
                alloc.Add(a);
            }

            // 实时类度量按"数量级桶"记录（毫秒按 10 倍、字节按 2 倍向上取整到桶上界）：墙钟与分配计数每次跑都不同，
            // 记原始值会让每次重写基线都改动全部基线文件；桶值在同一台机器上基本稳定，且上限检查按桶值给倍率余量。
            sink.Add("frame_ms_p50", Bucket(Percentile(ms, 0.50), 0.1, 10));
            sink.Add("frame_ms_p95", Bucket(Percentile(ms, 0.95), 0.1, 10));
            sink.Add("alloc_bytes_per_frame_p95", Bucket(Percentile(alloc, 0.95), 1024, 2));
        }

        /// <summary>向上取整到 <c>start * factor^k</c> 的桶上界（k &gt;= 0）。</summary>
        internal static double Bucket(double value, double start, double factor)
        {
            var bucket = start;
            while (bucket < value)
            {
                bucket *= factor;
            }

            return bucket;
        }

        /// <summary>最近秩百分位（无插值，确定性）。</summary>
        internal static double Percentile(List<double> values, double p)
        {
            if (values.Count == 0)
            {
                return 0;
            }

            var sorted = new List<double>(values);
            sorted.Sort();
            var rank = (int)Math.Ceiling(p * sorted.Count) - 1;
            if (rank < 0)
            {
                rank = 0;
            }

            if (rank >= sorted.Count)
            {
                rank = sorted.Count - 1;
            }

            return sorted[rank];
        }
    }
}
