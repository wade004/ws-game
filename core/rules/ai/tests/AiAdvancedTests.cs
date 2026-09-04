using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SimLoop;
using Core.Rules.Ai;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary>决策节奏、确定性、平局随机分流三项验收标准（任务书 T2-10"确定性：同输入两次 Step
    /// 序列产生相同意图与事件""RandomTieBreak 时经 RngHost（断言 rng 流被消耗）"）。</summary>
    public class AiAdvancedTests
    {
        private static readonly Id Mob = new Id("unit.advanced_mob");
        private static readonly Id Enemy = new Id("unit.advanced_enemy");
        private static readonly Id ProfileId = new Id("ai.profile.advanced_test");

        private static double Dx(Intent i) => ((JsonNumber)i.Args["dx"]).Value;
        private static double Dy(Intent i) => ((JsonNumber)i.Args["dy"]).Value;

        [Fact]
        public void DecisionInterval_ThrottlesEvaluateCallsWithinInterval()
        {
            const string profileJson = @"[{
                ""id"": ""ai.profile.advanced_test"", ""perception_radius"": 15, ""leash_range"": 20,
                ""combat_return_policy"": ""return_to_spawn"", ""rotation_ref"": ""ai.rotation.always_true"",
                ""decision_interval"": 0.5
            }]";
            const string rotationsJson = @"[
                { ""id"": ""ai.rotation.always_true"", ""entries"": [
                    { ""priority"": 1, ""condition"": ""true"", ""skill_id"": ""skill.tick_test"" }
                ] }
            ]";

            var harness = AiTestHarness.Build(profileJson, rotationsJson);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.AddUnit(Enemy, new Vec2(1, 0), AiTestSupport.FactionPlayer);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);

            // ForceState 进入 combat 时累加器被拨满（见 README），紧接着的 Step 立即决策一次。
            harness.Host.ForceState(Mob, BehaviorState.Combat);

            harness.Host.Step(Mob, 0.1); // 累加器 0.5+0.1=0.6 >= 0.5 -> 决策 #1，余量 0.1
            Assert.Single(harness.Skills.Calls);

            harness.Host.Step(Mob, 0.1); // 0.2
            harness.Host.Step(Mob, 0.1); // 0.3
            harness.Host.Step(Mob, 0.1); // 0.4
            Assert.Single(harness.Skills.Calls); // decision_interval 内未再决策

            harness.Host.Step(Mob, 0.1); // 0.5 -> 决策 #2
            Assert.Equal(2, harness.Skills.Calls.Count);
        }

        private const string DetProfileJson = @"[{
            ""id"": ""ai.profile.advanced_test"", ""perception_radius"": 15, ""leash_range"": 20,
            ""combat_return_policy"": ""return_to_spawn"", ""rotation_ref"": ""ai.rotation.trivial_false""
        }]";

        private const string DetRotationsJson = @"[
            { ""id"": ""ai.rotation.trivial_false"", ""entries"": [
                { ""priority"": 1, ""condition"": ""false"", ""skill_id"": ""skill.never"" }
            ] }
        ]";

        [Fact]
        public void Determinism_SameStepSequenceTwice_ProducesIdenticalIntentsAndEvents()
        {
            (List<(double dx, double dy)> intents, List<(BehaviorState, BehaviorState)> events) Run()
            {
                var harness = AiTestHarness.Build(DetProfileJson, DetRotationsJson, rngSeed: 999);
                harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
                harness.AddUnit(Enemy, new Vec2(5, 0), AiTestSupport.FactionPlayer);
                harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);

                var intents = new List<(double, double)>();
                void Collect(IReadOnlyList<Intent> list)
                {
                    foreach (var intent in list) intents.Add((Dx(intent), Dy(intent)));
                }

                Collect(harness.Host.Step(Mob, 0.1));
                Collect(harness.Host.Step(Mob, 1.0));
                harness.MoveUnit(Mob, new Vec2(4, 0));
                Collect(harness.Host.Step(Mob, 0.1));
                Collect(harness.Host.Step(Mob, 0.1));

                harness.Dispatch();
                var events = harness.StateChanges.Select(e => (e.OldState, e.NewState)).ToList();
                return (intents, events);
            }

            var run1 = Run();
            var run2 = Run();

            Assert.Equal(run1.intents, run2.intents);
            Assert.Equal(run1.events, run2.events);
        }

        private static readonly Id TieEnemyA = new Id("unit.tie_enemy_a");
        private static readonly Id TieEnemyB = new Id("unit.tie_enemy_b");
        private static readonly Id RngStreamId = new Id("ai.decision");

        [Fact]
        public void FindNearestHostile_EquidistantCandidates_DefaultPicksByIdOrder_NoRngConsumed()
        {
            var harness = AiTestHarness.Build(DetProfileJson, DetRotationsJson);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.AddUnit(TieEnemyA, new Vec2(5, 0), AiTestSupport.FactionPlayer);
            harness.AddUnit(TieEnemyB, new Vec2(-5, 0), AiTestSupport.FactionPlayer);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);

            Assert.DoesNotContain(RngStreamId, harness.Rng.Streams);

            harness.Host.Step(Mob, 0.1);

            Assert.Equal(TieEnemyA, harness.Host.GetTarget(Mob)); // "unit.tie_enemy_a" < "unit.tie_enemy_b" 序数比较
            Assert.DoesNotContain(RngStreamId, harness.Rng.Streams);
        }

        [Fact]
        public void FindNearestHostile_EquidistantCandidates_RandomTieBreak_ConsumesRngStream()
        {
            var options = new AiOptions { RandomTieBreak = true };
            var harness = AiTestHarness.Build(DetProfileJson, DetRotationsJson, options: options);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.AddUnit(TieEnemyA, new Vec2(5, 0), AiTestSupport.FactionPlayer);
            harness.AddUnit(TieEnemyB, new Vec2(-5, 0), AiTestSupport.FactionPlayer);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);

            Assert.DoesNotContain(RngStreamId, harness.Rng.Streams);

            harness.Host.Step(Mob, 0.1);

            Assert.Contains(RngStreamId, harness.Rng.Streams);
            var target = harness.Host.GetTarget(Mob);
            Assert.True(target == TieEnemyA || target == TieEnemyB);
        }
    }
}
