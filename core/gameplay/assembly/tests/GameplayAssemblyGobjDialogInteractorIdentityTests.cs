using Adapters.Stub;
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
    /// ADR-0044 落地验收：直接交互一个 <c>on_use: dialog</c> 的 gobj，全链路（<see
    /// cref="Core.Carriers.Gobj.GameObjectHost"/> → 本装配根接线的 <see
    /// cref="Core.Carriers.Gobj.GobjOptions.DialogOpenerWithSource"/> → <see
    /// cref="Core.Gameplay.Dialog.DialogHost.OpenGossip"/> → gossip <c>vendor</c> 动作）应当把触发
    /// 交互的 gobj 实例 id 当作真实、稳定的"交互对象"身份一路透传到下游消费点，而不是像 ADR-0044 之前
    /// 那样被 <c>dialogRef</c>（gossip 菜单 id）顶替——顶替会让 vendor 动作 <c>ref</c> 为空时"按当前
    /// NPC 自身推断"（<c>dialog.gossip_menu.md</c> 第 3.3 节承诺）拿到错误的推断依据。
    /// </summary>
    public sealed class GameplayAssemblyGobjDialogInteractorIdentityTests
    {
        private static readonly Id MapId = new Id("world.gobj_dialog_identity_test_map");
        private static readonly Id PlayerId = new Id("unit.gobj_dialog_identity_test_player");
        private static readonly Id PlayerFactionId = new Id("fac.gobj_dialog_identity_test_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.gobj_dialog_identity_test_sample");
        private static readonly Id SignTemplateId = new Id("gobj.gobj_dialog_identity_test_sign");
        private static readonly Id GossipMenuId = new Id("dialog.gossip_menu.gobj_dialog_identity_test_sample");
        private static readonly Id DisplayRef = new Id("display.gobj_dialog_identity_test");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.gobj_dialog_identity_test_sample" + "\", \"name_key\": \"l10n.arch.class.gobj_dialog_identity_test_sample.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, \"power_types\": [\"arch.power.health\"]}]";

        private static string SignTemplateRow(Id templateId, Id gossipMenuId) =>
            "{\"id\": \"" + templateId.Value + "\", \"name_key\": \"l10n." + templateId.Value.Replace('.', '_') + ".name\", " +
            "\"kind\": \"sign\", \"type_data\": {\"text_key\": \"l10n." + templateId.Value.Replace('.', '_') + ".text\"}, " +
            "\"display_ref\": \"" + DisplayRef.Value + "\", " +
            "\"on_use\": {\"kind\": \"dialog\", \"ref\": \"" + gossipMenuId.Value + "\"}}";

        private const string VendorGossipMenuRowsTemplate =
            "[{\"id\": \"{0}\", \"options\": [" +
            "{\"text_key\": \"l10n.gossip.gobj_dialog_identity_test_sample.open_shop\", \"actions\": [{\"kind\": \"vendor\"}]}" +
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
                .Add("gobj.template", Envelope("gobj.template", "[" + SignTemplateRow(SignTemplateId, GossipMenuId) + "]"))
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
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.gobj_dialog_identity_test")), bus);

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

        /// <summary>核心验收：直接交互 gobj（不手工调用 <c>Dialog.OpenGossip</c>，走
        /// <c>GameObjectHost.Interact</c> → <c>DispatchOnUse</c> → 本装配根接线的
        /// <c>DialogOpenerWithSource</c> 完整链路）后选择 vendor 选项，<c>VendorOpenRequestedCallback</c>
        /// 收到的 <c>npcId</c> 必须等于触发交互的 gobj 实例 id，且明确不等于 gossip 菜单 id
        /// （ADR-0044 之前的权宜实现会把菜单 id 当 npcId 传下去，本断言能把回归复现出来）。</summary>
        [Fact]
        public void InteractWithSignGobj_ChooseVendorOption_ReceivesGobjInstanceIdAsNpcId_NotMenuId()
        {
            Id? openedForNpc = null;
            var fx = Build(vendorOpenRequested: (unitId, npcId) => openedForNpc = npcId);

            var gobjInstanceId = fx.Gameplay.Carriers.GameObjects.Spawn(SignTemplateId, MapId, new Vec2(0, 0), 0);

            var result = fx.Gameplay.Carriers.GameObjectInteractions.Interact(PlayerId, gobjInstanceId);
            Assert.True(result.Success);
            Assert.Equal(Core.Carriers.Common.InteractOutcome.Dialog, result.Outcome);

            var chosen = fx.Gameplay.Dialog.ChooseOption(PlayerId, 0);
            Assert.True(chosen);

            Assert.NotNull(openedForNpc);
            Assert.Equal(gobjInstanceId, openedForNpc!.Value);
            Assert.NotEqual(GossipMenuId, openedForNpc!.Value);
        }
    }
}
