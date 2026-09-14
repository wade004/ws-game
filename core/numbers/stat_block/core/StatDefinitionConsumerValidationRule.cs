using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Numbers.StatBlock
{
    /// <summary>
    /// 分阶段落地计划 T-N1-9（ADR-0030 决策 8/9；04_数据与内容管线.md 第 5 节数值类校验项分级表
    /// "警告 | 属性无消费者 | <c>stat.definition</c> 条目未被命中表配置、结算管线、资源池定义、派生规则
    /// 或任一表达式引用"一行）：<c>stat.definition</c> 每条记录若没有任何"消费者"，报告一条 Warning，
    /// 帮内容作者抓"登记了属性但忘了接上任何系统"这类遗漏（04 第 5 节该行属于 ADR-0030～0034 新增的
    /// 数值类警告组，整组登记为<b>不可提升</b>——见该节表格上方引导段"警告级这一组登记为不可提升"，
    /// 因此本规则 <see cref="NonEscalatable"/> 恒 <c>true</c>：即便调用方把
    /// <see cref="DataRegistryStrictness.WarningsBlock"/> 整体设为"警告也阻断"，本规则产出的 Warning
    /// 仍不计入阻断——04 原文"它们抓的是意图不是手滑"）。
    /// <para>
    /// <b>"消费者"定义（判断记录，04 原文只给四个例子，不是穷举清单）</b>：04 该行原文"未被命中表配置、
    /// 结算管线、资源池定义、派生规则或任一表达式引用"是四个典型例子而非封闭枚举（同 04 第 5 节其它
    /// 检查项一贯的"如……"措辞风格，见"引用完整性"一行"每个通过 declareReference 声明的外键字段"这类
    /// 泛化表述）。任务派发提示词给出的操作化定义更宽：属性 id 被"其它属性的 <c>derived_from</c>、
    /// <c>stat.weight</c>、<c>arch.class.base_stats</c>/<c>derivation_overrides</c>、
    /// <c>combat.hit_table_config</c> 各分支的 <c>stat</c>/<c>hit_stat</c>、
    /// <c>CombatOptions</c> 默认属性名、<c>item.template</c>/<c>item.affix</c> 的属性修饰、
    /// <c>skill.aura_def</c> 效果的 <c>stat</c> 参数、<c>arch.power_type</c> 的上限/回复引用、
    /// <c>creature.template.base_stats</c> 键等"任一命中即视为"有消费者"。本规则据此实现为
    /// "扫描范围不硬编码表名"：不针对上面枚举的具体表各写一条判断，而是通用地扫描<b>全部已注册表</b>
    /// 的<b>全部字段</b>（含 <see cref="FieldSchema.Fields"/>/<see cref="FieldSchema.Item"/>/
    /// <see cref="FieldSchema.Variants"/>/<see cref="FieldSchema.Map"/> 任意深度嵌套），只要某字段
    /// 登记了 <see cref="FieldSchema.ReferenceTable"/>/<see cref="FieldSchema.SoftReferenceTable"/>
    /// 等于 <c>"stat.definition"</c>，或某 <see cref="MapSchema.KeyReferenceTable"/> 等于
    /// <c>"stat.definition"</c>（如 <c>arch.class.base_stats</c> 的键），就把该字段在已加载记录里
    /// 实际出现的取值全部计入"已消费属性 id"集合——新增一张引用 <c>stat.definition</c> 的表不需要改
    /// 本规则一行代码，天然覆盖任务派发提示词枚举的绝大多数场景（<c>derived_from</c>/
    /// <c>stat.weight.stat</c>/<c>base_stats</c>/<c>derivation_overrides</c>/
    /// <c>hit_table_config.*.stat</c>/<c>hit_table_config.miss.hit_stat</c>/
    /// <c>arch.power_type.max_source.stat</c> 均属"某字段登记为 Reference(stat.definition) 或
    /// Map 键引用 stat.definition"这一形状，一次遍历全覆盖）。另加两条补充来源：
    /// </para>
    /// <para>
    /// (1) <see cref="IDataRegistryView.GetReferenceDeclarations"/>（1.26.0，消费方反馈第 37 条）：
    /// 动态经 <c>DeclareReference</c> 登记、但未在 <see cref="FieldSchema"/> 里声明为
    /// <see cref="FieldKind.Reference"/> 的引用（当前仓库尚无表以这种方式指向
    /// <c>stat.definition</c>，但扫描逻辑不假设"没有"，为后续新增该类声明预留）。
    /// </para>
    /// <para>
    /// (2) <b>框架内置消费者属性名清单</b>（<see cref="FrameworkBuiltinConsumerStatIds"/>，任务派发
    /// 提示词"CombatOptions 默认属性名……这类运行时选项引用不在数据里，规则拿不到，怎么处理要写判断
    /// 记录：例如规则只扫描数据层引用 + 允许通过登记'框架内置消费者属性名清单'豁免"）：
    /// <see cref="Core.Rules.Combat.CombatOptions"/> 构造期默认值直接硬编码了几个属性 id
    /// （<c>ArmorStat</c>=<c>stat.armor</c>、<c>DamageDonePctStat</c>=<c>stat.damage_done_pct</c>、
    /// <c>DamageTakenPctStat</c>=<c>stat.damage_taken_pct</c>、<c>HealingDonePctStat</c>=
    /// <c>stat.healing_done_pct</c>）——这些属性即便内容数据里没有任何表引用它们，只要游戏用默认
    /// <c>CombatOptions</c> 构造 <c>Resolver</c>，运行时就会读取它们，语义上"有消费者"，只是消费者
    /// 是 C# 代码里的默认值而不是数据表的一条记录，本规则的数据层扫描天然拿不到。<c>本模块（L1
    /// stat_block）不能引用 L2 combat 的具体类型</c>（分层边界，见 01_分层与依赖.md），因此这份清单
    /// 是手抄的字符串常量，不是反射读取 <c>CombatOptions</c> 的默认值——两边各自维护，若
    /// <c>CombatOptions</c> 未来新增/改名默认属性 id，本清单需要人工同步更新，不会自动感知，已如实
    /// 记录在此。<c>ResistStatPrefix</c>（<c>"stat.resist_"</c> 前缀 + 学派名，非固定 id）与
    /// <c>PhysicalSchool</c>/<c>RngStream</c> 等不是属性 id，不登记。
    /// </para>
    /// </summary>
    public sealed class StatDefinitionConsumerValidationRule : IValidationRule
    {
        private const string StatDefinitionTable = "stat.definition";

        private const int MaxDepth = 32;

        /// <summary>检查名——04 第 5 节分级表"属性无消费者"一行未给出检查名（该表只逐行给出"级别 |
        /// 检查项 | 说明"三列，检查项是中文短语不是代码标识符，同表其它多数行也是如此），按
        /// <c>stat_definition_*</c> 前缀命名（同本模块既有
        /// <see cref="StatDefinitionValidationRule.CheckDerivedFromRequiresDerivedCategory"/> 等三条
        /// 判断记录一致的处理口径），<b>待设计层确认</b>。</summary>
        public const string CheckNoConsumer = "stat_definition_no_consumer";

        /// <summary>见类型判断记录"框架内置消费者属性名清单"一节。</summary>
        public static readonly IReadOnlyList<string> FrameworkBuiltinConsumerStatIds = new[]
        {
            "stat.armor",              // Core.Rules.Combat.CombatOptions.ArmorStat
            "stat.damage_done_pct",    // Core.Rules.Combat.CombatOptions.DamageDonePctStat
            "stat.damage_taken_pct",   // Core.Rules.Combat.CombatOptions.DamageTakenPctStat
            "stat.healing_done_pct",   // Core.Rules.Combat.CombatOptions.HealingDonePctStat
        };

        public string RuleId => nameof(StatDefinitionConsumerValidationRule);

        public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

        /// <summary>04 第 5 节"警告级这一组登记为不可提升"——见类型顶部判断记录，恒 true。</summary>
        public bool NonEscalatable => true;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (view.GetSchema(StatDefinitionTable) == null)
            {
                yield break;
            }

            var definitions = view.GetAll(StatDefinitionTable);
            if (definitions.Count == 0)
            {
                yield break;
            }

            var consumed = new HashSet<string>(FrameworkBuiltinConsumerStatIds, StringComparer.Ordinal);

            // (1) 动态 DeclareReference 声明（只支持记录顶层标量字段，见 ReferenceDeclaration 类型判断记录）。
            var declarations = view.GetReferenceDeclarations();
            for (var i = 0; i < declarations.Count; i++)
            {
                var decl = declarations[i];
                if (!string.Equals(decl.ToTable, StatDefinitionTable, StringComparison.Ordinal))
                {
                    continue;
                }

                var fromRecords = view.GetAll(decl.FromTable);
                for (var r = 0; r < fromRecords.Count; r++)
                {
                    if (fromRecords[r].TryGetString(decl.FieldPath, out var value) && !string.IsNullOrEmpty(value))
                    {
                        consumed.Add(value);
                    }
                }
            }

            // (2) 全部已注册表的 schema 级 Reference/SoftReference/Map 键引用，任意深度递归。
            var tables = view.Tables;
            for (var t = 0; t < tables.Count; t++)
            {
                var schema = view.GetSchema(tables[t]);
                if (schema == null)
                {
                    continue;
                }

                var records = view.GetAll(tables[t]);
                for (var r = 0; r < records.Count; r++)
                {
                    CollectFromFields(schema.Fields, records[r].Raw, consumed, 0);
                }
            }

            for (var i = 0; i < definitions.Count; i++)
            {
                var id = definitions[i].Key;
                if (!consumed.Contains(id))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Warning, StatDefinitionTable, CheckNoConsumer,
                        $"属性 \"{id}\" 未被任何已注册表的引用字段、派生规则或框架内置消费者清单引用" +
                            "（04 第 5 节\"属性无消费者\"）", recordKey: id, field: "id");
                }
            }
        }

        private static void CollectFromFields(
            IReadOnlyList<FieldSchema>? fields, JsonObject obj, HashSet<string> consumed, int depth)
        {
            if (fields == null || depth > MaxDepth)
            {
                return;
            }

            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (!obj.TryGetValue(field.Name, out var value) || value.Kind == JsonKind.Null)
                {
                    continue;
                }

                CollectFromValue(field, value, consumed, depth);
            }
        }

        private static void CollectFromValue(FieldSchema field, JsonValue value, HashSet<string> consumed, int depth)
        {
            if (depth > MaxDepth)
            {
                return;
            }

            var isStatReferenceField =
                string.Equals(field.ReferenceTable, StatDefinitionTable, StringComparison.Ordinal) ||
                string.Equals(field.SoftReferenceTable, StatDefinitionTable, StringComparison.Ordinal);
            if (isStatReferenceField)
            {
                if (value is JsonString str)
                {
                    consumed.Add(str.Value);
                }
                else if (value is JsonArray idListArray)
                {
                    for (var i = 0; i < idListArray.Count; i++)
                    {
                        if (idListArray[i] is JsonString elementStr)
                        {
                            consumed.Add(elementStr.Value);
                        }
                    }
                }
            }

            if (field.Kind == FieldKind.Object && value is JsonObject obj)
            {
                var map = field.Map;
                if (map != null)
                {
                    var keyIsStatReference = string.Equals(map.KeyReferenceTable, StatDefinitionTable, StringComparison.Ordinal);
                    foreach (var kv in obj)
                    {
                        if (keyIsStatReference)
                        {
                            consumed.Add(kv.Key);
                        }

                        if (kv.Value.Kind != JsonKind.Null)
                        {
                            CollectFromValue(map.ValueSchema, kv.Value, consumed, depth + 1);
                        }
                    }

                    return;
                }

                var variants = field.Variants;
                if (variants != null)
                {
                    CollectFromFields(variants.CommonFields, obj, consumed, depth + 1);
                    if (obj.TryGetValue(variants.Discriminator, out var discriminatorValue) &&
                        discriminatorValue is JsonString discriminatorStr &&
                        variants.Cases.TryGetValue(discriminatorStr.Value, out var caseFields))
                    {
                        CollectFromFields(caseFields, obj, consumed, depth + 1);
                    }

                    return;
                }

                CollectFromFields(field.Fields, obj, consumed, depth + 1);
            }
            else if (field.Kind == FieldKind.Array && value is JsonArray array)
            {
                var item = field.Item;
                if (item == null)
                {
                    return;
                }

                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i].Kind == JsonKind.Null)
                    {
                        continue;
                    }

                    CollectFromValue(item, array[i], consumed, depth + 1);
                }
            }
        }
    }
}
