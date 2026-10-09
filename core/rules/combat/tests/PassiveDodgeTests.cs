using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Rng;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// ADR-0178 被动闪避：命中表的 <c>dodge</c> 分支按受击者的闪避属性掷骰；带保留标签
    /// <c>skill.tag.unavoidable</c> 的技能不掷回避类分支。
    /// <para>
    /// 判断记录（期望值怎么算）：命中表只开 <c>dodge</c> 一个分支时，每次结算恰好消耗命中判定流（<c>combat.hit</c>）
    /// 的一次抽样，闪避当且仅当 <c>抽样 &lt; 闪避属性当前值</c>。用例在结算之前取该流的状态，用另一个同种子的
    /// <see cref="RngHost"/> 把状态拨到同一处，独立重放同样次数的抽样，得到「按属性与种子算出的期望次数」，与管线实际
    /// 判出的闪避次数逐次比对；这条比对不依赖管线里的任何实现细节，只依赖流与判定式的约定。
    /// </para>
    /// </summary>
    public class PassiveDodgeTests
    {
        private static readonly Id Hero = new Id("unit.dodge_attacker");
        private static readonly Id Dummy = new Id("unit.dodge_target");
        private static readonly Id SkillId = new Id("skill.dodge_test_strike");
        private static readonly Id HitStream = new Id("combat.hit");

        private const double TinyDamage = 0.001; // 目标 1000 点生命，万级样本也打不死，保证每次结算都进命中表

        private static CombatTestSupport.Fixture MakeFixture(ulong seed, double dodge)
        {
            var fx = CombatTestSupport.Build(o => o.HitTableConfigId = new Id("combat.hit_table.dodge_stat"), seed);
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);
            fx.Stats.SetBase(Dummy, CombatTestSupport.StatDodgeRating, dodge);
            return fx;
        }

        private static EffectContext Strike(IReadOnlyList<Id>? tags = null) =>
            new EffectContext(Hero, Dummy, SkillId, EffectKind.SchoolDamage, CombatTestSupport.SchoolPhysical,
                TinyDamage, coefficient: 1.0, canCrit: false, canMiss: true, tags: tags);

        /// <summary>独立重放：同种子新建一个宿主，把命中流拨到 <paramref name="state"/>，抽 <paramref name="draws"/> 次。</summary>
        private static bool[] ReplayDodges(ulong seed, RngStreamState state, int draws, double probability)
        {
            var replay = new RngHost(seed);
            replay.SetStreamState(HitStream, state);
            var result = new bool[draws];
            for (var i = 0; i < draws; i++)
            {
                result[i] = replay.Next(HitStream) < probability;
            }

            return result;
        }

        private static bool[] ResolveMany(CombatTestSupport.Fixture fx, int count, IReadOnlyList<Id>? tags = null)
        {
            var dodged = new bool[count];
            for (var i = 0; i < count; i++)
            {
                dodged[i] = fx.Host.ResolveEffect(Strike(tags)).Hit == HitResult.Dodge;
            }

            return dodged;
        }

        [Theory]
        [InlineData(0.05, 20000)]
        [InlineData(0.2, 20000)]
        [InlineData(0.5, 20000)]
        public void DodgeCount_EqualsReplayOfTheSeededStream_AndConvergesToTheStatProbability(double probability, int attacks)
        {
            const ulong seed = 20261010UL;
            var fx = MakeFixture(seed, probability);
            var state = fx.Rng.GetStreamState(HitStream);
            var expected = ReplayDodges(seed, state, attacks, probability);

            var actual = ResolveMany(fx, attacks);

            // 逐次一致：同一个种子、同一个属性值，第 i 次攻击是否被闪避由流唯一决定。
            Assert.Equal(expected, actual);

            // 收敛：样本闪避率落在 4σ 内（二项分布），说明判定式确实是「抽样 < 属性值」，不是别的曲线。
            var rate = actual.Count(d => d) / (double)attacks;
            var sigma = Math.Sqrt(probability * (1 - probability) / attacks);
            Assert.InRange(rate, probability - 4 * sigma, probability + 4 * sigma);
        }

        [Fact]
        public void SameSeedAndStat_ReproduceTheExactDodgeSequence_AndAnotherSeedDiffers()
        {
            const int attacks = 2000;
            var first = ResolveMany(MakeFixture(77UL, 0.3), attacks);
            var second = ResolveMany(MakeFixture(77UL, 0.3), attacks);
            var other = ResolveMany(MakeFixture(78UL, 0.3), attacks);

            Assert.Equal(first, second);
            Assert.NotEqual(first, other);
        }

        [Fact]
        public void DodgedAttack_DealsNoDamage_AndPublishesAvoidedEventWithDodgeResult()
        {
            var fx = MakeFixture(1UL, dodge: 1.0);
            var before = fx.Powers.GetPower(Dummy, WellKnownPowers.Health);

            var result = fx.Host.ResolveEffect(Strike());
            CombatTestSupport.Dispatch(fx);

            Assert.Equal(HitResult.Dodge, result.Hit);
            Assert.Equal(0.0, result.FinalAmount);
            Assert.Equal(before, fx.Powers.GetPower(Dummy, WellKnownPowers.Health));
            var avoided = Assert.Single(fx.Events.OfType<CombatAttackAvoidedEvent>());
            Assert.Equal(HitResult.Dodge, avoided.HitResult);
            Assert.Equal(Dummy, avoided.TargetId);
            Assert.Empty(fx.Events.OfType<CombatDamageDealtEvent>());
        }

        [Fact]
        public void ZeroDodgeStat_NeverDodges_AndFullDodgeStatAlwaysDodges()
        {
            Assert.DoesNotContain(true, ResolveMany(MakeFixture(5UL, 0.0), 500));
            Assert.DoesNotContain(false, ResolveMany(MakeFixture(5UL, 1.0), 500));
        }

        [Fact]
        public void UnavoidableTag_SkipsTheDodgeRoll_EvenAtFullDodge_AndConsumesNoRandomDraw()
        {
            var fx = MakeFixture(9UL, dodge: 1.0);
            var stateBefore = fx.Rng.GetStreamState(HitStream);
            var tags = new[] { new Id("skill.tag.sample_melee"), WellKnownSkillTags.Unavoidable };

            var dodged = ResolveMany(fx, 300, tags);

            Assert.DoesNotContain(true, dodged);
            // 没有掷回避分支，也就没有消耗命中流（暴击分支在此命中表里关闭）。
            Assert.Equal(stateBefore, fx.Rng.GetStreamState(HitStream));
            Assert.Empty(fx.Events.OfType<CombatAttackAvoidedEvent>());
        }

        [Fact]
        public void TagWithoutTheReservedId_StillDodges()
        {
            var fx = MakeFixture(9UL, dodge: 1.0);
            var tags = new[] { new Id("skill.tag.sample_melee") };

            Assert.DoesNotContain(false, ResolveMany(fx, 100, tags));
        }

        [Fact]
        public void HealingIsNeverDodged()
        {
            var fx = MakeFixture(3UL, dodge: 1.0);
            var heal = new EffectContext(Hero, Dummy, new Id("skill.dodge_test_heal"), EffectKind.Heal,
                CombatTestSupport.SchoolPhysical, 10, coefficient: 1.0);

            Assert.NotEqual(HitResult.Dodge, fx.Host.ResolveEffect(heal).Hit);
        }
    }
}
