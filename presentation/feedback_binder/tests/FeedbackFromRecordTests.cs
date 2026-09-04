using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.ExprHost;
using Presentation.FeedbackBinder.Contracts;
using Xunit;

namespace Tests.Presentation.FeedbackBinder
{
    public class FeedbackFromRecordTests
    {
        [Fact]
        public void FeedbackRule_FromRecord_ParsesConditionAndTwoActions()
        {
            var (registry, report) = FeedbackBinderTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["feedback.binding"] = "[" + FeedbackBinderTestSupport.CritDamageRuleRow + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("feedback.binding", "feedback.crit_damage_text")!;
            var rule = FeedbackRule.FromRecord(record, RulesExprSchema.Base);

            Assert.Equal(new Id("feedback.crit_damage_text"), rule.Id);
            Assert.Equal(new Id("combat.damage_dealt"), rule.EventKey);
            Assert.NotNull(rule.Condition);
            Assert.Equal(2, rule.Actions.Count);

            var floatingText = Assert.IsType<FloatingTextAction>(rule.Actions[0]);
            Assert.Equal(new Id("feedback.style.crit"), floatingText.StyleId);
            Assert.Equal(TextSourceKind.Amount, floatingText.TextSource.Kind);

            var shake = Assert.IsType<ShakeCameraAction>(rule.Actions[1]);
            Assert.Equal(new Id("feedback.shake.crit"), shake.ProfileId);
        }

        [Fact]
        public void FeedbackRule_FromRecord_ParsesRuleWithoutCondition_AndFourActionKinds()
        {
            var (registry, report) = FeedbackBinderTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["feedback.binding"] = "[" + FeedbackBinderTestSupport.AuraAppliedRuleRow + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("feedback.binding", "feedback.aura_applied_vfx")!;
            var rule = FeedbackRule.FromRecord(record, RulesExprSchema.Base);

            Assert.Null(rule.Condition);
            Assert.Equal(4, rule.Actions.Count);

            var playVfx = Assert.IsType<PlayVfxAction>(rule.Actions[0]);
            Assert.Null(playVfx.VfxId);
            Assert.Equal(FromDisplaySource.Skill, playVfx.FromDisplay);
            Assert.Equal(FeedbackAttachTarget.Target, playVfx.Attach);

            var playSfx = Assert.IsType<PlaySfxAction>(rule.Actions[1]);
            Assert.Equal(new Id("sfx.buff_apply"), playSfx.SfxId);

            var flash = Assert.IsType<FlashAction>(rule.Actions[2]);
            Assert.Equal(FeedbackAttachTarget.Target, flash.Target);

            var freeze = Assert.IsType<FreezeAction>(rule.Actions[3]);
            Assert.Equal(40.0, freeze.DurationMs);
        }

        [Fact]
        public void FloatingTextStyleDef_FromRecord_ParsesAllFields()
        {
            var (registry, report) = FeedbackBinderTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["feedback.floating_text_style"] = "[" + FeedbackBinderTestSupport.CritStyleRow + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("feedback.floating_text_style", "feedback.style.crit")!;
            var style = FloatingTextStyleDef.FromRecord(record);

            Assert.Equal(new Id("color.crit_yellow"), style.ColorRef);
            Assert.Equal(1.4, style.SizeScale);
            Assert.Equal(new Id("motion.pop_up"), style.MotionProfile);
        }

        [Fact]
        public void TextSource_Parse_HandlesAllThreeForms()
        {
            Assert.Equal(TextSourceKind.Amount, TextSource.Parse("amount").Kind);

            var field = TextSource.Parse("field:reasonCode");
            Assert.Equal(TextSourceKind.Field, field.Kind);
            Assert.Equal("reasonCode", field.FieldName);

            var literal = TextSource.Parse("literal:l10n.combat.dodge");
            Assert.Equal(TextSourceKind.Literal, literal.Kind);
            Assert.Equal(new Id("l10n.combat.dodge"), literal.TextKey);
        }
    }
}
