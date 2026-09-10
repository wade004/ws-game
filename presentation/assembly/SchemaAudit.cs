using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Presentation.Assembly
{
    /// <summary>
    /// F3 元数据门禁（ADR-0018 决策 3、ADR-0019 决策 4）：对全部已登记 <see cref="TableSchema"/>
    /// 做静态审计——不加载任何数据，只检查"结构声明本身"是否完整、自洽，供 <c>toolchain/validator
    /// --schema-audit</c> 与编辑器基础套件共用同一份审计逻辑（同 <see cref="ContentValidationAssembly"/>
    /// 的判断记录："编辑器里看到的红线 = 门禁会报的错"）。
    /// <para>
    /// 判断记录（P3-02 澄清，ADR-0019"修订记录"一节同步）：本类型与"变体表键集合与运行时注册的
    /// 原语集合一致"（ADR-0019 决策 3）不是同一件事，不要混同——本类型（<see cref="WalkVariant"/>
    /// 为入口之一）只查空键、<c>common</c>/<c>discriminator</c> 冲突、递归结构等"登记本身是否
    /// 自洽"，不加载任何数据、不比较任何运行时注册表；真正比较"登记的变体键集合"与"运行时实际注册的
    /// 原语/kind 集合"是否一致的，是各模块自己的 coverage tests（如 <c>SkillSchemaCoverageTests</c>/
    /// <c>GobjSchemaCoverageTests</c>），走 <c>dotnet test</c>，不属于本类型或 <c>--schema-audit</c>
    /// 这条命令行门禁。
    /// </para>
    /// </summary>
    public sealed class SchemaAuditIssue
    {
        /// <summary><c>"error"</c> 或 <c>"warning"</c>。</summary>
        public string Severity { get; }

        public string Table { get; }

        /// <summary>字段路径，顶层字段即字段名本身；递归进入子结构后按校验器同款记法拼接——
        /// <c>Object.Fields</c>/<c>Variants.CommonFields</c> 追加 <c>".子字段名"</c>，
        /// <c>Array.Item</c> 追加 <c>"[]"</c>，<c>Variants.Cases[case]</c> 追加
        /// <c>"{判别字段=case}"</c> 再追加 <c>".子字段名"</c>，如 <c>effects[].params.base_value</c>、
        /// <c>shape{kind=circle}.radius</c>。</summary>
        public string FieldPath { get; }

        /// <summary>检查项名：<c>missing_description</c>/<c>composite_without_substructure</c>/
        /// <c>allowlist_entry_unused</c>/<c>reference_target_unknown</c>/<c>variant_shape</c>/
        /// <c>unschematized_table</c>/<c>field_range_kind</c>（ADR-0021）。</summary>
        public string Check { get; }

        public string Message { get; }

        public SchemaAuditIssue(string severity, string table, string fieldPath, string check, string message)
        {
            Severity = severity ?? throw new ArgumentNullException(nameof(severity));
            Table = table ?? throw new ArgumentNullException(nameof(table));
            FieldPath = fieldPath ?? throw new ArgumentNullException(nameof(fieldPath));
            Check = check ?? throw new ArgumentNullException(nameof(check));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }
    }

    /// <summary>一次 <see cref="SchemaAudit.Run"/> 的完整结果。</summary>
    public sealed class SchemaAuditReport
    {
        public IReadOnlyList<SchemaAuditIssue> Issues { get; }

        public int ErrorCount { get; }

        public int WarningCount { get; }

        public int TableCount { get; }

        /// <summary>本次审计遍历到的全部字段节点数（含递归进入的子结构字段，不止顶层字段）。</summary>
        public int FieldCount { get; }

        /// <summary>与 <see cref="ValidationReport.IsBlocking"/> 同一惯例：只要有 Error 就阻断，
        /// Warning 不阻断（本审计不区分严格级别，白名单本身就是"确认放行"的机制）。</summary>
        public bool IsBlocking => ErrorCount > 0;

        internal SchemaAuditReport(IReadOnlyList<SchemaAuditIssue> issues, int tableCount, int fieldCount)
        {
            Issues = issues;
            TableCount = tableCount;
            FieldCount = fieldCount;

            var errors = 0;
            var warnings = 0;
            for (var i = 0; i < issues.Count; i++)
            {
                if (issues[i].Severity == "error") errors++;
                else warnings++;
            }
            ErrorCount = errors;
            WarningCount = warnings;
        }
    }

    /// <summary>一条白名单条目：<c>table</c>/<c>field</c> 定位一个 <c>composite_without_substructure</c>
    /// 命中（<c>field</c> 用与 <see cref="SchemaAuditIssue.FieldPath"/> 相同的记法），<c>reason</c>
    /// 必填——本审计只允许白名单豁免 <c>composite_without_substructure</c>，不允许豁免
    /// <c>missing_description</c>（见 <c>toolchain/schema_audit_allowlist.json</c> 头注释）。</summary>
    public sealed class SchemaAuditAllowlistEntry
    {
        public string Table { get; }

        public string Field { get; }

        public string Reason { get; }

        public SchemaAuditAllowlistEntry(string table, string field, string reason)
        {
            if (string.IsNullOrWhiteSpace(table)) throw new ArgumentException("table 不能为空", nameof(table));
            if (string.IsNullOrWhiteSpace(field)) throw new ArgumentException("field 不能为空", nameof(field));
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("reason 不能为空——白名单条目必须写明原因", nameof(reason));
            Table = table;
            Field = field;
            Reason = reason;
        }
    }

    /// <summary>白名单：见 <c>toolchain/schema_audit_allowlist.json</c>。</summary>
    public sealed class SchemaAuditAllowlist
    {
        public IReadOnlyList<SchemaAuditAllowlistEntry> Entries { get; }

        private readonly HashSet<string> _keys;

        public static SchemaAuditAllowlist Empty { get; } = new SchemaAuditAllowlist(Array.Empty<SchemaAuditAllowlistEntry>());

        public SchemaAuditAllowlist(IReadOnlyList<SchemaAuditAllowlistEntry> entries)
        {
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
            _keys = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < entries.Count; i++)
            {
                _keys.Add(Key(entries[i].Table, entries[i].Field));
            }
        }

        public bool Contains(string table, string field) => _keys.Contains(Key(table, field));

        private static string Key(string table, string field) => table + "" + field;

        /// <summary>解析 <c>toolchain/schema_audit_allowlist.json</c> 的文本内容：
        /// <c>{"entries":[{"table":"...","field":"...","reason":"..."}, ...]}</c>。纯解析，不做任何
        /// 文件 I/O（本模块不依赖引擎适配层的 <c>IFileSystem</c>，由调用方——通常是一次性命令行工具
        /// 或编辑器基础套件——自行读取文件文本后传入，见 <c>toolchain/validator</c> 用法）。</summary>
        public static SchemaAuditAllowlist Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            var root = JsonReader.Parse(json);
            if (!(root is JsonObject rootObj) || !rootObj.TryGetValue("entries", out var entriesVal) || !(entriesVal is JsonArray entriesArr))
            {
                throw new FormatException("白名单文件顶层结构必须是 {\"entries\": [...]}");
            }

            var entries = new List<SchemaAuditAllowlistEntry>(entriesArr.Count);
            for (var i = 0; i < entriesArr.Count; i++)
            {
                if (!(entriesArr[i] is JsonObject entryObj))
                {
                    throw new FormatException($"白名单 entries[{i}] 不是对象");
                }

                var table = RequireString(entryObj, "table", i);
                var field = RequireString(entryObj, "field", i);
                var reason = RequireString(entryObj, "reason", i);
                entries.Add(new SchemaAuditAllowlistEntry(table, field, reason));
            }

            return new SchemaAuditAllowlist(entries);
        }

        private static string RequireString(JsonObject obj, string key, int index)
        {
            if (!obj.TryGetValue(key, out var v) || !(v is JsonString s) || s.Value.Length == 0)
            {
                throw new FormatException($"白名单 entries[{index}] 缺少非空字符串字段 \"{key}\"");
            }
            return s.Value;
        }
    }

    /// <summary>审计逻辑本体，见类型（本文件顶部）判断记录。</summary>
    public static class SchemaAudit
    {
        /// <summary>04 第 2.2 节"域名清单（固定，新增域名走审批）"，硬编码在此——
        /// <see cref="FieldKind.Reference"/> 字段的 <see cref="FieldSchema.ReferenceDomain"/>
        /// 取值必须落在这份清单内。来源：<c>architecture/04_数据与内容管线.md</c> 第 2.2 节表格，
        /// 与该文档改动时需同步维护（不做成从文档反向解析——26 个固定域名走审批才会变化，硬编码
        /// 更直接，也避免核心库反过来解析架构文档这种奇怪的依赖方向）。</summary>
        private static readonly HashSet<string> KnownDomains = new HashSet<string>(StringComparer.Ordinal)
        {
            "found", "prog", "stat", "arch", "fac", "skill", "combat", "ai", "target",
            "item", "creature", "gobj", "loot", "quest", "dialog", "encounter", "diff",
            "achv", "econ", "world", "spawn", "area", "display", "vfx", "sfx", "l10n", "input",
        };

        /// <summary>递归深度上限，防御自引用 schema（<see cref="FieldSchema.Item"/>/
        /// <see cref="FieldSchema.Variants"/> 的惰性 factory 允许"元素结构复用自身"，如
        /// <c>effects</c> 数组元素内的 <c>on_hit_effects</c> 复用 <c>effects</c> 自身的元素结构，
        /// 见 <see cref="FieldSchema.Item"/> 类型注释）——本审计走的是静态 schema 图，不像
        /// <c>DataRegistry</c> 递归校验那样天然被"实际 JSON 数据的有限嵌套深度"兜底，必须显式限深，
        /// 数值与 <c>DataRegistry.MaxSubstructureDepth</c> 一致，超过后静默停止递归（自引用是文档
        /// 承认的合法用法，不算错误）。</summary>
        private const int MaxDepth = 32;

        /// <summary>ADR-0022（04 第 2.2 节勘误"命名例外"）：三张单段名表——不符合"域.表"命名约定，
        /// <see cref="TableSchema.Domain"/> 经 <see cref="TableSchema.WithDomain"/> 显式声明，不等于
        /// 表名首段。<see cref="CheckTableOwnership"/> 对这三张表放宽"Domain 必须等于表名首段"检查，
        /// 对其它任何表仍要求两者一致（Domain 走 WithDomain 是登记错误的信号）。</summary>
        private static readonly HashSet<string> DomainNamingExceptions = new HashSet<string>(StringComparer.Ordinal)
        {
            "camera_profile", "ui_layout_definition", "shell_menu_definition",
        };

        /// <summary>ADR-0022（04 第 3.4 节"时间字段单位与作用域"）：04 第 3.1 节"全部与时间相关的
        /// 数据字段"清单里已落地为真实字段的一份静态对照表——(表名, 顶层字段路径) 对，用于
        /// <see cref="CheckKnownTimeFieldsHaveUnit"/> 防遗漏（该清单里的字段名在对应表里必须已登记
        /// <see cref="FieldUnit.Time"/>）。字段路径用 "." 定位一层嵌套（如
        /// <c>charges.recharge_time</c>），与 <see cref="TableSchema.GetField"/>/
        /// <see cref="FieldSchema.Fields"/> 逐段查找一致；不含 04 第 3.1 节例举但落在
        /// <c>Variants</c> 分支内部的 <c>skill.aura_def.effects[].params.interval</c>（惰性 Variants
        /// 结构，见 <c>SkillSchemas.PeriodicParamsCase</c>，字段对象本身已登记 Unit=Time，只是本清单
        /// 不逐一枚举变体分支路径——变体分支路径的存在性/形状已由 <c>SkillSchemaCoverageTests</c> 等
        /// 覆盖测试锁死，不在本审计职责范围）。</summary>
        private static readonly (string Table, string FieldPath)[] KnownTimeFieldPaths =
        {
            ("skill.def", "cast_time"),
            ("skill.def", "channel_time"),
            ("skill.def", "cooldown_duration"),
            ("skill.def", "charges.recharge_time"),
            ("skill.aura_def", "duration"),
            ("skill.proc_def", "internal_cooldown"),
            ("arch.power_type", "regen_in_combat"),
            ("arch.power_type", "regen_out_of_combat"),
            ("arch.power_type", "decay_out_of_combat"),
            ("spawn.table", "respawn_timer"),
        };

        /// <summary>ADR-0022（04 第 3.4 节"表级归属元数据"）：每张已登记表的 Layer/Module/Domain
        /// 非空、Layer 取值合法（枚举本身保证）、Domain 与表名首段一致或属登记例外（检查名
        /// "table_ownership"）。</summary>
        private static void CheckTableOwnership(TableSchema schema, List<SchemaAuditIssue> issues)
        {
            if (schema.Layer == null)
            {
                issues.Add(new SchemaAuditIssue("error", schema.Name, "", "table_ownership",
                    $"表 \"{schema.Name}\" 未登记 Layer（未调用 TableSchema.WithOwnership，ADR-0022 决策 1）"));
            }

            if (string.IsNullOrWhiteSpace(schema.Module))
            {
                issues.Add(new SchemaAuditIssue("error", schema.Name, "", "table_ownership",
                    $"表 \"{schema.Name}\" 未登记 Module（未调用 TableSchema.WithOwnership，ADR-0022 决策 1）"));
            }

            var dot = schema.Name.IndexOf('.');
            var defaultDomain = dot > 0 ? schema.Name.Substring(0, dot) : schema.Name;
            if (schema.Domain != defaultDomain && !DomainNamingExceptions.Contains(schema.Name))
            {
                issues.Add(new SchemaAuditIssue("error", schema.Name, "", "table_ownership",
                    $"表 \"{schema.Name}\" 的 Domain \"{schema.Domain}\" 与表名首段 \"{defaultDomain}\" 不一致，且不在 04 第 2.2 节命名例外清单内"));
            }
            if (schema.Domain == defaultDomain && DomainNamingExceptions.Contains(schema.Name))
            {
                issues.Add(new SchemaAuditIssue("error", schema.Name, "", "table_ownership",
                    $"表 \"{schema.Name}\" 属 04 第 2.2 节命名例外，须经 TableSchema.WithDomain 显式声明 Domain（不能沿用默认的表名首段）"));
            }
        }

        /// <summary>见 <see cref="KnownTimeFieldPaths"/>。</summary>
        private static void CheckKnownTimeFieldsHaveUnit(IReadOnlyList<TableSchema> schemas, List<SchemaAuditIssue> issues)
        {
            var byName = new Dictionary<string, TableSchema>(StringComparer.Ordinal);
            for (var i = 0; i < schemas.Count; i++)
            {
                byName[schemas[i].Name] = schemas[i];
            }

            for (var i = 0; i < KnownTimeFieldPaths.Length; i++)
            {
                var (table, fieldPath) = KnownTimeFieldPaths[i];
                if (!byName.TryGetValue(table, out var schema))
                {
                    continue; // 表未登记进本次审计范围（如调用方只传入部分表），不属于本检查职责。
                }

                // 逐段查找：第 0 段来自表顶层 GetField，其余段来自上一段的 Fields 清单。
                var segments = fieldPath.Split('.');
                FieldSchema? field = schema.GetField(segments[0]);
                for (var s = 1; s < segments.Length; s++)
                {
                    if (field?.Fields == null)
                    {
                        field = null;
                        break;
                    }
                    FieldSchema? next = null;
                    for (var k = 0; k < field.Fields.Count; k++)
                    {
                        if (field.Fields[k].Name == segments[s])
                        {
                            next = field.Fields[k];
                            break;
                        }
                    }
                    field = next;
                }

                if (field == null)
                {
                    issues.Add(new SchemaAuditIssue("error", table, fieldPath, "time_unit_missing",
                        $"04 第 3.1 节时间字段清单里的 \"{table}.{fieldPath}\" 在当前登记里找不到（字段被改名/删除但清单未同步）"));
                }
                else if (field.Unit != FieldUnit.Time)
                {
                    issues.Add(new SchemaAuditIssue("error", table, fieldPath, "time_unit_missing",
                        $"04 第 3.1 节时间字段清单里的 \"{table}.{fieldPath}\" 尚未登记 Unit=Time（ADR-0022 决策 3，防遗漏）"));
                }
            }
        }

        /// <summary>构造一个只登记 schema、不加载任何数据的 <see cref="DataRegistry"/>（复用
        /// <see cref="ContentValidationAssembly.CreateRegistry"/> 同一份装配顺序，见该类型判断
        /// 记录），读出全部已登记 <see cref="TableSchema"/>。</summary>
        public static IReadOnlyList<TableSchema> EnumerateRegisteredSchemas(ContentValidationOptions? options = null)
        {
            var opts = options ?? new ContentValidationOptions();
            var registry = (DataRegistry)ContentValidationAssembly.CreateRegistry(
                new InMemoryDataSource(), opts, out _);
            return registry.RegisteredSchemas;
        }

        public static SchemaAuditReport Run(IReadOnlyList<TableSchema> schemas, SchemaAuditAllowlist allowlist)
        {
            if (schemas == null) throw new ArgumentNullException(nameof(schemas));
            if (allowlist == null) throw new ArgumentNullException(nameof(allowlist));

            var issues = new List<SchemaAuditIssue>();
            var fieldCount = 0;

            var allTableNames = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < schemas.Count; i++)
            {
                allTableNames.Add(schemas[i].Name);
            }

            var usedAllowlistKeys = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < schemas.Count; i++)
            {
                var schema = schemas[i];

                if (schema.IsUnschematized)
                {
                    issues.Add(new SchemaAuditIssue("warning", schema.Name, "", "unschematized_table",
                        $"表 \"{schema.Name}\" 是 IsUnschematized 占位 schema（只做信封/主键格式检查，字段结构未登记，编辑器只能用 JSON 编辑）"));
                    continue;
                }

                CheckTableOwnership(schema, issues);

                var fields = schema.Fields;
                for (var f = 0; f < fields.Count; f++)
                {
                    var ancestors = new List<FieldSchema>();
                    WalkField(schema.Name, schema.TimeScope, fields[f], fields[f].Name, depth: 0, issues, allTableNames, allowlist, usedAllowlistKeys, ref fieldCount, ancestors);
                }
            }

            CheckKnownTimeFieldsHaveUnit(schemas, issues);

            for (var i = 0; i < allowlist.Entries.Count; i++)
            {
                var entry = allowlist.Entries[i];
                var key = entry.Table + "" + entry.Field;
                if (!usedAllowlistKeys.Contains(key))
                {
                    issues.Add(new SchemaAuditIssue("warning", entry.Table, entry.Field, "allowlist_entry_unused",
                        $"白名单条目 table=\"{entry.Table}\" field=\"{entry.Field}\" 在当前登记里找不到对应的 composite_without_substructure 命中（登记补齐后忘删，或表/字段路径写错）"));
                }
            }

            return new SchemaAuditReport(issues, schemas.Count, fieldCount);
        }

        /// <summary>
        /// 判断记录（自引用 schema 的环检测，2026-09-09 修复）：<see cref="FieldSchema.Item"/>/
        /// <see cref="FieldSchema.Variants"/> 允许惰性 factory 让子结构复用自身（如
        /// <c>skill.def.effects[].params.on_hit_effects</c> 复用 <c>effects</c> 数组元素自身的
        /// <see cref="FieldSchema"/> 对象实例，见该类型注释）。本审计走的是静态 schema 图（不是像
        /// <c>DataRegistry</c> 那样校验有限深度的真实 JSON 数据），若只靠 <see cref="MaxDepth"/>
        /// 限深，会在每一层自引用处把同一个 <see cref="FieldSchema"/> 对象当成"新字段"重新展开一遍
        /// （<c>effects[]{kind=projectile}.params.on_hit_effects[]{kind=projectile}.params
        /// .on_hit_effects[]...</c>），32 层深度下产生指数级膨胀的重复路径与重复
        /// <c>missing_description</c> 报告——同一处代码里的同一个 <see cref="FieldSchema"/> 声明被
        /// 报告几十次，且路径长到不可读。改法：<paramref name="ancestors"/> 是"从本次遍历的顶层字段
        /// 到当前字段"这条路径上已经进入过的 <see cref="FieldSchema"/> 对象引用栈（按对象引用比较，
        /// 不是按内容/名字比较——<see cref="FieldSchema"/> 未重写 <see cref="object.Equals(object)"/>，
        /// 默认即引用相等，<see cref="List{T}.Contains"/> 直接可用）。进入 <see cref="WalkField"/>
        /// 时若 <paramref name="field"/> 已经在这条祖先链上出现过，说明沿当前路径绕回了同一个对象——
        /// 这正是自引用点，直接停止（不计入 <see cref="SchemaAuditReport.FieldCount"/>、不重复报告
        /// <c>missing_description</c>：该对象已经在祖先链更早的那一次真正访问里被检查过一次），
        /// 不再继续往下展开。<paramref name="ancestors"/> 只在"从根到当前"这一条路径上有效——
        /// 兄弟分支（如 <c>Variants.Cases</c> 里不同 case 各自引用同一个公共子结构对象）不算祖先，
        /// 不受影响，因为每个分支各自维护自己进入/退出这条路径时对 <paramref name="ancestors"/> 的
        /// 添加/移除（见方法末尾 <c>finally</c>），互不干扰。
        /// </summary>
        private static void WalkField(
            string tableName, TimeScope timeScope, FieldSchema field, string path, int depth,
            List<SchemaAuditIssue> issues, HashSet<string> allTableNames,
            SchemaAuditAllowlist allowlist, HashSet<string> usedAllowlistKeys, ref int fieldCount,
            List<FieldSchema> ancestors)
        {
            if (ancestors.Contains(field))
            {
                // 自引用环：同一个 FieldSchema 对象已经在当前路径的祖先链上出现过，见本方法判断
                // 记录。不重复计数、不重复报告、不再展开——它已经在祖先链更早的那一帧被完整检查过。
                return;
            }

            fieldCount++;

            if (string.IsNullOrWhiteSpace(field.Description))
            {
                issues.Add(new SchemaAuditIssue("error", tableName, path, "missing_description",
                    $"字段 \"{path}\" 缺少描述（FieldSchema.Description 为空/空白），需要中文一句话描述（含缺省值/单位/引用目标提示）"));
            }

            // ADR-0021（04 第 4 节勘误"范围约束"）自洽检查：Range 只对 Number/Int 字段有意义。
            // FieldSchema.WithRange 刻意不在挂载时检查 Kind（见 FieldRange 类型顶部判断记录），
            // 让"登记在错误种类字段上"停留在这里——一条可枚举、可被 --schema-audit 门禁与测试
            // 断言的软失败，而不是启动路径上的一次性异常。min&lt;=max/端点合法这两条已经由
            // FieldRange.Range 工厂在构造时无条件拒绝（同 Enum 必须有 EnumValues 的既有风格），
            // 不可能有实例带着非法取值走到这里，因此本审计不重复检查。
            if (field.Range != null && field.Kind != FieldKind.Number && field.Kind != FieldKind.Int)
            {
                issues.Add(new SchemaAuditIssue("error", tableName, path, "field_range_kind",
                    $"字段 \"{path}\" 登记了 Range，但 Kind 为 {field.Kind}——Range 仅 Number/Int 字段可设"));
            }

            if (field.Kind == FieldKind.Reference)
            {
                if (field.ReferenceTable != null && !allTableNames.Contains(field.ReferenceTable))
                {
                    issues.Add(new SchemaAuditIssue("error", tableName, path, "reference_target_unknown",
                        $"字段 \"{path}\" 的 ReferenceTable \"{field.ReferenceTable}\" 不在已登记表清单内"));
                }
                if (field.ReferenceDomain != null && !KnownDomains.Contains(field.ReferenceDomain))
                {
                    issues.Add(new SchemaAuditIssue("error", tableName, path, "reference_target_unknown",
                        $"字段 \"{path}\" 的 ReferenceDomain \"{field.ReferenceDomain}\" 不在 04 第 2.2 节域名清单内（architecture/04_数据与内容管线.md）"));
                }
            }

            // ADR-0022（04 第 3.4 节"IdList 引用目标"）：IdList 字段必须在 ReferenceTable/
            // ReferenceDomain/FreeIds 三者中恰好登记一种——FieldSchema.WithFreeIds 已在挂载时拒绝
            // "FreeIds 与 Reference* 同时设置"，这里只需要检查"三者都没设"的遗漏情形。
            if (field.Kind == FieldKind.IdList)
            {
                if (field.ReferenceTable == null && field.ReferenceDomain == null && !field.FreeIds)
                {
                    issues.Add(new SchemaAuditIssue("error", tableName, path, "idlist_reference_target",
                        $"字段 \"{path}\" 是 IdList 但未登记 ReferenceTable/ReferenceDomain，也未调用 WithFreeIds() 显式声明为自由 id 列表（ADR-0022 决策 4）"));
                }
                if (field.ReferenceTable != null && !allTableNames.Contains(field.ReferenceTable))
                {
                    issues.Add(new SchemaAuditIssue("error", tableName, path, "reference_target_unknown",
                        $"字段 \"{path}\" 的 ReferenceTable \"{field.ReferenceTable}\" 不在已登记表清单内"));
                }
                if (field.ReferenceDomain != null && !KnownDomains.Contains(field.ReferenceDomain))
                {
                    issues.Add(new SchemaAuditIssue("error", tableName, path, "reference_target_unknown",
                        $"字段 \"{path}\" 的 ReferenceDomain \"{field.ReferenceDomain}\" 不在 04 第 2.2 节域名清单内（architecture/04_数据与内容管线.md）"));
                }
            }

            // ADR-0022（04 第 3.4 节"字段分组元数据"）：Group 是计算属性，理论上不可能落到声明
            // 集合之外的取值——本检查只是给"万一未来 FieldGroup 枚举扩展、DefaultGroup 分支遗漏"
            // 留一道可被 --schema-audit/测试枚举出来的软失败（同 field_range_kind 的既有风格），
            // 不依赖运行期抛异常。
            if (!Enum.IsDefined(typeof(FieldGroup), field.Group))
            {
                issues.Add(new SchemaAuditIssue("error", tableName, path, "field_group",
                    $"字段 \"{path}\" 的 Group 取值 {field.Group} 不是 FieldGroup 已定义的枚举值"));
            }

            // ADR-0022（04 第 3.4 节"时间字段单位与作用域"）：标记为 Unit=Time 的字段，所属表必须
            // 声明非 None 的 TimeScope（见 CheckTableOwnership 里对 timeScope 参数的比较），否则
            // "时间字段与时间模型一致"校验无从判定该字段对照哪个作用域的 found.time_model.mode。
            if (field.Unit == FieldUnit.Time && timeScope == TimeScope.None)
            {
                issues.Add(new SchemaAuditIssue("error", tableName, path, "time_scope_declared",
                    $"字段 \"{path}\" 登记为 Unit=Time，但所属表 \"{tableName}\" 的 TimeScope 仍是 None（未调用 TableSchema.WithTimeScope，ADR-0022 决策 3）"));
            }

            if (depth >= MaxDepth)
            {
                return;
            }

            if (field.Kind != FieldKind.Object && field.Kind != FieldKind.Array)
            {
                return;
            }

            ancestors.Add(field);
            try
            {
                if (field.Kind == FieldKind.Object)
                {
                    var variants = field.Variants;
                    var subFields = field.Fields;

                    if (variants == null && subFields == null)
                    {
                        ReportOrConsumeAllowlist(tableName, path, "Object", issues, allowlist, usedAllowlistKeys);
                        return;
                    }

                    if (variants != null)
                    {
                        WalkVariant(tableName, timeScope, variants, path, depth, issues, allTableNames, allowlist, usedAllowlistKeys, ref fieldCount, ancestors);
                    }
                    else
                    {
                        for (var i = 0; i < subFields!.Count; i++)
                        {
                            var sub = subFields[i];
                            WalkField(tableName, timeScope, sub, path + "." + sub.Name, depth + 1, issues, allTableNames, allowlist, usedAllowlistKeys, ref fieldCount, ancestors);
                        }
                    }
                }
                else
                {
                    var item = field.Item;
                    if (item == null)
                    {
                        ReportOrConsumeAllowlist(tableName, path, "Array", issues, allowlist, usedAllowlistKeys);
                        return;
                    }

                    WalkField(tableName, timeScope, item, path + "[]", depth + 1, issues, allTableNames, allowlist, usedAllowlistKeys, ref fieldCount, ancestors);
                }
            }
            finally
            {
                ancestors.RemoveAt(ancestors.Count - 1);
            }
        }

        private static void ReportOrConsumeAllowlist(
            string tableName, string path, string kindLabel,
            List<SchemaAuditIssue> issues, SchemaAuditAllowlist allowlist, HashSet<string> usedAllowlistKeys)
        {
            if (allowlist.Contains(tableName, path))
            {
                usedAllowlistKeys.Add(tableName + "" + path);
                return;
            }

            issues.Add(new SchemaAuditIssue("error", tableName, path, "composite_without_substructure",
                $"字段 \"{path}\" 是 {kindLabel} 但未登记子结构（{(kindLabel == "Object" ? "Fields/Variants" : "Item")}），若确认暂不支持结构化登记（如 Map 型对象），须加入 toolchain/schema_audit_allowlist.json 并写明 reason"));
        }

        private static void WalkVariant(
            string tableName, TimeScope timeScope, VariantSchema variants, string path, int depth,
            List<SchemaAuditIssue> issues, HashSet<string> allTableNames,
            SchemaAuditAllowlist allowlist, HashSet<string> usedAllowlistKeys, ref int fieldCount,
            List<FieldSchema> ancestors)
        {
            var commonFields = variants.CommonFields;
            var commonNames = new HashSet<string>(StringComparer.Ordinal);
            if (commonFields != null)
            {
                for (var i = 0; i < commonFields.Count; i++)
                {
                    commonNames.Add(commonFields[i].Name);
                }
            }

            foreach (var kv in variants.Cases)
            {
                var caseKey = kv.Key;
                var caseFields = kv.Value;

                if (string.IsNullOrEmpty(caseKey))
                {
                    issues.Add(new SchemaAuditIssue("error", tableName, path, "variant_shape",
                        $"字段 \"{path}\" 的 Variants.Cases 存在空字符串键"));
                }

                if (caseFields != null)
                {
                    for (var i = 0; i < caseFields.Count; i++)
                    {
                        var cf = caseFields[i];
                        if (commonNames.Contains(cf.Name))
                        {
                            issues.Add(new SchemaAuditIssue("error", tableName, path, "variant_shape",
                                $"字段 \"{path}\" 的 case \"{caseKey}\" 声明的子字段 \"{cf.Name}\" 与 Variants.CommonFields 同名"));
                        }
                        if (cf.Name == variants.Discriminator)
                        {
                            issues.Add(new SchemaAuditIssue("error", tableName, path, "variant_shape",
                                $"字段 \"{path}\" 的 case \"{caseKey}\" 声明的子字段 \"{cf.Name}\" 与判别字段 \"{variants.Discriminator}\" 同名"));
                        }
                    }
                }

                var casePath = path + "{" + variants.Discriminator + "=" + caseKey + "}";
                if (caseFields != null)
                {
                    for (var i = 0; i < caseFields.Count; i++)
                    {
                        var cf = caseFields[i];
                        WalkField(tableName, timeScope, cf, casePath + "." + cf.Name, depth + 1, issues, allTableNames, allowlist, usedAllowlistKeys, ref fieldCount, ancestors);
                    }
                }
            }

            if (commonFields != null)
            {
                for (var i = 0; i < commonFields.Count; i++)
                {
                    var cf = commonFields[i];
                    WalkField(tableName, timeScope, cf, path + "." + cf.Name, depth + 1, issues, allTableNames, allowlist, usedAllowlistKeys, ref fieldCount, ancestors);
                }
            }
        }
    }
}
