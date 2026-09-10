using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.Economy;
using Core.Rules.Common;
using Presentation.Assembly;

internal static class Program
{
    private static void Main()
    {
        Console.WriteLine("PRESENTATION_CONSUMER_BEGIN");
        var schemas = SchemaAudit.EnumerateRegisteredSchemas();
        var report = SchemaAudit.Run(schemas, SchemaAuditAllowlist.Empty);
        Console.WriteLine("SCHEMA_AUDIT tables=" + report.TableCount + " fields=" + report.FieldCount + " errors=" + report.ErrorCount + " warnings=" + report.WarningCount + " blocking=" + report.IsBlocking);
        foreach (var issue in report.Issues) Console.WriteLine("AUDIT " + issue.Severity + " " + issue.Table + "/" + issue.FieldPath + " " + issue.Check + " " + issue.Message);
        var allowlistPath = Environment.GetEnvironmentVariable("VALIDATION_ALLOWLIST")
            ?? Path.Combine(AppContext.BaseDirectory, "schema_audit_allowlist.json");
        if (!File.Exists(allowlistPath)) throw new FileNotFoundException("schema audit allowlist missing", allowlistPath);
        var allowlisted = SchemaAudit.Run(schemas, SchemaAuditAllowlist.Parse(File.ReadAllText(allowlistPath)));
        Console.WriteLine("SCHEMA_AUDIT_ALLOWLIST tables=" + allowlisted.TableCount + " fields=" + allowlisted.FieldCount + " errors=" + allowlisted.ErrorCount + " warnings=" + allowlisted.WarningCount + " blocking=" + allowlisted.IsBlocking);
        foreach (var schema in schemas.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            Console.WriteLine("TABLE name=" + schema.Name + " layer=" + schema.Layer + " module=" + schema.Module + " domain=" + schema.Domain + " scope=" + schema.TimeScope + " fields=" + schema.Fields.Count);
            foreach (var field in schema.Fields)
            {
                if (field.Kind == FieldKind.IdList || field.Range != null || field.Unit == FieldUnit.Time)
                    Console.WriteLine("FIELD table=" + schema.Name + " name=" + field.Name + " kind=" + field.Kind + " group=" + field.Group + " unit=" + field.Unit + " refTable=" + (field.ReferenceTable ?? "") + " refDomain=" + (field.ReferenceDomain ?? "") + " freeIds=" + field.FreeIds + " range=" + (field.Range?.Describe() ?? ""));
            }
        }
        var stringRange = new FieldSchema("text", FieldKind.String, false, description: "bad range").WithRange(FieldRange.Range(min: 0));
        var badRange = new TableSchema("found.audit_bad", "id", 1, new[] { new FieldSchema("id", FieldKind.Id, true, description: "id"), stringRange }).WithOwnership(SchemaLayer.Foundation, "audit");
        var badReport = SchemaAudit.Run(new[] { badRange }, SchemaAuditAllowlist.Empty);
        Console.WriteLine("BAD_RANGE errors=" + badReport.ErrorCount + " field_range_kind=" + badReport.Issues.Count(x => x.Check == "field_range_kind"));
        var exported = SchemaFieldRangeExport.Collect(badRange);
        Console.WriteLine("BAD_RANGE export_count=" + exported.Count);
        DomainRuntimeCase();
        RealFrameworkOverflowCase();
        FiniteAuraControlCase();
        ExprReloadRecoveryCase();
        ExprDoubleOverflowCase();
        FieldRangeAndJsonNumberCase();
        NonFiniteNumberMigrationCase();
        UnexpectedValidationExceptionCase();
        SchemaVersionBoundaryCase();
        CurrencySavePrecisionCase();
        Console.WriteLine("PRESENTATION_CONSUMER_END");
    }

    private static void DomainRuntimeCase()
    {
        var camera = new TableSchema("camera_profile", "id", 1, new[]
        {
            new FieldSchema("id", FieldKind.Id, true, description: "camera id"),
        }).WithOwnership(SchemaLayer.Presentation, "camera").WithDomain("camera");
        var consumer = new TableSchema("found.consumer", "id", 1, new[]
        {
            new FieldSchema("id", FieldKind.Id, true, description: "consumer id"),
            new FieldSchema("camera", FieldKind.Reference, false, referenceDomain: "camera", description: "camera ref"),
        }).WithOwnership(SchemaLayer.Foundation, "test");
        var source = new InMemoryDataSource()
            .Add("camera_profile", "{\"table\":\"camera_profile\",\"schema_version\":1,\"rows\":[{\"id\":\"camera_profile.default\"}]}")
            .Add("found.consumer", "{\"table\":\"found.consumer\",\"schema_version\":1,\"rows\":[{\"id\":\"found.consumer_one\",\"camera\":\"camera_profile.default\"}]}");
        var registry = new DataRegistry(source, new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false }));
        registry.RegisterSchema(camera);
        registry.RegisterSchema(consumer);
        var report = registry.LoadAll();
        Console.WriteLine("DOMAIN_RUNTIME tableDomain=" + camera.Domain + " id=" + "camera_profile.default" + " errors=" + report.ErrorCount + " cameraRefErrors=" + report.Issues.Count(x => x.Check == "reference_integrity"));
        foreach (var issue in report.Issues) Console.WriteLine("DOMAIN_ISSUE " + issue);
    }

    private static void RealFrameworkOverflowCase()
    {
        var source = new InMemoryDataSource().Add("skill.aura_def", "{\"table\":\"skill.aura_def\",\"schema_version\":1,\"rows\":[{\"id\":\"skill.aura_def.overflow\",\"duration\":1e309,\"effects\":[{\"kind\":\"periodic_damage\",\"params\":{\"interval\":1e309,\"base_value\":1,\"school\":\"school.fire\"}}]}]}");
        var run = ContentValidationAssembly.Run(new[] { source });
        Console.WriteLine("F01_REAL_AURA input=duration:1e309, effects[0].params.interval:1e309 expected=JSON parse rejects non-finite and registry blocks actual_errors=" + run.Report.ErrorCount + " actual_blocking=" + run.Report.IsBlocking);
        foreach (var issue in run.Report.Issues)
        {
            if (issue.Table == "skill.aura_def") Console.WriteLine("F01_REAL_AURA_ISSUE " + issue);
        }
        TryReadBlocked(run.Registry, "skill.aura_def", "F01_REAL_AURA_READ");

        var exprSource = new InMemoryDataSource().Add("skill.proc_def", "{\"table\":\"skill.proc_def\",\"schema_version\":1,\"rows\":[{\"id\":\"skill.proc_def.overflow\",\"trigger_event\":\"combat.damage_dealt\",\"condition\":\"9223372036854775808\",\"trigger_skill\":\"skill.sample_burn\",\"proc_chance\":0.5}]}").Add("skill.def", SkillDefJson()).Add("target.chain_def", TargetChainJson());
        IDataRegistry? exprRegistry = null;
        try
        {
            exprRegistry = ContentValidationAssembly.CreateRegistry(exprSource, new ContentValidationOptions(), out _);
            var exprReport = exprRegistry.LoadAll();
            Console.WriteLine("F03_REAL_PROC input=condition:9223372036854775808 expected=expr_parsable error, blocking, no throw actual_errors=" + exprReport.ErrorCount + " actual_blocking=" + exprReport.IsBlocking);
            foreach (var issue in exprReport.Issues)
            {
                if (issue.Table == "skill.proc_def") Console.WriteLine("F03_REAL_PROC_ISSUE " + issue);
            }
            TryReadBlocked(exprRegistry, "skill.proc_def", "F03_REAL_PROC_READ");
        }
        catch (Exception ex)
        {
            Console.WriteLine("F03_REAL_PROC_THROW unexpected=" + ex.GetType().Name + " message=" + ex.Message);
            if (exprRegistry != null)
            {
                try
                {
                    Console.WriteLine("F03_REAL_PROC_READ_AFTER_THROW count=" + exprRegistry.GetAll("skill.proc_def").Count);
                }
                catch (Exception readEx)
                {
                    Console.WriteLine("F03_REAL_PROC_READ_AFTER_THROW threw=" + readEx.GetType().Name + " message=" + readEx.Message);
                }
            }
        }
    }

    private static void ExprReloadRecoveryCase()
    {
        var source = new MutableSource("skill.proc_def", ProcJson("true")).Add("skill.def", SkillDefJson()).Add("target.chain_def", TargetChainJson());
        var registry = ContentValidationAssembly.CreateRegistry(source, new ContentValidationOptions(), out _);
        var initial = registry.LoadAll();
        Console.WriteLine("F03_RELOAD_BASELINE input=condition:true expected=readable actual_errors=" + initial.ErrorCount + " actual_blocking=" + initial.IsBlocking + " issues=" + string.Join("|", initial.Issues));
        if (!initial.IsBlocking) Console.WriteLine("F03_RELOAD_BASELINE_READ actual_count=" + registry.GetAll("skill.proc_def").Count);
        source.Json = ProcJson("9223372036854775808");
        var bad = registry.Reload("skill.proc_def");
        Console.WriteLine("F03_RELOAD_BAD input=condition:9223372036854775808 expected=blocking+read shield actual_errors=" + bad.ErrorCount + " actual_blocking=" + bad.IsBlocking);
        TryReadBlocked(registry, "skill.proc_def", "F03_RELOAD_BAD_READ");
        source.Json = ProcJson("true");
        var fixedReport = registry.Reload("skill.proc_def");
        Console.WriteLine("F03_RELOAD_RECOVER input=condition:true expected=unblocked+record restored actual_errors=" + fixedReport.ErrorCount + " actual_blocking=" + fixedReport.IsBlocking + " actual_count=" + registry.GetAll("skill.proc_def").Count);
    }

    private static void FiniteAuraControlCase()
    {
        var source = new InMemoryDataSource().Add("skill.aura_def", "{\"table\":\"skill.aura_def\",\"schema_version\":1,\"rows\":[{\"id\":\"skill.aura_def.finite\",\"duration\":1e308,\"effects\":[{\"kind\":\"periodic_damage\",\"params\":{\"interval\":1e308,\"base_value\":1,\"school\":\"school.fire\"}}]}]}");
        var run = ContentValidationAssembly.Run(new[] { source });
        var duration = "<unreadable>"; var interval = "<unreadable>";
        if (!run.Report.IsBlocking && run.Registry.Get("skill.aura_def", "skill.aura_def.finite") is DataRecord record && record.TryGetNumber("duration", out var d))
        {
            duration = d.ToString("R", CultureInfo.InvariantCulture);
            if (record.TryGetArray("effects", out var effects) && effects.Count > 0 && effects[0] is JsonObject effect && effect.TryGetValue("params", out var p) && p is JsonObject parameters && parameters.TryGetValue("interval", out var raw) && raw is JsonNumber n)
                interval = n.Value.ToString("R", CultureInfo.InvariantCulture);
        }
        Console.WriteLine("F01_REAL_AURA_FINITE input=duration:1e308, effects[0].params.interval:1e308 expected=finite values accepted actual_errors=" + run.Report.ErrorCount + " actual_blocking=" + run.Report.IsBlocking + " duration=" + duration + " interval=" + interval);
    }

    private static void ExprDoubleOverflowCase()
    {
        var input = new string('9', 500) + ".1";
        try { var token = ExprLexer.Tokenize(input)[0]; Console.WriteLine("F03_EXPR_DOUBLE input=500digit+.1 expected=ExprParseException actual=ACCEPT value=" + token.NumberValue); }
        catch (Exception ex) { Console.WriteLine("F03_EXPR_DOUBLE input=500digit+.1 expected=ExprParseException actual=" + ex.GetType().Name + " position=" + (ex is ExprParseException pe ? pe.Position.ToString() : "NA")); }
    }

    private static void FieldRangeAndJsonNumberCase()
    {
        foreach (var label in new[] { "NaNMin", "PositiveInfinityMax", "NegativeInfinityMin" })
        {
            try
            {
                var range = label == "NaNMin" ? FieldRange.Range(min: double.NaN) : label == "PositiveInfinityMax" ? FieldRange.Range(max: double.PositiveInfinity) : FieldRange.Range(min: double.NegativeInfinity);
                Console.WriteLine("F02_RANGE input=" + label + " expected=ArgumentException actual=ACCEPT describe=" + range.Describe());
            }
            catch (Exception ex) { Console.WriteLine("F02_RANGE input=" + label + " expected=ArgumentException actual=" + ex.GetType().Name); }
        }
        var finiteRange = FieldRange.Range(min: 0, max: 10);
        Console.WriteLine("F02_RANGE_VALUE input=Contains(NaN) expected=false actual=" + finiteRange.Contains(double.NaN));

        var parsedOverflow = (JsonNumber)JsonReader.Parse("9223372036854775808");
        Console.WriteLine("JSON_INT_RAW input=9223372036854775808 expected=TryGetInt64 false actual=" + parsedOverflow.TryGetInt64(out var rawValue) + " value=" + rawValue);
        var parsedMax = (JsonNumber)JsonReader.Parse("9223372036854775807");
        Console.WriteLine("JSON_INT_RAW_MAX input=9223372036854775807 expected=true exact actual=" + parsedMax.TryGetInt64(out var maxValue) + " value=" + maxValue);
        var direct = new JsonNumber(9223372036854775808d);
        var directOk = direct.TryGetInt64(out var directValue);
        Console.WriteLine("JSON_INT_DIRECT input=new JsonNumber(2^63) expected=false actual=" + directOk + " value=" + directValue + " candidate=JsonValue.cs:108");
        IntMigrationCase();
    }

    private static void IntMigrationCase()
    {
        MigrateDelegate migrate = row =>
        {
            var b = new JsonObjectBuilder();
            foreach (var kv in row) b.Add(kv.Key, kv.Key == "count" ? new JsonNumber(9223372036854775808d) : kv.Value);
            return b.Build();
        };
        var schema = new TableSchema("test.int", "id", 2,
            new[] { new FieldSchema("id", FieldKind.Id, true), new FieldSchema("count", FieldKind.Int, true) },
            migrations: new[] { new TableMigration(1, 2, migrate) });
        var source = new InMemoryDataSource().Add("test.int", "{\"table\":\"test.int\",\"schema_version\":1,\"rows\":[{\"id\":\"test.int.a\",\"count\":1}]}");
        var registry = new DataRegistry(source, MakeBus()); registry.RegisterSchema(schema);
        var report = registry.LoadAll();
        Console.WriteLine("INT_FIELD_DIRECT input=migration count=2^63 expected=field_type blocking actual_errors=" + report.ErrorCount + " actual_blocking=" + report.IsBlocking + " issues=" + string.Join("|", report.Issues));
    }

    private static void NonFiniteNumberMigrationCase()
    {
        MigrateDelegate migrate = row =>
        {
            var b = new JsonObjectBuilder();
            foreach (var kv in row) b.Add(kv.Key, kv.Key == "value" ? new JsonNumber(double.PositiveInfinity) : kv.Value);
            return b.Build();
        };
        var schema = new TableSchema("test.number_direct", "id", 2,
            new[] { new FieldSchema("id", FieldKind.Id, true), new FieldSchema("value", FieldKind.Number, true) },
            migrations: new[] { new TableMigration(1, 2, migrate) });
        var source = new InMemoryDataSource().Add("test.number_direct", "{\"table\":\"test.number_direct\",\"schema_version\":1,\"rows\":[{\"id\":\"test.number_direct.a\",\"value\":1}]}");
        var registry = new DataRegistry(source, MakeBus()); registry.RegisterSchema(schema);
        var report = registry.LoadAll();
        Console.WriteLine("NUMBER_FIELD_DIRECT input=migration value=+Infinity expected=field_finite blocking actual_errors=" + report.ErrorCount + " actual_blocking=" + report.IsBlocking + " issues=" + string.Join("|", report.Issues));
    }

    private sealed class ThrowingValidationRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view) { throw new InvalidOperationException("validation probe unexpected exception"); }
    }

    private static void UnexpectedValidationExceptionCase()
    {
        var schema = new TableSchema("test.throwing_rule", "id", 1, new[] { new FieldSchema("id", FieldKind.Id, true) });
        var source = new InMemoryDataSource().Add("test.throwing_rule", "{\"table\":\"test.throwing_rule\",\"schema_version\":1,\"rows\":[{\"id\":\"test.throwing_rule.a\"}]}");
        var registry = new DataRegistry(source, MakeBus()); registry.RegisterSchema(schema); registry.RegisterValidationRule(new ThrowingValidationRule());
        string loadActual;
        try { registry.LoadAll(); loadActual = "returned"; }
        catch (Exception ex) { loadActual = ex.GetType().Name; }
        string readActual;
        try { readActual = "count=" + registry.GetAll("test.throwing_rule").Count; }
        catch (Exception ex) { readActual = ex.GetType().Name + ":" + ex.Message; }
        Console.WriteLine("VALIDATION_EXCEPTION input=custom rule throws expected=load propagates+blocked read actual_load=" + loadActual + " actual_read=" + readActual);
    }

    private static void SchemaVersionBoundaryCase()
    {
        var schema = new TableSchema("test.version", "id", 1, new[] { new FieldSchema("id", FieldKind.Id, true) });
        foreach (var sv in new[] { "1", "2", "4294967297" })
        {
            var source = new InMemoryDataSource().Add("test.version", "{\"table\":\"test.version\",\"schema_version\":" + sv + ",\"rows\":[{\"id\":\"test.version.a\"}]}");
            var registry = new DataRegistry(source, MakeBus()); registry.RegisterSchema(schema);
            var report = registry.LoadAll();
            Console.WriteLine("SCHEMA_VERSION input=" + sv + " expected=" + (sv == "1" ? "accept" : "blocking") + " actual_errors=" + report.ErrorCount + " actual_blocking=" + report.IsBlocking + " issues=" + string.Join("|", report.Issues));
        }
    }

    private static void CurrencySavePrecisionCase()
    {
        const long intended = 9007199254740993L;
        var currency = "[{\"id\":\"econ.currency.precision\",\"name_key\":\"l10n.currency.precision\",\"display_ref\":\"display.precision\"}]";
        var source = new InMemoryDataSource().Add("econ.currency", Envelope("econ.currency", currency)).Add("econ.vendor", Envelope("econ.vendor", "[]"));
        var registry = new DataRegistry(source, MakeBus()); registry.RegisterSchema(EconomySchemas.Currency); registry.RegisterSchema(EconomySchemas.Vendor); registry.RegisterValidationRule(new EconomyContentValidationRule());
        var report = registry.LoadAll();
        if (report.IsBlocking) { Console.WriteLine("CURRENCY_PRECISION expected=excluded_due_to_schema actual=blocked issues=" + string.Join("|", report.Issues)); return; }
        var bus = MakeBus(); var host = new EconomyHost(registry, bus, new NullInventory(), new NullExprFactory());
        var unit = new Id("player.precision"); var cid = new Id("econ.currency.precision");
        host.SetBalance(unit, cid, intended);
        var saved = new CurrencyPersistable(unit, host).Save(); var text = JsonWriter.Write(saved); var roundtrip = JsonReader.Parse(text);
        var host2 = new EconomyHost(registry, MakeBus(), new NullInventory(), new NullExprFactory()); new CurrencyPersistable(unit, host2).Load(roundtrip);
        Console.WriteLine("CURRENCY_PRECISION input=" + intended + " expected=exact roundtrip actual_text=" + text + " actual_balance=" + host2.GetBalance(unit, cid) + " exact=" + (host2.GetBalance(unit, cid) == intended));
    }

    private static string ProcJson(string condition) => "{\"table\":\"skill.proc_def\",\"schema_version\":1,\"rows\":[{\"id\":\"skill.proc_def.validation\",\"trigger_event\":\"combat.damage_dealt\",\"condition\":\"" + condition + "\",\"trigger_skill\":\"skill.sample_burn\",\"proc_chance\":0.5}]}";
    private static string SkillDefJson() => "{\"table\":\"skill.def\",\"schema_version\":1,\"rows\":[{\"id\":\"skill.sample_burn\",\"school\":\"school.fire\",\"kind\":\"active\",\"range\":5,\"cast_time\":0,\"respects_gcd\":false,\"target_shape_ref\":\"target.chain_def.single\",\"effects\":[{\"kind\":\"school_damage\",\"params\":{\"base_value\":1,\"school\":\"school.fire\"}}]}] }";
    private static string TargetChainJson() => "{\"table\":\"target.chain_def\",\"schema_version\":1,\"rows\":[{\"id\":\"target.chain_def.single\",\"source\":\"self\",\"shape\":{\"kind\":\"circle\",\"radius\":5}}]}";
    private static string Envelope(string table, string rows) => "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rows + "}";
    private static IEventBus MakeBus() => new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
    private static void TryReadBlocked(IDataRegistryView registry, string table, string label)
    {
        try { Console.WriteLine(label + " expected=InvalidOperationException actual=count=" + registry.GetAll(table).Count); }
        catch (Exception ex) { Console.WriteLine(label + " expected=InvalidOperationException actual=" + ex.GetType().Name + " message=" + ex.Message); }
    }

    private sealed class MutableSource : IDataSource
    {
        private readonly Dictionary<string, string> _json = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Json { get => _json["skill.proc_def"]; set => _json["skill.proc_def"] = value; }
        public MutableSource(string table, string json) { _json[table] = json; }
        public MutableSource Add(string table, string json) { _json[table] = json; return this; }
        public IReadOnlyList<DataTableSource> ListTables()
        {
            var result = new List<DataTableSource>();
            foreach (var table in _json.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                var captured = table;
                result.Add(new DataTableSource(captured, "memory://" + captured, () => _json[captured]));
            }
            return result;
        }
    }

    private sealed class NullInventory : IInventoryHost
    {
        public bool AddItem(Id unitId, Id templateId, int count) => false;
        public bool RemoveItem(Id unitId, Id instanceId, int count) => false;
        public IReadOnlyList<ItemInstance> ListItems(Id unitId) => Array.Empty<ItemInstance>();
        public int CountOf(Id unitId, Id templateId) => 0;
        public ItemInstance? FindInstance(Id unitId, Id instanceId) => null;
    }

    private sealed class NullExprFactory : IExprHostFactory
    {
        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => new NullExprHost();
        private sealed class NullExprHost : IExprHost { public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args) => ExprValue.OfBool(false); }
    }
}
