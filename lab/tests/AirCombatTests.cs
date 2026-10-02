using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Core.Foundation.Feel;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 空中战斗与实验室竖直格（手感落地 M4-W1b，ADR-0130 追加决定"竖直轴二期·战斗部分"）的运行期验收：八个 <c>space.*</c> 脚本
    /// （空中连击、击飞体型缩放、空中受击反应上限、靶子主动跳跃、受控位移深度锁及其缺省对照、动作式三维射程、真实姿势集的空中姿势）
    /// 在竖直格子上的复现度量。期望值全部由数据行与规则算出（预设值、标定的参考身高、跳跃高度、毫秒换算 tick），不写死裸数。
    /// </summary>
    public sealed class AirCombatTests
    {
        private const double Dt = 1.0 / 60.0;
        private static readonly string[] VerticalCells = { "side_2d_targeted", "side_2d_action", "volume_targeted", "volume_action" };
        private static readonly string[] SideCells = { "side_2d_targeted", "side_2d_action" };
        private static readonly string[] VolumeCells = { "volume_targeted", "volume_action" };
        private static readonly string[] NewScripts =
        {
            "space.air_combo", "space.launch_body_scale", "space.air_reaction", "space.dummy_air", "space.depth_knockback",
            "space.depth_knockback_free", "space.range_action", "space.air_pose_real",
        };

        public static IEnumerable<object[]> VerticalCellData() => VerticalCells.Select(c => new object[] { c });

        public static IEnumerable<object[]> SideCellData() => SideCells.Select(c => new object[] { c });

        public static IEnumerable<object[]> VolumeCellData() => VolumeCells.Select(c => new object[] { c });

        public static IEnumerable<object[]> PlaneCellData() => LabTestSupport.AllCells.Select(c => new object[] { c });

        private static LabRecording Record(string script, string cell, LabRunVariant? variant = null) =>
            LabTestSupport.Runner.Record(LabTestSupport.Script(script), cell, variant);

        private static AirCombatRecording Air(string script, string cell) =>
            Record(script, cell).Space?.AirCombat ?? throw new InvalidOperationException($"{script} @ {cell} 没有空中战斗记录");

        private static JsonElement FixtureRow(string relativePath, string id)
        {
            var text = File.ReadAllText(Path.Combine(LabTestSupport.FixturesDir, "data", "space_ext", relativePath));
            var doc = JsonDocument.Parse(text);
            return doc.RootElement.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        }

        private static JsonElement PresetValues(string presetId) => FixtureRow("feel/feel.preset.json", presetId).GetProperty("values");

        private static double ReferenceHeight(string presetId)
        {
            var calibration = FixtureRow("feel/feel.calibration.json", "feel.calibration." + presetId.Replace("feel.preset.", "lab_"));
            return calibration.GetProperty("reference_height").GetDouble();
        }

        private static string Presets(string script) => LabTestSupport.Script(script).Meta.PresetId;

        // ---------- 适用性：竖直格有 air_combat 组，平面格没有，既有脚本不受影响 ----------

        [Fact]
        public void NewScripts_AreInTheSubsetOfEveryVerticalCell()
        {
            foreach (var id in NewScripts)
            {
                var script = LabTestSupport.Script(id);
                Assert.NotNull(script.Meta.SpaceExt);
                var cells = LabTestSupport.Runner.ApplicableCells(script).Select(c => c.Cell).ToList();
                foreach (var cell in VerticalCells)
                {
                    Assert.Contains(cell, cells);
                }
            }
        }

        [Theory]
        [MemberData(nameof(PlaneCellData))]
        public void PlaneCells_NeverGetAirCombat_AndRunEveryNewScript(string cell)
        {
            foreach (var id in NewScripts)
            {
                var record = Record(id, cell);
                Assert.Null(record.Space?.AirCombat);
                Assert.False(LabTestSupport.Runner.Run(LabTestSupport.Script(id), cell).Groups.ContainsKey("air_combat"));
            }
        }

        [Fact]
        public void ExistingSpaceScripts_NeverGetAirCombat_SoTheirBaselinesAreUntouched()
        {
            foreach (var id in new[] { "space.air_control", "space.terrain", "space.air_hit", "space.launch_stack", "space.height_offset", "space.range_3d", "space.jump", "space.launch" })
            {
                foreach (var cell in VerticalCells)
                {
                    Assert.Null(Record(id, cell).Space?.AirCombat);
                    Assert.False(LabTestSupport.Runner.Run(LabTestSupport.Script(id), cell).Groups.ContainsKey("air_combat"));
                }
            }
        }

        // ---------- 一、空中连击：高度封顶、空中硬直撑到落地、落地事件 ----------

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void AirCombo_TheHeightCapBindsTheStackedLaunches_AndTheStaggerLastsUntilLanding(string cell)
        {
            var preset = Presets("space.air_combo");
            var values = PresetValues(preset);
            var cap = values.GetProperty("launch_height_cap").GetDouble() * ReferenceHeight(preset);
            var single = values.GetProperty("launch_height").GetDouble() * ReferenceHeight(preset);
            var run = LabTestSupport.Runner.Run(LabTestSupport.Script("space.air_combo"), cell);
            var group = (global::Core.Foundation.Common.Json.JsonObject)run.Groups["air_combat"];
            double N(string name) => ((global::Core.Foundation.Common.Json.JsonNumber)group[name]).Value;

            // 复现：三下都抛起；叠加把顶点抬过单次击飞高度，但被绝对高度上限封在 cap。
            Assert.Equal(3, N("dummy_launches_max"));
            Assert.True(N("dummy_peak_max") > single + 0.1, "叠加应抬高顶点");
            Assert.InRange(N("dummy_peak_max"), cap - 0.01, cap + 1e-6);

            // 不变量：air_stun_until_land 下硬直不早于落地结束；落地后至多再多一两个 tick。
            Assert.InRange(N("dummy_stagger_end_vs_land_min"), 0, 2);
            var hitStunTicks = (int)Math.Round(values.TryGetProperty("hit_stun_ms", out var hs) ? hs.GetDouble() / 1000.0 / Dt : 9);
            Assert.True(N("dummy_stagger_air_ticks_max") > 3 * Math.Max(hitStunTicks, 9));

            // 落地事件：整段连击只落地一次，落地高度是地面高度。
            Assert.Equal(1, N("landed_count"));
            Assert.Equal(0.0, N("landed_height_max"), 6);
        }

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void AirCombo_TheLandedEventAirTimeMatchesTheAirborneTicks(string cell)
        {
            var air = Air("space.air_combo", cell);
            var landed = Assert.Single(air.Landed);
            var airTicks = air.DummyAirborne["stake"].Count(a => a);
            Assert.InRange(landed.AirSeconds, (airTicks - 2) * Dt, (airTicks + 2) * Dt);
            Assert.True(landed.ImpactSpeed > 0.0);
        }

        // ---------- 二、击飞体型缩放 ----------

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void LaunchBodyScale_TheApexIsTheLaunchHeightTimesTheScale(string cell)
        {
            var preset = Presets("space.launch_body_scale");
            var values = PresetValues(preset);
            var expected = values.GetProperty("launch_height").GetDouble() * values.GetProperty("launch_body_scale").GetDouble() * ReferenceHeight(preset);
            var group = (global::Core.Foundation.Common.Json.JsonObject)LabTestSupport.Runner.Run(LabTestSupport.Script("space.launch_body_scale"), cell).Groups["air_combat"];
            var peak = ((global::Core.Foundation.Common.Json.JsonNumber)group["dummy_peak_max"]).Value;
            Assert.InRange(peak, expected - 0.005, expected + 1e-6);
            Assert.True(peak < values.GetProperty("launch_height").GetDouble() * ReferenceHeight(preset), "缩放后的顶点应低于不缩放的顶点");
        }

        // ---------- 三、空中受击反应上限 ----------

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void AirReaction_AirHitsAreCappedToTheCapReaction_SoTheTargetIsLaunchedOnlyOnce(string cell)
        {
            var cap = PresetValues(Presets("space.air_reaction")).GetProperty("air_reaction_cap").GetString();
            Assert.Equal("flinch", cap);
            var run = LabTestSupport.Runner.Run(LabTestSupport.Script("space.air_reaction"), cell);
            var reaction = (global::Core.Foundation.Common.Json.JsonObject)run.Groups["reaction"];
            Assert.Equal("Flinch:2;Knockback:1", ((global::Core.Foundation.Common.Json.JsonString)reaction["reaction_counts"]).Value);
            var air = (global::Core.Foundation.Common.Json.JsonObject)run.Groups["air_combat"];
            Assert.Equal(1, ((global::Core.Foundation.Common.Json.JsonNumber)air["dummy_launches_max"]).Value);
        }

        // ---------- 四、靶子的主动跳跃与空中移动 ----------

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void DummyAir_TheDummyJumpsAndMovesInTheAirAtTheAirControlRatio(string cell)
        {
            var script = LabTestSupport.Script("space.dummy_air");
            var ratio = script.Meta.SpaceExt!.AirControl;
            var scenario = FixtureRowFromScenario(cell);
            var group = (global::Core.Foundation.Common.Json.JsonObject)LabTestSupport.Runner.Run(script, cell).Groups["air_combat"];
            double N(string name) => ((global::Core.Foundation.Common.Json.JsonNumber)group[name]).Value;

            Assert.Equal("stake=1/0;", ((global::Core.Foundation.Common.Json.JsonString)group["dummy_jumps"]).Value);
            Assert.Equal(ratio!.Value, N("dummy_air_ground_step_ratio"), 6);
            Assert.InRange(N("dummy_peak_max"), scenario.JumpHeight - 0.005, scenario.JumpHeight + 1e-6);
            Assert.Equal(1, N("landed_count"));
        }

        [Theory]
        [MemberData(nameof(PlaneCellData))]
        public void DummyAir_PlaneCellsIgnoreTheDummyJump_AndTheDummyStillWalks(string cell)
        {
            var record = Record("space.dummy_air", cell);
            Assert.Null(record.Space?.AirCombat);
            var track = record.Ticks;
            Assert.True(track.Count > 0);
        }

        // ---------- 五、受控位移的深度锁 ----------

        [Theory]
        [MemberData(nameof(SideCellData))]
        public void DepthKnockback_TheLockedWorldPushesOnlyLaterally(string cell)
        {
            var preset = Presets("space.depth_knockback");
            var distance = PresetValues(preset).GetProperty("knockback_distance").GetDouble() * ReferenceHeight(preset);
            var group = (global::Core.Foundation.Common.Json.JsonObject)LabTestSupport.Runner.Run(LabTestSupport.Script("space.depth_knockback"), cell).Groups["air_combat"];
            double N(string name) => ((global::Core.Foundation.Common.Json.JsonNumber)group[name]).Value;
            Assert.Equal(0.0, N("dummy_depth_drift_max"), 9);
            Assert.Equal(distance, N("dummy_displacement_x_max"), 6);
        }

        [Theory]
        [MemberData(nameof(VolumeCellData))]
        public void DepthKnockback_TheVolumeWorldPushesAlongTheSubmittedDirection(string cell)
        {
            var group = (global::Core.Foundation.Common.Json.JsonObject)LabTestSupport.Runner.Run(LabTestSupport.Script("space.depth_knockback"), cell).Groups["air_combat"];
            Assert.True(((global::Core.Foundation.Common.Json.JsonNumber)group["dummy_depth_drift_max"]).Value > 0.1);
        }

        [Theory]
        [MemberData(nameof(SideCellData))]
        public void DepthKnockbackFree_WithoutTheOptionTheKnockbackKeepsItsDepthComponent(string cell)
        {
            var group = (global::Core.Foundation.Common.Json.JsonObject)LabTestSupport.Runner.Run(LabTestSupport.Script("space.depth_knockback_free"), cell).Groups["air_combat"];
            Assert.True(((global::Core.Foundation.Common.Json.JsonNumber)group["dummy_depth_drift_max"]).Value > 0.1);
        }

        // ---------- 六、动作式结算的三维射程 ----------

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void RangeAction_BothSettlementKindsHitOnlyTheSwingThatIsInRange(string cell)
        {
            var record = Record("space.range_action", cell);
            var damage = record.Events.Where(e => e.Kind == "damage").ToList();
            Assert.Single(damage);
            var range = FixtureRow("skill/skill.def.json", "skill.lab_spx_pick").GetProperty("range").GetDouble();
            var dummy = FixtureRow("lab/lab.dummy_set.json", "lab.dummy_set.lab_space_ext").GetProperty("entries").EnumerateArray()
                .Single(e => e.GetProperty("name").GetString() == "far_high");
            var dx = dummy.GetProperty("position").GetProperty("x").GetDouble();
            var dz = dummy.GetProperty("height").GetDouble();

            // 命中那一下发生时，玩家到靶子的三维距离在射程内；第一下出手时（玩家在原点）三维距离超过射程。
            Assert.True(Math.Sqrt(dx * dx + dz * dz) > range);
            var playerX = record.Ticks[damage[0].Tick].Position.X;
            Assert.True(Math.Sqrt((dx - playerX) * (dx - playerX) + dz * dz) <= range);
        }

        [Theory]
        [MemberData(nameof(PlaneCellData))]
        public void RangeAction_PlaneCellsIgnoreTheGate_EverySwingHits(string cell)
        {
            var record = Record("space.range_action", cell);
            Assert.Equal(2, record.Events.Count(e => e.Kind == "damage"));
        }

        // ---------- 七、真实姿势集的空中姿势 ----------

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void AirPoseReal_RequestsCarryTheVariantDimensions_AndFallBackAlongTheRealAnimSet(string cell)
        {
            var record = Record("space.air_pose_real", cell);
            var requests = record.Space!.Ext!.AirPoses;
            Assert.NotEmpty(requests);

            // 键表读真实姿势集行（含继承链）：落到的键必须真的在该行的剪辑键里。
            var text = File.ReadAllText(Path.Combine(LabTestSupport.RepoRoot(), "data", "_framework", "display", "display.anim_set.json"));
            var row = JsonDocument.Parse(text).RootElement.GetProperty("rows").EnumerateArray()
                .Single(r => r.GetProperty("id").GetString() == "display.anim_set.std_dummy_biped");
            var clips = row.GetProperty("clips").EnumerateObject().Select(p => p.Name).ToHashSet();
            foreach (var request in requests.Where(r => r.Entity == "player"))
            {
                Assert.Contains(".combat.1h.wounded", request.Requested);
                if (request.Resolved.Length > 0)
                {
                    Assert.Contains(request.Resolved, clips);
                    Assert.True(request.Depth >= 0);
                }
            }

            // 目标（靶子）的受击请求不带玩家的姿态维度。
            foreach (var request in requests.Where(r => r.Entity == "stake"))
            {
                Assert.DoesNotContain("combat", request.Requested);
            }
        }

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void AirPoseReal_TheLandHoldFollowsTheLandHoldMsOfTheUnit(string cell)
        {
            var preset = Presets("space.air_pose_real");
            var ms = PresetValues(preset).GetProperty("land_hold_ms").GetDouble();
            var expected = FeelCalibration.MillisecondsToTicks(ms, Dt);
            var phases = Record("space.air_pose_real", cell).Space!.Ext!.PlayerAirPhases;
            var landTicks = phases.Count(p => p == "land");
            Assert.Equal(expected, landTicks);
        }

        // ---------- 脚本格式：新事件与新选项往返 ----------

        [Fact]
        public void ScriptFormat_NewEventKindsAndOptionsRoundTrip()
        {
            foreach (var id in new[] { "space.dummy_air", "space.depth_knockback", "space.range_action", "space.air_pose_real", "space.air_combo" })
            {
                var script = LabTestSupport.Script(id);
                var again = InputScript.Parse(script.ToJson());
                Assert.Equal(script.ToJson(), again.ToJson());
                Assert.Equal(script.Events.Count, again.Events.Count);
            }

            var dummy = LabTestSupport.Script("space.dummy_air");
            Assert.Contains(dummy.Events, e => e.Kind == ScriptEventKind.Jump && e.Actor == "stake");
            Assert.Contains(dummy.Events, e => e.Kind == ScriptEventKind.Move && e.Actor == "stake");
            Assert.True(LabTestSupport.Script("space.depth_knockback").Meta.SpaceExt!.DepthLockControlledMotion);
            Assert.True(LabTestSupport.Script("space.range_action").Meta.SpaceExt!.SpatialRangeHitWindow);
            Assert.Equal("display.anim_set.std_dummy_biped", LabTestSupport.Script("space.air_pose_real").Meta.SpaceExt!.PoseAnimSet);
        }

        [Fact]
        public void ScriptFormat_JumpAndMoveEventsNeedAnActor()
        {
            var script = LabTestSupport.Script("space.dummy_air");
            var text = script.ToJson().Replace("\"actor\": \"stake\"", "\"actor\": \"\"");
            Assert.Throws<LabFormatException>(() => InputScript.Parse(text));
        }

        [Fact]
        public void ScriptFormat_AUnknownActorIsRejectedAtRunTime()
        {
            var script = LabTestSupport.Script("space.dummy_air");
            var text = script.ToJson().Replace("\"actor\": \"stake\"", "\"actor\": \"nobody\"");
            var broken = InputScript.Parse(text);
            Assert.Throws<LabFormatException>(() => LabTestSupport.Runner.Record(broken, "side_2d_targeted"));
        }

        private static (double JumpHeight, double Gravity) FixtureRowFromScenario(string cell)
        {
            var row = FixtureRow("lab/lab.scenario.json", "lab.scenario." + cell);
            return (row.GetProperty("jump_height").GetDouble(), row.GetProperty("gravity").GetDouble());
        }
    }
}
