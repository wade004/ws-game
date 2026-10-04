using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Core.Foundation.Common.Json;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 姿势与动画脚本（M5-S4，ADR-0147）的运行期验收：受击反应 → 受击姿势子键、霸体不播受击、动作各相位的剪辑播放速率与重映射后的相位长度一致、
    /// 冲刺速度与移动剪辑的步幅速率。期望全部由"数据根里的毫秒/倍率 × 预设 × 标定 × 规则"在用例里推出，不写死裸数。
    /// 复现（改动前动画状态机由伤害驱动、没有子键也没有速率，这些请求无从成立）加不变量（无敌窗口里不请求受击姿势、速率×实际毫秒=作者毫秒）。
    /// 观测的是表现层对外发出的请求；引擎侧把速率交给播放器的最后一跳由引擎侧 PlayMode 用例验收。
    /// </summary>
    public sealed class PoseExtSceneTests
    {
        private static readonly string[] ActionCells = { "2d_action", "2_5d_action", "3d_action" };

        private const string React = "feel_pose_react";
        private const string Armor = "feel_pose_armor";
        private const string Motion = "feel_pose_motion";
        private const string PosePreset = "feel.preset.pose_motion";
        private static readonly string PoseDataDir = Path.Combine("lab", "fixtures", "data", "pose", "feel");

        private static List<FeelEventRecord> Events(LabRecording record, string kind) =>
            record.Feel!.Events.Where(e => e.Kind == kind).ToList();

        private static LabRecording Run(string script, string cell) => LabTestSupport.Runner.Record(LabTestSupport.Script(script), cell);

        private static double PresetValue(string field)
        {
            var row = FeelRules.Row(Path.Combine(PoseDataDir, "feel.preset.json"), PosePreset);
            return ((JsonNumber)((JsonObject)row["values"])[field]).Value;
        }

        private static double CalibrationBaseSpeed()
        {
            var row = FeelRules.Row(Path.Combine(PoseDataDir, "feel.calibration.json"), "feel.calibration.lab_pose_motion");
            return ((JsonNumber)row["base_speed"]).Value;
        }

        // ------------------------------------------------------------------ 受击反应 -> 受击姿势

        [Theory]
        [InlineData("2d_action")]
        [InlineData("2_5d_action")]
        [InlineData("3d_action")]
        public void HitPoses_FollowTheAdjudicatedReactionPhases_AndTheInvulnerableHitRequestsNone(string cell)
        {
            var record = Run(React, cell);

            // 期望：每次击倒 = 反应落地（knockback 硬直段）、倒地事件（knockdown）、起身开始事件（getup）三个受击姿势，tick 与裁决事件一致。
            var expected = new List<(int Tick, string Sub)>();
            foreach (var r in Events(record, "reaction").Where(e => e.Detail == "Knockdown"))
            {
                expected.Add((r.Tick, "knockback"));
            }

            expected.AddRange(Events(record, "knocked_down").Select(e => (e.Tick, "knockdown")));
            expected.AddRange(Events(record, "getup_started").Select(e => (e.Tick, "getup")));
            expected = expected.OrderBy(x => x.Tick).ToList();

            var actual = Events(record, "pose_state")
                .Where(e => e.Detail == "Hit" && e.Target == "stake_downable")
                .Select(e => (e.Tick, e.Detail2))
                .ToList();

            Assert.NotEmpty(expected);
            Assert.Equal(expected, actual);

            // 不变量：起身无敌窗口里的那一击（结局 Invulnerable）不产生反应，也就没有受击姿势请求。
            var avoided = Events(record, "hit_confirmed").Where(e => e.Detail == "Invulnerable").ToList();
            Assert.NotEmpty(avoided);
            foreach (var hit in avoided)
            {
                Assert.DoesNotContain(actual, a => a.Tick == hit.Tick);
            }
        }

        [Theory]
        [InlineData("2d_action")]
        [InlineData("2_5d_action")]
        [InlineData("3d_action")]
        public void ArmorReactionNone_PlaysNoHitPose(string cell)
        {
            var record = Run(Armor, cell);

            // 复现：霸体窗口内确实有命中打在精英身上，裁决给出的反应是 None。
            var armored = Events(record, "hit_confirmed")
                .Where(e => e.Target == "elite" && e.Detail2.Contains("reaction=None", StringComparison.Ordinal))
                .ToList();
            Assert.NotEmpty(armored);

            // 不变量：反应为 none 的命中不请求任何受击姿势（精英自己的施法姿势照常）。
            var hitPoses = Events(record, "pose_state").Where(e => e.Detail == "Hit").ToList();
            Assert.Empty(hitPoses);
            Assert.Contains(Events(record, "pose_state"), e => e.Target == "elite");
        }

        // ------------------------------------------------------------------ 播放速率与冲刺

        private static double PhaseRate(double authoredMs, double scale)
        {
            var ticks = FeelRules.T(authoredMs * scale);
            return authoredMs / (ticks * FeelRules.StepSeconds * 1000.0);
        }

        [Theory]
        [InlineData("2d_action")]
        [InlineData("2_5d_action")]
        [InlineData("3d_action")]
        public void RemappedAction_ClipRateTimesRealPhaseLengthEqualsAuthoredLength(string cell)
        {
            var record = Run(Motion, cell);
            const string skill = "skill.lab_a_slash";
            var startupMs = FeelRules.TimelineMs(skill, "startup_ms");
            var activeMs = FeelRules.TimelineMs(skill, "active_ms");
            var startupScale = PresetValue("phase_scale.startup");

            // 预设把前摇缩到一半：前摇相速率 > 1，判定相不动（速率 1）；速率 = 作者毫秒 ÷ 重映射后实际毫秒。
            Assert.True(startupScale < 1.0);
            var startupRate = PhaseRate(startupMs, startupScale);
            var activeRate = PhaseRate(activeMs, 1.0);
            Assert.True(startupRate > 1.0);

            var rates = Events(record, "pose_rate")
                .Where(e => e.Target == "player" && e.Detail == "Cast")
                .ToList();
            Assert.Contains(rates, e => Math.Abs(e.D - startupRate) < 1e-6);
            Assert.Contains(rates, e => Math.Abs(e.D - activeRate) < 1e-6);

            // 不变量：用真实的相位切换事件量出各相实际 tick 数，速率 × 实际长度 = 作者长度（动画与判定时间线不脱节）。
            var started = Events(record, "action_started").First(e => e.Actor == "player");
            var active = Events(record, "action_phase").First(e => e.Actor == "player" && e.Detail == "Active");
            var realStartupMs = (active.Tick - started.Tick) * FeelRules.StepSeconds * 1000.0;
            Assert.Equal(startupMs, startupRate * realStartupMs, 6);
            Assert.Equal(FeelRules.T(startupMs * startupScale), active.Tick - started.Tick);
        }

        [Theory]
        [InlineData("2d_action")]
        [InlineData("2_5d_action")]
        [InlineData("3d_action")]
        public void Sprint_SteadySpeedIsBaseSpeedTimesSprintRatio_AndStrideRateFollowsTheSpeedRatio(string cell)
        {
            var record = Run(Motion, cell);
            var fp = FeelFp.Of(Motion, cell);

            // 冲刺稳态速度 = 基础移速 × 冲刺速度比（预设值，与走路/跑步的比值无关）。
            var sprintRatio = PresetValue("sprint_speed_ratio");
            Assert.Equal(sprintRatio * CalibrationBaseSpeed(), fp.Num("motion.speed_max"), 6);

            // 移动剪辑的步幅速率 = 速度比 ÷ 步态参考速度比；冲刺无专属参考时参考比取 1，所以稳态时速率 = 冲刺速度比（在 0.5 到 2.0 的夹取范围内）。
            var moveRates = Events(record, "pose_rate").Where(e => e.Target == "player" && e.Detail == "Move").Select(e => e.D).ToList();
            Assert.NotEmpty(moveRates);
            Assert.InRange(sprintRatio, 0.5, 2.0);
            Assert.Equal(sprintRatio, moveRates.Max(), 6);
            Assert.All(moveRates, r => Assert.InRange(r, 0.5 - 1e-9, 2.0 + 1e-9));
        }

        // ------------------------------------------------------------------ 按住冲刺（M5-S8，ADR-0153）

        private const string SprintHold = "feel_pose_sprint_hold";

        /// <summary>每个 tick 的位移速度（该 tick 末位置与上一 tick 末位置的距离 ÷ 步长）；键为 tick。</summary>
        private static Dictionary<int, double> TickSpeeds(LabRecording record)
        {
            var speeds = new Dictionary<int, double>();
            for (var i = 1; i < record.Ticks.Count; i++)
            {
                var a = record.Ticks[i - 1].Position;
                var b = record.Ticks[i].Position;
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                speeds[record.Ticks[i].Tick] = Math.Sqrt(dx * dx + dy * dy) / record.StepSeconds;
            }

            return speeds;
        }

        private static (int From, int To) Window(InputScript script, string action, string kind, string endKind)
        {
            var begin = script.Events.First(e => e.Action == action && e.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase)).Tick;
            var end = script.Events.First(e => e.Action == action && e.Kind.ToString().Equals(endKind, StringComparison.OrdinalIgnoreCase) && e.Tick > begin).Tick;
            return (begin, end);
        }

        [Theory]
        [InlineData("2d_action")]
        [InlineData("2_5d_action")]
        [InlineData("3d_action")]
        public void HoldSprint_SteadySpeedFollowsTheHeldKey_WalkBeforeAndAfter(string cell)
        {
            var script = LabTestSupport.Script(SprintHold);
            var record = Run(SprintHold, cell);
            var speeds = TickSpeeds(record);
            var baseSpeed = CalibrationBaseSpeed();
            var walkSpeed = FeelRules.Preset("feel.preset.arpg_responsive").N("walk_speed_ratio") * baseSpeed;
            var sprintSpeed = PresetValue("sprint_speed_ratio") * baseSpeed;
            Assert.True(sprintSpeed > walkSpeed);

            var push = Window(script, "input.action.move", "axis", "axis");           // 推杆起止
            var hold = Window(script, "input.action.sprint", "press", "release");     // 第一次按住（有推杆）
            var settle = 10;                                                             // 起步/转速过渡的容差 tick（只用于选稳态窗口，不进断言数值）

            // 复现：按住冲刺键之前只是走路（达不到冲刺速度），按住后稳态达到冲刺速度；松键后回到走路速度。
            double MaxIn(int from, int to) => Enumerable.Range(from, to - from).Max(t => speeds[t]);
            Assert.Equal(walkSpeed, MaxIn(push.From, hold.From), 6);
            Assert.Equal(sprintSpeed, MaxIn(hold.From, hold.To), 6);
            Assert.Equal(walkSpeed, MaxIn(hold.To + settle, push.To), 6);

            // 不变量：任何 tick 的速度都不超过冲刺上限；按住窗口之外（含松键后的过渡）速度从不超过走路上限，除了按住窗口刚结束的减速尾巴（只会比冲刺低）。
            Assert.All(speeds.Values, v => Assert.True(v <= sprintSpeed + 1e-9));
            foreach (var t in Enumerable.Range(push.From, hold.From - push.From))
            {
                Assert.True(speeds[t] <= walkSpeed + 1e-9, $"tick {t}");
            }
        }

        [Theory]
        [InlineData("2d_action")]
        [InlineData("2_5d_action")]
        [InlineData("3d_action")]
        public void HoldSprint_WithoutMoveInput_ProducesNoMovement(string cell)
        {
            var script = LabTestSupport.Script(SprintHold);
            var record = Run(SprintHold, cell);
            var speeds = TickSpeeds(record);
            var stops = script.Events.Where(e => e.Action == "input.action.move").Select(e => e.Tick).OrderBy(t => t).ToList();
            var idleHold = script.Events.Where(e => e.Action == "input.action.sprint" && e.Tick > stops.Last()).Select(e => e.Tick).OrderBy(t => t).ToList();

            // 复现：推杆松开后又按住冲刺键（没有移动输入）——这段时间没有移动请求，位置不变。
            Assert.Equal(2, idleHold.Count);
            var decelTicks = FeelRules.T(FeelRules.Preset("feel.preset.arpg_responsive").N("decel_ms"));
            foreach (var tick in Enumerable.Range(stops.Last() + decelTicks + 2, idleHold[1] - (stops.Last() + decelTicks + 2)))
            {
                Assert.False(record.Ticks.First(s => s.Tick == tick).MoveRequested, $"tick {tick}");
                Assert.Equal(0.0, speeds[tick], 9);
            }
        }

        // ------------------------------------------------------------------ 既有脚本不受影响

        [Fact]
        public void ScriptsWithoutPoseExt_HaveNoPoseGroup_AndNoPoseEvents()
        {
            foreach (var script in LabTestSupport.FeelScripts().Where(s => s.Meta.PoseExt == null))
            {
                var fp = FeelFp.Of(script.Meta.ScriptId, "2d_action");
                Assert.False(fp.Has("poseext"), script.Meta.ScriptId);
            }

            var withPose = LabTestSupport.FeelScripts().Where(s => s.Meta.PoseExt != null).Select(s => s.Meta.ScriptId).OrderBy(x => x).ToList();
            Assert.Equal(new[] { Armor, Motion, React, SprintHold }.OrderBy(x => x).ToList(), withPose);
        }
    }
}
