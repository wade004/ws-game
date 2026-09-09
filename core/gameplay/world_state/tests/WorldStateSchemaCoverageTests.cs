using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Gameplay.WorldState;
using Xunit;

namespace Tests.Gameplay.WorldState
{
    /// <summary>
    /// ADR-0019 / F1b：<c>world.flag_schema.allowed_values</c> 的登记判断记录（见
    /// <see cref="WorldStateSchemas"/> 该字段旁注释、<c>schema/README.md</c>"子结构登记表"一节）——
    /// 元素类型随同记录 <c>kind</c> 取值变化，<see cref="FieldSchema.Item"/>/<see cref="VariantSchema"/>
    /// 均表达不了"判别字段在父级同层、不在数组元素内部"这种依赖，且
    /// <see cref="Core.Gameplay.WorldState.WorldState"/> 运行期从不读取本字段（无解析代码可作登记
    /// 依据），因此保持登记前行为不变：只检查"存在且是数组"。本测试锁定这一决定——数组元素放
    /// 任意形状（含标量与对象混杂）都不应报错，但字段本身不是数组时仍应报 <c>field_type</c>。
    /// </summary>
    public sealed class WorldStateSchemaCoverageTests
    {
        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        private static string Envelope(string rowsJson) =>
            "{\"table\": \"world.flag_schema\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        [Fact]
        public void AllowedValues_MixedElementShapes_NoSubstructureRegistered_Passes()
        {
            var rows = "[{\"id\": \"world.sample.mixed\", \"kind\": \"string\", " +
                "\"description\": \"示例\", " +
                "\"allowed_values\": [\"a\", 1, true, {\"nested\": \"whatever\"}]}]";

            var source = new InMemoryDataSource().Add(WorldStateSchemas.FlagSchema.Name, Envelope(rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(WorldStateSchemas.FlagSchema);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void AllowedValues_NotAnArray_ReportsFieldType()
        {
            var rows = "[{\"id\": \"world.sample.bad\", \"kind\": \"string\", " +
                "\"description\": \"示例\", \"allowed_values\": \"not_an_array\"}]";

            var source = new InMemoryDataSource().Add(WorldStateSchemas.FlagSchema.Name, Envelope(rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(WorldStateSchemas.FlagSchema);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "allowed_values");
        }
    }
}
