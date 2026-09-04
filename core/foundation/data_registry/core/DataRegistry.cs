using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using CommonId = Core.Foundation.Common.Id;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// <see cref="IDataRegistry"/> 的默认实现（见 04 第 4 节、01_分层与依赖.md L0 模块表
    /// <c>data_registry</c> 行）。加载流程（<see cref="LoadAll"/>）：读取每张表文本 → JSON
    /// 解析 → 信封检查（<c>table</c>/<c>schema_version</c>/<c>rows</c>，<c>table</c> 必须等于
    /// 文件名）→ 版本迁移（低于当前版本依次跑迁移链，缺环节报错；高于当前版本报错）→ 逐行建
    /// <see cref="DataRecord"/>（主键重复报错）→ 全部表进内存后跑第 5 节校验项 + 已注册
    /// <see cref="IValidationRule"/> → 发 <c>data.load_completed</c>；报告为阻断态时再发
    /// <c>data.validation_failed</c>，且此后 <see cref="Get(string, string)"/>/
    /// <see cref="GetAll"/>/<see cref="Query(string, ExprNode)"/> 一律抛
    /// <see cref="InvalidOperationException"/>（见 11 第 4 节"不做静默降级"）。
    /// 无反射、无 LINQ 热路径、无线程。
    /// </summary>
    public sealed class DataRegistry : IDataRegistry
    {
        private readonly IDataSource _source;
        private readonly IEventBus _bus;
        private readonly DataRegistryOptions _options;

        private readonly Dictionary<string, TableSchema> _schemas = new Dictionary<string, TableSchema>(StringComparer.Ordinal);
        private readonly List<(string FromTable, string Field, string ToTable)> _declaredReferences =
            new List<(string FromTable, string Field, string ToTable)>();
        private readonly List<IValidationRule> _rules = new List<IValidationRule>();

        private Dictionary<string, LoadedTable> _tables = new Dictionary<string, LoadedTable>(StringComparer.Ordinal);

        /// <summary>true 时 <see cref="Get(string, string)"/>/<see cref="GetAll"/>/
        /// <see cref="Query(string, ExprNode)"/> 拒绝读取；初始为 true（尚未 <see cref="LoadAll"/>
        /// 时不允许读取）。</summary>
        private bool _blocked = true;

        public DataRegistry(IDataSource source, IEventBus bus, DataRegistryOptions? options = null)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _options = options ?? new DataRegistryOptions();
        }

        private sealed class LoadedTable
        {
            public TableSchema Schema = null!;
            public List<DataRecord> Records = null!;
            public Dictionary<string, DataRecord> ByKey = null!;
        }

        // ---------------------------------------------------------------
        // 注册
        // ---------------------------------------------------------------

        public void RegisterSchema(TableSchema schema)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            _schemas[schema.Name] = schema;
        }

        public void DeclareReference(string fromTable, string field, string toTable)
        {
            if (string.IsNullOrEmpty(fromTable)) throw new ArgumentException("fromTable 不能为空", nameof(fromTable));
            if (string.IsNullOrEmpty(field)) throw new ArgumentException("field 不能为空", nameof(field));
            if (string.IsNullOrEmpty(toTable)) throw new ArgumentException("toTable 不能为空", nameof(toTable));
            _declaredReferences.Add((fromTable, field, toTable));
        }

        public void RegisterValidationRule(IValidationRule rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            _rules.Add(rule);
        }

        // ---------------------------------------------------------------
        // 加载 / 校验 / 重载
        // ---------------------------------------------------------------

        public ValidationReport LoadAll()
        {
            var issues = new List<ValidationIssue>();
            var loaded = new Dictionary<string, LoadedTable>(StringComparer.Ordinal);
            int recordCount = 0;

            foreach (var tableSource in _source.ListTables())
            {
                LoadOneTable(tableSource, issues, loaded, ref recordCount);
            }

            _tables = loaded;

            var report = RunValidationAndBuildReport(issues);

            _bus.PublishImmediate(new DataLoadCompletedEvent(_tables.Count, recordCount, report.ErrorCount, report.WarningCount));
            if (report.IsBlocking)
            {
                _bus.PublishImmediate(new DataValidationFailedEvent(report.ErrorCount, report.WarningCount));
            }

            return report;
        }

        public ValidationReport Validate() => RunValidationAndBuildReport(new List<ValidationIssue>());

        public ValidationReport Reload(string table)
        {
            if (string.IsNullOrEmpty(table)) throw new ArgumentException("table 不能为空", nameof(table));

            DataTableSource? tableSource = null;
            foreach (var ts in _source.ListTables())
            {
                if (ts.TableName == table) { tableSource = ts; break; }
            }

            var localIssues = new List<ValidationIssue>();
            if (tableSource == null)
            {
                localIssues.Add(new ValidationIssue(ValidationSeverity.Error, table, "envelope", $"数据源中不存在表 \"{table}\""));
            }
            else
            {
                var singleLoaded = new Dictionary<string, LoadedTable>(StringComparer.Ordinal);
                int recordCount = 0;
                LoadOneTable(tableSource, localIssues, singleLoaded, ref recordCount);
                if (singleLoaded.TryGetValue(table, out var loadedTable))
                {
                    _tables[table] = loadedTable;
                }
            }

            return RunValidationAndBuildReport(localIssues);
        }

        private ValidationReport RunValidationAndBuildReport(List<ValidationIssue> issues)
        {
            // 校验期间允许读取（RunFieldValidation 与 IValidationRule 都要经 this 只读查询），
            // 最终是否阻断由本次算出的报告决定。
            _blocked = false;

            RunFieldValidation(issues);

            foreach (var rule in _rules)
            {
                foreach (var issue in rule.Validate(this))
                {
                    issues.Add(issue);
                }
            }

            var report = new ValidationReport(issues, _options.Strictness);
            _blocked = report.IsBlocking;
            return report;
        }

        // ---------------------------------------------------------------
        // 只读查询（IDataRegistryView）
        // ---------------------------------------------------------------

        public DataRecord? Get(string table, string key)
        {
            EnsureReadable();
            return _tables.TryGetValue(table, out var t) && t.ByKey.TryGetValue(key, out var record) ? record : null;
        }

        public DataRecord? Get(string table, CommonId id) => Get(table, id.Value);

        public IReadOnlyList<DataRecord> GetAll(string table)
        {
            EnsureReadable();
            return _tables.TryGetValue(table, out var t) ? t.Records : Array.Empty<DataRecord>();
        }

        public IReadOnlyList<DataRecord> Query(string table, ExprNode predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            var records = GetAll(table);
            var result = new List<DataRecord>();
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                var diagnostics = new ExprDiagnosticsRecorder();
                var host = new RecordExprHost(record, diagnostics);
                var value = ExprEvaluator.Evaluate(predicate, host, diagnostics);
                if (value.Kind == ExprValueKind.Bool && value.AsBool)
                {
                    result.Add(record);
                }
            }
            return result;
        }

        /// <summary>便捷重载：解析 <paramref name="predicateText"/> 用 <see cref="RecordExprSchema.For"/>
        /// 把 <paramref name="table"/> 的字段登记为 <c>self.&lt;field&gt;</c>（见 04 第 4 节）。
        /// 要求该表已经 <see cref="RegisterSchema"/>。</summary>
        public IReadOnlyList<DataRecord> Query(string table, string predicateText)
        {
            if (predicateText == null) throw new ArgumentNullException(nameof(predicateText));

            var schema = GetSchema(table) ?? throw new InvalidOperationException($"表 \"{table}\" 未注册 schema，无法解析谓词文本");
            var exprSchema = RecordExprSchema.For(schema);
            var node = ExprParser.Parse(predicateText, exprSchema);
            return Query(table, node);
        }

        public IReadOnlyList<string> Tables
        {
            get
            {
                var names = new List<string>(_tables.Count);
                foreach (var name in _tables.Keys) names.Add(name);
                return names;
            }
        }

        public TableSchema? GetSchema(string table) => _schemas.TryGetValue(table, out var s) ? s : null;

        private void EnsureReadable()
        {
            if (_blocked)
            {
                throw new InvalidOperationException("数据校验未通过，禁止读取");
            }
        }

        // ---------------------------------------------------------------
        // 加载单表
        // ---------------------------------------------------------------

        private void LoadOneTable(DataTableSource tableSource, List<ValidationIssue> issues, Dictionary<string, LoadedTable> loaded, ref int recordCount)
        {
            var tableName = tableSource.TableName;

            string text;
            try
            {
                text = tableSource.ReadText();
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope", $"读取表文本失败：{ex.Message}"));
                return;
            }

            JsonValue root;
            try
            {
                root = JsonReader.Parse(text);
            }
            catch (JsonParseException ex)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope", $"JSON 解析失败：{ex.Message}"));
                return;
            }

            if (!(root is JsonObject rootObj))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope", "顶层结构必须是 JSON 对象"));
                return;
            }

            if (!rootObj.TryGetValue("table", out var tableVal) || !(tableVal is JsonString tableStr))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope", "缺少或非法的顶层字段 \"table\""));
                return;
            }
            if (!string.Equals(tableStr.Value, tableName, StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope",
                    $"table 字段 \"{tableStr.Value}\" 与文件名 \"{tableName}\" 不一致"));
                return;
            }

            if (!rootObj.TryGetValue("schema_version", out var svVal) || !(svVal is JsonNumber svNum)
                || !svNum.TryGetInt64(out var svLong) || svLong < 1)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope",
                    "缺少或非法的顶层字段 \"schema_version\"（须为 >=1 的整数）"));
                return;
            }
            var schemaVersion = (int)svLong;

            if (!rootObj.TryGetValue("rows", out var rowsVal) || !(rowsVal is JsonArray rowsArr))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope", "缺少或非法的顶层字段 \"rows\"（须为数组）"));
                return;
            }

            TableSchema schema;
            if (_schemas.TryGetValue(tableName, out var registered))
            {
                schema = registered;
            }
            else if (_options.FailOnUnknownTable)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope", $"表 \"{tableName}\" 未通过 RegisterSchema 登记"));
                return;
            }
            else
            {
                var guessedKey = "id";
                if (rowsArr.Count > 0 && rowsArr[0] is JsonObject firstRow && !firstRow.ContainsKey("id") && firstRow.ContainsKey("key"))
                {
                    guessedKey = "key";
                }
                schema = TableSchema.Unschematized(tableName, guessedKey);
            }

            IReadOnlyList<JsonValue> effectiveRows = rowsArr;
            if (!schema.IsUnschematized)
            {
                if (schemaVersion > schema.CurrentSchemaVersion)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "schema_version",
                        $"schema_version {schemaVersion} 超过当前代码期望的版本 {schema.CurrentSchemaVersion}"));
                    return;
                }

                if (schemaVersion < schema.CurrentSchemaVersion)
                {
                    var chain = BuildMigrationChain(schema, schemaVersion, schema.CurrentSchemaVersion);
                    if (chain == null)
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "schema_version",
                            $"从版本 {schemaVersion} 到 {schema.CurrentSchemaVersion} 缺少迁移环节"));
                        return;
                    }

                    var migrated = new List<JsonValue>(rowsArr.Count);
                    for (int i = 0; i < rowsArr.Count; i++)
                    {
                        if (!(rowsArr[i] is JsonObject rowObj))
                        {
                            issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope", $"rows[{i}] 不是对象，无法迁移"));
                            continue;
                        }

                        var current = rowObj;
                        for (int m = 0; m < chain.Count; m++)
                        {
                            current = chain[m].Migrate(current);
                        }
                        migrated.Add(current);
                    }
                    effectiveRows = migrated;
                }
            }

            var recordsByKey = new Dictionary<string, DataRecord>(StringComparer.Ordinal);
            var recordsInOrder = new List<DataRecord>();
            var keyField = schema.PrimaryKey;
            var expectedDomain = tableName.Split('.')[0];

            for (int i = 0; i < effectiveRows.Count; i++)
            {
                if (!(effectiveRows[i] is JsonObject rowObj))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope", $"rows[{i}] 不是对象"));
                    continue;
                }

                if (!rowObj.TryGetValue(keyField, out var keyVal) || !(keyVal is JsonString keyStr) || keyStr.Value.Length == 0)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "primary_key",
                        $"rows[{i}] 缺少主键字段 \"{keyField}\" 或不是非空字符串"));
                    continue;
                }
                var keyRaw = keyStr.Value;

                CommonId? idValue = null;
                string recordKey;

                if (keyField == "id")
                {
                    if (!CommonId.TryParse(keyRaw, out var parsedId))
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "primary_key",
                            $"id \"{keyRaw}\" 不符合 id 格式", recordKey: keyRaw));
                        continue;
                    }
                    if (!schema.IsRegistryTable && !string.Equals(parsedId.Domain, expectedDomain, StringComparison.Ordinal))
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "primary_key",
                            $"id 的 domain 前缀 \"{parsedId.Domain}\" 应等于表名首段 \"{expectedDomain}\"", recordKey: keyRaw));
                        continue;
                    }
                    idValue = parsedId;
                    recordKey = keyRaw;
                }
                else
                {
                    if (!CommonId.IsValidFormat(keyRaw))
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "primary_key",
                            $"key \"{keyRaw}\" 不符合 id 格式", recordKey: keyRaw));
                        continue;
                    }
                    recordKey = keyRaw;

                    if (schema.HasLocaleCompositeKey)
                    {
                        if (!rowObj.TryGetValue("locale", out var localeVal) || !(localeVal is JsonString localeStr) || localeStr.Value.Length == 0)
                        {
                            issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "primary_key",
                                "复合主键表缺少 \"locale\" 字段", recordKey: keyRaw));
                            continue;
                        }
                        recordKey = keyRaw + "@" + localeStr.Value;
                    }
                }

                if (recordsByKey.ContainsKey(recordKey))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "primary_key",
                        $"主键重复：\"{recordKey}\"", recordKey: recordKey));
                    continue;
                }

                var record = new DataRecord(schema, recordKey, idValue, rowObj);
                recordsByKey.Add(recordKey, record);
                recordsInOrder.Add(record);
                recordCount++;
            }

            loaded[tableName] = new LoadedTable { Schema = schema, Records = recordsInOrder, ByKey = recordsByKey };
        }

        private static List<TableMigration>? BuildMigrationChain(TableSchema schema, int fromVersion, int toVersion)
        {
            var chain = new List<TableMigration>();
            var current = fromVersion;
            while (current < toVersion)
            {
                TableMigration? step = null;
                foreach (var m in schema.Migrations)
                {
                    if (m.FromVersion == current)
                    {
                        step = m;
                        break;
                    }
                }
                if (step == null) return null;
                chain.Add(step);
                current = step.ToVersion;
            }
            return current == toVersion ? chain : null;
        }

        // ---------------------------------------------------------------
        // 字段级校验（04 第 5 节：required_field / field_type / reference_integrity /
        // text_key_exists / expr_parsable；envelope / schema_version / primary_key 已在
        // LoadOneTable 中检查）
        // ---------------------------------------------------------------

        private void RunFieldValidation(List<ValidationIssue> issues)
        {
            foreach (var kvp in _tables)
            {
                var schema = kvp.Value.Schema;
                if (schema.IsUnschematized) continue;

                var records = kvp.Value.Records;
                for (int i = 0; i < records.Count; i++)
                {
                    var record = records[i];
                    var fields = schema.Fields;
                    for (int f = 0; f < fields.Count; f++)
                    {
                        ValidateRecordField(record, fields[f], issues);
                    }
                }
            }

            foreach (var decl in _declaredReferences)
            {
                if (!_tables.TryGetValue(decl.FromTable, out var fromLoaded)) continue;

                foreach (var record in fromLoaded.Records)
                {
                    if (!record.Has(decl.Field)) continue;
                    if (!record.TryGetString(decl.Field, out var refValue)) continue;

                    if (!ReferenceExists(decl.ToTable, refValue))
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, decl.FromTable, "reference_integrity",
                            $"字段 \"{decl.Field}\" 的值 \"{refValue}\" 在表 \"{decl.ToTable}\" 中不存在",
                            recordKey: record.Key, field: decl.Field));
                    }
                }
            }
        }

        private void ValidateRecordField(DataRecord record, FieldSchema field, List<ValidationIssue> issues)
        {
            if (!record.Has(field.Name))
            {
                if (field.Required)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, record.Table.Name, "required_field",
                        $"必填字段 \"{field.Name}\" 缺失", recordKey: record.Key, field: field.Name));
                }
                return;
            }

            var raw = record.Raw[field.Name];

            switch (field.Kind)
            {
                case FieldKind.Bool:
                    if (!(raw is JsonBool)) AddFieldTypeError(issues, record, field, raw, "Bool");
                    break;

                case FieldKind.Int:
                    if (!(raw is JsonNumber ni && ni.TryGetInt64(out _))) AddFieldTypeError(issues, record, field, raw, "Int");
                    break;

                case FieldKind.Number:
                    if (!(raw is JsonNumber)) AddFieldTypeError(issues, record, field, raw, "Number");
                    break;

                case FieldKind.String:
                    if (!(raw is JsonString)) AddFieldTypeError(issues, record, field, raw, "String");
                    break;

                case FieldKind.Id:
                    if (!(raw is JsonString sid && CommonId.IsValidFormat(sid.Value))) AddFieldTypeError(issues, record, field, raw, "Id");
                    break;

                case FieldKind.IdList:
                    ValidateIdList(record, field, raw, issues);
                    break;

                case FieldKind.Reference:
                    ValidateReferenceField(record, field, raw, issues);
                    break;

                case FieldKind.TextKey:
                    ValidateTextKeyField(record, field, raw, issues);
                    break;

                case FieldKind.Expr:
                    ValidateExprField(record, field, raw, issues);
                    break;

                case FieldKind.Enum:
                    ValidateEnumField(record, field, raw, issues);
                    break;

                case FieldKind.Vec2:
                    if (!(raw is JsonObject vo && vo.TryGetValue("x", out var xv) && xv is JsonNumber
                                              && vo.TryGetValue("y", out var yv) && yv is JsonNumber))
                    {
                        AddFieldTypeError(issues, record, field, raw, "Vec2 ({\"x\": Number, \"y\": Number})");
                    }
                    break;

                case FieldKind.Object:
                    if (!(raw is JsonObject)) AddFieldTypeError(issues, record, field, raw, "Object");
                    break;

                case FieldKind.Array:
                    if (!(raw is JsonArray)) AddFieldTypeError(issues, record, field, raw, "Array");
                    break;
            }
        }

        private static void AddFieldTypeError(List<ValidationIssue> issues, DataRecord record, FieldSchema field, JsonValue raw, string expected)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, record.Table.Name, "field_type",
                $"字段 \"{field.Name}\" 期望 {expected}，实际 JSON 类型 {raw.Kind}", recordKey: record.Key, field: field.Name));
        }

        private static void ValidateIdList(DataRecord record, FieldSchema field, JsonValue raw, List<ValidationIssue> issues)
        {
            if (!(raw is JsonArray arr))
            {
                AddFieldTypeError(issues, record, field, raw, "IdList");
                return;
            }

            for (int i = 0; i < arr.Count; i++)
            {
                if (!(arr[i] is JsonString s) || !CommonId.IsValidFormat(s.Value))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, record.Table.Name, "field_type",
                        $"字段 \"{field.Name}\" 第 {i} 个元素不是合法 Id", recordKey: record.Key, field: field.Name));
                }
            }
        }

        private void ValidateReferenceField(DataRecord record, FieldSchema field, JsonValue raw, List<ValidationIssue> issues)
        {
            if (!(raw is JsonString s) || !CommonId.IsValidFormat(s.Value))
            {
                AddFieldTypeError(issues, record, field, raw, "Reference(Id)");
                return;
            }

            var value = s.Value;

            if (field.ReferenceTable != null)
            {
                if (!ReferenceExists(field.ReferenceTable, value))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, record.Table.Name, "reference_integrity",
                        $"字段 \"{field.Name}\" 的值 \"{value}\" 在表 \"{field.ReferenceTable}\" 中不存在", recordKey: record.Key, field: field.Name));
                }
            }
            else if (field.ReferenceDomain != null)
            {
                if (!CommonId.TryParse(value, out var idVal) || !string.Equals(idVal.Domain, field.ReferenceDomain, StringComparison.Ordinal))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, record.Table.Name, "reference_integrity",
                        $"字段 \"{field.Name}\" 的值 \"{value}\" 的 domain 应为 \"{field.ReferenceDomain}\"", recordKey: record.Key, field: field.Name));
                }
                else if (!ReferenceExistsInDomain(field.ReferenceDomain, value))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, record.Table.Name, "reference_integrity",
                        $"字段 \"{field.Name}\" 的值 \"{value}\" 在 domain \"{field.ReferenceDomain}\" 下的任何已加载表中都不存在",
                        recordKey: record.Key, field: field.Name));
                }
            }
        }

        private void ValidateTextKeyField(DataRecord record, FieldSchema field, JsonValue raw, List<ValidationIssue> issues)
        {
            if (!(raw is JsonString s) || !CommonId.IsValidFormat(s.Value))
            {
                AddFieldTypeError(issues, record, field, raw, "TextKey(Id)");
                return;
            }

            var textKey = s.Value;

            if (!_tables.TryGetValue("l10n.text", out var l10nText))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, record.Table.Name, "text_key_exists",
                    "l10n.text 表未加载，跳过文本键存在性检查", recordKey: record.Key, field: field.Name));
                return;
            }

            var defaultLocale = _options.DefaultLocale.Value;
            var composite = textKey + "@" + defaultLocale;
            if (!l10nText.ByKey.ContainsKey(composite))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, record.Table.Name, "text_key_exists",
                    $"文本键 \"{textKey}\" 在语言 \"{defaultLocale}\" 下不存在", recordKey: record.Key, field: field.Name));
            }
        }

        private void ValidateExprField(DataRecord record, FieldSchema field, JsonValue raw, List<ValidationIssue> issues)
        {
            if (!(raw is JsonString s))
            {
                AddFieldTypeError(issues, record, field, raw, "Expr(String)");
                return;
            }

            if (_options.ExprSchema == null)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, record.Table.Name, "expr_parsable",
                    "ExprSchema 未配置，跳过表达式解析与静态校验", recordKey: record.Key, field: field.Name));
                return;
            }

            ExprNode node;
            try
            {
                node = ExprParser.Parse(s.Value, _options.ExprSchema);
            }
            catch (ExprParseException ex)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, record.Table.Name, "expr_parsable",
                    $"表达式解析失败：{ex.Message}", recordKey: record.Key, field: field.Name));
                return;
            }

            var exprIssues = ExprValidator.Validate(node, _options.ExprSchema);
            for (int i = 0; i < exprIssues.Count; i++)
            {
                var exprIssue = exprIssues[i];
                var severity = exprIssue.Severity == ExprIssueSeverity.Error ? ValidationSeverity.Error : ValidationSeverity.Warning;
                issues.Add(new ValidationIssue(severity, record.Table.Name, "expr_parsable", exprIssue.Message,
                    recordKey: record.Key, field: field.Name));
            }
        }

        private static void ValidateEnumField(DataRecord record, FieldSchema field, JsonValue raw, List<ValidationIssue> issues)
        {
            if (!(raw is JsonString s))
            {
                AddFieldTypeError(issues, record, field, raw, "Enum(String)");
                return;
            }

            var values = field.EnumValues;
            var found = false;
            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    if (string.Equals(values[i], s.Value, StringComparison.Ordinal)) { found = true; break; }
                }
            }

            if (!found)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, record.Table.Name, "field_type",
                    $"字段 \"{field.Name}\" 取值 \"{s.Value}\" 不在枚举合法集合内", recordKey: record.Key, field: field.Name));
            }
        }

        private bool ReferenceExists(string tableName, string value) =>
            _tables.TryGetValue(tableName, out var t) && t.ByKey.ContainsKey(value);

        private bool ReferenceExistsInDomain(string domain, string value)
        {
            foreach (var kv in _tables)
            {
                var firstSeg = kv.Key.Split('.')[0];
                if (string.Equals(firstSeg, domain, StringComparison.Ordinal) && kv.Value.ByKey.ContainsKey(value))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
