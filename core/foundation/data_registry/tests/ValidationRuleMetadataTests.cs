using System.Collections.Generic;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// 分阶段落地计划 T-N0-2 验收（落地清单 2.2 V1/V2）：<see cref="IValidationRule"/> 规则元数据默认值、
    /// 不可提升的警告在 <see cref="DataRegistryStrictness.WarningsBlock"/> 下不阻断（可提升的照旧阻断）、
    /// 重复注册去重（同一实例 / 同 RuleId 的另一实例）、问题补规则 id（规则自填不覆盖）、
    /// <see cref="ValidationReport.Rules"/> 按注册顺序含零命中规则、<see cref="ValidationIssue"/> 新构造
    /// 与 <see cref="ValidationIssue.WithRuleId"/>、两参数报告构造行为不变。
    /// </summary>
    public sealed class ValidationRuleMetadataTests
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

        private static TableSchema WidgetSchema() => new TableSchema("test.widget", "id", 1, new[]
        {
            new FieldSchema("id", FieldKind.Id, required: true, description: "主键"),
        });

        private static DataRegistry MakeRegistry(DataRegistryStrictness strictness)
        {
            var source = new InMemoryDataSource().Add("test.widget",
                "{\"table\": \"test.widget\", \"schema_version\": 1, \"rows\": [{\"id\": \"test.widget.a\"}]}");
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { Strictness = strictness });
            registry.RegisterSchema(WidgetSchema());
            return registry;
        }

        /// <summary>沿用默认元数据的警告规则（RuleId = 类型名、Error、可提升）。</summary>
        private sealed class PlainWarnRule : IValidationRule
        {
            public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
            {
                yield return new ValidationIssue(ValidationSeverity.Warning, "test.widget", "plain_warn", "普通警告");
            }
        }

        /// <summary>登记为不可提升的警告规则。</summary>
        private sealed class IntentWarnRule : IValidationRule
        {
            public string RuleId => "intent_warn_rule";
            public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;
            public bool NonEscalatable => true;

            public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
            {
                yield return new ValidationIssue(ValidationSeverity.Warning, "test.widget", "intent_warn", "意图类警告",
                    recordKey: "test.widget.a", field: null, group: "待确认", note: "作者说明原文", ruleId: null);
            }
        }

        /// <summary>自带 RuleId 的规则，且给问题自填了另一个 RuleId（收集时不应被覆盖）。</summary>
        private sealed class SelfTaggedRule : IValidationRule
        {
            public string RuleId => "self_tagged";

            public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
            {
                yield return new ValidationIssue(ValidationSeverity.Warning, "test.widget", "self_tagged_check", "自填 id",
                    null, null, null, null, "custom_id");
            }
        }

        private sealed class SilentRule : IValidationRule
        {
            public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
            {
                yield break;
            }
        }

        [Fact]
        public void DefaultMetadata_RuleIdIsTypeName_ErrorLevel_Escalatable()
        {
            IValidationRule rule = new PlainWarnRule();

            Assert.Equal(nameof(PlainWarnRule), rule.RuleId);
            Assert.Equal(ValidationSeverity.Error, rule.DefaultSeverity);
            Assert.False(rule.NonEscalatable);
        }

        [Fact]
        public void NonEscalatableWarning_UnderWarningsBlock_DoesNotBlock()
        {
            var registry = MakeRegistry(DataRegistryStrictness.WarningsBlock);
            registry.RegisterValidationRule(new IntentWarnRule());

            var report = registry.LoadAll();

            Assert.Equal(1, report.WarningCount);
            Assert.Equal(1, report.NonEscalatableWarningCount);
            Assert.Equal(0, report.ErrorCount);
            Assert.False(report.IsBlocking);
            Assert.NotNull(registry.Get("test.widget", "test.widget.a"));
        }

        [Fact]
        public void EscalatableWarning_UnderWarningsBlock_StillBlocks()
        {
            var registry = MakeRegistry(DataRegistryStrictness.WarningsBlock);
            registry.RegisterValidationRule(new IntentWarnRule());
            registry.RegisterValidationRule(new PlainWarnRule());

            var report = registry.LoadAll();

            Assert.Equal(2, report.WarningCount);
            Assert.Equal(1, report.NonEscalatableWarningCount);
            Assert.True(report.IsBlocking);
        }

        [Fact]
        public void NonEscalatableWarning_UnderWarningsAllowed_NotBlockingAsBefore()
        {
            var registry = MakeRegistry(DataRegistryStrictness.WarningsAllowed);
            registry.RegisterValidationRule(new IntentWarnRule());
            registry.RegisterValidationRule(new PlainWarnRule());

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking);
        }

        [Fact]
        public void RegisterValidationRule_SameInstanceTwice_RunsOnce()
        {
            var registry = MakeRegistry(DataRegistryStrictness.WarningsAllowed);
            var rule = new PlainWarnRule();
            registry.RegisterValidationRule(rule);
            registry.RegisterValidationRule(rule);

            var report = registry.LoadAll();

            Assert.Single(report.Issues, i => i.Check == "plain_warn");
            Assert.Single(report.Rules);
        }

        [Fact]
        public void RegisterValidationRule_AnotherInstanceWithSameRuleId_Deduplicated()
        {
            var registry = MakeRegistry(DataRegistryStrictness.WarningsAllowed);
            registry.RegisterValidationRule(new IntentWarnRule());
            registry.RegisterValidationRule(new IntentWarnRule());

            var report = registry.LoadAll();

            Assert.Single(report.Issues, i => i.Check == "intent_warn");
            Assert.Single(report.Rules);
            Assert.Equal("intent_warn_rule", report.Rules[0].RuleId);
        }

        [Fact]
        public void CollectedIssues_GetRuleIdStamped_SelfFilledIdPreserved()
        {
            var registry = MakeRegistry(DataRegistryStrictness.WarningsAllowed);
            registry.RegisterValidationRule(new PlainWarnRule());
            registry.RegisterValidationRule(new SelfTaggedRule());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "plain_warn" && i.RuleId == nameof(PlainWarnRule));
            Assert.Contains(report.Issues, i => i.Check == "self_tagged_check" && i.RuleId == "custom_id");
            // 字段级内置检查项不带规则 id（本用例数据合法，没有内置问题；这里只断言规则问题都有 id）。
            Assert.All(report.Issues, i => Assert.NotNull(i.RuleId));
        }

        [Fact]
        public void ReportRules_InRegistrationOrder_IncludeZeroHitRules_WithMetadata()
        {
            var registry = MakeRegistry(DataRegistryStrictness.WarningsAllowed);
            registry.RegisterValidationRule(new SilentRule());
            registry.RegisterValidationRule(new IntentWarnRule());
            registry.RegisterValidationRule(new PlainWarnRule());

            var report = registry.LoadAll();

            Assert.Equal(3, report.Rules.Count);
            Assert.Equal(nameof(SilentRule), report.Rules[0].RuleId);
            Assert.Equal(0, report.Rules[0].HitCount);
            Assert.Equal(ValidationSeverity.Error, report.Rules[0].DefaultSeverity);
            Assert.False(report.Rules[0].NonEscalatable);

            Assert.Equal("intent_warn_rule", report.Rules[1].RuleId);
            Assert.Equal(1, report.Rules[1].HitCount);
            Assert.Equal(ValidationSeverity.Warning, report.Rules[1].DefaultSeverity);
            Assert.True(report.Rules[1].NonEscalatable);

            Assert.Equal(nameof(PlainWarnRule), report.Rules[2].RuleId);
            Assert.Equal(1, report.Rules[2].HitCount);
        }

        [Fact]
        public void GroupAndNote_CarriedThroughReport()
        {
            var registry = MakeRegistry(DataRegistryStrictness.WarningsAllowed);
            registry.RegisterValidationRule(new IntentWarnRule());

            var report = registry.LoadAll();

            var issue = Assert.Single(report.Issues, i => i.Check == "intent_warn");
            Assert.Equal("待确认", issue.Group);
            Assert.Equal("作者说明原文", issue.Note);
            Assert.Equal("test.widget.a", issue.RecordKey);
            Assert.Equal("intent_warn_rule", issue.RuleId);
        }

        [Fact]
        public void ValidationIssue_LegacyConstructor_LeavesNewFieldsNull_WithRuleIdCopies()
        {
            var issue = new ValidationIssue(ValidationSeverity.Error, "t", "c", "m", "k", "f");

            Assert.Null(issue.Group);
            Assert.Null(issue.Note);
            Assert.Null(issue.RuleId);

            var stamped = issue.WithRuleId("r1");
            Assert.Equal("r1", stamped.RuleId);
            Assert.Equal("t", stamped.Table);
            Assert.Equal("k", stamped.RecordKey);
            Assert.Equal("f", stamped.Field);
            Assert.Null(issue.RuleId);
            Assert.Equal(issue.ToString(), stamped.ToString());
        }

        [Fact]
        public void ValidationReport_TwoArgConstructor_HasNoRules_AndBlocksWarningsUnderWarningsBlock()
        {
            var issues = new[]
            {
                new ValidationIssue(ValidationSeverity.Warning, "t", "c", "m"),
            };

            var allowed = new ValidationReport(issues, DataRegistryStrictness.WarningsAllowed);
            var block = new ValidationReport(issues, DataRegistryStrictness.WarningsBlock);

            Assert.Empty(allowed.Rules);
            Assert.False(allowed.IsBlocking);
            Assert.True(block.IsBlocking);
            Assert.Equal(0, block.NonEscalatableWarningCount);
        }
    }
}
