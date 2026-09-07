using Adapters.Stub;
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

        // GP-04 复现固件：一个 victory/defeat 恒为 false 的遭遇（不依赖任何 expr 分组查询，只用
        // 字面量），Evaluate 永远不会自行结束——只有靠 LeaveMap 主动终止才会变为不活跃，适合用来
        // 验证"切图后旧地图的遭遇不再被求值"。
        private const string EncounterDefId = "encounter.reload_test";
        private const string EncounterDefRows =
            "[{\"id\": \"" + EncounterDefId + "\", \"units\": [{\"template_ref\": \"" + TemplateId + "\", " +
            "\"position\": {\"x\": 0, \"y\": 0}}], \"victory_condition\": \"false\", \"defeat_condition\": \"false\"}]";

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
                .Add("spawn.table", Envelope("spawn.table", SpawnTableRows))
                .Add("encounter.def", Envelope("encounter.def", EncounterDefRows))
                .Add("encounter.level", Envelope("encounter.level", "[]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var saveSystem = new SaveSystem(new StubFileSystem(), new SaveSystemOptions(new Id("game.reload_test")));

            return new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
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

        /// <summary>
        /// GP-04 复现与回归（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：修复前
        /// <see cref="GameplayAssembly.LeaveMap"/> 只卸载 AreaTrigger/Spawn，从不终止
        /// <see cref="Core.Gameplay.Encounter.IEncounterHost"/> 侧的运行实例——
        /// <see cref="Core.Gameplay.Encounter.EncounterTickHandler"/> 每次仍会枚举
        /// <c>ActiveInstanceIds</c> 全部活跃实例（不按地图过滤），旧地图的遭遇会在玩家已经身处
        /// 新地图时继续被求值。本用例：在旧地图开始一个恒不结束（victory/defeat 恒 false）的遭遇，
        /// 切图（<c>ClearAll</c> + <c>LeaveMap</c>）后断言该实例已被终止、不再出现在
        /// <c>ActiveInstanceIds</c>，且后续 tick 不会再产生任何副作用。
        /// </summary>
        [Fact]
        public void LeaveMap_TerminatesEncounterBoundToOldMap_NoLongerEvaluatedAfterMapSwitch()
        {
            var assembly = Build(out var world);

            var instanceId = assembly.Encounter.Start(new Id(EncounterDefId), MapId, PlayerId);
            Assert.Contains(instanceId, assembly.Encounter.ActiveInstanceIds);
            Assert.True(assembly.Encounter.GetState(instanceId).IsActive);

            // 若不终止：即使 ClearAll 把参战单位清空，Evaluate 本身不依赖那些单位是否还存活
            // （victory/defeat 恒为字面量 false），实例会继续"活着"、继续被下一次 tick 求值——
            // 这正是 GP-04 的复现路径（用新地图上下文求值旧地图的遭遇）。
            world.ClearAll();
            assembly.LeaveMap(MapId);

            Assert.False(assembly.Encounter.GetState(instanceId).IsActive);
            Assert.DoesNotContain(instanceId, assembly.Encounter.ActiveInstanceIds);

            // 进新图、tick 若干次：旧实例已终止，EncounterTickHandler 不会再碰它，全程不抛异常。
            var ex = Record.Exception(() =>
            {
                for (var i = 0; i < 20; i++)
                {
                    world.Tick(SimStep.Continuous(0.1));
                }
            });
            Assert.Null(ex);
            Assert.False(assembly.Encounter.GetState(instanceId).IsActive);
        }
    }
}
