using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.SceneRouter;
using Core.Foundation.Common.Json;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 空间扩展（ADR-0130 追加决定：竖直轴能力包补完）的运行期验收：六个 <c>space.*</c> 扩展脚本（空中控制与多段跳、地形、空中受击与空中姿势、
    /// 击飞叠加、命中形状竖直偏移、三维施法射程）在竖直格子上的复现度量。期望值全部由规则算出（空中控制比例、地形数据行、预设与目标链行、
    /// 姿势键表与固定回落链、抛体公式），不写死裸数。数据在 <c>lab/fixtures/data/space_ext/</c>。
    /// </summary>
    public sealed class SpaceExtTests
    {
        private const double Dt = 1.0 / 60.0;
        private static readonly string[] VerticalCells = { "side_2d_targeted", "side_2d_action", "volume_targeted", "volume_action" };
        private static readonly string[] VerticalTargeted = { "side_2d_targeted", "volume_targeted" };

        public static IEnumerable<object[]> VerticalCellData() => VerticalCells.Select(c => new object[] { c });

        public static IEnumerable<object[]> VerticalTargetedData() => VerticalTargeted.Select(c => new object[] { c });

        public static IEnumerable<object[]> PlaneCellData() => LabTestSupport.AllCells.Select(c => new object[] { c });

        // ---------- 辅助 ----------

        private static LabRecording Record(string script, string cell, LabRunVariant? variant = null) =>
            LabTestSupport.Runner.Record(LabTestSupport.Script(script), cell, variant);

        private static JsonObject Group(string script, string cell, string group, LabRunVariant? variant = null) =>
            (JsonObject)LabTestSupport.Runner.Run(LabTestSupport.Script(script), cell, variant).Groups[group];

        private static double Number(JsonObject group, string metric) => ((JsonNumber)group[metric]).Value;

        private static string Text(JsonObject group, string metric) => ((JsonString)group[metric]).Value;

        private static JsonElement FixtureRow(string relativePath, string id)
        {
            var text = File.ReadAllText(Path.Combine(LabTestSupport.FixturesDir, "data", "space_ext", relativePath));
            var doc = JsonDocument.Parse(text);
            return doc.RootElement.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        }

        private static List<TerrainRegion> TerrainRegions(string mapId)
        {
            var row = FixtureRow("world/world.map.json", mapId);
            var regions = new List<TerrainRegion>();
            foreach (var item in row.GetProperty("terrain").EnumerateArray())
            {
                Vec2 V(JsonElement e) => new Vec2(e.GetProperty("x").GetDouble(), e.GetProperty("y").GetDouble());
                var slope = item.TryGetProperty("slope", out var sl) ? V(sl) : Vec2.Zero;
                regions.Add(new TerrainRegion(
                    V(item.GetProperty("min")), V(item.GetProperty("max")),
                    item.TryGetProperty("ground", out var g) ? g.GetDouble() : 0.0, slope.X, slope.Y,
                    item.TryGetProperty("ceiling", out var c) ? c.GetDouble() : double.PositiveInfinity));
            }

            return regions;
        }

        private static double GroundAt(List<TerrainRegion> regions, Vec2 p)
        {
            for (var i = regions.Count - 1; i >= 0; i--)
            {
                if (regions[i].Contains(p)) return regions[i].GroundAt(p);
            }

            return 0.0;
        }

        private static double CeilingAt(List<TerrainRegion> regions, Vec2 p)
        {
            for (var i = regions.Count - 1; i >= 0; i--)
            {
                if (regions[i].Contains(p)) return regions[i].Ceiling;
            }

            return double.PositiveInfinity;
        }

        private static double PresetValue(string presetId, string field)
        {
            var values = FixtureRow("feel/feel.preset.json", presetId).GetProperty("values");
            return values.GetProperty(field).GetDouble();
        }

        private static string PresetText(string presetId, string field) =>
            FixtureRow("feel/feel.preset.json", presetId).GetProperty("values").GetProperty(field).GetString()!;

        // ---------- 适用性与既有数据不受影响 ----------

        [Fact]
        public void ExtScripts_DeclareSpaceExt_AndOnlyVerticalCellsGetTheExtGroup()
        {
            var ids = new[] { "space.air_control", "space.terrain", "space.air_hit", "space.launch_stack", "space.height_offset", "space.range_3d" };
            foreach (var id in ids)
            {
                var script = LabTestSupport.Script(id);
                Assert.NotNull(script.Meta.SpaceExt);
                foreach (var cell in LabTestSupport.AllCells)
                {
                    Assert.Null(Record(id, cell).Space?.Ext);
                }

                foreach (var cell in VerticalCells)
                {
                    Assert.NotNull(Record(id, cell).Space?.Ext);
                }
            }
        }

        [Fact]
        public void ExistingSpaceScripts_NeverGetTheExtGroup_SoTheirBaselinesAreUntouched()
        {
            foreach (var id in new[] { "space.jump", "space.hit_window", "space.launch", "space.neutral", "space.depth_lock", "space.pick_3d" })
            {
                foreach (var cell in VerticalCells)
                {
                    Assert.Null(Record(id, cell).Space?.Ext);
                    Assert.False(LabTestSupport.Runner.Run(LabTestSupport.Script(id), cell).Groups.ContainsKey("space_ext"));
                }
            }
        }

        // ---------- 一、空中控制与多段跳 ----------

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void AirControl_AirStepsAreTheAirControlRatioOfTheGroundSteps(string cell)
        {
            var script = LabTestSupport.Script("space.air_control");
            var ratio = script.Meta.SpaceExt!.AirControl!.Value;
            var ext = Group("space.air_control", cell, "space_ext");
            Assert.Equal(ratio, Number(ext, "air_ground_ratio"), 6);
            Assert.True(Number(ext, "air_step_mean") > 0.0);
        }

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void AirJumps_AreCappedByMaxAirJumps_AndTheNextRequestIsRefused(string cell)
        {
            var maxAirJumps = LabTestSupport.Script("space.air_control").Meta.SpaceExt!.MaxAirJumps!.Value;
            var record = Record("space.air_control", cell);
            var ext = record.Space!.Ext!;
            // 脚本在空中依次请求 3 次起跳里的后两次（第 55、65 tick）：第一次空中跳（预算 1）被接受，下一次被拒绝。
            Assert.Equal(maxAirJumps, ext.AirJumpStarts);
            Assert.Equal(1, ext.AirJumpRefusals);
            Assert.Equal(1 + maxAirJumps, record.Space.JumpsStarted);
            Assert.Equal(3, record.Space.JumpRequests);
        }

        [Fact]
        public void AirControl_DefaultIsUnrestricted_ExistingJumpScriptKeepsFullAirMovement()
        {
            // 没有声明 spaceExt 的脚本：空中水平位移不受限（1.95.0 行为），也没有扩展记录可查——用同一脚本去掉选项对照。
            var script = LabTestSupport.Script("space.air_control");
            var clone = InputScript.Parse(script.ToJson());
            clone.Meta.SpaceExt!.AirControl = null;
            clone.Meta.SpaceExt.MaxAirJumps = null;
            var fingerprint = LabTestSupport.Runner.Run(clone, "side_2d_targeted");
            var ext = (JsonObject)fingerprint.Groups["space_ext"];
            Assert.Equal(1.0, Number(ext, "air_ground_ratio"), 6);
            Assert.Equal(0, Number(ext, "air_jump_starts"));
            Assert.Equal(2, Number(ext, "air_jump_refusals")); // 第 55、65 tick 的空中跳（不允许空中跳）都被拒绝
        }

        [Theory]
        [MemberData(nameof(PlaneCellData))]
        public void AirControl_PlaneCellsIgnoreTheOptionsAndRefuseEveryJump(string cell)
        {
            var record = Record("space.air_control", cell);
            Assert.Equal(0, record.Space!.JumpsStarted);
            Assert.Equal(3, record.Space.JumpRequests);
        }

        // ---------- 二、地形 ----------

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void Terrain_LandsOnTheGroundHeightOfTheLandingPoint_NotOnZero(string cell)
        {
            var regions = TerrainRegions("world.lab_terrain");
            var record = Record("space.terrain", cell);
            var ext = record.Space!.Ext!;
            var landed = -1;
            for (var t = 1; t < ext.PlayerAirborne.Count; t++)
            {
                if (ext.PlayerAirborne[t - 1] && !ext.PlayerAirborne[t])
                {
                    landed = t;
                    break;
                }
            }

            Assert.True(landed > 0, "起跳后应当落地");
            var expected = GroundAt(regions, record.Ticks[landed].Position);
            Assert.True(expected > 0.0, "落点在平台/斜坡上，地面高度大于 0");
            Assert.Equal(expected, record.Space.PlayerHeights[landed], 6);
        }

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void Terrain_CeilingZeroesTheRisingSpeed_AndNeverLetsTheFeetPassIt(string cell)
        {
            var regions = TerrainRegions("world.lab_terrain");
            var record = Record("space.terrain", cell);
            var ext = record.Space!.Ext!;
            var clamps = new List<int>();
            for (var t = 1; t < ext.PlayerAirborne.Count; t++)
            {
                var ceiling = CeilingAt(regions, record.Ticks[t].Position);
                if (ext.PlayerAirborne[t])
                {
                    Assert.True(record.Space.PlayerHeights[t] <= ceiling + 1e-9, $"tick {t}");
                    if (ext.PlayerVerticalSpeeds[t - 1] > 0.0 && ext.PlayerVerticalSpeeds[t] == 0.0)
                    {
                        clamps.Add(t);
                        Assert.Equal(ceiling, record.Space.PlayerHeights[t], 6);
                    }
                }
            }

            Assert.NotEmpty(clamps);
        }

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void Terrain_StepHigherThanStepHeightBlocksWalking_LowerStepsDoNot(string cell)
        {
            var regions = TerrainRegions("world.lab_terrain");
            var stepHeight = LabTestSupport.Script("space.terrain").Meta.SpaceExt!.StepHeight!.Value;
            var record = Record("space.terrain", cell);

            // 数据里的台阶：0 -> 0.4（x=3，低于台阶高度，能走上去）；斜坡顶 1.15 -> 3（x=9，高于台阶高度，被顶住）。
            var lowStep = GroundAt(regions, new Vec2(3.0, 0)) - GroundAt(regions, new Vec2(2.99, 0));
            var cliff = GroundAt(regions, new Vec2(9.01, 0)) - GroundAt(regions, new Vec2(8.99, 0));
            Assert.True(lowStep > 0 && lowStep <= stepHeight);
            Assert.True(cliff > stepHeight);

            var finalX = record.Ticks[record.Ticks.Count - 1].Position.X;
            Assert.True(finalX <= 9.0 + 1e-9, "不得越过悬崖脚");
            Assert.True(finalX > 8.9, "走到悬崖脚才停");
        }

        [Theory]
        [MemberData(nameof(PlaneCellData))]
        public void Terrain_PlaneCellsIgnoreTerrainEntirely(string cell)
        {
            var record = Record("space.terrain", cell);
            Assert.Null(record.Space?.Ext);
            Assert.All(record.Space!.PlayerHeights, h => Assert.Equal(0.0, h));
            Assert.True(record.Ticks[record.Ticks.Count - 1].Position.X > 9.0, "没有竖直轴：悬崖不存在，玩家一路走过去");
        }

        // ---------- 三、空中受击反应与空中姿势 ----------

        [Theory]
        [MemberData(nameof(VerticalTargetedData))]
        public void AirHit_SecondHitOnTheAirborneTargetUsesTheAirHitReaction(string cell)
        {
            var airReaction = PresetText("feel.preset.space_air_hit", "air_hit_reaction");
            var reactions = Text(Group("space.air_hit", cell, "reaction"), "reactions").Split(';', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, reactions.Length);
            Assert.Equal("Knockback", reactions[0].Split(':')[2]); // 第一下：目标在地面，普通击退（击飞）
            Assert.Equal(airReaction, reactions[1].Split(':')[2], ignoreCase: true); // 第二下：目标在空中，反应被替换
        }

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void AirPoses_FoundKeysResolveDirectly_MissingKeysFallBackAlongTheFixedChains(string cell)
        {
            var script = LabTestSupport.Script("space.air_hit");
            var keys = new HashSet<string>(script.Meta.SpaceExt!.PoseKeys, StringComparer.Ordinal);
            var record = Record("space.air_hit", cell);
            var poses = record.Space!.Ext!.AirPoses;
            Assert.NotEmpty(poses);

            foreach (var pose in poses)
            {
                // 期望：链上第一个在键表里的键；链末端（idle/hit/attack）恒作为兜底。
                AirPoseRequest request = pose.Kind switch
                {
                    "hit" => AirPoseRequest.HitAir(),
                    "attack" => AirPoseRequest.AttackAir(script.Meta.SpaceExt.PoseFamily),
                    _ => AirPoseRequest.Jump(pose.Requested.Substring("jump.".Length)),
                };
                var chain = request.Chain();
                Assert.Equal(chain[0], pose.Requested);
                var expectedDepth = 0;
                while (expectedDepth < chain.Count - 1 && !keys.Contains(chain[expectedDepth]))
                {
                    expectedDepth++;
                }

                Assert.Equal(chain[expectedDepth], pose.Resolved);
                Assert.Equal(expectedDepth, pose.Depth);
            }

            // 两类情形都被演练到：键直接取到（深度 0）与键缺失回落（深度 > 0）。
            Assert.Contains(poses, p => p.Depth == 0);
            Assert.Contains(poses, p => p.Depth > 0);
            // 每个空中阶段都出现过：腾空上升、下降、落地。
            Assert.Contains(poses, p => p.Kind == "phase" && p.Requested == "jump.rise");
            Assert.Contains(poses, p => p.Kind == "phase" && p.Requested == "jump.fall");
            Assert.Contains(poses, p => p.Kind == "phase" && p.Requested == "jump.land");
        }

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void AirPoses_AHitOnAGroundedTargetStaysAPlainHit_AndTheAirPhasesFollowTheVerticalMotion(string cell)
        {
            var record = Record("space.air_hit", cell);
            var ext = record.Space!.Ext!;
            var firstDamage = record.Events.First(e => e.Kind == "damage" && e.Source == "player");
            // 第一下打中时木桩在地面：没有 hit.air 请求（空中姿势只在空中请求，地面保持改动前的解析）。
            Assert.DoesNotContain(ext.AirPoses, p => p.Kind == "hit" && p.Tick <= firstDamage.Tick);
            Assert.Contains(ext.AirPoses, p => p.Kind == "hit");

            // 阶段区间：上升在速度向上的步，下降在向下的步，落地保持窗口之后清空。
            for (var t = 0; t < ext.PlayerAirPhases.Count; t++)
            {
                var phase = ext.PlayerAirPhases[t];
                if (phase == "rise") Assert.True(ext.PlayerAirborne[t] && ext.PlayerVerticalSpeeds[t] > 0.0, $"rise @ {t}");
                if (phase == "fall") Assert.True(ext.PlayerAirborne[t] && ext.PlayerVerticalSpeeds[t] <= 0.0, $"fall @ {t}");
                if (phase == "land") Assert.False(ext.PlayerAirborne[t], $"land @ {t}");
            }

            var landRun = ext.PlayerAirPhases.Count(p => p == "land");
            Assert.Equal(Presentation.Render.AirPoseFeeder.DefaultLandHoldTicks, landRun);
        }

        [Theory]
        [MemberData(nameof(PlaneCellData))]
        public void AirHit_PlaneCellsHaveNoAirPosesAndEveryHitIsPlain(string cell)
        {
            var record = Record("space.air_hit", cell);
            Assert.Null(record.Space?.Ext);
            var reactions = Text(Group("space.air_hit", cell, "reaction"), "reaction_counts");
            Assert.DoesNotContain("Flinch", reactions);
        }

        // ---------- 四、击飞叠加 ----------

        [Theory]
        [MemberData(nameof(VerticalTargetedData))]
        public void LaunchStack_AddRaisesTheSecondLaunchByTheCap_RestartRelaunchesFromTheCurrentHeight(string cell)
        {
            var launchHeight = PresetValue("feel.preset.space_launch_stack", "launch_height");
            var cap = PresetValue("feel.preset.space_launch_stack", "launch_stack_cap");
            var script = LabTestSupport.Script("space.launch_stack");
            var stack = Record("space.launch_stack", cell);
            var restart = Record("space.launch_stack", cell, new LabRunVariant { PresetId = "feel.preset.space_launch_restart" });

            var hits = stack.Events.Where(e => e.Kind == "damage" && e.Source == "player").Select(e => e.Tick).ToList();
            Assert.Equal(2, hits.Count);
            var second = hits[1];
            var heights = stack.Space!.DummyHeights["stake"];
            var restartHeights = restart.Space!.DummyHeights["stake"];
            Assert.Equal(heights.Take(second).ToList(), restartHeights.Take(second).ToList()); // 第二下命中之前两种模式一模一样

            // 击飞在命中之后若干 tick（顿帧）才真正起算：起算那一步的特征是脚下高度增量不降反升（重力下增量单调递减，新初速让它跳高）。
            var launch = second;
            while (launch < heights.Count && !(heights[launch] - heights[launch - 1] > heights[launch - 1] - heights[launch - 2] + 1e-9))
            {
                launch++;
            }

            Assert.True(launch < heights.Count, "第二下应当在空中重新起算击飞");
            var heightAtSecondHit = heights[launch - 1];
            var slack = 0.15;
            var apexStack = heights.Skip(launch).Max();
            var apexRestart = restartHeights.Skip(launch).Max();

            // 重启：以新初速重新计算——从当前高度再抬 launch_height；叠加：当前上升速度 + 初速，被上限 cap 封住——从当前高度再抬 cap。
            Assert.Equal(heightAtSecondHit + launchHeight, apexRestart, slack);
            Assert.Equal(heightAtSecondHit + cap, apexStack, slack);
            Assert.True(apexStack > apexRestart);
            Assert.True(script.Meta.PresetId.Length > 0);
        }

        [Fact]
        public void LaunchStack_RestartIsTheDefault_ExistingLaunchScriptStillRelaunchesFromTheNewVelocity()
        {
            // 既有击飞预设（space_launch，没有声明 launch_stack）等价于 restart：同一脚本换成显式 restart 的预设，木桩的竖直曲线逐步相同。
            var restartRecord = Record("space.launch_stack", "side_2d_targeted", new LabRunVariant { PresetId = "feel.preset.space_launch_restart" });
            Assert.Equal("restart", PresetText("feel.preset.space_launch_restart", "launch_stack"));
            Assert.True(restartRecord.Space!.DummyHeights["stake"].Max() > 0.0);
        }

        // ---------- 五、命中形状竖直偏移 ----------

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void HeightOffset_TheWindowIsShiftedByTheOffset_OnlyTheTargetAtThatHeightIsHit(string cell)
        {
            var chain = FixtureRow("target/target.chain_def.json", "target.chain.lab_spx_arc_up").GetProperty("shape");
            var offset = chain.GetProperty("height_offset").GetDouble();
            var height = chain.GetProperty("height").GetDouble();
            var record = Record("space.height_offset", cell);
            var declared = record.Space!.DeclaredDummyHeights.ToDictionary(p => p.Key, p => p.Value);
            var hit = record.Events.Where(e => e.Kind == "damage" && e.Source == "player").Select(e => e.Target).Distinct().ToList();

            // 玩家站在地面（脚下高度 0）：窗口是 [offset, offset + height]；只有高度落在窗口内的靶子被打中。
            double HeightOf(string name) => declared.TryGetValue(name, out var h) ? h : 0.0;
            var expected = new[] { "ground", "high" }
                .Where(name => HeightOf(name) >= offset - 1e-9 && HeightOf(name) <= offset + height + 1e-9)
                .ToList();
            Assert.Equal(expected, hit);
            Assert.NotEmpty(hit);
        }

        [Theory]
        [MemberData(nameof(PlaneCellData))]
        public void HeightOffset_PlaneCellsIgnoreTheWindow_EveryTargetInTheConeIsHit(string cell)
        {
            var record = Record("space.height_offset", cell);
            var hit = record.Events.Where(e => e.Kind == "damage" && e.Source == "player").Select(e => e.Target).Distinct().OrderBy(x => x).ToList();
            Assert.Equal(new[] { "ground", "high" }, hit);
        }

        // ---------- 六、三维施法射程 ----------

        [Theory]
        [MemberData(nameof(VerticalTargetedData))]
        public void Range3D_TheFarCastFailsOnTheSpatialDistance_TheCastAfterWalkingForwardSucceeds(string cell)
        {
            var range = FixtureRow("skill/skill.def.json", "skill.lab_spx_pick").GetProperty("range").GetDouble();
            var dummy = FixtureRow("lab/lab.dummy_set.json", "lab.dummy_set.lab_space_ext").GetProperty("entries").EnumerateArray()
                .Single(e => e.GetProperty("name").GetString() == "far_high");
            var dx = dummy.GetProperty("position").GetProperty("x").GetDouble();
            var dz = dummy.GetProperty("height").GetDouble();
            var record = Record("space.range_3d", cell);

            var failed = record.Events.Where(e => e.Kind == "cast_failed").ToList();
            var succeeded = record.Events.Where(e => e.Kind == "cast_success").ToList();
            Assert.Single(failed);
            Assert.Equal("OutOfRange", failed[0].Detail);
            // 第一次施法：平面距离在射程内，三维距离超出——这正是失败的原因。
            Assert.True(dx <= range);
            Assert.True(Math.Sqrt(dx * dx + dz * dz) > range);

            Assert.Single(succeeded);
            var playerX = record.Ticks[succeeded[0].Tick].Position.X;
            var spatial = Math.Sqrt((dx - playerX) * (dx - playerX) + dz * dz);
            Assert.True(spatial <= range, $"走到 x={playerX} 后三维距离 {spatial} 应在射程 {range} 内");
        }

        [Theory]
        [MemberData(nameof(PlaneCellData))]
        public void Range3D_PlaneCellsKeepThePlanarRange_BothCastsSucceed(string cell)
        {
            var record = Record("space.range_3d", cell);
            Assert.Empty(record.Events.Where(e => e.Kind == "cast_failed"));
            Assert.Equal(2, record.Events.Count(e => e.Kind == "cast_success"));
        }

        [Fact]
        public void Range3D_ActionCellsSettleByTheHitWindowGeometry_SoTheyDoNotGateOnRange()
        {
            // 判断记录：动作式结算（带 timeline 的技能）由命中窗口的几何决定命中，施法阶段没有"目标已解析"这一步，不做射程判定——
            // 三维射程只影响目标选择式（技能管线步骤 7）。两个竖直动作式格子的两次施法都成功，与平面格子一致。
            foreach (var cell in new[] { "side_2d_action", "volume_action" })
            {
                var record = Record("space.range_3d", cell);
                Assert.Empty(record.Events.Where(e => e.Kind == "cast_failed"));
            }
        }
    }
}
