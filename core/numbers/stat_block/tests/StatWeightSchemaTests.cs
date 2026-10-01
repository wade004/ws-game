using System;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Numbers.StatBlock
{
    /// <summary>
    /// 分阶段落地计划 T-N1-5（ADR-0030 决策 7；04 第 1.1 节表清单 <c>stat.weight</c> 行）：
    /// <see cref="StatSchemas.Weight"/> 的 schema 覆盖测试（合法记录、缺必填、引用不存在、
    /// <c>class_overrides</c> 子结构坏形状，惯例同 <c>StatSchemaCoverageTests.cs</c>）与
    /// "<see cref="StatHost"/> 不读该表"防御测试（两条行为用例：取值不受影响 + 经读取记录装饰器断言
    /// 全程未读该表，见类型顶部禁止事项"禁止 StatHost 消费权重表"；2026-10-01 起不再做源码文本扫描）。
    /// </summary>
    public sealed class StatWeightSchemaTests
    {
        private static string EnvelopeV2(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":2,\"rows\":" + rowsJson + "}";

        private static string Envelope1(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        /// <summary>装配一个只登记 <c>stat.definition</c>/<c>stat.weight</c> 两张真 schema 的最小
        /// 注册表；<c>arch.class</c> 按 <c>core/numbers/tests/L1SampleDataTests.cs
        /// BuildWorld</c> 既有手法以 <c>FailOnUnknownTable=false</c> 加载为无 schema 占位表——本模块
        /// （<c>stat_block</c>）不依赖 <c>Core.Numbers.Archetype</c> 程序集（同 06 第 1 节"L1 各
        /// 模块间不静态耦合表结构"分层边界），测试只需要 <c>class_overrides[].class</c> 引用完整性
        /// 检查能找到一条同 id 的已加载记录，不需要真正构造 <c>ArchSchemas.Class</c>。</summary>
        private static DataRegistry BuildRegistry(string definitionRowsJson, string weightRowsJson, string? archClassRowsJson = null)
        {
            var source = new InMemoryDataSource()
                .Add(StatSchemas.Definition.Name, EnvelopeV2(StatSchemas.Definition.Name, definitionRowsJson))
                .Add(StatSchemas.Weight.Name, Envelope1(StatSchemas.Weight.Name, weightRowsJson));
            if (archClassRowsJson != null)
            {
                source.Add("arch.class", Envelope1("arch.class", archClassRowsJson));
            }

            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(StatSchemas.Weight);
            return registry;
        }

        // -----------------------------------------------------------------
        // T-N1-5：stat.weight 合法记录 / 缺必填 / 引用不存在 / class_overrides 子结构坏形状。
        // -----------------------------------------------------------------

        [Fact]
        public void Weight_WellFormed_LoadsWithoutErrors()
        {
            var defRows = "[{\"id\":\"stat.cov_w_src\",\"name_key\":\"l10n.w_src\",\"category\":\"primary\",\"group\":\"primary\"}]";
            var weightRows = "[{\"id\":\"stat.weight.cov_w_src\",\"stat\":\"stat.cov_w_src\",\"weight\":1.0," +
                "\"class_overrides\":[{\"class\":\"arch.class.cov_sample\",\"weight\":1.5}]}]";
            var archRows = "[{\"id\":\"arch.class.cov_sample\"}]";

            var registry = BuildRegistry(defRows, weightRows, archRows);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Weight_MissingWeightField_ReportsRequiredField()
        {
            var defRows = "[{\"id\":\"stat.cov_w_src2\",\"name_key\":\"l10n.w_src2\",\"category\":\"primary\",\"group\":\"primary\"}]";
            var weightRows = "[{\"id\":\"stat.weight.cov_bad\",\"stat\":\"stat.cov_w_src2\"}]";

            var registry = BuildRegistry(defRows, weightRows);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "weight");
        }

        [Fact]
        public void Weight_StatReferenceNotExist_ReportsReferenceIntegrity()
        {
            var defRows = "[]";
            var weightRows = "[{\"id\":\"stat.weight.cov_dangling\",\"stat\":\"stat.does_not_exist\",\"weight\":1.0}]";

            var registry = BuildRegistry(defRows, weightRows);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "stat");
        }

        [Fact]
        public void Weight_ClassOverrides_MissingWeight_ReportsRequiredField()
        {
            var defRows = "[{\"id\":\"stat.cov_w_src3\",\"name_key\":\"l10n.w_src3\",\"category\":\"primary\",\"group\":\"primary\"}]";
            var weightRows = "[{\"id\":\"stat.weight.cov_bad_override\",\"stat\":\"stat.cov_w_src3\",\"weight\":1.0," +
                "\"class_overrides\":[{\"class\":\"arch.class.cov_sample2\"}]}]";
            var archRows = "[{\"id\":\"arch.class.cov_sample2\"}]";

            var registry = BuildRegistry(defRows, weightRows, archRows);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "class_overrides[0].weight");
        }

        [Fact]
        public void Weight_ClassOverrides_ClassReferenceNotExist_ReportsReferenceIntegrity()
        {
            var defRows = "[{\"id\":\"stat.cov_w_src4\",\"name_key\":\"l10n.w_src4\",\"category\":\"primary\",\"group\":\"primary\"}]";
            var weightRows = "[{\"id\":\"stat.weight.cov_dangling_override\",\"stat\":\"stat.cov_w_src4\",\"weight\":1.0," +
                "\"class_overrides\":[{\"class\":\"arch.class.does_not_exist\",\"weight\":1.0}]}]";

            var registry = BuildRegistry(defRows, weightRows);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "class_overrides[0].class");
        }

        [Fact]
        public void Weight_NegativeWeight_ReportsFieldRange()
        {
            var defRows = "[{\"id\":\"stat.cov_w_src5\",\"name_key\":\"l10n.w_src5\",\"category\":\"primary\",\"group\":\"primary\"}]";
            var weightRows = "[{\"id\":\"stat.weight.cov_negative\",\"stat\":\"stat.cov_w_src5\",\"weight\":-1.0}]";

            var registry = BuildRegistry(defRows, weightRows);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_range" && i.Field == "weight");
        }

        // -----------------------------------------------------------------
        // 禁止事项"禁止 StatHost 消费权重表"防御：源码扫描 + 行为测试双重覆盖。
        // -----------------------------------------------------------------

        /// <summary>记录读取行为的 <see cref="IDataRegistryView"/> 装饰器：把对 <c>Get/GetAll/Query/GetSchema</c> 的每次
        /// 调用涉及的表名记下来，其余成员原样转发。用来把"StatHost 不读 stat.weight"从源码文本扫描改写成
        /// 行为断言（T-M9，2026-10-01 测试覆盖第四批拍板：不留文本扫描）。</summary>
        private sealed class RecordingView : IDataRegistryView
        {
            private readonly IDataRegistryView _inner;

            public System.Collections.Generic.List<string> TablesRead { get; } = new System.Collections.Generic.List<string>();

            public RecordingView(IDataRegistryView inner)
            {
                _inner = inner;
            }

            public DataRecord? Get(string table, string key) { TablesRead.Add(table); return _inner.Get(table, key); }

            public DataRecord? Get(string table, Id id) { TablesRead.Add(table); return _inner.Get(table, id); }

            public System.Collections.Generic.IReadOnlyList<DataRecord> GetAll(string table)
            {
                TablesRead.Add(table);
                return _inner.GetAll(table);
            }

            public System.Collections.Generic.IReadOnlyList<DataRecord> Query(string table, Core.Foundation.Expr.ExprNode predicate)
            {
                TablesRead.Add(table);
                return _inner.Query(table, predicate);
            }

            public System.Collections.Generic.IReadOnlyList<DataRecord> Query(string table, string predicateText)
            {
                TablesRead.Add(table);
                return _inner.Query(table, predicateText);
            }

            public System.Collections.Generic.IReadOnlyList<string> Tables => _inner.Tables;

            public TableSchema? GetSchema(string table) { TablesRead.Add(table); return _inner.GetSchema(table); }
        }

        /// <summary>行为版"禁止 StatHost 消费权重表"：经装饰器观察 StatHost 构造、注册单位、取值，以及
        /// 注册表 Reload 触发的重新装载全过程，从未读取 <c>stat.weight</c>（含 schema 查询）；
        /// 同时对照确认它确实读取了 <c>stat.definition</c>（证明装饰器能观察到读取）。</summary>
        [Fact]
        public void StatHost_NeverReadsStatWeightTable_AcrossConstructionQueryAndReload()
        {
            var defRows = "[{\"id\":\"stat.cov_host_b\",\"name_key\":\"l10n.host_b\",\"category\":\"primary\",\"group\":\"primary\"," +
                "\"default_base\":7}]";
            var weightRows = "[{\"id\":\"stat.weight.cov_host_b\",\"stat\":\"stat.cov_host_b\",\"weight\":3.0}]";
            var registry = BuildRegistry(defRows, weightRows);
            Assert.False(registry.LoadAll().IsBlocking);
            var view = new RecordingView(registry);
            var bus = NewBus();

            var statHost = new StatHost(view, bus, new StatHostOptions());
            var unitId = new Id("unit.cov_host_b");
            statHost.RegisterUnit(unitId);
            var before = statHost.GetStat(unitId, new Id("stat.cov_host_b"));

            // 触发重装载：StatHost 订阅了 LoadCompleted，收到后重新读取定义（经 StatHost 自己的总线发布）。
            bus.PublishImmediate(new DataLoadCompletedEvent(2, 2, 0, 0));
            var after = statHost.GetStat(unitId, new Id("stat.cov_host_b"));

            Assert.Contains(StatSchemas.Definition.Name, view.TablesRead);
            Assert.DoesNotContain(StatSchemas.Weight.Name, view.TablesRead);
            Assert.Equal(7.0, before);
            Assert.Equal(before, after);
        }

        [Fact]
        public void StatHost_TablesLoaded_WithStatWeightPresent_DoesNotThrowAndIgnoresIt()
        {
            var defRows = "[{\"id\":\"stat.cov_host_a\",\"name_key\":\"l10n.host_a\",\"category\":\"primary\",\"group\":\"primary\"," +
                "\"default_base\":10}]";
            var weightRows = "[{\"id\":\"stat.weight.cov_host_a\",\"stat\":\"stat.cov_host_a\",\"weight\":1.0}]";

            var registry = BuildRegistry(defRows, weightRows);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var bus = NewBus();
            // StatHost 构造/注册/取值全程正常——stat.weight 表已加载进 registry，但 StatHost 不读取它
            // （读取记录断言见 StatHost_NeverReadsStatWeightTable_AcrossConstructionQueryAndReload），
            // 本用例断言"不抛异常、取值不受影响"。
            var statHost = new StatHost(registry, bus, new StatHostOptions());
            var unitId = new Id("unit.cov_host");
            statHost.RegisterUnit(unitId);

            var value = statHost.GetStat(unitId, new Id("stat.cov_host_a"));

            Assert.Equal(10.0, value);
        }
    }
}
