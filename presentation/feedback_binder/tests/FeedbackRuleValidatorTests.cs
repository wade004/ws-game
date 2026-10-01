using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;
using Presentation.FeedbackBinder.Contracts;
using FeedbackRuleValidator = Presentation.FeedbackBinder.Core.FeedbackRuleValidator;
using Xunit;

namespace Tests.Presentation.FeedbackBinder
{
    public class FeedbackRuleValidatorTests
    {
        private static readonly IReadOnlyCollection<Id> KnownEvents = new[]
        {
            RulesEventKeys.CombatDamageDealt,
            RulesEventKeys.AuraApplied,
        };

        private static FeedbackRule Rule(string id, Id eventKey) =>
            new FeedbackRule(new Id(id), eventKey, null, new List<FeedbackAction>
            {
                new FreezeAction(10),
            });

        [Fact]
        public void Validate_AllRulesValid_ReturnsNoIssues()
        {
            var rules = new List<FeedbackRule>
            {
                Rule("feedback.rule_a", RulesEventKeys.CombatDamageDealt),
                Rule("feedback.rule_b", RulesEventKeys.AuraApplied),
            };

            var issues = FeedbackRuleValidator.Validate(rules, KnownEvents);

            Assert.Empty(issues);
        }

        [Fact]
        public void Validate_UnregisteredEvent_ReportsError()
        {
            var rules = new List<FeedbackRule> { Rule("feedback.rule_a", new Id("combat.unregistered_event")) };

            var issues = FeedbackRuleValidator.Validate(rules, KnownEvents);

            Assert.Contains(issues, i => i.Check == FeedbackRuleValidator.CheckEventRegistered);
        }

        [Fact]
        public void Validate_DuplicateId_ReportsError()
        {
            var rules = new List<FeedbackRule>
            {
                Rule("feedback.rule_a", RulesEventKeys.CombatDamageDealt),
                Rule("feedback.rule_a", RulesEventKeys.AuraApplied),
            };

            var issues = FeedbackRuleValidator.Validate(rules, KnownEvents);

            Assert.Contains(issues, i => i.Check == FeedbackRuleValidator.CheckDuplicateId);
        }

        [Fact]
        public void Validate_WrongDomain_ReportsError()
        {
            var rules = new List<FeedbackRule> { Rule("vfx.not_a_feedback_id", RulesEventKeys.CombatDamageDealt) };

            var issues = FeedbackRuleValidator.Validate(rules, KnownEvents);

            Assert.Contains(issues, i => i.Check == FeedbackRuleValidator.CheckIdDomain);
        }

        // -----------------------------------------------------------------
        // T-M35（测试覆盖剩余项第四批）：同规则多问题 / 空集合 / knownEventKeys 为空 / 问题字段
        // -----------------------------------------------------------------

        [Fact]
        public void Validate_EmptyRuleSet_ReturnsNoIssues()
        {
            Assert.Empty(FeedbackRuleValidator.Validate(new List<FeedbackRule>(), KnownEvents));
            Assert.Empty(FeedbackRuleValidator.Validate(new List<FeedbackRule>(), new List<Id>()));
        }

        [Fact]
        public void Validate_EmptyKnownEventKeys_FlagsEveryRuleAsUnregistered()
        {
            var rules = new List<FeedbackRule>
            {
                Rule("feedback.rule_a", RulesEventKeys.CombatDamageDealt),
                Rule("feedback.rule_b", RulesEventKeys.AuraApplied),
            };

            var issues = FeedbackRuleValidator.Validate(rules, new List<Id>());

            Assert.Equal(rules.Count, issues.Count);
            Assert.All(issues, i => Assert.Equal(FeedbackRuleValidator.CheckEventRegistered, i.Check));
        }

        [Fact]
        public void Validate_RuleWithAllThreeProblems_ReportsAllChecks_InDomainDuplicateEventOrder()
        {
            // 同一非法 id 出现两次：第一次 domain 错 + event 未登记；第二次多出 duplicate。
            var badId = "vfx.bad_rule";
            var unknownEvent = new Id("combat.never_registered");
            var rules = new List<FeedbackRule> { Rule(badId, unknownEvent), Rule(badId, unknownEvent) };

            var issues = FeedbackRuleValidator.Validate(rules, KnownEvents);

            var checks = new List<string>();
            foreach (var issue in issues)
            {
                checks.Add(issue.Check);
            }
            Assert.Equal(new[]
            {
                FeedbackRuleValidator.CheckIdDomain, FeedbackRuleValidator.CheckEventRegistered,
                FeedbackRuleValidator.CheckIdDomain, FeedbackRuleValidator.CheckDuplicateId, FeedbackRuleValidator.CheckEventRegistered,
            }, checks);
        }

        [Fact]
        public void Validate_Issues_AreErrors_OnTheBindingTable_WithRecordKey_AndEventFieldForEventCheck()
        {
            var rules = new List<FeedbackRule>
            {
                Rule("vfx.bad_rule", new Id("combat.never_registered")),
            };

            var issues = FeedbackRuleValidator.Validate(rules, KnownEvents);

            Assert.Equal(2, issues.Count);
            Assert.All(issues, i =>
            {
                Assert.Equal(ValidationSeverity.Error, i.Severity);
                Assert.Equal("feedback.binding", i.Table);
                Assert.Equal(rules[0].Id.ToString(), i.RecordKey);
            });
            var eventIssue = Assert.Single(issues, i => i.Check == FeedbackRuleValidator.CheckEventRegistered);
            Assert.Equal("event", eventIssue.Field);
        }

        [Fact]
        public void Validate_DuplicateIdThreeTimes_ReportsTwoDuplicates_FirstOccurrenceNotFlagged()
        {
            var rules = new List<FeedbackRule>
            {
                Rule("feedback.rule_a", RulesEventKeys.CombatDamageDealt),
                Rule("feedback.rule_a", RulesEventKeys.CombatDamageDealt),
                Rule("feedback.rule_a", RulesEventKeys.CombatDamageDealt),
            };

            var issues = FeedbackRuleValidator.Validate(rules, KnownEvents);

            Assert.Equal(rules.Count - 1, issues.Count);
            Assert.All(issues, i => Assert.Equal(FeedbackRuleValidator.CheckDuplicateId, i.Check));
        }

        [Fact]
        public void Validate_KnownEventKeysWithDuplicates_StillAccepted()
        {
            var rules = new List<FeedbackRule> { Rule("feedback.rule_a", RulesEventKeys.CombatDamageDealt) };
            var withDuplicates = new List<Id> { RulesEventKeys.CombatDamageDealt, RulesEventKeys.CombatDamageDealt };

            Assert.Empty(FeedbackRuleValidator.Validate(rules, withDuplicates));
        }
    }
}
