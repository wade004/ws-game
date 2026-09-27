using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Assembly
{
    /// <summary>
    /// ADR-0106 落地验收（消费方反馈第五十五批"单一模板受伤但不死"）：<c>creature.template
    /// .power_floors</c> 让一个模板生成的单位持续挨打也不会归零/死亡，其它单位死亡结算逐位不变。
    /// 惯例同 <see cref="CreatureDespawnPeriodicEffectTests"/>——生产装配级夹具：真实
    /// <c>CarriersAssembly</c>（<c>CreatureFactory</c>/<c>AuraHost</c>/<c>EffectDispatcher</c>/
    /// <c>Resolver</c>/<c>StatHost</c>/<c>PowerHost</c> 全链路组合），真实 <c>WorldSim.Tick</c> 驱动
    /// 周期性伤害光环反复结算，不手工调用 <c>ModifyPower</c> 模拟伤害——伤害必须真的经
    /// <c>core/rules/combat.Resolver</c> 结算落地，<c>combat.damage_dealt</c> 事件必须真的由生产
    /// 结算管线发布，这样才能验证"伤害事件、飘字（携带的 Amount）照常，生命值被夹在覆盖下限之上"
    /// 这一设计意图，而不是"框架代码里 GetPower 恰好返回了预期值"这种更弱的验证。
    /// </summary>
    public sealed class ADR0106_CreaturePowerFloorTests
    {
        private static readonly Id MapId = new Id("map.adr0106_test");
        private static readonly Id CasterTemplateId = new Id("creature.adr0106_caster");
        private static readonly Id DummyTemplateId = new Id("creature.adr0106_training_dummy");
        private static readonly Id ControlTemplateId = new Id("creature.adr0106_control_target");
        private static readonly Id TierNormal = new Id("creature.tier.adr0106_normal");
        private static readonly Id SchoolPhysical = new Id("school.physical");
        private static readonly Id AuraDotId = new Id("skill.aura_def.adr0106_sample_dot");
        private static readonly Id HealthPowerId = WellKnownPowers.Health;

        private const double MaxHealth = 100;
        private const double DamagePerTick = 40;
        private const double DummyFloor = 1;

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static InMemoryDataSource BuildDataSource()
        {
            var powerTypeRows = "[" +
                "{\"id\": \"" + HealthPowerId.Value + "\", \"name_key\": \"l10n.power.health.name\", " +
                "\"max_source\": {\"kind\": \"fixed\", \"value\": " + MaxHealth.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}, " +
                "\"regen_in_combat\": 0, \"regen_out_of_combat\": 0, \"decay_out_of_combat\": 0, " +
                "\"refill_on_leave_combat\": false, \"start_full\": true, \"allow_overflow\": false, \"min\": 0}" +
                "]";

            var tierDefinitionRows = "[" +
                "{\"id\": \"" + TierNormal.Value + "\", \"name_key\": \"l10n.creature.tier.adr0106_normal.name\", " +
                "\"stat_multiplier\": 1, \"control_immune\": false, \"sort_weight\": 0}" +
                "]";

            var templateRows = "[" +
                "{\"id\": \"" + CasterTemplateId.Value + "\", \"name_key\": \"l10n.creature.adr0106_caster.name\", " +
                "\"level\": 1, \"tier\": \"" + TierNormal.Value + "\", " +
                "\"base_stats\": {}, \"faction_id\": \"fac.adr0106_test\", " +
                "\"display_ref\": \"display.adr0106_caster\"}," +
                "{\"id\": \"" + DummyTemplateId.Value + "\", \"name_key\": \"l10n.creature.adr0106_training_dummy.name\", " +
                "\"level\": 1, \"tier\": \"" + TierNormal.Value + "\", " +
                "\"base_stats\": {}, \"faction_id\": \"fac.adr0106_test\", " +
                "\"display_ref\": \"display.adr0106_training_dummy\", " +
                "\"power_floors\": {\"" + HealthPowerId.Value + "\": " + DummyFloor.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}," +
                "{\"id\": \"" + ControlTemplateId.Value + "\", \"name_key\": \"l10n.creature.adr0106_control_target.name\", " +
                "\"level\": 1, \"tier\": \"" + TierNormal.Value + "\", " +
                "\"base_stats\": {}, \"faction_id\": \"fac.adr0106_test\", " +
                "\"display_ref\": \"display.adr0106_control_target\"}" +
                "]";

            // 命中表全部分支禁用（惯例同 core/rules/tests/Integration/FightWorldBuilder.cs），伤害
            // 结算不含随机波动，DamagePerTick 每次落地的量确定且可精确断言。
            var hitTableRows = "[" +
                "{\"id\": \"combat.hit_table.default\", " +
                "\"miss\": {\"enabled\": false, \"base\": 0}, \"dodge\": {\"enabled\": false, \"base\": 0}, " +
                "\"parry\": {\"enabled\": false, \"base\": 0}, \"glancing_blow\": {\"enabled\": false, \"base\": 0}, " +
                "\"block\": {\"enabled\": false, \"base\": 0}, \"crit\": {\"enabled\": false, \"base\": 0}, " +
                "\"crit_multiplier_base\": 2.0}" +
                "]";

            var auraDefRows = "[" +
                "{\"id\": \"" + AuraDotId.Value + "\", \"duration\": 60, \"max_stacks\": 1, " +
                "\"effects\": [{\"kind\": \"periodic_damage\", \"params\": {" +
                "\"interval\": 1.0, \"base_value\": " + DamagePerTick.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", \"coefficient\": 0, " +
                "\"school\": \"" + SchoolPhysical.Value + "\"}}]}" +
                "]";

            var itemBudgetCurveRows = "[" +
                "{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}" +
                "]";

            return new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", "[]"))
                .Add("arch.power_type", Envelope("arch.power_type", powerTypeRows))
                .Add(Core.Carriers.Creature.CreatureSchemas.TierDefinition.Name,
                    Envelope(Core.Carriers.Creature.CreatureSchemas.TierDefinition.Name, tierDefinitionRows))
                .Add(Core.Carriers.Creature.CreatureSchemas.Template.Name,
                    Envelope(Core.Carriers.Creature.CreatureSchemas.Template.Name, templateRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", hitTableRows))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("skill.aura_def", Envelope("skill.aura_def", auraDefRows))
                .Add("item.budget_curve", Envelope("item.budget_curve", itemBudgetCurveRows));
        }

        private sealed class Harness
        {
            public CarriersAssembly Assembly = null!;
            public WorldSim World = null!;
            public Id CasterId;
            public Id DummyId;
            public Id ControlId;
            public List<CombatDamageDealtEvent> DamageEvents = null!;
            public List<UnitDiedEvent> DeathEvents = null!;
        }

        private static Harness Build()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = BuildDataSource();
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);

            var assembly = new CarriersAssembly(bus, registry, rng, world, spatial);

            var casterId = assembly.Creatures.Spawn(CasterTemplateId, MapId, new Vec2(0, 0), 0);
            var dummyId = assembly.Creatures.Spawn(DummyTemplateId, MapId, new Vec2(1, 0), 0);
            var controlId = assembly.Creatures.Spawn(ControlTemplateId, MapId, new Vec2(2, 0), 0);

            // 对两个目标各挂一份同款周期性伤害 DOT（来源同一个 caster），惯例同
            // CreatureDespawnPeriodicEffectTests——不用施法/命中判定路径，直接施加光环，驱动
            // WorldSim.Tick 让 AuraHost.Update -> EffectDispatcher -> Resolver 真实结算每一跳。
            assembly.Rules.Skill.EffectSink.ApplyAura(dummyId, AuraDotId, casterId);
            assembly.Rules.Skill.EffectSink.ApplyAura(controlId, AuraDotId, casterId);

            var damageEvents = new List<CombatDamageDealtEvent>();
            var deathEvents = new List<UnitDiedEvent>();
            bus.Subscribe<CombatDamageDealtEvent>(RulesEventKeys.CombatDamageDealt, e => damageEvents.Add(e));
            bus.Subscribe<UnitDiedEvent>(RulesEventKeys.UnitDied, e => deathEvents.Add(e));

            return new Harness
            {
                Assembly = assembly,
                World = world,
                CasterId = casterId,
                DummyId = dummyId,
                ControlId = controlId,
                DamageEvents = damageEvents,
                DeathEvents = deathEvents,
            };
        }

        /// <summary>复现用例：带 <c>power_floors</c> 的训练假人持续挨打（累计伤害 ≥ 1.5×最大生命）
        /// 后仍存活、可继续被命中，每次 <c>combat.damage_dealt</c> 的 Amount 等于结算 FinalAmount
        /// （不是夹取后的 0），全程无 <see cref="UnitDiedEvent"/>。
        /// <para>
        /// 修前预期为红：<c>CreatureTemplate</c> 未解析 <c>power_floors</c>、<c>CreatureFactory</c>
        /// 未调用 <c>SetMinOverride</c> 时，该单位仍按资源类型定义的全局 <c>min=0</c> 夹取，第 3 拍
        /// 生命降到 0 后 <c>Resolver</c> 判死、发 <see cref="UnitDiedEvent"/>，第 4 拍起
        /// <c>Resolver.Resolve</c> precheck 命中"目标已死亡"分支直接返回 Miss、不再落地也不再发
        /// <c>combat.damage_dealt</c>——本用例断言的"IsAlive 恒真""6 次 damage_dealt""0 次
        /// UnitDied"三条修前必然全部落空。
        /// </para>
        /// </summary>
        [Fact]
        public void TrainingDummyWithPowerFloor_TakesRepeatedDamage_NeverDiesAndKeepsTakingHits()
        {
            var h = Build();

            const int ticks = 6; // 6 × 40 = 240 累计伤害 ≥ 100 × 1.5 = 150。
            for (var i = 0; i < ticks; i++)
            {
                h.World.Tick(SimStep.Continuous(1.0));

                // 每一拍都应仍然存活——不是只在最后一拍检查一次，逐拍核对"持续挨打不会中途死亡"。
                Assert.True(h.Assembly.Units.IsAlive(h.DummyId), $"训练假人应在第 {i + 1} 拍后仍存活");
            }

            var dummyDamageEvents = h.DamageEvents.FindAll(e => e.TargetId == h.DummyId);
            Assert.Equal(ticks, dummyDamageEvents.Count);
            foreach (var e in dummyDamageEvents)
            {
                // 飘字数字完整：Amount 是结算算出的 FinalAmount，不是夹取到下限之后的差值。
                Assert.Equal(DamagePerTick, e.Amount);
            }

            Assert.DoesNotContain(h.DeathEvents, e => e.UnitId == h.DummyId);

            // 当前值被夹在覆盖下限之上，累计伤害 240 远超"降到下限"所需的量，稳定停在覆盖值 1。
            Assert.Equal(DummyFloor, h.Assembly.Rules.Powers.GetPower(h.DummyId, HealthPowerId));
        }

        /// <summary>不变量①（阳性对照）：同数据、不带 <c>power_floors</c> 的目标在相同累计伤害下
        /// 正常死亡，<see cref="UnitDiedEvent"/> 恰好一次——证明本次改动只影响声明了 <c>power_floors</c>
        /// 的单位，其余单位死亡结算逐位不变。</summary>
        [Fact]
        public void ControlTargetWithoutPowerFloor_TakesSameDamage_DiesExactlyOnce()
        {
            var h = Build();

            const int ticks = 6;
            for (var i = 0; i < ticks; i++)
            {
                h.World.Tick(SimStep.Continuous(1.0));
            }

            var deathEventsForControl = h.DeathEvents.FindAll(e => e.UnitId == h.ControlId);
            Assert.Single(deathEventsForControl);
            Assert.False(h.Assembly.Units.IsAlive(h.ControlId));

            // 100 / 40 = 2.5 拍即耗尽：第 1 拍 100->60，第 2 拍 60->20，第 3 拍 20 - 40 触底死亡、
            // 夹到 0；死亡之后 Resolver 对已死亡目标的结算 precheck 直接判 Miss，不再落地、不再发
            // combat.damage_dealt，因此总数只有 3 次，不是 6 次。
            var controlDamageEvents = h.DamageEvents.FindAll(e => e.TargetId == h.ControlId);
            Assert.Equal(3, controlDamageEvents.Count);
            Assert.Equal(0, h.Assembly.Rules.Powers.GetPower(h.ControlId, HealthPowerId));
        }
    }
}
