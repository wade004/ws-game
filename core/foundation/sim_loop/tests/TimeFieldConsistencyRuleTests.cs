using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Foundation.SimLoop
{
    /// <summary>
    /// 04 第 3.1/5 节"时间字段与时间模型一致"（ADR-0013 补齐任务）：<see cref="TimeFieldConsistencyRule"/>
    /// 是本模块（L0）提供的通用机制，不认识任何具体上层表名——本文件用一张自造的最小测试表
    /// （<c>test.time_field_target</c>）验证机制本身：某字段归属的作用域声明为 <c>discrete</c> 时，
    /// 非整数取值报 <see cref="ValidationSeverity.Error"/>（消息含表名/行 id/字段名，见任务书验收
    /// "校验器报错含表名/行 id/字段名"）；声明为 <c>continuous</c>，或压根没有该作用域的
    /// <c>found.time_model</c> 记录时，不做整数限制。真正认识 <c>skill.def.cast_time</c> 一类具体
    /// 字段名的注册点见 <c>core/rules/assembly/RulesSchemaCatalog.cs</c>/
    /// <c>core/gameplay/assembly/GameplaySchemaCatalog.cs</c> 各自的
    /// <c>RegisterTimeFieldConsistencyRule</c> 方法（不在本文件重复测试其具体字段清单，那属于
    /// "使用方"而非"机制本身"）。
    /// </summary>
    public sealed class TimeFieldConsistencyRuleTests
    {
        private static readonly TableSchema TargetTable = new TableSchema(
            name: "test.time_field_target",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("cost", FieldKind.Number, required: true),
            });

        private static IDataRegistry BuildRegistry(string timeModelJson, string targetJson)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("found.time_model", timeModelJson)
                .Add("test.time_field_target", targetJson);

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(TimeModelSchema.Table);
            registry.RegisterSchema(TargetTable);

            var declarations = new[]
            {
                new TimeFieldDeclaration("test.time_field_target", "cost", "combat", r => r.TryGetNumber("cost", out var v) ? v : (double?)null),
            };
            registry.RegisterValidationRule(new TimeFieldConsistencyRule(declarations));

            return registry;
        }

        private const string DiscreteCombatTimeModelJson = @"
        {
            ""table"": ""found.time_model"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""found.time_model.tfc_exploration"", ""scope"": ""exploration"", ""mode"": ""continuous"" },
                { ""id"": ""found.time_model.tfc_combat"", ""scope"": ""combat"", ""mode"": ""discrete"",
                  ""seconds_per_turn"": 6, ""initiative_policy"": ""fixed_order"", ""movement_budget_rule"": ""distance"" }
            ]
        }";

        private const string ContinuousCombatTimeModelJson = @"
        {
            ""table"": ""found.time_model"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""found.time_model.tfc_exploration"", ""scope"": ""exploration"", ""mode"": ""continuous"" },
                { ""id"": ""found.time_model.tfc_combat"", ""scope"": ""combat"", ""mode"": ""continuous"" }
            ]
        }";

        [Fact]
        public void DiscreteScope_NonIntegerValue_ReportsError_WithTableRecordAndField()
        {
            const string targetJson = @"
            {
                ""table"": ""test.time_field_target"",
                ""schema_version"": 1,
                ""rows"": [ { ""id"": ""test.time_field_target.bad"", ""cost"": 1.5 } ]
            }";

            var registry = BuildRegistry(DiscreteCombatTimeModelJson, targetJson);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking, string.Join("; ", report.Issues));
            var issue = Assert.Single(report.Issues, i => i.Check == TimeFieldConsistencyRule.CheckTimeFieldMustBeInteger);
            Assert.Equal(ValidationSeverity.Error, issue.Severity);
            Assert.Equal("test.time_field_target", issue.Table);
            Assert.Equal("test.time_field_target.bad", issue.RecordKey);
            Assert.Equal("cost", issue.Field);
            Assert.Contains("cost", issue.Message);
        }

        [Fact]
        public void DiscreteScope_IntegerValue_NoError()
        {
            const string targetJson = @"
            {
                ""table"": ""test.time_field_target"",
                ""schema_version"": 1,
                ""rows"": [ { ""id"": ""test.time_field_target.ok"", ""cost"": 3 } ]
            }";

            var registry = BuildRegistry(DiscreteCombatTimeModelJson, targetJson);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.DoesNotContain(report.Issues, i => i.Check == TimeFieldConsistencyRule.CheckTimeFieldMustBeInteger);
        }

        [Fact]
        public void ContinuousScope_NonIntegerValue_NoError()
        {
            const string targetJson = @"
            {
                ""table"": ""test.time_field_target"",
                ""schema_version"": 1,
                ""rows"": [ { ""id"": ""test.time_field_target.fractional"", ""cost"": 1.5 } ]
            }";

            var registry = BuildRegistry(ContinuousCombatTimeModelJson, targetJson);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.DoesNotContain(report.Issues, i => i.Check == TimeFieldConsistencyRule.CheckTimeFieldMustBeInteger);
        }

        // 数组元素形态（ADR-0013 补齐任务新增，见 TimeFieldDeclaration.ExtractMany 判断记录
        // "光环周期字段覆盖"）：一张自造的最小测试表，"items" 字段是一个 "kind + params" 数组
        // （形状同 skill.aura_def.effects[]），declaration 用 ExtractMany 扫描每个元素
        // params.cost 字段，按下标产出独立字段路径。真正认识 skill.aura_def.effects[].params.
        // interval/tick_interval 的注册点见 core/rules/assembly/RulesSchemaCatalog.cs
        // AuraEffectPeriodicFields（同一套机制，字段名不同，不在本文件重复）。

        private static readonly TableSchema ArrayTargetTable = new TableSchema(
            name: "test.time_field_array_target",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("items", FieldKind.Array, required: true, description: "[{kind: String, params: Object}, ...]"),
            });

        private static IDataRegistry BuildArrayRegistry(string timeModelJson, string targetJson)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("found.time_model", timeModelJson)
                .Add("test.time_field_array_target", targetJson);

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(TimeModelSchema.Table);
            registry.RegisterSchema(ArrayTargetTable);

            IEnumerable<(string FieldLabel, double Value)> ItemCosts(DataRecord record)
            {
                if (!record.TryGetArray("items", out var items))
                {
                    yield break;
                }

                for (var i = 0; i < items.Count; i++)
                {
                    if (!(items[i] is Core.Foundation.Common.Json.JsonObject item))
                    {
                        continue;
                    }
                    if (!item.TryGetValue("params", out var paramsRaw) ||
                        !(paramsRaw is Core.Foundation.Common.Json.JsonObject itemParams))
                    {
                        continue;
                    }
                    if (itemParams.TryGetValue("cost", out var costRaw) &&
                        costRaw is Core.Foundation.Common.Json.JsonNumber costNumber)
                    {
                        yield return ($"items[{i}].params.cost", costNumber.Value);
                    }
                }
            }

            var declarations = new[]
            {
                new TimeFieldDeclaration("test.time_field_array_target", "items[].params", "combat", ItemCosts),
            };
            registry.RegisterValidationRule(new TimeFieldConsistencyRule(declarations));

            return registry;
        }

        [Fact]
        public void ExtractMany_DiscreteScope_ReportsOneIssuePerArrayElement_WithIndexedFieldPath()
        {
            const string targetJson = @"
            {
                ""table"": ""test.time_field_array_target"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""test.time_field_array_target.mixed"", ""items"": [
                        { ""kind"": ""a"", ""params"": { ""cost"": 2 } },
                        { ""kind"": ""b"", ""params"": { ""cost"": 1.5 } },
                        { ""kind"": ""c"", ""params"": { ""other"": 9 } }
                    ] }
                ]
            }";

            var registry = BuildArrayRegistry(DiscreteCombatTimeModelJson, targetJson);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking, string.Join("; ", report.Issues));
            var issue = Assert.Single(report.Issues, i => i.Check == TimeFieldConsistencyRule.CheckTimeFieldMustBeInteger);
            Assert.Equal("test.time_field_array_target.mixed", issue.RecordKey);
            Assert.Equal("items[1].params.cost", issue.Field);
            Assert.Contains("items[1].params.cost", issue.Message);
        }

        [Fact]
        public void ExtractMany_DiscreteScope_AllIntegerValues_NoError()
        {
            const string targetJson = @"
            {
                ""table"": ""test.time_field_array_target"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""test.time_field_array_target.ok"", ""items"": [
                        { ""kind"": ""a"", ""params"": { ""cost"": 2 } },
                        { ""kind"": ""b"", ""params"": { ""cost"": 3 } }
                    ] }
                ]
            }";

            var registry = BuildArrayRegistry(DiscreteCombatTimeModelJson, targetJson);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.DoesNotContain(report.Issues, i => i.Check == TimeFieldConsistencyRule.CheckTimeFieldMustBeInteger);
        }

        [Fact]
        public void ExtractMany_ContinuousScope_NonIntegerValues_NoError()
        {
            const string targetJson = @"
            {
                ""table"": ""test.time_field_array_target"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""test.time_field_array_target.fractional"", ""items"": [
                        { ""kind"": ""a"", ""params"": { ""cost"": 1.5 } }
                    ] }
                ]
            }";

            var registry = BuildArrayRegistry(ContinuousCombatTimeModelJson, targetJson);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            Assert.DoesNotContain(report.Issues, i => i.Check == TimeFieldConsistencyRule.CheckTimeFieldMustBeInteger);
        }

        [Fact]
        public void ScopeWithoutTimeModelRecord_NonIntegerValue_NoError()
        {
            // 数据集只登记 exploration，没有 combat 一条：declaration 归属 combat 作用域，
            // scopeModes 里找不到 "combat" 键，等价于"该项检查不适用"（见类型判断记录）。
            const string timeModelJson = @"
            {
                ""table"": ""found.time_model"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""found.time_model.tfc_exploration_only"", ""scope"": ""exploration"", ""mode"": ""continuous"" }
                ]
            }";
            const string targetJson = @"
            {
                ""table"": ""test.time_field_target"",
                ""schema_version"": 1,
                ""rows"": [ { ""id"": ""test.time_field_target.fractional"", ""cost"": 1.5 } ]
            }";

            var registry = BuildRegistry(timeModelJson, targetJson);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }
    }
}
