using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Core.Sim;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// 2026-09-16 深度复审 B 报告测试覆盖缺口 2（复审整合项 3）：<c>core/carriers/item/tests
    /// /T_N2_6_WeaponDpsDeviationTests.cs</c> 与 <c>core/gameplay/loot/tests
    /// /T_N6_3b_GoldMultiplierLootTests.cs</c> 等既有用例全部保证"装备时使用的品质 == 模板自身登记
    /// 的默认品质"，<c>core/sim/core/StandardPlayerBuilder.cs</c>.<c>FindCarrierTemplate</c> 同样
    /// 天然规避了这一场景（只挑 <c>quality</c> 字段恰好等于目标品质的模板）——review_B.md 测试覆盖
    /// 缺口 2 指出"N6 数值仿真锚点校准的零告警不能作为 GetWeaponDps/武器秒伤路径正确性的证据"，
    /// 建议仿真侧也补一条"故意品质骰漂移"的对照场景。
    /// <para>
    /// 本文件在仿真侧（嵌入式最小数据集 <c>core/sim/tests/data</c>）补上这一缺口：标准玩家经真实
    /// <c>InventoryHost.AddItem(..., qualityId, ...)</c> + <c>EquipmentHost.Equip</c> 换上一件
    /// 品质骰结果偏离模板默认品质（<c>item.sim_main_hand_l1_common</c> 模板默认
    /// <c>item.quality.sim_common</c>，本用例改骰 <c>item.quality.sim_rare</c>）的主手武器，断言
    /// <c>EquipmentHost.GetWeaponDps</c> 与新增测试专用技能 <c>skill.sim_review_b_weapon_pct_strike</c>
    /// （<c>weapon_damage_pct</c> 效果，见 <c>core/sim/tests/data/skill/skill.def.json</c> 该行
    /// 判断记录）一场战斗的累计伤害，均按 <c>item.quality_definition.budget_multiplier</c> 品质
    /// 倍率变化——对照默认品质那场。
    /// </para>
    /// <para>
    /// 判断记录（专用技能 + 专用 <c>ai.rotation</c>，不复用 <c>ai.rotation.sim_warrior</c>）：本数据
    /// 集既有 <c>skill.sim_warrior_*</c> 系列没有一条使用 <c>weapon_damage_pct</c> 效果（全部是
    /// <c>school_damage</c>，按 <c>stat.attack_power</c> 缩放，与武器品质无关）。新增
    /// <c>skill.sim_review_b_weapon_pct_strike</c>（<c>cast_time:0</c>/<c>respects_gcd:false</c>，
    /// 同 <c>skill.sim_warrior_strike</c> 惯例，逐 tick 连续施放）与专用
    /// <c>ai.rotation.sim_review_b_weapon_pct</c>（单条目、恒真条件）仅供本文件使用：不进
    /// <c>skill.book</c>、不是任何 <c>arch.class</c> 的默认优先级表，经
    /// <see cref="StandardPlayerBuilder.Build"/> 的 <c>rotationId</c> 参数显式指定，不影响其它既有
    /// 用例/三份仿真基线（同 <c>skill.def.sim_probe_overbudget</c> 既有"不进 book/rotation"先例）。
    /// </para>
    /// <para>
    /// 判断记录（如何在一场战斗内验证伤害按品质倍率线性变化，而不受"击杀提前结束战斗"干扰）：两组
    /// 世界（默认品质/骰出品质）使用完全相同的种子与 <c>maxTicks</c>（较小，仅 6 tick=3 秒），命中/
    /// 暴击等战斗判定的 RNG 流与武器品质无关（<c>InventoryHost.AddItem</c>/<c>EquipmentHost.Equip</c>
    /// 不消费 <c>IRngHost</c>），两组的施放次数与命中序列因此逐 tick 完全一致，唯一差异是
    /// <c>weapon_damage_pct</c> 结算时读到的 <see cref="Core.Rules.Common.IWeaponDamageQuery.GetWeaponDps"/>
    /// 数值——只要两场都以 <see cref="FightOutcome.Timeout"/> 收尾（生物全程未被打死，断言明确核对
    /// 这一点，而不是假定），总伤害之比就必然等于武器秒伤之比。生物（<c>creature.sim_wolf_l1</c>，
    /// <c>stat.stamina</c>=119.4）在 6 tick 内不可能被以下量级的伤害打死：单次
    /// <c>weapon_damage_pct</c> 结算 = 秒伤(5 或 7) × 一拍常数(缺省 1.0) × pct(1.0)，6 次累计上限
    /// 42，远低于生物有效血量。
    /// </para>
    /// </summary>
    public sealed class T_ReviewB_Gap2_WeaponQualityDriftBattleDamageTests
    {
        private static readonly Id WeaponTemplateId = new Id("item.sim_main_hand_l1_common");
        private static readonly Id WeaponSlotId = new Id("item.slot.sim_main_hand");
        private static readonly Id DefaultQualityId = new Id("item.quality.sim_common");
        private static readonly Id RolledQualityId = new Id("item.quality.sim_rare");
        private static readonly Id ReviewB2RotationId = new Id("ai.rotation.sim_review_b_weapon_pct");
        private static readonly Id ReviewB2SkillId = new Id("skill.sim_review_b_weapon_pct_strike");

        // item.quality_definition.json：sim_common budget_multiplier=1.0，sim_rare=1.4
        // （core/sim/tests/data/item/item.quality_definition.json）。
        private const double ExpectedQualityMultiplierRatio = 1.4;

        private const int MaxTicks = 6;

        private sealed class FightSetup
        {
            public HeadlessWorld World = null!;
            public FightRunner.FightAccumulator Accumulator = null!;
            public Id CreatureId;
        }

        /// <summary>装配一份嵌入式最小仿真数据集世界 + 标准玩家（专用 rotation），<paramref
        /// name="rolledWeaponQuality"/> 非空时额外经真实 <c>InventoryHost.AddItem</c> +
        /// <c>EquipmentHost.Equip</c> 把主手武器换成该品质的新实例（模板不变，仍是
        /// <see cref="WeaponTemplateId"/>，只有品质骰结果偏离模板自身默认品质）。</summary>
        private static FightSetup BuildSetup(ulong seed, Id? rolledWeaponQuality)
        {
            var accumulator = new FightRunner.FightAccumulator();
            var dataSources = SimTestWorldFactory.BuildEmbeddedDataSources();

            var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = dataSources,
                Seed = seed,
                MapId = SimTestWorldFactory.EmbeddedMapId,
                PlayerId = SimTestWorldFactory.PlayerId,
                PlayerFactionId = SimTestWorldFactory.FactionPlayer,
                PlayerClassId = SimTestWorldFactory.EmbeddedClassId,
                PlayerLevel = 1,
                GameId = SimTestWorldFactory.EmbeddedGameId,
                StepSeconds = SimTestWorldFactory.StepSeconds,
                FailOnUnknownTable = false,
                CombatOptions = accumulator.CombatOptions,
            });

            StandardPlayerBuilder.Build(
                world, SimTestWorldFactory.EmbeddedClassId, 1, DefaultQualityId,
                rotationId: ReviewB2RotationId);

            if (rolledWeaponQuality.HasValue)
            {
                var inventory = world.Gameplay.Carriers.Inventory;
                var equipment = world.Gameplay.Carriers.Equipment;

                var added = inventory.AddItem(
                    SimTestWorldFactory.PlayerId, WeaponTemplateId, 1, rolledWeaponQuality.Value, Array.Empty<Id>());
                Assert.True(added, "掉落骰出的新武器实例应能成功加入背包");

                var rolledInstance = inventory.ListItems(SimTestWorldFactory.PlayerId)
                    .Single(i => i.TemplateId.Equals(WeaponTemplateId) && i.Quality.Equals(rolledWeaponQuality.Value));

                var equipResult = equipment.Equip(SimTestWorldFactory.PlayerId, rolledInstance.InstanceId, WeaponSlotId);
                Assert.True(equipResult.Success, "换装骰出品质的武器应能成功装备（自动带出模板默认品质的旧武器）");
            }

            var creatureSpawnPos = new Vec2(3, 0);
            var creatureId = world.Gameplay.Carriers.Creatures.Spawn(
                SimTestWorldFactory.EmbeddedCreatureWolfL1, SimTestWorldFactory.EmbeddedMapId,
                creatureSpawnPos, facing: Math.PI, ownerId: null, level: 1);
            world.Spatial.Register(creatureId, creatureSpawnPos, 0.5);

            return new FightSetup { World = world, Accumulator = accumulator, CreatureId = creatureId };
        }

        [Fact]
        public void RolledQualityDeviatesFromTemplateDefault_GetWeaponDpsAndBattleDamageScaleByQualityMultiplier()
        {
            const ulong seed = 20260916_9001UL;

            var defaultSetup = BuildSetup(seed, rolledWeaponQuality: null);
            var defaultDps = defaultSetup.World.Gameplay.Carriers.Equipment.GetWeaponDps(SimTestWorldFactory.PlayerId);
            var defaultResult = FightRunner.RunWithinWorld(
                defaultSetup.World, defaultSetup.Accumulator, SimTestWorldFactory.PlayerId, defaultSetup.CreatureId,
                ReviewB2RotationId, SimTestWorldFactory.StepSeconds, MaxTicks,
                SimpleMoveModel.DefaultMoveSpeed, maxResourceCurveSamples: 8);

            var rolledSetup = BuildSetup(seed, rolledWeaponQuality: RolledQualityId);
            var rolledDps = rolledSetup.World.Gameplay.Carriers.Equipment.GetWeaponDps(SimTestWorldFactory.PlayerId);
            var rolledResult = FightRunner.RunWithinWorld(
                rolledSetup.World, rolledSetup.Accumulator, SimTestWorldFactory.PlayerId, rolledSetup.CreatureId,
                ReviewB2RotationId, SimTestWorldFactory.StepSeconds, MaxTicks,
                SimpleMoveModel.DefaultMoveSpeed, maxResourceCurveSamples: 8);

            // 两场都须以 Timeout 收尾（生物全程未被打死）——否则"施放次数/命中序列逐 tick 完全一致"
            // 这一前提不成立，下方的线性比例断言就不再可靠（见类型判断记录）。
            Assert.Equal(FightOutcome.Timeout, defaultResult.Outcome);
            Assert.Equal(FightOutcome.Timeout, rolledResult.Outcome);

            // GetWeaponDps：品质预算倍率 1.4 倍（其余因子——曲线值、槽位系数——两组完全相同）。
            Assert.True(defaultDps > 0.0, "默认品质武器秒伤应 > 0（否则下方比例断言退化）");
            Assert.Equal(defaultDps * ExpectedQualityMultiplierRatio, rolledDps, 9);

            // 一场战斗的累计伤害：两组施放次数/命中序列相同（同种子、装备操作不消费 RNG），weapon_damage_pct
            // 每次结算金额与 GetWeaponDps 成正比，总伤害之比因此也应等于同一品质倍率。
            Assert.True(defaultResult.PlayerTotalDamage > 0.0, "默认品质那场应至少命中一次，否则下方比例断言退化");
            defaultResult.PlayerSkillDamageShare.TryGetValue(ReviewB2SkillId, out var defaultShare);
            rolledResult.PlayerSkillDamageShare.TryGetValue(ReviewB2SkillId, out var rolledShare);
            Assert.True(defaultShare > 0.0, "专用技能应在默认品质那场贡献伤害占比");
            Assert.Equal(1.0, defaultShare, 9); // 本场景玩家仅有这一条技能可释放，占比恒为 1。
            Assert.Equal(1.0, rolledShare, 9);

            Assert.Equal(
                defaultResult.PlayerTotalDamage * ExpectedQualityMultiplierRatio,
                rolledResult.PlayerTotalDamage,
                6);
        }
    }
}
