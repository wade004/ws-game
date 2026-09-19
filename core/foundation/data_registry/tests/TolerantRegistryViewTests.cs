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

        // -----------------------------------------------------------------
        // ADR-0041（2026-09-19）连带验收：IDataRegistryView.TryGet 契约行为修正后，
        // TolerantRegistryView.Get 不能再靠 _inner.TryGet 的布尔返回值反推"是否阻断"（修正后该
        // 布尔值只表达"是否找到记录"），必须能继续区分"记录本就不存在（未阻断，不应标记
        // IsDegraded）"与"因阻断读不到（应标记）"——见 TolerantRegistryView.Get 判断记录
        // 2026-09-19 补充段"两步探测"。
        // -----------------------------------------------------------------

        /// <summary>未覆盖 Try* 成员的最小 <see cref="IDataRegistryView"/> 替身：<paramref name="blocked"/>
        /// 为 <c>false</c> 时，<see cref="Get"/> 对不存在的键正常返回 <c>null</c>（不抛异常，模拟
        /// "表可正常访问，只是这条记录本就不存在"）；为 <c>true</c> 时一律抛出（模拟阻断）。用于
        /// 验证 <see cref="TolerantRegistryView"/> 不再把前一种情形误标记为退化。</summary>
        private sealed class PartiallyPopulatedView : IDataRegistryView
        {
            private readonly bool _blocked;
            private readonly DataRecord _existing;

            public PartiallyPopulatedView(bool blocked, DataRecord existing)
            {
                _blocked = blocked;
                _existing = existing;
            }

            private static InvalidOperationException Blocked() =>
                new InvalidOperationException("数据校验未通过，禁止读取（测试替身模拟阻断态）");

            public DataRecord? Get(string table, string key)
            {
                if (_blocked) throw Blocked();
                return key == _existing.Key ? _existing : null;
            }

            public DataRecord? Get(string table, Id id) => Get(table, id.Value);

            public IReadOnlyList<DataRecord> GetAll(string table)
            {
                if (_blocked) throw Blocked();
                return new[] { _existing };
            }

            public IReadOnlyList<DataRecord> Query(string table, ExprNode predicate) => throw new NotSupportedException();

            public IReadOnlyList<DataRecord> Query(string table, string predicateText) => throw new NotSupportedException();

            public IReadOnlyList<string> Tables => new[] { "test.widget" };

            public TableSchema? GetSchema(string table) => null;
        }

        private static DataRecord BuildExistingWidgetRecord()
        {
            var source = new InMemoryDataSource().Add("test.widget", Envelope("test.widget", "[{\"id\": \"test.widget.a\"}]"));
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(WidgetSchema());
            registry.LoadAll();
            return registry.Get("test.widget", "test.widget.a")!;
        }

        /// <summary>反向确认关键用例（ADR-0041 连带修正）：本用例验证的是 <see
        /// cref="TolerantRegistryView.Get(string, string)"/> 新增的"两步探测"（先 <c>_inner.TryGet</c>
        /// 取记录，找不到时再探 <c>_inner.TryGetAll</c> 确认是否阻断）。若把该方法改回只按
        /// <c>_inner.TryGet</c> 单一布尔值判定（ADR-0041 修正 <c>TryGet</c> 契约前的旧写法），本
        /// 用例必然从通过变为失败——<c>IsDegraded</c>/<c>WasMissing</c> 断言会从 <c>False</c>
        /// 误判为 <c>True</c>（把"记录本就不存在"错误标记成"因阻断读不到"）。</summary>
        [Fact]
        public void Get_PartiallyPopulatedInnerView_RecordDoesNotExist_NotBlocking_ReturnsNullNotDegraded()
        {
            var inner = new PartiallyPopulatedView(blocked: false, BuildExistingWidgetRecord());
            var tolerant = new TolerantRegistryView(inner);

            var record = tolerant.Get("test.widget", "test.widget.does_not_exist");

            Assert.Null(record);
            Assert.False(tolerant.IsDegraded);
            Assert.False(tolerant.WasMissing("test.widget"));
        }

        [Fact]
        public void Get_PartiallyPopulatedInnerView_RecordExists_NotBlocking_ReturnsRecordNotDegraded()
        {
            var existing = BuildExistingWidgetRecord();
            var inner = new PartiallyPopulatedView(blocked: false, existing);
            var tolerant = new TolerantRegistryView(inner);

            var record = tolerant.Get("test.widget", "test.widget.a");

            Assert.Same(existing, record);
            Assert.False(tolerant.IsDegraded);
        }

        [Fact]
        public void Get_PartiallyPopulatedInnerView_Blocked_ReturnsNullAndMarksDegraded()
        {
            var inner = new PartiallyPopulatedView(blocked: true, BuildExistingWidgetRecord());
            var tolerant = new TolerantRegistryView(inner);

            var record = tolerant.Get("test.widget", "test.widget.a");

            Assert.Null(record);
            Assert.True(tolerant.IsDegraded);
            Assert.True(tolerant.WasMissing("test.widget"));
        }

        [Fact]
        public void TryGet_PartiallyPopulatedInnerView_RecordDoesNotExist_NotBlocking_ReturnsFalseNotDegraded()
        {
            var inner = new PartiallyPopulatedView(blocked: false, BuildExistingWidgetRecord());
            var tolerant = new TolerantRegistryView(inner);

            var ok = tolerant.TryGet("test.widget", "test.widget.does_not_exist", out var record);

            Assert.False(ok);
            Assert.Null(record);
            Assert.False(tolerant.IsDegraded);
        }
    }
}
