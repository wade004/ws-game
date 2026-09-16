using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;
using Core.Sim;
using Xunit;

namespace Tests.Sim
{
    /// <summary>T-N6-4：<see cref="AnchorCreatureLevelScaler"/> 的验收测试——见任务书验收点
    /// "L1 模板按 L10 出生后血量 == DPS(10)×TTK(10)×分档倍率（容差 1e-6）"。</summary>
    public class AnchorCreatureLevelScalerTests
    {
        /// <summary>算法本身的精确性验证：喂给 <see cref="AnchorCreatureLevelScaler.ScaleBaseStats"/>
        /// 的血量槽位输入值取 <c>anchor(1).Dps × anchor(1).TtkSeconds</c> 的精确值（不经四舍五入，
        /// 与嵌入数据集 <c>creature.sim_wolf_l1.base_stats.stat.stamina</c> 为方便阅读四舍五入到一位
        /// 小数（T-N6-4b 起为 119.4）不同，见 <c>core/sim/tests/data/README.md</c>"T-N6-4b 调参
        /// 记录"一节）——据此验证换算公式本身逐位精确（容差 1e-6），不掺入数据集四舍五入带来的误差
        /// 来源。</summary>
        [Fact]
        public void ScaleBaseStats_HealthStat_ExactRatio_MatchesDpsTimesTtk()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 1);
            var anchors = world.AnchorTable!;
            var scaler = new AnchorCreatureLevelScaler(world.Registry, anchors);
            var template = world.Gameplay.Carriers.Creatures.Get(SimTestWorldFactory.EmbeddedCreatureWolfL1);

            var anchor1 = anchors.Get(1);
            var anchor10 = anchors.Get(10);
            var exactHealthAt1 = anchor1.Dps * anchor1.TtkSeconds;

            var baseStats = new Dictionary<Id, double>
            {
                [new Id("stat.stamina")] = exactHealthAt1,
                [new Id("stat.strength")] = 12,
            };

            var scaled = scaler.ScaleBaseStats(template, templateLevel: 1, targetLevel: 10, baseStats);

            var expectedHealthAt10 = anchor10.Dps * anchor10.TtkSeconds;
            Assert.Equal(expectedHealthAt10, scaled[new Id("stat.stamina")], precision: 6);
        }

        /// <summary>装配根接线的端到端验证：<c>CreatureFactory.Spawn(...,level:10)</c> 对
        /// <c>creature.sim_wolf_l1</c>（登记等级 1）按等级 10 出生后，实际生命上限
        /// （<c>IPowerHost.GetPowerMax</c>）应等于模板登记的 <c>stat.stamina</c>（T-N6-4b 起为
        /// 119.4，含数据集四舍五入）乘以 <c>ScaleBaseStats_HealthStat_ExactRatio_MatchesDpsTimesTtk</c>
        /// 同一条锚点比值×分档倍率（<c>creature.tier.sim_normal.stat_multiplier</c>=1.0）——验证的是
        /// "装配根→CreatureFactory.LevelScaler→StatHost→PowerHost"这条链路确实接通、确实用了锚点表
        /// 算出的比值，不是"公式本身在孤立环境下精确"这一点（上一测试已覆盖），因此以数据集实际
        /// 登记的 119.4 为基准，同样容差 1e-6（两个数都是精确的 double 乘法，无新增舍入来源）。</summary>
        [Fact]
        public void Spawn_AtOverriddenLevel_WiresAnchorScalerThroughToPowerHost()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 2);
            var anchors = world.AnchorTable!;

            var creatureId = world.Gameplay.Carriers.Creatures.Spawn(
                SimTestWorldFactory.EmbeddedCreatureWolfL1, SimTestWorldFactory.EmbeddedMapId,
                new Vec2(50, 0), facing: 0, ownerId: null, level: 10);

            var actualMaxHealth = world.Gameplay.Carriers.Rules.Powers.GetPowerMax(creatureId, WellKnownPowers.Health);

            var anchor1 = anchors.Get(1);
            var anchor10 = anchors.Get(10);
            var ratio = (anchor10.Dps * anchor10.TtkSeconds) / (anchor1.Dps * anchor1.TtkSeconds);
            const double registeredL1Stamina = 119.4;
            var expected = registeredL1Stamina * ratio; // tier_multiplier(sim_normal) == 1.0

            Assert.Equal(expected, actualMaxHealth, precision: 6);
        }

        /// <summary>未覆盖等级（<c>level == template.Level</c>）时不触碰缩放器，属性与模板原值一致——
        /// <see cref="ICreatureLevelScaler"/> 类型判断记录"等级改、属性不变"仅在缩放器为 null 时适用；
        /// 本测试验证的是另一个更基础的前提：spawnLevel == template.Level 时
        /// <c>CreatureFactory.ResolveBaseStats</c> 压根不会调用缩放器（见该方法源码），本测试确保
        /// 这条路径不受"是否装配了锚点缩放器"影响——5 参 Spawn（不传 level）与 6 参 Spawn 传入等于
        /// 模板登记等级的值，两者应产出完全相同的生命上限。</summary>
        [Fact]
        public void Spawn_AtTemplateLevel_UnaffectedByScaler()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 3);

            var idViaFiveArg = world.Gameplay.Carriers.Creatures.Spawn(
                SimTestWorldFactory.EmbeddedCreatureWolfL1, SimTestWorldFactory.EmbeddedMapId, new Vec2(60, 0), 0, null);
            var idViaSixArgSameLevel = world.Gameplay.Carriers.Creatures.Spawn(
                SimTestWorldFactory.EmbeddedCreatureWolfL1, SimTestWorldFactory.EmbeddedMapId, new Vec2(70, 0), 0, null, level: 1);

            var maxA = world.Gameplay.Carriers.Rules.Powers.GetPowerMax(idViaFiveArg, WellKnownPowers.Health);
            var maxB = world.Gameplay.Carriers.Rules.Powers.GetPowerMax(idViaSixArgSameLevel, WellKnownPowers.Health);

            Assert.Equal(maxA, maxB, precision: 9);
            Assert.Equal(119.4, maxA, precision: 9);
        }

        /// <summary>越界夹取：目标等级超过 <c>AnchorTable.MaxLevel</c> 时按该类型判断记录"越界夹到
        /// MaxLevel"处理，不抛异常——生成一只名义等级 30 的生物应得到与等级 25 完全相同的缩放比值。
        /// T-N6-4b 判断记录：<c>MaxLevel</c> 从 20 扩到 25（见 <c>core/sim/tests/data/README.md</c>
        /// "T-N6-4b 根因排查"——越级矩阵允许玩家 20 级时对手偏移 +5，生物出生等级达到 25，若锚点表
        /// 只到 20 会让 21～25 级全部夹到同一强度，本测试的"越界夹取"边界本身也要跟着从 25 移到
        /// 30，否则测的就不再是真正的越界。</summary>
        [Fact]
        public void Spawn_AboveMaxAnchorLevel_ClampsToMaxLevel()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 4);

            var idAt25 = world.Gameplay.Carriers.Creatures.Spawn(
                SimTestWorldFactory.EmbeddedCreatureWolfL1, SimTestWorldFactory.EmbeddedMapId, new Vec2(80, 0), 0, null, level: 25);
            var idAt30 = world.Gameplay.Carriers.Creatures.Spawn(
                SimTestWorldFactory.EmbeddedCreatureWolfL1, SimTestWorldFactory.EmbeddedMapId, new Vec2(90, 0), 0, null, level: 30);

            var maxAt25 = world.Gameplay.Carriers.Rules.Powers.GetPowerMax(idAt25, WellKnownPowers.Health);
            var maxAt30 = world.Gameplay.Carriers.Rules.Powers.GetPowerMax(idAt30, WellKnownPowers.Health);

            Assert.Equal(maxAt25, maxAt30, precision: 9);
        }
    }
}
