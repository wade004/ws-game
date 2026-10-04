using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Carriers.Unit;
using Core.Foundation.Common;

namespace Lab
{
    /// <summary>
    /// 脚本可声明的"可选度量组"名字（<c>meta.extraMetrics</c>，M5-S7，ADR-0151）。这些组只在声明了它们的脚本里出现，
    /// 既有脚本的指纹没有这些组，既有基线逐字不变；组的度量与其它组一样落进指纹、可写期望、逐格子比较。
    /// </summary>
    public static class LabExtraMetrics
    {
        /// <summary>输入到首次可见响应的延迟（逻辑 tick 差 + 按帧时间线换算的可见时刻）。</summary>
        public const string Latency = "latency";

        /// <summary>攻击组补项：取消后残留命中数、连招接续成功率。</summary>
        public const string AttackExt = "attackx";

        /// <summary>受击组补项：每次反应后的击退距离。</summary>
        public const string HitExt = "hitx";

        /// <summary>群体组：同 tick 发声峰值、反馈指令总数与分布。</summary>
        public const string Crowd = "crowd";

        /// <summary>朝向量化：方向档位序列、档位切换数、量化与框架量化函数的不一致帧数。</summary>
        public const string Facing = "facing";

        public static readonly string[] Known = { Latency, AttackExt, HitExt, Crowd, Facing };

        public static bool IsDeclared(ScriptMeta meta, string name) =>
            meta.ExtraMetrics.IndexOf(name) >= 0;
    }

    internal static class ExtraMetricUtil
    {
        /// <summary>
        /// 第一个"已完成固定步数大于 <paramref name="tick"/>"的表现帧的结束时刻（秒）：该 tick 里发生的响应最早在这一帧的画面里出现。
        /// 没有这样的帧返回 NaN。帧时刻按帧步长（慢放时按缩放后的步长，<see cref="LabRecording.EffectiveFrameSeconds"/>）换算。
        /// </summary>
        public static double VisibleSeconds(LabRecording recording, int tick)
        {
            var dt = recording.EffectiveFrameSeconds;
            foreach (var frame in recording.Frames)
            {
                if (frame.TicksDone > tick)
                {
                    return frame.Time + dt;
                }
            }

            return double.NaN;
        }
    }

    /// <summary>
    /// 输入到首次可见响应（可选组 <c>latency</c>，06 第 3.3 节响应行的表现半边）。对每个玩家的攻击/闪避/技能类按下：
    /// <list type="bullet">
    /// <item>配对到它引发的动作开始（先进先出，被缓冲丢弃的按下不参与，与 <c>inputbuf.accept_latency_ticks</c> 同口径）；</item>
    /// <item>首次可见响应 = 动作开始之后第一个表现层可见的变化：姿势请求（攻击/施法/跳跃状态进入或再触发）或运动层出现动作/受控位移；</item>
    /// <item>逻辑量（tick 差）与固定步序列一致，逐字节比较；可见时刻按"第一个已完成该 tick 的表现帧的结束时刻"换算，是表现类度量，按帧步长量化。</item>
    /// </list>
    /// 判断记录：可见时刻取帧结束而不是帧开始，是因为该帧的表现同步发生在帧内固定步之后；<c>visible_excess_ms</c> 把"逻辑上必须经过的时间"
    /// （<c>(可见 tick 差 + 1) × 步长</c>）扣掉，剩下的就是帧量化多出来的那一截，恒落在 <c>[0, 帧步长)</c>——期望据此写成"不超过 <c>frame_ms</c>"，
    /// 随脚本帧率上限与慢放倍率自动变化，不写死数。
    /// </summary>
    public sealed class VisibleLatencyMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("inputs", MetricClass.Logic, "输入到响应（tick:类别:动作开始 tick 差:首次可见 tick 差；没有响应写 -；按按下顺序）"),
            MetricSpec.Exact("by_class", MetricClass.Logic, "按类别汇总（类别:按下数:有响应数:动作开始 tick 差最大值:首次可见 tick 差最大值，按类别名排序）"),
            MetricSpec.Exact("action_ticks_max", MetricClass.Logic, "全部类别里动作开始 tick 差的最大值（没有响应为 -1）"),
            MetricSpec.Exact("visible_ticks_max", MetricClass.Logic, "全部类别里首次可见 tick 差的最大值（没有可见响应为 -1）"),
            MetricSpec.Exact("step_ms", MetricClass.Logic, "固定步长，毫秒"),
            MetricSpec.Absolute("frame_ms", MetricClass.Presentation, 1e-6, "表现帧步长，毫秒（慢放时是缩放后的帧步长）"),
            MetricSpec.Absolute("visible_ms", MetricClass.Presentation, 1e-6, "每个有可见响应的输入，从按下 tick 起点到首次可见帧结束的毫秒数（按下顺序）"),
            MetricSpec.Absolute("visible_ms_max", MetricClass.Presentation, 1e-6, "上一项最大值（没有为 -1）"),
            MetricSpec.Absolute("visible_excess_ms", MetricClass.Presentation, 1e-6, "每个有可见响应的输入，可见毫秒数扣掉逻辑必经时间 (首次可见 tick 差 + 1) × 步长（帧量化多出的一截）"),
            MetricSpec.Absolute("visible_excess_ms_max", MetricClass.Presentation, 1e-6, "上一项最大值（没有为 -1）"),
            MetricSpec.Absolute("visible_excess_ms_min", MetricClass.Presentation, 1e-6, "上上项最小值（没有为 -1）"),
        };

        public string Name => LabExtraMetrics.Latency;

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) =>
            recording.Feel != null && LabExtraMetrics.IsDeclared(recording.Script.Meta, LabExtraMetrics.Latency);

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var feel = recording.Feel!;
            var stepMs = recording.StepSeconds * 1000.0;

            var presses = new List<(int Tick, string Action, string Class)>();
            foreach (var e in recording.InjectedInputs)
            {
                if (e.Kind != ScriptEventKind.Press || e.Actor.Length != 0)
                {
                    continue;
                }

                if (!feel.InputClasses.TryGetValue(e.Action, out var cls) || !(cls == "attack" || cls == "dodge" || cls == "skill"))
                {
                    continue;
                }

                presses.Add((e.Tick, FeelMetricUtil.Short(e.Action), cls));
            }

            // 逻辑时间线上"可见的变化"的 tick（升序）：姿势请求与运动层的动作/受控位移来源。
            var visibleTicks = new List<int>();
            foreach (var e in feel.Events)
            {
                if (e.Kind == "pose_state" && e.Target == "player"
                    && (e.Detail == "Attack" || e.Detail == "Cast" || e.Detail == "Jump"))
                {
                    visibleTicks.Add(e.Tick);
                }
            }

            foreach (var t in feel.Ticks)
            {
                if (t.Source == "Action" || t.Source == "Forced")
                {
                    visibleTicks.Add(t.Tick);
                }
            }

            visibleTicks.Sort();

            // 先进先出配对，同 inputbuf 的口径：丢弃事件摘掉最早的同名待配对按下；动作开始取最早的待配对按下。
            var pending = new List<int>();
            for (var i = 0; i < presses.Count; i++)
            {
                pending.Add(i);
            }

            var actionTick = new int[presses.Count];
            for (var i = 0; i < actionTick.Length; i++)
            {
                actionTick[i] = -1;
            }

            foreach (var e in feel.Events)
            {
                if (e.Kind == "buffer_dropped" && e.Actor == "player")
                {
                    var action = FeelMetricUtil.Short(e.Detail);
                    var index = pending.FindIndex(p => presses[p].Tick <= e.Tick && presses[p].Action == action);
                    if (index >= 0)
                    {
                        pending.RemoveAt(index);
                    }
                }
                else if (e.Kind == "action_started" && e.Actor == "player")
                {
                    var index = pending.FindIndex(p => presses[p].Tick <= e.Tick);
                    if (index >= 0)
                    {
                        actionTick[pending[index]] = e.Tick;
                        pending.RemoveAt(index);
                    }
                }
            }

            var inputParts = new List<string>();
            var classes = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
            var visibleMs = new List<double>();
            var excess = new List<double>();
            var actionMax = -1;
            var visibleMax = -1;
            for (var i = 0; i < presses.Count; i++)
            {
                var press = presses[i];
                if (!classes.TryGetValue(press.Class, out var tally))
                {
                    // 按下数、有响应数、动作 tick 差最大值、可见 tick 差最大值
                    classes[press.Class] = tally = new[] { 0, 0, -1, -1 };
                }

                tally[0]++;
                var actionDelta = -1;
                var visibleDelta = -1;
                if (actionTick[i] >= 0)
                {
                    actionDelta = actionTick[i] - press.Tick;
                    tally[1]++;
                    tally[2] = Math.Max(tally[2], actionDelta);
                    actionMax = Math.Max(actionMax, actionDelta);
                    foreach (var tick in visibleTicks)
                    {
                        if (tick >= actionTick[i])
                        {
                            visibleDelta = tick - press.Tick;
                            break;
                        }
                    }

                    if (visibleDelta >= 0)
                    {
                        tally[3] = Math.Max(tally[3], visibleDelta);
                        visibleMax = Math.Max(visibleMax, visibleDelta);
                        var seconds = ExtraMetricUtil.VisibleSeconds(recording, press.Tick + visibleDelta);
                        if (!double.IsNaN(seconds))
                        {
                            var ms = (seconds - press.Tick * recording.StepSeconds) * 1000.0;
                            visibleMs.Add(ms);
                            excess.Add(ms - (visibleDelta + 1) * stepMs);
                        }
                    }
                }

                inputParts.Add(
                    FeelMetricUtil.Num(press.Tick) + ":" + press.Class + ":"
                    + (actionDelta >= 0 ? FeelMetricUtil.Num(actionDelta) : "-") + ":"
                    + (visibleDelta >= 0 ? FeelMetricUtil.Num(visibleDelta) : "-"));
            }

            var classParts = new List<string>();
            foreach (var pair in classes)
            {
                var t = pair.Value;
                classParts.Add(
                    pair.Key + ":" + FeelMetricUtil.Num(t[0]) + ":" + FeelMetricUtil.Num(t[1]) + ":" + FeelMetricUtil.Num(t[2]) + ":" + FeelMetricUtil.Num(t[3]));
            }

            sink.Add("inputs", FeelMetricUtil.Join(inputParts));
            sink.Add("by_class", FeelMetricUtil.Join(classParts));
            sink.Add("action_ticks_max", actionMax);
            sink.Add("visible_ticks_max", visibleMax);
            sink.Add("step_ms", stepMs);
            sink.Add("frame_ms", recording.EffectiveFrameSeconds * 1000.0);
            sink.Add("visible_ms", visibleMs);
            sink.Add("visible_ms_max", Max(visibleMs));
            sink.Add("visible_excess_ms", excess);
            sink.Add("visible_excess_ms_max", Max(excess));
            sink.Add("visible_excess_ms_min", Min(excess));
        }

        private static double Max(List<double> values)
        {
            if (values.Count == 0)
            {
                return -1.0;
            }

            var best = values[0];
            foreach (var v in values)
            {
                best = Math.Max(best, v);
            }

            return best;
        }

        private static double Min(List<double> values)
        {
            if (values.Count == 0)
            {
                return -1.0;
            }

            var best = values[0];
            foreach (var v in values)
            {
                best = Math.Min(best, v);
            }

            return best;
        }
    }

    /// <summary>
    /// 攻击组补项（可选组 <c>attackx</c>，06 第 3.3 节攻击行）：取消后残留命中数与连招接续成功率。全部是逻辑类度量。
    /// <list type="bullet">
    /// <item>取消后残留命中：玩家的命中确认所属的动作（同技能的最近一次开始）已经被取消，且命中 tick 晚于取消 tick——取消应当切断后续判定，这个数应为 0；</item>
    /// <item>连招接续：<c>attempts</c> = 玩家在某个动作进行中（开始 tick &lt; 按下 tick &lt; 结束 tick，被取消的动作取消 tick 也算，因为取消就是这次按下引起的）按下的攻击类输入数；
    /// <c>follow_ups</c> = 连招序号大于 0 的动作开始数；<c>success</c> = 先进先出配对成功的数（每个接续开始配对到不晚于它的最早未配对尝试）；
    /// 成功率 = success / attempts，没有尝试为 -1。</item>
    /// </list>
    /// </summary>
    public sealed class AttackExtMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("cancels_total", MetricClass.Logic, "玩家动作被取消的次数"),
            MetricSpec.Exact("residual_hits", MetricClass.Logic, "取消之后才落地的玩家命中确认数（所属动作已取消、命中 tick 晚于取消 tick；应恒为 0）"),
            MetricSpec.Exact("residual_hit_list", MetricClass.Logic, "上一项的明细（tick:技能:受击方）"),
            MetricSpec.Exact("combo_attempts", MetricClass.Logic, "动作进行中按下的攻击类输入数（连招接续尝试）"),
            MetricSpec.Exact("combo_follow_ups", MetricClass.Logic, "连招序号大于 0 的玩家动作开始数"),
            MetricSpec.Exact("combo_success", MetricClass.Logic, "先进先出配对成功的接续数"),
            MetricSpec.Exact("combo_success_rate", MetricClass.Logic, "连招接续成功率（接续数 / 尝试数；没有尝试为 -1）"),
        };

        public string Name => LabExtraMetrics.AttackExt;

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) =>
            recording.Feel != null && LabExtraMetrics.IsDeclared(recording.Script.Meta, LabExtraMetrics.AttackExt);

        private sealed class ActionSpan
        {
            public string Skill = string.Empty;
            public int Start;
            public int End = int.MaxValue;
            public bool Cancelled;
        }

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var feel = recording.Feel!;
            var spans = new List<ActionSpan>();
            ActionSpan? current = null;
            var cancels = 0;
            var residual = new List<string>();
            var followUps = new List<int>();
            foreach (var e in feel.Events)
            {
                if (e.Actor != "player")
                {
                    continue;
                }

                switch (e.Kind)
                {
                    case "action_started":
                        current = new ActionSpan { Skill = e.SkillId, Start = e.Tick };
                        spans.Add(current);
                        if (e.A > 0)
                        {
                            followUps.Add(e.Tick);
                        }

                        break;
                    case "action_cancelled":
                        cancels++;
                        if (current != null)
                        {
                            current.End = e.Tick;
                            current.Cancelled = true;
                            current = null;
                        }

                        break;
                    case "action_finished":
                        if (current != null)
                        {
                            current.End = e.Tick;
                            current = null;
                        }

                        break;
                    case "hit_confirmed":
                        for (var i = spans.Count - 1; i >= 0; i--)
                        {
                            var span = spans[i];
                            if (span.Skill == e.SkillId && span.Start <= e.Tick)
                            {
                                if (span.Cancelled && e.Tick > span.End)
                                {
                                    residual.Add(FeelMetricUtil.Num(e.Tick) + ":" + e.SkillId + ":" + e.Target);
                                }

                                break;
                            }
                        }

                        break;
                }
            }

            var attempts = new List<int>();
            foreach (var input in recording.InjectedInputs)
            {
                if (input.Kind != ScriptEventKind.Press || input.Actor.Length != 0
                    || !feel.InputClasses.TryGetValue(input.Action, out var cls) || cls != "attack")
                {
                    continue;
                }

                foreach (var span in spans)
                {
                    // 取消事件与引起取消的按下落在同一 tick，所以被取消的动作的结束 tick 也算"进行中"；自然结束的动作结束 tick 不算。
                    if (span.Start < input.Tick && (input.Tick < span.End || (span.Cancelled && input.Tick == span.End)))
                    {
                        attempts.Add(input.Tick);
                        break;
                    }
                }
            }

            attempts.Sort();
            var pending = new List<int>(attempts);
            var success = 0;
            foreach (var start in followUps)
            {
                var index = pending.FindIndex(t => t <= start);
                if (index >= 0)
                {
                    pending.RemoveAt(index);
                    success++;
                }
            }

            sink.Add("cancels_total", cancels);
            sink.Add("residual_hits", residual.Count);
            sink.Add("residual_hit_list", FeelMetricUtil.Join(residual));
            sink.Add("combo_attempts", attempts.Count);
            sink.Add("combo_follow_ups", followUps.Count);
            sink.Add("combo_success", success);
            sink.Add("combo_success_rate", attempts.Count == 0 ? -1.0 : (double)success / attempts.Count);
        }
    }

    /// <summary>
    /// 受击组补项（可选组 <c>hitx</c>，06 第 3.3 节受击行）：每次反应落地后受击靶子离反应落地位置的最远距离（击退距离）。
    /// 与 <c>reactionext.knockback_peaks</c> 同口径（到该靶子的下一次反应或脚本结束为止），但对任何反应类型都给，不要求脚本里有倒地/格挡。
    /// 逻辑类度量。
    /// </summary>
    public sealed class HitExtMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("reaction_distances", MetricClass.Logic, "每次反应落地后靶子离落地位置的最远距离（tick:受击方:反应:距离，世界单位）"),
            MetricSpec.Exact("distance_max", MetricClass.Logic, "上一项最大值（没有反应为 -1）"),
            MetricSpec.Exact("distance_by_reaction", MetricClass.Logic, "按反应类型的最大距离（反应:距离，按名排序）"),
        };

        public string Name => LabExtraMetrics.HitExt;

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) =>
            recording.Feel != null && LabExtraMetrics.IsDeclared(recording.Script.Meta, LabExtraMetrics.HitExt);

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var feel = recording.Feel!;
            var positions = new Dictionary<string, List<KeyValuePair<int, Vec2>>>(StringComparer.Ordinal);
            var lastTick = 0;
            foreach (var t in feel.Ticks)
            {
                lastTick = Math.Max(lastTick, t.Tick);
                foreach (var pair in t.TargetPositions)
                {
                    if (!positions.TryGetValue(pair.Key, out var list))
                    {
                        positions[pair.Key] = list = new List<KeyValuePair<int, Vec2>>();
                    }

                    list.Add(new KeyValuePair<int, Vec2>(t.Tick, pair.Value));
                }
            }

            var reactionTicks = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            foreach (var e in feel.Events)
            {
                if (e.Kind != "reaction")
                {
                    continue;
                }

                if (!reactionTicks.TryGetValue(e.Target, out var list))
                {
                    reactionTicks[e.Target] = list = new List<int>();
                }

                list.Add(e.Tick);
            }

            var parts = new List<string>();
            var byReaction = new SortedDictionary<string, double>(StringComparer.Ordinal);
            var max = -1.0;
            foreach (var e in feel.Events)
            {
                if (e.Kind != "reaction" || !positions.TryGetValue(e.Target, out var track))
                {
                    continue;
                }

                var end = lastTick;
                foreach (var tick in reactionTicks[e.Target])
                {
                    if (tick > e.Tick)
                    {
                        end = tick;
                        break;
                    }
                }

                Vec2? start = null;
                var peak = 0.0;
                foreach (var sample in track)
                {
                    if (sample.Key < e.Tick || sample.Key > end)
                    {
                        continue;
                    }

                    if (start == null)
                    {
                        start = sample.Value;
                        continue;
                    }

                    peak = Math.Max(peak, (sample.Value - start.Value).Length);
                }

                parts.Add(FeelMetricUtil.Num(e.Tick) + ":" + e.Target + ":" + e.Detail + ":" + FeelMetricUtil.Num(peak));
                max = Math.Max(max, peak);
                byReaction[e.Detail] = byReaction.TryGetValue(e.Detail, out var old) ? Math.Max(old, peak) : peak;
            }

            var byParts = new List<string>();
            foreach (var pair in byReaction)
            {
                byParts.Add(pair.Key + ":" + FeelMetricUtil.Num(pair.Value));
            }

            sink.Add("reaction_distances", FeelMetricUtil.Join(parts));
            sink.Add("distance_max", max);
            sink.Add("distance_by_reaction", FeelMetricUtil.Join(byParts));
        }
    }

    /// <summary>
    /// 群体组（可选组 <c>crowd</c>，06 第 3.3 节群体行）：反馈指令的同 tick 并发。同 tick 发声峰值、同 tick 反馈指令峰值、反馈指令总数与按种类的分布。
    /// 表现类、计数口径（精确比较）：来源是反馈包流水线出批后假 sink 收到的指令。
    /// </summary>
    public sealed class CrowdMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("sfx_peak", MetricClass.Presentation, "同一 tick 里发声条数的最大值"),
            MetricSpec.Exact("sfx_peak_tick", MetricClass.Presentation, "第一次出现发声峰值的 tick（没有发声为 -1）"),
            MetricSpec.Exact("ops_peak", MetricClass.Presentation, "同一 tick 里全部反馈指令条数的最大值"),
            MetricSpec.Exact("ops_total", MetricClass.Presentation, "反馈指令总数"),
            MetricSpec.Exact("ops_by_kind", MetricClass.Presentation, "按种类的指令数（种类:个数，按名排序）"),
        };

        public string Name => LabExtraMetrics.Crowd;

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) =>
            recording.Feel != null && LabExtraMetrics.IsDeclared(recording.Script.Meta, LabExtraMetrics.Crowd);

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var feel = recording.Feel!;
            var sfx = new SortedDictionary<int, int>();
            var ops = new SortedDictionary<int, int>();
            var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var p in feel.Presentation)
            {
                ops[p.Tick] = ops.TryGetValue(p.Tick, out var n) ? n + 1 : 1;
                FeelMetricUtil.Bump(kinds, p.Kind);
                if (p.Kind == "sfx")
                {
                    sfx[p.Tick] = sfx.TryGetValue(p.Tick, out var m) ? m + 1 : 1;
                }
            }

            var sfxPeak = 0;
            var sfxPeakTick = -1;
            foreach (var pair in sfx)
            {
                if (pair.Value > sfxPeak)
                {
                    sfxPeak = pair.Value;
                    sfxPeakTick = pair.Key;
                }
            }

            var opsPeak = 0;
            foreach (var pair in ops)
            {
                opsPeak = Math.Max(opsPeak, pair.Value);
            }

            sink.Add("sfx_peak", sfxPeak);
            sink.Add("sfx_peak_tick", sfxPeakTick);
            sink.Add("ops_peak", opsPeak);
            sink.Add("ops_total", feel.Presentation.Count);
            sink.Add("ops_by_kind", FeelMetricUtil.Counts(kinds));
        }
    }

    /// <summary>
    /// 朝向量化（可选组 <c>facing</c>，06 第 1.1 节格子间不变量 4）：量化朝向格子（精灵型外形）的方向档位序列，
    /// 以及假 View 的档位与框架量化函数（<see cref="DirectionQuantizer"/>）对同一连续朝向的结果不一致的帧数（应恒为 0）。
    /// 连续朝向格子的档位恒为 0、档数为 0，这些度量取空值。表现类度量，精确比较。
    /// </summary>
    public sealed class FacingMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("direction_count", MetricClass.Presentation, "方向档数（连续朝向为 0）"),
            MetricSpec.Exact("direction_runs", MetricClass.Presentation, "方向档位的连续帧段（档位x帧数，逗号分隔；只在有位姿的帧里数）"),
            MetricSpec.Exact("direction_changes", MetricClass.Presentation, "相邻有位姿帧之间档位变化的次数"),
            MetricSpec.Exact("quantize_mismatch_frames", MetricClass.Presentation, "假 View 的档位与框架量化函数对同一朝向的结果不一致的帧数（应恒为 0）"),
        };

        public string Name => LabExtraMetrics.Facing;

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) => LabExtraMetrics.IsDeclared(recording.Script.Meta, LabExtraMetrics.Facing);

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var count = 0;
            var runs = new List<string>();
            var changes = 0;
            var mismatches = 0;
            var runIndex = -1;
            var runLength = 0;
            foreach (var frame in recording.Frames)
            {
                if (!frame.HasPose)
                {
                    continue;
                }

                count = Math.Max(count, frame.ViewDirectionCount);
                if (frame.ViewDirectionCount > 0
                    && DirectionQuantizer.Quantize(frame.ViewFacingRadians, frame.ViewDirectionCount) != frame.ViewDirectionIndex)
                {
                    mismatches++;
                }

                if (runLength > 0 && frame.ViewDirectionIndex == runIndex)
                {
                    runLength++;
                    continue;
                }

                if (runLength > 0)
                {
                    runs.Add(runIndex.ToString(CultureInfo.InvariantCulture) + "x" + runLength.ToString(CultureInfo.InvariantCulture));
                    changes++;
                }

                runIndex = frame.ViewDirectionIndex;
                runLength = 1;
            }

            if (runLength > 0)
            {
                runs.Add(runIndex.ToString(CultureInfo.InvariantCulture) + "x" + runLength.ToString(CultureInfo.InvariantCulture));
            }

            sink.Add("direction_count", count);
            sink.Add("direction_runs", string.Join(",", runs));
            sink.Add("direction_changes", changes);
            sink.Add("quantize_mismatch_frames", mismatches);
        }
    }
}
