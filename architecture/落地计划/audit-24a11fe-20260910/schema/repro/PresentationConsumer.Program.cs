using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
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
        var allowlistPath = @"D:\workespace\ws-game-artifacts\audit-24a11fe-frozen\toolchain\schema_audit_allowlist.json";
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
        Console.WriteLine("REAL_FRAMEWORK_OVERFLOW table=skill.aura_def errors=" + run.Report.ErrorCount + " warnings=" + run.Report.WarningCount + " blocking=" + run.Report.IsBlocking);
        foreach (var issue in run.Report.Issues)
        {
            if (issue.Table == "skill.aura_def") Console.WriteLine("REAL_FRAMEWORK_ISSUE " + issue);
        }
        if (!run.Report.IsBlocking && run.Registry.Get("skill.aura_def", "skill.aura_def.overflow") is DataRecord record)
        {
            Console.WriteLine("REAL_FRAMEWORK_VALUE duration_is_infinity=" + (record.TryGetNumber("duration", out var duration) && double.IsInfinity(duration)));
            if (record.TryGetArray("effects", out var effects) && effects.Count > 0 && effects[0] is Core.Foundation.Common.Json.JsonObject effect && effect.TryGetValue("params", out var raw) && raw is Core.Foundation.Common.Json.JsonObject parameters && parameters.TryGetValue("interval", out var intervalRaw) && intervalRaw is Core.Foundation.Common.Json.JsonNumber interval)
            {
                Console.WriteLine("REAL_FRAMEWORK_VALUE interval_is_infinity=" + double.IsInfinity(interval.Value));
            }
        }

        var exprSource = new InMemoryDataSource().Add("skill.proc_def", "{\"table\":\"skill.proc_def\",\"schema_version\":1,\"rows\":[{\"id\":\"skill.proc_def.overflow\",\"trigger_event\":\"combat.damage_dealt\",\"condition\":\"9223372036854775808\",\"trigger_skill\":\"skill.sample_burn\",\"proc_chance\":0.5}]}");
        IDataRegistry? exprRegistry = null;
        try
        {
            exprRegistry = ContentValidationAssembly.CreateRegistry(exprSource, new ContentValidationOptions(), out _);
            var exprReport = exprRegistry.LoadAll();
            Console.WriteLine("REAL_FRAMEWORK_EXPR_OVERFLOW errors=" + exprReport.ErrorCount + " warnings=" + exprReport.WarningCount + " blocking=" + exprReport.IsBlocking);
            foreach (var issue in exprReport.Issues)
            {
                if (issue.Table == "skill.proc_def") Console.WriteLine("REAL_FRAMEWORK_EXPR_ISSUE " + issue);
            }
            try
            {
                var count = exprRegistry.GetAll("skill.proc_def").Count;
                Console.WriteLine("REAL_FRAMEWORK_EXPR_READABLE count=" + count);
            }
            catch (Exception readEx)
            {
                Console.WriteLine("REAL_FRAMEWORK_EXPR_READABLE threw=" + readEx.GetType().Name + " message=" + readEx.Message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("REAL_FRAMEWORK_EXPR_THROW threw=" + ex.GetType().Name + " message=" + ex.Message);
            if (exprRegistry != null)
            {
                try
                {
                    Console.WriteLine("REAL_FRAMEWORK_EXPR_READABLE_AFTER_THROW count=" + exprRegistry.GetAll("skill.proc_def").Count);
                }
                catch (Exception readEx)
                {
                    Console.WriteLine("REAL_FRAMEWORK_EXPR_READABLE_AFTER_THROW threw=" + readEx.GetType().Name + " message=" + readEx.Message);
                }
            }
        }
    }
}
