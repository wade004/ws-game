using Core.Carriers.Assembly;
using Core.Carriers.Gobj;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Presentation.Assembly;

static class GobjLockBoundaryProbe
{
    static string Envelope(string row) => "{\"table\":\"gobj.lock\",\"schema_version\":1,\"rows\":[" + row + "]}";
    static InMemoryDataSource Source(bool expected) => new InMemoryDataSource().Add("gobj.lock", Envelope(expected
        ? "{\"id\":\"gobj.lock.audit\",\"requirement\":{\"kind\":\"world_flag\",\"flag_key\":\"world.flag.test\",\"expected\":true}}"
        : "{\"id\":\"gobj.lock.audit\",\"requirement\":{\"kind\":\"world_flag\",\"flag_key\":\"world.flag.test\"}}"));
    static IEventBus Bus() => new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
    static string Issues(ValidationReport r) => string.Join(" | ", r.Issues.Select(i => i.ToString()));
    static void Main()
    {
        var missing = Source(false);
        var formal = ContentValidationAssembly.Run(new IDataSource[] { missing });
        Console.WriteLine($"formal_blocking={formal.Report.IsBlocking};formal_issue_count={formal.Report.Issues.Count};formal_error_count={formal.Report.ErrorCount};formal_warning_count={formal.Report.WarningCount};issues={Issues(formal.Report)}");
        var bus = Bus();
        var registry = new DataRegistry(missing, bus, new DataRegistryOptions { FailOnUnknownTable = true });
        CarriersSchemaCatalog.RegisterAll(registry);
        var carriers = registry.LoadAll();
        Console.WriteLine($"carriers_blocking={carriers.IsBlocking};carriers_issue_count={carriers.Issues.Count};carriers_error_count={carriers.ErrorCount};carriers_warning_count={carriers.WarningCount}");
        var record = registry.Get("gobj.lock", "gobj.lock.audit")!;
        try { _ = LockDef.FromRecord(record); Console.WriteLine("lockdef_parse=success"); }
        catch (Exception ex) { Console.WriteLine($"lockdef_parse=throws;type={ex.GetType().Name};message={ex.Message}"); }
        var positive = ContentValidationAssembly.Run(new IDataSource[] { Source(true) });
        var positiveBus = Bus(); var positiveRegistry = new DataRegistry(Source(true), positiveBus, new DataRegistryOptions { FailOnUnknownTable = true }); CarriersSchemaCatalog.RegisterAll(positiveRegistry); var positiveReport = positiveRegistry.LoadAll();
        var positiveDef = LockDef.FromRecord(positiveRegistry.Get("gobj.lock", "gobj.lock.audit")!);
        Console.WriteLine($"positive_expected_formal_blocking={positive.Report.IsBlocking};positive_expected_error_count={positive.Report.ErrorCount};positive_carriers_error_count={positiveReport.ErrorCount};positive_lockdef_kind={positiveDef.Requirement.Kind}");
        Console.WriteLine("ORACLE-CURRENT=world_flag requirement requires flag_key and expected; missing expected should be rejected by formal validation before runtime parse, while expected=true is valid.");
        Console.WriteLine("INTERPRETATION-CURRENT=Current 1.13 schema validation reports no issue for the missing expected variant, but LockDef.FromRecord throws DataFieldException. The successful process exit is not semantic proof; the positive fixture confirms the parser path with expected=true.");
    }
}
