using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Gameplay.EndToEnd
{
    /// <summary>
    /// 收边任务补齐（排查 Unity PlayMode <c>VerticalSliceTests.
    /// Projectile_CastBoltSkill_ShowsProjectileView_ThenRemovedOnHit</c> 首次失败留下的核心侧
    /// 回归测试）：核对施放 <c>skill.sample_bolt</c>（<c>effects[0].kind == "projectile"</c>）经
    /// <see cref="GameWorldFixture"/> 装配的完整 L0～L4 世界（<c>WorldSim.SubmitIntent</c> → 目标解析
    /// → <c>Core.Rules.Skill.CastPipeline</c> → <c>EffectDispatcher.ApplyProjectile</c> →
    /// <c>Core.Carriers.Projectile.ProjectileHost.Spawn</c>）确实产出一个
    /// <see cref="EntityKinds.Projectile"/> 实体——锁定"核心侧链路完整"这一事实，供未来任何一次
    /// <c>CarriersAssembly</c>/<c>EffectDispatcher</c>/<c>ProjectileHost</c> 改动做回归防线。
    /// <para>
    /// 判断记录（修正：原先本段注释误判为"一次性瞬时状况、未发现可复现缺陷"——复核确认该结论
    /// 不成立）：核心装配链路（<c>CarriersAssembly</c> 构造期无条件 <c>new ProjectileHost(...)</c>
    /// 并作为 <c>projectileSpawner</c> 传给 <c>RulesAssembly</c>，见该类型第 2.5 步）与 Unity 引导
    /// （<c>display.map.sample_bolt</c>/<c>EntityKindMapping</c>）确实都没问题，本用例本身也一直
    /// 稳定通过（用 <c>StubSpatialQuery.Register</c> 手动登记目标位置，绕开了下面第二点的时序），
    /// 但 Unity 侧那条 PlayMode 用例最初的失败是<b>可复现的两个真实缺陷</b>共同导致，均已定位并
    /// 修复（详见落地计划"收边波 I"小节，具体见下方两点与各自类型判断记录）：
    /// (1) <c>core/rules/skill.AuraHost</c> 此前不订阅 <c>entity.destroyed</c>，目标单位被
    /// <c>WorldSim.ClearAll()</c> 移除后残留的周期光环仍会在下一次 <c>Update</c> 对着已消失的目标
    /// 结算，命中 <c>WorldUnitAccess.Require</c> 抛异常，与其它 PlayMode 用例混跑时会连累无关用例
    /// 失败——已订阅该事件自愈（见 <c>AuraHost</c> 判断记录），配回归测试
    /// <c>AuraEffectTests.EntityDestroyed_*</c>。
    /// (2) 该 PlayMode 用例自身写法有缺陷：只在开头 <c>Cast</c> 一次后被动轮询，而示例生物刚生成时
    /// <c>entity.created</c> 尚未派发、空间索引未同步，首次施法命中
    /// <c>CastFailureReason.NoValidTarget</c>；已改为每次轮询迭代都重新 <c>Cast</c>（与本文件其余
    /// 用例"普攻直到死亡"等既有惯例一致）。
    /// </para>
    /// </summary>
    public sealed class ProjectileCastEndToEndTests
    {
        [Fact]
        public void CastBoltSkill_WithHostileTargetInRange_SpawnsProjectileEntity()
        {
            var fx = GameWorldFixture.Build();
            fx.Gameplay.EnterMap(GameWorldFixture.MapId, GameWorldFixture.PlayerId);

            // 惯例同 EndToEndTests.EnterAndAcceptQuest：进场景触发 spawn.sample_beast_field 生成
            // 一只 creature.sample_beast，但 CreatureFactory.Spawn 不会自动同步进本夹具自己的
            // StubSpatialQuery（该桩没有 ISpatialIndexSync），需要手动登记，否则
            // target.chain.sample_nearest_enemy 的 nearest_in_shape 找不到它（NoValidTarget）。
            var spawnRecord = fx.Gameplay.Spawn.GetSpawnRecord(GameWorldFixture.SpawnBeastField);
            Assert.NotNull(spawnRecord);
            Assert.True(spawnRecord!.EntityId.HasValue, "spawn.sample_beast_field 在 EnterMap 后应已生成一个实体");
            var beastId = spawnRecord.EntityId!.Value;
            var beastPos = fx.Gameplay.Carriers.Units.GetPosition(beastId);
            fx.Spatial.Register(beastId, beastPos, 0.5);

            var args = new JsonObjectBuilder().Add("skill_id", new JsonString("skill.sample_bolt")).Build();
            fx.World.SubmitIntent(new Intent(GameWorldFixture.PlayerId, "cast", args));

            var projCountAfterEachTick = -1;
            for (var i = 0; i < 5 && projCountAfterEachTick <= 0; i++)
            {
                fx.Tick();
                projCountAfterEachTick = fx.World.QueryEntities(new EntityFilter(kind: EntityKinds.Projectile)).Count;
            }

            var failed = System.Linq.Enumerable.OfType<Core.Rules.Common.SkillCastFailedEvent>(fx.Events);
            var failMsg = string.Join(",", System.Linq.Enumerable.Select(failed, e => e.ReasonCode.ToString()));
            Assert.True(projCountAfterEachTick > 0, $"projCountAfterEachTick={projCountAfterEachTick}; failReasons={failMsg}");
        }
    }
}
