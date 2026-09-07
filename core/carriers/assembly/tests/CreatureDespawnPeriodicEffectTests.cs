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
    /// C02 收口（外部审计 7e63d66 第四轮，P1）：施法者给存活目标施加带来源缩放的周期性 DOT 光环后，
    /// 用真实 <see cref="Core.Carriers.Creature.CreatureFactory.Despawn"/>（不是测试 fake unit
    /// access）注销施法者，随后周期性效果继续经真实 <c>AuraHost.Update</c> → <c>EffectDispatcher</c>
    /// → <c>Resolver</c> 结算多个 tick，验证不再抛异常。
    /// <para>
    /// 复现的两条崩溃路径（修复前，任一条都足以让本测试失败）：
    /// <list type="number">
    /// <item><c>EffectDispatcher.ApplyDamageOrHeal</c> 在效果声明了 <c>scaling_stat</c> 时无条件调用
    /// <c>IStatHost.GetStat(context.SourceId, ...)</c>——来源已被 <c>Despawn</c> 同步注销
    /// <c>IStatHost</c> 后直接抛 <see cref="System.InvalidOperationException"/>。</item>
    /// <item><c>CombatHost.NotifyCombatEvent</c>（<c>Resolver.Resolve</c> 结算双方各调用一次）在来源
    /// 未在 <c>IPowerHost</c> 注册时调用 <c>IPowerHost.SetInCombat</c> 同样直接抛异常。</item>
    /// </list>
    /// </para>
    /// 用本类型专属的最小 <see cref="Core.Carriers.Assembly.CarriersAssembly"/> 装配（真实
    /// <c>CreatureFactory</c>/<c>AuraHost</c>/<c>EffectDispatcher</c>/<c>CombatHost</c>/
    /// <c>StatHost</c>/<c>PowerHost</c> 全链路组合，不是任何模块内部的 fake unit access），驱动真实
    /// <see cref="WorldSim.Tick"/>（<c>SkillTickHandler</c>/<c>CombatTickHandler</c> 已由
    /// <c>RulesAssembly.RegisterTickHandlers</c> 挂好），使 <c>entity.destroyed</c> 在 Despawn 后
    /// 真正派发、周期光环按生产环境的真实节奏结算。
    /// </summary>
    public sealed class CreatureDespawnPeriodicEffectTests
    {
        private static readonly Id MapId = new Id("map.c02_test");
        private static readonly Id CasterTemplateId = new Id("creature.sample_c02_caster");
        private static readonly Id TargetTemplateId = new Id("creature.sample_c02_target");
        private static readonly Id TierNormal = new Id("creature.tier.c02_normal");
        private static readonly Id StatPower = new Id("stat.power");
        private static readonly Id SchoolPhysical = new Id("school.physical");
        private static readonly Id AuraDotId = new Id("skill.aura_def.c02_sample_dot");

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static InMemoryDataSource BuildDataSource()
        {
            var statDefinitionRows = "[" +
                "{\"id\": \"stat.power\", \"name_key\": \"l10n.stat.power.name\", \"group\": \"primary\", \"default_base\": 0}" +
                "]";

            var powerTypeRows = "[" +
                "{\"id\": \"" + WellKnownPowers.Health.Value + "\", \"name_key\": \"l10n.power.health.name\", " +
                "\"max_source\": {\"kind\": \"fixed\", \"value\": 1000}, " +
                "\"regen_in_combat\": 0, \"regen_out_of_combat\": 0, \"decay_out_of_combat\": 0, " +
                "\"refill_on_leave_combat\": false, \"start_full\": true, \"allow_overflow\": false, \"min\": 0}" +
                "]";

            var tierDefinitionRows = "[" +
                "{\"id\": \"" + TierNormal.Value + "\", \"name_key\": \"l10n.creature.tier.c02_normal.name\", " +
                "\"stat_multiplier\": 1, \"control_immune\": false, \"sort_weight\": 0}" +
                "]";

            var templateRows = "[" +
                "{\"id\": \"" + CasterTemplateId.Value + "\", \"name_key\": \"l10n.creature.c02_caster.name\", " +
                "\"level\": 1, \"tier\": \"" + TierNormal.Value + "\", " +
                "\"base_stats\": {\"stat.power\": 10}, \"faction_id\": \"fac.c02_test\", " +
                "\"display_ref\": \"display.c02_caster\"}," +
                "{\"id\": \"" + TargetTemplateId.Value + "\", \"name_key\": \"l10n.creature.c02_target.name\", " +
                "\"level\": 1, \"tier\": \"" + TierNormal.Value + "\", " +
                "\"base_stats\": {\"stat.power\": 5}, \"faction_id\": \"fac.c02_test\", " +
                "\"display_ref\": \"display.c02_target\"}" +
                "]";

            // 命中表全部分支禁用（惯例同 core/rules/tests/Integration/FightWorldBuilder.cs），伤害
            // 结算不含随机波动，只关心"是否抛异常"与"是否继续落地"，与命中判定本身无关。
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
                "\"interval\": 1.0, \"base_value\": 5, \"coefficient\": 2, " +
                "\"school\": \"" + SchoolPhysical.Value + "\", \"scaling_stat\": \"" + StatPower.Value + "\"}}]}" +
                "]";

            var itemBudgetCurveRows = "[" +
                "{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}" +
                "]";

            return new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", statDefinitionRows))
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

        [Fact]
        public void PeriodicDotWithScalingStat_SourceDespawnedThenMultipleTicksElapse_DoesNotThrow_AndKeepsLandingDamage()
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
            var targetId = assembly.Creatures.Spawn(TargetTemplateId, MapId, new Vec2(1, 0), 0);

            // 施加带 scaling_stat 的周期性 DOT：来源是 casterId（尚存活）。
            assembly.Rules.Skill.EffectSink.ApplyAura(targetId, AuraDotId, casterId);

            // 先正常跑一个 interval，确认周期效果本身在来源存活时能正常落地（排除"数据没配对"这种
            // 假阳性——如果这里都不掉血，后面"despawn 后不抛异常"的验收就没有意义）。
            world.Tick(SimStep.Continuous(1.0));
            var healthAfterFirstTick = assembly.Rules.Powers.GetPower(targetId, WellKnownPowers.Health);
            Assert.True(healthAfterFirstTick < 1000, "第一次周期 tick 应已对目标造成伤害，用例前提不成立");

            // 真实 Despawn 施法者（同步注销 IStatHost/IPowerHost 的单位注册），随后推进世界一个 tick
            // 让 entity.destroyed 真正派发（AuraHost/CombatHost 各自的 OnEntityDestroyed 订阅生效）。
            assembly.Creatures.Despawn(casterId, "c02_test_despawn");
            world.Tick(SimStep.Continuous(1.0));

            var healthAfterDespawnTick = assembly.Rules.Powers.GetPower(targetId, WellKnownPowers.Health);

            // 修复前：本次 Tick（内含一次周期效果结算）会在 EffectDispatcher.ApplyDamageOrHeal 或
            // CombatHost.NotifyCombatEvent 处抛 InvalidOperationException，直接让本测试失败/报错
            // （xUnit 会把测试方法内未捕获的异常记为失败）。修复后应正常完成，且伤害继续落地
            // （scaling 贡献降级为 0，只保留 base_value，见 EffectDispatcher 判断记录 C02）。
            Assert.True(healthAfterDespawnTick < healthAfterFirstTick,
                "来源销毁后周期效果应继续结算落地（scaling 贡献降级为 0，不是被跳过或冻结）");

            // 再连续跑数个 tick，验证"来源已销毁"这一状态下周期效果能稳定持续结算，不是只多撑一次
            // 就在后续 tick 抛异常。
            for (var i = 0; i < 5; i++)
            {
                world.Tick(SimStep.Continuous(1.0));
            }

            var healthAfterMoreTicks = assembly.Rules.Powers.GetPower(targetId, WellKnownPowers.Health);
            Assert.True(healthAfterMoreTicks < healthAfterDespawnTick,
                "来源销毁后应能持续经历多次周期 tick 而不中断（不止一次性侥幸不抛异常）");

            // 目标自身仍然存活、仍在世界中——本测试只验证"来源销毁不应让目标侧的正常状态跟着崩"。
            Assert.True(assembly.Units.Exists(targetId));
        }
    }
}
