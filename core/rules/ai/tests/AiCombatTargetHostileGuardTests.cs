using Core.Foundation.Common;
using Core.Rules.Ai;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary>
    /// ADR-0088（消费方第三十三批反馈2护栏，决定7）：<c>HandleCombat</c> 选目标时必须跳过已运行期
    /// 转为非敌对的仇恨来源，不能无条件信任 <c>GetTopThreat</c>（它不检查敌对性）。这条护栏与
    /// <c>ThreatTable</c> 订阅 <c>unit.faction_changed</c> 的源头修复（决定6，见
    /// <c>Tests.Rules.Combat.ThreatTableTests</c>）各自独立存在——即便某条路径遗留了一条非敌对的
    /// 仇恨条目，AI 也不应该选它当目标（"AI 永不锁定非敌对目标"的不变量）。本用例用
    /// <see cref="AiTestHarness"/>（<c>FakeThreatTable</c>，不经真实 <c>ThreatTable</c> 的事件订阅
    /// 清理）直接摆一条非敌对来源，只验证 <see cref="AiHost"/> 自己的目标选择逻辑。
    /// </summary>
    public class AiCombatTargetHostileGuardTests
    {
        private static readonly Id Mob = new Id("unit.hostile_guard_mob");
        private static readonly Id FriendlySource = new Id("unit.hostile_guard_friendly_source");
        private static readonly Id ProfileId = new Id("ai.profile.hostile_guard_test");

        private const string RotationsJson = @"[
            { ""id"": ""ai.rotation.hostile_guard_trivial_false"", ""entries"": [
                { ""priority"": 1, ""condition"": ""false"", ""skill_id"": ""skill.never"" }
            ] }
        ]";

        private const string ProfileJson = @"[{
            ""id"": ""ai.profile.hostile_guard_test"",
            ""perception_radius"": 0.01,
            ""leash_range"": 20,
            ""combat_return_policy"": ""return_to_spawn"",
            ""rotation_ref"": ""ai.rotation.hostile_guard_trivial_false""
        }]";

        [Fact]
        public void HandleCombat_ThreatTableHasOnlyNonHostileSource_PicksNoTarget_FallsBackToCombatToReturn()
        {
            var harness = AiTestHarness.Build(ProfileJson, RotationsJson);

            // Mob 与 FriendlySource 同阵营——现实场景对应"目标运行期转为友方后，仇恨表理应被
            // ThreatTable 的 unit.faction_changed 订阅清空，但假设某条路径遗留了一条僵尸条目"：
            // 本用例直接在 FakeThreatTable 里摆出这种局面，不依赖真实 ThreatTable 的事件订阅。
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.AddUnit(FriendlySource, new Vec2(1, 0), AiTestSupport.FactionMonster);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);

            harness.Threat.AddThreat(Mob, FriendlySource, 10);
            harness.Host.ForceState(Mob, BehaviorState.Combat);

            Assert.Null(harness.Host.GetTarget(Mob));

            harness.Host.Step(Mob, 0.5);

            // 不变量：仇恨表里只剩非敌对来源时，HandleCombat 不选任何目标——修复前会直接把
            // FriendlySource 当目标（GetTopThreat 不检查敌对性），此后 AI 一直"追"这个友方目标、
            // Rotation 求值恒 NoValidTarget（消费方实测复现：69 次），旁边的真正敌对单位 0 出手。
            Assert.Null(harness.Host.GetTarget(Mob));
            Assert.Equal(BehaviorState.Return, harness.Host.GetBehaviorState(Mob));
        }
    }
}
