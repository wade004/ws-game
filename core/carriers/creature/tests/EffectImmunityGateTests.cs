using System;
using System.Linq;
using Core.Carriers.Creature;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Numbers.PowerSet;
using Core.Rules.Assembly;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Creature
{
    /// <summary>
    /// 根治 2026-09-10"消费方反馈-2026-09-10-打断免疫"（M-C06 前置核验复现的产品缺口）：
    /// <see cref="Core.Rules.Skill.EffectDispatcher.ApplyEffect"/> 此前只在
    /// <c>school_damage</c>/<c>weapon_damage_pct</c>/<c>heal</c> 分支查询免疫，<c>interrupt</c>/
    /// <c>dispel</c>/<c>energize</c>/<c>teleport</c> 等其余效果原语分支从未查询过免疫（见该方法
    /// 判断记录"效果免疫统一门"）。本测试用真实 <see cref="RulesAssembly"/>（含
    /// <see cref="CreatureImmunityProvider"/> 静态免疫、真实 <see cref="Core.Rules.Skill.AuraHost"/>
    /// 动态免疫），与消费方前置核验探针（<c>docs/接入记录/M-C06/前置核验/证据/
    /// interrupt-immunity-1.12.0/Program.cs</c>）同一套装配惯例，覆盖该缺口修复后的产品预期矩阵。
    /// </summary>
    public sealed class EffectImmunityGateTests
    {
        private static readonly Id Map = new Id("map.eig_test");
        private static readonly Id Player = new Id("unit.eig_player");
        private static readonly Id Creature = new Id("creature.eig_target");
        private static readonly Id FactionPlayer = new Id("fac.eig_player");
        private static readonly Id FactionCreature = new Id("fac.eig_wildlife");
        private static readonly Id Class = new Id("arch.class.eig_probe");
        private static readonly Id ChainHostile = new Id("target.chain.eig_hostile");
        private static readonly Id ChainSelf = new Id("target.chain.eig_self");

        private static readonly Id LongDamage = new Id("skill.eig_long_damage");
        private static readonly Id InterruptSkill = new Id("skill.eig_interrupt");
        private static readonly Id DispelSkill = new Id("skill.eig_dispel");
        private static readonly Id EnergizeSelfSkill = new Id("skill.eig_energize_self");
        private static readonly Id TeleportSkill = new Id("skill.eig_teleport");
        private static readonly Id ChannelMovementSkill = new Id("skill.eig_channel_movement");

        private static readonly Id InterruptImmuneAura = new Id("skill.aura_def.eig_interrupt_immune");
        private static readonly Id DispelImmuneAura = new Id("skill.aura_def.eig_dispel_immune");
        private static readonly Id EnergizeImmuneAura = new Id("skill.aura_def.eig_energize_immune");
        private static readonly Id TeleportImmuneAura = new Id("skill.aura_def.eig_teleport_immune");
        private static readonly Id DispellableAura = new Id("skill.aura_def.eig_dispellable");
        private static readonly Id DispelType = new Id("skill.eig_dispel_type");

        private static readonly Id Health = WellKnownPowers.Health;
        private static readonly Id Mana = new Id("arch.power.eig_mana");
        private static readonly Id TestEnergy = new Id("arch.power.eig_test_energy");
        private static readonly Id Physical = new Id("school.physical");

        // -----------------------------------------------------------------
        // interrupt：动态/静态免疫保持读条，控制免疫不等同打断免疫，免疫时不产生打断事件
        // -----------------------------------------------------------------

        [Fact]
        public void NoImmunity_ExternalInterrupt_StopsCast_FiresInterruptedEvent_NoDamageLands()
        {
            using var fx = Build();

            var start = fx.Rules.Skill.CastSkill(Creature, LongDamage, new[] { Player });
            fx.Bus.DispatchPending();
            Assert.True(start.Success);
            Assert.True(fx.Rules.Skill.IsCasting(Creature));

            var interrupt = fx.Rules.Skill.CastSkill(Player, InterruptSkill, new[] { Creature });
            fx.Bus.DispatchPending();
            Assert.True(interrupt.Success);

            Assert.False(fx.Rules.Skill.IsCasting(Creature));
            Assert.Single(fx.Events.OfType<SkillCastInterruptedEvent>());
            Assert.Empty(fx.Events.OfType<SkillCastSuccessEvent>().Where(e => e.SkillId.Equals(LongDamage)));

            fx.Rules.Skill.AdvanceCastForActor(Creature, 2.5);
            fx.Bus.DispatchPending();
            Assert.Empty(fx.Events.OfType<CombatDamageDealtEvent>());
        }

        [Fact]
        public void DynamicInterruptImmunity_PreservesCast_CompletesNaturally_WithDamage_NoInterruptedEvent()
        {
            using var fx = Build();
            fx.Rules.Skill.EffectSink.ApplyAura(Creature, InterruptImmuneAura, Player);
            fx.Bus.DispatchPending();
            Assert.True(fx.Rules.Skill.AuraQuery.IsImmune(Creature, Physical, EffectKind.Interrupt));

            Assert.True(fx.Rules.Skill.CastSkill(Creature, LongDamage, new[] { Player }).Success);
            fx.Bus.DispatchPending();

            Assert.True(fx.Rules.Skill.CastSkill(Player, InterruptSkill, new[] { Creature }).Success);
            fx.Bus.DispatchPending();

            // 免疫命中：读条原样保留，不产生打断事件。
            Assert.True(fx.Rules.Skill.IsCasting(Creature));
            Assert.Empty(fx.Events.OfType<SkillCastInterruptedEvent>());

            fx.Rules.Skill.AdvanceCastForActor(Creature, 2.5);
            fx.Bus.DispatchPending();

            Assert.False(fx.Rules.Skill.IsCasting(Creature));
            Assert.Contains(fx.Events.OfType<SkillCastSuccessEvent>(), e => e.SkillId.Equals(LongDamage) && e.CasterId.Equals(Creature));
            Assert.Contains(fx.Events.OfType<CombatDamageDealtEvent>(), e => e.TargetId.Equals(Player));
        }

        [Fact]
        public void StaticEffectInterruptImmunity_PreservesCast_CompletesNaturally()
        {
            using var fx = Build();
            var target = (CreatureUnit)fx.World.GetEntity(Creature)!;
            target.Immunities.Add(new Id("effect.interrupt"));
            Assert.True(fx.StaticImmunity.IsImmune(Creature, Physical, EffectKind.Interrupt));

            Assert.True(fx.Rules.Skill.CastSkill(Creature, LongDamage, new[] { Player }).Success);
            fx.Bus.DispatchPending();

            Assert.True(fx.Rules.Skill.CastSkill(Player, InterruptSkill, new[] { Creature }).Success);
            fx.Bus.DispatchPending();

            Assert.True(fx.Rules.Skill.IsCasting(Creature));
            Assert.Empty(fx.Events.OfType<SkillCastInterruptedEvent>());

            fx.Rules.Skill.AdvanceCastForActor(Creature, 2.5);
            fx.Bus.DispatchPending();

            Assert.False(fx.Rules.Skill.IsCasting(Creature));
            Assert.Contains(fx.Events.OfType<SkillCastSuccessEvent>(), e => e.SkillId.Equals(LongDamage) && e.CasterId.Equals(Creature));
        }

        [Fact]
        public void ControlImmuneOnly_DoesNotPreventInterrupt()
        {
            // 消费方前置核验明确要求：immunity.control_immune 只免疫控制类光环（GetControlImmunity），
            // 不能自动等同打断免疫——见 IStaticImmunityProvider.GetControlImmunity 判断记录。
            using var fx = Build();
            var target = (CreatureUnit)fx.World.GetEntity(Creature)!;
            target.Immunities.Add(new Id("immunity.control_immune"));
            Assert.False(fx.StaticImmunity.IsImmune(Creature, Physical, EffectKind.Interrupt));
            Assert.Equal(ControlFlags.NoMove | ControlFlags.NoCast | ControlFlags.NoAttack | ControlFlags.NoInteract,
                fx.StaticImmunity.GetControlImmunity(Creature));

            Assert.True(fx.Rules.Skill.CastSkill(Creature, LongDamage, new[] { Player }).Success);
            fx.Bus.DispatchPending();

            Assert.True(fx.Rules.Skill.CastSkill(Player, InterruptSkill, new[] { Creature }).Success);
            fx.Bus.DispatchPending();

            Assert.False(fx.Rules.Skill.IsCasting(Creature));
            Assert.Single(fx.Events.OfType<SkillCastInterruptedEvent>());
        }

        [Fact]
        public void ImmuneInterrupt_CasterStillConsumesCostAndStartsCooldown()
        {
            // 免疫拦截的是"效果落地"，不是"施法本身"——消耗、冷却与伤害免疫同一惯例（06 第 3.6 节
            // 步骤 9：读条/引导完成后先扣资源、进冷却，再依次执行 effects）。
            using var fx = Build();
            fx.Rules.Skill.EffectSink.ApplyAura(Creature, InterruptImmuneAura, Player);
            fx.Bus.DispatchPending();

            var manaBefore = fx.Rules.Powers.GetPower(Player, Mana);
            var cast = fx.Rules.Skill.CastSkill(Player, InterruptSkill, new[] { Creature });
            fx.Bus.DispatchPending();

            Assert.True(cast.Success);
            Assert.Equal(manaBefore - 10, fx.Rules.Powers.GetPower(Player, Mana));

            var again = fx.Rules.Skill.CastSkill(Player, InterruptSkill, new[] { Creature });
            Assert.False(again.Success);
            Assert.Equal(CastFailureReason.OnCooldown, again.Reason);
        }

        [Fact]
        public void ImmuneResult_ObservableViaEffectSinkReturnValue()
        {
            using var fx = Build();
            fx.Rules.Skill.EffectSink.ApplyAura(Creature, InterruptImmuneAura, Player);
            fx.Bus.DispatchPending();

            var immuneContext = new EffectContext(
                Player, Creature, InterruptSkill, EffectKind.Interrupt, Physical, 0, 0,
                new JsonObjectBuilder().Build());
            var immuneResult = fx.Rules.Skill.EffectSink.ApplyEffect(immuneContext);
            Assert.True(immuneResult.Immune);

            var nonImmuneContext = new EffectContext(
                Creature, Player, InterruptSkill, EffectKind.Interrupt, Physical, 0, 0,
                new JsonObjectBuilder().Build());
            var nonImmuneResult = fx.Rules.Skill.EffectSink.ApplyEffect(nonImmuneContext);
            Assert.False(nonImmuneResult.Immune);
        }

        // -----------------------------------------------------------------
        // dispel：免疫时目标身上的可驱散光环不被移除
        // -----------------------------------------------------------------

        [Fact]
        public void NoImmunity_DispelRemovesAura()
        {
            using var fx = Build();
            fx.Rules.Skill.EffectSink.ApplyAura(Creature, DispellableAura, Player);
            fx.Bus.DispatchPending();
            Assert.True(fx.Rules.Skill.AuraQuery.HasAura(Creature, DispellableAura));

            Assert.True(fx.Rules.Skill.CastSkill(Player, DispelSkill, new[] { Creature }).Success);
            fx.Bus.DispatchPending();

            Assert.False(fx.Rules.Skill.AuraQuery.HasAura(Creature, DispellableAura));
        }

        [Fact]
        public void DispelImmunity_PreventsAuraRemoval()
        {
            using var fx = Build();
            fx.Rules.Skill.EffectSink.ApplyAura(Creature, DispellableAura, Player);
            fx.Rules.Skill.EffectSink.ApplyAura(Creature, DispelImmuneAura, Player);
            fx.Bus.DispatchPending();
            Assert.True(fx.Rules.Skill.AuraQuery.IsImmune(Creature, Physical, EffectKind.Dispel));

            Assert.True(fx.Rules.Skill.CastSkill(Player, DispelSkill, new[] { Creature }).Success);
            fx.Bus.DispatchPending();

            Assert.True(fx.Rules.Skill.AuraQuery.HasAura(Creature, DispellableAura));
        }

        // -----------------------------------------------------------------
        // energize：自我目标的效果同样按免疫语义处理（免疫不区分来源，见 EffectDispatcher.ApplyEffect
        // 判断记录"效果免疫统一门"）
        // -----------------------------------------------------------------

        [Fact]
        public void NoImmunity_EnergizeSelf_IncreasesPower()
        {
            using var fx = Build();
            Assert.Equal(0, fx.Rules.Powers.GetPower(Player, TestEnergy));

            Assert.True(fx.Rules.Skill.CastSkill(Player, EnergizeSelfSkill, Array.Empty<Id>()).Success);
            fx.Bus.DispatchPending();

            Assert.Equal(20, fx.Rules.Powers.GetPower(Player, TestEnergy));
        }

        [Fact]
        public void EnergizeImmunity_SelfTarget_BlocksOwnPowerGain()
        {
            using var fx = Build();
            fx.Rules.Skill.EffectSink.ApplyAura(Player, EnergizeImmuneAura, Player);
            fx.Bus.DispatchPending();
            Assert.True(fx.Rules.Skill.AuraQuery.IsImmune(Player, Physical, EffectKind.Energize));

            Assert.True(fx.Rules.Skill.CastSkill(Player, EnergizeSelfSkill, Array.Empty<Id>()).Success);
            fx.Bus.DispatchPending();

            Assert.Equal(0, fx.Rules.Powers.GetPower(Player, TestEnergy));
        }

        // -----------------------------------------------------------------
        // teleport：位移/传送类效果同样受免疫门保护
        // -----------------------------------------------------------------

        [Fact]
        public void NoImmunity_TeleportMovesTarget()
        {
            using var fx = Build();
            var before = fx.Units.GetPosition(Creature);

            Assert.True(fx.Rules.Skill.CastSkill(Player, TeleportSkill, new[] { Creature }).Success);
            fx.Bus.DispatchPending();

            Assert.NotEqual(before, fx.Units.GetPosition(Creature));
            Assert.Equal(new Vec2(9, 9), fx.Units.GetPosition(Creature));
        }

        [Fact]
        public void TeleportImmunity_BlocksPositionChange()
        {
            using var fx = Build();
            fx.Rules.Skill.EffectSink.ApplyAura(Creature, TeleportImmuneAura, Player);
            fx.Bus.DispatchPending();
            Assert.True(fx.Rules.Skill.AuraQuery.IsImmune(Creature, Physical, EffectKind.Teleport));
            var before = fx.Units.GetPosition(Creature);

            Assert.True(fx.Rules.Skill.CastSkill(Player, TeleportSkill, new[] { Creature }).Success);
            fx.Bus.DispatchPending();

            Assert.Equal(before, fx.Units.GetPosition(Creature));
        }

        // -----------------------------------------------------------------
        // 边界：施法者自身内部取消（移动打断）不经 EffectDispatcher，不受"对打断免疫"影响——
        // CastPipeline.NotifyMoved 直接调用 CastPipeline.Interrupt，同一方法也是 ApplyInterrupt
        // 效果原语分支经 _interrupt 回调转调的目标，但走的是两条不同的调用入口，只有后者会先过
        // 本次新增的免疫门（见 EffectDispatcher.ApplyEffect 判断记录）。
        // -----------------------------------------------------------------

        [Fact]
        public void SelfMovementCancel_NotBlockedByOwnInterruptImmunity()
        {
            using var fx = Build();
            fx.Rules.Skill.EffectSink.ApplyAura(Player, InterruptImmuneAura, Player);
            fx.Bus.DispatchPending();
            Assert.True(fx.Rules.Skill.AuraQuery.IsImmune(Player, Physical, EffectKind.Interrupt));

            Assert.True(fx.Rules.Skill.CastSkill(Player, ChannelMovementSkill, Array.Empty<Id>()).Success);
            fx.Bus.DispatchPending();
            Assert.True(fx.Rules.Skill.IsCasting(Player));

            fx.Rules.Skill.NotifyMoved(Player);
            fx.Bus.DispatchPending();

            Assert.False(fx.Rules.Skill.IsCasting(Player));
            var interrupted = Assert.Single(fx.Events.OfType<SkillCastInterruptedEvent>());
            Assert.Equal(Player, interrupted.CasterId);
            Assert.Equal(Player, interrupted.InterrupterId);
        }

        // -----------------------------------------------------------------
        // 世界组装（惯例同 M-C06 前置核验探针 Program.cs.Build，真实 RulesAssembly + CreatureImmunityProvider）
        // -----------------------------------------------------------------

        private sealed class Fixture : IDisposable
        {
            public IEventBus Bus = null!;
            public System.Collections.Generic.List<IEvent> Events = null!;
            public WorldSim World = null!;
            public RulesAssembly Rules = null!;
            public WorldUnitAccess Units = null!;
            public CreatureImmunityProvider StaticImmunity = null!;

            public void Dispose() => World.Dispose();
        }

        private static Fixture Build()
        {
            var events = new System.Collections.Generic.List<IEvent>();
            var definitions = EventKeys.All.Distinct()
                .Select(k => new EventDefinition(k, k.Value.Split('.')[0], Array.Empty<string>()))
                .ToArray();
            var bus = new EventBus(EventCatalog.FromDefinitions(definitions), new EventBusOptions { StrictCatalog = false });
            foreach (var key in EventKeys.All.Distinct())
            {
                bus.Subscribe(key, evt => events.Add(evt));
            }

            var registry = new DataRegistry(BuildData(), bus, RulesSchemaCatalog.CreateOptions());
            RulesSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException("EffectImmunityGateTests 夹具数据未通过校验：" + string.Join("; ", report.Issues));
            }

            var world = new WorldSim(bus);
            var spatial = new EigSpatial();
            var player = new PlayerUnit(Player, Map, FactionPlayer, Class) { Position = new Vec2(0, 0) };
            var creature = new CreatureUnit(Creature, Map, FactionCreature, new Id("creature.eig_template")) { Position = new Vec2(1, 0) };
            world.AddEntity(player);
            world.AddEntity(creature);
            spatial.Register(Player, player.Position, 0.1, new[] { "unit" });
            spatial.Register(Creature, creature.Position, 0.1, new[] { "unit" });

            var rng = new RngHost(20260910UL);
            var units = new WorldUnitAccess(world, spatial);
            var provider = new CreatureImmunityProvider(world);
            var rules = new RulesAssembly(bus, registry, rng, units, spatial, world, staticImmunity: provider);
            rules.RegisterUnit(Player, Class, null, 1);
            rules.RegisterUnit(Creature, Class, null, 1);
            bus.DispatchPending();
            events.Clear();

            return new Fixture
            {
                Bus = bus,
                Events = events,
                World = world,
                Rules = rules,
                Units = units,
                StaticImmunity = provider,
            };
        }

        private static InMemoryDataSource BuildData() => new InMemoryDataSource()
            .Add("stat.definition", "{\"table\":\"stat.definition\",\"schema_version\":1,\"rows\":[{\"id\":\"stat.armor\",\"name_key\":\"l10n.stat.armor\",\"group\":\"secondary\",\"default_base\":0}]}")
            .Add("arch.power_type", "{\"table\":\"arch.power_type\",\"schema_version\":1,\"rows\":["
                + "{\"id\":\"" + Health.Value + "\",\"name_key\":\"l10n.power.health\",\"max_source\":{\"kind\":\"fixed\",\"value\":100},\"regen_in_combat\":0,\"regen_out_of_combat\":0,\"decay_out_of_combat\":0,\"refill_on_leave_combat\":false,\"start_full\":true,\"allow_overflow\":false,\"min\":0},"
                + "{\"id\":\"" + Mana.Value + "\",\"name_key\":\"l10n.power.mana\",\"max_source\":{\"kind\":\"fixed\",\"value\":100},\"regen_in_combat\":0,\"regen_out_of_combat\":0,\"decay_out_of_combat\":0,\"refill_on_leave_combat\":false,\"start_full\":true,\"allow_overflow\":false,\"min\":0},"
                + "{\"id\":\"" + TestEnergy.Value + "\",\"name_key\":\"l10n.power.eig_test_energy\",\"max_source\":{\"kind\":\"fixed\",\"value\":50},\"regen_in_combat\":0,\"regen_out_of_combat\":0,\"decay_out_of_combat\":0,\"refill_on_leave_combat\":false,\"start_full\":false,\"allow_overflow\":false,\"min\":0}"
                + "]}")
            .Add("arch.class", "{\"table\":\"arch.class\",\"schema_version\":1,\"rows\":[{\"id\":\"" + Class.Value + "\",\"name_key\":\"l10n.arch.class.eig_probe\",\"primary_stat\":\"stat.armor\",\"base_stats\":{\"stat.armor\":5},\"power_types\":[\"" + Health.Value + "\",\"" + Mana.Value + "\",\"" + TestEnergy.Value + "\"]}]}")
            .Add("fac.faction", "{\"table\":\"fac.faction\",\"schema_version\":1,\"rows\":[{\"id\":\"" + FactionPlayer.Value + "\",\"name_key\":\"l10n.fac.eig_player\",\"default_reaction\":\"friendly\"},{\"id\":\"" + FactionCreature.Value + "\",\"name_key\":\"l10n.fac.eig_wildlife\",\"default_reaction\":\"neutral\"}]}")
            .Add("fac.reaction_matrix", "{\"table\":\"fac.reaction_matrix\",\"schema_version\":1,\"rows\":[{\"id\":\"fac.eig_r1\",\"from\":\"" + FactionPlayer.Value + "\",\"to\":\"" + FactionCreature.Value + "\",\"reaction\":\"hostile\"},{\"id\":\"fac.eig_r2\",\"from\":\"" + FactionCreature.Value + "\",\"to\":\"" + FactionPlayer.Value + "\",\"reaction\":\"hostile\"}]}")
            .Add("combat.hit_table_config", "{\"table\":\"combat.hit_table_config\",\"schema_version\":1,\"rows\":[{\"id\":\"combat.hit_table.default\",\"miss\":{\"enabled\":false,\"base\":0},\"dodge\":{\"enabled\":false,\"base\":0},\"parry\":{\"enabled\":false,\"base\":0},\"glancing_blow\":{\"enabled\":false,\"base\":0},\"block\":{\"enabled\":false,\"base\":0},\"crit\":{\"enabled\":false,\"base\":0},\"crit_multiplier_base\":2}]}")
            .Add("combat.resist_curve", "{\"table\":\"combat.resist_curve\",\"schema_version\":1,\"rows\":[]}")
            .Add("target.chain_def", "{\"table\":\"target.chain_def\",\"schema_version\":1,\"rows\":["
                + "{\"id\":\"" + ChainHostile.Value + "\",\"source\":\"nearest_in_shape\",\"shape\":{\"kind\":\"circle\",\"radius\":20},\"filters\":[\"relation:hostile\",\"alive\"],\"sort_by\":{\"key\":\"distance\",\"direction\":\"asc\"},\"max_targets\":1},"
                + "{\"id\":\"" + ChainSelf.Value + "\",\"source\":\"self\",\"max_targets\":1}"
                + "]}")
            .Add("skill.def", "{\"table\":\"skill.def\",\"schema_version\":1,\"rows\":["
                + "{\"id\":\"" + LongDamage.Value + "\",\"school\":\"school.physical\",\"kind\":\"active\",\"range\":10,\"cast_time\":2,\"respects_gcd\":false,\"target_shape_ref\":\"" + ChainHostile.Value + "\",\"effects\":[{\"kind\":\"school_damage\",\"params\":{\"base_value\":25,\"coefficient\":0}}]},"
                + "{\"id\":\"" + InterruptSkill.Value + "\",\"school\":\"school.physical\",\"kind\":\"active\",\"range\":10,\"cast_time\":0,\"respects_gcd\":false,\"cost\":[{\"power_type\":\"" + Mana.Value + "\",\"amount\":10}],\"cooldown_duration\":5,\"target_shape_ref\":\"" + ChainHostile.Value + "\",\"effects\":[{\"kind\":\"interrupt\",\"params\":{}}]},"
                + "{\"id\":\"" + DispelSkill.Value + "\",\"school\":\"school.physical\",\"kind\":\"active\",\"range\":10,\"cast_time\":0,\"respects_gcd\":false,\"target_shape_ref\":\"" + ChainHostile.Value + "\",\"effects\":[{\"kind\":\"dispel\",\"params\":{\"category\":\"" + DispelType.Value + "\",\"count\":1}}]},"
                + "{\"id\":\"" + EnergizeSelfSkill.Value + "\",\"school\":\"school.physical\",\"kind\":\"active\",\"range\":0,\"cast_time\":0,\"respects_gcd\":false,\"target_shape_ref\":\"" + ChainSelf.Value + "\",\"effects\":[{\"kind\":\"energize\",\"params\":{\"power_type\":\"" + TestEnergy.Value + "\",\"amount\":20}}]},"
                + "{\"id\":\"" + TeleportSkill.Value + "\",\"school\":\"school.physical\",\"kind\":\"active\",\"range\":10,\"cast_time\":0,\"respects_gcd\":false,\"target_shape_ref\":\"" + ChainHostile.Value + "\",\"effects\":[{\"kind\":\"teleport\",\"params\":{\"point\":{\"x\":9,\"y\":9}}}]},"
                + "{\"id\":\"" + ChannelMovementSkill.Value + "\",\"school\":\"school.physical\",\"kind\":\"active\",\"range\":0,\"cast_time\":3,\"respects_gcd\":false,\"target_shape_ref\":\"" + ChainSelf.Value + "\",\"interrupt_flags\":[\"movement\"],\"effects\":[]}"
                + "]}")
            .Add("skill.aura_def", "{\"table\":\"skill.aura_def\",\"schema_version\":1,\"rows\":["
                + "{\"id\":\"" + InterruptImmuneAura.Value + "\",\"duration\":30,\"max_stacks\":1,\"effects\":[{\"kind\":\"immunity\",\"params\":{\"effect_kinds\":[\"interrupt\"]}}]},"
                + "{\"id\":\"" + DispelImmuneAura.Value + "\",\"duration\":30,\"max_stacks\":1,\"effects\":[{\"kind\":\"immunity\",\"params\":{\"effect_kinds\":[\"dispel\"]}}]},"
                + "{\"id\":\"" + EnergizeImmuneAura.Value + "\",\"duration\":30,\"max_stacks\":1,\"effects\":[{\"kind\":\"immunity\",\"params\":{\"effect_kinds\":[\"energize\"]}}]},"
                + "{\"id\":\"" + TeleportImmuneAura.Value + "\",\"duration\":30,\"max_stacks\":1,\"effects\":[{\"kind\":\"immunity\",\"params\":{\"effect_kinds\":[\"teleport\"]}}]},"
                + "{\"id\":\"" + DispellableAura.Value + "\",\"duration\":30,\"max_stacks\":1,\"dispel_type\":\"" + DispelType.Value + "\",\"effects\":[]}"
                + "]}");

        private sealed class EigSpatial : Core.Foundation.EngineAdapter.ISpatialQuery
        {
            private readonly System.Collections.Generic.Dictionary<Id, (Vec2 p, double r, string[] tags)> _items = new();

            public System.Collections.Generic.IReadOnlyList<Id> QueryRadius(Vec2 c, double r, Core.Foundation.EngineAdapter.QueryFilter f) => Query(f);
            public System.Collections.Generic.IReadOnlyList<Id> QueryCone(Vec2 o, double d, double a, double r, Core.Foundation.EngineAdapter.QueryFilter f) => Query(f);
            public System.Collections.Generic.IReadOnlyList<Id> QueryLine(Vec2 a, Vec2 b, Core.Foundation.EngineAdapter.QueryFilter f) => Query(f);
            public System.Collections.Generic.IReadOnlyList<Id> QueryRect(Vec2 a, Vec2 b, Core.Foundation.EngineAdapter.QueryFilter f) => Query(f);
            public System.Collections.Generic.IReadOnlyList<Id> QueryShape(Core.Foundation.EngineAdapter.Shape s, Core.Foundation.EngineAdapter.QueryFilter f) => Query(f);
            public Id? Nearest(Vec2 p, Core.Foundation.EngineAdapter.QueryFilter f) => Query(f).FirstOrDefault();
            public bool HasLineOfSight(Vec2 a, Vec2 b) => true;
            public void Register(Id id, Vec2 p, double r, System.Collections.Generic.IReadOnlyList<string> tags) => _items[id] = (p, r, tags.ToArray());
            public void UpdatePosition(Id id, Vec2 p) { if (_items.TryGetValue(id, out var x)) _items[id] = (p, x.r, x.tags); }
            public void Unregister(Id id) => _items.Remove(id);
            public void Clear() => _items.Clear();

            private System.Collections.Generic.IReadOnlyList<Id> Query(Core.Foundation.EngineAdapter.QueryFilter f) =>
                _items.Where(x => f.RequiredTags.All(t => x.Value.tags.Contains(t)) && !f.ExcludedTags.Any(t => x.Value.tags.Contains(t)))
                    .Select(x => x.Key).OrderBy(x => x.Value, StringComparer.Ordinal).ToArray();
        }
    }
}
