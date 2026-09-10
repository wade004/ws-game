using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;

internal sealed class MapExprSchema : IExprSchema
{
    public bool TryGetSignature(string group, string key, out ExprSignature signature)
    {
        if (group == "event" && key == "value")
        {
            signature = new ExprSignature(ExprValueKind.Number, Array.Empty<ExprValueKind>());
            return true;
        }
        signature = default;
        return false;
    }
}

internal static class Program
{
    private static IEventBus Bus() => new EventBus(
        EventCatalog.FromDefinitions(new[]
        {
            new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
            new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
        }), new EventBusOptions { StrictCatalog = false });

    private static void Main()
    {
        Console.WriteLine("SCHEMA_CONSUMER_BEGIN");
        LexerCases();
        RangeCases();
        RegistryCases();
        Console.WriteLine("SCHEMA_CONSUMER_END");
    }

    private static void LexerCases()
    {
        var text = "  event.value == \"a\\\"b\" and quest.foo  ";
        var tokens = ExprLexer.Tokenize(text);
        Console.WriteLine("LEXER token_count=" + tokens.Count);
        foreach (var token in tokens)
        {
            Console.WriteLine("TOKEN kind=" + token.Kind + " text=" + Escape(token.Text) + " start=" + token.Start + " length=" + token.Length + " raw=" + Escape(text.Substring(token.Start, token.Length)));
        }
        var schema = new MapExprSchema();
        var parsed = ExprParser.Parse(text, schema);
        Console.WriteLine("PARSER success=" + (parsed != null));
        foreach (var bad in new[] { "event.value = 1", "event.value == \"unterminated" })
        {
            try { ExprLexer.Tokenize(bad); Console.WriteLine("LEXER bad_input=" + Escape(bad) + " outcome=ACCEPT"); }
            catch (Exception ex) { Console.WriteLine("LEXER bad_input=" + Escape(bad) + " outcome=" + ex.GetType().Name + " position=" + (ex is ExprParseException pe ? pe.Position.ToString() : "NA")); }
            try { ExprParser.Parse(bad, schema); Console.WriteLine("PARSER bad_input=" + Escape(bad) + " outcome=ACCEPT"); }
            catch (Exception ex) { Console.WriteLine("PARSER bad_input=" + Escape(bad) + " outcome=" + ex.GetType().Name + " position=" + (ex is ExprParseException pe ? pe.Position.ToString() : "NA")); }
        }
        try { ExprLexer.Tokenize("9223372036854775808"); Console.WriteLine("LEXER overflow_int outcome=ACCEPT"); }
        catch (Exception ex) { Console.WriteLine("LEXER overflow_int outcome=" + ex.GetType().Name + " position=" + (ex is ExprParseException pe ? pe.Position.ToString() : "NA")); }
        try
        {
            var huge = ExprLexer.Tokenize(new string('9', 500) + ".1")[0];
            Console.WriteLine("LEXER overflow_number outcome=ACCEPT number_is_infinity=" + double.IsInfinity(huge.NumberValue) + " value=" + huge.NumberValue);
        }
        catch (Exception ex) { Console.WriteLine("LEXER overflow_number outcome=" + ex.GetType().Name + " position=" + (ex is ExprParseException pe ? pe.Position.ToString() : "NA")); }
    }

    private static void RangeCases()
    {
        foreach (var label in new[] { "NaNMin", "InfinityMin", "NaNMax" })
        {
            try
            {
                var range = label == "NaNMin" ? FieldRange.Range(min: double.NaN) : label == "InfinityMin" ? FieldRange.Range(min: double.PositiveInfinity) : FieldRange.Range(max: double.NaN);
                Console.WriteLine("RANGE label=" + label + " outcome=ACCEPT describe=" + range.Describe() + " contains0=" + range.Contains(0));
            }
            catch (Exception ex) { Console.WriteLine("RANGE label=" + label + " outcome=" + ex.GetType().Name); }
        }
    }

    private static void RegistryCases()
    {
        var schema = new TableSchema("test.number", "id", 1, new[]
        {
            new FieldSchema("id", FieldKind.Id, true),
            new FieldSchema("value", FieldKind.Number, false, description: "finite value").WithRange(FieldRange.Range(min: 0)),
        });
        foreach (var raw in new[] { "1", "1e309", "-1e309" })
        {
            var source = new InMemoryDataSource().Add("test.number", "{\"table\":\"test.number\",\"schema_version\":1,\"rows\":[{\"id\":\"test.one\",\"value\":" + raw + "}]}");
            var registry = new DataRegistry(source, Bus());
            registry.RegisterSchema(schema);
            try
            {
                var report = registry.LoadAll();
                var loaded = !report.IsBlocking ? registry.Get("test.number", "test.one") : null;
                var actual = loaded != null && loaded.TryGetNumber("value", out var loadedValue)
                    ? (double.IsInfinity(loadedValue) ? (loadedValue > 0 ? "+Infinity" : "-Infinity") : loadedValue.ToString())
                    : "<unloaded>";
                Console.WriteLine("JSON_NUMBER raw=" + raw + " errors=" + report.ErrorCount + " warnings=" + report.WarningCount + " blocking=" + report.IsBlocking + " actual=" + actual);
                foreach (var issue in report.Issues) Console.WriteLine("ISSUE raw=" + raw + " " + issue);
            }
            catch (Exception ex) { Console.WriteLine("JSON_NUMBER raw=" + raw + " threw=" + ex.GetType().Name + " message=" + Escape(ex.Message)); }
        }
        var nan = new JsonNumber(double.NaN);
        Console.WriteLine("DIRECT_JSON_NUMBER nan_kind=" + nan.Kind + " value_is_nan=" + double.IsNaN(nan.Value));
        var parsedHuge = JsonReader.Parse("1e309");
        Console.WriteLine("JSON_READER_OVERFLOW kind=" + parsedHuge.Kind + " value_is_infinity=" + double.IsInfinity(((JsonNumber)parsedHuge).Value));
        try { Console.WriteLine("JSON_WRITER_OVERFLOW outcome=" + JsonWriter.Write(parsedHuge)); }
        catch (Exception ex) { Console.WriteLine("JSON_WRITER_OVERFLOW outcome=" + ex.GetType().Name); }
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"");
}
