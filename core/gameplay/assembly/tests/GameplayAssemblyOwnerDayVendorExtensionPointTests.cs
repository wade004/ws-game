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
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// owner/day/vendor 装配扩展点根治（architecture/落地计划/audit-85f1f4f-20260908，见 08 号文档
    /// "装配扩展点"一节）：<see cref="GameplayAssembly"/> 构造函数此前对内部装配的 <see
    /// cref="QuestHost"/> 的 <c>ownerResolver</c>/<c>dayProvider</c>、<see
    /// cref="Core.Gameplay.Dialog.DialogHost"/> 的 <c>vendorOpenRequested</c> 恒传 <c>null</c>，
    /// 游戏层无法在不绕开本装配根的前提下接上"击杀归属判定""每日任务按天数重置""gossip 打开商店 UI"
    /// 这三个能力。本文件用真实内容 + 真实事件/调用路径证明：经本方法新增的三个可选构造参数传入的
    /// 回调，确实被转发进真正参与业务判定的内部宿主，不是装配层"收下了参数但没接线"的假象。
    /// </summary>
    public sealed class GameplayAssemblyOwnerDayVendorExtensionPointTests
    {
        private static readonly Id MapId = new Id("world.owner_day_vendor_map");
        private static readonly Id PlayerId = new Id("unit.owner_day_vendor_player");
        private static readonly Id PlayerFactionId = new Id("fac.owner_day_vendor_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.owner_day_vendor_sample");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.power\", \"name_key\": \"l10n.stat.power.name\", \"group\": \"primary\", \"default_base\": 1}," +
            "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.owner_day_vendor_sample" + "\", \"name_key\": \"l10n.arch.class.owner_day_vendor_sample.name\", " +
            "\"primary_stat\": \"stat.power\", \"base_stats\": {}, \"power_types\": [\"arch.power.health\"]}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static InMemoryDataSource BuildBaseDataSource() =>
            new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", PowerTypeRows))
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"));

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;
        }

        private static Fixture Build(
            InMemoryDataSource source,
            System.Func<Id, Id?>? questOwnerResolver = null,
            System.Func<long>? questDayProvider = null,
            Core.Gameplay.Dialog.VendorOpenRequestedCallback? vendorOpenRequested = null)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.owner_day_vendor_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId,
                questOwnerResolver: questOwnerResolver,
                questDayProvider: questDayProvider,
                vendorOpenRequested: vendorOpenRequested);

            var player = new PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeSample) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            return new Fixture { Bus = bus, World = world, Gameplay = gameplay, Player = player };
        }

        // -----------------------------------------------------------------
        // vendorOpenRequested：gossip 菜单 vendor 动作应当真的调用调用方传入的回调。
        // -----------------------------------------------------------------

        private const string VendorGossipMenuRows =
            "[{\"id\": \"dialog.gossip_menu.owner_day_vendor_sample\", \"options\": [" +
            "{\"text_key\": \"l10n.gossip.owner_day_vendor_sample.open_shop\", \"actions\": [{\"kind\": \"vendor\"}]}" +
            "]}]";

        [Fact]
        public void VendorOpenRequested_Forwarded_InvokedOnGossipVendorAction()
        {
            var source = BuildBaseDataSource()
                .Add("dialog.gossip_menu", Envelope("dialog.gossip_menu", VendorGossipMenuRows))
                .Add("dialog.story_tree", Envelope("dialog.story_tree", "[]"));

            Id? openedForNpc = null;
            var fx = Build(source, vendorOpenRequested: (unitId, npcId) => openedForNpc = npcId);

            var npcId = new Id("npc.owner_day_vendor_sample");
            var menuId = new Id("dialog.gossip_menu.owner_day_vendor_sample");
            fx.Gameplay.Dialog.OpenGossip(PlayerId, npcId, menuId);
            var chosen = fx.Gameplay.Dialog.ChooseOption(PlayerId, 0);

            Assert.True(chosen);
            Assert.Equal(npcId, openedForNpc);
        }

        // -----------------------------------------------------------------
        // questOwnerResolver：宠物/召唤物击杀应当能归功给它解析出的主人。
        // -----------------------------------------------------------------

        private static readonly Id KillTargetTemplateId = new Id("creature.owner_day_vendor_target");

        private const string KillQuestRows =
            "[{\"id\": \"quest.owner_day_vendor_kill_sample\", \"title_key\": \"l10n.quest.owner_day_vendor_kill_sample\", " +
            "\"objectives\": [{\"type\": \"kill\", \"target_ref\": \"creature.owner_day_vendor_target\", \"count\": 1}], " +
            "\"start_method\": \"npc_gossip\", \"turn_in_method\": \"npc_gossip\", \"repeatable\": \"none\"}]";

        [Fact]
        public void QuestOwnerResolver_Forwarded_CreditsKillToResolvedOwner()
        {
            var source = BuildBaseDataSource().Add("quest.def", Envelope("quest.def", KillQuestRows));
            var petId = new Id("unit.owner_day_vendor_pet");

            var fx = Build(source, questOwnerResolver: killerId => killerId.Equals(petId) ? PlayerId : (Id?)null);

            var questId = new Id("quest.owner_day_vendor_kill_sample");
            Assert.True(fx.Gameplay.Quest.Accept(PlayerId, questId));

            // "被击杀的目标"只需要是一个带正确 TemplateId 的世界实体（QuestHost.HandleUnitDied 经
            // IUnitAccess.GetTemplateId 查询），复用 PlayerUnit 作为最小可用的具体 Entity 子类型
            // 承载它——本用例不关心它是不是"真的玩家"，只需要它有 TemplateId 且在世界里可查。
            var victim = new PlayerUnit(new Id("unit.owner_day_vendor_victim"), MapId, PlayerFactionId, ArchetypeSample)
            {
                TemplateId = KillTargetTemplateId,
            };
            fx.World.AddEntity(victim);

            fx.Bus.PublishImmediate(new UnitDiedEvent(victim.EntityId, petId, MapId, new Vec2(0, 0)));

            var progress = fx.Gameplay.Quest.GetLog(PlayerId);
            var questProgress = System.Linq.Enumerable.Single(progress, p => p.QuestId.Equals(questId));
            Assert.Equal(QuestState.ObjectivesComplete, questProgress.State);
        }

        [Fact]
        public void QuestOwnerResolver_NotForwarded_KillNotCredited()
        {
            // 对照组：不传 questOwnerResolver（装配层不接线，回到修复前默认行为）时，宠物击杀不应
            // 计入玩家的任务进度——证明上面一条用例的"计入"确实来自转发的回调，不是任务内容本身
            // 就会不看击杀者是谁而记账。
            var source = BuildBaseDataSource().Add("quest.def", Envelope("quest.def", KillQuestRows));
            var petId = new Id("unit.owner_day_vendor_pet");

            var fx = Build(source);

            var questId = new Id("quest.owner_day_vendor_kill_sample");
            Assert.True(fx.Gameplay.Quest.Accept(PlayerId, questId));

            var victim = new PlayerUnit(new Id("unit.owner_day_vendor_victim"), MapId, PlayerFactionId, ArchetypeSample)
            {
                TemplateId = KillTargetTemplateId,
            };
            fx.World.AddEntity(victim);

            fx.Bus.PublishImmediate(new UnitDiedEvent(victim.EntityId, petId, MapId, new Vec2(0, 0)));

            var progress = fx.Gameplay.Quest.GetLog(PlayerId);
            var questProgress = System.Linq.Enumerable.Single(progress, p => p.QuestId.Equals(questId));
            Assert.Equal(QuestState.Active, questProgress.State);
        }

        // -----------------------------------------------------------------
        // questDayProvider：每日任务的"当天不可再接"应当按转发的天数来源判定。
        // -----------------------------------------------------------------

        private const string DailyEscortQuestRows =
            "[{\"id\": \"quest.owner_day_vendor_daily_sample\", \"title_key\": \"l10n.quest.owner_day_vendor_daily_sample\", " +
            "\"objectives\": [{\"type\": \"escort\", \"target_ref\": \"creature.owner_day_vendor_escort_target\", \"count\": 1}], " +
            "\"start_method\": \"npc_gossip\", \"turn_in_method\": \"npc_gossip\", \"repeatable\": \"daily\"}]";

        [Fact]
        public void QuestDayProvider_Forwarded_ControlsDailyRepeatableAvailability()
        {
            var source = BuildBaseDataSource().Add("quest.def", Envelope("quest.def", DailyEscortQuestRows));
            var currentDay = 1L;

            var fx = Build(source, questDayProvider: () => currentDay);

            var questId = new Id("quest.owner_day_vendor_daily_sample");
            Assert.True(fx.Gameplay.Quest.Accept(PlayerId, questId));
            fx.Gameplay.Quest.UpdateProgress(PlayerId, questId, 0, 1);
            Assert.True(fx.Gameplay.Quest.TurnIn(PlayerId, questId));

            // 同一天（转发的 dayProvider 仍返回 1）：不可再接。
            Assert.Equal(QuestState.Unavailable, fx.Gameplay.Quest.GetState(PlayerId, questId));

            // 天数来源推进到第二天：应当重新变为可接（不再是 Unavailable）。
            currentDay = 2;
            Assert.NotEqual(QuestState.Unavailable, fx.Gameplay.Quest.GetState(PlayerId, questId));
        }
    }
}
