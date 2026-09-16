using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// 深度复审领域 E"测试覆盖缺口 5"补齐：<see cref="Core.Sim.AnchorTableSkillBudgetAnchorProvider"/>
    /// 的 <c>ResolveStandardPlayer</c> 在 <c>item.quality_definition</c> 多行 <c>sort_weight</c> 并列
    /// 时的选择顺序——复审报告称"当前实现依赖 <c>GetAll</c> 的登记顺序，属于'应已确定性但未显式锁定'
    /// 的边界，建议补一条测试但不认为是缺陷"。本文件只测这一条边界，不复用
    /// <c>AnchorProviderIntegrationTests</c>（那边走完整嵌入数据集 + <see cref="Core.Sim.ExpectedStatCalculator"/>
    /// 全链路），改用最小 <c>item.quality_definition</c> 单表桩数据直接驱动，经新增的只读属性
    /// <see cref="Core.Sim.AnchorTableSkillBudgetAnchorProvider.ResolvedStandardPlayer"/> 观察解析
    /// 结果（该属性本身也是本次深度复审新增，见其判断记录）。
    /// </summary>
    public sealed class AnchorTableSkillBudgetAnchorProviderTests
    {
        private static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
            });
            return new EventBus(catalog);
        }

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        /// <summary>最小 <c>item.quality_definition</c> 桩 schema：只登记 <c>id</c>/<c>sort_weight</c>，
        /// 够驱动 <c>ResolveStandardPlayer</c> 的回退分支即可，不需要生产 schema 的其余字段
        /// （<c>budget_multiplier</c>/<c>price_multiplier</c> 等——那些只有 <see
        /// cref="Core.Sim.ExpectedStatCalculator"/> 真正求值时才用到，本测试不触发求值）。</summary>
        private static TableSchema QualityStubTable() => new TableSchema(
            "item.quality_definition", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("sort_weight", FieldKind.Int, required: true),
            });

        private static DataRegistry BuildRegistryWithQualityRows(string qualityRowsJson)
        {
            var source = new InMemoryDataSource();
            source.Add("item.quality_definition", Envelope("item.quality_definition", qualityRowsJson));

            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions());
            registry.RegisterSchema(QualityStubTable());
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return registry;
        }

        /// <summary>正例对照：唯一最低 <c>sort_weight</c> 的一行被选中（不涉及并列，确认基本回退分支
        /// 本身工作正常，作为下面并列用例的对照基线）。</summary>
        [Fact]
        public void ResolveStandardPlayer_SingleLowestSortWeight_PicksThatRow()
        {
            var rows = "[" +
                "{\"id\": \"item.quality.mid\", \"sort_weight\": 5}, " +
                "{\"id\": \"item.quality.lowest\", \"sort_weight\": 1}, " +
                "{\"id\": \"item.quality.high\", \"sort_weight\": 9}" +
                "]";
            var registry = BuildRegistryWithQualityRows(rows);
            var provider = new Core.Sim.AnchorTableSkillBudgetAnchorProvider(registry, classId: new Id("arch.class.t"));

            var (_, qualityId) = provider.ResolvedStandardPlayer;

            Assert.Equal("item.quality.lowest", qualityId.Value);
        }

        /// <summary>核心用例：多行 <c>sort_weight</c> 恰好并列时，选中"先在 <c>GetAll</c> 枚举顺序里
        /// 出现的那一行"——不是任何形式的 id 字典序（故意把字典序更靠后的 <c>b_tied</c> 排在数据文件
        /// 第一条、字典序更靠前的 <c>a_tied</c> 排在第二条，若实现改成按 id 排序选择，本用例会失败，
        /// 与"依赖 GetAll 登记顺序"这一现状精确对应）。</summary>
        [Fact]
        public void ResolveStandardPlayer_TiedSortWeight_PicksFirstRegisteredRow_NotLexicographicallyFirst()
        {
            var rows = "[" +
                "{\"id\": \"item.quality.b_tied\", \"sort_weight\": 5}, " +
                "{\"id\": \"item.quality.a_tied\", \"sort_weight\": 5}, " +
                "{\"id\": \"item.quality.high\", \"sort_weight\": 9}" +
                "]";
            var registry = BuildRegistryWithQualityRows(rows);
            var provider = new Core.Sim.AnchorTableSkillBudgetAnchorProvider(registry, classId: new Id("arch.class.t"));

            var (_, qualityId) = provider.ResolvedStandardPlayer;

            Assert.Equal("item.quality.b_tied", qualityId.Value);
        }

        /// <summary>并列判定与登记顺序反过来时，选中结果也反过来——确认上一条用例锁定的确实是"登记
        /// 顺序"这个自变量本身，而不是巧合命中了某个字段的默认排序。</summary>
        [Fact]
        public void ResolveStandardPlayer_TiedSortWeight_OrderReversed_PicksNewFirstRow()
        {
            var rows = "[" +
                "{\"id\": \"item.quality.a_tied\", \"sort_weight\": 5}, " +
                "{\"id\": \"item.quality.b_tied\", \"sort_weight\": 5}, " +
                "{\"id\": \"item.quality.high\", \"sort_weight\": 9}" +
                "]";
            var registry = BuildRegistryWithQualityRows(rows);
            var provider = new Core.Sim.AnchorTableSkillBudgetAnchorProvider(registry, classId: new Id("arch.class.t"));

            var (_, qualityId) = provider.ResolvedStandardPlayer;

            Assert.Equal("item.quality.a_tied", qualityId.Value);
        }

        /// <summary>缓存判断记录复核：<see cref="Core.Sim.AnchorTableSkillBudgetAnchorProvider.ResolvedStandardPlayer"/>
        /// 多次访问返回同一结果（解析结果按类型判断记录"惰性持有 IDataRegistry 引用"缓存，不是每次
        /// 重新扫描）。</summary>
        [Fact]
        public void ResolvedStandardPlayer_AccessedTwice_ReturnsSameCachedValue()
        {
            var rows = "[{\"id\": \"item.quality.only\", \"sort_weight\": 3}]";
            var registry = BuildRegistryWithQualityRows(rows);
            var provider = new Core.Sim.AnchorTableSkillBudgetAnchorProvider(registry, classId: new Id("arch.class.t"));

            var first = provider.ResolvedStandardPlayer;
            var second = provider.ResolvedStandardPlayer;

            Assert.Equal(first, second);
            Assert.Equal("item.quality.only", first.QualityId.Value);
        }
    }
}
