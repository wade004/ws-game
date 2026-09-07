using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>N05（外部审计 68c9bed，P1）：来源单位销毁（Despawn）后，仍按该来源结算的周期效果
    /// （典型场景：DOT 光环的施法者提前死亡/离开世界，光环本身按 06 第 3.3 节继续在目标身上生效，
    /// 见 <c>core/rules/skill/core/AuraHost</c> 类注释"来源单位被销毁不代表已施加到其他目标身上的
    /// 光环应当消失"）不应再让 <see cref="Resolver.ComputeMitigation"/> 里无条件的
    /// <c>IUnitAccess.GetLevel(sourceId)</c> 命中 <c>WorldUnitAccess.Require</c> 抛
    /// <see cref="System.InvalidOperationException"/> 而中断整条结算。</summary>
    public sealed class MitigationSourceDespawnTests
    {
        private static readonly Id Source = new Id("unit.mitigation_source");
        private static readonly Id Target = new Id("unit.mitigation_target");
        private static readonly Id SkillId = new Id("skill.mitigation_dot");

        private static EffectContext PeriodicDamageContext(double baseValue = 100) =>
            new EffectContext(Source, Target, SkillId, EffectKind.SchoolDamage, CombatTestSupport.SchoolPhysical,
                baseValue, coefficient: 1.0, isPeriodic: true, canCrit: false, canMiss: false);

        [Fact]
        public void Resolve_SourceDespawned_DoesNotThrow_MitigationDegradesToTargetLevel()
        {
            // combat.hit_table.default 全分支关闭（恒 Hit，不 crit），combat.resist.physical_test
            // 的护甲曲线 reduction = armor / (armor + k*attackerLevel)，见 CombatTestSupport 手算注释：
            // armor=300、k=70。来源已被 Despawn，修复后应退化为使用目标自身等级（此处两者都设为 10，
            // 与"手算全链"测试的 attackerLevel=10 保持同一数值，便于交叉核对 reduction=0.3）。
            var fx = CombatTestSupport.Build();
            CombatTestSupport.RegisterUnit(fx, Source, CombatTestSupport.FactionParty, level: 10);
            CombatTestSupport.RegisterUnit(fx, Target, CombatTestSupport.FactionHorde, level: 10);
            fx.Stats.SetBase(Target, CombatTestSupport.StatArmor, 300);

            fx.Units.Despawn(Source);

            var context = PeriodicDamageContext(baseValue: 100);

            // 修复前：本调用直接抛 InvalidOperationException（FakeUnitAccess.Require 对齐真实
            // WorldUnitAccess.Require 语义后复现该缺口），周期效果整条结算中断。
            var result = fx.Host.ResolveEffect(context);

            Assert.Equal(HitResult.Hit, result.Hit);
            // base 100 * (1 - 0.3 reduction) = 70。
            Assert.Equal(70.0, result.FinalAmount, 6);
            Assert.Equal(1000.0 - 70.0, fx.Powers.GetPower(Target, WellKnownPowers.Health), 6);
            Assert.Contains(result.Steps!, s => s.Contains("已不存在于世界模拟"));
        }

        [Fact]
        public void Resolve_SourceDespawned_TargetAlsoMissing_DoesNotThrow()
        {
            // 极端情形：来源与目标都已从 IUnitAccess 查不到（如两者都在同一批批量清理中消失，
            // 事件仍在队列里派发）。目标 Despawn 后 IsAlive 恒假，命中 Resolve 顶部"目标已死亡→
            // Miss"的既有短路分支（见 Resolve 判断记录），不会真正走到 ComputeMitigation——
            // 本用例只确认这条组合路径同样不抛异常（Resolver.ResolveAttackerLevelForMitigation
            // 的等级 1 兜底分支只在"目标存在但来源不存在"且目标恰好在 ComputeMitigation 之前又变成
            // 不存在这一理论组合下才会命中，当前 Resolve 顶层短路已覆盖了更常见的"目标也消失"
            // 情形，故此处只断言不崩溃，不逐位核对数值）。
            var fx = CombatTestSupport.Build();
            CombatTestSupport.RegisterUnit(fx, Source, CombatTestSupport.FactionParty, level: 10);
            CombatTestSupport.RegisterUnit(fx, Target, CombatTestSupport.FactionHorde, level: 10);
            fx.Stats.SetBase(Target, CombatTestSupport.StatArmor, 300);

            fx.Units.Despawn(Source);
            fx.Units.Despawn(Target);

            var context = PeriodicDamageContext(baseValue: 100);

            var exception = Record.Exception(() => fx.Host.ResolveEffect(context));
            Assert.Null(exception);
        }
    }
}
