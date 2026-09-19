using System;
using System.Collections.Generic;
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

        // -----------------------------------------------------------------
        // 框架调用外部实现不做隔离系列第四条（2026-09-19）：
        // AnchorTableSkillBudgetAnchorProvider.DataSourcesHaveAnchorRows 预扫描阶段的
        // IDataSource.ListTables() 调用此前无 try/catch，某个数据源枚举抛出未预期异常会先于
        // DataRegistry.LoadAllCore 自身（同系列第三条已隔离）崩溃，见 AnchorTableSkillBudgetAnchorProvider.cs
        // 该方法判断记录、core/foundation/data_registry/README.md"数据源枚举执行期异常隔离"一节
        // "顺带发现"条目。
        // -----------------------------------------------------------------

        /// <summary>只在 <see cref="ListTables"/> 被调用时抛出的 <see cref="IDataSource"/> 测试替身。</summary>
        private sealed class ThrowingListTablesSource : IDataSource
        {
            private readonly Exception _exception;

            public ThrowingListTablesSource(Exception exception)
            {
                _exception = exception;
            }

            public IReadOnlyList<DataTableSource> ListTables() => throw _exception;
        }

        /// <summary>核心回归用例：某个数据源枚举抛出未预期异常时，<c>DataSourcesHaveAnchorRows</c>
        /// 不应崩溃（不外抛异常），也不应被误当作"没有锚点行"而返回 <c>false</c>——见该方法判断记录
        /// "拍板"一节：枚举失败时无法确定该源是否含 <c>sim.anchor</c>，必须保守返回 <c>true</c>，
        /// 不能悄悄给出一个可能错误的"无锚点行"结论（AGENTS.md 第 3 节"不静默降级"）。</summary>
        [Fact]
        public void DataSourcesHaveAnchorRows_SourceListTablesThrowsUnexpectedException_DoesNotThrow_ReturnsTrueNotFalse()
        {
            var bad = new ThrowingListTablesSource(new InvalidOperationException("模拟数据源枚举内部未预期异常"));

            var result = Core.Sim.AnchorTableSkillBudgetAnchorProvider.DataSourcesHaveAnchorRows(new IDataSource[] { bad });

            Assert.True(result);
        }

        /// <summary>互补场景：抛异常的数据源排在一个正常（但确实不含 <c>sim.anchor</c>）的数据源之后——
        /// 确认无论顺序如何，只要扫描过程中遇到任何一个枚举失败的数据源，整体结果都保守收敛为
        /// <c>true</c>，不会因为"先看到的几个源都没有锚点行"而在遇到失败源之前就已经形成
        /// "false"的错误路径依赖。</summary>
        [Fact]
        public void DataSourcesHaveAnchorRows_GoodSourceWithoutAnchorThenThrowingSource_DoesNotThrow_ReturnsTrue()
        {
            var goodWithoutAnchor = new InMemoryDataSource()
                .Add("test.owner", Envelope("test.owner", "[{\"id\": \"test.owner.a\"}]"));
            var bad = new ThrowingListTablesSource(new InvalidOperationException("模拟数据源枚举内部未预期异常"));

            var result = Core.Sim.AnchorTableSkillBudgetAnchorProvider.DataSourcesHaveAnchorRows(
                new IDataSource[] { goodWithoutAnchor, bad });

            Assert.True(result);
        }

        /// <summary>反向确认失败信息确实"可达调用方/报告"：预扫描本身没有报告通道
        /// （见该方法判断记录"难点"），依赖紧随其后、使用同一份 <c>sources</c> 的
        /// <see cref="Core.Sim.HeadlessWorldBuilder.Build"/>（内部 <c>DataRegistry.LoadAll</c>）
        /// 独立重新枚举同一个失败源、产出 <c>data_source_unavailable</c>——本用例证明这条链路完整
        /// 闭合：整个装配过程不以 <c>Unhandled exception</c>/0xE0434352 崩溃，而是抛出携带完整
        /// 问题描述的 <see cref="InvalidOperationException"/>，调用方能读到失败原因。</summary>
        [Fact]
        public void HeadlessWorldBuilderBuild_OneExtraSourceListTablesThrows_DoesNotCrash_ThrowsWithDataSourceUnavailableDetail()
        {
            // bad 故意排在列表最前面：embeddedSource（列表后段）其实真的含 sim.anchor 行，若 bad
            // 排在后面，"预扫描找到第一个真实含行的源就提前返回 true"这一既有短路逻辑会让预扫描根本
            // 不会走到 bad 这一步，无法验证"预扫描自身的 try/catch"是否生效（只会验证到
            // DataRegistry.LoadAll 内部早已存在、与本次改动无关的既有隔离）——反向确认已用这个顺序
            // 验证过，见判断记录"反向确认"。
            var bad = new ThrowingListTablesSource(new InvalidOperationException("模拟数据源枚举内部未预期异常——回归测试专用"));
            var dataSources = new List<IDataSource> { bad };
            dataSources.AddRange(SimTestWorldFactory.BuildEmbeddedDataSources());

            var ex = Assert.Throws<InvalidOperationException>(() => Core.Sim.HeadlessWorldBuilder.Build(new Core.Sim.HeadlessWorldOptions
            {
                DataSources = dataSources,
                PlayerClassId = SimTestWorldFactory.EmbeddedClassId,
                FailOnUnknownTable = false,
            }));

            Assert.Contains("data_source_unavailable", ex.Message);
            Assert.Contains("模拟数据源枚举内部未预期异常——回归测试专用", ex.Message);
        }
    }
}
