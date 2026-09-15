using System;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Gameplay.Economy;
using Core.Gameplay.Loot;
using Core.Rules.Common;
using Tests.Gameplay.Economy;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>
    /// 分阶段落地计划 T-N4-7 验收（ADR-0034 决策 3/4；08 第 1.1/7.4 节修订段"货币条目、掉落货币条目
    /// 与入账"）：掉落货币条目（<c>ref</c> 放行 <c>econ.currency</c>、当量数量公式）、拾取不进背包直接
    /// 入账。数量公式 ≥ 2 组（手算）；背包满入账 1 组（背包容量 0 或已满时拾取货币条目仍入账、非货币
    /// 条目按既有语义失败）。
    /// </summary>
    public sealed class T_N4_7_CurrencyLootTests
    {
        private static readonly Id CurrencyId = new Id("econ.currency.sample_coin");
        private static readonly Id PlayerId = new Id("player.sample_1");
        private static readonly Id MapId = new Id("map.sample_1");

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        // 金币基数曲线：等级 1 → 2；等级 10 → 8；等级 30 → 20（线性插值），同 data/_sample/econ/
        // econ.gold_base_curve.json 的样例曲线取值，供手算核对。
        private const string GoldBaseCurveRows =
            "[{\"id\": \"econ.gold_base.default\", \"entries\": [" +
            "{\"x\": 1, \"y\": 2}, {\"x\": 10, \"y\": 8}, {\"x\": 30, \"y\": 20}]}]";

        private const string CurrencyRows =
            "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.coin\", \"display_ref\": \"display.coin\"}]";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public IWorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public FakeInventoryHost Inventory = null!;
            public EconomyHost Economy = null!;
            public LootHost Loot = null!;
        }

        /// <summary>装配一个货币掉落条目所需的最小注册表（<c>loot.table</c> + <c>econ.currency</c> +
        /// <c>econ.gold_base_curve</c>），真实 <see cref="EconomyHost"/> 注入 <see cref="LootHost"/>
        /// 新增的 <c>economyHost</c> 构造参数（T-N4-7 新增重载）。</summary>
        private static Fixture NewFixture(string lootTableRowsJson)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, lootTableRowsJson))
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
            // FakeNumericExprHostFactory（Tests.Gameplay.Economy.EconomyTestSupport 既有假实现）：
            // 本文件不测试任何 buy_price_rule，只是 EconomyHost 构造必填参数，复用既有假实现避免
            // 重复造一份等价的最小 IExprHostFactory。
            var economy = new EconomyHost(registry, bus, inventory, new FakeNumericExprHostFactory());

            var loot = new LootHost(
                registry, new RngHost(1), bus, world, units, inventory, new FakeExprHostFactory(),
                () => 0.0, options: null, diagnostics: null, conditionSchema: null, economyHost: economy);

            return new Fixture { Bus = bus, World = world, Units = units, Inventory = inventory, Economy = economy, Loot = loot };
        }

        // -----------------------------------------------------------------
        // 数量公式（手算）：equivalents（当量） × econ.gold_base_curve(来源等级) × RollContext.Multiplier
        // （既有难度倍率挂载点，落地为 diff.tier.loot_multiplier），见 LootHost.ResolveCurrencyOutcome
        // 判断记录。两组用 count_range.min==max 固定当量、weight_or_chance=1.0 保证必然命中，规避
        // "命中"骰子本身的不确定性，只验证换算结果。
        // -----------------------------------------------------------------

        private const string FixedEquivalentsTable =
            "[{\"id\": \"loot.n4_7_coin\", \"groups\": [{\"roll_mode\": \"chance_each\", \"entries\": [" +
            "{\"ref\": \"econ.currency.sample_coin\", \"weight_or_chance\": 1.0, \"count_range\": {{COUNT}}}" +
            "]}]}]";

        private static string FixedEquivalentsTableWith(int equivalents) =>
            FixedEquivalentsTable.Replace("{{COUNT}}", "{\"min\": " + equivalents + ", \"max\": " + equivalents + "}");

        [Fact]
        public void RollDetailed_CurrencyEntry_Amount_EqualsEquivalentsTimesGoldBaseAtSourceLevel()
        {
            // 手算：当量 1 × 金币基数(等级 10)=8 × Multiplier(默认 1.0) = 8。
            var f = NewFixture(FixedEquivalentsTableWith(1));
            var context = new RollContext(PlayerId, killerId: null, multiplier: 1.0, contextId: null, sourceLevel: 10, itemLevelOffset: 0);

            var outcomes = f.Loot.RollDetailed(new Id("loot.n4_7_coin"), context);

            var outcome = Assert.Single(outcomes);
            Assert.Equal(CurrencyId, outcome.TemplateId);
            Assert.Equal(8, outcome.Count);
            Assert.Null(outcome.QualityId);
            Assert.Empty(outcome.Affixes);
            Assert.Null(outcome.ItemLevel);
        }

        [Fact]
        public void RollDetailed_CurrencyEntry_Amount_ScalesWithEquivalentsAndDifficultyMultiplier()
        {
            // 手算：当量 3 × 金币基数(等级 1)=2 × Multiplier(2.5，模拟 diff.tier.loot_multiplier) = 15。
            var f = NewFixture(FixedEquivalentsTableWith(3));
            var context = new RollContext(PlayerId, killerId: null, multiplier: 2.5, contextId: null, sourceLevel: 1, itemLevelOffset: 0);

            var outcomes = f.Loot.RollDetailed(new Id("loot.n4_7_coin"), context);

            var outcome = Assert.Single(outcomes);
            Assert.Equal(CurrencyId, outcome.TemplateId);
            Assert.Equal(15, outcome.Count);
        }

        // -----------------------------------------------------------------
        // 背包满入账：背包容量为 0（恒满）时，货币条目仍入账（不进背包、不受容量限制）；同一次拾取
        // 里的非货币条目仍按既有语义失败（放不下留在地面），验证货币入账改动没有影响既有满包语义。
        // -----------------------------------------------------------------

        private const string MixedCurrencyAndItemTable =
            "[{\"id\": \"loot.n4_7_mixed\", \"groups\": [{\"roll_mode\": \"chance_each\", \"entries\": [" +
            "{\"ref\": \"econ.currency.sample_coin\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\": 1, \"max\": 1}}," +
            "{\"ref\": \"item.n4_7_ore\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\": 1, \"max\": 1}}" +
            "]}]}]";

        [Fact]
        public void PickUp_ZeroCapacityInventory_StillDepositsCurrency_ButNonCurrencyItemStaysOnGround()
        {
            var f = NewFixture(MixedCurrencyAndItemTable);
            LootTestSupport.AddPlayer(f.World, PlayerId, MapId, new Vec2(0, 0));
            f.Inventory.MaxTotalItems = 0; // 背包容量恒为 0：任何非货币物品都放不下。

            // 手算：当量 1 × 金币基数(等级 10)=8 × Multiplier(1.0) = 8。
            var context = new RollContext(PlayerId, killerId: null, multiplier: 1.0, contextId: null, sourceLevel: 10, itemLevelOffset: 0);
            var outcomes = f.Loot.RollDetailed(new Id("loot.n4_7_mixed"), context);
            var lootId = f.Loot.Drop(MapId, new Vec2(0, 0), outcomes, ownerHint: null);

            var result = f.Loot.PickUp(PlayerId, lootId);

            // 默认 LootPickupPolicy.Partial：货币恒成功入账（不占背包格，不受容量限制），非货币物品
            // 放不下、留在地面——本次拾取因为拿到了货币而不是"一件都没拿到"，返回 Ok。
            Assert.True(result.Success);
            Assert.Equal(8, f.Economy.GetBalance(PlayerId, CurrencyId));
            Assert.Equal(0, f.Inventory.CountOf(PlayerId, new Id("item.n4_7_ore")));

            // 地面掉落物仍存在（非货币条目未拾取成功），且只剩下这一件非货币物品。
            Assert.True(f.Loot.TryGetDropped(lootId, out var entity));
            Assert.Single(entity.Items);
            Assert.Equal(new Id("item.n4_7_ore"), entity.Items[0].TemplateId);
        }

        [Fact]
        public void PickUp_CurrencyOnlyEntity_ZeroCapacityInventory_DepositsCurrencyAndDestroysGroundEntity()
        {
            var f = NewFixture(FixedEquivalentsTableWith(2));
            LootTestSupport.AddPlayer(f.World, PlayerId, MapId, new Vec2(0, 0));
            f.Inventory.MaxTotalItems = 0;

            var context = new RollContext(PlayerId, killerId: null, multiplier: 1.0, contextId: null, sourceLevel: 30, itemLevelOffset: 0);
            var outcomes = f.Loot.RollDetailed(new Id("loot.n4_7_coin"), context);
            var lootId = f.Loot.Drop(MapId, new Vec2(0, 0), outcomes, ownerHint: null);

            var result = f.Loot.PickUp(PlayerId, lootId);

            // 手算：当量 2 × 金币基数(等级 30)=20 × 1.0 = 40。
            Assert.True(result.Success);
            Assert.Equal(40, f.Economy.GetBalance(PlayerId, CurrencyId));
            Assert.False(f.Loot.TryGetDropped(lootId, out _)); // 全部拾完，地面掉落物已销毁。
        }
    }
}
