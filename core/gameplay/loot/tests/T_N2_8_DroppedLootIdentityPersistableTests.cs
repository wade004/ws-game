using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>
    /// T-N2-8（ADR-0032 决策 7/8）：地面掉落物 <see cref="DroppedLootEntity.Outcomes"/> 存读档带身份——
    /// 新格式往返一致，旧存档（缺 <c>qualityId</c>/<c>affixes</c>/<c>itemLevel</c> key）按 T-N2-7
    /// 兼容读取先例缺省为"未额外指定"（消费方回退模板）。
    /// </summary>
    public class T_N2_8_DroppedLootIdentityPersistableTests
    {
        private const string SlotRows = "[{\"id\": \"item.slot.sample_weapon\", \"name_key\": \"l10n.slot.name\"}]";

        private const string QualityRows =
            "[{\"id\": \"item.quality.sample_common\", \"name_key\": \"l10n.quality.common\", \"affix_count\": 1}," +
            "{\"id\": \"item.quality.sample_rare\", \"name_key\": \"l10n.quality.rare\", \"affix_count\": 1}]";

        private const string StatRows =
            "[{\"id\": \"stat.sample_str\", \"name_key\": \"l10n.stat.str\", \"category\": \"primary\"}]";

        private const string AffixRows =
            "[{\"id\": \"item.affix.a1\", \"name_key\": \"l10n.affix.a1\", \"budget_share\": 0.1, " +
            "\"stat_mix\": [{\"stat\": \"stat.sample_str\", \"ratio\": 1.0}], " +
            "\"quality_pool\": \"item.quality.sample_rare\", \"weight\": 1}]";

        private const string TemplateRows =
            "[{\"id\": \"item.sample_gear\", \"slot\": \"item.slot.sample_weapon\", " +
            "\"quality\": \"item.quality.sample_common\", \"item_level\": 12, " +
            "\"display_ref\": \"display.sample\", \"stack_size\": 1, \"name_key\": \"l10n.item.sample_gear\"}]";

        private const string LootTableRows =
            "[{\"id\": \"loot.sample_identity\", \"groups\": [" +
            "{\"roll_mode\": \"chance_each\", \"entries\": [" +
            "{\"ref\": \"item.sample_gear\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\":1,\"max\":1}, " +
            "\"quality_weights\": {\"item.quality.sample_common\": 0, \"item.quality.sample_rare\": 1}}" +
            "]}]}]";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public IWorldSim World = null!;
            public LootHost Host = null!;
        }

        private static Fixture NewFixture(ulong seed = 1)
        {
            var fixture = new Fixture();
            fixture.Bus = LootTestSupport.NewEventBus();
            var registry = LootTestSupport.MakeRegistryWithItems(
                fixture.Bus, LootTableRows, TemplateRows, SlotRows, QualityRows, AffixRows, StatRows);
            fixture.World = LootTestSupport.NewWorld(fixture.Bus);
            var units = new WorldUnitAccess(fixture.World);
            var inventory = new FakeInventoryHost();

            fixture.Host = new LootHost(
                registry, new RngHost(seed), fixture.Bus, fixture.World, units, inventory,
                new FakeExprHostFactory(), () => 0.0);

            return fixture;
        }

        [Fact]
        public void RoundTrip_NewFormat_PreservesQualityAffixesItemLevel()
        {
            var f1 = NewFixture();
            var context = new RollContext(new Id("unit.sample_source"), null, 1.0, null, sourceLevel: 30, itemLevelOffset: 4);
            var outcomes = f1.Host.RollDetailed(new Id("loot.sample_identity"), context);

            // 品质权重把 common 权重压到 0，必然掉出 rare（affix_count=1，池内唯一候选 a1）。
            Assert.Equal(new Id("item.quality.sample_rare"), outcomes[0].QualityId);
            Assert.Single(outcomes[0].Affixes);
            Assert.Equal(new Id("item.affix.a1"), outcomes[0].Affixes[0]);
            Assert.Equal(34, outcomes[0].ItemLevel);

            var lootId = f1.Host.Drop(new Id("map.sample_1"), new Vec2(1, 2), outcomes);

            var persistable1 = new DroppedLootPersistable(f1.Host);
            var saved = persistable1.Save();

            var f2 = NewFixture();
            var persistable2 = new DroppedLootPersistable(f2.Host);
            persistable2.Load(saved);

            Assert.True(f2.Host.TryGetDropped(lootId, out var restored));
            Assert.Single(restored.Items);
            Assert.Single(restored.Outcomes);
            Assert.Equal(outcomes[0], restored.Outcomes[0]);
            Assert.Equal(new Id("item.quality.sample_rare"), restored.Outcomes[0].QualityId);
            Assert.Equal(new Id("item.affix.a1"), restored.Outcomes[0].Affixes[0]);
            Assert.Equal(34, restored.Outcomes[0].ItemLevel);
        }

        [Fact]
        public void RoundTrip_LegacyDropOverload_ResolvesTemplateDefaultIdentity()
        {
            var f1 = NewFixture();
            var items = new[] { new ItemStack(new Id("item.sample_gear"), 2) };
            var lootId = f1.Host.Drop(new Id("map.sample_1"), new Vec2(0, 0), items);

            Assert.True(f1.Host.TryGetDropped(lootId, out var dropped));
            Assert.Single(dropped.Outcomes);
            // 旧签名（无身份可给）：Drop 内部经 ResolveDefaultOutcome 解析出模板缺省（品质=模板品质、
            // 物品等级=模板 item_level、无词缀）。
            Assert.Equal(new Id("item.quality.sample_common"), dropped.Outcomes[0].QualityId);
            Assert.Empty(dropped.Outcomes[0].Affixes);
            Assert.Equal(12, dropped.Outcomes[0].ItemLevel);

            var persistable1 = new DroppedLootPersistable(f1.Host);
            var saved = persistable1.Save();

            var f2 = NewFixture();
            var persistable2 = new DroppedLootPersistable(f2.Host);
            persistable2.Load(saved);

            Assert.True(f2.Host.TryGetDropped(lootId, out var restored));
            Assert.Equal(dropped.Outcomes[0], restored.Outcomes[0]);
        }

        /// <summary>旧存档缺 <c>qualityId</c>/<c>affixes</c>/<c>itemLevel</c> 三个 key（T-N2-8 之前的
        /// 存档格式，或手写的最小 JSON）：读档后按 T-N2-7 兼容读取先例缺省为"未额外指定"
        /// （<c>QualityId=null</c>/<c>Affixes=空</c>/<c>ItemLevel=null</c>），不是报错、也不凭空
        /// 编造模板缺省——消费方需要具体数值时自行回退模板（见 <see cref="LootRollOutcome"/>
        /// 判断记录）。</summary>
        [Fact]
        public void Load_LegacySaveWithoutIdentityKeys_DefaultsToUnspecified()
        {
            var f = NewFixture();

            var legacyItemObj = new JsonObjectBuilder()
                .Add("templateId", new JsonString("item.sample_gear"))
                .Add("count", new JsonNumber(5))
                .Build();
            var legacyEntity = new JsonObjectBuilder()
                .Add("entityId", new JsonString("loot.inst_legacy_1"))
                .Add("mapId", new JsonString("map.sample_1"))
                .Add("position", new JsonObjectBuilder().Add("x", new JsonNumber(0)).Add("y", new JsonNumber(0)).Build())
                .Add("items", new JsonArray(new List<JsonValue> { legacyItemObj }))
                .Build();
            var legacySave = new JsonArray(new List<JsonValue> { legacyEntity });

            var persistable = new DroppedLootPersistable(f.Host);
            persistable.Load(legacySave);

            Assert.True(f.Host.TryGetDropped(new Id("loot.inst_legacy_1"), out var restored));
            Assert.Single(restored.Outcomes);
            Assert.Null(restored.Outcomes[0].QualityId);
            Assert.Empty(restored.Outcomes[0].Affixes);
            Assert.Null(restored.Outcomes[0].ItemLevel);
            Assert.Equal(new Id("item.sample_gear"), restored.Outcomes[0].TemplateId);
            Assert.Equal(5, restored.Outcomes[0].Count);
        }
    }
}
