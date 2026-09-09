#pragma warning disable CS0618 // 有意调用 [Obsolete] 的 1.12 兼容签名，验证 ABI/API 兼容 façade 行为。
using System;
using System.Linq;
using Core.Foundation.DataRegistry;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// P2-01 ABI/API 兼容回归测试（外部审计 audit-c9ff301-20260909，改造自审计探针
    /// architecture/落地计划/audit-c9ff301-20260909/docs-project/api-compat/Program.cs）：<see
    /// cref="FieldSchema"/> 1.12 的七参数构造签名（<c>name, kind, required, enumValues,
    /// referenceTable, referenceDomain, description</c>）必须仍然存在且可用，行为与用主构造函数
    /// 显式传 <c>fields/item/variants/itemFactory/variantsFactory</c> 全 null 完全一致。
    /// </summary>
    public sealed class P2_01_FieldSchemaLegacyCtorTests
    {
        [Fact]
        public void LegacySevenParamCtor_ProducesEquivalentFieldSchema_ToMainCtorWithNullSubstructures()
        {
            var legacy = new FieldSchema(
                "id", FieldKind.Id, false, null, null, null, "legacy");

            var current = new FieldSchema(
                "id", FieldKind.Id, false,
                enumValues: null, referenceTable: null, referenceDomain: null, description: "legacy",
                fields: null, item: null, variants: null, itemFactory: null, variantsFactory: null);

            Assert.Equal(current.Name, legacy.Name);
            Assert.Equal(current.Kind, legacy.Kind);
            Assert.Equal(current.Required, legacy.Required);
            Assert.Equal(current.EnumValues, legacy.EnumValues);
            Assert.Equal(current.ReferenceTable, legacy.ReferenceTable);
            Assert.Equal(current.ReferenceDomain, legacy.ReferenceDomain);
            Assert.Equal(current.Description, legacy.Description);
            Assert.Null(legacy.Fields);
            Assert.Null(legacy.Item);
            Assert.Null(legacy.Variants);
        }

        [Fact]
        public void LegacySevenParamCtor_WithEnumValues_MatchesMainCtor()
        {
            var values = new[] { "a", "b" };
            var legacy = new FieldSchema("kind", FieldKind.Enum, true, values, null, null, "desc");

            Assert.Equal(FieldKind.Enum, legacy.Kind);
            Assert.Equal(values, legacy.EnumValues);
        }

        [Fact]
        public void LegacySevenParamCtor_WithReferenceTable_MatchesMainCtor()
        {
            var legacy = new FieldSchema("ref", FieldKind.Reference, true, null, "some.table", null, null);

            Assert.Equal("some.table", legacy.ReferenceTable);
            Assert.Null(legacy.ReferenceDomain);
        }

        /// <summary>调用点恰好传 3～6 个参数时必须不受新增七参数重载影响，仍然唯一匹配主构造函数
        /// （不产生重载二义性编译错误）——见 <see cref="FieldSchema"/> 七参数构造函数判断记录"不是
        /// 可选参数"。</summary>
        [Fact]
        public void MainCtor_StillResolvesUnambiguously_WithFewerThanSevenArguments()
        {
            var threeArgs = new FieldSchema("a", FieldKind.Bool, true);
            var fiveArgs = new FieldSchema("b", FieldKind.String, false, null, null);

            Assert.Equal("a", threeArgs.Name);
            Assert.Equal("b", fiveArgs.Name);
        }

        /// <summary>调用点传超过七个参数（用到 ADR-0019 的 Fields/Item/Variants 等）时仍然只匹配
        /// 主构造函数，不受新增七参数重载影响。</summary>
        [Fact]
        public void MainCtor_StillResolvesUnambiguously_WithMoreThanSevenArguments()
        {
            var withFields = new FieldSchema(
                "obj", FieldKind.Object, true, null, null, null, "desc",
                fields: new[] { new FieldSchema("inner", FieldKind.Bool, true) });

            Assert.NotNull(withFields.Fields);
            Assert.Single(withFields.Fields!);
        }

        /// <summary>端到端：用旧七参数构造签名拼出的 <see cref="TableSchema"/> 仍能正常参与
        /// <see cref="DataRegistry"/> 的加载/校验流程（不只是构造函数本身不抛异常）。</summary>
        [Fact]
        public void LegacyFieldSchema_WorksInsideRealTableSchemaLoad()
        {
            var schema = new TableSchema(
                name: "p2_01.legacy_field_schema",
                primaryKey: "id",
                currentSchemaVersion: 1,
                fields: new[]
                {
                    new FieldSchema("id", FieldKind.Id, true, null, null, null, "legacy ctor 构造的字段"),
                    new FieldSchema("label", FieldKind.String, false, null, null, null, null),
                });

            var source = new InMemoryDataSource().Add(
                "p2_01.legacy_field_schema",
                "{\"table\": \"p2_01.legacy_field_schema\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"p2_01.legacy_field_schema.sample\", \"label\": \"hello\"}]}");

            var bus = new Core.Foundation.EventBus.EventBus(
                Core.Foundation.EventBus.EventCatalog.FromDefinitions(Array.Empty<Core.Foundation.EventBus.EventDefinition>()),
                new Core.Foundation.EventBus.EventBusOptions { StrictCatalog = false });
            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(schema);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            var record = registry.Get("p2_01.legacy_field_schema", "p2_01.legacy_field_schema.sample");
            Assert.NotNull(record);
            Assert.Equal("hello", record!.GetString("label"));
        }
    }
}
#pragma warning restore CS0618
