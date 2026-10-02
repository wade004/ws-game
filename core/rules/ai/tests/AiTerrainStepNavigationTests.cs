using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.SimLoop;
using Core.Rules.Ai;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary>
    /// 手感落地 M4-W1a 补丁（AI 转向感知地形）：<see cref="AiHost.TerrainStepConstraint"/> 非空时，AI 取下一路点方向改用带约束的
    /// <c>FindPath</c> 重载（与移动层同一口径），台阶墙前的追击绕行到达；缺省（null）时寻路调用与路点序列与引入之前逐位一致。
    /// 测试按 <see cref="AiStateMachineTests"/> 的约定在两次 Step 之间手动应用 move 意图，应用规则就是移动系统的台阶阻挡
    /// （移动被台阶挡住时停在阻挡点前），所以"旧行为顶在台阶前"与"新行为绕行"都是可观测的位置变化，不是看内部状态。
    /// </summary>
    public class AiTerrainStepNavigationTests
    {
        private static readonly Id Map = new Id("map.ai_terrain_nav");
        private static readonly Id Mob = new Id("unit.ai_terrain_mob");
        private static readonly Id Enemy = new Id("unit.ai_terrain_enemy");
        private static readonly Id ProfileId = new Id("ai.profile.state_machine");
        private const double Dt = 0.1;
        private const double WallX = 5.0;
        private const double WallHalfWidth = 3.0;

        private const string RotationsJson = @"[
            { ""id"": ""ai.rotation.trivial_false"", ""entries"": [
                { ""priority"": 1, ""condition"": ""false"", ""skill_id"": ""skill.never"" }
            ] }
        ]";

        private const string ProfileJson =
            "[{ \"id\": \"ai.profile.state_machine\", \"perception_radius\": 50, \"leash_range\": 200, " +
            "\"combat_return_policy\": \"return_to_spawn\", \"rotation_ref\": \"ai.rotation.trivial_false\" }]";

        /// <summary>地形台阶墙：x = <see cref="WallX"/> 处、|y| &lt; <see cref="WallHalfWidth"/> 的一面台阶，任何穿过它的线段在穿越点被挡住。</summary>
        private sealed class WallStepConstraint : ITerrainStepConstraint
        {
            public int Calls;

            public Vec2? FirstStepBlock(Id mapId, Vec2 from, Vec2 to)
            {
                Calls++;
                var dx = to.X - from.X;
                if (Math.Abs(dx) <= 1e-12)
                {
                    return null;
                }

                var t = (WallX - from.X) / dx;
                if (t < 0.0 || t > 1.0)
                {
                    return null;
                }

                var y = from.Y + (to.Y - from.Y) * t;
                return Math.Abs(y) < WallHalfWidth ? new Vec2(WallX, y) : (Vec2?)null;
            }
        }

        /// <summary>从不挡路的约束（没有台阶的地形）：带约束规划应与不带约束逐位等价。</summary>
        private sealed class OpenStepConstraint : ITerrainStepConstraint
        {
            public Vec2? FirstStepBlock(Id mapId, Vec2 from, Vec2 to) => null;
        }

        /// <summary>数着两个 <c>FindPath</c> 重载各被调用几次的导航（其余走桩）。</summary>
        private sealed class SpyNavigation : INavigation2D
        {
            private readonly StubNavigation2D _inner = new StubNavigation2D();
            public int PlainFindPathCalls;
            public int TerrainFindPathCalls;
            public readonly List<Vec2> FirstWaypoints = new List<Vec2>();

            public void BuildNavMesh(Id mapId) => _inner.BuildNavMesh(mapId);

            public bool IsWalkable(Id mapId, Vec2 point) => _inner.IsWalkable(mapId, point);

            public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to)
            {
                PlainFindPathCalls++;
                var path = _inner.FindPath(mapId, from, to);
                Record(path);
                return path;
            }

            public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to, ITerrainStepConstraint? terrain)
            {
                TerrainFindPathCalls++;
                var path = _inner.FindPath(mapId, from, to, terrain);
                Record(path);
                return path;
            }

            private void Record(IReadOnlyList<Vec2>? path)
            {
                if (path != null && path.Count >= 2)
                {
                    FirstWaypoints.Add(path[1]);
                }
            }

            public Vec2? Raycast(Id mapId, Vec2 from, Vec2 to) => _inner.Raycast(mapId, from, to);

            public void SetBlocking(Id mapId, IReadOnlyList<Rect> rects) => _inner.SetBlocking(mapId, rects);

            public void Clear(Id mapId) => _inner.Clear(mapId);

            public int GetBlockingVersion(Id mapId) => _inner.GetBlockingVersion(mapId);
        }

        private sealed class Run
        {
            public AiTestHarness Harness = null!;
            public readonly List<Vec2> Deltas = new List<Vec2>();
            public double MaxAbsY;
            public int Steps;
        }

        private static AiTestHarness Build(INavigation2D navigation)
        {
            var options = new AiOptions();
#pragma warning disable CS0618 // 测试假实现没有 IUnitAccess.GetMapId，走文档化的单地图兜底
            options.MapId = Map;
#pragma warning restore CS0618
            var harness = AiTestHarness.Build(ProfileJson, RotationsJson, options: options, navigation: navigation);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.AddUnit(Enemy, new Vec2(10, 0), AiTestSupport.FactionPlayer);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);
            return harness;
        }

        /// <summary>
        /// 驱动 AI 追击并应用 move 意图。应用规则 = 移动系统的台阶阻挡：位移线段被 <paramref name="wall"/> 挡住就停在阻挡点前
        /// （与方向移动"贴着阻挡停住"同一结果）。到达攻击范围转入 Combat 或步数用完即停。
        /// </summary>
        private static Run Chase(AiTestHarness harness, ITerrainStepConstraint wall, int maxSteps = 300)
        {
            var run = new Run { Harness = harness };
            for (var i = 0; i < maxSteps; i++)
            {
                var intents = harness.Host.Step(Mob, Dt);
                run.Steps++;
                if (harness.Host.GetBehaviorState(Mob) == BehaviorState.Combat)
                {
                    break;
                }

                foreach (var intent in intents)
                {
                    if (intent.Kind != "move")
                    {
                        continue;
                    }

                    var delta = new Vec2(((JsonNumber)intent.Args["dx"]).Value, ((JsonNumber)intent.Args["dy"]).Value);
                    run.Deltas.Add(delta);
                    var from = harness.Units.GetPosition(Mob);
                    var to = from + delta;
                    var hit = wall.FirstStepBlock(Map, from, to);
                    if (hit.HasValue)
                    {
                        var travel = hit.Value - from;
                        var length = travel.Length;
                        to = length <= 1e-3 ? from : from + travel * ((length - 1e-3) / length);
                    }

                    harness.MoveUnit(Mob, to);
                    run.MaxAbsY = Math.Max(run.MaxAbsY, Math.Abs(to.Y));
                }
            }

            return run;
        }

        // ------------------------------------------------------------------ 复现

        [Fact]
        public void Chase_AcrossAStepWall_WithTheConstraint_DetoursAndReachesAttackRange_WithoutItStaysAtTheWall()
        {
            var wall = new WallStepConstraint();

            // 旧行为（没有地形约束）：AI 一路直冲台阶墙，移动层把它挡在台阶前，追不到目标。
            var oldBehavior = Chase(Build(new StubNavigation2D()), wall);
            var oldPos = oldBehavior.Harness.Units.GetPosition(Mob);
            Assert.Equal(BehaviorState.Chase, oldBehavior.Harness.Host.GetBehaviorState(Mob));
            Assert.True(oldPos.X < WallX && oldPos.X > WallX - 0.01, $"应顶在台阶前，实际 {oldPos}");
            Assert.True(oldBehavior.MaxAbsY < 1e-9, "没有地形约束：路点就是直线，AI 不会拐弯");

            // 新行为：宿主把台阶约束交给 AI，取路点带约束，绕过墙后进入攻击范围。
            var harness = Build(new StubNavigation2D());
            harness.Host.TerrainStepConstraint = wall;
            var run = Chase(harness, wall);
            var attackRange = harness.Options.AttackRange;
            var pos = harness.Units.GetPosition(Mob);
            Assert.Equal(BehaviorState.Combat, harness.Host.GetBehaviorState(Mob));
            Assert.True(Vec2.Distance(pos, new Vec2(10, 0)) <= attackRange + 1e-9, $"应进入攻击范围，实际 {pos}");
            Assert.True(run.MaxAbsY > WallHalfWidth, $"绕行必须走出台阶墙的侧面之外（|y| > {WallHalfWidth}），最大 |y|={run.MaxAbsY}");
        }

        [Fact]
        public void FirstWaypointTurnsAwayFromTheWall_OnlyWhenTheConstraintIsSet()
        {
            var plain = Build(new StubNavigation2D());
            plain.Host.Step(Mob, Dt); // idle -> chase
            var plainDelta = ((JsonNumber)plain.Host.Step(Mob, Dt)[0].Args["dy"]).Value;
            Assert.Equal(0.0, plainDelta); // 直线：转向提示正对目标，dy = 0

            var guarded = Build(new StubNavigation2D());
            guarded.Host.TerrainStepConstraint = new WallStepConstraint();
            guarded.Host.Step(Mob, Dt);
            var guardedDelta = ((JsonNumber)guarded.Host.Step(Mob, Dt)[0].Args["dy"]).Value;
            Assert.True(Math.Abs(guardedDelta) > 1e-6, "台阶墙在直线上：第一个路点已经是绕行方向，dy 应不为 0");
        }

        // ------------------------------------------------------------------ 不变量

        [Fact]
        public void Default_WithoutTheConstraint_NeverCallsTheTerrainAwarePlanner_AndWaypointsMatchThePlainPath()
        {
            var spy = new SpyNavigation();
            var harness = Build(spy);
            Assert.Null(harness.Host.TerrainStepConstraint);
            var run = Chase(harness, new OpenStepConstraint(), maxSteps: 40);

            Assert.Equal(0, spy.TerrainFindPathCalls);
            Assert.True(spy.PlainFindPathCalls > 0);
            // 没有地形时路点序列就是无地形 FindPath 的结果：直线上第一个路点恒为终点（目标位置）。
            Assert.All(spy.FirstWaypoints, w => Assert.Equal(new Vec2(10, 0), w));
            Assert.All(run.Deltas, d => Assert.Equal(0.0, d.Y));
        }

        [Fact]
        public void AConstraintWithoutAnyStep_GivesBitIdenticalMovesToNoConstraint()
        {
            var openWall = new OpenStepConstraint();

            var spyA = new SpyNavigation();
            var a = Chase(Build(spyA), openWall, maxSteps: 60);

            var spyB = new SpyNavigation();
            var harnessB = Build(spyB);
            harnessB.Host.TerrainStepConstraint = openWall;
            var b = Chase(harnessB, openWall, maxSteps: 60);

            Assert.True(spyB.TerrainFindPathCalls > 0, "带约束时应走带约束的重载");
            Assert.Equal(0, spyA.TerrainFindPathCalls);
            Assert.Equal(a.Deltas.Count, b.Deltas.Count);
            for (var i = 0; i < a.Deltas.Count; i++)
            {
                Assert.True(a.Deltas[i].Equals(b.Deltas[i]), $"第 {i} 步位移应逐位一致：{a.Deltas[i]} vs {b.Deltas[i]}");
            }

            Assert.True(a.Harness.Units.GetPosition(Mob).Equals(b.Harness.Units.GetPosition(Mob)));
        }
    }
}
