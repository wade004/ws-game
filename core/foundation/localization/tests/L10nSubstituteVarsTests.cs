using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Localization;
using Xunit;

namespace Tests.Foundation.Localization
{
    /// <summary>
    /// <see cref="L10nHost"/> 变量代入（<c>SubstituteVars</c>）的边界用例（T-M2，2026-10-01 测试覆盖第四批）：
    /// 未闭合 / 嵌套 / 空名 / 同名多次 / 值内含占位符不二次代入 / <c>WarnOnMissingVar=false</c> /
    /// 缺失键策略与变量代入的组合。期望值由"扫描 <c>{name}</c>，遇未闭合或嵌套的 <c>{</c> 原样保留该字符"
    /// 这条规则手推。
    /// </summary>
    public sealed class L10nSubstituteVarsTests
    {
        private static readonly Id Key = new Id("l10n.test.line");

        private static (L10nHost host, InMemoryL10nDiagnostics diagnostics) Build(
            string text, L10nOptions? options = null)
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
                new EventDefinition(L10nEventKeys.LanguageChanged, "l10n", new[] { "locale" }),
            });
            var bus = new EventBus(catalog);

            var escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var source = new InMemoryDataSource()
                .Add("l10n.locale",
                    "{\"table\": \"l10n.locale\", \"schema_version\": 1, \"rows\": " +
                    "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}]}")
                .Add("l10n.text",
                    "{\"table\": \"l10n.text\", \"schema_version\": 1, \"rows\": " +
                    "[{\"key\": \"" + Key.Value + "\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"" + escaped + "\"}]}");
            var registry = new DataRegistry(source, bus);
            registry.RegisterSchema(L10nSchemas.Locale);
            registry.RegisterSchema(L10nSchemas.Text);
            var report = registry.LoadAll();
            Assert.Equal(0, report.ErrorCount);

            var diagnostics = new InMemoryL10nDiagnostics();
            return (new L10nHost(registry, bus, options, diagnostics), diagnostics);
        }

        private static Dictionary<string, string> Vars(params (string name, string value)[] pairs)
        {
            var d = new Dictionary<string, string>();
            foreach (var (name, value) in pairs)
            {
                d[name] = value;
            }

            return d;
        }

        [Fact]
        public void TextWithoutBraces_IsReturnedUntouched_EvenWithVars()
        {
            var (host, diagnostics) = Build("plain text");

            Assert.Equal("plain text", host.Text(Key, Vars(("a", "x"))));
            Assert.Empty(diagnostics.Warnings);
        }

        [Fact]
        public void UnclosedBrace_IsKeptLiterally_WithoutWarning()
        {
            var (host, diagnostics) = Build("hp {value");

            Assert.Equal("hp {value", host.Text(Key, Vars(("value", "9"))));
            Assert.Empty(diagnostics.Warnings);
        }

        [Fact]
        public void LoneClosingBrace_IsKeptLiterally()
        {
            var (host, diagnostics) = Build("a } b {v} c }");

            Assert.Equal("a } b 7 c }", host.Text(Key, Vars(("v", "7"))));
            Assert.Empty(diagnostics.Warnings);
        }

        [Fact]
        public void NestedOpenBrace_KeepsOuterBraceLiteral_AndSubstitutesInnerPlaceholder()
        {
            // "{a{b}"：位置 0 的 '{' 之后先遇到另一个 '{'（早于 '}'），按规则原样保留该字符；
            // 随后 "{b}" 是一个完整占位符。
            var (host, _) = Build("{a{b}");

            Assert.Equal("{a" + "B", host.Text(Key, Vars(("a", "A"), ("b", "B"))));
        }

        [Fact]
        public void DoubleBraces_OuterKeptLiteral_InnerSubstituted()
        {
            var (host, _) = Build("{{a}}");

            Assert.Equal("{" + "A" + "}", host.Text(Key, Vars(("a", "A"))));
        }

        [Fact]
        public void EmptyName_NotInVars_KeepsBracesAndWarnsWithEmptyName()
        {
            var (host, diagnostics) = Build("x{}y");

            Assert.Equal("x{}y", host.Text(Key, Vars(("a", "A"))));
            Assert.Single(diagnostics.Warnings);
            Assert.Contains("\"\"", diagnostics.Warnings[0]);
        }

        [Fact]
        public void EmptyName_PresentInVars_IsSubstituted()
        {
            var (host, diagnostics) = Build("x{}y");

            Assert.Equal("xZy", host.Text(Key, Vars(("", "Z"))));
            Assert.Empty(diagnostics.Warnings);
        }

        [Fact]
        public void SameNameMultipleTimes_AllOccurrencesSubstituted()
        {
            var (host, diagnostics) = Build("{n}+{n}={n}{n}");

            Assert.Equal("3+3=33", host.Text(Key, Vars(("n", "3"))));
            Assert.Empty(diagnostics.Warnings);
        }

        [Fact]
        public void SameMissingNameMultipleTimes_WarnsOncePerOccurrence()
        {
            var (host, diagnostics) = Build("{n} and {n}");

            Assert.Equal("{n} and {n}", host.Text(Key, null));
            Assert.Equal(2, diagnostics.Warnings.Count);
            Assert.All(diagnostics.Warnings, w => Assert.Contains("\"n\"", w));
        }

        [Fact]
        public void ValueContainingPlaceholder_IsNotSubstitutedAgain()
        {
            var (host, diagnostics) = Build("{a}/{b}");

            // a 的值本身长得像占位符 {b}：不得被二次代入；b 仍按自己的值代入。
            Assert.Equal("{b}/B", host.Text(Key, Vars(("a", "{b}"), ("b", "B"))));
            Assert.Empty(diagnostics.Warnings);
        }

        [Fact]
        public void EmptyValue_SubstitutesEmptyString()
        {
            var (host, _) = Build("[{v}]");

            Assert.Equal("[]", host.Text(Key, Vars(("v", ""))));
        }

        [Fact]
        public void VariableNames_AreCaseSensitive()
        {
            var (host, diagnostics) = Build("{Name}");

            Assert.Equal("{Name}", host.Text(Key, Vars(("name", "x"))));
            Assert.Single(diagnostics.Warnings);
        }

        [Fact]
        public void WarnOnMissingVarFalse_KeepsPlaceholder_WithoutAnyWarning()
        {
            var (host, diagnostics) = Build("{a} {b}", new L10nOptions { WarnOnMissingVar = false });

            Assert.Equal("1 {b}", host.Text(Key, Vars(("a", "1"))));
            Assert.Empty(diagnostics.Warnings);
        }

        [Fact]
        public void WarnOnMissingVarTrue_IsTheDefault()
        {
            Assert.True(new L10nOptions().WarnOnMissingVar);
        }

        [Fact]
        public void ExtraVarsNotReferencedByText_AreIgnoredSilently()
        {
            var (host, diagnostics) = Build("{a}");

            Assert.Equal("1", host.Text(Key, Vars(("a", "1"), ("zzz", "unused"))));
            Assert.Empty(diagnostics.Warnings);
        }

        [Theory]
        [InlineData(MissingKeyPolicy.ReturnKey, "l10n.test.ghost")]
        [InlineData(MissingKeyPolicy.ReturnEmpty, "")]
        [InlineData(MissingKeyPolicy.ReturnMarker, "[[l10n.test.ghost]]")]
        public void MissingKey_WithVars_ReturnsPolicyResult_NoSubstitution_OnlyMissingKeyWarning(
            MissingKeyPolicy policy, string expected)
        {
            var (host, diagnostics) = Build("{a}", new L10nOptions { MissingKeyPolicy = policy });
            var ghost = new Id("l10n.test.ghost");

            Assert.Equal(expected, host.Text(ghost, Vars(("a", "1"))));

            Assert.Single(diagnostics.Warnings);
            Assert.Contains("l10n.test.ghost", diagnostics.Warnings[0]);
        }

        [Fact]
        public void MissingKeyPolicy_DoesNotAffectPresentKeyWithMissingVar()
        {
            var (host, diagnostics) = Build("{a}", new L10nOptions { MissingKeyPolicy = MissingKeyPolicy.ReturnEmpty });

            // 键存在、变量缺失：仍保留占位符（不是按"缺失键策略"返回空串）。
            Assert.Equal("{a}", host.Text(Key, null));
            Assert.Single(diagnostics.Warnings);
        }

        [Fact]
        public void UnicodeValue_AndTextAroundPlaceholder_ArePreserved()
        {
            var (host, _) = Build("对{who}造成{n}点伤害");

            Assert.Equal("对哥布林造成12点伤害", host.Text(Key, Vars(("who", "哥布林"), ("n", "12"))));
        }
    }
}
