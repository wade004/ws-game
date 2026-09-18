using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Numbers.Archetype
{
    /// <summary>
    /// 消费方反馈第 56 条追问（2026-09-18）验收：<c>TalentTreeIsolationRule</c>
    /// （检查名 <c>talent_node_isolated</c>）触发/不触发两类行为，数据构造手法同
    /// <c>ArchTalentTreeCycleValidationRuleTests</c>（手工拼一份只登记 <c>arch.talent_tree</c> 的
    /// <see cref="DataRegistry"/>，本规则不需要任何跨表依赖）。规则本身默认是否注册（"开关关闭时不
    /// 运行"）属于 <c>Presentation.Assembly.ContentValidationOptions.EnableGraphIsolationDiagnostics</c>
    /// 的装配层职责，不是本规则自身能表达的行为——覆盖在
    /// <c>presentation/assembly/tests/ContentValidationAssemblyTests.cs</c>
    /// （<c>Run_EnabledDisabledOptionalRuleChecks_CorrespondToRuleNameLists</c>/
    /// <c>Run_EnableGraphIsolationDiagnostics_TwoIsolationRulesBecomeEnabled</c>），本文件只测规则
    /// 本身注册后的判定逻辑是否正确。
    /// </summary>
    public sealed class TalentTreeIsolationRuleTests
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
            registry.RegisterValidationRule(new Core.Numbers.Archetype.TalentTreeIsolationRule());
            return registry.LoadAll();
        }

        [Fact]
        public void IsolatedNodeExists_ReportsWarningWithIndexedFieldAndAffectedNodeIds()
        {
            // n3 既无前置、也未被 n1/n2 引用——孤立；n2 引用 n1 为前置，n1/n2 都不孤立。
            var rows = "[{\"id\":\"arch.talent_tree.iso_a\",\"nodes\":[" +
                "{\"id\":\"n1\",\"cost\":1}," +
                "{\"id\":\"n2\",\"prerequisites\":[\"n1\"],\"cost\":1}," +
                "{\"id\":\"n3\",\"cost\":1}" +
                "]}]";

            var report = Load(rows);

            var issue = Assert.Single(report.Issues, i => i.Check == "talent_node_isolated");
            Assert.Equal(ValidationSeverity.Warning, issue.Severity);
            Assert.Equal("nodes[2]", issue.Field);
            Assert.Equal(new[] { "n3" }, issue.AffectedNodeIds);
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void AllNodesConnected_NoIsolatedIssue()
        {
            var rows = "[{\"id\":\"arch.talent_tree.iso_b\",\"nodes\":[" +
                "{\"id\":\"n1\",\"cost\":1}," +
                "{\"id\":\"n2\",\"prerequisites\":[\"n1\"],\"cost\":1}" +
                "]}]";

            var report = Load(rows);

            Assert.DoesNotContain(report.Issues, i => i.Check == "talent_node_isolated");
        }

        [Fact]
        public void SingleNodeTreeOnly_NoIsolatedIssue()
        {
            // 见 TalentTreeIsolationRule 类型判断记录："树内节点数 ≥2 时才报"。
            var rows = "[{\"id\":\"arch.talent_tree.iso_c\",\"nodes\":[{\"id\":\"n1\",\"cost\":1}]}]";

            var report = Load(rows);

            Assert.DoesNotContain(report.Issues, i => i.Check == "talent_node_isolated");
        }

        [Fact]
        public void MultipleTrees_EachJudgedIndependently()
        {
            // 第一棵树只有一个节点（不报），第二棵树两个节点均孤立（各自都报）。
            var rows = "[" +
                "{\"id\":\"arch.talent_tree.iso_d1\",\"nodes\":[{\"id\":\"n1\",\"cost\":1}]}," +
                "{\"id\":\"arch.talent_tree.iso_d2\",\"nodes\":[{\"id\":\"n1\",\"cost\":1},{\"id\":\"n2\",\"cost\":1}]}" +
                "]";

            var report = Load(rows);

            var issues = System.Linq.Enumerable.Where(report.Issues, i => i.Check == "talent_node_isolated");
            Assert.Equal(2, System.Linq.Enumerable.Count(issues));
            Assert.Contains(issues, i => i.RecordKey == "arch.talent_tree.iso_d2" && i.AffectedNodeIds[0] == "n1");
            Assert.Contains(issues, i => i.RecordKey == "arch.talent_tree.iso_d2" && i.AffectedNodeIds[0] == "n2");
        }
    }
}
