using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Lab
{
    /// <summary>
    /// 受击反应扩展组（条件组，M5-S2b，ADR-0145）：倒地/起身阶段事件、击退距离、倒地与起身时长、起身无敌窗口的命中结局、被连击锁住（硬直保护期）、格挡/弹反的命中结局。
    /// 只在本次运行里出现过 <c>unit.knocked_down</c>/<c>unit.getup_*</c> 事件、或有命中被判为格挡/弹反/起身无敌回避时出现，
    /// 因此只有声明了这些新字段的脚本的指纹有这个组，既有脚本（击退、静态/动态韧性等）的基线逐字不变。
    /// 受击反应本身（反应分布、硬直 tick 数）仍在 <c>reaction</c> 组里看。
    /// </summary>
    public sealed class ReactionExtMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("knockdown_count", MetricClass.Logic, "倒地事件数（<c>unit.knocked_down</c>）"),
            MetricSpec.Exact("knockdowns", MetricClass.Logic, "倒地事件（tick:受击方:倒地段 tick 数:起身段 tick 数）"),
            MetricSpec.Exact("getups", MetricClass.Logic, "起身开始事件（tick:受击方:起身段 tick 数:起身无敌 tick 数）"),
            MetricSpec.Exact("getup_finishes", MetricClass.Logic, "起身结束事件（tick:受击方）"),
            MetricSpec.Exact("down_ticks", MetricClass.Logic, "倒地段实测时长（倒地事件到起身开始事件的 tick 数；没有起身段的倒地不列），按事件顺序"),
            MetricSpec.Exact("getup_ticks", MetricClass.Logic, "起身段实测时长（起身开始事件到起身结束事件的 tick 数），按事件顺序"),
            MetricSpec.Exact("phase_unpaired", MetricClass.Logic, "阶段事件配对错误数（起身开始前没有倒地、起身结束前没有起身开始；应恒为 0）"),
            MetricSpec.Exact("knockback_peaks", MetricClass.Logic, "击退/倒地反应后受击方离反应落地时位置的最远距离（tick:受击方:距离，世界单位，到该单位的下一次反应或脚本结束为止）"),
            MetricSpec.Exact("invuln_hits", MetricClass.Logic, "落在起身无敌窗口里、结局为 Invulnerable 的命中数"),
            MetricSpec.Exact("invuln_leaks", MetricClass.Logic, "落在起身无敌窗口里、结局不是 Invulnerable 的命中数（应恒为 0）"),
            MetricSpec.Exact("chained", MetricClass.Logic, "被连击锁住的受击方（标签=次数：同一单位前一次倒地没走完（硬直 + 倒地 + 起身）就又被击倒的次数），只列非零"),
            MetricSpec.Exact("chained_targets", MetricClass.Logic, "被连击锁住的受击方个数"),
            MetricSpec.Exact("guard_results", MetricClass.Logic, "命中确认的结局与伤害（tick:受击方:结局:伤害，按命中确认顺序）"),
            MetricSpec.Exact("player_reactions", MetricClass.Logic, "玩家被弹开的反应（tick:反应：玩家作为 combat.reaction_applied 的受击方）"),
        };

        public string Name => "reactionext";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording)
        {
            if (recording.Feel == null)
            {
                return false;
            }

            foreach (var e in recording.Feel.Events)
            {
                if (e.Kind == "knocked_down" || e.Kind == "getup_started" || e.Kind == "getup_finished")
                {
                    return true;
                }

                if (e.Kind == "hit_confirmed" && (e.Detail == "Block" || e.Detail == "Parry" || e.Detail == "GlancingBlow"))
                {
                    return true;
                }
            }

            return false;
        }

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var feel = recording.Feel!;

            // 目标位置时间线：标签 -> (tick, 位置)。
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

            var knockdowns = new List<string>();
            var getups = new List<string>();
            var finishes = new List<string>();
            var downTicks = new List<string>();
            var getupTicks = new List<string>();
            var unpaired = 0;
            var knockdownCount = 0;

            var downAt = new Dictionary<string, int>(StringComparer.Ordinal);
            var upAt = new Dictionary<string, int>(StringComparer.Ordinal);
            var windows = new List<(string Target, int From, int To)>();
            var lastKnockdown = new Dictionary<string, (int Tick, int Total)>(StringComparer.Ordinal);
            var chained = new Dictionary<string, int>(StringComparer.Ordinal);
            var reactionTicks = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var totals = new Dictionary<string, List<(int Tick, int Total)>>(StringComparer.Ordinal);

            // 反应落地事件先走一遍：同一 tick 的反应与倒地事件配对时要按 tick 找到该次反应的总时长。
            foreach (var e in feel.Events)
            {
                if (e.Kind != "reaction")
                {
                    continue;
                }

                if (!reactionTicks.TryGetValue(e.Target, out var rt))
                {
                    reactionTicks[e.Target] = rt = new List<int>();
                }

                rt.Add(e.Tick);
                if (e.Detail == "Knockdown")
                {
                    if (!totals.TryGetValue(e.Target, out var tl))
                    {
                        totals[e.Target] = tl = new List<(int, int)>();
                    }

                    tl.Add((e.Tick, e.A));
                }
            }

            var invulnHits = 0;
            var invulnLeaks = 0;
            var results = new List<string>();
            var playerReactions = new List<string>();
            var peaks = new List<string>();

            foreach (var e in feel.Events)
            {
                switch (e.Kind)
                {
                    case "knocked_down":
                        knockdownCount++;
                        knockdowns.Add(FeelMetricUtil.Num(e.Tick) + ":" + e.Target + ":" + FeelMetricUtil.Num(e.A) + ":" + FeelMetricUtil.Num(e.B));
                        downAt[e.Target] = e.Tick;
                        upAt.Remove(e.Target);
                        {
                            // 本次击倒对应的反应总时长 = 最近一次不晚于本 tick 的 Knockdown 反应；前一次击倒没走完就又被击倒算被锁住。
                            var total = 0;
                            if (totals.TryGetValue(e.Target, out var tl))
                            {
                                foreach (var r in tl)
                                {
                                    if (r.Tick <= e.Tick)
                                    {
                                        total = r.Total;
                                    }
                                }
                            }

                            if (lastKnockdown.TryGetValue(e.Target, out var prev) && e.Tick - prev.Tick < prev.Total)
                            {
                                FeelMetricUtil.Bump(chained, e.Target);
                            }

                            lastKnockdown[e.Target] = (e.Tick, total);
                        }

                        break;
                    case "getup_started":
                        getups.Add(FeelMetricUtil.Num(e.Tick) + ":" + e.Target + ":" + FeelMetricUtil.Num(e.A) + ":" + FeelMetricUtil.Num(e.B));
                        if (!downAt.TryGetValue(e.Target, out var d))
                        {
                            unpaired++;
                        }
                        else
                        {
                            downTicks.Add(FeelMetricUtil.Num(e.Tick - d));
                            downAt.Remove(e.Target);
                        }

                        upAt[e.Target] = e.Tick;
                        if (e.B > 0)
                        {
                            windows.Add((e.Target, e.Tick, e.Tick + e.B));
                        }

                        break;
                    case "getup_finished":
                        finishes.Add(FeelMetricUtil.Num(e.Tick) + ":" + e.Target);
                        if (!upAt.TryGetValue(e.Target, out var u))
                        {
                            unpaired++;
                        }
                        else
                        {
                            getupTicks.Add(FeelMetricUtil.Num(e.Tick - u));
                            upAt.Remove(e.Target);
                        }

                        break;
                    case "hit_confirmed":
                        results.Add(
                            FeelMetricUtil.Num(e.Tick) + ":" + e.Target + ":" + e.Detail + ":" + FeelMetricUtil.Num(e.D));
                        break;
                    case "reaction":
                        if (e.Target == "player")
                        {
                            playerReactions.Add(FeelMetricUtil.Num(e.Tick) + ":" + e.Detail);
                        }

                        if ((e.Detail == "Knockback" || e.Detail == "Knockdown") && positions.TryGetValue(e.Target, out var track))
                        {
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

                            peaks.Add(FeelMetricUtil.Num(e.Tick) + ":" + e.Target + ":" + FeelMetricUtil.Num(peak));
                        }

                        break;
                }
            }

            // 起身无敌窗口里的命中：tick ∈ [起身开始, 起身开始 + 无敌 tick 数)。
            foreach (var e in feel.Events)
            {
                if (e.Kind != "hit_confirmed")
                {
                    continue;
                }

                foreach (var w in windows)
                {
                    if (w.Target == e.Target && e.Tick >= w.From && e.Tick < w.To)
                    {
                        if (e.Detail == "Invulnerable")
                        {
                            invulnHits++;
                        }
                        else
                        {
                            invulnLeaks++;
                        }

                        break;
                    }
                }
            }

            var chainedParts = new List<string>();
            var chainedLabels = new List<string>(chained.Keys);
            chainedLabels.Sort(StringComparer.Ordinal);
            foreach (var label in chainedLabels)
            {
                chainedParts.Add(label + "=" + FeelMetricUtil.Num(chained[label]));
            }

            sink.Add("knockdown_count", knockdownCount);
            sink.Add("knockdowns", FeelMetricUtil.Join(knockdowns));
            sink.Add("getups", FeelMetricUtil.Join(getups));
            sink.Add("getup_finishes", FeelMetricUtil.Join(finishes));
            sink.Add("down_ticks", FeelMetricUtil.Join(downTicks));
            sink.Add("getup_ticks", FeelMetricUtil.Join(getupTicks));
            sink.Add("phase_unpaired", unpaired);
            sink.Add("knockback_peaks", FeelMetricUtil.Join(peaks));
            sink.Add("invuln_hits", invulnHits);
            sink.Add("invuln_leaks", invulnLeaks);
            sink.Add("chained", FeelMetricUtil.Join(chainedParts));
            sink.Add("chained_targets", chainedLabels.Count);
            sink.Add("guard_results", FeelMetricUtil.Join(results));
            sink.Add("player_reactions", FeelMetricUtil.Join(playerReactions));
        }
    }
}
