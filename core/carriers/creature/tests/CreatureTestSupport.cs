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
            "]}," +
            // 消费方反馈第 36 条根治验收专用曲线：口径同上（entries[level-1].growth 生效），
            // 供 creature.sample_beast（出生等级 2，见 TemplateRows）复现/验收"出生等级 > 1 再升级
            // 不重复计入成长"。
            "{\"id\": \"prog.sample_curve_e36\", \"max_level\": 3, \"entries\": [" +
            "{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}," +
            "{\"level\": 2, \"xp_to_next\": 200, \"growth\": {\"stat.power\": 2}}," +
            "{\"level\": 3, \"xp_to_next\": 0, \"growth\": {\"stat.power\": 2}}" +
            "]}," +
            // 消费方反馈第 36 条根治验收专用曲线（五级）：供"出生等级 5 直接生成" vs "出生 1 级
            // 逐级真实升到 5 级"两条路径的等价性验证（creature.sample_hydra/sample_hydra_cub，见
            // TemplateRows）——每级成长量刻意各不相同，避免"总和碰巧相等"掩盖顺序错误。
            "{\"id\": \"prog.sample_curve_e36_l5\", \"max_level\": 5, \"entries\": [" +
            "{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}," +
            "{\"level\": 2, \"xp_to_next\": 100, \"growth\": {\"stat.power\": 1}}," +
            "{\"level\": 3, \"xp_to_next\": 100, \"growth\": {\"stat.power\": 2}}," +
            "{\"level\": 4, \"xp_to_next\": 100, \"growth\": {\"stat.power\": 3}}," +
            "{\"level\": 5, \"xp_to_next\": 0, \"growth\": {\"stat.power\": 4}}" +
            "]}" +
            "]";

        /// <summary>消费方反馈第 33 条：<c>arch.power_type</c> 样例行——health（沿用此前
        /// <see cref="MakePowerHost"/> 手写的同一份 max_source/start_full 取值，保证既有断言
        /// 不变）+ mana（新增，验证"回落到数据集全部 arch.power_type 定义"这条新默认路径确实
        /// 让未在 <see cref="CreatureOptions.DefaultPowerTypes"/> 里显式列出的资源类型也能被
        /// 注册/查询）。</summary>
        public const string PowerTypeRows = "[" +
            "{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, " +
            "\"regen_in_combat\": 0, \"regen_out_of_combat\": 0, \"decay_out_of_combat\": 0, " +
            "\"refill_on_leave_combat\": false, \"start_full\": true, \"allow_overflow\": false, \"min\": 0}," +
            "{\"id\": \"arch.power.mana\", \"name_key\": \"l10n.power.mana.name\", " +
            "\"max_source\": {\"kind\": \"fixed\", \"value\": 50}, " +
            "\"regen_in_combat\": 0, \"regen_out_of_combat\": 0, \"decay_out_of_combat\": 0, " +
            "\"refill_on_leave_combat\": false, \"start_full\": true, \"allow_overflow\": false, \"min\": 0}" +
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
            "\"immunities\": [\"school.sample_fire\"]}," +
            // 消费方反馈第 36 条根治验收专用：出生等级 2（> 1）+ stat_growth_ref，tier.normal
            // 倍率 1——探针原文的"出生等级 2、出生 strength 7、升到 3 级应为 9"用本仓库现成的
            // stat.power 复现同一形状（base=5，曲线每级 +2，见 prog.sample_curve_e36）：出生
            // power = 5*1 + Σ[2..2](2) = 7；升到 3 级正确应为 5*1 + Σ[2..3](2+2=4) = 9，根治前会
            // 重复计入成为 7 + 4 = 11。
            "{\"id\": \"creature.sample_beast\", \"name_key\": \"l10n.creature.sample_beast.name\", " +
            "\"level\": 2, \"tier\": \"creature.tier.normal\", " +
            "\"base_stats\": {\"stat.power\": 5}, " +
            "\"stat_growth_ref\": \"prog.sample_curve_e36\", " +
            "\"faction_id\": \"fac.test_monster\", \"display_ref\": \"display.sample_beast\"}," +
            // 消费方反馈第 36 条根治验收专用：路径等价性一对（同一条五级曲线 prog.sample_curve_e36_l5，
            // 同样的 base_stats/tier）——sample_hydra 出生即 5 级，sample_hydra_cub 出生 1 级（供测试
            // 用例真实经 AddXp 逐级升到 5 级后与前者对比最终值）。
            "{\"id\": \"creature.sample_hydra\", \"name_key\": \"l10n.creature.sample_hydra.name\", " +
            "\"level\": 5, \"tier\": \"creature.tier.normal\", " +
            "\"base_stats\": {\"stat.power\": 3}, " +
            "\"stat_growth_ref\": \"prog.sample_curve_e36_l5\", " +
            "\"faction_id\": \"fac.test_monster\", \"display_ref\": \"display.sample_hydra\"}," +
            "{\"id\": \"creature.sample_hydra_cub\", \"name_key\": \"l10n.creature.sample_hydra_cub.name\", " +
            "\"level\": 1, \"tier\": \"creature.tier.normal\", " +
            "\"base_stats\": {\"stat.power\": 3}, " +
            "\"stat_growth_ref\": \"prog.sample_curve_e36_l5\", " +
            "\"faction_id\": \"fac.test_monster\", \"display_ref\": \"display.sample_hydra_cub\"}" +
            "]";

        public static IEventBus CreateBus() =>
            new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        /// <summary>装配一个已加载 stat.definition/creature.tier_definition/creature.template/
        /// prog.level_curve/arch.power_type（消费方反馈第 33 条新增，见 <see cref="PowerTypeRows"/>
        /// 判断记录）五张表的 <see cref="DataRegistry"/>。</summary>
        public static DataRegistry MakeRegistry(IEventBus bus)
        {
            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add(Core.Carriers.Creature.CreatureSchemas.TierDefinition.Name,
                    Envelope(Core.Carriers.Creature.CreatureSchemas.TierDefinition.Name, TierDefinitionRows))
                .Add(Core.Carriers.Creature.CreatureSchemas.Template.Name,
                    Envelope(Core.Carriers.Creature.CreatureSchemas.Template.Name, TemplateRows))
                .Add("prog.level_curve", Envelope("prog.level_curve", LevelCurveRows))
                .Add(PowerSchemas.PowerType.Name, Envelope(PowerSchemas.PowerType.Name, PowerTypeRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(Core.Carriers.Creature.CreatureSchemas.TierDefinition);
            registry.RegisterSchema(Core.Carriers.Creature.CreatureSchemas.Template);
            registry.RegisterSchema(ProgSchemas.LevelCurve);
            registry.RegisterSchema(PowerSchemas.PowerType);
            registry.RegisterValidationRule(new ProgLevelCurveValidationRule());
            registry.RegisterValidationRule(new Core.Carriers.Creature.CreatureContentValidationRule());

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking);
            return registry;
        }

        public static StatHost MakeStatHost(DataRegistry registry, IEventBus bus) => new StatHost(registry, bus);

        /// <summary>消费方反馈第 33 条：从 <paramref name="registry"/> 的 <c>arch.power_type</c>
        /// 全部已登记行装配 <see cref="PowerHost"/>（惯例改同 <c>Core.Rules.Assembly.RulesAssembly</c>
        /// 装配 <c>Powers</c> 字段的既有真实生产路径——遍历 <c>Registry.GetAll("arch.power_type")</c>
        /// 逐行构造 <see cref="PowerTypeDefinition"/>——此前本方法手写单一 health 定义、与
        /// <paramref name="registry"/> 完全脱节，<see cref="CreatureFactory"/> 新增的"回落到数据集
        /// 全部 arch.power_type 定义"路径据此才有意义验证：<see cref="PowerHost"/> 与
        /// <see cref="CreatureFactory"/> 的资源类型全集现在共享同一个 <paramref name="registry"/>
        /// 来源，不会出现"工厂想注册一个 PowerHost 根本不认识的资源类型"从而抛异常的情形）。</summary>
        public static PowerHost MakePowerHost(DataRegistry registry, IEventBus bus, IStatHost stats)
        {
            var powerTypes = new List<PowerTypeDefinition>();
            foreach (var record in registry.GetAll(PowerSchemas.PowerType.Name))
            {
                powerTypes.Add(new PowerTypeDefinition(record));
            }

            StatLookup lookup = stats.GetStat;
            return new PowerHost(powerTypes, bus, lookup);
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
