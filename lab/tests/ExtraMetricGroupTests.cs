using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 可选度量组（<c>meta.extraMetrics</c>，M5-S7，ADR-0151）的判定逻辑：用手工拼的记录证明各组的口径
    /// （组的数值进指纹后由脚本基线与期望守护，这里证明"口径本身是对的"，包括该报错的情形确实报出来）。
    /// </summary>
    public sealed class ExtraMetricGroupTests
    {
        private static LabRecording NewRecording(params string[] groups)
        {
            var meta = new ScriptMeta { ScriptId = "synthetic", DurationTicks = 100, TickRate = 60, FrameRateCap = 60, Feel = true };
            meta.ExtraMetrics.AddRange(groups);
            var cell = LabTestSupport.Runner.Dataset.Catalog.GetScenario("2d_action");
            var recording = new LabRecording(new InputScript(meta, new List<ScriptEvent>()), cell, 1.0 / 60.0);
            recording.Feel = new FeelRecording { Assembled = true };
            recording.Feel.InputClasses["input.action.atk"] = "attack";
            recording.Feel.InputClasses["input.action.dodge"] = "dodge";
            return recording;
        }

        private static JsonObject Compute(LabRecording recording, string group)
        {
            var registry = MetricRegistry.CreateDefault();
            var groups = registry.Compute(recording);
            Assert.True(groups.ContainsKey(group), $"指纹里没有组 {group}");
            return (JsonObject)groups[group];
        }

        private static FeelEventRecord Ev(int tick, string kind, string actor, string target = "", string skill = "", string detail = "", int a = 0) =>
            new FeelEventRecord(tick, kind, actor, target, skill, detail, string.Empty, a);

        // ---------- 脚本格式 ----------

        [Fact]
        public void ExtraMetrics_AndPlayerClass_RoundTrip_AndAreOmittedWhenUnused()
        {
            var script = LabTestSupport.Script("feel_x_latency");
            Assert.Contains(LabExtraMetrics.Latency, script.Meta.ExtraMetrics);
            var again = InputScript.Parse(script.ToJson());
            Assert.Equal(script.ToJson(), again.ToJson());
            Assert.Equal(script.Meta.ExtraMetrics, again.Meta.ExtraMetrics);

            var matrix = LabTestSupport.Script(LabMatrix.ScriptId("feel.archetype.heavy", "feel.weapon.greatsword"));
            Assert.Equal("arch.class.matrix_heavy", matrix.Meta.PlayerClass);
            Assert.Equal(matrix.Meta.PlayerClass, InputScript.Parse(matrix.ToJson()).Meta.PlayerClass);

            // 没用到的脚本不写这两个字段：既有脚本的文本与基线键序不变。
            var plain = LabTestSupport.Script("feel_melee").ToJson();
            Assert.DoesNotContain("extraMetrics", plain);
            Assert.DoesNotContain("playerClass", plain);
        }

        [Fact]
        public void UnknownExtraMetric_IsRejected()
        {
            var text = LabTestSupport.Script("feel_x_latency").ToJson().Replace("\"latency\"", "\"no_such_group\"");
            Assert.Throws<LabFormatException>(() => InputScript.Parse(text));
        }

        // ---------- attackx ----------

        [Fact]
        public void AttackExt_CountsHitsLandingAfterTheirActionWasCancelled()
        {
            var r = NewRecording(LabExtraMetrics.AttackExt);
            r.Feel!.Events.Add(Ev(5, "action_started", "player", skill: "skill.a", a: 0));
            r.Feel.Events.Add(Ev(10, "action_cancelled", "player", skill: "skill.b", detail: "CancelInto"));
            r.Feel.Events.Add(Ev(10, "hit_confirmed", "player", "stake", "skill.a", "Hit"));
            r.Feel.Events.Add(Ev(12, "hit_confirmed", "player", "stake", "skill.a", "Hit"));
            var g = Compute(r, LabExtraMetrics.AttackExt);
            Assert.Equal(1.0, ((JsonNumber)g["cancels_total"]).Value);
            Assert.Equal(1.0, ((JsonNumber)g["residual_hits"]).Value);
            Assert.Equal("12:skill.a:stake", ((JsonString)g["residual_hit_list"]).Value);
        }

        [Fact]
        public void AttackExt_ComboSuccessRate_PairsFollowUpsWithPressesDuringAnAction()
        {
            var r = NewRecording(LabExtraMetrics.AttackExt);
            r.Feel!.Events.Add(Ev(5, "action_started", "player", skill: "skill.c1", a: 0));
            r.Feel.Events.Add(Ev(25, "action_started", "player", skill: "skill.c2", a: 1));
            r.Feel.Events.Add(Ev(60, "action_finished", "player"));
            r.InjectedInputs.Add(new ScriptEvent(5, "input.action.atk", ScriptEventKind.Press));
            r.InjectedInputs.Add(new ScriptEvent(20, "input.action.atk", ScriptEventKind.Press));
            r.InjectedInputs.Add(new ScriptEvent(40, "input.action.atk", ScriptEventKind.Press));
            var g = Compute(r, LabExtraMetrics.AttackExt);

            // 第 5 tick 的按下是动作的起点，不算尝试；20 与 40 在动作进行中，两次尝试，只有一次接续成功。
            Assert.Equal(2.0, ((JsonNumber)g["combo_attempts"]).Value);
            Assert.Equal(1.0, ((JsonNumber)g["combo_follow_ups"]).Value);
            Assert.Equal(1.0, ((JsonNumber)g["combo_success"]).Value);
            Assert.Equal(0.5, ((JsonNumber)g["combo_success_rate"]).Value, 9);
        }

        [Fact]
        public void AttackExt_NoAttempts_ReportsMinusOneRate()
        {
            var r = NewRecording(LabExtraMetrics.AttackExt);
            var g = Compute(r, LabExtraMetrics.AttackExt);
            Assert.Equal(-1.0, ((JsonNumber)g["combo_success_rate"]).Value);
        }

        // ---------- latency ----------

        [Fact]
        public void Latency_PairsPressesFifo_AndSkipsDroppedOnes()
        {
            var r = NewRecording(LabExtraMetrics.Latency);
            r.StartPosition = Vec2.Zero;
            r.InjectedInputs.Add(new ScriptEvent(5, "input.action.atk", ScriptEventKind.Press));
            r.InjectedInputs.Add(new ScriptEvent(8, "input.action.atk", ScriptEventKind.Press));
            r.InjectedInputs.Add(new ScriptEvent(30, "input.action.dodge", ScriptEventKind.Press));
            // 第 8 tick 的按下被缓冲丢弃（过期），第 30 tick 的闪避在第 31 tick 开始并在同 tick 有姿势请求。
            r.Feel!.Events.Add(Ev(6, "action_started", "player", skill: "skill.a"));
            r.Feel.Events.Add(new FeelEventRecord(6, "pose_state", string.Empty, "player", string.Empty, "Attack", string.Empty));
            r.Feel.Events.Add(new FeelEventRecord(20, "buffer_dropped", "player", string.Empty, string.Empty, "input.action.atk", "Expired"));
            r.Feel.Events.Add(Ev(31, "action_started", "player", skill: "skill.d"));
            r.Feel.Events.Add(new FeelEventRecord(32, "pose_state", string.Empty, "player", string.Empty, "Attack", string.Empty));
            for (var i = 0; i < 60; i++)
            {
                r.Frames.Add(new FrameSample(i, i / 60.0, i + 1, 0, false, Vec2.Zero, 0, 0, 0));
            }

            var g = Compute(r, LabExtraMetrics.Latency);
            Assert.Equal("5:attack:1:1;8:attack:-:-;30:dodge:1:2", ((JsonString)g["inputs"]).Value);
            Assert.Equal("attack:2:1:1:1;dodge:1:1:1:2", ((JsonString)g["by_class"]).Value);
            Assert.Equal(1.0, ((JsonNumber)g["action_ticks_max"]).Value);
            Assert.Equal(2.0, ((JsonNumber)g["visible_ticks_max"]).Value);

            // 60 帧/秒、每帧恰好完成一个固定步：响应 tick R 在第 R 帧结束时可见，可见毫秒 = (R + 1 - 按下 tick) × 步长，没有量化多出的一截。
            var excess = (JsonArray)g["visible_excess_ms"];
            Assert.Equal(2, excess.Count);
            foreach (var v in excess)
            {
                Assert.Equal(0.0, ((JsonNumber)v).Value, 6);
            }
        }

        // ---------- crowd ----------

        [Fact]
        public void Crowd_ReportsSameTickPeaksAndKindDistribution()
        {
            var r = NewRecording(LabExtraMetrics.Crowd);
            r.Feel!.Presentation.Add(new FeelPresentationRecord(7, "sfx", "a"));
            r.Feel.Presentation.Add(new FeelPresentationRecord(7, "sfx", "b"));
            r.Feel.Presentation.Add(new FeelPresentationRecord(7, "sfx", "c"));
            r.Feel.Presentation.Add(new FeelPresentationRecord(7, "camera", "x"));
            r.Feel.Presentation.Add(new FeelPresentationRecord(9, "sfx", "d"));
            var g = Compute(r, LabExtraMetrics.Crowd);
            Assert.Equal(3.0, ((JsonNumber)g["sfx_peak"]).Value);
            Assert.Equal(7.0, ((JsonNumber)g["sfx_peak_tick"]).Value);
            Assert.Equal(4.0, ((JsonNumber)g["ops_peak"]).Value);
            Assert.Equal(5.0, ((JsonNumber)g["ops_total"]).Value);
            Assert.Equal("camera:1;sfx:4", ((JsonString)g["ops_by_kind"]).Value);
        }

        // ---------- facing ----------

        [Fact]
        public void Facing_CountsFramesWhoseIndexDisagreesWithTheFrameworkQuantizer()
        {
            var r = NewRecording(LabExtraMetrics.Facing);
            // 8 方向：0.0 弧度属 0 档；0.5 弧度（约 28.6 度）属 1 档；最后一帧故意写错（0.5 弧度标 0 档）。
            r.Frames.Add(new FrameSample(0, 0, 1, 0, true, Vec2.Zero, 0.0, 0, 8));
            r.Frames.Add(new FrameSample(1, 1 / 60.0, 2, 0, true, Vec2.Zero, 0.5, 1, 8));
            r.Frames.Add(new FrameSample(2, 2 / 60.0, 3, 0, true, Vec2.Zero, 0.5, 1, 8));
            r.Frames.Add(new FrameSample(3, 3 / 60.0, 4, 0, true, Vec2.Zero, 0.5, 0, 8));
            var g = Compute(r, LabExtraMetrics.Facing);
            Assert.Equal(8.0, ((JsonNumber)g["direction_count"]).Value);
            Assert.Equal("0x1,1x2,0x1", ((JsonString)g["direction_runs"]).Value);
            Assert.Equal(2.0, ((JsonNumber)g["direction_changes"]).Value);
            Assert.Equal(1.0, ((JsonNumber)g["quantize_mismatch_frames"]).Value);
        }
    }
}
