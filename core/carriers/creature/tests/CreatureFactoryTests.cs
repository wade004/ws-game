using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Numbers.PowerSet;
using Core.Numbers.Progression;
using Core.Numbers.StatBlock;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Creature
{
    public class CreatureFactoryTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id BasicTemplateId = new Id("creature.sample_basic");
        private static readonly Id EliteTemplateId = new Id("creature.sample_elite");
        private static readonly Id BeastTemplateId = new Id("creature.sample_beast");
        private static readonly Id HydraTemplateId = new Id("creature.sample_hydra");
        private static readonly Id HydraCubTemplateId = new Id("creature.sample_hydra_cub");
        private static readonly Id PowerStat = new Id("stat.power");
        private static readonly Id MaxHealthStat = new Id("stat.max_health");
        // AddXp 的 sourceId 只用于事件携带，不要求在 prog.xp_source 表里登记，任意 Id 均可。
        private static readonly Id TestXpSourceId = new Id("prog.xp.test_source");

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public IEventBus Bus = null!;
            public WorldUnitAccess Units = null!;
            public StatHost Stats = null!;
            public PowerHost Powers = null!;
            public ProgressionHost Progression = null!;
            public CreatureFactory Factory = null!;
            public List<(Id unitId, Id profileId, Vec2 spawnPoint, Id? rotationId)> AiCalls = null!;
            public List<CreatureSpawnedEvent> SpawnedEvents = null!;
            public List<CreatureDespawnedEvent> DespawnedEvents = null!;
        }

        private static Fixture Build(CreatureOptions? options = null)
        {
            var bus = CreatureTestSupport.CreateBus();
            var registry = CreatureTestSupport.MakeRegistry(bus);
            var world = new WorldSim(bus);
            var units = new WorldUnitAccess(world);
            var stats = CreatureTestSupport.MakeStatHost(registry, bus);
            var powers = CreatureTestSupport.MakePowerHost(registry, bus, stats);
            var progression = CreatureTestSupport.MakeProgressionHost(registry, bus, stats);

            var aiCalls = new List<(Id, Id, Vec2, Id?)>();
            AiRegistrar registrar = (unitId, profileId, spawnPoint, rotationId) =>
                aiCalls.Add((unitId, profileId, spawnPoint, rotationId));

            var factory = new CreatureFactory(registry, world, bus, stats, powers, progression, units, registrar, options);

            var spawned = new List<CreatureSpawnedEvent>();
            var despawned = new List<CreatureDespawnedEvent>();
            bus.Subscribe<CreatureSpawnedEvent>(CarriersEventKeys.CreatureSpawned, e => spawned.Add(e));
            bus.Subscribe<CreatureDespawnedEvent>(CarriersEventKeys.CreatureDespawned, e => despawned.Add(e));

            return new Fixture
            {
                World = world,
                Bus = bus,
                Units = units,
                Stats = stats,
                Powers = powers,
                Progression = progression,
                Factory = factory,
                AiCalls = aiCalls,
                SpawnedEvents = spawned,
                DespawnedEvents = despawned,
            };
        }

        [Fact]
        public void Spawn_CreatesEntity_WithCreatureKindAndFaction()
        {
            var f = Build();

            var id = f.Factory.Spawn(BasicTemplateId, MapId, new Vec2(1, 2), 0.5);

            var entity = f.World.GetEntity(id);
            Assert.NotNull(entity);
            Assert.Equal(EntityKinds.Creature, entity!.Kind);
            var unit = Assert.IsType<CreatureUnit>(entity);
            Assert.Equal(new Id("fac.test_monster"), unit.FactionId);
            Assert.Equal(BasicTemplateId, unit.TemplateId);
            Assert.Equal(new Vec2(1, 2), unit.Position);
            Assert.Equal(0.5, unit.Facing);
        }

        [Fact]
        public void Spawn_WithoutGrowth_AppliesTierMultiplierOnly()
        {
            var f = Build();

            var id = f.Factory.Spawn(BasicTemplateId, MapId, Vec2.Zero, 0);

            // tier.normal 倍率 1：最终值 = base_stats 原值。
            Assert.Equal(10.0, f.Stats.GetStat(id, PowerStat), 6);
            Assert.Equal(100.0, f.Stats.GetStat(id, MaxHealthStat), 6);
        }

        [Fact]
        public void Spawn_WithGrowthCurve_AppliesTierMultiplierAndAccumulatesFromLevel2()
        {
            var f = Build();

            var id = f.Factory.Spawn(EliteTemplateId, MapId, Vec2.Zero, 0);

            // tier.elite 倍率 2：base 10*2=20，成长曲线 2/3 级各 +5 → +10，最终 30。
            Assert.Equal(30.0, f.Stats.GetStat(id, PowerStat), 6);
            // base 100*2=200，成长曲线只在 3 级 +20 → 最终 220。
            Assert.Equal(220.0, f.Stats.GetStat(id, MaxHealthStat), 6);
        }

        // -----------------------------------------------------------------
        // 消费方反馈第 36 条根治：出生等级 > 1 的生物再升级时，成长量此前被重复计入
        // -----------------------------------------------------------------

        /// <summary>复现/验收探针本身：<c>creature.sample_beast</c> 出生等级 2、
        /// <c>base_stats.stat.power=5</c>、tier.normal 倍率 1、曲线每级 +2——出生时只应叠加
        /// "2 级"这一段成长（5+2=7），不应该把 3 级的成长也提前算进去。</summary>
        [Fact]
        public void E36_Spawn_AtBirthLevel2_AppliesLevel2GrowthOnly()
        {
            var f = Build();

            var id = f.Factory.Spawn(BeastTemplateId, MapId, Vec2.Zero, 0);

            Assert.Equal(7.0, f.Stats.GetStat(id, PowerStat), 6);
            Assert.Equal(2, f.Progression.GetLevel(id));
        }

        /// <summary>核心验收：出生等级 2 的生物真实升到 3 级后，<c>stat.power</c> 应为 9
        /// （<c>base(5) + Σ[2..3](2+2=4)</c>），根治前的错误行为是 11
        /// （<c>base 已含 level2 的 7 + 升级再整段重写的 4</c>）——与消费方反馈第 36 条给出的
        /// 探针数值完全对应。</summary>
        [Fact]
        public void E36_SpawnAtBirthLevel2_UpgradeToLevel3_StrengthIsNineNotEleven()
        {
            var f = Build();
            var id = f.Factory.Spawn(BeastTemplateId, MapId, Vec2.Zero, 0);
            Assert.Equal(7.0, f.Stats.GetStat(id, PowerStat), 6); // 出生态基线，见上一条用例

            // prog.sample_curve_e36 的 level2.xp_to_next=200，投入恰好 200 经验触发一次升级。
            f.Progression.AddXp(id, TestXpSourceId, 200);

            Assert.Equal(3, f.Progression.GetLevel(id));
            Assert.Equal(9.0, f.Stats.GetStat(id, PowerStat), 6);
        }

        /// <summary>路径等价性：出生等级 5 直接生成的最终值，应与"出生等级 1 生成后逐级真实升到
        /// 5 级"的最终值完全一致——成长统一只由 <c>ProgressionHost</c> 的修正承载后，两条路径
        /// 共用同一份聚合实现，不应因为"从哪个等级起步"而产生不同结果。<c>creature.sample_hydra</c>
        /// （出生 5 级）与 <c>creature.sample_hydra_cub</c>（出生 1 级）共用同一条五级曲线
        /// <c>prog.sample_curve_e36_l5</c>（每级成长量各不相同，避免"总和碰巧相等"掩盖顺序
        /// 错误）与相同的 <c>base_stats</c>/tier。</summary>
        [Fact]
        public void E36_BirthAtLevel5_EqualsBirthAtLevel1ThenUpgradeToLevel5()
        {
            // 路径 A：出生即 5 级。
            var fA = Build();
            var birth5 = fA.Factory.Spawn(HydraTemplateId, MapId, Vec2.Zero, 0);
            var birth5Power = fA.Stats.GetStat(birth5, PowerStat);

            // 路径 B：出生 1 级，真实逐级升到 5 级（曲线每级 xp_to_next=100）。
            var fB = Build();
            var upgraded = fB.Factory.Spawn(HydraCubTemplateId, MapId, Vec2.Zero, 0);
            fB.Progression.AddXp(upgraded, TestXpSourceId, 100); // 1→2
            fB.Progression.AddXp(upgraded, TestXpSourceId, 100); // 2→3
            fB.Progression.AddXp(upgraded, TestXpSourceId, 100); // 3→4
            fB.Progression.AddXp(upgraded, TestXpSourceId, 100); // 4→5
            var upgradeToLevel5Power = fB.Stats.GetStat(upgraded, PowerStat);

            Assert.Equal(5, fA.Progression.GetLevel(birth5));
            Assert.Equal(5, fB.Progression.GetLevel(upgraded));
            Assert.Equal(birth5Power, upgradeToLevel5Power, 6);
            // 具体数值核对：base 3 + Σ[2..5](1+2+3+4=10) = 13。
            Assert.Equal(13.0, birth5Power, 6);
        }

        /// <summary>存档对账：读档（<c>ProgressionHost.RestoreState</c>，经
        /// <c>ProgressionPersistable.Load</c> 同一路径）恢复出生等级 &gt; 1 的生物后，属性与
        /// 修正数量应与"出生即处于该等级"一致（不重复施加、不丢失），随后再真实升级一次，结果也应
        /// 正确——验证 <see cref="Core.Numbers.Progression.ProgressionHost.RestoreState"/> 与
        /// <see cref="Core.Numbers.Progression.IProgressionHost.ApplyGrowthToCurrentLevel"/> 共用
        /// 同一份聚合实现，读档不会在"出生已经写过一次"的基础上再叠加一次。</summary>
        [Fact]
        public void E36_SaveThenLoad_BirthLevel2Creature_RestoresConsistentState_ThenUpgradesCorrectly()
        {
            var f = Build();
            var id = f.Factory.Spawn(BeastTemplateId, MapId, Vec2.Zero, 0);
            Assert.Equal(7.0, f.Stats.GetStat(id, PowerStat), 6);

            // 模拟"存档 → 读档"：取回当前 Progression 快照，用 RestoreState 重放到同一个单位
            // （同一单位、同一 StatHost：RestoreState 内部会先移除旧的 prog.growth 修正再重写，
            // 幂等——不应该让属性偏离出生态的 7）。
            var snapshot = f.Progression.SaveUnit(id);
            var snapshotObj = (Core.Foundation.Common.Json.JsonObject)snapshot;
            var curveId = new Id(((Core.Foundation.Common.Json.JsonString)snapshotObj["curve_id"]).Value);
            var level = (int)((Core.Foundation.Common.Json.JsonNumber)snapshotObj["level"]).Value;
            var xp = (long)((Core.Foundation.Common.Json.JsonNumber)snapshotObj["xp"]).Value;

            f.Progression.RestoreState(id, curveId, level, xp);

            Assert.Equal(2, f.Progression.GetLevel(id));
            Assert.Equal(7.0, f.Stats.GetStat(id, PowerStat), 6); // 读档对账后不应偏离出生态

            // 读档之后再真实升级一次，结果应与"从未读档、一路升上来"完全一致（9，不是 11 或更高）。
            f.Progression.AddXp(id, TestXpSourceId, 200);
            Assert.Equal(3, f.Progression.GetLevel(id));
            Assert.Equal(9.0, f.Stats.GetStat(id, PowerStat), 6);
        }

        /// <summary>换职业/种族重载路径（<c>RulesAssembly.ReloadArchetypeAndRace</c>）本身完全不
        /// 触碰 <c>Progression</c>（只处理职业基础属性/种族修正/被动光环，见该方法与
        /// <c>ProgressionWriters.LevelSync</c> 判断记录），<c>CreatureFactory.Spawn</c> 也只在生成
        /// 这一次性时机调用 <c>ApplyGrowthToCurrentLevel</c>——本用例证明重复调用
        /// <c>ApplyGrowthToCurrentLevel</c>（模拟"换职业类路径意外重放成长"的最坏情形）本身是幂等
        /// 的，不会因为多调用一次而让成长重复叠加。</summary>
        [Fact]
        public void E36_ApplyGrowthToCurrentLevel_CalledAgainAfterSpawn_DoesNotDuplicateGrowth()
        {
            var f = Build();
            var id = f.Factory.Spawn(BeastTemplateId, MapId, Vec2.Zero, 0);
            Assert.Equal(7.0, f.Stats.GetStat(id, PowerStat), 6);

            f.Progression.ApplyGrowthToCurrentLevel(id);
            f.Progression.ApplyGrowthToCurrentLevel(id);

            Assert.Equal(7.0, f.Stats.GetStat(id, PowerStat), 6);
        }

        /// <summary>分档倍率与成长组合：<c>creature.sample_elite</c>（tier.elite 倍率 2，出生
        /// 即满配 3 级）验证成长量本身不受倍率影响（曲线是 flat 修正，见
        /// <c>ProgressionWriters.StatModifierWriter</c> 判断记录"本模块固定传 flat"）——只有
        /// <c>base_stats</c> 部分乘以倍率，成长部分原样相加，两者互不相乘。</summary>
        [Fact]
        public void E36_TierMultiplier_DoesNotScaleGrowthAmount()
        {
            var f = Build();

            var id = f.Factory.Spawn(EliteTemplateId, MapId, Vec2.Zero, 0);

            // base 10*2=20，成长 Σ[2..3](5+5=10)——倍率只作用于 base，不作用于成长，最终 30。
            Assert.Equal(30.0, f.Stats.GetStat(id, PowerStat), 6);
        }

        /// <summary>出生 1 级、无成长曲线的既有行为不受影响（既有测试
        /// <see cref="Spawn_WithoutGrowth_AppliesTierMultiplierOnly"/> 已覆盖同一场景，本条从
        /// 消费方反馈第 36 条验收清单角度重复确认：改动前后数值完全一致）。</summary>
        [Fact]
        public void E36_SpawnAtBirthLevel1_NoGrowthCurve_BehaviorUnchanged()
        {
            var f = Build();

            var id = f.Factory.Spawn(BasicTemplateId, MapId, Vec2.Zero, 0);

            Assert.Equal(10.0, f.Stats.GetStat(id, PowerStat), 6);
            Assert.Equal(100.0, f.Stats.GetStat(id, MaxHealthStat), 6);
            Assert.Throws<ArgumentException>(() => f.Progression.GetLevel(id)); // 未挂 Progression
        }

        [Fact]
        public void Spawn_RegistersProgressionHost_WhenStatGrowthRefPresent()
        {
            var f = Build();

            var id = f.Factory.Spawn(EliteTemplateId, MapId, Vec2.Zero, 0);

            Assert.Equal(3, f.Progression.GetLevel(id));
        }

        [Fact]
        public void Spawn_DoesNotRegisterProgressionHost_WhenNoStatGrowthRef()
        {
            var f = Build();

            var id = f.Factory.Spawn(BasicTemplateId, MapId, Vec2.Zero, 0);

            Assert.Throws<ArgumentException>(() => f.Progression.GetLevel(id));
        }

        [Fact]
        public void Spawn_RegistersPowerHost_WithStatBasedMaxReflectingGrowth()
        {
            var f = Build();

            var id = f.Factory.Spawn(EliteTemplateId, MapId, Vec2.Zero, 0);

            Assert.True(f.Powers.HasPower(id, WellKnownPowers.Health));
            Assert.Equal(220.0, f.Powers.GetPowerMax(id, WellKnownPowers.Health), 6);
            Assert.Equal(220.0, f.Powers.GetPower(id, WellKnownPowers.Health), 6); // start_full=true
        }

        // -----------------------------------------------------------------
        // 消费方反馈第 33 条：资源类型注册顺序（CreatureOptions.DefaultPowerTypes 新语义）
        // -----------------------------------------------------------------

        /// <summary>默认选项（<see cref="CreatureOptions.DefaultPowerTypes"/> 未显式覆盖，为
        /// <c>null</c>）下，<see cref="CreatureFactory.Spawn"/> 回落到数据集里全部已登记的
        /// <c>arch.power_type</c> 定义（见 <see cref="CreatureTestSupport.PowerTypeRows"/>：
        /// health + mana 两条）——mana 不在旧默认值 <c>[Health]</c> 里，此前会因未注册而在
        /// <c>GetPower</c> 时抛异常，本用例证明新默认路径下可用。</summary>
        [Fact]
        public void Spawn_WithDefaultOptions_RegistersAllDatasetPowerTypes_IncludingMana()
        {
            var f = Build();

            var id = f.Factory.Spawn(BasicTemplateId, MapId, Vec2.Zero, 0);

            Assert.True(f.Powers.HasPower(id, WellKnownPowers.Health));
            var mana = new Id("arch.power.mana");
            Assert.True(f.Powers.HasPower(id, mana));
            Assert.Equal(50.0, f.Powers.GetPower(id, mana), 6); // max_source: fixed 50, start_full=true
        }

        /// <summary>未注册的资源类型（本仓库任何 <c>arch.power_type</c> 都未登记的虚构 id）上调用
        /// <see cref="IPowerHost.TryGetPower"/> 返回 <c>false</c> 且不抛异常——同一场景下
        /// <see cref="IPowerHost.GetPower"/> 仍然抛出，两者行为按各自契约分别验证。</summary>
        [Fact]
        public void TryGetPower_UnregisteredPowerType_ReturnsFalseWithoutThrowing()
        {
            var f = Build();
            var id = f.Factory.Spawn(BasicTemplateId, MapId, Vec2.Zero, 0);
            var unknownPowerType = new Id("arch.power.does_not_exist");

            var found = f.Powers.TryGetPower(id, unknownPowerType, out var value);

            Assert.False(found);
            Assert.Equal(0.0, value);
            Assert.Throws<InvalidOperationException>(() => f.Powers.GetPower(id, unknownPowerType));
        }

        /// <summary>已注册资源类型上 <see cref="IPowerHost.TryGetPower"/> 返回 <c>true</c> 且取值与
        /// <see cref="IPowerHost.GetPower"/> 一致。</summary>
        [Fact]
        public void TryGetPower_RegisteredPowerType_ReturnsTrueWithCurrentValue()
        {
            var f = Build();
            var id = f.Factory.Spawn(EliteTemplateId, MapId, Vec2.Zero, 0);

            var found = f.Powers.TryGetPower(id, WellKnownPowers.Health, out var value);

            Assert.True(found);
            Assert.Equal(220.0, value, 6);
        }

        /// <summary>显式设置 <see cref="CreatureOptions.DefaultPowerTypes"/>（哪怕设成与旧默认值
        /// 相同的 <c>[Health]</c>）时，优先于"回落到数据集全部 arch.power_type 定义"这一步，
        /// 对全部生成的生物统一生效——mana 不会被注册，<see cref="IPowerHost.TryGetPower"/> 对它
        /// 返回 <c>false</c>。</summary>
        [Fact]
        public void Spawn_WithExplicitDefaultPowerTypes_OverridesDatasetFallback()
        {
            var options = new CreatureOptions { DefaultPowerTypes = new[] { WellKnownPowers.Health } };
            var f = Build(options);

            var id = f.Factory.Spawn(BasicTemplateId, MapId, Vec2.Zero, 0);

            Assert.True(f.Powers.HasPower(id, WellKnownPowers.Health));
            Assert.False(f.Powers.TryGetPower(id, new Id("arch.power.mana"), out _));
        }

        [Fact]
        public void Spawn_ConvertsNpcFlagsToTags_AndSetsTierTag()
        {
            var f = Build();

            var id = f.Factory.Spawn(EliteTemplateId, MapId, Vec2.Zero, 0);
            var unit = (CreatureUnit)f.World.GetEntity(id)!;

            Assert.Contains(new Id("npc_flag.vendor"), unit.NpcFlags);
            Assert.Contains(new Id("npc_flag.questgiver"), unit.NpcFlags);
            Assert.Contains(new Id("tag.npc.vendor"), unit.Tags);
            Assert.Contains(new Id("tag.npc.questgiver"), unit.Tags);
            Assert.Contains(new Id("creature.tier.elite"), unit.Tags);
        }

        [Fact]
        public void HasFlag_ReflectsTemplateNpcFlags()
        {
            var f = Build();

            Assert.True(f.Factory.HasFlag(EliteTemplateId, NpcFlag.Vendor));
            Assert.True(f.Factory.HasFlag(EliteTemplateId, NpcFlag.Questgiver));
            Assert.False(f.Factory.HasFlag(EliteTemplateId, NpcFlag.SavePoint));
            Assert.False(f.Factory.HasFlag(BasicTemplateId, NpcFlag.Vendor));
        }

        // -----------------------------------------------------------------
        // ICreatureTemplateQuery.Get（W1 收边补齐：A3 审计 #18，公开 API 本身此前无直接测试断言，
        // 只被 CreatureFactory.RequireTemplate 间接复用同一索引）
        // -----------------------------------------------------------------

        [Fact]
        public void Get_ReturnsStrongTypedTemplate_MatchingRegisteredFields()
        {
            ICreatureTemplateQuery query = Build().Factory;

            var template = query.Get(BasicTemplateId);

            Assert.Equal(BasicTemplateId, template.Id);
            Assert.Equal(new Id("l10n.creature.sample_basic.name"), template.NameKey);
        }

        [Fact]
        public void Get_UnknownTemplateId_Throws()
        {
            ICreatureTemplateQuery query = Build().Factory;

            Assert.Throws<ArgumentException>(() => query.Get(new Id("creature.does_not_exist")));
        }

        [Fact]
        public void Spawn_WritesImmunitiesFromTemplateAndTierControlImmune()
        {
            var f = Build();

            var id = f.Factory.Spawn(EliteTemplateId, MapId, Vec2.Zero, 0);
            var unit = (CreatureUnit)f.World.GetEntity(id)!;

            Assert.Contains(new Id("school.sample_fire"), unit.Immunities);
            // tier.elite.control_immune = true → 默认 ImmunityTagPrefix 写入。
            Assert.Contains(new Id("immunity.control_immune"), unit.Immunities);
        }

        [Fact]
        public void Spawn_DoesNotWriteControlImmuneTag_ForNonControlImmuneTier()
        {
            var f = Build();

            var id = f.Factory.Spawn(BasicTemplateId, MapId, Vec2.Zero, 0);
            var unit = (CreatureUnit)f.World.GetEntity(id)!;

            Assert.DoesNotContain(new Id("immunity.control_immune"), unit.Immunities);
        }

        [Fact]
        public void Spawn_RegistersAi_WhenAiBehaviorRefPresent()
        {
            var f = Build();

            var id = f.Factory.Spawn(EliteTemplateId, MapId, new Vec2(3, 4), 0);

            var call = Assert.Single(f.AiCalls);
            Assert.Equal(id, call.unitId);
            Assert.Equal(new Id("ai.behavior.sample"), call.profileId);
            Assert.Equal(new Vec2(3, 4), call.spawnPoint);
            Assert.Equal(new Id("ai.rotation.sample"), call.rotationId);
        }

        [Fact]
        public void Spawn_DoesNotRegisterAi_WhenNoAiBehaviorRef()
        {
            var f = Build();

            f.Factory.Spawn(BasicTemplateId, MapId, Vec2.Zero, 0);

            Assert.Empty(f.AiCalls);
        }

        [Fact]
        public void Spawn_EnqueuesCreatureSpawnedEvent_WithEntityAndTemplateId()
        {
            var f = Build();

            var id = f.Factory.Spawn(BasicTemplateId, MapId, Vec2.Zero, 0);
            f.Bus.DispatchPending();

            var evt = Assert.Single(f.SpawnedEvents);
            Assert.Equal(id, evt.EntityId);
            Assert.Equal(BasicTemplateId, evt.TemplateId);
        }

        [Fact]
        public void Despawn_RemovesEntity_AndEnqueuesCreatureDespawnedEventWithReason()
        {
            var f = Build();
            var id = f.Factory.Spawn(BasicTemplateId, MapId, Vec2.Zero, 0);
            f.Bus.DispatchPending();

            f.Factory.Despawn(id, "died");
            f.World.Tick(SimStep.Continuous(0.1));

            Assert.Null(f.World.GetEntity(id));
            var evt = Assert.Single(f.DespawnedEvents);
            Assert.Equal(id, evt.EntityId);
            Assert.Equal("died", evt.Reason);
        }

        [Fact]
        public void Spawn_UnknownTemplate_Throws()
        {
            var f = Build();

            Assert.Throws<ArgumentException>(() =>
                f.Factory.Spawn(new Id("creature.does_not_exist"), MapId, Vec2.Zero, 0));
        }

        [Fact]
        public void Spawn_UnknownTier_Throws()
        {
            var bus = CreatureTestSupport.CreateBus();
            var registry = CreatureTestSupport.MakeRegistry(bus);
            var world = new WorldSim(bus);
            var units = new WorldUnitAccess(world);
            var stats = CreatureTestSupport.MakeStatHost(registry, bus);
            var powers = CreatureTestSupport.MakePowerHost(registry, bus, stats);
            var progression = CreatureTestSupport.MakeProgressionHost(registry, bus, stats);
            AiRegistrar registrar = (unitId, profileId, spawnPoint, rotationId) => { };

            // 用绕过引用完整性校验的假 registry 装配工厂：证明 CreatureFactory 自身对未知 tier
            // 有防御性检查（正常数据管线里这一分支已在 reference_integrity 校验阶段被拦截）。
            var orphanRegistry = new CreatureTestSupport.UnknownTierRegistryView();
            var factory = new CreatureFactory(orphanRegistry, world, bus, stats, powers, progression, units, registrar);

            Assert.Throws<ArgumentException>(() =>
                factory.Spawn(new Id("creature.sample_orphan"), MapId, Vec2.Zero, 0));
        }
    }
}
