using System;
using System.Linq;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Gameplay.Economy;
using Core.Gameplay.Loot;
using Core.Rules.Common;
using Tests.Gameplay.Economy;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>
    /// 分阶段落地计划 T-N4-7 验收（ADR-0034 决策 4；08 第 7.4 节修订段"入账方式为策略配置项：击杀即
    /// 入账（默认）或掉在地上靠近自动拾取"）：<see cref="CreatureDeathLootListener"/> 在
    /// <see cref="CurrencyDepositPolicy.OnKill"/>（默认）策略下把货币掉落条目在死亡结算这一刻直接
    /// 入账给击杀者、不生成地面掉落物；找不到明确击杀者时退回"落地待拾取"（判断记录，设计层裁定
    /// （2026-09-16）：采纳，见该类型 <c>OnUnitDied</c> 判断记录）；<see cref="CurrencyDepositPolicy.GroundPickup"/> 策略下
    /// 恒落地，不在击杀那一刻入账。
    /// </summary>
    public sealed class T_N4_7_CreatureDeathCurrencyDepositTests
    {
        private static readonly Id CurrencyId = new Id("econ.currency.sample_coin");

        private const string GoldBaseCurveRows =
            "[{\"id\": \"econ.gold_base.default\", \"entries\": [{\"x\": 1, \"y\": 2}, {\"x\": 10, \"y\": 8}]}]";

        private const string CurrencyRows =
            "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.coin\", \"display_ref\": \"display.coin\"}]";

        private const string CurrencyOnlyLootTableRows =
            "[{\"id\": \"loot.n4_7_kill_coin\", \"groups\": [{\"roll_mode\": \"chance_each\", \"entries\": [" +
            "{\"ref\": \"econ.currency.sample_coin\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\": 1, \"max\": 1}}" +
            "]}]}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public Core.Foundation.SimLoop.IWorldSim World = null!;
            public Core.Carriers.Unit.WorldUnitAccess Units = null!;
            public LootHost Loot = null!;
            public EconomyHost Economy = null!;
        }

        private static Fixture NewFixture(EconomyOptions? economyOptions = null)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, CurrencyOnlyLootTableRows))
                .Add(EconomySchemas.Currency.Name, Envelope(EconomySchemas.Currency.Name, CurrencyRows))
                .Add(EconomySchemas.GoldBaseCurve.Name, Envelope(EconomySchemas.GoldBaseCurve.Name, GoldBaseCurveRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterSchema(EconomySchemas.Currency);
            registry.RegisterSchema(EconomySchemas.GoldBaseCurve);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues.Select(i => i.ToString())));

            var inventory = new FakeInventoryHost();
            var world = LootTestSupport.NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var economy = new EconomyHost(registry, bus, inventory, new FakeNumericExprHostFactory(), economyOptions);
            var loot = new LootHost(
                registry, new RngHost(1), bus, world, units, inventory, new FakeExprHostFactory(),
                () => 0.0, options: null, diagnostics: null, conditionSchema: null, economyHost: economy);

            return new Fixture { Bus = bus, World = world, Units = units, Loot = loot, Economy = economy };
        }

        [Fact]
        public void OnUnitDied_OnKillPolicy_WithKiller_DepositsCurrencyImmediately_NoGroundLoot()
        {
            var f = NewFixture(); // 默认 EconomyOptions.DepositPolicy = OnKill。
            var templates = new FakeCreatureTemplateQuery();
            var lootTableId = new Id("loot.n4_7_kill_coin");
            var monsterTemplateId = new Id("creature.n4_7_wolf");
            templates.Add(monsterTemplateId, lootTableId);

            var listener = new CreatureDeathLootListener(
                f.Bus, f.Loot, templates, f.Units, f.World,
                lootMultiplierProvider: null, difficultyHost: null, economyHost: f.Economy);

            var creatureId = new Id("creature.n4_7_wolf_inst_1");
            var creature = LootTestSupport.AddCreature(f.World, creatureId, new Id("map.n4_7"), monsterTemplateId, new Vec2(0, 0));
            creature.Level = 10;
            var killerId = new Id("player.n4_7_killer");

            f.Bus.Enqueue(new UnitDiedEvent(creatureId, killerId));
            f.Bus.DispatchPending();

            // 手算：当量 1 × 金币基数(等级 10)=8 × Multiplier(默认 1.0) = 8，直接入账给击杀者。
            Assert.Equal(8, f.Economy.GetBalance(killerId, CurrencyId));
            Assert.Empty(f.Loot.ActiveLootIds); // 全部产出都是货币且已入账，没有生成地面掉落物。
        }

        [Fact]
        public void OnUnitDied_OnKillPolicy_WithoutKiller_FallsBackToGroundLoot_NotDeposited()
        {
            var f = NewFixture();
            var templates = new FakeCreatureTemplateQuery();
            var lootTableId = new Id("loot.n4_7_kill_coin");
            var monsterTemplateId = new Id("creature.n4_7_wolf");
            templates.Add(monsterTemplateId, lootTableId);

            var listener = new CreatureDeathLootListener(
                f.Bus, f.Loot, templates, f.Units, f.World,
                lootMultiplierProvider: null, difficultyHost: null, economyHost: f.Economy);

            var creatureId = new Id("creature.n4_7_wolf_inst_2");
            var creature = LootTestSupport.AddCreature(f.World, creatureId, new Id("map.n4_7"), monsterTemplateId, new Vec2(0, 0));
            creature.Level = 10;

            // 没有明确击杀者（环境死亡）：不会有单位余额可入账，退回落地待拾取。
            f.Bus.Enqueue(new UnitDiedEvent(creatureId, killerId: null));
            f.Bus.DispatchPending();

            Assert.Single(f.Loot.ActiveLootIds);
            var lootId = f.Loot.ActiveLootIds[0];
            Assert.True(f.Loot.TryGetDropped(lootId, out var entity));
            var outcome = Assert.Single(entity.Outcomes);
            Assert.Equal(CurrencyId, outcome.TemplateId);
            Assert.Equal(8, outcome.Count);
        }

        [Fact]
        public void OnUnitDied_GroundPickupPolicy_WithKiller_StillFallsToGround_NotDepositedOnKill()
        {
            var f = NewFixture(new EconomyOptions { DepositPolicy = CurrencyDepositPolicy.GroundPickup });
            var templates = new FakeCreatureTemplateQuery();
            var lootTableId = new Id("loot.n4_7_kill_coin");
            var monsterTemplateId = new Id("creature.n4_7_wolf");
            templates.Add(monsterTemplateId, lootTableId);

            var listener = new CreatureDeathLootListener(
                f.Bus, f.Loot, templates, f.Units, f.World,
                lootMultiplierProvider: null, difficultyHost: null, economyHost: f.Economy);

            var creatureId = new Id("creature.n4_7_wolf_inst_3");
            var creature = LootTestSupport.AddCreature(f.World, creatureId, new Id("map.n4_7"), monsterTemplateId, new Vec2(0, 0));
            creature.Level = 10;
            var killerId = new Id("player.n4_7_killer");

            f.Bus.Enqueue(new UnitDiedEvent(creatureId, killerId));
            f.Bus.DispatchPending();

            Assert.Equal(0, f.Economy.GetBalance(killerId, CurrencyId)); // GroundPickup：击杀不即时入账。
            Assert.Single(f.Loot.ActiveLootIds);
        }
    }
}
