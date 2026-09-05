using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Gameplay.EndToEnd
{
    /// <summary>
    /// 收边任务补齐（排查 Unity PlayMode <c>VerticalSliceTests.
    /// Projectile_CastBoltSkill_ShowsProjectileView_ThenRemovedOnHit</c> 一次性失败留下的核心侧
    /// 回归测试）：核对施放 <c>skill.sample_bolt</c>（<c>effects[0].kind == "projectile"</c>）经
    /// <see cref="GameWorldFixture"/> 装配的完整 L0～L4 世界（<c>WorldSim.SubmitIntent</c> → 目标解析
    /// → <c>Core.Rules.Skill.CastPipeline</c> → <c>EffectDispatcher.ApplyProjectile</c> →
    /// <c>Core.Carriers.Projectile.ProjectileHost.Spawn</c>）确实产出一个
    /// <see cref="EntityKinds.Projectile"/> 实体——锁定"核心侧链路完整"这一事实，供未来任何一次
    /// <c>CarriersAssembly</c>/<c>EffectDispatcher</c>/<c>ProjectileHost</c> 改动做回归防线。
    /// <para>
    /// 判断记录（Unity 失败复查结论）：本用例 + 单独/整类/全量三种粒度重跑 86 条 PlayMode 用例均
    /// 稳定通过（详见落地计划"收边波 I"小节 I3b 记录），核心装配（<c>CarriersAssembly</c> 构造期
    /// 无条件 <c>new ProjectileHost(...)</c> 并作为 <c>projectileSpawner</c> 传给
    /// <c>RulesAssembly</c>，不依赖调用方显式传入 <c>projectileOptions</c>，见该类型第 2.5 步）与
    /// Unity 引导（<c>display.map.sample_bolt</c>/<c>EntityKindMapping</c> 均已在 I1 补齐）均未发现
    /// 可复现缺陷，原始失败判定为一次性瞬时状况（如背景任务描述的门禁在 I3 仍在编辑工作树期间
    /// 启动）。本用例作为长期回归留存，不代表"曾经存在的 bug 在此修复"。
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
