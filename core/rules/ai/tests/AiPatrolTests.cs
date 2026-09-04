using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SimLoop;
using Core.Rules.Ai;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary>巡逻路径 loop/pingpong 两种模式的移动意图方向（06 §6.3）与
    /// <c>combat_return_policy = patrol</c> 回到巡逻的行为。</summary>
    public class AiPatrolTests
    {
        private static readonly Id Mob = new Id("unit.patrol_mob");
        private static readonly Id ProfileId = new Id("ai.profile.patrol_test");

        private const string RotationsJson = @"[
            { ""id"": ""ai.rotation.trivial_false"", ""entries"": [
                { ""priority"": 1, ""condition"": ""false"", ""skill_id"": ""skill.never"" }
            ] }
        ]";

        // 三点直线路径：P0=(0,0) P1=(10,0) P2=(20,0)。
        private const string PatrolPointsJson = @"[{""x"":0,""y"":0},{""x"":10,""y"":0},{""x"":20,""y"":0}]";

        private static string ProfileJson(string mode) =>
            "[{ \"id\": \"ai.profile.patrol_test\", \"perception_radius\": 0.01, \"leash_range\": 999," +
            " \"combat_return_policy\": \"return_to_spawn\", \"rotation_ref\": \"ai.rotation.trivial_false\"," +
            " \"patrol_path_ref\": \"ai.path.patrol_test\" }]";

        private static string PatrolJson(string mode) =>
            "[{ \"id\": \"ai.path.patrol_test\", \"points\": " + PatrolPointsJson + ", \"mode\": \"" + mode + "\" }]";

        private static double Dx(Intent intent) => ((JsonNumber)intent.Args["dx"]).Value;

        [Fact]
        public void Loop_AfterReachingLastPoint_WrapsBackToFirstPointThenForwardAgain()
        {
            var harness = AiTestHarness.Build(ProfileJson("loop"), RotationsJson, PatrolJson("loop"));
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);
            Assert.Equal(BehaviorState.Patrol, harness.Host.GetBehaviorState(Mob));

            // 起点恰好等于 P0：立即"到达"并推进到目标 P1，不产生意图。
            Assert.Empty(harness.Host.Step(Mob, 1.0));

            // 朝 P1 前进：方向为正。
            var toP1 = Assert.Single(harness.Host.Step(Mob, 1.0));
            Assert.True(Dx(toP1) > 0);
            harness.MoveUnit(Mob, new Vec2(10, 0));

            // 到达 P1，推进到目标 P2，不产生意图。
            Assert.Empty(harness.Host.Step(Mob, 1.0));

            // 朝 P2 前进：方向为正。
            var toP2 = Assert.Single(harness.Host.Step(Mob, 1.0));
            Assert.True(Dx(toP2) > 0);
            harness.MoveUnit(Mob, new Vec2(20, 0));

            // 到达 P2（最后一点），loop 模式跳回 P0，不产生意图。
            Assert.Empty(harness.Host.Step(Mob, 1.0));

            // 朝 P0 前进：方向为负。
            var toP0 = Assert.Single(harness.Host.Step(Mob, 1.0));
            Assert.True(Dx(toP0) < 0);
            harness.MoveUnit(Mob, new Vec2(0, 0));

            // 到达 P0，loop 继续前进到 P1，不产生意图。
            Assert.Empty(harness.Host.Step(Mob, 1.0));

            // loop 的判定性特征：绕回起点后再次朝 P1（正向）前进，而不是折返。
            var toP1Again = Assert.Single(harness.Host.Step(Mob, 1.0));
            Assert.True(Dx(toP1Again) > 0);
        }

        [Fact]
        public void PingPong_AfterReachingLastPoint_BouncesBackTowardFirstPoint()
        {
            var harness = AiTestHarness.Build(ProfileJson("pingpong"), RotationsJson, PatrolJson("pingpong"));
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);

            Assert.Empty(harness.Host.Step(Mob, 1.0)); // 到达 P0，推进目标到 P1

            var toP1 = Assert.Single(harness.Host.Step(Mob, 1.0));
            Assert.True(Dx(toP1) > 0);
            harness.MoveUnit(Mob, new Vec2(10, 0));

            Assert.Empty(harness.Host.Step(Mob, 1.0)); // 到达 P1，推进目标到 P2

            var toP2 = Assert.Single(harness.Host.Step(Mob, 1.0));
            Assert.True(Dx(toP2) > 0);
            harness.MoveUnit(Mob, new Vec2(20, 0));

            Assert.Empty(harness.Host.Step(Mob, 1.0)); // 到达 P2（端点），pingpong 折返：目标变为 P1

            var backToP1 = Assert.Single(harness.Host.Step(Mob, 1.0));
            Assert.True(Dx(backToP1) < 0);
            harness.MoveUnit(Mob, new Vec2(10, 0)); // 模拟到达折返后的目标 P1

            Assert.Empty(harness.Host.Step(Mob, 1.0)); // 到达 P1，pingpong 继续沿反方向推进到 P0

            // pingpong 的判定性特征：折返后继续朝 P0（负向）前进，而不是像 loop 那样转回正向。
            var towardP0 = Assert.Single(harness.Host.Step(Mob, 1.0));
            Assert.True(Dx(towardP0) < 0);
        }

        [Fact]
        public void CombatReturnPolicyPatrol_ReturnsToPatrolStart_ThenResumesPatrol()
        {
            const string profileJson = "[{ \"id\": \"ai.profile.patrol_test\", \"perception_radius\": 15, \"leash_range\": 20," +
                " \"combat_return_policy\": \"patrol\", \"rotation_ref\": \"ai.rotation.trivial_false\"," +
                " \"patrol_path_ref\": \"ai.path.patrol_test\" }]";

            var harness = AiTestHarness.Build(profileJson, RotationsJson, PatrolJson("loop"));
            harness.AddUnit(Mob, new Vec2(0, 0), AiTestSupport.FactionMonster);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: new Vec2(0, 0));

            harness.Host.ForceState(Mob, BehaviorState.Combat);
            harness.Host.ForceState(Mob, BehaviorState.Return);

            // 巡逻路径起点是 P0=(0,0)，单位已经在那里：一步内即到达并回到 patrol。
            var intents = harness.Host.Step(Mob, 1.0);

            Assert.Empty(intents);
            Assert.Equal(BehaviorState.Patrol, harness.Host.GetBehaviorState(Mob));
        }
    }
}
