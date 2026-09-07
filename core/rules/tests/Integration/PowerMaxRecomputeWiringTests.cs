using Core.Foundation.Common;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Rules.Integration
{
    /// <summary>
    /// RC-06（见外部审计 architecture/落地计划/audit-b3b91ee-20260907/code-review.md RC-06）：
    /// <see cref="Core.Numbers.PowerSet.PowerHost.RecomputeMax"/>（派生上限）与
    /// <see cref="Core.Numbers.StatBlock.StatHost.RecomputeRatingStats"/>（评级换算属性缓存）此前
    /// 都只在注册时算一次或要求调用方显式手动调用——本测试验证 <see cref="Core.Rules.Assembly.RulesAssembly"/>
    /// 的生产装配是否真的把它们接到了 <c>stat.changed</c>/<c>progression.level_up</c> 事件上（见该
    /// 类型构造函数"RC-06 收边补齐"判断记录），而不是要求游戏层自己记得手动调用。
    /// </summary>
    public sealed class PowerMaxRecomputeWiringTests
    {
        [Fact]
        public void StatChanged_AutomaticallyRecomputesStatSourcedPowerMax_WithoutManualRecomputeMaxCall()
        {
            var fx = FightWorldBuilder.Build();
            var unit = new Id("unit.rc06_power_max");
            var source = new Id("item.rc06_test_source");

            fx.Rules.Stats.RegisterUnit(unit);
            fx.Rules.Stats.SetBase(unit, FightWorldBuilder.StatArmor, 100);
            fx.Rules.Powers.RegisterUnit(unit, new[] { FightWorldBuilder.PowerShield });

            Assert.Equal(100, fx.Rules.Powers.GetPowerMax(unit, FightWorldBuilder.PowerShield));

            // 模拟装备/光环给 stat.armor 加值（真实生产路径：EquipmentHost.ApplyGrants/AuraHost 的
            // 光环属性修正都经同一个 IStatHost.AddModifier 入口）——只调用 StatHost，完全不触碰
            // PowerHost，验证的正是"生产装配本身"是否会自动联动，而不是调用方手动串起两者。
            fx.Rules.Stats.AddModifier(unit, new StatModifier(FightWorldBuilder.StatArmor, StatModifierOp.Flat, 50, source));
            fx.Bus.DispatchPending();

            // 修复前：PowerHost.RecomputeMax 从未被自动调用，GetPowerMax 会一直停留在注册时算出的
            // 100，即便 stat.armor 已经变成 150（见外部审计 RC-06"上限只在注册或显式 RecomputeMax
            // 读取属性"）。
            Assert.Equal(150, fx.Rules.Powers.GetPowerMax(unit, FightWorldBuilder.PowerShield));

            // 移除来源（对应卸装备/光环消失）同样应该自动联动降回原值。
            fx.Rules.Stats.RemoveModifiersBySource(unit, source);
            fx.Bus.DispatchPending();
            Assert.Equal(100, fx.Rules.Powers.GetPowerMax(unit, FightWorldBuilder.PowerShield));
        }

        // 评级换算属性（is_rating + rating_conversion_ref）的等级失效见
        // core/numbers/stat_block/tests/StatHostTests.cs
        // LevelUp_ProgressionEvent_RecomputesRatingConvertedStat_ViaRecomputeRatingStats——评级换算
        // 需要额外的 stat.rating_conversion 表与 StatHostOptions.EnableRatingConversion 开关，
        // StatHostTests.cs 已有现成夹具（BuildHost(enableRatingConversion:true, ...)），不重复
        // 在本文件另起一份 RulesAssembly 级别的等价数据。RulesAssembly 是否真的把
        // StatHost.RecomputeRatingStats 接到 progression.level_up 上，见
        // core/rules/assembly/RulesAssembly.cs 构造函数"RC-06 收边补齐"判断记录源码本身——两处
        // 订阅代码紧邻在一起，调用的正是 StatHostTests.cs 那条用例验证过的同一个方法。
    }
}
