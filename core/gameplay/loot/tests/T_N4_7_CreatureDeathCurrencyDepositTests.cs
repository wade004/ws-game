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
            // 2026-09-16 深度复审 D-M1 根治后：CreatureDeathLootListener.OnUnitDied 在货币入账前核对
            // 击杀者 SourceKind 是否为 Player——本用例本意就是"击杀者是玩家"，之前只是一个裸 Id、未
            // 真正登记为世界里的玩家实体也能通过（因为修复前压根没有这一核对），现在必须显式登记为
            // PlayerUnit，否则 GetSourceKind 会退化为 Unknown，与本用例想验证的场景不符。
            LootTestSupport.AddPlayer(f.World, killerId, new Id("map.n4_7"), new Vec2(0, 0));

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

        // -----------------------------------------------------------------
        // 2026-09-16 深度复审 D-M1 回归：OnKill 策略下击杀者身份核对 + 召唤物归属解析。修复前本类
        // 对 evt.KillerId 只要有值就无条件 Add 进它自己的钱包，不判断是否是玩家、也不做召唤物→主人
        // 解析——下面两条用例分别复现"生物互殺"与"玩家召唤物击杀"两个此前完全未被覆盖的场景。
        // -----------------------------------------------------------------

        /// <summary>击杀者是非玩家生物（生物互殺）：金币不应入账进这个生物的钱包（该钱包通常几秒后
        /// 就随生物一起被清理，玩家永远没有机会访问），应整条退回 GroundPickup 语义——落地成地面
        /// 掉落物，与"击杀者为空"的既有处理口径一致。</summary>
        [Fact]
        public void OnUnitDied_OnKillPolicy_KillerIsNonPlayerCreature_FallsBackToGroundLoot_NotDepositedIntoCreatureWallet()
        {
            var f = NewFixture(); // 默认 EconomyOptions.DepositPolicy = OnKill。
            var templates = new FakeCreatureTemplateQuery();
            var lootTableId = new Id("loot.n4_7_kill_coin");
            var monsterTemplateId = new Id("creature.n4_7_wolf");
            templates.Add(monsterTemplateId, lootTableId);

            var listener = new CreatureDeathLootListener(
                f.Bus, f.Loot, templates, f.Units, f.World,
                lootMultiplierProvider: null, difficultyHost: null, economyHost: f.Economy, summons: null);

            var creatureId = new Id("creature.n4_7_wolf_inst_4");
            var creature = LootTestSupport.AddCreature(f.World, creatureId, new Id("map.n4_7"), monsterTemplateId, new Vec2(0, 0));
            creature.Level = 10;

            // 击杀者是另一只生物（生物互殺），不是玩家。
            var killerCreatureId = new Id("creature.n4_7_killer_wolf");
            LootTestSupport.AddCreature(f.World, killerCreatureId, new Id("map.n4_7"), monsterTemplateId, new Vec2(1, 0));

            f.Bus.Enqueue(new UnitDiedEvent(creatureId, killerCreatureId));
            f.Bus.DispatchPending();

            Assert.Equal(0, f.Economy.GetBalance(killerCreatureId, CurrencyId)); // 没有静默存进击杀者钱包。
            Assert.Single(f.Loot.ActiveLootIds); // 整条退回落地，不是"部分入账、部分落地"。
            var lootId = f.Loot.ActiveLootIds[0];
            Assert.True(f.Loot.TryGetDropped(lootId, out var entity));
            var outcome = Assert.Single(entity.Outcomes);
            Assert.Equal(CurrencyId, outcome.TemplateId);
            Assert.Equal(8, outcome.Count);
        }

        /// <summary>击杀者是玩家已注册的召唤物：金币应入账进召唤物的<b>主人</b>钱包，不是召唤物自己的
        /// 钱包——同 <c>CreatureDeathXpListener.ResolveCreditUnit</c> 的"召唤物击杀归主人"语义
        /// （ADR-0033 决策 3），两者现在共用同一份 <see
        /// cref="Core.Gameplay.Common.SummonCreditResolver.ResolveCreditUnit"/>。</summary>
        [Fact]
        public void OnUnitDied_OnKillPolicy_KillerIsPlayerSummon_CreditsOwnerWallet_NotSummonWallet()
        {
            var f = NewFixture();
            var templates = new FakeCreatureTemplateQuery();
            var lootTableId = new Id("loot.n4_7_kill_coin");
            var monsterTemplateId = new Id("creature.n4_7_wolf");
            templates.Add(monsterTemplateId, lootTableId);

            var summons = new Tests.Gameplay.ProgressionBridge.ProgressionBridgeTestSupport.FakeSummonHost();
            var listener = new CreatureDeathLootListener(
                f.Bus, f.Loot, templates, f.Units, f.World,
                lootMultiplierProvider: null, difficultyHost: null, economyHost: f.Economy, summons: summons);

            var creatureId = new Id("creature.n4_7_wolf_inst_5");
            var creature = LootTestSupport.AddCreature(f.World, creatureId, new Id("map.n4_7"), monsterTemplateId, new Vec2(0, 0));
            creature.Level = 10;

            var ownerId = new Id("player.n4_7_summon_owner");
            LootTestSupport.AddPlayer(f.World, ownerId, new Id("map.n4_7"), new Vec2(0, 0));
            var summonId = new Id("creature.n4_7_owned_summon");
            LootTestSupport.AddCreature(f.World, summonId, new Id("map.n4_7"), monsterTemplateId, new Vec2(1, 0));
            summons.SetOwner(summonId, ownerId);

            f.Bus.Enqueue(new UnitDiedEvent(creatureId, summonId));
            f.Bus.DispatchPending();

            // 手算同 OnUnitDied_OnKillPolicy_WithKiller_DepositsCurrencyImmediately_NoGroundLoot：
            // 当量 1 × 金币基数(等级 10)=8 × Multiplier(默认 1.0) = 8，入账进召唤物主人的钱包。
            Assert.Equal(8, f.Economy.GetBalance(ownerId, CurrencyId));
            Assert.Equal(0, f.Economy.GetBalance(summonId, CurrencyId)); // 不是召唤物自己的钱包。
            Assert.Empty(f.Loot.ActiveLootIds); // 全部产出都是货币且已入账，没有生成地面掉落物。
        }
    }
}
