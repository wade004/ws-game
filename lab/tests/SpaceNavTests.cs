using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SceneRouter;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// M4-W1a（ADR-0130 追加决定"寻路感知台阶 / 地形形状 / 台阶判定精确化"）的实验室验收：九个地形与导航脚本在四个竖直格子上的运行期复现。
    /// 期望值全部由规则算出（地形数据行、抛体公式、双线性插值与平面方程、到达容差），不写死裸数。数据在 <c>lab/fixtures/data/space_ext/</c>。
    /// </summary>
    public sealed class SpaceNavTests
    {
        private const double Dt = 1.0 / 60.0;
        private static readonly string[] VerticalCells = { "side_2d_targeted", "side_2d_action", "volume_targeted", "volume_action" };

        public static IEnumerable<object[]> VerticalCellData() => VerticalCells.Select(c => new object[] { c });

        private static LabRecording Record(string script, string cell) =>
            LabTestSupport.Runner.Record(LabTestSupport.Script(script), cell);

        private static JsonObject Group(string script, string cell, string group) =>
            (JsonObject)LabTestSupport.Runner.Run(LabTestSupport.Script(script), cell).Groups[group];

        private static double Number(JsonObject group, string metric) => ((JsonNumber)group[metric]).Value;

        private static JsonElement Row(string id)
        {
            var text = File.ReadAllText(Path.Combine(LabTestSupport.FixturesDir, "data", "space_ext", "world", "world.map.json"));
            return JsonDocument.Parse(text).RootElement.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        }

        /// <summary>格子的重力与跳跃顶点高度（数据行 <c>lab.scenario.*</c>：横版 30/1.5，体积格子另有各自取值）。</summary>
        private static (double Gravity, double JumpHeight) CellVertical(string cell)
        {
            var text = File.ReadAllText(Path.Combine(LabTestSupport.FixturesDir, "data", "space_ext", "lab", "lab.scenario.json"));
            var row = JsonDocument.Parse(text).RootElement.GetProperty("rows").EnumerateArray()
                .Single(r => r.GetProperty("id").GetString() == "lab.scenario." + cell);
            return (row.GetProperty("gravity").GetDouble(), row.GetProperty("jump_height").GetDouble());
        }

        private static Vec2 V(JsonElement e) => new Vec2(e.GetProperty("x").GetDouble(), e.GetProperty("y").GetDouble());

        /// <summary>按地图行的 terrain 数组读出全部形状（矩形/多边形/高度场），声明顺序，供按规则算期望高度。</summary>
        private static List<ITerrainShape> Shapes(string mapId)
        {
            var list = new List<ITerrainShape>();
            foreach (var item in Row(mapId).GetProperty("terrain").EnumerateArray())
            {
                var shape = item.TryGetProperty("shape", out var s) ? s.GetString() : "rect";
                var ceiling = item.TryGetProperty("ceiling", out var c) ? c.GetDouble() : double.PositiveInfinity;
                var ground = item.TryGetProperty("ground", out var g) ? g.GetDouble() : 0.0;
                var slope = item.TryGetProperty("slope", out var sl) ? V(sl) : Vec2.Zero;
                switch (shape)
                {
                    case "rect":
                        list.Add(new TerrainRegion(V(item.GetProperty("min")), V(item.GetProperty("max")), ground, slope.X, slope.Y, ceiling));
                        break;
                    case "polygon":
                        list.Add(new TerrainPolygon(
                            item.GetProperty("points").EnumerateArray().Select(V).ToArray(), ground, slope.X, slope.Y, ceiling,
                            item.TryGetProperty("origin", out var o) ? V(o) : (Vec2?)null));
                        break;
                    default:
                        list.Add(new TerrainHeightField(
                            V(item.GetProperty("min")), item.GetProperty("cell").GetDouble(),
                            item.GetProperty("heights").EnumerateArray().Select(r => r.EnumerateArray().Select(x => x.GetDouble()).ToArray()).ToArray(),
                            ceiling));
                        break;
                }
            }

            return list;
        }

        private static double GroundAt(List<ITerrainShape> shapes, Vec2 p)
        {
            for (var i = shapes.Count - 1; i >= 0; i--)
            {
                if (shapes[i].Contains(p)) return shapes[i].GroundAt(p);
            }

            return 0.0;
        }

        private static IEnumerable<(string Id, Vec2 Min, Vec2 Max, double Height)> Rects(string mapId) =>
            Row(mapId).GetProperty("terrain").EnumerateArray()
                .Where(i => !i.TryGetProperty("shape", out var s) || s.GetString() == "rect")
                .Select(i => (mapId, V(i.GetProperty("min")), V(i.GetProperty("max")), i.GetProperty("ground").GetDouble()));

        // ---------- 限制 1/3：寻路绕台阶（不被截断）----------

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void NavDetour_NeverEntersTheUnclimbableStep_AndArrivesWithoutBeingBlocked(string cell)
        {
            var record = Record("space.nav_detour", cell);
            var platform = Rects("world.lab_nav_steps").Last();
            foreach (var tick in record.Ticks)
            {
                var inside = tick.Position.X > platform.Min.X && tick.Position.X < platform.Max.X &&
                             tick.Position.Y > platform.Min.Y && tick.Position.Y < platform.Max.Y;
                Assert.False(inside, $"tick {tick.Tick} 位置 {tick.Position} 走进了台阶");
            }

            var nav = Group("space.nav_detour", cell, "space_nav");
            Assert.Equal(1.0, Number(nav, "arrived"));
            Assert.Equal(0.0, Number(nav, "terrain_blocked_stops"));
            Assert.True(Number(nav, "max_abs_y") > Math.Abs(platform.Max.Y) - 1e-9);
            // 绕行的路程只比直线长一个"斜绕"的量：不超过直线加两倍（半宽 + 余量）。
            Assert.InRange(Number(nav, "detour_ratio"), 1.0, 1.5);
        }

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void NavDetour_PlaneCellsIgnoreTheTerrain_AndWalkStraight(string cell)
        {
            var planar = "2d_" + (cell.EndsWith("targeted", StringComparison.Ordinal) ? "targeted" : "action");
            var record = Record("space.nav_detour", planar);
            Assert.All(record.Ticks, t => Assert.Equal(0.0, t.Position.Y));
        }

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void NavUnreachable_FailsOnceWithNoPath_ThenTheNextRequestArrives(string cell)
        {
            var nav = Group("space.nav_unreachable", cell, "space_nav");
            Assert.Equal(1.0, Number(nav, "move_failed"));
            Assert.Equal("0:NoPath", ((JsonString)nav["move_failures"]).Value);
            Assert.Equal(1.0, Number(nav, "arrived"));
            var record = Record("space.nav_unreachable", cell);
            // 失败后到第二次请求（tick 30）之前原地不动。
            for (var t = 0; t < 30; t++) Assert.Equal(record.Ticks[0].Position, record.Ticks[t].Position);
        }

        // ---------- 限制 2：路径校验看见地形 ----------

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void NavRevalidate_TerrainSwapMidWalk_ReplansAroundTheNewStep(string cell)
        {
            var record = Record("space.nav_revalidate", cell);
            var swapped = Rects("world.lab_nav_walled").Last();
            foreach (var tick in record.Ticks)
            {
                var inside = tick.Position.X > swapped.Min.X && tick.Position.X < swapped.Max.X &&
                             tick.Position.Y > swapped.Min.Y && tick.Position.Y < swapped.Max.Y;
                Assert.False(inside, $"tick {tick.Tick} 位置 {tick.Position} 走进了热切换出来的台阶");
            }

            var nav = Group("space.nav_revalidate", cell, "space_nav");
            Assert.Equal(1.0, Number(nav, "terrain_swaps"));
            Assert.Equal(1.0, Number(nav, "arrived"));
            Assert.Equal(0.0, Number(nav, "terrain_blocked_stops"));
            Assert.Equal(0.0, Number(nav, "move_failed"));
            // 切换那一刻玩家还在台阶之前：地形热切换发生在 tick 20，那时 x 远小于台阶边缘。
            Assert.True(record.Ticks[20].Position.X < swapped.Min.X);
        }

        // ---------- 限制 4：落地只在下落时发生 ----------

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void RiseLanding_StaysAirborneThroughTheRise_AndLandsAtThePlatformHeightAfterTheApex(string cell)
        {
            var platform = Rects("world.lab_nav_rise").Last();
            var (gravity, jumpHeight) = CellVertical(cell);
            var v0 = Math.Sqrt(2.0 * gravity * jumpHeight);
            // 下落回到平台面的时间：v0·t − g·t²/2 = h，取较大根。
            var t0 = (v0 + Math.Sqrt(v0 * v0 - 2.0 * gravity * platform.Height)) / gravity;
            var expectedTicks = (int)Math.Ceiling(t0 / Dt - 1e-9);

            var ext = Group("space.rise_landing", cell, "space_ext");
            var airborne = (int)Number(ext, "airborne_ticks");
            Assert.InRange(airborne, expectedTicks - 1, expectedTicks + 1);
            Assert.Equal(platform.Height, ((JsonNumber)((JsonArray)ext["landing_heights"])[0]).Value, 6);
        }

        // ---------- 限制 5：走下台地 ----------

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void WalkOff_FallsFromTheLedgeHeightWithoutStepHeight_AndLandsOnTheGround(string cell)
        {
            Assert.Null(LabTestSupport.Script("space.walk_off").Meta.SpaceExt!.StepHeight);
            var ledge = Rects("world.lab_nav_ledge").Last();
            var ext = Group("space.walk_off", cell, "space_ext");
            Assert.Equal(ledge.Height, Number(ext, "airborne_height_max"), 6);
            var expectedTicks = (int)Math.Ceiling(Math.Sqrt(2.0 * ledge.Height / CellVertical(cell).Gravity) / Dt - 1e-9);
            Assert.InRange((int)Number(ext, "airborne_ticks"), expectedTicks - 1, expectedTicks + 1);
            Assert.Equal(0.0, Number(ext, "ground_height_final"));
        }

        // ---------- 限制 6：精确停点 ----------

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void ExactStop_EndsWithinTheArrivalEpsilonOfTheWallEdge_NotAWholeSampleShort(string cell)
        {
            var wall = Rects("world.lab_nav_wall").Last();
            var epsilon = new MovementOptionsProbe().ArrivalEpsilon;
            var ext = Group("space.exact_stop", cell, "space_ext");
            var finalX = Number(ext, "final_x");
            Assert.True(finalX < wall.Min.X, $"不越过墙边缘 {wall.Min.X}，实际 {finalX}");
            Assert.True(finalX >= wall.Min.X - epsilon - 1e-9, $"停点应在边缘前一个到达容差之内（>= {wall.Min.X - epsilon}），实际 {finalX}");
            // 墙边缘 10.03 与默认采样间隔 0.1 不对齐：旧的"最后一个可通行采样点"近似会停在 10.0 一带或更短。
            Assert.True(Math.Abs(wall.Min.X / 0.1 - Math.Round(wall.Min.X / 0.1)) > 0.1);
        }

        private sealed class MovementOptionsProbe
        {
            public double ArrivalEpsilon { get; } = new Core.Carriers.Unit.MovementOptions().ArrivalEpsilon;
        }

        // ---------- 限制 8：高度场、凸多边形、后声明覆盖 ----------

        [Theory]
        [MemberData(nameof(VerticalCellData))]
        public void TerrainShapes_FinalHeightEqualsTheShapesGroundAtTheFinalPosition(string cell)
        {
            foreach (var (script, map) in new[]
            {
                ("space.terrain_hills", "world.lab_nav_hills"),
                ("space.terrain_polygons", "world.lab_nav_polys"),
                ("space.terrain_overlay", "world.lab_nav_polys"),
            })
            {
                var shapes = Shapes(map);
                var record = Record(script, cell);
                var finalPosition = record.Ticks[record.Ticks.Count - 1].Position;
                Assert.Equal(GroundAt(shapes, finalPosition), record.Space!.PlayerHeights[record.Ticks.Count - 1], 9);
                // 每一步贴地（没有腾空）：脚下高度就是该点的地面高度。
                for (var t = 0; t < record.Ticks.Count; t++)
                {
                    if (!record.Space.Ext!.PlayerAirborne[t])
                    {
                        Assert.Equal(GroundAt(shapes, record.Ticks[t].Position), record.Space.PlayerHeights[t], 9);
                    }
                }
            }
        }

        [Fact]
        public void TerrainOverlay_LaterDeclarationDiffersFromTheEarlierShapeAtTheTargetPoint()
        {
            var shapes = Shapes("world.lab_nav_polys");
            var target = new Vec2(13.5, 0);
            var earlierOnly = shapes.Take(shapes.Count - 1).ToList();
            Assert.NotEqual(GroundAt(earlierOnly, target), GroundAt(shapes, target)); // 前提：覆盖确实改变了该点的高度
            var record = Record("space.terrain_overlay", "side_2d_targeted");
            Assert.Equal(GroundAt(shapes, target), record.Space!.PlayerHeights[record.Ticks.Count - 1], 2);
        }

        // ---------- 缺省不变 ----------

        [Fact]
        public void ScriptsWithoutMoveToOrSwap_HaveNoNavGroup_SoExistingFingerprintsAreUntouched()
        {
            foreach (var id in new[] { "space.terrain", "space.jump", "space.air_control" })
            {
                var fp = LabTestSupport.Runner.Run(LabTestSupport.Script(id), "side_2d_targeted");
                Assert.False(fp.Groups.ContainsKey("space_nav"), id);
            }
        }

        [Fact]
        public void MoveToAndTerrainSwapEvents_RoundTripThroughTheScriptJson()
        {
            var script = LabTestSupport.Script("space.nav_revalidate");
            var again = InputScript.Parse(script.ToJson());
            Assert.Equal(script.ToJson(), again.ToJson());
            Assert.Contains(again.Events, e => e.Kind == ScriptEventKind.MoveTo && e.Value.Equals(new Vec2(22, 0)));
            Assert.Contains(again.Events, e => e.Kind == ScriptEventKind.TerrainSwap && e.Action == "world.lab_nav_walled");
        }
    }
}
