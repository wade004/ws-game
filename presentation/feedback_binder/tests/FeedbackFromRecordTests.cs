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

            // ADR-0017 决策 d：未显式提供 sync 字段、event 恰为 combat.damage_dealt 时默认 HitFrame。
            Assert.Equal(FeedbackSyncMode.HitFrame, rule.Sync);
        }

        [Fact]
        public void FeedbackRule_FromRecord_NonDamageEvent_WithoutSyncField_DefaultsToNone()
        {
            var (registry, report) = FeedbackBinderTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["feedback.binding"] = "[" + FeedbackBinderTestSupport.AuraAppliedRuleRow + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("feedback.binding", "feedback.aura_applied_vfx")!;
            var rule = FeedbackRule.FromRecord(record, RulesExprSchema.Base);

            Assert.Equal(FeedbackSyncMode.None, rule.Sync);
        }

        [Fact]
        public void FeedbackRule_FromRecord_ExplicitSyncField_OverridesDefault()
        {
            const string row = @"
            {
              ""id"": ""feedback.explicit_no_sync"",
              ""event"": ""combat.damage_dealt"",
              ""sync"": ""hit_frame"",
              ""actions"": [
                {""kind"": ""flash"", ""params"": {""profile_id"": ""feedback.flash.buff"", ""target"": ""target""}}
              ]
            }";

            var (registry, report) = FeedbackBinderTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["feedback.binding"] = "[" + row + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("feedback.binding", "feedback.explicit_no_sync")!;
            var rule = FeedbackRule.FromRecord(record, RulesExprSchema.Base);

            Assert.Equal(FeedbackSyncMode.HitFrame, rule.Sync);
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
        public void StopVfxAction_FromRecord_ParsesVfxIdAndAttach()
        {
            const string row = @"
            {
              ""id"": ""feedback.stop_stun_vfx"",
              ""event"": ""aura.applied"",
              ""actions"": [
                {""kind"": ""stop_vfx"", ""params"": {""vfx_id"": ""vfx.stun_loop"", ""attach"": ""target""}}
              ]
            }";

            var (registry, report) = FeedbackBinderTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["feedback.binding"] = "[" + row + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("feedback.binding", "feedback.stop_stun_vfx")!;
            var rule = FeedbackRule.FromRecord(record, RulesExprSchema.Base);

            var stopVfx = Assert.IsType<StopVfxAction>(Assert.Single(rule.Actions));
            Assert.Equal(new Id("vfx.stun_loop"), stopVfx.VfxId);
            Assert.Null(stopVfx.FromDisplay);
            Assert.Equal(FeedbackAttachTarget.Target, stopVfx.Attach);
        }

        [Fact]
        public void StopVfxAction_Constructor_RejectsWorldAttach()
        {
            var ex = Assert.Throws<System.ArgumentException>(
                () => new StopVfxAction(new Id("vfx.stun_loop"), null, FeedbackAttachTarget.World));
            Assert.Contains("world", ex.Message);
        }

        [Fact]
        public void StopVfxAction_Constructor_RequiresExactlyOneOf_VfxIdOrFromDisplay()
        {
            Assert.Throws<System.ArgumentException>(
                () => new StopVfxAction(null, null, FeedbackAttachTarget.Target));
            Assert.Throws<System.ArgumentException>(
                () => new StopVfxAction(new Id("vfx.stun_loop"), FromDisplaySource.Skill, FeedbackAttachTarget.Target));
        }

        [Fact]
        public void PlaySfxAction_FromRecord_ParsesAttach_DefaultsToWorldWhenOmitted()
        {
            // ADR-0089：不声明 attach 时缺省 world，与本字段新增前的既有行为一致。
            const string row = @"
            {
              ""id"": ""feedback.play_sfx_default_attach"",
              ""event"": ""aura.applied"",
              ""actions"": [
                {""kind"": ""play_sfx"", ""params"": {""sfx_id"": ""sfx.buff_apply""}}
              ]
            }";

            var (registry, report) = FeedbackBinderTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["feedback.binding"] = "[" + row + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("feedback.binding", "feedback.play_sfx_default_attach")!;
            var rule = FeedbackRule.FromRecord(record, RulesExprSchema.Base);

            var playSfx = Assert.IsType<PlaySfxAction>(Assert.Single(rule.Actions));
            Assert.Equal(FeedbackAttachTarget.World, playSfx.Attach);
        }

        [Fact]
        public void PlaySfxAction_FromRecord_ParsesExplicitAttach()
        {
            const string row = @"
            {
              ""id"": ""feedback.play_sfx_source_attach"",
              ""event"": ""aura.applied"",
              ""actions"": [
                {""kind"": ""play_sfx"", ""params"": {""sfx_id"": ""sfx.buff_hum"", ""attach"": ""source""}}
              ]
            }";

            var (registry, report) = FeedbackBinderTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["feedback.binding"] = "[" + row + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("feedback.binding", "feedback.play_sfx_source_attach")!;
            var rule = FeedbackRule.FromRecord(record, RulesExprSchema.Base);

            var playSfx = Assert.IsType<PlaySfxAction>(Assert.Single(rule.Actions));
            Assert.Equal(FeedbackAttachTarget.Source, playSfx.Attach);
        }

        [Fact]
        public void StopSfxAction_FromRecord_ParsesSfxIdAndAttach()
        {
            const string row = @"
            {
              ""id"": ""feedback.stop_buff_hum"",
              ""event"": ""aura.applied"",
              ""actions"": [
                {""kind"": ""stop_sfx"", ""params"": {""sfx_id"": ""sfx.buff_hum"", ""attach"": ""source""}}
              ]
            }";

            var (registry, report) = FeedbackBinderTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["feedback.binding"] = "[" + row + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("feedback.binding", "feedback.stop_buff_hum")!;
            var rule = FeedbackRule.FromRecord(record, RulesExprSchema.Base);

            var stopSfx = Assert.IsType<StopSfxAction>(Assert.Single(rule.Actions));
            Assert.Equal(new Id("sfx.buff_hum"), stopSfx.SfxId);
            Assert.Null(stopSfx.FromDisplay);
            Assert.Equal(FeedbackAttachTarget.Source, stopSfx.Attach);
        }

        [Fact]
        public void StopSfxAction_Constructor_RejectsWorldAttach()
        {
            var ex = Assert.Throws<System.ArgumentException>(
                () => new StopSfxAction(new Id("sfx.buff_hum"), null, FeedbackAttachTarget.World));
            Assert.Contains("world", ex.Message);
        }

        [Fact]
        public void StopSfxAction_Constructor_RequiresExactlyOneOf_SfxIdOrFromDisplay()
        {
            Assert.Throws<System.ArgumentException>(
                () => new StopSfxAction(null, null, FeedbackAttachTarget.Target));
            Assert.Throws<System.ArgumentException>(
                () => new StopSfxAction(new Id("sfx.buff_hum"), FromDisplaySource.Skill, FeedbackAttachTarget.Target));
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
