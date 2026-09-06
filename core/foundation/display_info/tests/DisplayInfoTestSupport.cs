using System.Collections.Generic;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;

namespace Tests.Foundation.DisplayInfo
{
    /// <summary>测试共用夹具：构造登记好本模块事件 key 的 <see cref="IEventBus"/>、
    /// 构造登记好三张 display 表 schema 的 <see cref="DataRegistry"/>（与 sim_loop 的
    /// <c>SimLoopTestSupport</c>、app_lifecycle 的 <c>AppLifecycleTestSupport</c> 同一惯例）。</summary>
    internal static class DisplayInfoTestSupport
    {
        public static IEventBus CreateBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
                new EventDefinition(DisplayInfoEventKeys.Reloaded, "display_info", System.Array.Empty<string>()),
            });

            return new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
        }

        /// <summary>登记 <see cref="DisplaySchemas.All"/>、把 <paramref name="tables"/>（表名 →
        /// 表体 JSON <c>rows</c> 数组文本）各自套上信封后加载，返回已加载完成的 registry 与报告。
        /// <paramref name="rules"/> 为要额外注册的 <see cref="IValidationRule"/>（如
        /// <see cref="DisplayKindFieldGroupRule"/>/<see cref="DisplayMapCoverageRule"/>）。</summary>
        public static (IDataRegistry Registry, ValidationReport Report) BuildRegistry(
            IReadOnlyDictionary<string, string> tables,
            IEnumerable<IValidationRule>? rules = null,
            IEventBus? bus = null)
        {
            var source = new InMemoryDataSource();
            foreach (var kv in tables)
            {
                source.Add(kv.Key, Envelope(kv.Key, kv.Value));
            }

            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, bus ?? CreateBus());

            foreach (var schema in DisplaySchemas.All)
            {
                registry.RegisterSchema(schema);
            }

            if (rules != null)
            {
                foreach (var rule in rules)
                {
                    registry.RegisterValidationRule(rule);
                }
            }

            var report = registry.LoadAll();
            return (registry, report);
        }

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        // ------------------------------------------------------------------
        // 04 第 7.1/7.1.1 节示例记录（原文示例）
        // ------------------------------------------------------------------

        public const string GreyWolfSpriteRow = @"
        {
          ""id"": ""display.grey_wolf"",
          ""category"": ""creature"",
          ""logical_id"": ""creature.grey_wolf"",
          ""kind"": ""sprite"",
          ""sprite_set_id"": ""sprite.creature.wolf_grey"",
          ""direction_count"": 8,
          ""icon_id"": ""icon.creature.wolf_grey"",
          ""sfx_id"": ""sfx.wolf_growl"",
          ""scale"": 1.0,
          ""shadow"": ""blob"",
          ""sort_offset"": 0
        }";

        public const string StoneGolemModelRow = @"
        {
          ""id"": ""display.stone_golem"",
          ""category"": ""creature"",
          ""logical_id"": ""creature.stone_golem"",
          ""kind"": ""model"",
          ""model_ref"": ""model.creature.stone_golem"",
          ""anim_set_ref"": ""display.anim_set.stone_golem"",
          ""icon_id"": ""icon.creature.stone_golem"",
          ""sfx_id"": ""sfx.stone_golem_step"",
          ""scale"": 1.2,
          ""shadow"": ""projected"",
          ""sort_offset"": 0,
          ""sockets"": [""socket.hand_main"", ""socket.hand_off""],
          ""slots"": [""slot.weapon_main""],
          ""default_slot_meshes"": {""slot.weapon_main"": ""mesh.golem_fist""}
        }";

        public const string StoneGolemAnimSetRow = @"
        {
          ""id"": ""display.anim_set.stone_golem"",
          ""clips"": {
            ""attack"": {""resource_ref"": ""anim.stone_golem.attack"", ""events"": [{""name"": ""hit"", ""time_pct"": 0.6}]},
            ""move"": {""resource_ref"": ""anim.stone_golem.move"", ""events"": []}
          }
        }";

        /// <summary>额外的 sprite 记录，覆盖 mirror_pairs/paperdoll_layers/anchor_points 三个可选字段
        /// （04 示例未展示这三个字段，本模块补一条用于测试解析）。</summary>
        public const string PlayerHeroSpriteRow = @"
        {
          ""id"": ""display.player_hero"",
          ""category"": ""creature"",
          ""logical_id"": ""creature.player_hero"",
          ""kind"": ""sprite"",
          ""sprite_set_id"": ""sprite.creature.player_hero"",
          ""direction_count"": 8,
          ""mirror_pairs"": [{""direction_slot"": ""dir.front_side_l"", ""mirror_of"": ""dir.front_side_r"", ""flip_x"": true}],
          ""paperdoll_layers"": [""layer.base"", ""layer.armor""],
          ""anchor_points"": {
            ""hand_main"": {
              ""parent_layer"": ""layer.armor"",
              ""offset"": {""x"": 1.5, ""y"": 0.5},
              ""offset_by_direction"": {""dir.side_r"": {""x"": 2.0, ""y"": 0.5}}
            }
          }
        }";
    }
}
