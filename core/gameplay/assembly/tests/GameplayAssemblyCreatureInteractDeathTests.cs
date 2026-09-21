using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// ADR-0067 落地验收：消费方反馈第十批——<see cref="Core.Carriers.Creature.CreatureInteractionHost.Interact"/>
    /// 此前不核对目标/发起者是否存活，尸体也能被交互。本文件脱离引擎，全程走真实
    /// <see cref="GameplayAssembly"/> 生产装配入口（<c>gameplay.Carriers.CreatureInteractions</c>/
    /// <c>gameplay.Carriers.CreatureInteractIntents</c> 等价的 <c>"interact"</c> 意图路径），复现
    /// <c>core/rules/combat.Resolver</c> 死亡结算那一刻的最小可观察后果（生命值砍到 0、
    /// <c>Alive=false</c>——同 <c>GameplayAssemblyDeathReloadTests.KillPlayer</c> 既有判断记录同一
    /// 模式，不依赖 <c>Despawn</c>、不依赖完整战斗管线判定），断言直接调用宿主与经
    /// <c>"interact"</c> 意图两条路径对死亡目标/死亡发起者都被拒绝，且不触发任何对话回调。
    /// </summary>
    public sealed class GameplayAssemblyCreatureInteractDeathTests
    {
        private static readonly Id MapId = new Id("world.creature_interact_death_test_map");
        private static readonly Id PlayerId = new Id("unit.creature_interact_death_test_player");
        private static readonly Id PlayerFactionId = new Id("fac.creature_interact_death_test_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.creature_interact_death_test_sample");
        private static readonly Id NpcTemplateId = new Id("creature.creature_interact_death_test_npc");
        private static readonly Id NpcTierId = new Id("creature.tier.creature_interact_death_test_sample");
        private static readonly Id NpcFactionId = new Id("fac.creature_interact_death_test_npc");
        private static readonly Id GossipMenuId = new Id("dialog.gossip_menu.creature_interact_death_test_sample");
        private static readonly Id DisplayRef = new Id("display.creature_interact_death_test");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.creature_interact_death_test_sample" + "\", \"name_key\": \"l10n.arch.class.creature_interact_death_test_sample.name\", " +
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
            "{\"text_key\": \"l10n.gossip.creature_interact_death_test_sample.open_shop\", \"actions\": [{\"kind\": \"vendor\"}]}" +
            "]}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;

            /// <summary>模拟 <c>core/rules/combat.Resolver</c> 死亡结算那一刻的状态变化（同
            /// <c>GameplayAssemblyDeathReloadTests.Fixture.KillPlayer</c> 判断记录）：把目标单位生命值
            /// 砍到 0、置 <c>Alive=false</c>——不 <c>Despawn</c>，运行期实体继续留在世界模拟中。</summary>
            public void Kill(Id unitId)
            {
                var current = Gameplay.Carriers.Rules.Powers.GetPower(unitId, WellKnownPowers.Health);
                if (current > 0)
                {
                    Gameplay.Carriers.Rules.Powers.ModifyPower(unitId, WellKnownPowers.Health, -current, unitId);
                }
                Gameplay.Carriers.Units.SetAlive(unitId, false);
            }
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
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.creature_interact_death_test")), bus);

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

        /// <summary>核心验收 1：真实结算把一个可交互生物打死后，直接调用生产装配入口
        /// <c>gameplay.Carriers.CreatureInteractions.Interact</c>（不经 <c>"interact"</c> 意图，
        /// 复现"鼠标点选生物后按交互键"这条直连生产路径）——必须返回
        /// <see cref="InteractOutcome.TargetDead"/>（<c>Success=false</c>），不触发
        /// <c>VendorOpenRequestedCallback</c>。</summary>
        [Fact]
        public void Interact_TargetCreatureDead_ReturnsTargetDead_DoesNotOpenDialog()
        {
            Id? openedForNpc = null;
            var fx = Build(vendorOpenRequested: (unitId, npcId) => openedForNpc = npcId);

            var creatureInstanceId = fx.Gameplay.Carriers.Creatures.Spawn(NpcTemplateId, MapId, new Vec2(0, 0), 0);
            fx.Kill(creatureInstanceId);

            var result = fx.Gameplay.Carriers.CreatureInteractions.Interact(PlayerId, creatureInstanceId);

            Assert.False(result.Success);
            Assert.Equal(InteractOutcome.TargetDead, result.Outcome);
            Assert.Null(openedForNpc);
        }

        /// <summary>核心验收 2：同一死亡目标经 <c>"interact"</c> 意图（<c>CreatureInteractIntentTickHandler</c>
        /// 分流）同样被拒绝——两条路径共用同一判定，不各写一份。断言可观测量：意图 tick 之后
        /// <c>Dialog.ChooseOption</c> 返回 <c>false</c>（没有任何对话菜单被打开，可选）。</summary>
        [Fact]
        public void InteractIntent_TargetCreatureDead_DoesNotOpenDialog()
        {
            Id? openedForNpc = null;
            var fx = Build(vendorOpenRequested: (unitId, npcId) => openedForNpc = npcId);

            var creatureInstanceId = fx.Gameplay.Carriers.Creatures.Spawn(NpcTemplateId, MapId, new Vec2(0, 0), 0);
            fx.Kill(creatureInstanceId);

            var args = new JsonObjectBuilder().Add("creature_instance_id", new JsonString(creatureInstanceId.Value)).Build();
            fx.World.SubmitIntent(new Intent(PlayerId, "interact", args));
            fx.World.Tick(SimStep.Continuous(0.1));

            Assert.False(fx.Gameplay.Dialog.ChooseOption(PlayerId, 0));
            Assert.Null(openedForNpc);
        }

        /// <summary>核心验收 3：交互发起者已死亡时，即便目标生物存活，交互同样被拒绝——返回
        /// <see cref="InteractOutcome.ActorDead"/>（<c>Success=false</c>），不触发对话回调。</summary>
        [Fact]
        public void Interact_ActorDead_ReturnsActorDead_DoesNotOpenDialog()
        {
            Id? openedForNpc = null;
            var fx = Build(vendorOpenRequested: (unitId, npcId) => openedForNpc = npcId);

            var creatureInstanceId = fx.Gameplay.Carriers.Creatures.Spawn(NpcTemplateId, MapId, new Vec2(0, 0), 0);
            fx.Kill(PlayerId);

            var result = fx.Gameplay.Carriers.CreatureInteractions.Interact(PlayerId, creatureInstanceId);

            Assert.False(result.Success);
            Assert.Equal(InteractOutcome.ActorDead, result.Outcome);
            Assert.Null(openedForNpc);
        }

        /// <summary>回归：存活生物照常交互成功——与
        /// <c>GameplayAssemblyCreatureDialogInteractionTests.InteractIntent_WithCreatureInstanceId_ChooseVendorOption_ReceivesCreatureInstanceIdAsNpcId_NotMenuId</c>
        /// 覆盖同一条路径，本处只断言直连生产入口（不经意图）这一半，避免与既有用例重复断言意图分流
        /// 半段。</summary>
        [Fact]
        public void Interact_AliveTargetAndActor_Succeeds_OpensDialog()
        {
            Id? openedForNpc = null;
            var fx = Build(vendorOpenRequested: (unitId, npcId) => openedForNpc = npcId);

            var creatureInstanceId = fx.Gameplay.Carriers.Creatures.Spawn(NpcTemplateId, MapId, new Vec2(0, 0), 0);

            var result = fx.Gameplay.Carriers.CreatureInteractions.Interact(PlayerId, creatureInstanceId);

            Assert.True(result.Success);
            Assert.Equal(InteractOutcome.Dialog, result.Outcome);
            Assert.Equal(GossipMenuId, result.DispatchedRef!.Value);
        }
    }
}
