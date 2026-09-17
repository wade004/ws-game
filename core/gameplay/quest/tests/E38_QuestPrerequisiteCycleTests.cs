using System.Linq;
using Core.Foundation.DataRegistry;
using Presentation.Assembly;
using Xunit;

namespace Tests.Gameplay.Quest
{
    /// <summary>
    /// 消费方反馈第 38 条验收（2026-09-13，见
    /// architecture/落地计划/消费方反馈-2026-09-13-编辑器-第38-39条.md）：
    /// <c>QuestContentValidationRule</c> 新增 <c>quest_prerequisite_cycle</c>（前置链成环，含自环）与
    /// <c>quest_prerequisite_unknown</c>（前置引用了不存在的任务）两项检查。全部用例经正式
    /// <see cref="ContentValidationAssembly.Run"/> 默认参数装配（真实装配路径，不是手工拼
    /// <c>DataRegistry</c>），复现步骤原文：两个任务互为前置，改造前 0 error（见
    /// <c>MutualPrerequisite_TwoNodeCycle_Blocks</c> 用例与本次任务复现记录）。
    /// </summary>
    public sealed class E38_QuestPrerequisiteCycleTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":[" + rowsJson + "]}";

        private static string TitleRow(string questShortId) =>
            "{\"key\":\"l10n.quest.e38_" + questShortId + ".title\",\"locale\":\"l10n.locale.zh_cn\",\"text\":\"" + questShortId + "\"}";

        private static string QuestRow(string shortId, string? prerequisite) =>
            "{\"id\":\"quest.e38_" + shortId + "\",\"title_key\":\"l10n.quest.e38_" + shortId + ".title\"," +
            "\"objectives\":[{\"type\":\"event\",\"target_ref\":\"event.e38\",\"count\":1}]," +
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

        // -----------------------------------------------------------------
        // 成环：两节点 / 三节点 / 自环
        // -----------------------------------------------------------------

        [Fact]
        public void MutualPrerequisite_TwoNodeCycle_Blocks()
        {
            var rows = string.Join(",",
                QuestRow("a", "quest.is_completed(quest.e38_b)"),
                QuestRow("b", "quest.is_completed(quest.e38_a)"));

            var run = ContentValidationAssembly.Run(new IDataSource[] { Source(rows, "a", "b") });

            Assert.True(run.Report.IsBlocking);
            var issue = Assert.Single(run.Report.Issues.Where(i => i.Check == "quest_prerequisite_cycle"));
            Assert.Equal("prerequisite", issue.Field);
            Assert.Contains("quest.e38_a", issue.Message);
            Assert.Contains("quest.e38_b", issue.Message);
            Assert.Contains("->", issue.Message);
            // 消费方反馈第 57 条：跨节点路径类诊断按环上出现顺序填入环上全部节点 id。
            Assert.NotEmpty(issue.AffectedNodeIds);
            Assert.All(issue.AffectedNodeIds, id => Assert.Contains(id, new[] { "quest.e38_a", "quest.e38_b" }));
        }

        [Fact]
        public void ThreeNodeCycle_Blocks()
        {
            var rows = string.Join(",",
                QuestRow("a", "quest.is_completed(quest.e38_b)"),
                QuestRow("b", "quest.is_completed(quest.e38_c)"),
                QuestRow("c", "quest.is_completed(quest.e38_a)"));

            var run = ContentValidationAssembly.Run(new IDataSource[] { Source(rows, "a", "b", "c") });

            Assert.True(run.Report.IsBlocking);
            var issue = Assert.Single(run.Report.Issues.Where(i => i.Check == "quest_prerequisite_cycle"));
            Assert.Equal("prerequisite", issue.Field);
            Assert.Contains("quest.e38_a", issue.Message);
            Assert.Contains("quest.e38_b", issue.Message);
            Assert.Contains("quest.e38_c", issue.Message);
        }

        [Fact]
        public void SelfReference_CountsAsCycle()
        {
            var rows = QuestRow("a", "quest.is_completed(quest.e38_a)");

            var run = ContentValidationAssembly.Run(new IDataSource[] { Source(rows, "a") });

            Assert.True(run.Report.IsBlocking);
            var issue = Assert.Single(run.Report.Issues.Where(i => i.Check == "quest_prerequisite_cycle"));
            Assert.Equal("quest.e38_a", issue.RecordKey);
            Assert.Equal("prerequisite", issue.Field);
            Assert.Equal(new[] { "quest.e38_a", "quest.e38_a" }, issue.AffectedNodeIds);
        }

        // -----------------------------------------------------------------
        // 无环菱形 DAG：应通过
        // -----------------------------------------------------------------

        [Fact]
        public void DiamondShapedDag_NoCycle_Passes()
        {
            // a 前置 b 与 c；b、c 都前置 d；d 无前置。菱形共享节点，不成环。
            var rows = string.Join(",",
                QuestRow("a", "quest.is_completed(quest.e38_b) and quest.is_completed(quest.e38_c)"),
                QuestRow("b", "quest.is_completed(quest.e38_d)"),
                QuestRow("c", "quest.is_completed(quest.e38_d)"),
                QuestRow("d", null));

            var run = ContentValidationAssembly.Run(new IDataSource[] { Source(rows, "a", "b", "c", "d") });

            Assert.False(run.Report.IsBlocking, string.Join("; ", run.Report.Issues));
            Assert.DoesNotContain(run.Report.Issues, i => i.Check == "quest_prerequisite_cycle");
            Assert.DoesNotContain(run.Report.Issues, i => i.Check == "quest_prerequisite_unknown");
        }

        // -----------------------------------------------------------------
        // 未知任务引用
        // -----------------------------------------------------------------

        [Fact]
        public void UnknownPrerequisiteQuest_Blocks()
        {
            var rows = QuestRow("a", "quest.is_completed(quest.e38_missing)");

            var run = ContentValidationAssembly.Run(new IDataSource[] { Source(rows, "a") });

            Assert.True(run.Report.IsBlocking);
            var issue = Assert.Single(run.Report.Issues.Where(i => i.Check == "quest_prerequisite_unknown"));
            Assert.Equal("quest.e38_a", issue.RecordKey);
            Assert.Equal("prerequisite", issue.Field);
            Assert.Contains("quest.e38_missing", issue.Message);
            // 消费方反馈第 57 条：单节点类诊断填该节点（本任务）自身 id。
            Assert.Equal(new[] { "quest.e38_a" }, issue.AffectedNodeIds);
        }

        // -----------------------------------------------------------------
        // 位置信息：ExprNode.Start/Length 附带在 message 里
        // -----------------------------------------------------------------

        [Fact]
        public void UnknownPrerequisiteQuest_MessageCarriesSourcePosition()
        {
            var rows = QuestRow("a", "quest.is_completed(quest.e38_missing)");

            var run = ContentValidationAssembly.Run(new IDataSource[] { Source(rows, "a") });

            var issue = Assert.Single(run.Report.Issues.Where(i => i.Check == "quest_prerequisite_unknown"));
            // "quest.is_completed(quest.e38_missing)"：quest.e38_missing 字面量从下标 19 开始，长度 17。
            Assert.Contains("（位置 19，长度 17）", issue.Message);
        }

        // -----------------------------------------------------------------
        // 既有 quest 校验测试不变：普通 quest（无 prerequisite）仍不受影响
        // -----------------------------------------------------------------

        [Fact]
        public void NoPrerequisiteField_NoCycleOrUnknownIssues()
        {
            var rows = QuestRow("a", null);

            var run = ContentValidationAssembly.Run(new IDataSource[] { Source(rows, "a") });

            Assert.False(run.Report.IsBlocking, string.Join("; ", run.Report.Issues));
            Assert.DoesNotContain(run.Report.Issues, i =>
                i.Check == "quest_prerequisite_cycle" || i.Check == "quest_prerequisite_unknown");
        }
    }
}
