using System.Linq;
using Core.Foundation.EventBus;
using Core.Gameplay.Encounter;
using Xunit;

namespace Tests.Gameplay.EndToEnd
{
    /// <summary>
    /// 消费方反馈 P2 缺口 5：遭遇开场同一拍被判胜利。参战单位刚生成时 <c>entity.created</c> 还在事件队列里，
    /// 空间索引（EntitySpatialSyncHost）要等它派发才同步；<c>EncounterTickHandler</c> 在同一个
    /// TriggerEvaluation 阶段对 <c>enemies.count_in_range(R) == 0</c> 求值，会把"敌人还没被索引看见"误判成"敌人已清空"。
    /// 复现：不做任何手动 <c>DispatchPending</c> 绕行，开遭遇后跑 Tick，敌人活着时不得出现 encounter.won。
    /// 不变量：遭遇判胜 ⇔ 参战敌人确实已清空（胜利事件之时，参战单位全部已死）。
    /// </summary>
    public sealed class EncounterStartSettleTests
    {
        [Fact]
        public void EncounterStart_WithoutManualDispatch_DoesNotWinWhileEnemyAlive_ThenWinsAfterItDies()
        {
            var fx = GameWorldFixture.Build();
            fx.Gameplay.EnterMap(GameWorldFixture.MapId, GameWorldFixture.PlayerId);

            fx.Gameplay.Encounter.Start(GameWorldFixture.EncounterBeastFight, GameWorldFixture.MapId, GameWorldFixture.PlayerId);
            var record = fx.Gameplay.Spawn.GetSpawnRecord(GameWorldFixture.SpawnEncounterAmbusher);
            Assert.NotNull(record);
            var ambusher = record!.EntityId!.Value;

            // 复现：开场连跑几拍（不提交施法，敌人必然活着），不得出现胜利。
            var won = false;
            fx.Bus.Subscribe<EncounterWonEvent>(new Core.Foundation.Common.Id("encounter.won"), _ => won = true);
            for (var i = 0; i < 5; i++)
            {
                fx.Tick();
                Assert.True(fx.Gameplay.Carriers.Units.IsAlive(ambusher), "敌人应仍存活");
                Assert.False(won, "敌人活着时遭遇不得被判胜利（第 " + i + " 拍）");
            }

            // 不变量：敌人确实被清空后才判胜利，且之后会判胜利。
            Assert.True(fx.CastUntilDead(GameWorldFixture.SkillStrike, ambusher), "应能打死敌人");
            Assert.False(won, "场上还有别的敌人（地图刷怪点的野兽）时遭遇不得判胜利");
            var field = fx.Gameplay.Spawn.GetSpawnRecord(GameWorldFixture.SpawnBeastField);
            if (field?.EntityId is Core.Foundation.Common.Id fieldBeast && fx.Gameplay.Carriers.Units.Exists(fieldBeast))
            {
                Assert.True(fx.CastUntilDead(GameWorldFixture.SkillStrike, fieldBeast), "应能打死野兽");
            }

            fx.Tick(3);
            Assert.True(won, "敌人清空后应判胜利");
            Assert.Single(fx.Events.OfType<EncounterWonEvent>());
        }
    }
}
