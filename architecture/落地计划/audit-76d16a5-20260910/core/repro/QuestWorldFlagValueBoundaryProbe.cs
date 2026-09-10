using Core.Foundation.DataRegistry;
using Core.Gameplay.Assembly;
using Core.Gameplay.Quest;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Presentation.Assembly;

static class QuestWorldFlagValueBoundaryProbe
{
    static string E(string table, string rows) => "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":[" + rows + "]}";
    static InMemoryDataSource Source(string value) => new InMemoryDataSource()
        .Add("l10n.locale", E("l10n.locale", "{\"id\":\"l10n.locale.zh_cn\",\"is_default\":true}"))
        .Add("l10n.text", E("l10n.text", "{\"key\":\"l10n.quest.audit.title\",\"locale\":\"l10n.locale.zh_cn\",\"text\":\"Audit\"}"))
        .Add("quest.def", E("quest.def", "{\"id\":\"quest.audit\",\"title_key\":\"l10n.quest.audit.title\",\"objectives\":[{\"type\":\"event\",\"target_ref\":\"event.audit\",\"count\":1}],\"start_method\":\"auto\",\"turn_in_method\":\"auto\",\"repeatable\":\"none\",\"rewards\":{\"world_flags\":[{\"flagKey\":\"world.flag.test\",\"value\":" + value + "}]}}"));
    static string IssueText(ValidationReport r) => string.Join(" | ", r.Issues.Select(x => x.ToString()));
    static void Main()
    {
        var invalidSource = Source("[]");
        var reportRun = ContentValidationAssembly.Run(new IDataSource[] { invalidSource });
        Console.WriteLine($"report_blocking={reportRun.Report.IsBlocking};report_issue_count={reportRun.Report.Issues.Count};report_error_count={reportRun.Report.ErrorCount};report_warning_count={reportRun.Report.WarningCount};issues={IssueText(reportRun.Report)}");
        var registry = (DataRegistry)ContentValidationAssembly.CreateRegistry(invalidSource, new ContentValidationOptions(), out _);
        var loaded = registry.LoadAll();
        try { _ = QuestDefinition.FromRecord(registry.Get("quest.def", "quest.audit")!, GameplaySchemaCatalog.FullExprSchema); Console.WriteLine("quest_parse=success"); }
        catch (Exception ex) { Console.WriteLine($"quest_parse=throws;type={ex.GetType().Name};message={ex.Message}"); }
        var positiveSource = Source("true");
        var positive = ContentValidationAssembly.Run(new IDataSource[] { positiveSource });
        var positiveRegistry = (DataRegistry)ContentValidationAssembly.CreateRegistry(positiveSource, new ContentValidationOptions(), out _);
        var positiveLoaded = positiveRegistry.LoadAll();
        var positiveParsed = QuestDefinition.FromRecord(positiveRegistry.Get("quest.def", "quest.audit")!, GameplaySchemaCatalog.FullExprSchema);
        Console.WriteLine($"positive_true_blocking={positive.Report.IsBlocking};positive_true_error_count={positive.Report.ErrorCount};positive_true_loaded_error_count={positiveLoaded.ErrorCount};positive_true_world_flag_count={positiveParsed.Rewards.WorldFlags.Count}");
        try
        {
            var malformedId = ContentValidationAssembly.Run(new IDataSource[] { Source("{\"$id\":\"BAD\"}") });
            Console.WriteLine($"invalid_id_formal_blocking={malformedId.Report.IsBlocking};invalid_id_error_count={malformedId.Report.ErrorCount}");
        }
        catch (Exception ex) { Console.WriteLine($"invalid_id_formal=throws;type={ex.GetType().Name};message={ex.Message}"); }
        Console.WriteLine("ORACLE-CURRENT=ExprValueJson.Parse accepts Bool, Number, Int, String, or {$id:...}; an Array is outside the union and must be rejected before QuestHost/QuestDefinition runtime parsing. The formal validator must report the malformed value before runtime parsing.");
        Console.WriteLine("INTERPRETATION-CURRENT=1.14 QuestContentValidationRule reports the Array shape error and formal assembly blocks registry reads; the positive value=true fixture validates the accepted path and parses one world flag. Process exit 0 is not semantic proof.");
        Console.WriteLine("EXPR-ID-CURRENT=the same QuestContentValidationRule path must also reject {$id:BAD} as a report; if IsValid lets Id constructor ArgumentException escape, formal validation itself is not exception-safe.");
    }
}

