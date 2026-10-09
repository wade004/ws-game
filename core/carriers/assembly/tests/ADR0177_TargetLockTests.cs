using System;
using System.Collections.Generic;
using System.Globalization;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Carriers.Unit;
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
    /// ADR-0177 验收：<c>creature.template.target_lock</c> 声明的"受击自动选中"，以及 <see cref="ITargetLockHost"/> 的显式设置 / 失效清除。
    /// 生产装配级夹具：真实 <see cref="CarriersAssembly"/>（阵营矩阵、召唤宿主、结算管线），伤害有两种来源——
    /// 真实持续伤害光环经 <c>WorldSim.Tick</c> 结算（端到端），以及直接入队的合成 <c>combat.damage_dealt</c>（规则分支的穷举，事件顺序可控）。
    /// 期望值全部由数据与事件算出，不写死裸 id 之外的数字。
    /// </summary>
    public sealed class ADR0177_TargetLockTests
    {
        private static readonly Id MapA = new Id("map.tl_a");
        private static readonly Id MapB = new Id("map.tl_b");
        private static readonly Id TierNormal = new Id("creature.tier.tl_normal");
        private static readonly Id FacHero = new Id("fac.tl_hero");
        private static readonly Id FacFoe = new Id("fac.tl_foe");
        private static readonly Id School = new Id("school.physical");
        private static readonly Id DotAura = new Id("skill.aura_def.tl_dot");

        private static readonly Id TplHero = new Id("creature.tl_hero");            // 开启规则（任何模式）
        private static readonly Id TplHeroTabOnly = new Id("creature.tl_hero_tab"); // 开启规则，仅 tab 模式
        private static readonly Id TplHeroOff = new Id("creature.tl_hero_off");     // 无 target_lock（缺省）
        private static readonly Id TplHeroDisabled = new Id("creature.tl_hero_disabled"); // target_lock 存在但 auto_select_on_hit=false
        private static readonly Id TplFoe = new Id("creature.tl_foe");
        private static readonly Id TplAlly = new Id("creature.tl_ally");            // 与 hero 同阵营

        private const double Dot = 5;

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static string TemplateRow(Id id, Id faction, string extra = "") =>
            "{\"id\": \"" + id.Value + "\", \"name_key\": \"l10n." + id.Value + ".name\", \"level\": 1, \"tier\": \"" + TierNormal.Value + "\", " +
            "\"base_stats\": {}, \"faction_id\": \"" + faction.Value + "\", \"display_ref\": \"display." + id.Value + "\"" + extra + "}";

        private static InMemoryDataSource BuildDataSource()
        {
            var inv = CultureInfo.InvariantCulture;
            var templates = "[" +
                TemplateRow(TplHero, FacHero, ", \"target_lock\": {\"auto_select_on_hit\": true}") + "," +
                TemplateRow(TplHeroTabOnly, FacHero, ", \"target_lock\": {\"auto_select_on_hit\": true, \"modes\": [\"tab\"]}") + "," +
                TemplateRow(TplHeroOff, FacHero) + "," +
                TemplateRow(TplHeroDisabled, FacHero, ", \"target_lock\": {\"auto_select_on_hit\": false}") + "," +
                TemplateRow(TplFoe, FacFoe) + "," +
                TemplateRow(TplAlly, FacHero) +
                "]";
            return new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", "[]"))
                .Add("arch.power_type", Envelope("arch.power_type",
                    "[{\"id\": \"" + WellKnownPowers.Health.Value + "\", \"name_key\": \"l10n.power.health.name\", " +
                    "\"max_source\": {\"kind\": \"fixed\", \"value\": 100}, \"regen_in_combat\": 0, \"regen_out_of_combat\": 0, " +
                    "\"decay_out_of_combat\": 0, \"refill_on_leave_combat\": false, \"start_full\": true, \"allow_overflow\": false, \"min\": 0}]"))
                .Add("fac.faction", Envelope("fac.faction",
                    "[{\"id\": \"" + FacHero.Value + "\", \"name_key\": \"l10n.fac.tl_hero.name\", \"default_reaction\": \"hostile\"}," +
                    "{\"id\": \"" + FacFoe.Value + "\", \"name_key\": \"l10n.fac.tl_foe.name\", \"default_reaction\": \"hostile\"}]"))
                .Add("fac.reaction_matrix", Envelope("fac.reaction_matrix", "[]"))
                .Add(CreatureSchemas.TierDefinition.Name, Envelope(CreatureSchemas.TierDefinition.Name,
                    "[{\"id\": \"" + TierNormal.Value + "\", \"name_key\": \"l10n.creature.tier.tl_normal.name\", \"stat_multiplier\": 1}]"))
                .Add(CreatureSchemas.Template.Name, Envelope(CreatureSchemas.Template.Name, templates))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config",
                    "[{\"id\": \"combat.hit_table.default\", " +
                    "\"miss\": {\"enabled\": false, \"base\": 0}, \"dodge\": {\"enabled\": false, \"base\": 0}, " +
                    "\"parry\": {\"enabled\": false, \"base\": 0}, \"glancing_blow\": {\"enabled\": false, \"base\": 0}, " +
                    "\"block\": {\"enabled\": false, \"base\": 0}, \"crit\": {\"enabled\": false, \"base\": 0}, \"crit_multiplier_base\": 2.0}]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("skill.aura_def", Envelope("skill.aura_def",
                    "[{\"id\": \"" + DotAura.Value + "\", \"duration\": 60, \"max_stacks\": 1, \"effects\": [{\"kind\": \"periodic_damage\", \"params\": {" +
                    "\"interval\": 1.0, \"base_value\": " + Dot.ToString(inv) + ", \"coefficient\": 0, \"school\": \"" + School.Value + "\"}}]}]"))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"));
        }

        private sealed class Harness
        {
            public CarriersAssembly Assembly = null!;
            public EventBus Bus = null!;
            public WorldSim World = null!;
            public List<UnitTargetChangedEvent> Changes = new List<UnitTargetChangedEvent>();

            public ITargetLockHost Lock => Assembly.TargetLock;

            public Id Spawn(Id template, double x = 0, Id? map = null) =>
                Assembly.Creatures.Spawn(template, map ?? MapA, new Vec2(x, 0), 0);

            public void Damage(Id source, Id target, double amount = Dot)
            {
                Bus.Enqueue(new CombatDamageDealtEvent(source, target, School, amount, false, HitResult.Hit));
                Bus.DispatchPending();
            }

            public void Kill(Id unit)
            {
                Assembly.Units.SetAlive(unit, false);
                Bus.Enqueue(new UnitDiedEvent(unit, null));
                Bus.DispatchPending();
            }

            public void Flush() => Bus.DispatchPending();

            public bool Set(Id unit, Id? target)
            {
                var ok = Lock.SetTarget(unit, target);
                Bus.DispatchPending();
                return ok;
            }
        }

        private static Harness Build()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });
            var registry = new DataRegistry(BuildDataSource(), bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            var world = new WorldSim(bus);
            var assembly = new CarriersAssembly(bus, registry, new RngHost(1), world, new StubSpatialQuery());
            var h = new Harness { Assembly = assembly, Bus = bus, World = world };
            bus.Subscribe<UnitTargetChangedEvent>(CarriersEventKeys.UnitTargetChanged, e => h.Changes.Add(e));
            return h;
        }

        // ------------------------------------------------------------------ 规则本体

        [Fact]
        public void NoTarget_HitByHostile_AutoSelectsAttacker_AndPublishesEvent()
        {
            var h = Build();
            var hero = h.Spawn(TplHero);
            var foe = h.Spawn(TplFoe, 3);
            Assert.Null(h.Lock.GetTarget(hero));

            h.Damage(foe, hero);

            Assert.Equal(foe, h.Lock.GetTarget(hero));
            var e = Assert.Single(h.Changes);
            Assert.Equal(hero, e.UnitId);
            Assert.Equal(foe, e.TargetId);
            Assert.Null(e.PreviousTargetId);
            Assert.Equal(TargetChangeCause.AutoHit, e.Cause);
            Assert.Equal(foe, e.SourceId);
        }

        [Fact]
        public void EndToEnd_PeriodicDamageFromRealAura_AutoSelectsCaster()
        {
            var h = Build();
            var hero = h.Spawn(TplHero);
            var foe = h.Spawn(TplFoe, 3);
            h.Assembly.Rules.Skill.EffectSink.ApplyAura(hero, DotAura, foe);

            h.World.Tick(SimStep.Continuous(1.0));
            h.Flush();

            Assert.Equal(foe, h.Lock.GetTarget(hero));
            Assert.Contains(h.Changes, c => c.UnitId == hero && c.TargetId == foe && c.Cause == TargetChangeCause.AutoHit);
        }

        [Fact]
        public void AlreadyHasValidTarget_IsNeverStolen_ByLaterAttacker()
        {
            var h = Build();
            var hero = h.Spawn(TplHero);
            var first = h.Spawn(TplFoe, 3);
            var second = h.Spawn(TplFoe, 6);
            Assert.True(h.Set(hero, first));
            h.Changes.Clear();

            h.Damage(second, hero);

            Assert.Equal(first, h.Lock.GetTarget(hero));
            Assert.Empty(h.Changes);
        }

        [Fact]
        public void StaleTarget_DeadOrGoneOrOtherMap_CountsAsNoTarget()
        {
            var h = Build();
            var hero = h.Spawn(TplHero);
            var foe = h.Spawn(TplFoe, 3);
            var other = h.Spawn(TplFoe, 6);

            // 目标已死亡：目标死亡事件先清除（target_died），随后被打则选中新攻击者。
            Assert.True(h.Set(hero, foe));
            h.Kill(foe);
            Assert.Null(h.Lock.GetTarget(hero));
            Assert.Equal(TargetChangeCause.TargetDied, h.Changes[^1].Cause);
            h.Damage(other, hero);
            Assert.Equal(other, h.Lock.GetTarget(hero));

            // 目标仍"存储"着但已失效（不在同一地图）：同样视为无目标，可被自动选中替换。
            var third = h.Spawn(TplFoe, 9);
            h.World.GetEntity(other)!.MapId = MapB;
            Assert.Null(h.Lock.GetTarget(hero));
            h.Damage(third, hero);
            Assert.Equal(third, h.Lock.GetTarget(hero));
            Assert.Equal(other, h.Changes[^1].PreviousTargetId);
        }

        [Fact]
        public void TargetDespawned_ClearsWithTargetGone()
        {
            var h = Build();
            var hero = h.Spawn(TplHero);
            var foe = h.Spawn(TplFoe, 3);
            Assert.True(h.Set(hero, foe));
            h.Changes.Clear();

            h.Assembly.Creatures.Despawn(foe, "test");
            h.World.Tick(SimStep.Continuous(0.1));
            h.Flush();

            Assert.Null(h.Lock.GetTarget(hero));
            var e = Assert.Single(h.Changes);
            Assert.Null(e.TargetId);
            Assert.Equal(foe, e.PreviousTargetId);
            Assert.Equal(TargetChangeCause.TargetGone, e.Cause);
        }

        [Fact]
        public void SummonAttacker_ResolvesToSummoner_OnlyWhenSummonerIsAliveHostile()
        {
            var h = Build();
            var hero = h.Spawn(TplHero);
            var summoner = h.Spawn(TplFoe, 8);
            var pet = h.Assembly.Summons.Summon(summoner, TplFoe, new Vec2(4, 0));
            Assert.Equal(summoner, h.Assembly.Summons.GetOwner(pet));

            h.Damage(pet, hero);

            Assert.Equal(summoner, h.Lock.GetTarget(hero));          // 归到召唤者，不是召唤物本身
            Assert.Equal(pet, h.Changes[^1].SourceId);               // 事件如实带原始来源

            // 召唤者已死亡：召唤物伤害归到一个死亡单位 → 不选中。
            var hero2 = h.Spawn(TplHero, 1);
            h.Kill(summoner);
            h.Damage(pet, hero2);
            Assert.Null(h.Lock.GetTarget(hero2));
        }

        [Fact]
        public void FriendlyFire_Environment_DeadSource_AndSelfDamage_DoNotSelect()
        {
            var h = Build();
            var hero = h.Spawn(TplHero);
            var ally = h.Spawn(TplAlly, 2);
            var foe = h.Spawn(TplFoe, 4);

            h.Damage(ally, hero);                                    // 友方误伤
            h.Damage(new Id("env.lava"), hero);                      // 环境伤害：来源不是单位
            h.Damage(hero, hero);                                    // 自伤
            Assert.Null(h.Lock.GetTarget(hero));
            Assert.Empty(h.Changes);

            h.Kill(foe);                                             // 持续伤害来源已死亡
            h.Damage(foe, hero);
            Assert.Null(h.Lock.GetTarget(hero));
            Assert.Empty(h.Changes.FindAll(c => c.Cause == TargetChangeCause.AutoHit));
        }

        [Fact]
        public void SameBatch_MultipleAttackers_FirstDispatchedEventWins_Deterministically()
        {
            Id Run(bool aFirst, out Id a, out Id b)
            {
                var h = Build();
                var hero = h.Spawn(TplHero);
                a = h.Spawn(TplFoe, 3);
                b = h.Spawn(TplFoe, 5);
                var (x, y) = aFirst ? (a, b) : (b, a);
                h.Bus.Enqueue(new CombatDamageDealtEvent(x, hero, School, Dot, false, HitResult.Hit));
                h.Bus.Enqueue(new CombatDamageDealtEvent(y, hero, School, Dot, false, HitResult.Hit));
                h.Bus.DispatchPending();
                Assert.Single(h.Changes);
                return h.Lock.GetTarget(hero)!.Value;
            }

            Assert.Equal(Run(true, out var a1, out _), a1);
            var picked = Run(false, out _, out var b2);
            Assert.Equal(b2, picked);
            // 同输入两次一致
            var again = Run(true, out var a3, out _);
            Assert.Equal(a3, again);
        }

        // ------------------------------------------------------------------ 数据开关

        [Fact]
        public void DefaultOff_NoTargetLockField_OrDisabled_NeverAutoSelects()
        {
            var h = Build();
            var off = h.Spawn(TplHeroOff);
            var disabled = h.Spawn(TplHeroDisabled);
            var foe = h.Spawn(TplFoe, 3);

            h.Damage(foe, off);
            h.Damage(foe, disabled);

            Assert.Null(h.Lock.GetTarget(off));
            Assert.Null(h.Lock.GetTarget(disabled));
            Assert.Empty(h.Changes);
        }

        [Fact]
        public void Modes_RuleAppliesOnlyInListedControlMode()
        {
            var h = Build();
            var hero = h.Spawn(TplHeroTabOnly);
            var foe = h.Spawn(TplFoe, 3);

            h.Damage(foe, hero);                                     // 未设置模式标签：声明了 modes 的规则不生效
            Assert.Null(h.Lock.GetTarget(hero));

            h.Lock.SetControlMode(hero, "action");
            h.Damage(foe, hero);
            Assert.Null(h.Lock.GetTarget(hero));

            h.Lock.SetControlMode(hero, "tab");
            h.Damage(foe, hero);
            Assert.Equal(foe, h.Lock.GetTarget(hero));
            Assert.Equal("tab", h.Lock.GetControlMode(hero));

            // 不声明 modes 的规则在任何模式都生效
            var any = h.Spawn(TplHero, 1);
            h.Lock.SetControlMode(any, "action");
            h.Damage(foe, any);
            Assert.Equal(foe, h.Lock.GetTarget(any));
        }

        // ------------------------------------------------------------------ 显式设置

        [Fact]
        public void SetTarget_RejectsDeadOrMissing_AcceptsAnyAliveUnit_AndIsIdempotent()
        {
            var h = Build();
            var hero = h.Spawn(TplHeroOff);
            var foe = h.Spawn(TplFoe, 3);
            var ally = h.Spawn(TplAlly, 4);
            var dead = h.Spawn(TplFoe, 5);
            h.Assembly.Units.SetAlive(dead, false);

            Assert.False(h.Set(hero, dead));
            Assert.False(h.Set(hero, new Id("unit.nobody")));
            Assert.Empty(h.Changes);

            Assert.True(h.Set(hero, foe));
            Assert.True(h.Set(hero, foe));                // 没变：成功但不发事件
            Assert.Single(h.Changes);
            Assert.Equal(TargetChangeCause.Manual, h.Changes[0].Cause);

            Assert.True(h.Set(hero, ally));               // 宿主不管阵营：友方也能选（游戏自己的规则）
            Assert.Equal(ally, h.Lock.GetTarget(hero));
            Assert.True(h.Set(hero, null));
            Assert.True(h.Set(hero, null));
            Assert.Equal(3, h.Changes.Count);
            Assert.Null(h.Changes[^1].TargetId);
            Assert.Equal(ally, h.Changes[^1].PreviousTargetId);
        }

        // ------------------------------------------------------------------ 不变量

        [Fact]
        public void Invariant_RandomEventSequences_NeverStealValidTarget_AndNeverReturnInvalidTarget()
        {
            // 固定种子的伪随机事件序列（确定性）：每一步之后，GetTarget 要么为空、要么指向存活 + 同图单位；
            // 且"自动选中"从不发生在持有者仍有有效目标时（由事件的 PreviousTargetId 判定：auto_hit 的 previous 必须为空或已失效）。
            var h = Build();
            var hero = h.Spawn(TplHero);
            var units = new List<Id>();
            for (var i = 0; i < 6; i++) units.Add(h.Spawn(i % 3 == 0 ? TplAlly : TplFoe, 2 + i));
            var rng = new Random(20261009);

            for (var step = 0; step < 400; step++)
            {
                var before = h.Lock.GetTarget(hero);
                var count = h.Changes.Count;
                var pick = units[rng.Next(units.Count)];
                switch (rng.Next(5))
                {
                    case 0: h.Damage(pick, hero); break;
                    case 1: h.Set(hero, pick); break;
                    case 2: h.Set(hero, null); break;
                    case 3: if (h.Assembly.Units.IsAlive(pick) && rng.Next(4) == 0) h.Kill(pick); break;
                    default: h.Damage(new Id("env.x"), hero); break;
                }

                h.Flush();
                var after = h.Lock.GetTarget(hero);
                if (after.HasValue)
                {
                    Assert.True(h.Assembly.Units.IsAlive(after.Value), $"step {step}: 返回了已死亡目标");
                }

                for (var i = count; i < h.Changes.Count; i++)
                {
                    var c = h.Changes[i];
                    if (c.Cause != TargetChangeCause.AutoHit) continue;
                    Assert.False(before.HasValue && c.PreviousTargetId.HasValue && h.Assembly.Units.IsAlive(c.PreviousTargetId.Value),
                        $"step {step}: 自动选中抢了仍有效的目标");
                    Assert.True(c.TargetId.HasValue && h.Assembly.Units.IsAlive(c.TargetId.Value));
                }

                if (rng.Next(25) == 0) // 偶尔把死掉的补一个新的，保持有活人可选
                {
                    units[rng.Next(units.Count)] = h.Spawn(TplFoe, 20 + step % 7);
                }
            }
        }
    }
}
