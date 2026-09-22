using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// ADR-0069 落地验收（消费方反馈——游戏接入方第十四批）：<c>InteractionTargetRegistry</c> 此前
    /// 对生物候选只核对"存在且存活"（ADR-0065），不看它有没有任何可交互内容——护送/跟随/闲逛一类
    /// 没有配置 <c>gossip_menu_ref</c> 的生物离玩家最近时会被误选中，玩家按交互键什么也不会发生，
    /// 旁边真正想交互的对象反而够不到。本文件全程走真实 <see cref="GameplayAssembly"/> 生产装配入口
    /// （<c>gameplay.Carriers.InteractionTargets</c>），复现并验证修复。
    /// </summary>
    public sealed class GameplayAssemblyNearestInteractableCreatureContentTests
    {
        private static readonly Id MapId = new Id("world.nearest_interactable_content_test_map");
        private static readonly Id PlayerId = new Id("unit.nearest_interactable_content_test_player");
        private static readonly Id PlayerFactionId = new Id("fac.nearest_interactable_content_test_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.nearest_interactable_content_test_sample");
        private static readonly Id NpcFactionId = new Id("fac.nearest_interactable_content_test_npc");
        private static readonly Id NpcTierId = new Id("creature.tier.nearest_interactable_content_test_sample");
        private static readonly Id DisplayRef = new Id("display.nearest_interactable_content_test");

        private static readonly Id ContentlessTemplateId = new Id("creature.nearest_interactable_content_test_contentless");
        private static readonly Id TalkativeTemplateId = new Id("creature.nearest_interactable_content_test_talkative");
        private static readonly Id GossipMenuId = new Id("dialog.gossip_menu.nearest_interactable_content_test_sample");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string ArchClassRows =
            "[{\"id\": \"arch.class.nearest_interactable_content_test_sample\", " +
            "\"name_key\": \"l10n.arch.class.nearest_interactable_content_test_sample.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, \"power_types\": [\"arch.power.health\"]}]";

        private static string NpcTierRow(Id tierId) =>
            "{\"id\": \"" + tierId.Value + "\", \"name_key\": \"l10n." + tierId.Value.Replace('.', '_') + ".name\"}";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;
        }

        private static Fixture Build()
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

            var templateRows =
                "[" +
                // 无任何交互内容（未配置 gossip_menu_ref）——护送/跟随/闲逛一类生物的最小复现形状。
                "{\"id\": \"" + ContentlessTemplateId.Value + "\", \"name_key\": \"l10n.creature.nearest_interactable_content_test_contentless.name\", " +
                "\"level\": 1, \"tier\": \"" + NpcTierId.Value + "\", \"base_stats\": {}, " +
                "\"faction_id\": \"" + NpcFactionId.Value + "\", \"display_ref\": \"" + DisplayRef.Value + "\"}," +
                // 有对话内容。
                "{\"id\": \"" + TalkativeTemplateId.Value + "\", \"name_key\": \"l10n.creature.nearest_interactable_content_test_talkative.name\", " +
                "\"level\": 1, \"tier\": \"" + NpcTierId.Value + "\", \"base_stats\": {}, " +
                "\"faction_id\": \"" + NpcFactionId.Value + "\", \"display_ref\": \"" + DisplayRef.Value + "\", " +
                "\"gossip_menu_ref\": \"" + GossipMenuId.Value + "\"}" +
                "]";

            var gossipMenuRows =
                "[{\"id\": \"" + GossipMenuId.Value + "\", \"options\": [" +
                "{\"text_key\": \"l10n.gossip.nearest_interactable_content_test_sample.open_shop\", \"actions\": [{\"kind\": \"vendor\"}]}" +
                "]}]";

            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", PowerTypeRows))
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"))
                .Add("creature.tier_definition", Envelope("creature.tier_definition", "[" + NpcTierRow(NpcTierId) + "]"))
                .Add("creature.template", Envelope("creature.template", templateRows))
                .Add("gobj.template", Envelope("gobj.template", "[]"))
                .Add("gobj.lock", Envelope("gobj.lock", "[]"))
                .Add("dialog.gossip_menu", Envelope("dialog.gossip_menu", gossipMenuRows))
                .Add("dialog.story_tree", Envelope("dialog.story_tree", "[]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.nearest_interactable_content_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId,
                vendorOpenRequested: null);

            var player = new PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeSample) { Position = Vec2.Zero };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            return new Fixture { World = world, Gameplay = gameplay, Player = player };
        }

        /// <summary>核心验收：无内容的生物更近（距离 1），有对话内容的生物更远（距离 5）——
        /// <c>TryFindNearest</c> 必须返回后者，不能被"存在且存活但什么都不会发生"的前者挡住。</summary>
        [Fact]
        public void TryFindNearest_ContentlessCreatureCloser_ReturnsFartherCreatureWithContent()
        {
            var fx = Build();

            var contentlessId = fx.Gameplay.Carriers.Creatures.Spawn(ContentlessTemplateId, MapId, new Vec2(1, 0), 0);
            var talkativeId = fx.Gameplay.Carriers.Creatures.Spawn(TalkativeTemplateId, MapId, new Vec2(5, 0), 0);

            var found = fx.Gameplay.Carriers.InteractionTargets.TryFindNearest(PlayerId, null, out var nearest);

            Assert.True(found);
            Assert.Equal(talkativeId, nearest.EntityId);
            Assert.Equal(InteractionTargetKind.Creature, nearest.Kind);
            Assert.NotEqual(contentlessId, nearest.EntityId);
        }

        /// <summary>回归：有对话内容的存活生物仍是候选（不是本次改动误伤了正常场景）。</summary>
        [Fact]
        public void TryFindNearest_CreatureWithContent_IsStillACandidate()
        {
            var fx = Build();

            var talkativeId = fx.Gameplay.Carriers.Creatures.Spawn(TalkativeTemplateId, MapId, new Vec2(2, 0), 0);

            var found = fx.Gameplay.Carriers.InteractionTargets.TryFindNearest(PlayerId, null, out var nearest);

            Assert.True(found);
            Assert.Equal(talkativeId, nearest.EntityId);
        }

        /// <summary>回归（ADR-0065）：死亡生物仍不是候选——即便它配置了对话内容。</summary>
        [Fact]
        public void TryFindNearest_DeadCreatureWithContent_IsNotACandidate()
        {
            var fx = Build();

            var talkativeId = fx.Gameplay.Carriers.Creatures.Spawn(TalkativeTemplateId, MapId, new Vec2(1, 0), 0);
            fx.Gameplay.Carriers.Units.SetAlive(talkativeId, false);

            var found = fx.Gameplay.Carriers.InteractionTargets.TryFindNearest(PlayerId, null, out _);

            Assert.False(found);
        }
    }
}
