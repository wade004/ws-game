using System.Linq;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Integration
{
    /// <summary>
    /// ADR-0073（消费方第十九批第 3 条根治）：<see cref="CombatDamageDealtEvent.SkillId"/> 的装配级
    /// 集成测试——全程经真实 <c>Core.Rules.Assembly.RulesAssembly</c>（经 <see cref="FightWorldBuilder"/>
    /// 组装，同 <c>MovementInterruptWiringTests</c>/<c>TwoUnitsFightTests</c> 既有惯例），不直接反射/
    /// 白盒构造 <see cref="Core.Rules.Common.EffectContext"/>，验证"真实技能命中携带该技能 id、光环
    /// 周期伤害不携带任何技能 id"这两条结论在真实装配根下成立。
    /// </summary>
    public sealed class CombatDamageDealtSkillIdWiringTests
    {
        /// <summary>验收标准 1：<see cref="FightWorldBuilder.PlayerScript"/> 在 tick 1 对 npc 施放
        /// <see cref="FightWorldBuilder.SkillStrike"/>（<c>cast_time = 0</c>，即时结算，见其数据定义），
        /// 早于 tick 3 才施放的 <see cref="FightWorldBuilder.SkillBurn"/>——玩家来源的第一条
        /// <see cref="CombatDamageDealtEvent"/> 必然来自这次 strike 直接命中（经
        /// <c>CastPipeline.ExecuteEffectsOnly</c> 落地，<c>AttackInstanceId</c> 因此非空，见该字段
        /// 判断记录），断言其 <c>SkillId</c> 等于施放的技能 id。改动前 <c>CombatDamageDealtEvent</c>
        /// 不存在 <c>SkillId</c> 这个成员，本用例在改动前根本无法编译通过，不是"断言失败"而是"契约
        /// 缺失"——这正是消费方反馈复现的真缺口本身。</summary>
        [Fact]
        public void RealSkillCast_DirectDamage_CarriesCastSkillId()
        {
            var fx = FightWorldBuilder.Build();
            fx.Run();

            var firstPlayerDamage = fx.Events
                .OfType<CombatDamageDealtEvent>()
                .First(e => e.SourceId == FightWorldBuilder.PlayerId);

            Assert.True(firstPlayerDamage.AttackInstanceId.HasValue,
                "玩家来源的第一条伤害事件应经 CastPipeline 落地（tick 1 的 skill.sample_strike），AttackInstanceId 应非空");
            Assert.Equal(FightWorldBuilder.SkillStrike, firstPlayerDamage.SkillId);
        }

        /// <summary>验收标准 3：<see cref="FightWorldBuilder.SkillBurn"/> 在 tick 3 施放后落地一个持续
        /// 周期伤害光环（<c>skill.aura_def.sample_burn</c>，<c>base_value = 5</c>），随后每 1 模拟秒
        /// 由 <c>AuraHost.FirePeriodic</c> 触发一次周期伤害——不经 <c>CastPipeline</c>，
        /// <c>AttackInstanceId</c> 恒为空（既有事实，见 <c>TwoUnitsFightTests.
        /// EventOrder_AuraApplied_BeforeFirstPeriodicDamage</c> 已用 <c>Amount == 5.0</c> 钉住"确实
        /// 出现过周期伤害"这一事实）。本用例断言这类周期伤害事件的 <c>SkillId</c> 为空——它们的
        /// <c>EffectContext.SkillId</c> 实际是光环定义 id <c>skill.aura_def.sample_burn</c>（不是
        /// <c>skill.def</c> 命名空间下的技能 id），若被直接转发会冒充成技能 id，见
        /// <c>Resolver.Resolve</c>/<c>CombatDamageDealtEvent.SkillId</c> 判断记录"按 IsPeriodic 截断"。</summary>
        [Fact]
        public void RealPeriodicAuraDamage_SkillIdIsNull()
        {
            var fx = FightWorldBuilder.Build();
            fx.Run();

            var periodicDamages = fx.Events
                .OfType<CombatDamageDealtEvent>()
                .Where(e => e.SourceId == FightWorldBuilder.PlayerId && e.AttackInstanceId == null)
                .ToList();

            Assert.NotEmpty(periodicDamages);
            Assert.All(periodicDamages, e => Assert.Equal(5.0, e.Amount));
            Assert.All(periodicDamages, e => Assert.Null(e.SkillId));
        }
    }
}
