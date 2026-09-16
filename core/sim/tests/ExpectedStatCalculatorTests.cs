using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Sim;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// T-N6-3a（ADR-0035 决策 2；数值总纲第 4.4 节）：<see cref="ExpectedStatCalculator"/> 在嵌入式最小
    /// 仿真数据集上的验收——<c>Compute(L)</c> 在 L=1/10/20 三级的数值与"手算"一致。手算按
    /// <c>core/sim/tests/data/README.md</c>"锚点推导"一节给出的职业成长闭式公式（<c>strength(L) = 12 +
    /// 3·(L-1)</c>/<c>stamina(L) = 150 + 50·(L-1)</c>/<c>attack_power(L) = strength(L)</c>）加上独立
    /// 调用 <see cref="IBudgetSolver.Solve"/>（与 <see cref="ExpectedStatCalculator"/> 内部使用同一份
    /// statMix 策略——<c>stat.weight</c> 全表按权重归一化，见该类型判断记录"Σ槽位 的槽位范围与
    /// statMix 来源"）算出的装备贡献相加，在测试里独立写一遍，不反射/调用 <see
    /// cref="ExpectedStatCalculator"/> 内部实现。
    /// </summary>
    public sealed class ExpectedStatCalculatorTests
    {
        private static readonly Id ClassId = SimTestWorldFactory.EmbeddedClassId;
        private static readonly Id QualityCommon = new Id("item.quality.sim_common");
        private static readonly Id BudgetCurveId = new Id("item.budget.default");
        private static readonly Id[] NonWeaponSlots =
        {
            new Id("item.slot.sim_head"),
            new Id("item.slot.sim_chest"),
            new Id("item.slot.sim_legs"),
            new Id("item.slot.sim_feet"),
        };
        private static readonly Id[] MixStats =
        {
            new Id("stat.strength"), new Id("stat.agility"), new Id("stat.intellect"), new Id("stat.stamina"),
            new Id("stat.attack_power"), new Id("stat.crit_rating"), new Id("stat.dodge_rating"),
            new Id("stat.hit_rating"), new Id("stat.armor"),
        };

        private static Dictionary<Id, double> HandComputeEquipmentFlat(Core.Sim.HeadlessWorld world, int level)
        {
            var itemLevel = (int)Math.Round(world.AnchorTable!.Get(level).ExpectedItemLevel);
            var solver = new BudgetSolver();
            var statMix = MixStats.Select(s => (Stat: s, Ratio: 1.0 / MixStats.Length)).ToList();

            var flat = new Dictionary<Id, double>();
            foreach (var slotId in NonWeaponSlots)
            {
                var solved = solver.Solve(itemLevel, QualityCommon, slotId, statMix, BudgetCurveId, world.Registry);
                foreach (var kv in solved.Values)
                {
                    flat.TryGetValue(kv.Key, out var existing);
                    flat[kv.Key] = existing + kv.Value;
                }
            }
            return flat;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(20)]
        public void Compute_MatchesHandCalculatedGrowthPlusEquipment(int level)
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 20260916300UL + (ulong)level, playerLevel: 1);
            var calculator = new ExpectedStatCalculator(
                world.Registry, world.AnchorTable!, ClassId, QualityCommon, new BudgetSolver(), BudgetCurveId);

            var computed = calculator.Compute(level);
            var flat = HandComputeEquipmentFlat(world, level);

            var handStrength = 12.0 + 3.0 * (level - 1) + flat.GetValueOrDefault(new Id("stat.strength"), 0.0);
            var handAgility = 6.0 + flat.GetValueOrDefault(new Id("stat.agility"), 0.0);
            var handIntellect = 4.0 + flat.GetValueOrDefault(new Id("stat.intellect"), 0.0);
            var handStamina = 150.0 + 50.0 * (level - 1) + flat.GetValueOrDefault(new Id("stat.stamina"), 0.0);
            // attack_power = strength 最终值（含其自身装备贡献，派生系数 1.0，见
            // arch.class.sim_warrior.derivation_overrides）× 1.0 + attack_power 自身在 statMix 里
            // 获得的装备贡献（派生属性的 flat 修正在其自身派生基础值之上直接叠加——同
            // Core.Numbers.StatBlock.StatHost.ComputeFinal "value = baseValue + flatSum" 的既有聚合
            // 顺序，ExpectedStatCalculator 按同一顺序实现）。
            var handAttackPower = handStrength + flat.GetValueOrDefault(new Id("stat.attack_power"), 0.0);

            Assert.True(Math.Abs(computed[new Id("stat.strength")] - handStrength) < 1e-6,
                $"L{level} strength: expected {handStrength}, got {computed[new Id("stat.strength")]}");
            Assert.True(Math.Abs(computed[new Id("stat.agility")] - handAgility) < 1e-6,
                $"L{level} agility: expected {handAgility}, got {computed[new Id("stat.agility")]}");
            Assert.True(Math.Abs(computed[new Id("stat.intellect")] - handIntellect) < 1e-6,
                $"L{level} intellect: expected {handIntellect}, got {computed[new Id("stat.intellect")]}");
            Assert.True(Math.Abs(computed[new Id("stat.stamina")] - handStamina) < 1e-6,
                $"L{level} stamina: expected {handStamina}, got {computed[new Id("stat.stamina")]}");
            Assert.True(Math.Abs(computed[new Id("stat.attack_power")] - handAttackPower) < 1e-6,
                $"L{level} attack_power: expected {handAttackPower}, got {computed[new Id("stat.attack_power")]}");
        }

        [Fact]
        public void Compute_CachesResultAcrossCalls()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 20260916399UL, playerLevel: 1);
            var calculator = new ExpectedStatCalculator(
                world.Registry, world.AnchorTable!, ClassId, QualityCommon, new BudgetSolver(), BudgetCurveId);

            var first = calculator.Compute(10);
            var second = calculator.Compute(10);

            Assert.Same(first, second);
        }

        [Fact]
        public void Compute_ArmorStatClampedToNonNegative()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 20260916398UL, playerLevel: 1);
            var calculator = new ExpectedStatCalculator(
                world.Registry, world.AnchorTable!, ClassId, QualityCommon, new BudgetSolver(), BudgetCurveId);

            var computed = calculator.Compute(1);

            Assert.True(computed[new Id("stat.armor")] >= 0.0);
        }
    }
}
