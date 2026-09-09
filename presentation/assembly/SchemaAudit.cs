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
        /// <c>unschematized_table</c>。</summary>
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
                }

                var fields = schema.Fields;
                for (var f = 0; f < fields.Count; f++)
                {
                    var ancestors = new List<FieldSchema>();
                    WalkField(schema.Name, fields[f], fields[f].Name, depth: 0, issues, allTableNames, allowlist, usedAllowlistKeys, ref fieldCount, ancestors);
                }
            }

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
            string tableName, FieldSchema field, string path, int depth,
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
                        WalkVariant(tableName, variants, path, depth, issues, allTableNames, allowlist, usedAllowlistKeys, ref fieldCount, ancestors);
                    }
                    else
                    {
                        for (var i = 0; i < subFields!.Count; i++)
                        {
                            var sub = subFields[i];
                            WalkField(tableName, sub, path + "." + sub.Name, depth + 1, issues, allTableNames, allowlist, usedAllowlistKeys, ref fieldCount, ancestors);
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

                    WalkField(tableName, item, path + "[]", depth + 1, issues, allTableNames, allowlist, usedAllowlistKeys, ref fieldCount, ancestors);
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
            string tableName, VariantSchema variants, string path, int depth,
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
                        WalkField(tableName, cf, casePath + "." + cf.Name, depth + 1, issues, allTableNames, allowlist, usedAllowlistKeys, ref fieldCount, ancestors);
                    }
                }
            }

            if (commonFields != null)
            {
                for (var i = 0; i < commonFields.Count; i++)
                {
                    var cf = commonFields[i];
                    WalkField(tableName, cf, path + "." + cf.Name, depth + 1, issues, allTableNames, allowlist, usedAllowlistKeys, ref fieldCount, ancestors);
                }
            }
        }
    }
}
