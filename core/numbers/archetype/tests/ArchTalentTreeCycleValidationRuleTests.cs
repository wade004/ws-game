using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Numbers.Archetype
{
    /// <summary>
    /// 消费方反馈第 57 条（2026-09-18，见
    /// architecture/落地计划/消费方反馈-2026-09-18-编辑器-第54-58条.md）验收：
    /// <c>ArchTalentTreeCycleValidationRule</c> 三项图诊断（<c>talent_node_id</c>/
    /// <c>talent_prerequisite_missing</c>/<c>talent_prerequisite_cycle</c>）的结构化定位——前两项
    /// <c>Field</c> 补具体下标路径，后者按环上出现顺序填入
    /// <see cref="ValidationIssue.AffectedNodeIds"/>。
    /// </summary>
    public sealed class ArchTalentTreeCycleValidationRuleTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        private static ValidationReport Load(string rowsJson)
        {
            var source = new InMemoryDataSource().Add(
                Core.Numbers.Archetype.ArchSchemas.TalentTree.Name,
                Envelope(Core.Numbers.Archetype.ArchSchemas.TalentTree.Name, rowsJson));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.Archetype.ArchSchemas.TalentTree);
            registry.RegisterValidationRule(new Core.Numbers.Archetype.ArchTalentTreeCycleValidationRule());
            return registry.LoadAll();
        }

        [Fact]
        public void MissingNodeId_ReportsIndexedFieldAndNoAffectedNodeIds()
        {
            var rows = "[{\"id\":\"arch.talent_tree.cov_bad_id\",\"nodes\":[" +
                "{\"cost\":1},{\"id\":\"n2\",\"cost\":2}]}]";

            var report = Load(rows);

            var issue = Assert.Single(report.Issues, i => i.Check == "talent_node_id");
            Assert.Equal("nodes[0].id", issue.Field);
            // 见判断记录：id 本身缺失，没有合法节点 id 可填 AffectedNodeIds（默认空集合）。
            Assert.Empty(issue.AffectedNodeIds);
        }

        [Fact]
        public void MissingPrerequisite_ReportsIndexedFieldAndAffectedNodeIds()
        {
            var rows = "[{\"id\":\"arch.talent_tree.cov_missing_pre\",\"nodes\":[" +
                "{\"id\":\"n1\",\"prerequisites\":[\"n_missing\"],\"cost\":1}]}]";

            var report = Load(rows);

            var issue = Assert.Single(report.Issues, i => i.Check == "talent_prerequisite_missing");
            Assert.Equal("nodes[0].prerequisites[0]", issue.Field);
            Assert.Equal(new[] { "n1" }, issue.AffectedNodeIds);
        }

        [Fact]
        public void MissingPrerequisite_NonFirstJsonIndex_FieldUsesActualIndex()
        {
            // prerequisites 数组里第一个元素不是字符串（被 field_type 单独报告），真正的缺失前置在
            // json 下标 1——field 路径应反映真实的 JSON 数组下标，不是"有效项计数"下标。
            var rows = "[{\"id\":\"arch.talent_tree.cov_mixed\",\"nodes\":[" +
                "{\"id\":\"n1\",\"prerequisites\":[123,\"n_missing\"],\"cost\":1}]}]";

            var report = Load(rows);

            var issue = Assert.Single(report.Issues, i => i.Check == "talent_prerequisite_missing");
            Assert.Equal("nodes[0].prerequisites[1]", issue.Field);
        }

        [Fact]
        public void Cycle_TwoNodeLoop_AffectedNodeIdsListsFullCyclePath()
        {
            var rows = "[{\"id\":\"arch.talent_tree.cov_cycle\",\"nodes\":[" +
                "{\"id\":\"n1\",\"prerequisites\":[\"n2\"],\"cost\":1}," +
                "{\"id\":\"n2\",\"prerequisites\":[\"n1\"],\"cost\":1}" +
                "]}]";

            var report = Load(rows);

            var issue = Assert.Single(report.Issues, i => i.Check == "talent_prerequisite_cycle");
            Assert.Equal(new[] { "n1", "n2", "n1" }, issue.AffectedNodeIds);
            Assert.Contains("->", issue.Message);
        }

        [Fact]
        public void SelfPrerequisite_CountsAsCycle()
        {
            var rows = "[{\"id\":\"arch.talent_tree.cov_self\",\"nodes\":[" +
                "{\"id\":\"n1\",\"prerequisites\":[\"n1\"],\"cost\":1}]}]";

            var report = Load(rows);

            var issue = Assert.Single(report.Issues, i => i.Check == "talent_prerequisite_cycle");
            Assert.Equal(new[] { "n1", "n1" }, issue.AffectedNodeIds);
        }

        [Fact]
        public void IsolatedNode_NoPrerequisiteNoReference_DoesNotReportAnyIssue()
        {
            // 消费方反馈第 56 条：孤立节点（无前置、未被引用）是天赋树合法的并列分支设计，不报警告。
            var rows = "[{\"id\":\"arch.talent_tree.cov_isolated\",\"nodes\":[" +
                "{\"id\":\"n1\",\"cost\":1},{\"id\":\"n2\",\"cost\":1}]}]";

            var report = Load(rows);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.Empty(report.Issues);
        }
    }
}
