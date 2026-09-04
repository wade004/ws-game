using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.PowerSet;
using Core.Numbers.Progression;
using Core.Numbers.StatBlock;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Creature
{
    /// <summary>
    /// 供 creature 模块测试共用的装配帮助：DataRegistry（stat.definition/creature.tier_definition/
    /// creature.template/prog.level_curve）+ 真实 StatHost/PowerHost/ProgressionHost。
    /// </summary>
    internal static class CreatureTestSupport
    {
        public static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        public const string StatDefinitionRows = "[" +
            "{\"id\": \"stat.power\", \"name_key\": \"l10n.stat.power.name\", \"group\": \"primary\", \"default_base\": 0}," +
            "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 0}" +
            "]";

        public const string TierDefinitionRows = "[" +
            "{\"id\": \"creature.tier.normal\", \"name_key\": \"l10n.creature.tier.normal.name\", " +
            "\"stat_multiplier\": 1, \"control_immune\": false, \"sort_weight\": 0}," +
            "{\"id\": \"creature.tier.elite\", \"name_key\": \"l10n.creature.tier.elite.name\", " +
            "\"stat_multiplier\": 2, \"control_immune\": true, \"sort_weight\": 10}" +
            "]";

        public const string LevelCurveRows = "[" +
            "{\"id\": \"prog.sample_curve\", \"max_level\": 3, \"entries\": [" +
            "{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}," +
            "{\"level\": 2, \"xp_to_next\": 200, \"growth\": {\"stat.power\": 5}}," +
            "{\"level\": 3, \"xp_to_next\": 0, \"growth\": {\"stat.power\": 5, \"stat.max_health\": 20}}" +
            "]}" +
            "]";

        public const string TemplateRows = "[" +
            "{\"id\": \"creature.sample_basic\", \"name_key\": \"l10n.creature.sample_basic.name\", " +
            "\"level\": 1, \"tier\": \"creature.tier.normal\", " +
            "\"base_stats\": {\"stat.power\": 10, \"stat.max_health\": 100}, " +
            "\"faction_id\": \"fac.test_monster\", \"display_ref\": \"display.sample_basic\"}," +
            "{\"id\": \"creature.sample_elite\", \"name_key\": \"l10n.creature.sample_elite.name\", " +
            "\"level\": 3, \"tier\": \"creature.tier.elite\", " +
            "\"base_stats\": {\"stat.power\": 10, \"stat.max_health\": 100}, " +
            "\"stat_growth_ref\": \"prog.sample_curve\", " +
            "\"faction_id\": \"fac.test_monster\", " +
            "\"npc_flags\": [\"npc_flag.vendor\", \"npc_flag.questgiver\"], " +
            "\"ai_rotation_ref\": \"ai.rotation.sample\", \"ai_behavior_ref\": \"ai.behavior.sample\", " +
            "\"loot_table_ref\": \"loot.sample_table\", \"display_ref\": \"display.sample_elite\", " +
            "\"immunities\": [\"school.sample_fire\"]}" +
            "]";

        public static IEventBus CreateBus() =>
            new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        /// <summary>装配一个已加载 stat.definition/creature.tier_definition/creature.template/
        /// prog.level_curve 四张表的 <see cref="DataRegistry"/>。</summary>
        public static DataRegistry MakeRegistry(IEventBus bus)
        {
            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add(Core.Carriers.Creature.CreatureSchemas.TierDefinition.Name,
                    Envelope(Core.Carriers.Creature.CreatureSchemas.TierDefinition.Name, TierDefinitionRows))
                .Add(Core.Carriers.Creature.CreatureSchemas.Template.Name,
                    Envelope(Core.Carriers.Creature.CreatureSchemas.Template.Name, TemplateRows))
                .Add("prog.level_curve", Envelope("prog.level_curve", LevelCurveRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(Core.Carriers.Creature.CreatureSchemas.TierDefinition);
            registry.RegisterSchema(Core.Carriers.Creature.CreatureSchemas.Template);
            registry.RegisterSchema(ProgSchemas.LevelCurve);
            registry.RegisterValidationRule(new ProgLevelCurveValidationRule());
            registry.RegisterValidationRule(new Core.Carriers.Creature.CreatureContentValidationRule());

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking);
            return registry;
        }

        public static StatHost MakeStatHost(DataRegistry registry, IEventBus bus) => new StatHost(registry, bus);

        public static PowerHost MakePowerHost(IEventBus bus, IStatHost stats)
        {
            var json = "{"
                + "\"id\": \"" + WellKnownPowers.Health.Value + "\","
                + "\"name_key\": \"l10n.power.health.name\","
                + "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"},"
                + "\"regen_in_combat\": 0,"
                + "\"regen_out_of_combat\": 0,"
                + "\"decay_out_of_combat\": 0,"
                + "\"refill_on_leave_combat\": false,"
                + "\"start_full\": true,"
                + "\"allow_overflow\": false,"
                + "\"min\": 0"
                + "}";

            var obj = (JsonObject)JsonReader.Parse(json);
            var record = new DataRecord(PowerSchemas.PowerType, WellKnownPowers.Health.Value, WellKnownPowers.Health, obj);
            var definition = new PowerTypeDefinition(record);

            StatLookup lookup = stats.GetStat;
            return new PowerHost(new[] { definition }, bus, lookup);
        }

        public static ProgressionHost MakeProgressionHost(DataRegistry registry, IEventBus bus, IStatHost stats)
        {
            StatModifierWriter writer = (unitId, stat, op, value, sourceId) =>
            {
                var modOp = op == "pct" ? StatModifierOp.Pct : (op == "mult" ? StatModifierOp.Mult : StatModifierOp.Flat);
                stats.AddModifier(unitId, new StatModifier(stat, modOp, value, sourceId));
            };
            StatModifierRemover remover = (unitId, sourceId) => stats.RemoveModifiersBySource(unitId, sourceId);

            return new ProgressionHost(registry, bus, writer, remover);
        }

        /// <summary>供"未知 tier"防御性异常测试使用的最小 <see cref="IDataRegistryView"/> 假实现：
        /// 绕过 <see cref="DataRegistry"/> 的引用完整性校验，直接构造一条 <c>tier</c> 指向不存在
        /// 记录的 <c>creature.template</c>，验证 <see cref="Core.Carriers.Creature.CreatureFactory"/>
        /// 自身在装配期对未知 tier 的防御性检查（正常数据管线里，这一分支已经在
        /// <c>reference_integrity</c> 校验阶段被拦截，见 CreatureSchemas 判断记录）。</summary>
        internal sealed class UnknownTierRegistryView : IDataRegistryView
        {
            private readonly List<DataRecord> _templates = new List<DataRecord>();

            public UnknownTierRegistryView()
            {
                var json = "{\"id\": \"creature.sample_orphan\", \"name_key\": \"l10n.creature.sample_orphan.name\", " +
                    "\"level\": 1, \"tier\": \"creature.tier.nonexistent\", " +
                    "\"base_stats\": {\"stat.power\": 1}, \"faction_id\": \"fac.test_monster\", " +
                    "\"display_ref\": \"display.sample_orphan\"}";
                var obj = (JsonObject)JsonReader.Parse(json);
                var id = new Id("creature.sample_orphan");
                _templates.Add(new DataRecord(
                    Core.Carriers.Creature.CreatureSchemas.Template, id.Value, id, obj));
            }

            public DataRecord? Get(string table, string key) => null;

            public DataRecord? Get(string table, Id id) => null;

            public IReadOnlyList<DataRecord> GetAll(string table)
            {
                if (table == Core.Carriers.Creature.CreatureSchemas.Template.Name)
                {
                    return _templates;
                }
                return Array.Empty<DataRecord>();
            }

            public IReadOnlyList<DataRecord> Query(string table, Core.Foundation.Expr.ExprNode predicate) =>
                Array.Empty<DataRecord>();

            public IReadOnlyList<DataRecord> Query(string table, string predicateText) =>
                Array.Empty<DataRecord>();

            public IReadOnlyList<string> Tables => Array.Empty<string>();

            public TableSchema? GetSchema(string table) => null;
        }
    }
}
