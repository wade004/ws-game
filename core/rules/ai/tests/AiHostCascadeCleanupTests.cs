using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Ai;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary>
    /// 场景卸载级联清理（ADR-0016 背景一节联动发现的既有缺口，见任务汇报"判断记录"）：
    /// <see cref="AiHost"/> 此前只能靠调用方显式 <see cref="AiHost.UnregisterUnit"/> 移除登记，
    /// <c>IWorldSim.ClearAll</c>/单个实体销毁走的是 <c>entity.destroyed</c> 事件，没有人替它调用
    /// <c>UnregisterUnit</c>——残留的登记状态会在下次 <c>AiTickHandler</c> 推进到该 id 时，因
    /// <c>WorldUnitAccess.Require</c> 找不到已销毁的实体而抛 <see cref="System.InvalidOperationException"/>
    /// （复现路径：进图生成生物 → 卸载 → 再进图 → tick，见 <c>core/gameplay/assembly</c>
    /// <c>GameplayAssembly.LeaveMap</c> 判断记录）。<see cref="AiHost"/> 现订阅
    /// <c>entity.destroyed</c> 静默移除对应登记，本测试直接发布该事件验证。
    /// </summary>
    public class AiHostCascadeCleanupTests
    {
        private static readonly Id Mob = new Id("unit.cascade_mob");
        private static readonly Id ProfileId = new Id("ai.profile.cascade");
        private const string TrivialRotationId = "ai.rotation.cascade_trivial_false";

        private const string RotationsJson = @"[
            { ""id"": ""ai.rotation.cascade_trivial_false"", ""entries"": [
                { ""priority"": 1, ""condition"": ""false"", ""skill_id"": ""skill.never"" }
            ] }
        ]";

        private const string ProfileJson = @"[{
            ""id"": ""ai.profile.cascade"",
            ""perception_radius"": 10,
            ""leash_range"": 20,
            ""combat_return_policy"": ""return_to_spawn"",
            ""rotation_ref"": """ + TrivialRotationId + @"""
        }]";

        [Fact]
        public void EntityDestroyedEvent_RemovesRegisteredUnit_WithoutRequiringExplicitUnregister()
        {
            var harness = AiTestHarness.Build(ProfileJson, RotationsJson);
            harness.AddUnit(Mob, Vec2.Zero, AiTestSupport.FactionMonster);
            harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero);

            Assert.Contains(Mob, harness.Host.RegisteredUnitIds);

            // 模拟 IWorldSim.ClearAll/生命周期清理派发的 entity.destroyed，不显式调用 UnregisterUnit。
            harness.Bus.PublishImmediate(new EntityDestroyedEvent(Mob));

            Assert.DoesNotContain(Mob, harness.Host.RegisteredUnitIds);

            // 残留登记已被移除：再次 RegisterUnit（同 id，模拟同一场景重新进图后的重新生成）
            // 不应该因为"已注册过"而抛异常。
            var ex = Record.Exception(() => harness.Host.RegisterUnit(Mob, ProfileId, spawnPoint: Vec2.Zero));
            Assert.Null(ex);
        }

        [Fact]
        public void EntityDestroyedEvent_ForUnregisteredUnit_IsSilentNoOp()
        {
            var harness = AiTestHarness.Build(ProfileJson, RotationsJson);

            var ex = Record.Exception(() =>
                harness.Bus.PublishImmediate(new EntityDestroyedEvent(new Id("unit.never_registered"))));

            Assert.Null(ex);
        }
    }
}
