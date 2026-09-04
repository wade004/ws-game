using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Rules.Ai;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary><c>ai.behavior_profile.transitions</c> 用 Expr 覆盖默认转移判定
    /// （06 §6.1、任务书"transitions 覆盖：idle_to_chase 用 Expr enemies.count_in_range(5) > 0"）。</summary>
    public class AiTransitionsOverrideTests
    {
        private static readonly Id Mob = new Id("unit.override_mob");
        private static readonly Id Enemy = new Id("unit.override_enemy");
        private static readonly Id ProfileId = new Id("ai.profile.override_test");

        private const string RotationsJson = @"[
            { ""id"": ""ai.rotation.trivial_false"", ""entries"": [
                { ""priority"": 1, ""condition"": ""false"", ""skill_id"": ""skill.never"" }
            ] }
        ]";

        private const string ProfileJson = @"[{
            ""id"": ""ai.profile.override_test"", ""perception_radius"": 15, ""leash_range"": 20,
            ""combat_return_policy"": ""return_to_spawn"", ""rotation_ref"": ""ai.rotation.trivial_false"",
            ""transitions"": { ""idle_to_chase"": ""enemies.count_in_range(5) > 0"" }
        }]";

        [Fact]
        public void OverrideReturnsFalse_SuppressesOtherwiseEligibleIdleToChaseTransition()
        {
            var harness = AiTestHarness.Build(ProfileJson, RotationsJson);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.AddUnit(Enemy, new Vec2(5, 0), AiTestSupport.FactionPlayer); // 默认判定下本会触发 chase
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);
            harness.ExprFactory.Host.Values["enemies.count_in_range"] = ExprValue.OfInt(0);

            harness.Host.Step(Mob, 0.1);

            Assert.Equal(BehaviorState.Idle, harness.Host.GetBehaviorState(Mob));
        }

        [Fact]
        public void OverrideReturnsTrue_AllowsTransition_AndExprHostIsConsulted()
        {
            var harness = AiTestHarness.Build(ProfileJson, RotationsJson);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.AddUnit(Enemy, new Vec2(5, 0), AiTestSupport.FactionPlayer);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);
            harness.ExprFactory.Host.Values["enemies.count_in_range"] = ExprValue.OfInt(1);

            harness.Host.Step(Mob, 0.1);

            Assert.Equal(BehaviorState.Chase, harness.Host.GetBehaviorState(Mob));
            Assert.NotEmpty(harness.ExprFactory.Calls); // 证明确实走了 IExprHostFactory 而非默认代码判定
        }
    }
}
