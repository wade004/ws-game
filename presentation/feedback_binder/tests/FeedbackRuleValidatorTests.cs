using System.Collections.Generic;
using Core.Foundation.Common;
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
    }
}
