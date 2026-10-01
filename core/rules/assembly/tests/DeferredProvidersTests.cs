using Core.Foundation.Common;
using Core.Rules.Assembly;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Assembly
{
    /// <summary>
    /// T-L11（测试覆盖剩余项 2026-10-01）：<see cref="RulesAssembly"/> 用来打破构造期循环依赖的三个
    /// "延迟绑定"转发器，以及 <see cref="NullAttackIntervalFallbackProvider"/> 的绑定前/绑定后两态。
    /// 绑定前必须是安全的中性值（不抛、不返回伪造数据），绑定后一律原样转发，重复绑定以最后一次为准。
    /// </summary>
    public class DeferredProvidersTests
    {
        private static readonly Id Unit = new Id("unit.deferred_probe");
        private static readonly Id School = new Id("school.deferred_probe");

        private sealed class FixedIntervalProvider : IAttackIntervalFallbackProvider
        {
            public double? Value;
            public Id? LastAsked;

            public double? GetAttackIntervalSeconds(Id unitId)
            {
                LastAsked = unitId;
                return Value;
            }
        }

        private sealed class FixedWeaponQuery : IWeaponDamageQuery
        {
            public double Base = 7.0;
            public double Dps = 11.0;
            public double? Interval = 1.75;
            public Id? WeaponSchool = School;

            public double GetWeaponBaseDamage(Id unitId) => Base;
            public double GetWeaponDps(Id unitId) => Dps;
            public double? GetWeaponAttackIntervalSeconds(Id unitId) => Interval;
            public Id? GetWeaponSchool(Id unitId) => WeaponSchool;
        }

        private sealed class HandlingExtension : IEffectExtension
        {
            public int Calls;
            public bool Handles = true;

            public bool TryHandle(EffectContext context, out ResolveResult result)
            {
                Calls++;
                result = new ResolveResult(HitResult.Hit, 3, 3, 0, false, false, null);
                return Handles;
            }
        }

        private static EffectContext MakeContext() =>
            new EffectContext(Unit, Unit, new Id("skill.deferred_probe"), EffectKind.SchoolDamage, School,
                baseValue: 1.0, coefficient: 0.0);

        [Fact]
        public void NullAttackIntervalFallbackProvider_AlwaysReturnsNull_AndIsASingleton()
        {
            Assert.Same(NullAttackIntervalFallbackProvider.Instance, NullAttackIntervalFallbackProvider.Instance);
            Assert.Null(NullAttackIntervalFallbackProvider.Instance.GetAttackIntervalSeconds(Unit));
            Assert.Null(NullAttackIntervalFallbackProvider.Instance.GetAttackIntervalSeconds(new Id("unit.other")));
        }

        [Fact]
        public void DeferredAttackIntervalProvider_BeforeBind_ReturnsNull_AfterBind_Forwards_RebindReplaces()
        {
            var deferred = new DeferredAttackIntervalProvider();
            Assert.Null(deferred.GetAttackIntervalSeconds(Unit));

            var first = new FixedIntervalProvider { Value = 2.0 };
            deferred.Bind(first);
            Assert.Equal(2.0, deferred.GetAttackIntervalSeconds(Unit));
            Assert.Equal(Unit, first.LastAsked);

            // 真实提供者返回 null（生物模板没有 attack_interval）时，转发器同样返回 null，不自行编造默认值。
            first.Value = null;
            Assert.Null(deferred.GetAttackIntervalSeconds(Unit));

            var second = new FixedIntervalProvider { Value = 0.5 };
            deferred.Bind(second);
            Assert.Equal(0.5, deferred.GetAttackIntervalSeconds(Unit));
        }

        [Fact]
        public void DeferredWeaponDamageQuery_BeforeBind_ReturnsNeutralValues_AfterBind_ForwardsAllFour()
        {
            var deferred = new DeferredWeaponDamageQuery();

            Assert.Equal(0.0, deferred.GetWeaponBaseDamage(Unit));
            Assert.Equal(0.0, deferred.GetWeaponDps(Unit));
            Assert.Null(deferred.GetWeaponAttackIntervalSeconds(Unit));
            Assert.Null(deferred.GetWeaponSchool(Unit));

            var real = new FixedWeaponQuery();
            deferred.Bind(real);

            Assert.Equal(real.Base, deferred.GetWeaponBaseDamage(Unit));
            Assert.Equal(real.Dps, deferred.GetWeaponDps(Unit));
            Assert.Equal(real.Interval, deferred.GetWeaponAttackIntervalSeconds(Unit));
            Assert.Equal(real.WeaponSchool, deferred.GetWeaponSchool(Unit));

            real.Interval = null;
            real.WeaponSchool = null;
            Assert.Null(deferred.GetWeaponAttackIntervalSeconds(Unit));
            Assert.Null(deferred.GetWeaponSchool(Unit));
        }

        [Fact]
        public void DeferredEffectExtension_BeforeBind_DeclinesWithoutThrowing_AfterBind_DelegatesAndPropagatesAnswer()
        {
            var deferred = new DeferredEffectExtension();
            var context = MakeContext();

            // 未绑定：不处理（返回 false），调用方继续走内置原语分发。
            Assert.False(deferred.TryHandle(context, out _));

            var real = new HandlingExtension { Handles = true };
            deferred.Bind(real);

            Assert.True(deferred.TryHandle(context, out var result));
            Assert.Equal(1, real.Calls);
            Assert.Equal(HitResult.Hit, result.Hit);

            // 真实扩展拒绝处理时，转发器也返回 false（不把"拒绝"吞成"已处理"）。
            real.Handles = false;
            Assert.False(deferred.TryHandle(context, out _));
            Assert.Equal(2, real.Calls);
        }
    }
}
