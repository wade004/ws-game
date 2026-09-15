using System;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Gameplay.Difficulty;
using Core.Gameplay.Loot;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>
    /// T-N2-8b（T-N2-8 已知缺口收口；ADR-0032 决策 5/7；README"来源等级接线"判断记录）：
    /// <see cref="CreatureDeathLootListener"/> 构造 <see cref="RollContext"/> 时，
    /// <see cref="RollContext.SourceLevel"/> 取死亡单位当前等级（<see cref="IUnitAccess.GetLevel"/>）、
    /// <see cref="RollContext.ItemLevelOffset"/> 取注入的 <see cref="IDifficultyHost.ItemLevelOffset"/>
    /// ——本文件验证两者真正落到 <see cref="Core.Gameplay.Loot.DroppedLootEntity.Outcomes"/> 的
    /// <see cref="LootRollOutcome.ItemLevel"/> 上（<c>= SourceLevel + ItemLevelOffset</c>），且不
    /// 提供难度宿主时偏移恒为 0。
    /// </summary>
    public sealed class T_N2_8b_CreatureDeathSourceLevelTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class FakeDifficultyHost : IDifficultyHost
        {
            public int ItemLevelOffset { get; set; }

            public Id? CurrentTier => null;

            public DifficultyScope? CurrentScope => null;

            public Id? CurrentMapId => null;

            public bool Apply(Id tierId, DifficultyScope scope, Id? mapId) => false;

            public double LootMultiplier => 1.0;

            public bool AllowMidSwitch => false;
        }

        private static (LootHost Loot, Core.Foundation.SimLoop.IWorldSim World, Core.Carriers.Unit.WorldUnitAccess Units, IEventBus Bus)
            NewFixture()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("item.slot_definition", Envelope("item.slot_definition",
                    "[{\"id\":\"item.slot.n2_8b_dl\",\"name_key\":\"l10n.slot.n2_8b_dl\"}]"))
                .Add("item.quality_definition", Envelope("item.quality_definition",
                    "[{\"id\":\"item.quality.n2_8b_dl\",\"name_key\":\"l10n.quality.n2_8b_dl\"}]"))
                .Add("item.template", Envelope("item.template", "["
                    + "{\"id\":\"item.n2_8b_dl_drop\",\"slot\":\"item.slot.n2_8b_dl\",\"quality\":\"item.quality.n2_8b_dl\",\"item_level\":1,\"display_ref\":\"display.item.n2_8b_dl_drop\",\"stack_size\":10,\"name_key\":\"l10n.item.n2_8b_dl_drop\"}]"))
                .Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name,
                    "[{\"id\":\"loot.n2_8b_dl\",\"groups\":[{\"roll_mode\":\"chance_each\",\"entries\":[" +
                    "{\"ref\":\"item.n2_8b_dl_drop\",\"weight_or_chance\":1.0,\"count_range\":{\"min\":1,\"max\":1}}]}]}]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            registry.RegisterSchema(LootSchemas.Table);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues.Select(i => i.ToString())));

            var inventory = new FakeInventoryHost();
            var world = LootTestSupport.NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var loot = new LootHost(
                registry, new RngHost(1), bus, world, units, inventory, new FakeExprHostFactory(), () => 0.0);

            return (loot, world, units, bus);
        }

        [Fact]
        public void OnUnitDied_WiresSourceLevelAndItemLevelOffset_IntoDroppedOutcomeItemLevel()
        {
            var f = NewFixture();
            var templates = new FakeCreatureTemplateQuery();
            var lootTableId = new Id("loot.n2_8b_dl");
            var monsterTemplateId = new Id("creature.n2_8b_dl_wolf");
            templates.Add(monsterTemplateId, lootTableId);

            var difficulty = new FakeDifficultyHost { ItemLevelOffset = 4 };
            var listener = new CreatureDeathLootListener(
                f.Bus, f.Loot, templates, f.Units, f.World,
                lootMultiplierProvider: null, difficultyHost: difficulty);

            var deathPos = new Vec2(3, 3);
            var creatureId = new Id("creature.n2_8b_dl_inst_1");
            var creature = LootTestSupport.AddCreature(f.World, creatureId, new Id("map.n2_8b_dl"), monsterTemplateId, deathPos);
            creature.Level = 12;

            f.Bus.Enqueue(new UnitDiedEvent(creatureId, killerId: null));
            f.Bus.DispatchPending();

            Assert.Single(f.Loot.ActiveLootIds);
            var lootId = f.Loot.ActiveLootIds[0];
            Assert.True(f.Loot.TryGetDropped(lootId, out var entity));
            var outcome = Assert.Single(entity.Outcomes);
            // 12（死亡生物等级） + 4（难度层 ItemLevelOffset）= 16，且不是掷骰得来——同一份 RngHost
            // 消耗序列与"不接线"前完全一致（README"来源等级接线"判断记录"不新增 RNG 消耗"）。
            Assert.Equal(16, outcome.ItemLevel);
        }

        [Fact]
        public void OnUnitDied_WithoutDifficultyHost_ItemLevelOffsetDefaultsToZero()
        {
            var f = NewFixture();
            var templates = new FakeCreatureTemplateQuery();
            var lootTableId = new Id("loot.n2_8b_dl");
            var monsterTemplateId = new Id("creature.n2_8b_dl_bear");
            templates.Add(monsterTemplateId, lootTableId);

            // 既有 6 参构造函数（未提供难度宿主）：偏移恒为 0，同 IDifficultyHost.ItemLevelOffset
            // 默认接口成员语义。
            var listener = new CreatureDeathLootListener(f.Bus, f.Loot, templates, f.Units, f.World);

            var creatureId = new Id("creature.n2_8b_dl_inst_2");
            var creature = LootTestSupport.AddCreature(f.World, creatureId, new Id("map.n2_8b_dl"), monsterTemplateId, new Vec2(0, 0));
            creature.Level = 7;

            f.Bus.Enqueue(new UnitDiedEvent(creatureId, killerId: null));
            f.Bus.DispatchPending();

            Assert.Single(f.Loot.ActiveLootIds);
            var lootId = f.Loot.ActiveLootIds[0];
            Assert.True(f.Loot.TryGetDropped(lootId, out var entity));
            var outcome = Assert.Single(entity.Outcomes);
            Assert.Equal(7, outcome.ItemLevel);
        }
    }
}
