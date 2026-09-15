using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Ai;
using Core.Sim;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// T-N6-3a（ADR-0035 决策 2）：<see cref="StandardPlayerBuilder"/> 在嵌入式最小仿真数据集
    /// （<c>core/sim/tests/data</c>）上的验收——L5/L15 两组：全部槽位有装备、物品等级与 E(L) 对齐、
    /// 装备贡献 == 反解向量（容差 1e-6）、已学技能 == 技能书 ≤L 的集合、能按优先级表对一只同级普通怪
    /// 施放并击杀。
    /// </summary>
    public sealed class StandardPlayerBuilderTests
    {
        private static readonly Id QualityCommon = new Id("item.quality.sim_common");

        private static void AssertLevel(int level, ulong seed)
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed, playerLevel: level);
            var player = StandardPlayerBuilder.Build(world, SimTestWorldFactory.EmbeddedClassId, level, QualityCommon);

            // 全部装备位有装备：item.slot_definition 里 is_equipment != false 的槽位共 5 个
            // （主手+头/胸/腿/脚，见 core/sim/tests/data/README.md 数据集清单）。
            var equipmentSlots = world.Registry.GetAll("item.slot_definition")
                .Count(s => !s.TryGetBool("is_equipment", out var eq) || eq);
            Assert.Equal(equipmentSlots, player.EquippedInstances.Count);

            // 物品等级 = E(L)：嵌入数据集在 L=5/15 两级上 expected_item_level 恰好落在既有档位
            // （1/5/10/15/20 阶梯，见 README"锚点推导"手算表），本断言只对这两个具体等级成立。
            var anchorRow = world.AnchorTable!.Get(level);
            var expectedItemLevel = (int)Math.Round(anchorRow.ExpectedItemLevel);
            // 判断记录：Equip 会把物品实例从背包（InventoryHost）移入装备槽（EquipmentHost 自己的
            // _equipped 索引）——装备后 InventoryHost.FindInstance 不再能找到它（已被移出背包），须改用
            // EquipmentHost.GetAllEquippedInstances 读回穿在身上的实例。
            var equippedInstances = world.Gameplay.Carriers.Equipment.GetAllEquippedInstances(player.UnitId);
            foreach (var (slotId, instanceId) in player.EquippedInstances)
            {
                Assert.True(equippedInstances.TryGetValue(slotId, out var instance), $"槽位 {slotId} 应已装备");
                Assert.Equal(instanceId, instance.InstanceId);
                var template = world.Registry.Get("item.template", instance.TemplateId);
                Assert.NotNull(template);
                Assert.Equal(expectedItemLevel, (int)template!.GetInt("item_level"));
            }

            // 装备贡献 == 反解向量：StatHost 里来源为该实例 id 的全部 Flat 修正之和，应与
            // StandardPlayerBuilder 独立算出的反解向量逐位相等（容差 1e-6）。
            foreach (var (slotId, instanceId) in player.EquippedInstances)
            {
                var expected = player.ExpectedEquipmentContributionBySlot[slotId];
                foreach (var kv in expected)
                {
                    var modifiers = world.Gameplay.Carriers.Rules.Stats.GetModifiers(player.UnitId, kv.Key);
                    var actual = modifiers
                        .Where(m => m.SourceId.Equals(instanceId) && m.Op == Core.Numbers.StatBlock.StatModifierOp.Flat)
                        .Sum(m => m.Value);
                    Assert.True(Math.Abs(actual - kv.Value) < 1e-6,
                        $"槽位 {slotId} 属性 {kv.Key}：期望反解值 {kv.Value}，实际装备贡献 {actual}");
                }
            }

            // 已学技能 == 技能书 <= L 的集合。
            var bookRecord = world.Registry.Get("skill.book", SimTestWorldFactory.EmbeddedSkillBookId);
            Assert.NotNull(bookRecord);
            var expectedSkills = bookRecord!.GetArray("entries")
                .OfType<Core.Foundation.Common.Json.JsonObject>()
                .Where(e => e.TryGetValue("level", out var lvlRaw) && lvlRaw is Core.Foundation.Common.Json.JsonNumber lvlNum && lvlNum.Value <= level)
                .Select(e => ((Core.Foundation.Common.Json.JsonString)e["skill_id"]).Value)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
            var actualSkills = player.KnownSkills.Select(s => s.Value).OrderBy(s => s, StringComparer.Ordinal).ToList();
            Assert.Equal(expectedSkills, actualSkills);

            // 能按优先级表对一只同级普通怪施放并击杀。
            var creatureId = new Id($"creature.sim_wolf_l{level}");
            var beastId = world.Gameplay.Carriers.Creatures.Spawn(creatureId, world.Registry.GetAll("world.map")[0].GetId("id"),
                new Core.Foundation.Common.Vec2(3, 0), facing: 0);
            var beastPos = world.Gameplay.Carriers.Units.GetPosition(beastId);
            world.Spatial.Register(beastId, beastPos, 0.5);

            var rotationEvaluator = new RotationEvaluator(
                world.Registry, world.Gameplay.Carriers.Rules.Skill, world.Gameplay.Carriers.Rules.ExprHostFactory,
                world.Gameplay.Carriers.Rules.ExprSchema);

            var died = false;
            for (var i = 0; i < 400 && !died; i++)
            {
                if (!(world.Gameplay.Carriers.Units.Exists(beastId) && world.Gameplay.Carriers.Units.IsAlive(beastId)))
                {
                    died = true;
                    break;
                }

                var request = rotationEvaluator.Evaluate(player.UnitId, player.RotationId, beastId);
                if (request.HasValue)
                {
                    world.Gameplay.Carriers.Rules.Skill.CastSkill(request.Value.CasterId, request.Value.SkillId, request.Value.Targets);
                }
                world.Clock.Advance(SimTestWorldFactory.StepSeconds);
            }

            if (!died)
            {
                died = !(world.Gameplay.Carriers.Units.Exists(beastId) && world.Gameplay.Carriers.Units.IsAlive(beastId));
            }
            Assert.True(died, $"L{level} 标准玩家应能在 400 次机会内按优先级表击杀同级普通怪");
            Assert.True(world.Gameplay.Carriers.Units.Exists(player.UnitId) && world.Gameplay.Carriers.Units.IsAlive(player.UnitId),
                "标准玩家在战斗结束时应仍存活");
        }

        [Fact]
        public void Build_LevelFive_AllAssertionsHold()
        {
            AssertLevel(5, seed: 20260916200UL);
        }

        [Fact]
        public void Build_LevelFifteen_AllAssertionsHold()
        {
            AssertLevel(15, seed: 20260916201UL);
        }

        [Fact]
        public void Build_LevelMismatch_ThrowsArgumentException()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 20260916202UL, playerLevel: 5);

            Assert.Throws<ArgumentException>(() =>
                StandardPlayerBuilder.Build(world, SimTestWorldFactory.EmbeddedClassId, 10, QualityCommon));
        }
    }
}
