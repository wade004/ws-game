using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 覆盖事件的载荷编解码（ADR-0150）：数值/布尔/枚举/文本/引用/列表字段各一条往返；数值事件与改动前逐位相同、不带文本载荷，
    /// 所以既有试玩脚本（含 ADR-0141 的数值覆盖）序列化后逐字节不变；未登记字段、种类不符、非法列表元素都带原因抛出。
    /// </summary>
    public sealed class OverrideCodecTests
    {
        private static readonly FeelFieldSet Fields = FeelFields.Extend(new[]
        {
            new FeelFieldDef(
                "game.tags", FeelFieldKind.List,
                new FeelFieldMeta(FeelHalf.Judging, FeelGroup.Action, FeelOpSet.Set | FeelOpSet.Add | FeelOpSet.Remove, FeelComposition.CharacterPrimary),
                "游戏自有列表字段（测试用）", optional: true),
        });

        private static FeelWrite RoundTrip(FeelWrite write, string actor = "")
        {
            var e = LabOverrideCodec.Encode(Fields, write, actor);
            // 经脚本 JSON 往返再解码：文件里读回来的事件，不是内存里的同一个对象。
            var script = new InputScript(new ScriptMeta { ScriptId = "codec", DurationTicks = 1 }, new List<ScriptEvent> { e });
            var back = InputScript.Parse(script.ToJson()).Events[0];
            Assert.Equal(actor, back.Actor);
            return LabOverrideCodec.Decode(Fields, back);
        }

        [Fact]
        public void EveryFieldKind_RoundTripsThroughTheScriptFile()
        {
            Assert.Equal(new FeelWrite("attacker_hitstop_ms", FeelOp.Set, FeelValue.Of(123.5)), RoundTrip(new FeelWrite("attacker_hitstop_ms", FeelOp.Set, FeelValue.Of(123.5))));
            Assert.Equal(new FeelWrite("turn_rate_deg_s", FeelOp.Multiply, FeelValue.Of(1.5)), RoundTrip(new FeelWrite("turn_rate_deg_s", FeelOp.Multiply, FeelValue.Of(1.5)), "player"));
            Assert.Equal(new FeelWrite("stop_distance", FeelOp.Add, FeelValue.Of(-0.25)), RoundTrip(new FeelWrite("stop_distance", FeelOp.Add, FeelValue.Of(-0.25))));
            Assert.Equal(new FeelWrite("action_turn_lock", FeelOp.Set, FeelValue.Of(true)), RoundTrip(new FeelWrite("action_turn_lock", FeelOp.Set, FeelValue.Of(true))));
            Assert.Equal(new FeelWrite("action_turn_lock", FeelOp.Set, FeelValue.Of(false)), RoundTrip(new FeelWrite("action_turn_lock", FeelOp.Set, FeelValue.Of(false))));
            Assert.Equal(new FeelWrite("reverse_policy", FeelOp.Set, FeelValue.Of("through_zero")), RoundTrip(new FeelWrite("reverse_policy", FeelOp.Set, FeelValue.Of("through_zero"))));
            Assert.Equal(new FeelWrite("sfx_material", FeelOp.Set, FeelValue.Of("metal")), RoundTrip(new FeelWrite("sfx_material", FeelOp.Set, FeelValue.Of("metal"))));
            Assert.Equal(
                new FeelWrite(FeelFieldNames.ImpactProfileRef, FeelOp.Set, FeelValue.Of("feedback.impact_profile.x")),
                RoundTrip(new FeelWrite(FeelFieldNames.ImpactProfileRef, FeelOp.Set, FeelValue.Of("feedback.impact_profile.x"))));
            Assert.Equal(
                new FeelWrite("game.tags", FeelOp.Set, FeelValue.OfList(new[] { "a", "b" })),
                RoundTrip(new FeelWrite("game.tags", FeelOp.Set, FeelValue.OfList(new[] { "a", "b" }))));
            Assert.Equal(new FeelWrite("game.tags", FeelOp.Add, FeelValue.Of("c")), RoundTrip(new FeelWrite("game.tags", FeelOp.Add, FeelValue.Of("c"))));
            Assert.Equal(new FeelWrite("game.tags", FeelOp.Remove, FeelValue.Of("a")), RoundTrip(new FeelWrite("game.tags", FeelOp.Remove, FeelValue.Of("a"))));
        }

        [Fact]
        public void NumericOverrideEvents_AreByteIdenticalToTheOldFormat_NoTextPayload()
        {
            var e = LabOverrideCodec.Encode(Fields, new FeelWrite("attacker_hitstop_ms", FeelOp.Set, FeelValue.Of(222.0)), "player");
            var old = new ScriptEvent(0, "attacker_hitstop_ms", ScriptEventKind.Override, new Vec2(222.0, 0.0), null, "player");
            Assert.Equal(string.Empty, e.Text);
            var a = new InputScript(new ScriptMeta { ScriptId = "x", DurationTicks = 1 }, new List<ScriptEvent> { e }).ToJson();
            var b = new InputScript(new ScriptMeta { ScriptId = "x", DurationTicks = 1 }, new List<ScriptEvent> { old }).ToJson();
            Assert.Equal(b, a);
            Assert.DoesNotContain("\"text\"", a);
            // 带文本载荷的事件才序列化 text。
            var t = LabOverrideCodec.Encode(Fields, new FeelWrite("reverse_policy", FeelOp.Set, FeelValue.Of("instant")), string.Empty);
            Assert.Contains("\"text\": \"instant\"", new InputScript(new ScriptMeta { ScriptId = "x", DurationTicks = 1 }, new List<ScriptEvent> { t }).ToJson());
        }

        [Fact]
        public void BadWrites_AreRejectedWithAReason()
        {
            Assert.Throws<LabFormatException>(() => LabOverrideCodec.Encode(Fields, new FeelWrite("no_such", FeelOp.Set, FeelValue.Of(1.0)), string.Empty));
            Assert.Throws<LabFormatException>(() => LabOverrideCodec.Encode(Fields, new FeelWrite("attacker_hitstop_ms", FeelOp.Set, FeelValue.Of("x")), string.Empty));
            Assert.Throws<LabFormatException>(() => LabOverrideCodec.Encode(Fields, new FeelWrite("game.tags", FeelOp.Add, FeelValue.Of("a,b")), string.Empty));
            Assert.Throws<LabFormatException>(() => LabOverrideCodec.Encode(Fields, new FeelWrite("game.tags", FeelOp.Add, FeelValue.Of("")), string.Empty));
            var missingText = new ScriptEvent(0, "reverse_policy", ScriptEventKind.Override, default, null, string.Empty);
            Assert.Throws<LabFormatException>(() => LabOverrideCodec.Decode(Fields, missingText));
        }
    }
}
