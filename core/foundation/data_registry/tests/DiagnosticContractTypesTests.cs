using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// 诊断/分析类契约类型的直接断言（T-L2，2026-10-01 测试覆盖第四批）：
    /// <see cref="ValidationIssueJsonWriter"/>（<c>JsonEscape</c> 控制字符 / 代理对 / 往返、
    /// <c>AppendIssueJson</c> 字段与可空字段）、<see cref="DataSourceDiagnostic"/>、
    /// <see cref="ContentGraphAnalysis"/>（<c>NodeIds</c> 与构造守卫）、<see cref="TolerantReadDiagnostics"/>。
    /// </summary>
    public sealed class DiagnosticContractTypesTests
    {
        // -----------------------------------------------------------------
        // ValidationIssueJsonWriter.JsonEscape
        // -----------------------------------------------------------------

        [Theory]
        [InlineData("", "")]
        [InlineData("plain", "plain")]
        [InlineData("a\"b", "a\\\"b")]
        [InlineData("a\\b", "a\\\\b")]
        [InlineData("line1\nline2", "line1\\nline2")]
        [InlineData("a\rb", "a\\rb")]
        [InlineData("a\tb", "a\\tb")]
        [InlineData("a/b", "a/b")]
        public void JsonEscape_NamedEscapesAndPassThrough(string input, string expected)
        {
            Assert.Equal(expected, ValidationIssueJsonWriter.JsonEscape(input));
        }

        [Fact]
        public void JsonEscape_EveryOtherControlCharacter_BecomesLowercaseFourDigitUnicodeEscape()
        {
            for (var code = 0; code < 0x20; code++)
            {
                var c = (char)code;
                if (c == '\n' || c == '\r' || c == '\t')
                {
                    continue;
                }

                var expected = "\\u" + code.ToString("x4", CultureInfo.InvariantCulture);
                Assert.Equal(expected, ValidationIssueJsonWriter.JsonEscape(c.ToString()));
            }
        }

        [Fact]
        public void JsonEscape_NonControlCharacters_AreLeftUntouched_IncludingDelAndNonAscii()
        {
            // 0x20 及以上（含 DEL 0x7F）按 JSON 语法无需转义；非 ASCII 原样保留。
            var s = " ~\u007f\u00e9\u6c49\u5b57";
            Assert.Equal(s, ValidationIssueJsonWriter.JsonEscape(s));
        }

        [Fact]
        public void JsonEscape_SurrogatePair_IsKeptIntact_NotSplitOrEscaped()
        {
            var emoji = char.ConvertFromUtf32(0x1F600);
            Assert.Equal(2, emoji.Length);

            var escaped = ValidationIssueJsonWriter.JsonEscape("x" + emoji + "y");

            Assert.Equal("x" + emoji + "y", escaped);
        }

        [Fact]
        public void JsonEscape_Output_RoundTripsThroughJsonReader_ForAdversarialText()
        {
            var sb = new StringBuilder();
            for (var code = 0; code < 0x80; code++)
            {
                sb.Append((char)code);
            }

            sb.Append("\"\\\u6c49").Append(char.ConvertFromUtf32(0x1F600));
            var original = sb.ToString();

            var parsed = (JsonString)JsonReader.Parse("\"" + ValidationIssueJsonWriter.JsonEscape(original) + "\"");

            Assert.Equal(original, parsed.Value);
        }

        // -----------------------------------------------------------------
        // ValidationIssueJsonWriter.AppendIssueJson
        // -----------------------------------------------------------------

        private static JsonObject Render(ValidationIssue issue)
        {
            var sb = new StringBuilder();
            ValidationIssueJsonWriter.AppendIssueJson(sb, issue);
            return (JsonObject)JsonReader.Parse(sb.ToString());
        }

        [Fact]
        public void AppendIssueJson_MinimalIssue_HasNullOptionalFields_AndNoAffectedNodeIdsKey()
        {
            var json = Render(new ValidationIssue(ValidationSeverity.Warning, "t.table", "some_check", "msg"));

            Assert.Equal("warning", ((JsonString)json["severity"]).Value);
            Assert.Equal("t.table", ((JsonString)json["table"]).Value);
            Assert.Equal("some_check", ((JsonString)json["check"]).Value);
            Assert.Equal("msg", ((JsonString)json["message"]).Value);
            foreach (var nullKey in new[] { "record_key", "field", "group", "note", "rule_id" })
            {
                Assert.IsType<JsonNull>(json[nullKey]);
            }

            Assert.False(json.ContainsKey("affected_node_ids"));
        }

        [Fact]
        public void AppendIssueJson_AllFieldsPresent_AreEmittedInDocumentedOrder_WithEscaping()
        {
            var issue = new ValidationIssue(
                ValidationSeverity.Error, "t.table", "chk", "line\nbreak \"quoted\"",
                "rec.key", "field_a", "grp", "note\ttab", "rule.x",
                new[] { "n1", "n\"2" });

            var json = Render(issue);

            Assert.Equal(
                new[]
                {
                    "severity", "table", "record_key", "field", "check", "message",
                    "group", "note", "rule_id", "affected_node_ids",
                },
                new List<string>(json.Keys));
            Assert.Equal("error", ((JsonString)json["severity"]).Value);
            Assert.Equal("rec.key", ((JsonString)json["record_key"]).Value);
            Assert.Equal("field_a", ((JsonString)json["field"]).Value);
            Assert.Equal("line\nbreak \"quoted\"", ((JsonString)json["message"]).Value);
            Assert.Equal("grp", ((JsonString)json["group"]).Value);
            Assert.Equal("note\ttab", ((JsonString)json["note"]).Value);
            Assert.Equal("rule.x", ((JsonString)json["rule_id"]).Value);
            var nodes = (JsonArray)json["affected_node_ids"];
            Assert.Equal(2, nodes.Count);
            Assert.Equal("n1", ((JsonString)nodes[0]).Value);
            Assert.Equal("n\"2", ((JsonString)nodes[1]).Value);
        }

        [Fact]
        public void AppendIssueJson_AppendsToExistingBuilder_WithoutClearingIt()
        {
            var sb = new StringBuilder("[");
            ValidationIssueJsonWriter.AppendIssueJson(sb, new ValidationIssue(ValidationSeverity.Error, "a", "c", "m"));
            sb.Append(',');
            ValidationIssueJsonWriter.AppendIssueJson(sb, new ValidationIssue(ValidationSeverity.Warning, "b", "c", "m"));
            sb.Append(']');

            var array = (JsonArray)JsonReader.Parse(sb.ToString());

            Assert.Equal(2, array.Count);
            Assert.Equal("a", ((JsonString)((JsonObject)array[0])["table"]).Value);
            Assert.Equal("b", ((JsonString)((JsonObject)array[1])["table"]).Value);
        }

        [Fact]
        public void AppendIssueJson_EmptyAffectedNodeIds_OmitsTheKey()
        {
            var issue = new ValidationIssue(
                ValidationSeverity.Error, "t", "c", "m", null, null, null, null, null, new string[0]);

            Assert.False(Render(issue).ContainsKey("affected_node_ids"));
        }

        // -----------------------------------------------------------------
        // ValidationIssue 构造守卫 / With*
        // -----------------------------------------------------------------

        [Fact]
        public void ValidationIssue_NullRequiredStrings_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new ValidationIssue(ValidationSeverity.Error, null!, "c", "m"));
            Assert.Throws<ArgumentNullException>(() => new ValidationIssue(ValidationSeverity.Error, "t", null!, "m"));
            Assert.Throws<ArgumentNullException>(() => new ValidationIssue(ValidationSeverity.Error, "t", "c", null!));
        }

        [Fact]
        public void ValidationIssue_WithRuleIdAndWithAffectedNodeIds_ReturnCopiesKeepingOtherFields()
        {
            var original = new ValidationIssue(ValidationSeverity.Error, "t", "c", "m", "rk", "f", "g", "n", null);

            var withRule = original.WithRuleId("rule.a");
            var withNodes = original.WithAffectedNodeIds(new[] { "x" });

            Assert.Null(original.RuleId);
            Assert.Equal("rule.a", withRule.RuleId);
            Assert.Equal("rk", withRule.RecordKey);
            Assert.Equal("n", withRule.Note);
            Assert.Empty(original.AffectedNodeIds);
            Assert.Equal(new[] { "x" }, withNodes.AffectedNodeIds);
            Assert.Throws<ArgumentException>(() => original.WithRuleId(""));
            Assert.Throws<ArgumentNullException>(() => original.WithAffectedNodeIds(null!));
        }

        // -----------------------------------------------------------------
        // DataSourceDiagnostic
        // -----------------------------------------------------------------

        [Fact]
        public void DataSourceDiagnostic_ExposesFields_AndToStringFormat()
        {
            var d = new DataSourceDiagnostic(2, "C:/data/x", "IOException", "boom");

            Assert.Equal(2, d.SourceIndex);
            Assert.Equal("C:/data/x", d.Identifier);
            Assert.Equal("IOException", d.ExceptionType);
            Assert.Equal("boom", d.ExceptionMessage);
            Assert.Equal("根2:C:/data/x（IOException：boom）", d.ToString());
        }

        [Fact]
        public void DataSourceDiagnostic_ToString_IsCultureInvariantForTheIndex()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
                Assert.Equal("根1234:i（T：m）", new DataSourceDiagnostic(1234, "i", "T", "m").ToString());
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void DataSourceDiagnostic_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new DataSourceDiagnostic(0, null!, "T", "m"));
            Assert.Throws<ArgumentNullException>(() => new DataSourceDiagnostic(0, "i", null!, "m"));
            Assert.Throws<ArgumentNullException>(() => new DataSourceDiagnostic(0, "i", "T", null!));
        }

        // -----------------------------------------------------------------
        // ContentGraphAnalysis.NodeIds 与构造守卫
        // -----------------------------------------------------------------

        [Fact]
        public void Analyze_NodeIds_PreserveInputOrder_AndIncludeEveryNode()
        {
            var nodeIds = new[] { "c", "a", "b" };
            var edges = new Dictionary<string, IReadOnlyList<string>>
            {
                ["c"] = new[] { "a" },
            };

            var analysis = ContentGraphAnalyzer.Analyze(nodeIds, edges);

            Assert.Equal(nodeIds, analysis.NodeIds);
            Assert.Equal(nodeIds.Length, analysis.InDegree.Count);
            Assert.Equal(nodeIds.Length, analysis.OutDegree.Count);
        }

        [Fact]
        public void Analyze_EmptyGraph_HasEmptyResultsEverywhere()
        {
            var analysis = ContentGraphAnalyzer.Analyze(new string[0], new Dictionary<string, IReadOnlyList<string>>());

            Assert.Empty(analysis.NodeIds);
            Assert.Empty(analysis.IsolatedNodeIds);
            Assert.Empty(analysis.ReachableNodeIds);
            Assert.Empty(analysis.UnreachableNodeIds);
            Assert.Empty(analysis.Cycles);
        }

        [Fact]
        public void Analyze_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ContentGraphAnalyzer.Analyze(null!, new Dictionary<string, IReadOnlyList<string>>()));
            Assert.Throws<ArgumentNullException>(() => ContentGraphAnalyzer.Analyze(new string[0], null!));
        }

        [Fact]
        public void ContentGraphAnalysis_Constructor_NullArguments_Throw()
        {
            var ids = new string[0];
            var deg = new Dictionary<string, int>();
            var cycles = new List<IReadOnlyList<string>>();

            Assert.Throws<ArgumentNullException>(() => new ContentGraphAnalysis(null!, deg, deg, ids, ids, ids, cycles));
            Assert.Throws<ArgumentNullException>(() => new ContentGraphAnalysis(ids, null!, deg, ids, ids, ids, cycles));
            Assert.Throws<ArgumentNullException>(() => new ContentGraphAnalysis(ids, deg, null!, ids, ids, ids, cycles));
            Assert.Throws<ArgumentNullException>(() => new ContentGraphAnalysis(ids, deg, deg, null!, ids, ids, cycles));
            Assert.Throws<ArgumentNullException>(() => new ContentGraphAnalysis(ids, deg, deg, ids, null!, ids, cycles));
            Assert.Throws<ArgumentNullException>(() => new ContentGraphAnalysis(ids, deg, deg, ids, ids, null!, cycles));
            Assert.Throws<ArgumentNullException>(() => new ContentGraphAnalysis(ids, deg, deg, ids, ids, ids, null!));
        }

        // -----------------------------------------------------------------
        // TolerantReadDiagnostics
        // -----------------------------------------------------------------

        [Fact]
        public void TolerantReadDiagnostics_None_IsNotDegradedAndHasNoMissingTables()
        {
            Assert.False(TolerantReadDiagnostics.None.IsDegraded);
            Assert.Empty(TolerantReadDiagnostics.None.MissingTables);
        }

        [Fact]
        public void TolerantReadDiagnostics_NullMissingTables_BecomesEmptyList()
        {
            var d = new TolerantReadDiagnostics(true, null!);

            Assert.True(d.IsDegraded);
            Assert.NotNull(d.MissingTables);
            Assert.Empty(d.MissingTables);
        }

        [Fact]
        public void TolerantReadDiagnostics_CarriesGivenValues()
        {
            var d = new TolerantReadDiagnostics(true, new[] { "a.b", "c.d" });

            Assert.True(d.IsDegraded);
            Assert.Equal(new[] { "a.b", "c.d" }, d.MissingTables);
        }

        [Fact]
        public void TolerantReadDiagnostics_DefaultStruct_HasNullMissingTables_ButNoneDoesNot()
        {
            // default(struct) 绕过构造函数，MissingTables 为 null；Tolerant 视图的 Diagnostics 属性
            // 总是经构造函数产出（不会是 default），消费方应取 None / 视图属性而不是 default。
            Assert.Null(default(TolerantReadDiagnostics).MissingTables);
            Assert.NotNull(TolerantReadDiagnostics.None.MissingTables);
        }
    }
}
