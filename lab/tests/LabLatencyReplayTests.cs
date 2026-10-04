using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 输入到首次可见响应的延迟度量、慢放重放（只缩放表现时钟）与手感脚本的帧率上限不变（M5-S7，ADR-0151，06 第 3.3/3.5 节）。
    /// 期望值都由脚本里的 tick、帧率上限与固定步长在用例里算出，不写裸数。
    /// </summary>
    public sealed class LabLatencyReplayTests
    {
        private static readonly string[] ActionCells = { "2d_action", "2_5d_action", "3d_action" };

        private const double Step = 1.0 / 60.0;

        private static (int Tick, string Class, int ActionDelta, int VisibleDelta)[] ParseInputs(FeelFp fp)
        {
            var items = fp.Items("latency.inputs");
            var result = new (int, string, int, int)[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                var p = items[i].Split(':');
                result[i] = (
                    int.Parse(p[0], CultureInfo.InvariantCulture), p[1],
                    p[2] == "-" ? -1 : int.Parse(p[2], CultureInfo.InvariantCulture),
                    p[3] == "-" ? -1 : int.Parse(p[3], CultureInfo.InvariantCulture));
            }

            return result;
        }

        // ---------- 延迟度量 ----------

        [Theory]
        [InlineData("feel_x_latency")]
        [InlineData("feel_x_latency_fps30")]
        public void VisibleTime_IsTheFrameQuantizedEndOfTheResponseTick(string scriptId)
        {
            var script = LabTestSupport.Script(scriptId);
            var frameDt = 1.0 / script.Meta.FrameRateCap;
            foreach (var cell in ActionCells)
            {
                var fp = FeelFp.Of(scriptId, cell);
                var inputs = ParseInputs(fp);
                Assert.Equal(3, inputs.Length);
                Assert.Equal(new[] { "attack", "skill", "dodge" }, inputs.Select(i => i.Class).ToArray());

                var visibleMs = fp.Nums("latency.visible_ms");
                var excess = fp.Nums("latency.visible_excess_ms");
                Assert.Equal(inputs.Length, visibleMs.Count);
                var maxExcess = 0.0;
                for (var i = 0; i < inputs.Length; i++)
                {
                    // 动作式格子里每类输入都有响应，首次可见不早于动作开始。
                    Assert.True(inputs[i].ActionDelta >= 0, $"{cell} 的 {inputs[i].Class} 没有动作开始");
                    Assert.True(inputs[i].VisibleDelta >= inputs[i].ActionDelta);

                    // 规则：响应发生在 tick R（该 tick 的模拟在 (R+1)×步长 结束），画面里最早在"结束时刻不早于它"的第一个表现帧出现。
                    var responseTick = inputs[i].Tick + inputs[i].VisibleDelta;
                    var end = Math.Ceiling((responseTick + 1) * Step / frameDt - 1e-9) * frameDt;
                    Assert.Equal((end - inputs[i].Tick * Step) * 1000.0, visibleMs[i], 6);
                    Assert.Equal(visibleMs[i] - (inputs[i].VisibleDelta + 1) * Step * 1000.0, excess[i], 6);
                    maxExcess = Math.Max(maxExcess, excess[i]);
                }

                // 帧量化多出的一截：不小于 0，小于一个表现帧步长（frame_ms 由脚本帧率上限算出）。
                Assert.Equal(frameDt * 1000.0, fp.Num("latency.frame_ms"), 6);
                Assert.True(excess.Min() >= -1e-6);
                Assert.True(maxExcess < frameDt * 1000.0);
                Assert.Equal(excess.Max(), fp.Num("latency.visible_excess_ms_max"), 6);
            }
        }

        [Fact]
        public void LowerFrameRate_ShowsFrameQuantization_WhileLogicChannelsStayEqual()
        {
            foreach (var cell in ActionCells)
            {
                var at60 = FeelFp.Of("feel_x_latency", cell);
                var at30 = FeelFp.Of("feel_x_latency_fps30", cell);

                // 逻辑通道（tick 差）与帧率无关。
                Assert.Equal(at60.Text("latency.inputs"), at30.Text("latency.inputs"));
                Assert.Equal(at60.Text("latency.by_class"), at30.Text("latency.by_class"));

                // 表现通道：60 帧/秒时帧步长等于固定步长，没有量化多出的一截；30 帧/秒时一帧两步，部分输入晚一个固定步才可见。
                Assert.Equal(0.0, at60.Num("latency.visible_excess_ms_max"), 6);
                Assert.True(at30.Num("latency.visible_excess_ms_max") > 0.0, "30 帧/秒下应出现帧量化");
                Assert.True(at30.Num("latency.visible_ms_max") >= at60.Num("latency.visible_ms_max") - 1e-6);
            }
        }

        [Fact]
        public void TargetedCells_HaveNoActionStart_SoLatencyReportsNoResponse()
        {
            // 目标选择式格子瞬发、没有动作开始与姿势请求：每个输入的响应记 -，可见毫秒为空，度量用 -1 表示"没有"。
            var fp = FeelFp.Of("feel_x_latency", "2d_targeted");
            foreach (var input in ParseInputs(fp))
            {
                Assert.Equal(-1, input.ActionDelta);
                Assert.Equal(-1, input.VisibleDelta);
            }

            Assert.Empty(fp.Nums("latency.visible_ms"));
            Assert.Equal(-1.0, fp.Num("latency.visible_ms_max"));
            Assert.Equal(-1.0, fp.Num("latency.action_ticks_max"));
        }

        [Fact]
        public void LatencyGroup_IsAbsentFromScriptsThatDoNotDeclareIt()
        {
            // 既有脚本的指纹没有可选组：既有基线逐字不变。
            var fp = FeelFp.Of("feel_melee", "2d_action");
            foreach (var group in LabExtraMetrics.Known)
            {
                Assert.False(fp.Has(group), $"feel_melee 不应有可选组 {group}");
            }
        }

        // ---------- 慢放重放 ----------

        private static readonly string[] ReplayScripts =
        {
            "feel_melee", "feel_x_latency", "feel_x_combo", "feel_x_cancel", "feel_x_crowd", "feel_projectile", "feel_dodge_cancel", "feel_kill",
            "feel_motion_accel_decel", "feel_matrix_heavy_greatsword",
        };

        private static string[] Cells => new[] { "2d_action", "3d_targeted" };

        private static string LogicTimeline(LabRecording r)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var t in r.Ticks)
            {
                sb.Append(t.Tick).Append('|').Append(t.Position.X.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(t.Position.Y.ToString("R", CultureInfo.InvariantCulture))
                    .Append('|').Append(t.Facing.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(t.MovementState).Append('|').Append(t.Alive).Append('\n');
            }

            foreach (var e in r.Events)
            {
                sb.Append("E|").Append(e.Tick).Append('|').Append(e.Kind).Append('|').Append(e.Source).Append('|').Append(e.Target).Append('|').Append(e.SkillId).Append('|')
                    .Append(e.Instance).Append('|').Append(e.Amount.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(e.Detail).Append('\n');
            }

            if (r.Feel != null)
            {
                foreach (var e in r.Feel.Events)
                {
                    sb.Append("F|").Append(e.Tick).Append('|').Append(e.Kind).Append('|').Append(e.Actor).Append('|').Append(e.Target).Append('|').Append(e.SkillId).Append('|')
                        .Append(e.Detail).Append('|').Append(e.Detail2).Append('|').Append(e.A).Append('|').Append(e.B).Append('|').Append(e.C).Append('|').Append(e.D.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                }

                foreach (var t in r.Feel.Ticks)
                {
                    sb.Append("T|").Append(t.Tick).Append('|').Append(t.BufferSlots).Append('|').Append(t.Mode).Append('|').Append(t.Source).Append('|').Append(t.Speed.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                }

                foreach (var p in r.Feel.Presentation)
                {
                    sb.Append("P|").Append(p.Tick).Append('|').Append(p.Kind).Append('|').Append(p.Text).Append('|').Append(p.Value.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(p.Count).Append('\n');
                }
            }

            return sb.ToString();
        }

        [Fact]
        public void SlowReplay_LeavesLogicBitIdentical_AndOnlyScalesThePresentationClock()
        {
            var runner = LabTestSupport.Runner;
            foreach (var id in ReplayScripts)
            {
                var script = LabTestSupport.Script(id);
                foreach (var cell in Cells)
                {
                    var baseline = runner.Record(script, cell);
                    var baselineLogic = runner.FingerprintOf(script, cell, baseline).Project(runner.Registry, MetricClass.Logic);
                    var baselineTimeline = LogicTimeline(baseline);
                    foreach (var scale in new[] { 0.25, 0.5, 2.0 })
                    {
                        var variant = new LabRunVariant { TimeScale = scale };
                        var replay = runner.Record(script, cell, variant);

                        // 逻辑：固定步序列、事件流、手感事件与表现指令逐位一致；逻辑类度量逐字节一致。
                        Assert.Equal(baselineTimeline, LogicTimeline(replay));
                        Assert.Equal(baselineLogic, runner.FingerprintOf(script, cell, replay, variant).Project(runner.Registry, MetricClass.Logic));

                        // 表现时钟：帧距按尺度缩放，帧时间严格等距，帧数随之按反比变化（总模拟时长不变）。
                        var frameDt = 1.0 / script.Meta.FrameRateCap * scale;
                        Assert.Equal(frameDt, replay.EffectiveFrameSeconds, 12);
                        for (var i = 1; i < replay.Frames.Count; i++)
                        {
                            Assert.Equal(frameDt, replay.Frames[i].Time - replay.Frames[i - 1].Time, 9);
                        }

                        var totalSeconds = script.Meta.DurationTicks / (double)script.Meta.TickRate;
                        Assert.InRange(replay.Frames.Count, (int)Math.Floor(totalSeconds / frameDt) - 1, (int)Math.Ceiling(totalSeconds / frameDt) + 2);
                        if (scale < 1.0)
                        {
                            Assert.True(replay.Frames.Count > baseline.Frames.Count);
                        }
                        else
                        {
                            Assert.True(replay.Frames.Count < baseline.Frames.Count);
                        }
                    }
                }
            }
        }

        [Fact]
        public void SlowReplay_PresentationMetrics_FollowTheScaledFrameClock()
        {
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("feel_x_latency_fps30");
            foreach (var cell in ActionCells)
            {
                var variant = new LabRunVariant { TimeScale = 0.25 };
                var baseline = runner.Run(script, cell);
                var replay = runner.Run(script, cell, variant);
                var b = new FeelFp(baseline);
                var r = new FeelFp(replay);

                // 帧步长缩成四分之一；逻辑通道不变；帧量化多出的一截仍落在 [0, 帧步长)；更细的帧不会让响应更晚可见。
                Assert.Equal(b.Num("latency.frame_ms") * 0.25, r.Num("latency.frame_ms"), 6);
                Assert.Equal(b.Text("latency.inputs"), r.Text("latency.inputs"));
                Assert.True(r.Nums("latency.visible_excess_ms").All(x => x >= -1e-6 && x < r.Num("latency.frame_ms")));
                var slow = r.Nums("latency.visible_ms");
                var normal = b.Nums("latency.visible_ms");
                for (var i = 0; i < slow.Count; i++)
                {
                    Assert.True(slow[i] <= normal[i] + 1e-6);
                }
            }
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void TimeScale_MustBeFinitePositive(double scale)
        {
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("feel_melee");
            Assert.Throws<ArgumentOutOfRangeException>(() => runner.Record(script, "2d_action", new LabRunVariant { TimeScale = scale }));
        }

        [Fact]
        public void TimeScale_DoesNotChangeTheDatasetKey()
        {
            var cell = LabTestSupport.Runner.Dataset.Catalog.GetScenario("2d_action");
            Assert.Equal(new LabRunVariant().DatasetKey(cell), new LabRunVariant { TimeScale = 0.25 }.DatasetKey(cell));
        }

        // ---------- 手感脚本的帧率上限不变（06 第 3.3 节末条，A16） ----------

        [Fact]
        public void FeelScripts_LogicGroups_AreIdenticalAt30_60_120FrameRateCaps()
        {
            var runner = LabTestSupport.Runner;
            var scripts = LabTestSupport.FeelScripts();
            Assert.NotEmpty(scripts);
            foreach (var script in scripts)
            {
                foreach (var cell in new[] { "2d_action", "3d_targeted" })
                {
                    var logic = new List<string>();
                    var frames = new List<double>();
                    foreach (var cap in new[] { 30, 60, 120 })
                    {
                        var clone = LabTestSupport.CloneWithFrameRate(script, cap);
                        var fp = runner.Run(clone, cell);
                        logic.Add(fp.Project(runner.Registry, MetricClass.Logic));
                        frames.Add(((JsonNumber)((JsonObject)fp.Groups["performance"])["frames"]).Value);
                    }

                    Assert.True(logic[0] == logic[1], $"{script.Meta.ScriptId} @ {cell}：30 与 60 帧上限的逻辑组不一致");
                    Assert.True(logic[0] == logic[2], $"{script.Meta.ScriptId} @ {cell}：30 与 120 帧上限的逻辑组不一致");
                    Assert.True(frames[0] < frames[1] && frames[1] < frames[2], $"{script.Meta.ScriptId}：帧数应随帧率上限递增");
                }
            }
        }
    }
}
