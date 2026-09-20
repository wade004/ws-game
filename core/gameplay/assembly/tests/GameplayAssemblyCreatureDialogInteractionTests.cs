using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
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
    /// ADR-0051 落地验收：生物不必包装成 <c>gobj.template</c> 即可被直接 <c>interact</c>——全链路
    /// 走框架对外的交互入口（提交一条 <c>"interact"</c> 意图，<c>Args</c> 携带
    /// <c>creature_instance_id</c>，经 tick 由本装配根注册的
    /// <see cref="Core.Carriers.Creature.CreatureInteractIntentTickHandler"/> 消费）→
    /// <see cref="Core.Carriers.Creature.CreatureInteractionHost"/> → 本装配根接线的
    /// <see cref="Core.Carriers.Creature.CreatureInteractOptions.GossipOpener"/> →
    /// <see cref="Core.Gameplay.Dialog.DialogHost.OpenGossip"/> → gossip <c>vendor</c> 动作，全程不
    /// 手工调用任何 L3/L4 内部方法。断言的可观测量：<c>VendorOpenRequestedCallback</c> 收到的
    /// <c>npcId</c> 从 <c>null</c> 变为生物实例自身 id（且明确不等于对话菜单 id）——与
    /// <c>GameplayAssemblyGobjDialogInteractorIdentityTests</c> 对 gobj 路径的既有验收是同一种断言
    /// 形状，证明生物侧的透传语义与 ADR-0044 的 gobj 路径完全对称。
    /// </summary>
    public sealed class GameplayAssemblyCreatureDialogInteractionTests
    {
        private static readonly Id MapId = new Id("world.creature_dialog_interact_test_map");
        private static readonly Id PlayerId = new Id("unit.creature_dialog_interact_test_player");
        private static readonly Id PlayerFactionId = new Id("fac.creature_dialog_interact_test_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.creature_dialog_interact_test_sample");
        private static readonly Id NpcTemplateId = new Id("creature.creature_dialog_interact_test_npc");
        private static readonly Id NpcTierId = new Id("creature.tier.creature_dialog_interact_test_sample");
        private static readonly Id NpcFactionId = new Id("fac.creature_dialog_interact_test_npc");
        private static readonly Id GossipMenuId = new Id("dialog.gossip_menu.creature_dialog_interact_test_sample");
        private static readonly Id DisplayRef = new Id("display.creature_dialog_interact_test");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.creature_dialog_interact_test_sample" + "\", \"name_key\": \"l10n.arch.class.creature_dialog_interact_test_sample.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, \"power_types\": [\"arch.power.health\"]}]";

        private static string NpcTierRow(Id tierId) =>
            "{\"id\": \"" + tierId.Value + "\", \"name_key\": \"l10n." + tierId.Value.Replace('.', '_') + ".name\"}";

        private static string NpcTemplateRow(Id templateId, Id tierId, Id factionId, Id gossipMenuId) =>
            "{\"id\": \"" + templateId.Value + "\", \"name_key\": \"l10n." + templateId.Value.Replace('.', '_') + ".name\", " +
            "\"level\": 1, \"tier\": \"" + tierId.Value + "\", \"base_stats\": {}, " +
            "\"faction_id\": \"" + factionId.Value + "\", " +
            "\"display_ref\": \"" + DisplayRef.Value + "\", " +
            "\"gossip_menu_ref\": \"" + gossipMenuId.Value + "\"}";

        private const string VendorGossipMenuRowsTemplate =
            "[{\"id\": \"{0}\", \"options\": [" +
            "{\"text_key\": \"l10n.gossip.creature_dialog_interact_test_sample.open_shop\", \"actions\": [{\"kind\": \"vendor\"}]}" +
            "]}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;
        }

        private static Fixture Build(Core.Gameplay.Dialog.VendorOpenRequestedCallback? vendorOpenRequested)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", PowerTypeRows))
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"))
                .Add("creature.tier_definition", Envelope("creature.tier_definition", "[" + NpcTierRow(NpcTierId) + "]"))
                .Add("creature.template", Envelope("creature.template",
                    "[" + NpcTemplateRow(NpcTemplateId, NpcTierId, NpcFactionId, GossipMenuId) + "]"))
                .Add("gobj.template", Envelope("gobj.template", "[]"))
                .Add("gobj.lock", Envelope("gobj.lock", "[]"))
                .Add("dialog.gossip_menu", Envelope("dialog.gossip_menu",
                    VendorGossipMenuRowsTemplate.Replace("{0}", GossipMenuId.Value)))
                .Add("dialog.story_tree", Envelope("dialog.story_tree", "[]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.creature_dialog_interact_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId,
                vendorOpenRequested: vendorOpenRequested);

            var player = new PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeSample) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            return new Fixture { Bus = bus, World = world, Gameplay = gameplay, Player = player };
        }

        /// <summary>核心验收：走 <c>"interact"</c> 意图（不手工调用
        /// <c>CreatureInteractionHost.Interact</c>，更不手工调用 <c>Dialog.OpenGossip</c>）交互一个
        /// 配置了 <c>gossip_menu_ref</c> 的生物，选择 vendor 选项后，
        /// <c>VendorOpenRequestedCallback</c> 收到的 <c>npcId</c> 必须等于触发交互的生物实例 id，
        /// 且明确不等于对话菜单 id——证明"被交互者实例身份"确实从生物实例一路透传到了下游，不是
        /// 被菜单 id 顶替（同 ADR-0044 对 gobj 路径的既有验收断言形状）。</summary>
        [Fact]
        public void InteractIntent_WithCreatureInstanceId_ChooseVendorOption_ReceivesCreatureInstanceIdAsNpcId_NotMenuId()
        {
            Id? openedForNpc = null;
            var fx = Build(vendorOpenRequested: (unitId, npcId) => openedForNpc = npcId);

            var creatureInstanceId = fx.Gameplay.Carriers.Creatures.Spawn(NpcTemplateId, MapId, new Vec2(0, 0), 0);

            var args = new JsonObjectBuilder().Add("creature_instance_id", new JsonString(creatureInstanceId.Value)).Build();
            fx.World.SubmitIntent(new Intent(PlayerId, "interact", args));
            fx.World.Tick(SimStep.Continuous(0.1));

            Assert.Null(openedForNpc);

            var chosen = fx.Gameplay.Dialog.ChooseOption(PlayerId, 0);
            Assert.True(chosen);

            Assert.NotNull(openedForNpc);
            Assert.Equal(creatureInstanceId, openedForNpc!.Value);
            Assert.NotEqual(GossipMenuId, openedForNpc!.Value);
        }

        /// <summary>诊断侧验收（ADR-0051 决策 5"不允许静默降级"）：生物模板未配置
        /// <c>gossip_menu_ref</c> 时，交互不抛异常、也不打开任何对话，但
        /// <see cref="Core.Carriers.Creature.CreatureInteractionHost.Diagnostics"/> 必须留下一条可见
        /// 记录——断言的可观测量：诊断出口的 Warnings 数量从 0 变为 1。</summary>
        [Fact]
        public void InteractIntent_CreatureWithoutGossipMenuRef_RecordsDiagnosticWarning_AndDoesNotOpenDialog()
        {
            var fx = Build(vendorOpenRequested: null);

            const string bareNpcRowsTemplate =
                "[{\"id\": \"{0}\", \"name_key\": \"l10n.creature_dialog_interact_test_bare.name\", \"level\": 1, " +
                "\"tier\": \"{1}\", \"base_stats\": {}, \"faction_id\": \"{2}\", \"display_ref\": \"{3}\"}]";
            var bareTemplateId = new Id("creature.creature_dialog_interact_test_bare_npc");

            // 独立注册一份不含 gossip_menu_ref 的模板，验证"没有可交互内容"分支——不复用 Build()
            // 里已经注册过的 NpcTemplateId（那份已配置 gossip_menu_ref，服务上一个用例）。
            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", PowerTypeRows))
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"))
                .Add("creature.tier_definition", Envelope("creature.tier_definition", "[" + NpcTierRow(NpcTierId) + "]"))
                .Add("creature.template", Envelope("creature.template",
                    bareNpcRowsTemplate
                        .Replace("{0}", bareTemplateId.Value)
                        .Replace("{1}", NpcTierId.Value)
                        .Replace("{2}", NpcFactionId.Value)
                        .Replace("{3}", DisplayRef.Value)))
                .Add("gobj.template", Envelope("gobj.template", "[]"))
                .Add("gobj.lock", Envelope("gobj.lock", "[]"))
                .Add("dialog.gossip_menu", Envelope("dialog.gossip_menu", "[]"))
                .Add("dialog.story_tree", Envelope("dialog.story_tree", "[]"));

            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.creature_dialog_interact_test_bare")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId,
                vendorOpenRequested: null);

            var player = new PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeSample) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            var diagnostics = Assert.IsType<Core.Carriers.Creature.InMemoryCreatureDiagnostics>(
                gameplay.Carriers.CreatureInteractions.Diagnostics);
            var warningsBefore = diagnostics.Warnings.Count;

            var creatureInstanceId = gameplay.Carriers.Creatures.Spawn(bareTemplateId, MapId, new Vec2(0, 0), 0);

            var args = new JsonObjectBuilder().Add("creature_instance_id", new JsonString(creatureInstanceId.Value)).Build();
            world.SubmitIntent(new Intent(PlayerId, "interact", args));
            world.Tick(SimStep.Continuous(0.1));

            Assert.Equal(warningsBefore + 1, diagnostics.Warnings.Count);
        }
    }
}
