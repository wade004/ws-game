using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Core.Foundation.Common.Json;
using Core.Rules.Combat;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 空间语义（手感设计/06 第 10 节勘误 9）的运行期验收：<c>plane</c>/<c>side_2d</c>/<c>volume</c> 三个空间取值在无头宿主上各自的复现度量，
    /// 期望值全部由规则（重力、跳跃顶点、命中形状高度、<c>launch_height</c> 与冲击等级倍率）在用例里算出，不写死裸数。
    /// 记号：竖直格子 = <c>side_2d_*</c>/<c>volume_*</c>（装配竖直轴）；平面格子 = <c>2d_*</c>/<c>2_5d_*</c>/<c>3d_*</c>（不装配）。
    /// </summary>
    public sealed class SpaceSemanticsTests
    {
        private const string Attack = "input.action.lab_a_attack";
        private static readonly double Dt = 1.0 / 60.0;

        public static IEnumerable<object[]> VerticalCells() => new[]
        {
            new object[] { "side_2d_targeted" }, new object[] { "side_2d_action" },
            new object[] { "volume_targeted" }, new object[] { "volume_action" },
        };

        public static IEnumerable<object[]> PlanarCells() => LabTestSupport.AllCells.Select(c => new object[] { c });

        // ---------- 辅助 ----------

        private static LabScenario Cell(InputScript script, string cell) =>
            LabTestSupport.Runner.ApplicableCells(script).Single(c => c.Cell == cell);

        private static LabRecording Record(string script, string cell, LabRunVariant? variant = null) =>
            LabTestSupport.Runner.Record(LabTestSupport.Script(script), cell, variant);

        private static JsonObject SpaceGroup(string script, string cell, LabRunVariant? variant = null) =>
            (JsonObject)LabTestSupport.Runner.Run(LabTestSupport.Script(script), cell, variant).Groups["space"];

        private static List<double> Numbers(JsonObject group, string metric) =>
            ((JsonArray)group[metric]).Select(v => ((JsonNumber)v).Value).ToList();

        private static double Number(JsonObject group, string metric) => ((JsonNumber)group[metric]).Value;

        /// <summary>解析式抛体：从地面以顶点高度 <paramref name="apex"/> 抛起、重力 <paramref name="gravity"/>，高度回到 0 所需的积分步数。</summary>
        private static int FlightSteps(double gravity, double apex)
        {
            var v0 = Math.Sqrt(2.0 * gravity * apex);
            return (int)Math.Ceiling(2.0 * v0 / gravity / Dt - 1e-9);
        }

        /// <summary>步长采样下顶点高度的最大亏欠：离散步最靠近真顶点的采样点至多差半步（g·(dt/2)²/2）。</summary>
        private static double ApexSamplingSlack(double gravity) => gravity * Dt * Dt / 8.0 + 1e-6;

        private static JsonElement FixtureRow(string relativePath, string id)
        {
            var text = File.ReadAllText(Path.Combine(LabTestSupport.FixturesDir, "data", "space", relativePath));
            var doc = JsonDocument.Parse(text);
            return doc.RootElement.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        }

        private static double ShapeHeight(string chainId) =>
            FixtureRow("target/target.chain_def.json", chainId).GetProperty("shape").GetProperty("height").GetDouble();

        // ---------- 格子与脚本适用性 ----------

        [Fact]
        public void SpaceScripts_ApplyToTenCells_OthersKeepSix_AndAllTenAreRunnable()
        {
            var runner = LabTestSupport.Runner;
            var spaceScripts = LabTestSupport.SpaceScripts();
            Assert.Equal(20, spaceScripts.Count); // 六个空间语义脚本（M3-E1）+ 六个空间扩展脚本（ADR-0130 追加决定）+ 八个空中战斗脚本（M4-W1b）
            foreach (var script in spaceScripts)
            {
                var cells = runner.ApplicableCells(script);
                Assert.Equal(10, cells.Count);
                Assert.All(cells, c => Assert.True(c.CheckRunnable(LabHost.AvailableCapabilities).Runnable, c.Cell));
                Assert.Equal(new[] { "side_2d", "side_2d", "volume", "volume" }, cells.Where(c => c.HasVerticalAxis).Select(c => c.Space).ToArray());
            }

            foreach (var script in LabTestSupport.AllScripts().Where(s => !s.Meta.ScriptId.StartsWith(LabTestSupport.SpaceScriptPrefix, StringComparison.Ordinal)))
            {
                Assert.Equal(6, runner.ApplicableCells(script).Count);
            }
        }

        [Fact]
        public void LegacyScripts_NeverGetSpaceGroup_SoExistingBaselinesAreUntouched()
        {
            foreach (var script in LabTestSupport.AllScripts().Where(s => !s.Meta.ScriptId.StartsWith(LabTestSupport.SpaceScriptPrefix, StringComparison.Ordinal)))
            {
                foreach (var cell in LabTestSupport.AllCells)
                {
                    Assert.Null(LabTestSupport.Runner.Record(script, cell).Space);
                }
            }
        }

        // ---------- 复现度量一：横版/体积的跳跃（竖直轴） ----------

        [Theory]
        [MemberData(nameof(VerticalCells))]
        public void Jump_FlightStepsAndLandingTick_FollowGravityAndJumpHeight(string cell)
        {
            var script = LabTestSupport.Script("space.jump");
            var scenario = Cell(script, cell);
            var gravity = scenario.Gravity!.Value;
            var apex = scenario.JumpHeight!.Value;
            var space = SpaceGroup("space.jump", cell);

            var expectedSteps = FlightSteps(gravity, apex);
            var presses = script.Events.Where(e => e.Action == LabHost.JumpAction && e.Kind == ScriptEventKind.Press).Select(e => e.Tick).ToList();
            Assert.Equal(3, presses.Count);

            // 第 2 次请求发生在第 1 次的飞行中（不允许二段跳）→ 被拒绝；第 3 次发生在落地之后 → 被接受。
            Assert.True(presses[1] < presses[0] + expectedSteps && presses[2] >= presses[0] + expectedSteps);
            Assert.Equal(3, Number(space, "jump_requests"));
            Assert.Equal(2, Number(space, "jumps_started"));
            Assert.Equal(1, Number(space, "jumps_refused"));
            Assert.Equal(new[] { presses[0], presses[2] }, Numbers(space, "jump_start_ticks").Select(d => (int)d).ToArray());

            // 整段飞行步数 = ceil(2·v0/(g·dt))，v0 = sqrt(2·g·H)；落地 tick = 起跳 tick + 步数 - 1。
            Assert.Equal(new double[] { expectedSteps, expectedSteps }, Numbers(space, "jump_flight_steps"));
            Assert.Equal(new double[] { presses[0] + expectedSteps - 1, presses[2] + expectedSteps - 1 }, Numbers(space, "player_landing_ticks"));
            Assert.Equal(2 * (expectedSteps - 1), Number(space, "player_airborne_ticks"));

            // 顶点：不超过设定的跳跃高度，且离它不超过半个采样步的亏欠；出现在 t = v0/g 附近。
            var observed = Number(space, "player_apex");
            Assert.InRange(observed, apex - ApexSamplingSlack(gravity), apex + 1e-9);
            var v0 = Math.Sqrt(2.0 * gravity * apex);
            var apexTick = Number(space, "player_apex_tick");
            Assert.InRange(apexTick, presses[0] + v0 / gravity / Dt - 1 - 1.0, presses[0] + v0 / gravity / Dt - 1 + 1.0);

            // 竖直轴只在跳跃期间离地：重放记录，每个 tick 的高度由解析式给出（与积分实现无关的独立期望）。
            var recording = Record("space.jump", cell);
            Assert.NotNull(recording.Space);
            var heights = recording.Space!.PlayerHeights;
            for (var k = 1; k <= expectedSteps; k++)
            {
                var t = k * Dt;
                var analytic = Math.Max(0.0, v0 * t - 0.5 * gravity * t * t);
                Assert.Equal(analytic, heights[presses[0] + k - 1], 6);
            }
        }

        [Theory]
        [MemberData(nameof(PlanarCells))]
        public void Jump_OnPlaneCells_IsRefusedAndCounted_NoVerticalAxis(string cell)
        {
            var recording = Record("space.jump", cell);
            Assert.NotNull(recording.Space);
            Assert.False(recording.Space!.VerticalAxis);
            Assert.Equal("plane", recording.Space.Model);
            Assert.Equal(3, recording.Space.JumpRequests);
            Assert.Equal(0, recording.Space.JumpsStarted);
            Assert.All(recording.Space.PlayerHeights, h => Assert.Equal(0.0, h));

            var space = SpaceGroup("space.jump", cell);
            Assert.Equal(3, Number(space, "jumps_refused"));
            Assert.Equal(0.0, Number(space, "player_apex"));
        }

        [Fact]
        public void Jump_DoesNotEnterInputBuffer_AndIsNotAnInjectedInput()
        {
            // 跳跃是宿主级请求：不进输入缓冲，不污染输入缓冲度量与注入输入清单。
            var recording = Record("space.jump", "side_2d_action");
            Assert.DoesNotContain(recording.InjectedInputs, e => e.Action == LabHost.JumpAction);
        }

        // ---------- 复现度量二：体积空间的命中形状高度窗口 ----------

        [Theory]
        [MemberData(nameof(VerticalCells))]
        public void HitWindow_TargetsOutsideShapeHeight_AreNotHit(string cell)
        {
            var window = ShapeHeight("target.chain.lab_sp_arc");
            var recording = Record("space.hit_window", cell);
            var space = recording.Space!;
            var swings = recording.Events.Where(e => e.Kind == "damage" && e.Source == "player").GroupBy(e => e.Instance).OrderBy(g => g.First().Tick).ToList();
            Assert.Equal(2, swings.Count);

            var candidates = space.DummyHeights.Keys.ToList();
            Assert.Equal(new[] { "ground", "high" }, candidates);
            var excludedSomewhere = false;
            foreach (var swing in swings)
            {
                var tick = swing.First().Tick;
                var at = tick - 1;
                var playerHeight = space.PlayerHeights[at];
                var expected = candidates.Where(c => Math.Abs(space.DummyHeights[c][at] - playerHeight) <= window).OrderBy(c => c, StringComparer.Ordinal).ToList();
                var actual = swing.Select(e => e.Target).OrderBy(c => c, StringComparer.Ordinal).ToList();
                Assert.Equal(expected, actual);
                excludedSomewhere |= expected.Count < candidates.Count;
            }

            // 用例不空转：两个靶子平面位置都在锥形内，高度窗口确实挡掉了其中一个。
            Assert.True(excludedSomewhere);
            Assert.All(Numbers(SpaceGroup("space.hit_window", cell), "hit_height_gaps"), g => Assert.True(g <= window + 1e-9));
        }

        [Theory]
        [MemberData(nameof(PlanarCells))]
        public void HitWindow_OnPlaneCells_IgnoresHeight_BothTargetsHitEverySwing(string cell)
        {
            var recording = Record("space.hit_window", cell);
            var swings = recording.Events.Where(e => e.Kind == "damage" && e.Source == "player").GroupBy(e => e.Instance).ToList();
            Assert.Equal(2, swings.Count);
            Assert.All(swings, s => Assert.Equal(new[] { "ground", "high" }, s.Select(e => e.Target).OrderBy(t => t, StringComparer.Ordinal).ToArray()));
            // 靶子声明了高度（high=2）但平面世界忽略它。
            Assert.Equal(new[] { new KeyValuePair<string, double>("high", 2.0) }, recording.Space!.DeclaredDummyHeights.ToArray());
            Assert.All(recording.Space.DummyHeights["high"], h => Assert.Equal(0.0, h));
        }

        // ---------- 复现度量三：击飞（受击裁决的竖直分量） ----------

        [Theory]
        [MemberData(nameof(VerticalCells))]
        public void Launch_ApexAndLandingTick_FollowLaunchHeightGravityAndImpactMultiplier(string cell)
        {
            var script = LabTestSupport.Script("space.launch");
            var scenario = Cell(script, cell);
            var gravity = scenario.Gravity!.Value;

            var launchHeight = FixtureRow("feel/feel.preset.json", "feel.preset.space_launch").GetProperty("values").GetProperty("launch_height").GetDouble();
            var referenceHeight = FixtureRow("feel/feel.calibration.json", "feel.calibration.lab_space_launch").GetProperty("reference_height").GetDouble();
            var impact = FixtureRow("feel/feel.preset.json", "feel.preset.space_launch").GetProperty("values").GetProperty("impact_class").GetString()!;
            var multiplier = new HitFeelOptions().KnockbackImpactMultipliers[impact];
            // 击飞顶点（世界单位）= launch_height（体型倍数 × 参考身高）× (1 − 击退抗性) × 冲击等级倍率；木桩没有抗性。
            var expectedApex = launchHeight * referenceHeight * multiplier;

            var recording = Record("space.launch", cell);
            var heights = recording.Space!.DummyHeights["stake"];
            var first = heights.FindIndex(h => h > 0.0);
            Assert.True(first > 0, "木桩被击飞前一直在地面");

            // 顿帧结束后才抛起：起飞 tick 晚于命中 tick。
            var hitTick = recording.Events.Single(e => e.Kind == "damage" && e.Source == "player").Tick;
            Assert.True(first > hitTick);

            var steps = FlightSteps(gravity, expectedApex);
            var landing = first + steps - 1;
            Assert.Equal(0.0, heights[landing]);
            Assert.True(heights[landing - 1] > 0.0);
            Assert.InRange(heights.Max(), expectedApex - ApexSamplingSlack(gravity), expectedApex + 1e-9);

            var space = SpaceGroup("space.launch", cell);
            Assert.Equal($"stake={landing};", ((JsonString)space["dummy_landing_ticks"]).Value);
            Assert.Equal($"stake={steps - 1};", ((JsonString)space["dummy_off_ground_ticks"]).Value);
        }

        [Theory]
        [MemberData(nameof(PlanarCells))]
        public void Launch_OnPlaneCells_HasNoVerticalAxis_TargetIsOnlyKnockedBack(string cell)
        {
            var recording = Record("space.launch", cell);
            // 没有竖直轴、没有跳跃事件、靶子没声明高度：不产生空间记录（指纹没有 space 组）。
            Assert.Null(recording.Space);
            Assert.Equal(1, recording.Events.Count(e => e.Kind == "damage"));
        }

        // ---------- 复现度量四：体积空间的三维距离 ----------

        [Theory]
        [MemberData(nameof(VerticalCells))]
        [InlineData("2d_action")]
        [InlineData("3d_targeted")]
        public void Pick_NearestUsesSpatialDistanceOnlyInVolume(string cell)
        {
            var script = LabTestSupport.Script("space.pick_3d");
            var scenario = Cell(script, cell);
            var recording = Record("space.pick_3d", cell);
            var damage = recording.Events.Single(e => e.Kind == "damage" && e.Source == "player");
            var at = damage.Tick - 1;
            var spatial = scenario.Space == "volume";

            // 期望：按记录里的出生位置与高度自己算两个靶子到玩家的距离（体积空间含高度差，其余平面距离），取最小者。
            var player = recording.StartPosition;
            double Distance(string label)
            {
                var pos = recording.Dummies.Single(d => d.Key == label).Value;
                var planar = Math.Sqrt((pos.X - player.X) * (pos.X - player.X) + (pos.Y - player.Y) * (pos.Y - player.Y));
                if (!spatial)
                {
                    return planar;
                }

                var dh = recording.Space!.DummyHeights[label][at] - recording.Space.PlayerHeights[at];
                return Math.Sqrt(planar * planar + dh * dh);
            }

            var expected = new[] { "near_high", "far_ground" }.OrderBy(Distance).First();
            Assert.Equal(expected, damage.Target);
            Assert.Equal(spatial ? "far_ground" : "near_high", damage.Target);
            Assert.Equal(1, recording.Events.Count(e => e.Kind == "damage"));
        }

        // ---------- 复现度量五：横版二维的深度锁 ----------

        [Fact]
        public void DepthLock_Side2d_DropsVerticalAxisComponent_PlaneAndVolumeKeepIt()
        {
            var script = LabTestSupport.Script("space.depth_lock");
            var withY = script.Events.Count(e => e.Kind == ScriptEventKind.Axis && Math.Abs(e.Value.Y) > 0.0);
            Assert.True(withY >= 2);

            foreach (var cell in new[] { "side_2d_targeted", "side_2d_action" })
            {
                var recording = Record("space.depth_lock", cell);
                Assert.Equal(withY, recording.Space!.DepthInputsDropped);
                Assert.All(recording.Ticks, t => Assert.Equal(recording.StartPosition.Y, t.Position.Y));
                var space = SpaceGroup("space.depth_lock", cell);
                Assert.Equal(withY, Number(space, "depth_inputs_dropped"));
                Assert.Equal(0.0, Number(space, "max_depth_drift"));
            }

            foreach (var cell in new[] { "volume_targeted", "volume_action", "2d_action", "3d_targeted" })
            {
                var recording = Record("space.depth_lock", cell);
                // 期望：深度偏移 = 玩家世界 y 相对出生点的最大偏移，取自逐 tick 位置（独立于度量实现）。
                var drift = recording.Ticks.Max(t => Math.Abs(t.Position.Y - recording.StartPosition.Y));
                Assert.True(drift > 0.0);
                Assert.Equal(0, recording.Space?.DepthInputsDropped ?? 0);
                if (recording.Space != null)
                {
                    Assert.Equal(drift, Number(SpaceGroup("space.depth_lock", cell), "max_depth_drift"), 6);
                }
            }
        }

        // ---------- 跨格子不变量：逻辑组差异只来自空间语义 ----------

        [Theory]
        [MemberData(nameof(VerticalCells))]
        public void SpaceInvariant_AsPlane_LogicEqualsPlaneCell_ForEverySpaceScript(string cell)
        {
            var runner = LabTestSupport.Runner;
            foreach (var script in LabTestSupport.SpaceScripts())
            {
                var planar = runner.Run(script, "2d_" + Cell(script, cell).Settlement);
                var asPlane = runner.Run(script, cell, new LabRunVariant { SpaceOverride = "plane" });
                Assert.Empty(LabInvariants.DiffLogic(runner.Registry, planar, asPlane, null));
            }
        }

        [Fact]
        public void SpaceInvariant_IsNotVacuous_SpaceCellsDifferOnlyByTheSemanticsTheyExercise()
        {
            var runner = LabTestSupport.Runner;

            // 跳跃：差异只在 space 组（战斗结局不变）。
            var jump = LabTestSupport.Script("space.jump");
            var jumpDiff = LabInvariants.DiffLogic(runner.Registry, runner.Run(jump, "2d_action"), runner.Run(jump, "side_2d_action"), null);
            Assert.NotEmpty(jumpDiff);
            Assert.All(jumpDiff, d => Assert.StartsWith("space.", d));

            // 命中高度窗口：命中数与结算变了，所以 space 之外的组也有差异——这正是"空间语义"本身。
            var hit = LabTestSupport.Script("space.hit_window");
            var hitDiff = LabInvariants.DiffLogic(runner.Registry, runner.Run(hit, "2d_action"), runner.Run(hit, "side_2d_action"), null);
            Assert.Contains(hitDiff, d => !d.StartsWith("space.", StringComparison.Ordinal));

            // 无空间刺激的对照脚本：除 space 组外逻辑组逐字节一致（空间格子不改变任何不涉及空间语义的行为）。
            var neutral = LabTestSupport.Script("space.neutral");
            foreach (var cell in new[] { "side_2d_targeted", "side_2d_action", "volume_targeted", "volume_action" })
            {
                var planar = runner.Run(neutral, "2d_" + Cell(neutral, cell).Settlement);
                var actual = runner.Run(neutral, cell);
                Assert.False(LabInvariants.SpaceWasExercised(actual));
                Assert.Empty(LabInvariants.DiffLogic(runner.Registry, planar, actual, null, "space"));
            }

            var results = LabInvariants.Check(runner, LabTestSupport.SpaceScripts());
            Assert.All(results, r => Assert.True(r.Ok, r.ToString()));
            // 全部空间脚本 × 4 个竖直格子各一次"按平面运行"；无刺激对照脚本再各一次"除 space 组外一致"。
            Assert.Equal(LabTestSupport.SpaceScripts().Count * 4 + 4, results.Count(r => r.Invariant == LabInvariants.SpaceSemanticsOnly));
        }

        [Fact]
        public void SpaceBaselines_ArePresentForAllTenCells_AndMatch()
        {
            var results = new List<CellResult>();
            foreach (var script in LabTestSupport.SpaceScripts())
            {
                results.AddRange(LabSuite.Check(LabTestSupport.Runner, LabTestSupport.FixturesDir, script.Meta.ScriptId));
            }

            Assert.Equal(LabTestSupport.SpaceScripts().Count * 10, results.Count);
            Assert.All(results, r => Assert.True(r.Status == CellStatus.Pass, r.Script + " @ " + r.Cell + " " + r.Message));
        }
    }
}
