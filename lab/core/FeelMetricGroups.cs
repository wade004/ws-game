using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common;

namespace Lab
{
    /// <summary>手感度量组共用的小工具。</summary>
    internal static class FeelMetricUtil
    {
        public static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Num(double value) => MetricSink.Round(value).ToString("R", CultureInfo.InvariantCulture);

        public static string Join(IEnumerable<string> parts) => string.Join(";", parts);

        public static string Short(string actionId) =>
            actionId.StartsWith("input.action.", StringComparison.Ordinal) ? actionId.Substring("input.action.".Length) : actionId;

        /// <summary>按名字排序后 <c>名:个数;…</c>。</summary>
        public static string Counts(Dictionary<string, int> counts)
        {
            var names = new List<string>(counts.Keys);
            names.Sort(StringComparer.Ordinal);
            var parts = new List<string>(names.Count);
            foreach (var name in names)
            {
                parts.Add(name + ":" + Num(counts[name]));
            }

            return Join(parts);
        }

        public static void Bump(Dictionary<string, int> counts, string key)
        {
            counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
        }

        /// <summary>从 <c>a=b;c=d</c> 形式的细节里取一个键。</summary>
        public static string Field(string detail, string key)
        {
            var needle = key + "=";
            foreach (var part in detail.Split(';'))
            {
                if (part.StartsWith(needle, StringComparison.Ordinal))
                {
                    return part.Substring(needle.Length);
                }
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// 输入缓冲组（手感设计 06 第 3.3 节"响应"的缓冲部分，条件组：只对手感场景适用）：入槽 / 消费 / 过期 tick。
    /// 约定：入槽 tick = 脚本按下事件被注入的 tick（按钮边沿同 tick 进缓冲）；消费 tick = 该输入换来的动作开始 tick
    /// （按下与动作开始按先进先出配对，丢弃事件按动作名摘掉最早的待配对按下）；"可见入槽"指 tick 末的缓冲槽快照里还看得到它，
    /// 即同 tick 内没被消费、留到了下一个 tick 以后。
    /// </summary>
    public sealed class InputBufferMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("press_ticks", MetricClass.Logic, "按下注入的 tick（tick:动作；移动轴不计）"),
            MetricSpec.Exact("slot_visible_ticks", MetricClass.Logic, "tick 末缓冲槽里新出现某动作的 tick（tick:动作；同 tick 即被消费的不在其中）"),
            MetricSpec.Exact("consume_ticks", MetricClass.Logic, "玩家动作开始 tick（tick:技能）"),
            MetricSpec.Exact("accept_latency_ticks", MetricClass.Logic, "每次动作开始相对其按下的 tick 数（先进先出配对；被丢弃的按下不参与）"),
            MetricSpec.Exact("accept_latency_max", MetricClass.Logic, "上一项最大值（-1 表示没有动作开始）"),
            MetricSpec.Exact("drop_events", MetricClass.Logic, "缓冲记录离开缓冲的事件（tick:动作:原因）"),
            MetricSpec.Exact("drop_counts", MetricClass.Logic, "离开原因计数（按原因名排序）"),
            MetricSpec.Exact("expire_ticks", MetricClass.Logic, "因过期离开缓冲的 tick（tick:动作）"),
            MetricSpec.Exact("max_slot_occupancy", MetricClass.Logic, "任一 tick 末缓冲槽里记录数的最大值"),
            MetricSpec.Exact("slot_occupied_ticks", MetricClass.Logic, "tick 末缓冲槽非空的 tick 数"),
        };

        public string Name => "inputbuf";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) => recording.Feel != null;

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var feel = recording.Feel!;
            var presses = new List<(int Tick, string Action)>();
            foreach (var e in recording.InjectedInputs)
            {
                if (e.Kind == ScriptEventKind.Press && e.Actor.Length == 0 && !e.Action.EndsWith(".move", StringComparison.Ordinal))
                {
                    presses.Add((e.Tick, FeelMetricUtil.Short(e.Action)));
                }
            }

            var pressParts = new List<string>();
            foreach (var press in presses)
            {
                pressParts.Add(FeelMetricUtil.Num(press.Tick) + ":" + press.Action);
            }

            var visible = new List<string>();
            var previous = new HashSet<string>(StringComparer.Ordinal);
            var maxOccupancy = 0;
            var occupiedTicks = 0;
            foreach (var t in feel.Ticks)
            {
                var current = new HashSet<string>(StringComparer.Ordinal);
                var count = 0;
                if (t.BufferSlots.Length > 0)
                {
                    foreach (var name in t.BufferSlots.Split('|'))
                    {
                        current.Add(name);
                        count++;
                    }

                    occupiedTicks++;
                }

                foreach (var name in current)
                {
                    if (!previous.Contains(name))
                    {
                        visible.Add(FeelMetricUtil.Num(t.Tick) + ":" + name);
                    }
                }

                if (count > maxOccupancy)
                {
                    maxOccupancy = count;
                }

                previous = current;
            }

            // 先进先出配对：按下队列按 tick 排；丢弃事件按动作名摘掉最早的待配对按下；动作开始取最早的待配对按下。
            var pending = new List<(int Tick, string Action)>(presses);
            var consumeParts = new List<string>();
            var latencies = new List<double>();
            var dropParts = new List<string>();
            var dropCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var expireParts = new List<string>();
            foreach (var e in feel.Events)
            {
                if (e.Kind == "buffer_dropped" && e.Actor == "player")
                {
                    var action = FeelMetricUtil.Short(e.Detail);
                    dropParts.Add(FeelMetricUtil.Num(e.Tick) + ":" + action + ":" + e.Detail2);
                    FeelMetricUtil.Bump(dropCounts, e.Detail2);
                    if (e.Detail2 == "Expired")
                    {
                        expireParts.Add(FeelMetricUtil.Num(e.Tick) + ":" + action);
                    }

                    var index = pending.FindIndex(p => p.Tick <= e.Tick && p.Action == action);
                    if (index >= 0)
                    {
                        pending.RemoveAt(index);
                    }
                }
                else if (e.Kind == "action_started" && e.Actor == "player")
                {
                    consumeParts.Add(FeelMetricUtil.Num(e.Tick) + ":" + e.SkillId);
                    var index = pending.FindIndex(p => p.Tick <= e.Tick);
                    if (index >= 0)
                    {
                        latencies.Add(e.Tick - pending[index].Tick);
                        pending.RemoveAt(index);
                    }
                }
            }

            var max = -1.0;
            foreach (var l in latencies)
            {
                if (l > max)
                {
                    max = l;
                }
            }

            sink.Add("press_ticks", FeelMetricUtil.Join(pressParts));
            sink.Add("slot_visible_ticks", FeelMetricUtil.Join(visible));
            sink.Add("consume_ticks", FeelMetricUtil.Join(consumeParts));
            sink.Add("accept_latency_ticks", latencies);
            sink.Add("accept_latency_max", max);
            sink.Add("drop_events", FeelMetricUtil.Join(dropParts));
            sink.Add("drop_counts", FeelMetricUtil.Counts(dropCounts));
            sink.Add("expire_ticks", FeelMetricUtil.Join(expireParts));
            sink.Add("max_slot_occupancy", maxOccupancy);
            sink.Add("slot_occupied_ticks", occupiedTicks);
        }
    }

    /// <summary>
    /// 动作时间线组（条件组）：每个动作的开始 tick、各相实际 tick 数、标记、取消、结束，以及连招段与目标辅助。
    /// 各相 tick 数 = 相变事件之间的 tick 差（最后一相到结束/取消事件）；蓄力相单列。
    /// </summary>
    public sealed class ActionTimelineMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("starts", MetricClass.Logic, "动作开始（行动者@tick:技能#连招序号）"),
            MetricSpec.Exact("phase_ticks", MetricClass.Logic, "每个动作各相实际 tick 数（行动者:技能:charge/startup/active/recovery 的 tick 数，/ 分隔）"),
            MetricSpec.Exact("action_total_ticks", MetricClass.Logic, "每个动作从开始到结束/取消的 tick 数（与开始顺序一一对应）"),
            MetricSpec.Exact("markers", MetricClass.Logic, "标记事件（行动者@tick:标记名）"),
            MetricSpec.Exact("cancels", MetricClass.Logic, "动作被取消（行动者@tick:原因[>下一技能]）"),
            MetricSpec.Exact("finishes", MetricClass.Logic, "动作自然结束（行动者@tick）"),
            MetricSpec.Exact("combo_chain", MetricClass.Logic, "玩家连招链：连招序号递增的相邻动作串成的技能序列，多条以 ; 分隔"),
            MetricSpec.Exact("combo_segments", MetricClass.Logic, "玩家连招序号大于 0 的动作开始数"),
            MetricSpec.Exact("charge_ratios", MetricClass.Logic, "玩家每次动作开始携带的蓄力比例"),
            MetricSpec.Exact("target_assists", MetricClass.Logic, "目标辅助事件（行动者@tick:目标，朝向修正弧度）"),
            MetricSpec.Exact("unfinished_at_end", MetricClass.Logic, "运行结束时仍在进行（没结束也没被取消）的动作数"),
        };

        public string Name => "actiontl";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) => recording.Feel != null;

        private sealed class Run
        {
            public string Actor = string.Empty;
            public string Skill = string.Empty;
            public int Start;
            public int End = -1;
            public readonly List<(string Phase, int Tick)> Phases = new List<(string, int)>();
        }

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var feel = recording.Feel!;
            var runs = new List<Run>();
            var open = new Dictionary<string, Run>(StringComparer.Ordinal);
            var starts = new List<string>();
            var markers = new List<string>();
            var cancels = new List<string>();
            var finishes = new List<string>();
            var assists = new List<string>();
            var ratios = new List<double>();
            var comboSegments = 0;
            var chains = new List<string>();
            var currentChain = new List<string>();
            var lastCombo = -1;

            foreach (var e in feel.Events)
            {
                switch (e.Kind)
                {
                    case "action_started":
                    {
                        var run = new Run { Actor = e.Actor, Skill = e.SkillId, Start = e.Tick };
                        runs.Add(run);
                        open[e.Actor] = run;
                        starts.Add(e.Actor + "@" + FeelMetricUtil.Num(e.Tick) + ":" + e.SkillId + "#" + FeelMetricUtil.Num(e.A));
                        if (e.Actor == "player")
                        {
                            ratios.Add(e.D);
                            if (e.A > 0)
                            {
                                comboSegments++;
                            }

                            if (e.A > 0 && e.A == lastCombo + 1 && currentChain.Count > 0)
                            {
                                currentChain.Add(e.SkillId);
                            }
                            else
                            {
                                if (currentChain.Count > 1)
                                {
                                    chains.Add(string.Join(">", currentChain));
                                }

                                currentChain = new List<string> { e.SkillId };
                            }

                            lastCombo = e.A;
                        }

                        break;
                    }

                    case "action_phase":
                        if (open.TryGetValue(e.Actor, out var phaseRun))
                        {
                            phaseRun.Phases.Add((e.Detail, e.Tick));
                        }

                        break;
                    case "action_marker":
                        markers.Add(e.Actor + "@" + FeelMetricUtil.Num(e.Tick) + ":" + e.Detail);
                        break;
                    case "action_cancelled":
                        cancels.Add(e.Actor + "@" + FeelMetricUtil.Num(e.Tick) + ":" + e.Detail + (e.SkillId.Length > 0 ? ">" + e.SkillId : string.Empty));
                        if (open.TryGetValue(e.Actor, out var cancelled))
                        {
                            cancelled.End = e.Tick;
                            open.Remove(e.Actor);
                        }

                        break;
                    case "action_finished":
                        finishes.Add(e.Actor + "@" + FeelMetricUtil.Num(e.Tick));
                        if (open.TryGetValue(e.Actor, out var finished))
                        {
                            finished.End = e.Tick;
                            open.Remove(e.Actor);
                        }

                        break;
                    case "target_assisted":
                        assists.Add(e.Actor + "@" + FeelMetricUtil.Num(e.Tick) + ":" + e.Target + "," + FeelMetricUtil.Num(e.D));
                        break;
                }
            }

            if (currentChain.Count > 1)
            {
                chains.Add(string.Join(">", currentChain));
            }

            var phaseParts = new List<string>();
            var totals = new List<double>();
            foreach (var run in runs)
            {
                var segments = new List<string>();
                for (var i = 0; i < run.Phases.Count; i++)
                {
                    var end = i + 1 < run.Phases.Count ? run.Phases[i + 1].Tick : run.End;
                    segments.Add(run.Phases[i].Phase.ToLowerInvariant() + "=" + (end >= 0 ? FeelMetricUtil.Num(end - run.Phases[i].Tick) : "open"));
                }

                phaseParts.Add(run.Actor + ":" + run.Skill + ":" + string.Join("/", segments));
                totals.Add(run.End >= 0 ? run.End - run.Start : -1);
            }

            sink.Add("starts", FeelMetricUtil.Join(starts));
            sink.Add("phase_ticks", FeelMetricUtil.Join(phaseParts));
            sink.Add("action_total_ticks", totals);
            sink.Add("markers", FeelMetricUtil.Join(markers));
            sink.Add("cancels", FeelMetricUtil.Join(cancels));
            sink.Add("finishes", FeelMetricUtil.Join(finishes));
            sink.Add("combo_chain", FeelMetricUtil.Join(chains));
            sink.Add("combo_segments", comboSegments);
            sink.Add("charge_ratios", ratios);
            sink.Add("target_assists", FeelMetricUtil.Join(assists));
            sink.Add("unfinished_at_end", open.Count);
        }
    }

    /// <summary>
    /// 顿帧组（条件组）：每次顿帧的作用实体集合与时长、两侧实际冻结 tick 数（运动层 <c>Frozen</c> 模式的 tick 数）、嵌套（叠加到仍在冻结的实体上）次数、
    /// 运行结束时未结束的顿帧数与表现层冻结登记里未释放的句柄数。
    /// </summary>
    public sealed class HitstopMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("started", MetricClass.Logic, "顿帧开始（tick:作用实体集合:时长 tick 数）"),
            MetricSpec.Exact("started_ticks_player", MetricClass.Logic, "含玩家的顿帧开始时长之和（攻击方一侧，请求值）"),
            MetricSpec.Exact("frozen_ticks_player", MetricClass.Logic, "玩家运动层处于 Frozen 的 tick 数（实际冻结）"),
            MetricSpec.Exact("frozen_ticks_targets", MetricClass.Logic, "各靶子运动层处于 Frozen 的 tick 数（靶子标签=tick 数，按出场顺序，只列非零）"),
            MetricSpec.Exact("nested_starts", MetricClass.Logic, "在目标实体上一次顿帧还没结束时又开始新顿帧的次数"),
            MetricSpec.Exact("unended_at_end", MetricClass.Logic, "运行结束时已开始未结束的顿帧数"),
            MetricSpec.Exact("presentation_frozen_at_end", MetricClass.Logic, "运行结束时表现层冻结登记里未释放的单位数"),
        };

        public string Name => "hitstop";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) => recording.Feel != null;

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var feel = recording.Feel!;
            var parts = new List<string>();
            var playerTicks = 0;
            var active = new Dictionary<string, int>(StringComparer.Ordinal);
            var nested = 0;
            var started = 0;
            var ended = 0;
            foreach (var e in feel.Events)
            {
                if (e.Kind == "hitstop_started")
                {
                    started++;
                    parts.Add(FeelMetricUtil.Num(e.Tick) + ":" + e.Detail + ":" + FeelMetricUtil.Num(e.A));
                    foreach (var unit in e.Detail.Split('+'))
                    {
                        if (unit == "player")
                        {
                            playerTicks += e.A;
                        }

                        if (active.TryGetValue(unit, out var n) && n > 0)
                        {
                            nested++;
                        }

                        active[unit] = (active.TryGetValue(unit, out var m) ? m : 0) + 1;
                    }
                }
                else if (e.Kind == "hitstop_ended")
                {
                    ended++;
                    foreach (var unit in e.Detail.Split('+'))
                    {
                        if (active.TryGetValue(unit, out var n) && n > 0)
                        {
                            active[unit] = n - 1;
                        }
                    }
                }
            }

            var player = 0;
            var targets = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var t in feel.Ticks)
            {
                if (t.Mode == "Frozen")
                {
                    player++;
                }

                foreach (var pair in t.TargetModes)
                {
                    if (pair.Value == "Frozen")
                    {
                        if (!targets.ContainsKey(pair.Key))
                        {
                            order.Add(pair.Key);
                        }

                        FeelMetricUtil.Bump(targets, pair.Key);
                    }
                }
            }

            var targetParts = new List<string>();
            foreach (var label in order)
            {
                targetParts.Add(label + "=" + FeelMetricUtil.Num(targets[label]));
            }

            sink.Add("started", FeelMetricUtil.Join(parts));
            sink.Add("started_ticks_player", playerTicks);
            sink.Add("frozen_ticks_player", player);
            sink.Add("frozen_ticks_targets", FeelMetricUtil.Join(targetParts));
            sink.Add("nested_starts", nested);
            sink.Add("unended_at_end", started - ended);
            sink.Add("presentation_frozen_at_end", feel.FrozenAtEnd);
        }
    }

    /// <summary>受击反应组（条件组）：命中结局分布、反应分布与时长、各单位处于硬直的 tick 数、击杀数。</summary>
    public sealed class HitReactionMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("hit_results", MetricClass.Logic, "命中确认的结局分布（结局:个数，按名排序）"),
            MetricSpec.Exact("reaction_counts", MetricClass.Logic, "命中确认里受击反应的分布（反应:个数，按名排序）"),
            MetricSpec.Exact("reactions", MetricClass.Logic, "反应落地事件（tick:受击者:反应:硬直 tick 数）"),
            MetricSpec.Exact("staggered_ticks", MetricClass.Logic, "各单位运动层处于 Staggered 的 tick 数（标签=tick 数，玩家第一，其后按靶子出场顺序，只列非零）"),
            MetricSpec.Exact("kills", MetricClass.Logic, "击杀命中数（命中确认的击杀标记）"),
            MetricSpec.Exact("hit_classes", MetricClass.Logic, "命中的冲击等级序列（按命中确认顺序）"),
        };

        public string Name => "reaction";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) => recording.Feel != null;

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var feel = recording.Feel!;
            var results = new Dictionary<string, int>(StringComparer.Ordinal);
            var reactions = new Dictionary<string, int>(StringComparer.Ordinal);
            var reactionParts = new List<string>();
            var classes = new List<string>();
            var kills = 0;
            foreach (var e in feel.Events)
            {
                if (e.Kind == "hit_confirmed")
                {
                    FeelMetricUtil.Bump(results, e.Detail);
                    FeelMetricUtil.Bump(reactions, FeelMetricUtil.Field(e.Detail2, "reaction"));
                    classes.Add(FeelMetricUtil.Field(e.Detail2, "class"));
                    if (FeelMetricUtil.Field(e.Detail2, "kill") == "1")
                    {
                        kills++;
                    }
                }
                else if (e.Kind == "reaction")
                {
                    reactionParts.Add(FeelMetricUtil.Num(e.Tick) + ":" + e.Target + ":" + e.Detail + ":" + FeelMetricUtil.Num(e.A));
                }
            }

            var staggered = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var t in feel.Ticks)
            {
                if (t.Mode == "Staggered")
                {
                    if (!staggered.ContainsKey("player"))
                    {
                        order.Add("player");
                    }

                    FeelMetricUtil.Bump(staggered, "player");
                }

                foreach (var pair in t.TargetModes)
                {
                    if (pair.Value == "Staggered")
                    {
                        if (!staggered.ContainsKey(pair.Key))
                        {
                            order.Add(pair.Key);
                        }

                        FeelMetricUtil.Bump(staggered, pair.Key);
                    }
                }
            }

            var staggeredParts = new List<string>();
            foreach (var label in order)
            {
                staggeredParts.Add(label + "=" + FeelMetricUtil.Num(staggered[label]));
            }

            sink.Add("hit_results", FeelMetricUtil.Counts(results));
            sink.Add("reaction_counts", FeelMetricUtil.Counts(reactions));
            sink.Add("reactions", FeelMetricUtil.Join(reactionParts));
            sink.Add("staggered_ticks", FeelMetricUtil.Join(staggeredParts));
            sink.Add("kills", kills);
            sink.Add("hit_classes", string.Join(",", classes));
        }
    }

    /// <summary>
    /// 运动组（条件组）：玩家运动模式序列（模式/来源按 tick 游程编码）、起步到全速与停止到静止的 tick 数、
    /// 转向到位 tick 数、终点位置、阻挡变更。
    /// </summary>
    public sealed class MotionMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("mode_sequence", MetricClass.Logic, "玩家运动模式序列（模式/来源 x 连续 tick 数，逗号分隔）"),
            MetricSpec.Exact("speed_max", MetricClass.Logic, "玩家运动层速度模长最大值（世界单位/秒）"),
            MetricSpec.Exact("ticks_to_full_speed", MetricClass.Logic, "第一次移动请求到速度首次达到最大值的 tick 数（-1 表示没有移动）"),
            MetricSpec.Exact("ticks_to_stop", MetricClass.Logic, "最后一次移动请求之后速度归零所需 tick 数（-1 表示没有停止段）"),
            MetricSpec.Exact("turn_ticks", MetricClass.Logic, "输入方向改变后朝向到位（与新输入方向夹角小于 1e-6）所需 tick 数（-1 表示没有方向改变）"),
            MetricSpec.Exact("facing_final", MetricClass.Logic, "最终朝向（弧度）"),
            MetricSpec.Exact("end_position", MetricClass.Logic, "最终位置（x,y）"),
            MetricSpec.Exact("blocking_updates", MetricClass.Logic, "阻挡变更（tick:矩形数/阻挡版本，只在可破坏障碍被打掉时发生）"),
            MetricSpec.Exact("target_track", MetricClass.Logic, "会移动的靶子（出场后位置变过的）轨迹：每 30 tick 一个采样点（标签@tick=x,y）和最后一个 tick 的点"),
        };

        public string Name => "motion";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) => recording.Feel != null;

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var feel = recording.Feel!;
            var sequence = new List<string>();
            string? last = null;
            var run = 0;
            var maxSpeed = 0.0;
            foreach (var t in feel.Ticks)
            {
                var key = t.Mode + "/" + t.Source;
                if (key == last)
                {
                    run++;
                }
                else
                {
                    if (last != null)
                    {
                        sequence.Add(last + "x" + FeelMetricUtil.Num(run));
                    }

                    last = key;
                    run = 1;
                }

                if (t.Speed > maxSpeed)
                {
                    maxSpeed = t.Speed;
                }
            }

            if (last != null)
            {
                sequence.Add(last + "x" + FeelMetricUtil.Num(run));
            }

            var ticks = recording.Ticks;
            var firstRequested = -1;
            var lastRequested = -1;
            for (var i = 0; i < ticks.Count; i++)
            {
                if (ticks[i].MoveRequested)
                {
                    if (firstRequested < 0)
                    {
                        firstRequested = i;
                    }

                    lastRequested = i;
                }
            }

            var toFull = -1;
            if (firstRequested >= 0 && maxSpeed > 0)
            {
                for (var i = firstRequested; i < feel.Ticks.Count; i++)
                {
                    if (feel.Ticks[i].Speed >= maxSpeed - 1e-9)
                    {
                        toFull = i - firstRequested;
                        break;
                    }
                }
            }

            var toStop = -1;
            if (lastRequested >= 0)
            {
                for (var i = lastRequested + 1; i < feel.Ticks.Count; i++)
                {
                    if (feel.Ticks[i].Speed <= 1e-9)
                    {
                        toStop = i - lastRequested;
                        break;
                    }
                }
            }

            // 转向到位：找第一次"请求中的输入方向与上一请求步方向夹角超过 1e-6"的 tick，再找朝向首次与新输入方向重合的 tick。
            var turn = -1;
            Vec2? previousAxis = null;
            for (var i = 0; i < ticks.Count && turn < 0; i++)
            {
                if (!ticks[i].MoveRequested)
                {
                    continue;
                }

                var axis = ticks[i].MoveAxis;
                if (previousAxis.HasValue)
                {
                    var before = Math.Atan2(previousAxis.Value.Y, previousAxis.Value.X);
                    var now = Math.Atan2(axis.Y, axis.X);
                    if (Math.Abs(AngleDiff(now, before)) > 1e-6)
                    {
                        for (var j = i; j < ticks.Count; j++)
                        {
                            if (Math.Abs(AngleDiff(ticks[j].Facing, now)) < 1e-6)
                            {
                                turn = j - i;
                                break;
                            }
                        }

                        break;
                    }
                }

                previousAxis = axis;
            }

            var updates = new List<string>();
            foreach (var e in feel.Events)
            {
                if (e.Kind == "blocking_changed")
                {
                    updates.Add(FeelMetricUtil.Num(e.Tick) + ":" + FeelMetricUtil.Num(e.A) + "/" + FeelMetricUtil.Num(e.B));
                }
            }

            var track = new List<string>();
            if (feel.Ticks.Count > 0)
            {
                var first = feel.Ticks[0].TargetPositions;
                var lastSample = feel.Ticks[feel.Ticks.Count - 1];
                for (var k = 0; k < first.Count; k++)
                {
                    var moved = false;
                    foreach (var t in feel.Ticks)
                    {
                        var d = t.TargetPositions[k].Value - first[k].Value;
                        if (Math.Abs(d.X) > 1e-9 || Math.Abs(d.Y) > 1e-9)
                        {
                            moved = true;
                            break;
                        }
                    }

                    if (!moved)
                    {
                        continue;
                    }

                    foreach (var t in feel.Ticks)
                    {
                        if (t.Tick % 30 == 0 || t.Tick == lastSample.Tick)
                        {
                            var p = t.TargetPositions[k].Value;
                            track.Add(first[k].Key + "@" + FeelMetricUtil.Num(t.Tick) + "=" + FeelMetricUtil.Num(p.X) + "," + FeelMetricUtil.Num(p.Y));
                        }
                    }
                }
            }

            var endPos = ticks.Count > 0 ? ticks[ticks.Count - 1].Position : recording.StartPosition;
            sink.Add("mode_sequence", string.Join(",", sequence));
            sink.Add("speed_max", maxSpeed);
            sink.Add("ticks_to_full_speed", toFull);
            sink.Add("ticks_to_stop", toStop);
            sink.Add("turn_ticks", turn);
            sink.Add("facing_final", ticks.Count > 0 ? ticks[ticks.Count - 1].Facing : 0.0);
            sink.Add("end_position", FeelMetricUtil.Num(endPos.X) + "," + FeelMetricUtil.Num(endPos.Y));
            sink.Add("blocking_updates", FeelMetricUtil.Join(updates));
            sink.Add("target_track", FeelMetricUtil.Join(track));
        }

        private static double AngleDiff(double a, double b)
        {
            var d = a - b;
            while (d > Math.PI)
            {
                d -= 2 * Math.PI;
            }

            while (d < -Math.PI)
            {
                d += 2 * Math.PI;
            }

            return d;
        }
    }

    /// <summary>
    /// 空间命中组（条件组）：命中确认数、每次施放（含投射物与多段）的命中集合、同一（施放, 段, 目标）的重复确认数（应恒为 0）、
    /// 造成伤害却没有命中确认的次数（应恒为 0，S7b：每次造成伤害的命中恰好一条确认）、回避数。
    /// </summary>
    public sealed class SpatialHitMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("hit_confirmed", MetricClass.Logic, "命中确认事件数（含回避结局）"),
            MetricSpec.Exact("avoided", MetricClass.Logic, "回避类结局的命中确认数"),
            MetricSpec.Exact("hit_sets", MetricClass.Logic, "每次施放每段的命中集合（施放序号.段=目标,目标…，施放序号是该施放在命中确认里首次出现的先后序号，不是引擎内部实例号，跨格子稳定）"),
            MetricSpec.Exact("confirm_ticks", MetricClass.Logic, "命中确认落地（tick:攻击方>受击方）"),
            MetricSpec.Exact("duplicate_confirmations", MetricClass.Logic, "同一（施放, 段, 受击方）的重复确认数（应恒为 0）"),
            MetricSpec.Exact("unconfirmed_damage", MetricClass.Logic, "造成了伤害却没有对应命中确认的伤害事件数（应恒为 0）"),
            MetricSpec.Exact("confirmed_without_damage", MetricClass.Logic, "有命中确认却没有伤害（非回避）的确认数（击杀致死那一击仍有伤害事件，应恒为 0）"),
        };

        public string Name => "spatialhit";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) => recording.Feel != null;

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var feel = recording.Feel!;
            var confirmed = 0;
            var avoided = 0;
            var duplicates = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sets = new List<KeyValuePair<string, List<string>>>();
            var ticks = new List<string>();
            var confirmedDamage = 0;
            var castOrder = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in feel.Events)
            {
                if (e.Kind != "hit_confirmed")
                {
                    continue;
                }

                confirmed++;
                var avoidedResult = e.Detail == "Miss" || e.Detail == "Dodge" || e.Detail == "Parry" || e.Detail == "Immune" || e.Detail == "Invulnerable";
                if (avoidedResult)
                {
                    avoided++;
                }
                else
                {
                    confirmedDamage++;
                }

                var castId = FeelMetricUtil.Field(e.Detail2, "cast");
                if (!castOrder.TryGetValue(castId, out var cast))
                {
                    cast = castOrder.Count;
                    castOrder[castId] = cast;
                }

                var key = FeelMetricUtil.Num(cast) + "." + FeelMetricUtil.Num(e.A);
                var identity = key + ">" + e.Target + ":" + FeelMetricUtil.Field(e.Detail2, "inst");
                if (!seen.Add(identity))
                {
                    duplicates++;
                }

                var index = sets.FindIndex(p => p.Key == key);
                if (index < 0)
                {
                    sets.Add(new KeyValuePair<string, List<string>>(key, new List<string>()));
                    index = sets.Count - 1;
                }

                if (!sets[index].Value.Contains(e.Target))
                {
                    sets[index].Value.Add(e.Target);
                }

                ticks.Add(FeelMetricUtil.Num(e.Tick) + ":" + e.Actor + ">" + e.Target);
            }

            var damageEvents = 0;
            foreach (var d in recording.Events)
            {
                if (d.Kind == "damage" && d.SkillId.Length > 0)
                {
                    damageEvents++;
                }
            }

            var setParts = new List<string>();
            foreach (var pair in sets)
            {
                setParts.Add(pair.Key + "=" + string.Join(",", pair.Value));
            }

            sink.Add("hit_confirmed", confirmed);
            sink.Add("avoided", avoided);
            sink.Add("hit_sets", FeelMetricUtil.Join(setParts));
            sink.Add("confirm_ticks", FeelMetricUtil.Join(ticks));
            sink.Add("duplicate_confirmations", duplicates);
            sink.Add("unconfirmed_damage", Math.Max(0, damageEvents - confirmedDamage));
            sink.Add("confirmed_without_damage", Math.Max(0, confirmedDamage - damageEvents));
        }
    }

    /// <summary>
    /// 表现时间线组（条件组，表现类）：反馈流水线出批到假 sink 的指令——手感音效层 id、镜头冲击（幅度/衰减/合并命中数）、
    /// 顿帧表现冻结/释放、冲击等级序列。数值度量按绝对允差比较（1e-6），id 与序列逐字比较；每个格子各自有基线
    /// （06 第 1.1 节不变量 3：表现组不跨格子比较）。
    /// </summary>
    public sealed class PresentationTimelineMetricGroup : IConditionalMetricGroup
    {
        private const double Tolerance = 1e-6;

        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("sfx_ids", MetricClass.Presentation, "手感音效层发声（tick:音效行 id，按出批顺序）"),
            MetricSpec.Absolute("sfx_count", MetricClass.Presentation, 0, "发声总数"),
            MetricSpec.Exact("camera_cues", MetricClass.Presentation, "镜头冲击（tick:合并命中数:震屏档）"),
            MetricSpec.Absolute("camera_magnitudes", MetricClass.Presentation, Tolerance, "每次镜头冲击的幅度"),
            MetricSpec.Absolute("camera_decay_ms", MetricClass.Presentation, Tolerance, "每次镜头冲击的衰减毫秒数"),
            MetricSpec.Exact("freeze_ops", MetricClass.Presentation, "顿帧表现指令（tick:冻结|释放:单位集合:tick 数）"),
            MetricSpec.Absolute("first_feedback_lag_ticks", MetricClass.Presentation, 0, "第一次命中确认到第一条表现指令的 tick 数（-1 表示没有命中或没有表现）"),
            MetricSpec.Exact("other_ops", MetricClass.Presentation, "其余指令（特效/闪白/飘字/震屏，tick:种类:id）"),
        };

        public string Name => "presentation";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) => recording.Feel != null;

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var feel = recording.Feel!;
            var sfx = new List<string>();
            var cues = new List<string>();
            var magnitudes = new List<double>();
            var decays = new List<double>();
            var freezes = new List<string>();
            var others = new List<string>();
            var firstPresentation = -1;
            var firstHitTick = -1;
            foreach (var e in feel.Events)
            {
                if (e.Kind == "hit_confirmed")
                {
                    firstHitTick = e.Tick;
                    break;
                }
            }

            foreach (var p in feel.Presentation)
            {
                // 命中之前的指令（挥空提示音等）不算"命中反馈"，只看第一次命中确认之后（含同 tick）的第一条。
                if (firstPresentation < 0 && firstHitTick >= 0 && p.Tick >= firstHitTick)
                {
                    firstPresentation = p.Tick;
                }

                switch (p.Kind)
                {
                    case "sfx":
                        sfx.Add(FeelMetricUtil.Num(p.Tick) + ":" + p.Text);
                        break;
                    case "camera":
                        cues.Add(FeelMetricUtil.Num(p.Tick) + ":" + FeelMetricUtil.Num(p.Count) + ":" + p.Text);
                        magnitudes.Add(p.Value);
                        decays.Add(p.Value2);
                        break;
                    case "freeze":
                        freezes.Add(FeelMetricUtil.Num(p.Tick) + ":freeze:" + p.Text + ":" + FeelMetricUtil.Num(p.Value));
                        break;
                    case "release":
                        freezes.Add(FeelMetricUtil.Num(p.Tick) + ":release:" + p.Text + ":0");
                        break;
                    default:
                        others.Add(FeelMetricUtil.Num(p.Tick) + ":" + p.Kind + ":" + p.Text);
                        break;
                }
            }

            sink.Add("sfx_ids", FeelMetricUtil.Join(sfx));
            sink.Add("sfx_count", sfx.Count);
            sink.Add("camera_cues", FeelMetricUtil.Join(cues));
            sink.Add("camera_magnitudes", magnitudes);
            sink.Add("camera_decay_ms", decays);
            sink.Add("freeze_ops", FeelMetricUtil.Join(freezes));
            sink.Add("first_feedback_lag_ticks", firstHitTick >= 0 && firstPresentation >= 0 ? firstPresentation - firstHitTick : -1);
            sink.Add("other_ops", FeelMetricUtil.Join(others));
        }
    }
}
