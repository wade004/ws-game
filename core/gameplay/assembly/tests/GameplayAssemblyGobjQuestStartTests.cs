using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// 消费方反馈第 9 条落地验收（ADR-0048）：<c>quest.def.start_method</c> 新增 <c>gobj_interact</c>
    /// 取值描述的是一条既有链路——<see cref="Core.Carriers.Gobj.GameObjectHost.Interact"/> 对
    /// <c>kind: quest_object</c> 的 gobj 分发 <c>quest_action_ref</c> 给 <see
    /// cref="Core.Carriers.Gobj.GobjOptions.QuestActionDispatcher"/>，生产装配根接到 <see
    /// cref="Core.Gameplay.Quest.IQuestHost.Accept"/>（见 <c>GameplayAssembly.cs</c> 步骤 16 判断
    /// 记录）。本文件验证这条链路对一条声明 <c>start_method: gobj_interact</c> 的真实 <c>quest.def</c>
    /// 记录确实生效——不是只加了一个没人消费的枚举值：直接交互一个 <c>kind: quest_object</c> 的 gobj
    /// （不手工调用 <c>Quest.Accept</c>），任务状态应从 <see cref="QuestState.Available"/> 转移到
    /// <see cref="QuestState.Active"/>。
    /// </summary>
    public sealed class GameplayAssemblyGobjQuestStartTests
    {
        private static readonly Id MapId = new Id("world.gobj_quest_start_test_map");
        private static readonly Id PlayerId = new Id("unit.gobj_quest_start_test_player");
        private static readonly Id PlayerFactionId = new Id("fac.gobj_quest_start_test_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.gobj_quest_start_test_sample");
        private static readonly Id QuestObjectTemplateId = new Id("gobj.gobj_quest_start_test_marker");
        private static readonly Id QuestId = new Id("quest.gobj_quest_start_test");
        private static readonly Id DisplayRef = new Id("display.gobj_quest_start_test");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.gobj_quest_start_test_sample" + "\", \"name_key\": \"l10n.arch.class.gobj_quest_start_test_sample.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, \"power_types\": [\"arch.power.health\"]}]";

        private static string QuestObjectTemplateRow(Id templateId, Id questId) =>
            "{\"id\": \"" + templateId.Value + "\", \"name_key\": \"l10n." + templateId.Value.Replace('.', '_') + ".name\", " +
            "\"kind\": \"quest_object\", \"type_data\": {\"quest_action_ref\": \"" + questId.Value + "\"}, " +
            "\"display_ref\": \"" + DisplayRef.Value + "\"}";

        private static string QuestDefRow(Id questId) =>
            "{\"id\": \"" + questId.Value + "\", \"title_key\": \"l10n." + questId.Value.Replace('.', '_') + ".title\", " +
            "\"objectives\": [{\"type\": \"kill\", \"target_ref\": \"creature.gobj_quest_start_test_target\", \"count\": 1}], " +
            "\"start_method\": \"gobj_interact\", \"turn_in_method\": \"npc_gossip\", \"repeatable\": \"none\"}";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;
        }

        private static Fixture Build()
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
                .Add("gobj.template", Envelope("gobj.template", "[" + QuestObjectTemplateRow(QuestObjectTemplateId, QuestId) + "]"))
                .Add("gobj.lock", Envelope("gobj.lock", "[]"))
                .Add("dialog.gossip_menu", Envelope("dialog.gossip_menu", "[]"))
                .Add("dialog.story_tree", Envelope("dialog.story_tree", "[]"))
                .Add("quest.def", Envelope("quest.def", "[" + QuestDefRow(QuestId) + "]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.gobj_quest_start_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId);

            var player = new PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeSample) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            return new Fixture { Bus = bus, World = world, Gameplay = gameplay, Player = player };
        }

        /// <summary>核心验收：交互前任务处于 <see cref="QuestState.Available"/>（无前置条件），直接交互
        /// <c>kind: quest_object</c> 的 gobj（不手工调用 <c>Quest.Accept</c>）后任务应转移为
        /// <see cref="QuestState.Active"/>——证明 <c>start_method: gobj_interact</c> 描述的这条链路
        /// 真的能触发任务接取，不是只加了一个没人消费的枚举值。</summary>
        [Fact]
        public void InteractWithQuestObjectGobj_StartsGobjInteractQuest_TransitionsAvailableToActive()
        {
            var fx = Build();
            Assert.Equal(QuestState.Available, fx.Gameplay.Quest.GetState(PlayerId, QuestId));

            var gobjInstanceId = fx.Gameplay.Carriers.GameObjects.Spawn(QuestObjectTemplateId, MapId, new Vec2(0, 0), 0);
            var result = fx.Gameplay.Carriers.GameObjectInteractions.Interact(PlayerId, gobjInstanceId);

            Assert.True(result.Success);
            Assert.Equal(QuestState.Active, fx.Gameplay.Quest.GetState(PlayerId, QuestId));
        }

        /// <summary>再次交互同一个已经把任务变为 Active 的 gobj：<c>IQuestHost.Accept</c> 对已经
        /// Active 的任务返回 false、不产生任何状态变化（<c>QuestHost.Accept</c> 既有语义），交互本身
        /// 仍然成功（<c>InteractOutcome.NoAction</c>，`quest_object` 没有独立的失败态）——验证本链路
        /// 不会因为重复交互而产生异常状态。</summary>
        [Fact]
        public void InteractWithQuestObjectGobj_AfterAlreadyActive_DoesNotChangeQuestState()
        {
            var fx = Build();
            var gobjInstanceId = fx.Gameplay.Carriers.GameObjects.Spawn(QuestObjectTemplateId, MapId, new Vec2(0, 0), 0);
            Assert.True(fx.Gameplay.Carriers.GameObjectInteractions.Interact(PlayerId, gobjInstanceId).Success);
            Assert.Equal(QuestState.Active, fx.Gameplay.Quest.GetState(PlayerId, QuestId));

            var second = fx.Gameplay.Carriers.GameObjectInteractions.Interact(PlayerId, gobjInstanceId);

            Assert.True(second.Success);
            Assert.Equal(QuestState.Active, fx.Gameplay.Quest.GetState(PlayerId, QuestId));
        }
    }
}
