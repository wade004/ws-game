using System;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// 消费方反馈第 45 条（2026-09-17）验收：<see cref="ItemBudgetCurve.BuildStatBudgetInfo"/>
    /// 两个重载在 registry 阻断态下不再抛 <see cref="InvalidOperationException"/>，且新增的
    /// <c>out TolerantReadDiagnostics</c> 重载能如实回吐"是否降级"。反馈原文复现步骤：把某条记录
    /// 的引用字段改成不存在的 id 触发 <c>reference_integrity</c>（Error）使 registry 整体阻断，
    /// 随后仍需要能对同一 registry 调用本方法算出预算信息——本文件用与三张支持表
    /// （<c>stat.definition</c>/<c>stat.weight</c>/<c>stat.rating_conversion</c>）完全无关的
    /// <c>test.widget</c>/<c>test.owner</c> 触发阻断（同 <c>DataRegistryTests</c> 既有夹具手法），
    /// 验证这三张表本身不受影响、结果不降级。
    /// </summary>
    public sealed class ItemBudgetCurveBuildStatBudgetInfoTests
    {
        private static readonly Id StrengthId = new Id("stat.strength");

        private const string StatDefJson =
            "[{\"id\": \"stat.strength\", \"name_key\": \"l10n.stat.strength\", \"category\": \"primary\"}]";

        private const string StatWeightJson =
            "[{\"id\": \"stat.weight.strength\", \"stat\": \"stat.strength\", \"weight\": 2.5}]";

        private static TableSchema WidgetSchema() => new TableSchema(
            "test.widget", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("owner", FieldKind.Reference, required: false, referenceTable: "test.owner"),
            });

        private static TableSchema OwnerSchema() => new TableSchema(
            "test.owner", "id", 1,
            new[] { new FieldSchema("id", FieldKind.Id, required: true) });

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(Array.Empty<EventDefinition>());
            return new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
        }

        /// <summary><paramref name="withBlockingUnrelatedWidget"/> 为 <c>true</c> 时额外登记
        /// <c>test.widget</c>/<c>test.owner</c> 并注入一条坏引用，使 <see
        /// cref="DataRegistry.LoadAll()"/> 返回阻断态报告——与 <c>stat.*</c> 三张支持表完全无关。</summary>
        private static DataRegistry BuildRegistry(bool withBlockingUnrelatedWidget)
        {
            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefJson))
                .Add("stat.weight", Envelope("stat.weight", StatWeightJson));

            if (withBlockingUnrelatedWidget)
            {
                source.Add("test.widget", Envelope("test.widget", "[{\"id\": \"test.widget.a\", \"owner\": \"test.owner.ghost\"}]"));
            }

            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Weight);
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.RatingConversion);

            if (withBlockingUnrelatedWidget)
            {
                registry.RegisterSchema(WidgetSchema());
                registry.RegisterSchema(OwnerSchema());
            }

            var report = registry.LoadAll();
            if (withBlockingUnrelatedWidget)
            {
                Assert.True(report.IsBlocking);
            }
            else
            {
                Assert.False(report.IsBlocking);
            }

            return registry;
        }

        [Fact]
        public void NonBlockingState_ResultMatchesPreChangeBehavior_NotDegraded()
        {
            var registry = BuildRegistry(withBlockingUnrelatedWidget: false);

            var withoutDiagnostics = ItemBudgetCurve.BuildStatBudgetInfo(registry);
            var withDiagnostics = ItemBudgetCurve.BuildStatBudgetInfo(registry, out var diagnostics);

            Assert.Equal(2.5, withoutDiagnostics[StrengthId].Weight, 9);
            Assert.Equal(2.5, withDiagnostics[StrengthId].Weight, 9);
            Assert.False(diagnostics.IsDegraded);
            Assert.Empty(diagnostics.MissingTables);
        }

        [Fact]
        public void BlockingState_UnrelatedReferenceIntegrityError_DoesNotThrow_AndNotDegraded()
        {
            // 反馈原文复现：registry 因与 stat.* 无关的坏引用整体阻断。
            var registry = BuildRegistry(withBlockingUnrelatedWidget: true);
            Assert.Throws<InvalidOperationException>(() => registry.GetAll("stat.weight"));

            // 修复前：下面这行会抛 InvalidOperationException（"数据校验未通过，禁止读取"）——即使
            // stat.definition/stat.weight/stat.rating_conversion 与触发阻断的 test.widget.owner
            // 毫无关系。修复后：不抛异常。
            var result = ItemBudgetCurve.BuildStatBudgetInfo(registry, out var diagnostics);

            Assert.Equal(2.5, result[StrengthId].Weight, 9);
            // "如实反映"：具体 DataRegistry 的 TryGetAll/TryGet 显式实现绕开 EnsureReadable 直读
            // 表快照，stat.* 三张表本身完全没受影响，因此这里应当是 false，不是"读不到就假装读到
            // 了"的静默降级——真正的降级路径由 TolerantRegistryViewTests 用不覆盖 Try* 的替身覆盖。
            Assert.False(diagnostics.IsDegraded);
            Assert.Empty(diagnostics.MissingTables);

            // 旧的两个重载（不带诊断输出）也必须不再抛异常，行为与带诊断的重载一致。
            var byClass = ItemBudgetCurve.BuildStatBudgetInfo(registry, new Id("arch.class.anything"));
            Assert.Equal(2.5, byClass[StrengthId].Weight, 9);
            var plain = ItemBudgetCurve.BuildStatBudgetInfo(registry);
            Assert.Equal(2.5, plain[StrengthId].Weight, 9);
        }
    }
}
