using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.ExprHost;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Schema;
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

        // ------------------------------------------------------------------
        // T-M14 / D24（ADR-0125）：FromRecord 错误路径。这里直接用 DataRecord 构造函数喂"绕过校验"的坏行
        // （同 CurveSchemaTests.ReadBreakpoints_* 手法），覆盖 11 章 §4 只读分析入口会走到的运行期解析分支。
        // ------------------------------------------------------------------

        private const string RuleHead = "{\"id\":\"feedback.t\",\"event\":\"combat.damage_dealt\",";

        private static DataRecord BadRecord(string rowJson, string key = "feedback.t")
        {
            var raw = (JsonObject)JsonReader.Parse(rowJson);
            return new DataRecord(FeedbackSchemas.Binding, key, null, raw);
        }

        private static FeedbackRule ParseRule(string rowJson) =>
            FeedbackRule.FromRecord(BadRecord(rowJson), RulesExprSchema.Base);

        private static string OneAction(string actionJson) => RuleHead + "\"actions\":[" + actionJson + "]}";

        /// <summary>外层字段（id/event/actions/sync）缺失、类型不符、非法值：抛 DataFieldException，
        /// Field 是出错的顶层字段名。</summary>
        [Theory]
        [InlineData("{\"event\":\"combat.damage_dealt\",\"actions\":[]}", "id")]
        [InlineData("{\"id\":42,\"event\":\"combat.damage_dealt\",\"actions\":[]}", "id")]
        [InlineData("{\"id\":\"feedback.t\",\"actions\":[]}", "event")]
        [InlineData("{\"id\":\"feedback.t\",\"event\":\"Bad Event!\",\"actions\":[]}", "event")]
        [InlineData("{\"id\":\"feedback.t\",\"event\":\"combat.damage_dealt\"}", "actions")]
        [InlineData("{\"id\":\"feedback.t\",\"event\":\"combat.damage_dealt\",\"actions\":\"x\"}", "actions")]
        [InlineData("{\"id\":\"feedback.t\",\"event\":\"combat.damage_dealt\",\"actions\":[],\"sync\":\"later\"}", "sync")]
        public void FeedbackRule_FromRecord_InvalidTopLevelField_ThrowsDataFieldException_WithFieldName(string row, string expectedField)
        {
            var ex = Assert.Throws<DataFieldException>(() => ParseRule(row));

            Assert.Equal(expectedField, ex.Field);
            Assert.Equal("feedback.binding", ex.Table);
        }

        /// <summary>actions 元素级错误：Field 固定为 "actions"，消息带元素下标与出错的 params 字段名。</summary>
        [Theory]
        [InlineData("5", "第 0 个元素不是对象")]
        [InlineData("{\"params\":{}}", "kind")]
        [InlineData("{\"kind\":7,\"params\":{}}", "kind")]
        [InlineData("{\"kind\":\"explode\",\"params\":{}}", "kind 非法")]
        [InlineData("{\"kind\":\"floating_text\",\"params\":{\"text_source\":\"amount\"}}", "style_id")]
        [InlineData("{\"kind\":\"floating_text\",\"params\":{\"style_id\":\"Bad Id\",\"text_source\":\"amount\"}}", "style_id")]
        [InlineData("{\"kind\":\"floating_text\",\"params\":{\"style_id\":\"feedback.style.crit\"}}", "text_source")]
        [InlineData("{\"kind\":\"floating_text\",\"params\":{\"style_id\":\"feedback.style.crit\",\"text_source\":9}}", "text_source")]
        [InlineData("{\"kind\":\"play_vfx\",\"params\":{\"vfx_id\":\"vfx.a\"}}", "attach")]
        [InlineData("{\"kind\":\"play_vfx\",\"params\":{\"vfx_id\":\"vfx.a\",\"attach\":\"sideways\"}}", "attach")]
        [InlineData("{\"kind\":\"play_vfx\",\"params\":{\"vfx_id\":\"vfx.a\",\"attach\":3}}", "attach")]
        [InlineData("{\"kind\":\"stop_vfx\",\"params\":{\"vfx_id\":\"vfx.a\"}}", "attach")]
        [InlineData("{\"kind\":\"stop_sfx\",\"params\":{\"sfx_id\":\"sfx.a\"}}", "attach")]
        [InlineData("{\"kind\":\"freeze\",\"params\":{}}", "duration_ms")]
        [InlineData("{\"kind\":\"freeze\",\"params\":{\"duration_ms\":\"40\"}}", "duration_ms")]
        [InlineData("{\"kind\":\"shake_camera\",\"params\":{}}", "profile_id")]
        [InlineData("{\"kind\":\"flash\",\"params\":{\"target\":\"target\"}}", "profile_id")]
        [InlineData("{\"kind\":\"flash\",\"params\":{\"profile_id\":\"feedback.flash.a\"}}", "target")]
        public void FeedbackRule_FromRecord_InvalidActionElement_ThrowsDataFieldException_NamingTheField(string actionJson, string expectedMessagePart)
        {
            var ex = Assert.Throws<DataFieldException>(() => ParseRule(OneAction(actionJson)));

            Assert.Equal("actions", ex.Field);
            Assert.Contains(expectedMessagePart, ex.Message);
        }

        /// <summary>D24 复现：可选 Id/枚举字段遇到非法字符串或类型不符，此前静默变 null（再由动作构造函数以
        /// ArgumentException 拒绝或直接放过）；现在一律 DataFieldException，消息带 params 字段名。</summary>
        [Theory]
        [InlineData("play_vfx", "{\"from_display\":\"bogus\",\"attach\":\"target\"}", "from_display")]
        [InlineData("play_vfx", "{\"vfx_id\":\"Bad Id!\",\"from_display\":\"skill\",\"attach\":\"target\"}", "vfx_id")]
        [InlineData("play_vfx", "{\"from_display\":\"skill\",\"attach\":\"target\",\"anchor_id\":\"Bad Id!\"}", "anchor_id")]
        [InlineData("play_vfx", "{\"vfx_id\":99,\"attach\":\"target\"}", "vfx_id")]
        [InlineData("play_sfx", "{\"sfx_id\":\"sfx.a\",\"attach\":\"bogus\"}", "attach")]
        [InlineData("play_sfx", "{\"sfx_id\":\"sfx.a\",\"attach\":7}", "attach")]
        [InlineData("play_sfx", "{\"sfx_id\":\"Bad Id!\"}", "sfx_id")]
        [InlineData("play_sfx", "{\"from_display\":\"bogus\"}", "from_display")]
        [InlineData("stop_vfx", "{\"from_display\":\"bogus\",\"attach\":\"target\"}", "from_display")]
        [InlineData("stop_vfx", "{\"vfx_id\":\"Bad Id!\",\"attach\":\"target\"}", "vfx_id")]
        [InlineData("stop_sfx", "{\"from_display\":\"bogus\",\"attach\":\"source\"}", "from_display")]
        [InlineData("stop_sfx", "{\"sfx_id\":\"Bad Id!\",\"attach\":\"source\"}", "sfx_id")]
        public void FeedbackRule_FromRecord_OptionalFieldWithInvalidValue_ThrowsDataFieldException_InsteadOfSilentNull(
            string kind, string paramsJson, string expectedFieldName)
        {
            var action = "{\"kind\":\"" + kind + "\",\"params\":" + paramsJson + "}";

            var ex = Assert.Throws<DataFieldException>(() => ParseRule(OneAction(action)));

            Assert.Equal("actions", ex.Field);
            Assert.Contains("\"" + expectedFieldName + "\"", ex.Message);
        }

        /// <summary>D24 正向对照：可选字段缺省、显式 null、大小写不同的合法枚举仍正常解析（不误伤）。</summary>
        [Fact]
        public void FeedbackRule_FromRecord_OptionalFields_AbsentNullOrCaseInsensitive_StillParse()
        {
            var rule = ParseRule(RuleHead + "\"actions\":[" +
                "{\"kind\":\"play_vfx\",\"params\":{\"vfx_id\":null,\"from_display\":\"Skill\",\"attach\":\"TARGET\",\"anchor_id\":null}}," +
                "{\"kind\":\"play_sfx\",\"params\":{\"sfx_id\":\"sfx.a\",\"attach\":null}}," +
                "{\"kind\":\"play_sfx\",\"params\":{\"sfx_id\":\"sfx.b\"}}" +
                "]}");

            var vfx = Assert.IsType<PlayVfxAction>(rule.Actions[0]);
            Assert.Null(vfx.VfxId);
            Assert.Equal(FromDisplaySource.Skill, vfx.FromDisplay);
            Assert.Equal(FeedbackAttachTarget.Target, vfx.Attach);
            Assert.Null(vfx.AnchorId);
            Assert.Equal(FeedbackAttachTarget.World, Assert.IsType<PlaySfxAction>(rule.Actions[1]).Attach);
            Assert.Equal(FeedbackAttachTarget.World, Assert.IsType<PlaySfxAction>(rule.Actions[2]).Attach);
        }

        /// <summary>数字字符串（如 "99"）能被 Enum.TryParse 接受但不是命名成员：按非法枚举处理，不得产出越界枚举值。</summary>
        [Theory]
        [InlineData("{\"kind\":\"play_sfx\",\"params\":{\"sfx_id\":\"sfx.a\",\"attach\":\"99\"}}")]
        [InlineData("{\"kind\":\"play_vfx\",\"params\":{\"vfx_id\":\"vfx.a\",\"attach\":\"99\"}}")]
        public void FeedbackRule_FromRecord_NumericEnumString_IsRejectedAsIllegalEnum(string actionJson)
        {
            var ex = Assert.Throws<DataFieldException>(() => ParseRule(OneAction(actionJson)));

            Assert.Equal("actions", ex.Field);
            Assert.Contains("attach", ex.Message);
        }

        /// <summary>现状钉住：通过字段层解析、但被动作构造函数的不变量拒绝的行（二选一守卫、world 附着、负时长、
        /// text_source 无法解析）从 FromRecord 抛出的是构造函数的 ArgumentException 家族，不是
        /// DataFieldException（是否统一包装待设计层确认，见汇报）。</summary>
        [Theory]
        [InlineData("{\"kind\":\"play_vfx\",\"params\":{\"attach\":\"target\"}}")]
        [InlineData("{\"kind\":\"play_vfx\",\"params\":{\"vfx_id\":\"vfx.a\",\"from_display\":\"skill\",\"attach\":\"target\"}}")]
        [InlineData("{\"kind\":\"stop_vfx\",\"params\":{\"vfx_id\":\"vfx.a\",\"attach\":\"world\"}}")]
        [InlineData("{\"kind\":\"stop_sfx\",\"params\":{\"sfx_id\":\"sfx.a\",\"attach\":\"world\"}}")]
        [InlineData("{\"kind\":\"freeze\",\"params\":{\"duration_ms\":-1}}")]
        [InlineData("{\"kind\":\"flash\",\"params\":{\"profile_id\":\"feedback.flash.a\",\"target\":\"world\"}}")]
        [InlineData("{\"kind\":\"floating_text\",\"params\":{\"style_id\":\"feedback.style.crit\",\"text_source\":\"nonsense\"}}")]
        public void FeedbackRule_FromRecord_ConstructorInvariantViolations_SurfaceAsArgumentException(string actionJson)
        {
            var ex = Assert.ThrowsAny<System.ArgumentException>(() => ParseRule(OneAction(actionJson)));

            Assert.IsNotType<DataFieldException>(ex);
        }

        [Fact]
        public void FloatingTextStyleDef_FromRecord_MissingOrInvalidColorRef_ThrowsDataFieldException()
        {
            DataRecord Style(string json) => new DataRecord(
                FeedbackSchemas.FloatingTextStyle, "feedback.style.t", null, (JsonObject)JsonReader.Parse(json));

            var missing = Assert.Throws<DataFieldException>(() => FloatingTextStyleDef.FromRecord(Style("{\"id\":\"feedback.style.t\"}")));
            Assert.Equal("color_ref", missing.Field);

            var wrongType = Assert.Throws<DataFieldException>(() => FloatingTextStyleDef.FromRecord(Style("{\"id\":\"feedback.style.t\",\"color_ref\":5}")));
            Assert.Equal("color_ref", wrongType.Field);

            var badId = Assert.Throws<DataFieldException>(() => FloatingTextStyleDef.FromRecord(Style("{\"id\":\"feedback.style.t\",\"color_ref\":\"Bad Id\"}")));
            Assert.Equal("color_ref", badId.Field);

            var noId = Assert.Throws<DataFieldException>(() => FloatingTextStyleDef.FromRecord(Style("{\"color_ref\":\"color.a\"}")));
            Assert.Equal("id", noId.Field);
        }

        [Theory]
        [InlineData("")]
        [InlineData("nonsense")]
        [InlineData("field")]
        public void TextSource_Parse_EmptyOrUnknownForm_ThrowsArgumentException(string text)
        {
            var ex = Assert.Throws<System.ArgumentException>(() => TextSource.Parse(text));

            Assert.Equal("text", ex.ParamName);
        }

        [Fact]
        public void TextSource_Parse_Null_ThrowsArgumentException_AndFieldFactoryRejectsNull()
        {
            Assert.Throws<System.ArgumentException>(() => TextSource.Parse(null!));
            Assert.Equal("fieldName", Assert.Throws<System.ArgumentNullException>(() => TextSource.Field(null!)).ParamName);
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
