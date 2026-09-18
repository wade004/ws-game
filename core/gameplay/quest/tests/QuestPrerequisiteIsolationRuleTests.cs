using System.Linq;
using Core.Foundation.DataRegistry;
using Presentation.Assembly;
using Xunit;

namespace Tests.Gameplay.Quest
{
    /// <summary>
    /// 消费方反馈第 56 条追问（2026-09-18）验收：<c>QuestPrerequisiteIsolationRule</c>
    /// （检查名 <c>quest_prerequisite_node_isolated</c>）——默认关闭的可选规则，只在
    /// <see cref="ContentValidationOptions.EnableGraphIsolationDiagnostics"/> 显式打开时注册。全部
    /// 用例经正式 <see cref="ContentValidationAssembly.Run"/> 装配（同 <c>E38_QuestPrerequisiteCycleTests</c>
    /// 手法），不手工拼一个只登记 <c>quest.def</c> 的裁剪版 <see cref="DataRegistry"/>。
    /// </summary>
    public sealed class QuestPrerequisiteIsolationRuleTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":[" + rowsJson + "]}";

        private static string TitleRow(string questShortId) =>
            "{\"key\":\"l10n.quest.iso_" + questShortId + ".title\",\"locale\":\"l10n.locale.zh_cn\",\"text\":\"" + questShortId + "\"}";

        private static string QuestRow(string shortId, string? prerequisite) =>
            "{\"id\":\"quest.iso_" + shortId + "\",\"title_key\":\"l10n.quest.iso_" + shortId + ".title\"," +
            "\"objectives\":[{\"type\":\"event\",\"target_ref\":\"event.iso\",\"count\":1}]," +
            (prerequisite == null ? "" : "\"prerequisite\":\"" + prerequisite.Replace("\"", "\\\"") + "\",") +
            "\"start_method\":\"auto\",\"turn_in_method\":\"auto\",\"repeatable\":\"none\"}";

        private static InMemoryDataSource Source(string questRowsJson, params string[] titleRowShortIds)
        {
            var source = new InMemoryDataSource()
                .Add("l10n.locale", Envelope("l10n.locale", "{\"id\":\"l10n.locale.zh_cn\",\"is_default\":true}"))
                .Add("l10n.text", Envelope("l10n.text", string.Join(",", titleRowShortIds.Select(TitleRow))))
                .Add("quest.def", Envelope("quest.def", questRowsJson));
            return source;
        }

        [Fact]
        public void EnabledAndIsolatedQuestExists_ReportsIsolatedWarning()
        {
            // c 既无前置、也未被 a/b 引用——孤立；a -> b 之间互相有引用关系（b 引用 a），a/b 都不孤立。
            var rows = string.Join(",",
                QuestRow("a", null),
                QuestRow("b", "quest.is_completed(quest.iso_a)"),
                QuestRow("c", null));

            var run = ContentValidationAssembly.Run(
                new IDataSource[] { Source(rows, "a", "b", "c") },
                new ContentValidationOptions { EnableGraphIsolationDiagnostics = true });

            var issue = Assert.Single(run.Report.Issues, i => i.Check == "quest_prerequisite_node_isolated");
            Assert.Equal(ValidationSeverity.Warning, issue.Severity);
            Assert.Equal("quest.iso_c", issue.RecordKey);
            Assert.Equal(new[] { "quest.iso_c" }, issue.AffectedNodeIds);
            // 展示性提示，NonEscalatable=true，不应参与阻断判定。
            Assert.False(run.Report.IsBlocking, string.Join("; ", run.Report.Issues.Where(i => i.Severity == ValidationSeverity.Error)));
        }

        [Fact]
        public void EnabledAndAllNodesConnected_NoIsolatedIssue()
        {
            var rows = string.Join(",",
                QuestRow("a", null),
                QuestRow("b", "quest.is_completed(quest.iso_a)"));

            var run = ContentValidationAssembly.Run(
                new IDataSource[] { Source(rows, "a", "b") },
                new ContentValidationOptions { EnableGraphIsolationDiagnostics = true });

            Assert.DoesNotContain(run.Report.Issues, i => i.Check == "quest_prerequisite_node_isolated");
        }

        [Fact]
        public void EnabledAndSingleQuestOnly_NoIsolatedIssue()
        {
            // 见 QuestPrerequisiteIsolationRule 类型判断记录："仅当图中任务总数 ≥2 时报"。
            var rows = QuestRow("solo", null);

            var run = ContentValidationAssembly.Run(
                new IDataSource[] { Source(rows, "solo") },
                new ContentValidationOptions { EnableGraphIsolationDiagnostics = true });

            Assert.DoesNotContain(run.Report.Issues, i => i.Check == "quest_prerequisite_node_isolated");
        }

        [Fact]
        public void DisabledByDefault_IsolatedQuestExists_RuleDoesNotRun()
        {
            var rows = string.Join(",",
                QuestRow("a", null),
                QuestRow("b", "quest.is_completed(quest.iso_a)"),
                QuestRow("c", null));

            // 默认 ContentValidationOptions.EnableGraphIsolationDiagnostics=false，规则不注册。
            var run = ContentValidationAssembly.Run(new IDataSource[] { Source(rows, "a", "b", "c") });

            Assert.DoesNotContain(run.Report.Issues, i => i.Check == "quest_prerequisite_node_isolated");
            Assert.Contains("QuestPrerequisiteIsolationRule", run.DisabledOptionalRules);
        }
    }
}
