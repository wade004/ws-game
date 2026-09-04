using System.Collections.Generic;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Presentation.VfxSfx.Schema;

namespace Tests.Presentation.VfxSfx
{
    /// <summary>测试共用夹具：构造登记好本模块三张表 schema 的 <see cref="Core.Foundation.DataRegistry.DataRegistry"/>
    /// （同 <c>Core.Foundation.DisplayInfo</c> 模块 <c>DisplayInfoTestSupport</c> 惯例），供
    /// <c>*FromRecord</c> 系列测试与 <see cref="Core.Foundation.DisplayInfo.DisplayInfoRegistry"/>
    /// 构造复用。</summary>
    internal static class VfxSfxTestSupport
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

        public static (IDataRegistry Registry, ValidationReport Report) BuildRegistry(
            IReadOnlyDictionary<string, string> tables,
            IEventBus? bus = null)
        {
            var source = new InMemoryDataSource();
            foreach (var kv in tables)
            {
                source.Add(kv.Key, Envelope(kv.Key, kv.Value));
            }

            var registry = new Core.Foundation.DataRegistry.DataRegistry(
                source,
                bus ?? CreateBus(),
                new DataRegistryOptions { ExprSchema = RulesExprSchemaOrNull() });

            registry.RegisterSchema(VfxSfxSchemas.Vfx);
            registry.RegisterSchema(VfxSfxSchemas.Sfx);
            registry.RegisterSchema(VfxSfxSchemas.WeaponStyle);
            registry.RegisterSchema(DisplaySchemas.Map);
            registry.RegisterSchema(DisplaySchemas.AnimSet);
            registry.RegisterSchema(DisplaySchemas.EquipVisual);

            var report = registry.LoadAll();
            return (registry, report);
        }

        // vfx_sfx 本身不需要 Expr 字段，这里始终返回 null（expr_parsable 校验项在无 schema 时只警告，
        // 不阻断，见 DataRegistryOptions.ExprSchema 注释）；保留这个间接层只是让本文件与
        // feedback_binder 的同名夹具（需要真正的 ExprSchema）形状一致，便于对照阅读。
        private static IExprSchema? RulesExprSchemaOrNull() => null;

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        public const string FireImpactVfxRow = @"
        {
          ""id"": ""vfx.fire_impact"",
          ""category"": ""impact"",
          ""attach_mode"": ""world"",
          ""lifetime"": 1.5,
          ""resource_ref"": ""res.vfx.fire_impact""
        }";

        public const string SwordHitSfxRow = @"
        {
          ""id"": ""sfx.sword_hit"",
          ""layer"": ""combat"",
          ""priority"": 5,
          ""variants"": [""res.sfx.sword_hit_1"", ""res.sfx.sword_hit_2""],
          ""resource_ref"": ""res.sfx.sword_hit_1""
        }";

        public const string GreatswordWeaponStyleRow = @"
        {
          ""id"": ""display.weapon_style.greatsword"",
          ""auto_attack_anim"": ""anim.greatsword.auto_attack"",
          ""cast_anim_override"": {""skill.cleave"": ""anim.greatsword.cleave""},
          ""swing_vfx"": ""vfx.greatsword_swing"",
          ""impact_vfx_override"": {""skill.cleave"": ""vfx.cleave_impact""}
        }";

        public const string GreyWolfDisplayRow = @"
        {
          ""id"": ""display.grey_wolf"",
          ""category"": ""creature"",
          ""logical_id"": ""creature.grey_wolf"",
          ""kind"": ""sprite"",
          ""sprite_set_id"": ""sprite.creature.wolf_grey"",
          ""direction_count"": 8,
          ""vfx_id"": ""vfx.fire_impact"",
          ""sfx_id"": ""sfx.sword_hit""
        }";
    }
}
