using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// 消费方反馈第 45 条（2026-09-17）验收：<see cref="TolerantRegistryView"/>（及 <see
    /// cref="IDataRegistryView.TryGet(string, string, out DataRecord)"/> 新增容错成员）本身的行为——
    /// 只读分析入口（<c>ItemBudgetCurve.BuildStatBudgetInfo</c>/<c>EquipmentScoreAnalyzer.Score</c>/
    /// <c>SkillBudgetAnalyzer.Analyze</c>/<c>ExpectedStatCalculator</c>）各自的用例只覆盖"业务结果
    /// 是否正确"，本文件单独覆盖"包装本身的容错/诊断机制是否如实工作"，两者互补。
    /// </summary>
    public sealed class TolerantRegistryViewTests
    {
        // -----------------------------------------------------------------
        // 夹具：同 DataRegistryTests 的 test.widget/test.owner 惯例——widget.owner 是指向
        // test.owner 的 Reference 字段，触发 reference_integrity 与本文件其余任何表都无关。
        // -----------------------------------------------------------------

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

        private static TableSchema OwnerSchema() => new TableSchema(
            "test.owner", "id", 1,
            new[] { new FieldSchema("id", FieldKind.Id, required: true) });

        private static TableSchema WidgetSchema() => new TableSchema(
            "test.widget", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("owner", FieldKind.Reference, required: false, referenceTable: "test.owner"),
            });

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        [Fact]
        public void Get_RealDataRegistry_BlockedByUnrelatedTable_UnaffectedTableStillFullyReadable_NotDegraded()
        {
            // 复现反馈第 45 条的核心场景：test.widget.a 的 owner 引用了不存在的 test.owner.ghost，
            // 触发 reference_integrity（Error）使整个 registry 阻断——但 test.owner 本身（这里改用
            // 一张完全无关的第三张表 test.owner，登记一条正常记录）与阻断的触发字段毫无关系。
            var source = new InMemoryDataSource()
                .Add("test.widget", Envelope("test.widget", "[{\"id\": \"test.widget.a\", \"owner\": \"test.owner.ghost\"}]"))
                .Add("test.owner", Envelope("test.owner", "[{\"id\": \"test.owner.real\"}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());
            registry.RegisterSchema(OwnerSchema());

            var report = registry.LoadAll();
            Assert.True(report.IsBlocking);
            Assert.Throws<InvalidOperationException>(() => registry.GetAll("test.owner"));

            var tolerant = new TolerantRegistryView(registry);

            // 不抛异常（本条反馈的核心 bug），且因为 DataRegistry.TryGetAll/TryGet 显式实现绕开
            // EnsureReadable 直读快照，这张与阻断无关的表实际仍完整可读——"如实反映"意味着这里
            // IsDegraded 应为 false，不是"读不到就假装读到了"的静默降级。
            var owners = tolerant.GetAll("test.owner");
            Assert.Single(owners);
            Assert.Equal("test.owner.real", owners[0].Key);

            var single = tolerant.Get("test.owner", "test.owner.real");
            Assert.NotNull(single);

            Assert.False(tolerant.IsDegraded);
            Assert.Empty(tolerant.MissingTables);
            Assert.False(tolerant.WasMissing("test.owner"));
        }

        /// <summary>只实现接口、未显式覆盖 Try* 成员的最小 <see cref="IDataRegistryView"/> 替身——
        /// <c>Get</c>/<c>GetAll</c> 恒抛出（模拟"阻断态一律拒绝读取"的教科书式实现，落回接口默认的
        /// try/catch 容错通道），用于验证 <see cref="TolerantRegistryView"/> 在这种"真的读不到"的
        /// 场景下确实标记 <see cref="TolerantRegistryView.IsDegraded"/> = <c>true</c>。</summary>
        private sealed class AlwaysBlockedView : IDataRegistryView
        {
            public DataRecord? Get(string table, string key) => throw new InvalidOperationException("数据校验未通过，禁止读取");

            public DataRecord? Get(string table, Id id) => Get(table, id.Value);

            public IReadOnlyList<DataRecord> GetAll(string table) => throw new InvalidOperationException("数据校验未通过，禁止读取");

            public IReadOnlyList<DataRecord> Query(string table, ExprNode predicate) => throw new InvalidOperationException("数据校验未通过，禁止读取");

            public IReadOnlyList<DataRecord> Query(string table, string predicateText) => throw new InvalidOperationException("数据校验未通过，禁止读取");

            public IReadOnlyList<string> Tables => new[] { "test.widget" };

            public TableSchema? GetSchema(string table) => null;
        }

        [Fact]
        public void Get_TrulyBlockedView_ReturnsNullAndMarksDegraded_DoesNotThrow()
        {
            var tolerant = new TolerantRegistryView(new AlwaysBlockedView());

            var record = tolerant.Get("test.widget", "test.widget.a");
            Assert.Null(record);

            var all = tolerant.GetAll("test.widget");
            Assert.Empty(all);

            Assert.True(tolerant.IsDegraded);
            Assert.Contains("test.widget", tolerant.MissingTables);
            Assert.True(tolerant.WasMissing("test.widget"));

            var diagnostics = tolerant.Diagnostics;
            Assert.True(diagnostics.IsDegraded);
            Assert.Contains("test.widget", diagnostics.MissingTables);
        }

        [Fact]
        public void Wrap_AlreadyTolerantRegistryView_ReturnsSameInstance_DoesNotDoubleWrap()
        {
            var tolerant = new TolerantRegistryView(new AlwaysBlockedView());

            var wrapped = TolerantRegistryView.Wrap(tolerant);

            Assert.Same(tolerant, wrapped);
        }

        [Fact]
        public void Wrap_PlainView_CreatesNewWrapper()
        {
            var inner = new AlwaysBlockedView();

            var wrapped = TolerantRegistryView.Wrap(inner);

            Assert.NotSame(inner, wrapped);
            Assert.False(wrapped.IsDegraded);
        }
    }
}
