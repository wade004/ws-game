using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// 场景卸载级联清理验收（ADR-0016 背景一节联动发现的既有缺口，见
    /// <see cref="GameplayAssembly.LeaveMap"/> 判断记录）：进图生成生物 → 卸载
    /// （<see cref="IWorldSim.ClearAll"/> + <see cref="GameplayAssembly.LeaveMap"/>）→ 再进图
    /// （<see cref="GameplayAssembly.EnterMap"/> 重新触发 <c>spawn.table</c> 生成）→ tick 数十次，
    /// 全程不抛异常，且再进图确实重新生成了生物（证明 <c>SpawnHost.RuntimeState</c> 没有残留
    /// 指向已销毁实体的 <c>EntityId</c>）。
    /// </summary>
    public class GameplayAssemblyMapReloadTests
    {
        private static readonly Id MapId = new Id("world.reload_test_map");
        private static readonly Id PlayerId = new Id("unit.reload_test_player");
        private static readonly Id PlayerFactionId = new Id("fac.reload_test_player");

        private const string StatDefinitionRows = "[" +
            "{\"id\": \"stat.power\", \"name_key\": \"l10n.stat.power.name\", \"group\": \"primary\", \"default_base\": 0}," +
            "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 0}" +
            "]";

        private const string TierDefinitionRows =
            "[{\"id\": \"creature.tier.normal\", \"name_key\": \"l10n.creature.tier.normal.name\", " +
            "\"stat_multiplier\": 1, \"control_immune\": false, \"sort_weight\": 0}]";

        private const string RotationId = "ai.rotation.reload_test";
        private const string RotationRows =
            "[{ \"id\": \"" + RotationId + "\", \"entries\": [" +
            "{ \"priority\": 1, \"condition\": \"false\", \"skill_id\": \"skill.never\" }" +
            "] }]";

        private const string ProfileId = "ai.behavior.reload_test";
        private const string ProfileRows =
            "[{ \"id\": \"" + ProfileId + "\", \"perception_radius\": 5, \"leash_range\": 10, " +
            "\"combat_return_policy\": \"return_to_spawn\", \"rotation_ref\": \"" + RotationId + "\" }]";

        private const string TemplateId = "creature.reload_test_wolf";
        private const string TemplateRows =
            "[{\"id\": \"" + TemplateId + "\", \"name_key\": \"l10n.creature.reload_test_wolf.name\", " +
            "\"level\": 1, \"tier\": \"creature.tier.normal\", " +
            "\"base_stats\": {\"stat.power\": 10, \"stat.max_health\": 100}, " +
            "\"faction_id\": \"fac.reload_test_monster\", \"display_ref\": \"display.reload_test_wolf\", " +
            "\"ai_behavior_ref\": \"" + ProfileId + "\"}]";

        private const string SpawnTableRows =
            "[{\"id\": \"spawn.reload_test.wolf\", \"map_id\": \"" + "world.reload_test_map" + "\", " +
            "\"content_ref\": \"" + TemplateId + "\", \"position\": {\"x\": 1, \"y\": 1}, " +
            "\"respawn_policy\": \"on_map_enter\"}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static GameplayAssembly Build(out WorldSim world)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("creature.tier_definition", Envelope("creature.tier_definition", TierDefinitionRows))
                .Add("creature.template", Envelope("creature.template", TemplateRows))
                .Add("ai.behavior_profile", Envelope("ai.behavior_profile", ProfileRows))
                .Add("ai.rotation", Envelope("ai.rotation", RotationRows))
                .Add("ai.patrol_path", Envelope("ai.patrol_path", "[]"))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("arch.power_type", Envelope("arch.power_type",
                    "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
                    "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, " +
                    "\"start_full\": true}]"))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"))
                .Add("spawn.table", Envelope("spawn.table", SpawnTableRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);

            return new GameplayAssembly(
                bus, registry, rng, world, spatial,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId);
        }

        [Fact]
        public void EnterMap_LeaveMap_ReenterMap_TicksRepeatedly_WithoutThrowing_AndRespawns()
        {
            var assembly = Build(out var world);

            // 1) 首次进图：spawn.table 触发一次生成（on_map_enter）。
            var generated1 = assembly.Spawn.ApplyForMap(MapId);
            Assert.Single(generated1);
            Assert.Equal(1, world.EntityCount);

            // tick 数次，确认新装配的 AI/移动/触发链路空跑不抛异常。
            for (var i = 0; i < 20; i++)
            {
                world.Tick(SimStep.Continuous(0.1));
            }

            // 2) 卸载旧地图：ClearAll 销毁全部实体（含刚生成的生物）+ LeaveMap 清空
            //    AreaTrigger/Spawn 的按地图登记（含 SpawnHost.RuntimeState.EntityId）。
            world.ClearAll();
            assembly.LeaveMap(MapId);

            Assert.Equal(0, world.EntityCount);

            // 3) 再进图：若 SpawnHost.RuntimeState.EntityId 残留指向已销毁实体，on_map_enter
            //    会误判"仍存活"而跳过重新生成（见 GameplayAssembly.LeaveMap 判断记录）。
            var generated2 = assembly.Spawn.ApplyForMap(MapId);
            Assert.Single(generated2);
            Assert.Equal(1, world.EntityCount);

            // 4) 再 tick 数十次：AiHost 订阅 entity.destroyed 清理过旧生物的登记（见
            //    AiHostCascadeCleanupTests），本次新生物走的是全新 RegisterUnit，
            //    全程不应抛出 InvalidOperationException（此前的复现路径）。
            var ex = Record.Exception(() =>
            {
                for (var i = 0; i < 50; i++)
                {
                    world.Tick(SimStep.Continuous(0.1));
                }
            });

            Assert.Null(ex);
        }
    }
}
