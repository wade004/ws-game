using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
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
    /// T-N6-3b（N4 遗留第 7 项；ADR-0034 决策 3 延伸；08 第 7.4 节"怪物掉钱 = 当量 ×
    /// econ.gold_base_curve(怪物等级) × 分档倍率 × diff.tier.loot_multiplier"）：
    /// <c>creature.tier_definition.gold_multiplier</c> 接入 <see cref="LootHost.ResolveCurrencyOutcome"/>
    /// 换算公式——分两层验收：① <see cref="LootHost"/> 单测，直接构造 <see cref="RollContext.TierId"/>
    /// + 注入 <see cref="LootGoldMultiplierProvider"/>，核对换算数量；② 端到端，
    /// <see cref="CreatureDeathLootListener"/> 解析死亡单位分档 id 并透传，验证击杀入账数量按分档
    /// <c>gold_multiplier</c> 缩放（同 <c>T_N4_7_CreatureDeathCurrencyDepositTests</c> 一类"直接构造
    /// 事件驱动监听器"写法）。
    /// </summary>
    public sealed class T_N6_3b_GoldMultiplierLootTests
    {
        private static readonly Id CurrencyId = new Id("econ.currency.sample_coin");
        private static readonly Id PlayerId = new Id("player.n6_3b_1");
        private static readonly Id MapId = new Id("map.n6_3b_1");

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        // 金币基数曲线：等级 10 → 8（同 T_N4_7_CurrencyLootTests 取值，便于手算核对）。
        private const string GoldBaseCurveRows =
            "[{\"id\": \"econ.gold_base.default\", \"entries\": [{\"x\": 1, \"y\": 2}, {\"x\": 10, \"y\": 8}]}]";

        private const string CurrencyRows =
            "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.coin\", \"display_ref\": \"display.coin\"}]";

        private const string FixedEquivalentsTable =
            "[{\"id\": \"loot.n6_3b_coin\", \"groups\": [{\"roll_mode\": \"chance_each\", \"entries\": [" +
            "{\"ref\": \"econ.currency.sample_coin\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\": 1, \"max\": 1}}" +
            "]}]}]";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public Core.Foundation.SimLoop.IWorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public EconomyHost Economy = null!;
            public LootHost Loot = null!;
        }

        /// <summary>装配一个货币掉落条目所需的最小注册表，<paramref name="goldMultiplierProvider"/>
        /// 经 <see cref="LootHost"/> 新增的 14 参构造重载注入（<c>null</c> 时等价于恒 1，见该重载
        /// 判断记录）。</summary>
        private static Fixture NewFixture(LootGoldMultiplierProvider? goldMultiplierProvider)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, FixedEquivalentsTable))
                .Add(EconomySchemas.Currency.Name, Envelope(EconomySchemas.Currency.Name, CurrencyRows))
                .Add(EconomySchemas.GoldBaseCurve.Name, Envelope(EconomySchemas.GoldBaseCurve.Name, GoldBaseCurveRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterSchema(EconomySchemas.Currency);
            registry.RegisterSchema(EconomySchemas.GoldBaseCurve);
            registry.RegisterValidationRule(new LootContentValidationRule());

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = LootTestSupport.NewWorld(bus);
            var units = new WorldUnitAccess(world);
            var inventory = new FakeInventoryHost();
            var economy = new EconomyHost(registry, bus, inventory, new FakeNumericExprHostFactory());

            var loot = new LootHost(
                registry, new RngHost(1), bus, world, units, inventory, new FakeExprHostFactory(),
                () => 0.0, options: null, diagnostics: null, conditionSchema: null, economyHost: economy,
                goldMultiplierProvider: goldMultiplierProvider);

            return new Fixture { Bus = bus, World = world, Units = units, Economy = economy, Loot = loot };
        }

        // -----------------------------------------------------------------
        // ① LootHost 单测：分档 gold_multiplier 直接乘进换算数量。
        // -----------------------------------------------------------------

        [Fact]
        public void RollDetailed_TierGoldMultiplierTwo_DoublesCurrencyAmount()
        {
            var eliteTierId = new Id("creature.tier.n6_3b_elite");
            LootGoldMultiplierProvider provider = tierId =>
                tierId.HasValue && tierId.Value.Equals(eliteTierId) ? 2.0 : 1.0;
            var f = NewFixture(provider);

            // 手算：当量 1 × 金币基数(等级 10)=8 × 分档倍率 2 × Multiplier(默认 1.0) = 16。
            var context = new RollContext(
                PlayerId, killerId: null, multiplier: 1.0, contextId: null,
                sourceLevel: 10, itemLevelOffset: 0, tierId: eliteTierId);

            var outcomes = f.Loot.RollDetailed(new Id("loot.n6_3b_coin"), context);

            var outcome = Assert.Single(outcomes);
            Assert.Equal(CurrencyId, outcome.TemplateId);
            Assert.Equal(16, outcome.Count);
        }

        [Fact]
        public void RollDetailed_TierIdNull_GoldMultiplierProviderInjected_FallsBackToOne()
        {
            var eliteTierId = new Id("creature.tier.n6_3b_elite");
            LootGoldMultiplierProvider provider = tierId =>
                tierId.HasValue && tierId.Value.Equals(eliteTierId) ? 2.0 : 1.0;
            var f = NewFixture(provider);

            // context.TierId 未提供（null）：委托对 null 的返回值按本用例约定也是 1（未解析出分档
            // 信息时退化为"无分档金币加成"，见 RollContext.TierId/LootGoldMultiplierProvider 判断
            // 记录）——手算：1 × 8 × 1 × 1.0 = 8。
            var context = new RollContext(
                PlayerId, killerId: null, multiplier: 1.0, contextId: null, sourceLevel: 10, itemLevelOffset: 0);

            var outcomes = f.Loot.RollDetailed(new Id("loot.n6_3b_coin"), context);

            var outcome = Assert.Single(outcomes);
            Assert.Equal(8, outcome.Count);
        }

        [Fact]
        public void RollDetailed_NoGoldMultiplierProviderInjected_BehavesAsMultiplierOne()
        {
            var f = NewFixture(goldMultiplierProvider: null);

            var context = new RollContext(
                PlayerId, killerId: null, multiplier: 1.0, contextId: null,
                sourceLevel: 10, itemLevelOffset: 0, tierId: new Id("creature.tier.n6_3b_elite"));

            var outcomes = f.Loot.RollDetailed(new Id("loot.n6_3b_coin"), context);

            // 未注入委托（旧 12 参构造重载走的路径）：无论 TierId 是否非空，分档倍率恒 1——
            // 手算：1 × 8 × 1 × 1.0 = 8，同没有 gold_multiplier 字段时的既有行为。
            var outcome = Assert.Single(outcomes);
            Assert.Equal(8, outcome.Count);
        }

        // -----------------------------------------------------------------
        // ② 端到端：CreatureDeathLootListener 解析死亡单位分档 id 并透传给 LootHost。
        // -----------------------------------------------------------------

        private static CreatureTemplate MakeTemplateWithTier(Id templateId, Id lootTableRef, Id tierId)
        {
            var json = "{\"id\": \"" + templateId.Value + "\", \"name_key\": \"l10n.creature.sample.name\", " +
                "\"level\": 1, \"tier\": \"" + tierId.Value + "\", \"base_stats\": {}, " +
                "\"faction_id\": \"fac.test_monster\", \"display_ref\": \"display.sample\", " +
                "\"loot_table_ref\": \"" + lootTableRef.Value + "\"}";
            var obj = (JsonObject)JsonReader.Parse(json);
            var record = new DataRecord(CreatureSchemas.Template, templateId.Value, templateId, obj);
            return CreatureTemplate.FromRecord(record);
        }

        private sealed class FakeCreatureTemplateQueryWithTier : ICreatureTemplateQuery
        {
            private readonly Dictionary<Id, CreatureTemplate> _templates = new Dictionary<Id, CreatureTemplate>();

            public void Add(Id templateId, Id lootTableRef, Id tierId) =>
                _templates[templateId] = MakeTemplateWithTier(templateId, lootTableRef, tierId);

            public CreatureTemplate Get(Id templateId) =>
                _templates.TryGetValue(templateId, out var t) ? t : throw new ArgumentException($"未登记的模板 \"{templateId}\"");

            public bool HasFlag(Id templateId, NpcFlag flag) => false;
        }

        [Fact]
        public void OnUnitDied_EliteTierGoldMultiplier_ScalesDepositedCurrencyAmount()
        {
            var eliteTierId = new Id("creature.tier.n6_3b_elite_e2e");
            LootGoldMultiplierProvider provider = tierId =>
                tierId.HasValue && tierId.Value.Equals(eliteTierId) ? 1.5 : 1.0;
            var f = NewFixture(provider);

            var templates = new FakeCreatureTemplateQueryWithTier();
            var lootTableId = new Id("loot.n6_3b_coin");
            var monsterTemplateId = new Id("creature.n6_3b_elite_wolf");
            templates.Add(monsterTemplateId, lootTableId, eliteTierId);

            _ = new CreatureDeathLootListener(
                f.Bus, f.Loot, templates, f.Units, f.World,
                lootMultiplierProvider: null, difficultyHost: null, economyHost: f.Economy);

            var creatureId = new Id("creature.n6_3b_elite_wolf_inst_1");
            var creature = LootTestSupport.AddCreature(f.World, creatureId, MapId, monsterTemplateId, new Vec2(0, 0));
            creature.Level = 10;
            var killerId = new Id("player.n6_3b_killer");
            // 2026-09-16 深度复审 D-M1 根治后：CreatureDeathLootListener.OnUnitDied 在货币入账前核对
            // 击杀者 SourceKind 是否为 Player（同 T_N4_7_CreatureDeathCurrencyDepositTests 判断
            // 记录），显式登记为世界里的 PlayerUnit，否则 GetSourceKind 会退化为 Unknown。
            LootTestSupport.AddPlayer(f.World, killerId, MapId, new Vec2(0, 0));

            f.Bus.Enqueue(new UnitDiedEvent(creatureId, killerId));
            f.Bus.DispatchPending();

            // 手算：当量 1 × 金币基数(等级 10)=8 × 分档倍率 1.5 × Multiplier(默认 1.0) = 12，
            // OnKill（默认）策略下直接入账给击杀者。
            Assert.Equal(12, f.Economy.GetBalance(killerId, CurrencyId));
            Assert.Empty(f.Loot.ActiveLootIds);
        }

        [Fact]
        public void OnUnitDied_NormalTierWithoutGoldMultiplierField_DepositsUnscaledAmount()
        {
            // creature.tier.n6_3b_normal 未经 goldMultiplierProvider 覆盖，退化为 1（同
            // creature.tier_definition.gold_multiplier 缺省 1 的既有语义）。
            var normalTierId = new Id("creature.tier.n6_3b_normal");
            var eliteTierId = new Id("creature.tier.n6_3b_elite_e2e_2");
            LootGoldMultiplierProvider provider = tierId =>
                tierId.HasValue && tierId.Value.Equals(eliteTierId) ? 1.5 : 1.0;
            var f = NewFixture(provider);

            var templates = new FakeCreatureTemplateQueryWithTier();
            var lootTableId = new Id("loot.n6_3b_coin");
            var monsterTemplateId = new Id("creature.n6_3b_normal_wolf");
            templates.Add(monsterTemplateId, lootTableId, normalTierId);

            _ = new CreatureDeathLootListener(
                f.Bus, f.Loot, templates, f.Units, f.World,
                lootMultiplierProvider: null, difficultyHost: null, economyHost: f.Economy);

            var creatureId = new Id("creature.n6_3b_normal_wolf_inst_1");
            var creature = LootTestSupport.AddCreature(f.World, creatureId, MapId, monsterTemplateId, new Vec2(0, 0));
            creature.Level = 10;
            var killerId = new Id("player.n6_3b_killer_2");
            // 2026-09-16 深度复审 D-M1 根治后：同上一条用例判断记录，显式登记击杀者为世界里的玩家。
            LootTestSupport.AddPlayer(f.World, killerId, MapId, new Vec2(0, 0));

            f.Bus.Enqueue(new UnitDiedEvent(creatureId, killerId));
            f.Bus.DispatchPending();

            // 手算：1 × 8 × 1（未命中分档倍率覆盖）× 1.0 = 8，与 T_N4_7_CurrencyLootTests 既有取值一致。
            Assert.Equal(8, f.Economy.GetBalance(killerId, CurrencyId));
        }
    }
}
