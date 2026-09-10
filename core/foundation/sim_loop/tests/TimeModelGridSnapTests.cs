using System;
using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    /// <summary>
    /// 格子吸附（ADR-0013 决策 6、04 第 3.1 节 <c>found.time_model.grid_snap</c>，codex 第十八轮）：
    /// <see cref="TimeModelDefinition.GridSnapCellSize"/> 的结构抽取、以及
    /// <see cref="TimeModelSchema"/> 的 <c>grid_snap.cell_size</c> 子结构登记（ADR-0019）在数据加载期
    /// 是否真的拦下非法取值——不只是 <c>SchemaAudit</c> 静态检查，<see cref="IDataRegistry.LoadAll"/>
    /// 才是这条约束在真实数据上生效的地方（见 <c>DataRegistry.ValidateObjectField</c> 递归校验，
    /// <see cref="TimeFieldConsistencyRuleTests"/> 同一惯例）。
    /// </summary>
    public sealed class TimeModelGridSnapTests
    {
        private static IDataRegistry BuildRegistry(string timeModelJson)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource().Add("found.time_model", timeModelJson);
            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(TimeModelSchema.Table);
            return registry;
        }

        [Fact]
        public void GridSnapNotDeclared_ParsesToNullCellSize()
        {
            const string json = @"
            {
                ""table"": ""found.time_model"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""found.time_model.exploration"", ""scope"": ""exploration"", ""mode"": ""continuous"" }
                ]
            }";

            var registry = BuildRegistry(json);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var record = registry.GetAll("found.time_model").Single();
            var def = TimeModelDefinition.FromRecord(record);

            Assert.Null(def.GridSnapCellSize);
        }

        [Fact]
        public void GridSnapDeclared_ParsesCellSize()
        {
            const string json = @"
            {
                ""table"": ""found.time_model"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""found.time_model.combat"", ""scope"": ""combat"", ""mode"": ""discrete"",
                      ""seconds_per_turn"": 6, ""initiative_policy"": ""fixed_order"",
                      ""movement_budget_rule"": ""distance"", ""grid_snap"": { ""cell_size"": 2.5 } }
                ]
            }";

            var registry = BuildRegistry(json);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var record = registry.GetAll("found.time_model").Single();
            var def = TimeModelDefinition.FromRecord(record);

            Assert.Equal(2.5, def.GridSnapCellSize);
        }

        [Fact]
        public void GridSnapCellSize_ZeroOrNegative_RejectedAtLoadTime()
        {
            const string json = @"
            {
                ""table"": ""found.time_model"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""found.time_model.combat"", ""scope"": ""combat"", ""mode"": ""discrete"",
                      ""seconds_per_turn"": 6, ""initiative_policy"": ""fixed_order"",
                      ""movement_budget_rule"": ""distance"", ""grid_snap"": { ""cell_size"": 0 } }
                ]
            }";

            var registry = BuildRegistry(json);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.Contains(report.Issues, i => i.Check == "field_range" && i.Field == "grid_snap.cell_size");
        }

        [Fact]
        public void GridSnapCellSize_MissingSubField_RejectedAtLoadTime()
        {
            // cell_size 在 grid_snap 子结构内登记为必填（TimeModelSchema.Table），声明了 grid_snap
            // 但漏掉 cell_size 时，DataRegistry 的递归结构校验应报"required"一类错误，不能悄悄
            // 放过一个没有格子尺寸的"启用了格子吸附"声明。
            const string json = @"
            {
                ""table"": ""found.time_model"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""found.time_model.combat"", ""scope"": ""combat"", ""mode"": ""discrete"",
                      ""seconds_per_turn"": 6, ""initiative_policy"": ""fixed_order"",
                      ""movement_budget_rule"": ""distance"", ""grid_snap"": { } }
                ]
            }";

            var registry = BuildRegistry(json);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.Contains(report.Issues, i => i.Field == "grid_snap.cell_size");
        }
    }
}
