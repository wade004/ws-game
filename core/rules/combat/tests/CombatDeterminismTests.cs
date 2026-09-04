using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// 确定性测试（见 06 第 4.1 节"保证同种子同输入同结果"、4.7 节"批量重放同一场景两次断言
    /// 结果完全一致"）：同一主种子、同一调用序列，两次独立搭建的 <see cref="Core.Rules.Combat.CombatHost"/>
    /// 必须产生逐位相等的结算结果序列。用 <c>probabilistic</c> 命中表（概率非 0/1）真正驱动
    /// <c>IRngHost</c> 分支选择，而不是像其它测试那样用 base=0/1 强制分支。
    /// </summary>
    public class CombatDeterminismTests
    {
        private static readonly Id Hero = new Id("unit.determinism_hero");
        private static readonly Id Dummy = new Id("unit.determinism_dummy");
        private static readonly Id SkillId = new Id("skill.determinism_strike");

        private readonly struct Sample
        {
            public readonly HitResult Hit;
            public readonly double Requested;
            public readonly double Final;

            public Sample(HitResult hit, double requested, double final)
            {
                Hit = hit;
                Requested = requested;
                Final = final;
            }
        }

        private static List<Sample> RunSequence(ulong seed, int count)
        {
            var fx = CombatTestSupport.Build(
                o => o.HitTableConfigId = new Id("combat.hit_table.probabilistic"),
                seed: seed);
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);

            var results = new List<Sample>(count);
            for (int i = 0; i < count; i++)
            {
                var context = new EffectContext(Hero, Dummy, SkillId, EffectKind.SchoolDamage,
                    CombatTestSupport.SchoolPhysical, baseValue: 100, coefficient: 1.0);
                var r = fx.Host.ResolveEffect(context);
                results.Add(new Sample(r.Hit, r.RequestedAmount, r.FinalAmount));

                // 目标可能被打死（probabilistic 表可暴击），死亡后复活满血以便继续采样同一序列。
                if (!fx.Units.IsAlive(Dummy))
                {
                    fx.Units.SetAlive(Dummy, true);
                    var current = fx.Powers.GetPower(Dummy, WellKnownPowers.Health);
                    fx.Powers.ModifyPower(Dummy, WellKnownPowers.Health, 1000.0 - current, Hero);
                }
            }
            return results;
        }

        [Fact]
        public void SameSeed_SameCallSequence_ProducesIdenticalResults()
        {
            const ulong seed = 987654321UL;

            var run1 = RunSequence(seed, 20);
            var run2 = RunSequence(seed, 20);

            Assert.Equal(run1.Count, run2.Count);
            for (int i = 0; i < run1.Count; i++)
            {
                Assert.Equal(run1[i].Hit, run2[i].Hit);
                Assert.Equal(run1[i].Requested, run2[i].Requested);
                Assert.Equal(run1[i].Final, run2[i].Final);
            }
        }

        [Fact]
        public void DifferentSeeds_CanProduceDifferentResults()
        {
            var runA = RunSequence(1UL, 20);
            var runB = RunSequence(2UL, 20);

            var anyDifference = false;
            for (int i = 0; i < runA.Count; i++)
            {
                if (runA[i].Hit != runB[i].Hit || runA[i].Final != runB[i].Final)
                {
                    anyDifference = true;
                    break;
                }
            }

            Assert.True(anyDifference, "不同种子跑 20 次仍完全一致的概率极低，若发生说明 RngHost 分流未生效");
        }
    }
}
