using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
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
    /// AUD-02 根治（architecture/落地计划/audit-85f1f4f-20260908，P2）：<c>player.vitals</c> 段
    /// （<see cref="PlayerVitalsPersistable"/>）的"缺段清空"回归——修复前 <c>Load</c> 对
    /// <c>JsonNull</c>（本段整体缺失，如旧格式存档）直接 no-op 返回，运行期已经死亡/掉血的状态原样
    /// 保留。夹具复用 <see cref="GameplayAssemblyDeathReloadTests"/> 同款"真实 <see
    /// cref="GameplayAssembly"/> + 真实内容"搭建手法，裁掉本文件不需要的场景路由/死亡策略部分。
    /// </summary>
    public sealed class PlayerVitalsPersistableAud02Tests
    {
        private static readonly Id PlayerId = new Id("unit.vitals_aud02_player");
        private static readonly Id PlayerFactionId = new Id("fac.vitals_aud02_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.vitals_aud02_sample");
        private static readonly Id MapA = new Id("world.vitals_aud02_map_a");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.vitals_aud02_sample" + "\", \"name_key\": \"l10n.arch.class.vitals_aud02_sample.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, " +
            "\"power_types\": [\"arch.power.health\"]}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class Fixture
        {
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
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.vitals_aud02_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId);

            var player = new PlayerUnit(PlayerId, MapA, PlayerFactionId, ArchetypeSample) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);

            return new Fixture { Gameplay = gameplay, Player = player };
        }

        [Fact]
        public void Load_NullData_ResetsToAliveAndFullHealth_EvenIfDeadWithPartialHealth()
        {
            var fx = Build();
            var persistable = new PlayerVitalsPersistable(fx.Player, fx.Gameplay.Carriers.Rules.Powers);

            var powers = fx.Gameplay.Carriers.Rules.Powers;
            var max = powers.GetPowerMax(PlayerId, WellKnownPowers.Health);
            powers.ModifyPower(PlayerId, WellKnownPowers.Health, -(max - 1), PlayerId); // 砍到只剩 1 点。
            fx.Player.Alive = false;

            Assert.False(fx.Player.Alive);
            Assert.Equal(1, powers.GetPower(PlayerId, WellKnownPowers.Health));

            persistable.Load(Core.Foundation.Common.Json.JsonNull.Instance);

            Assert.True(fx.Player.Alive);
            Assert.Equal(max, powers.GetPower(PlayerId, WellKnownPowers.Health));
        }
    }
}
