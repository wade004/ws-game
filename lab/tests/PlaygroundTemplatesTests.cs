using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 默认手感模板（ADR-0142）接进人手试玩会话（ADR-0141）：试玩会话的数据根带上模板根与实验室对模板的标定行后，
    /// 运行中切到某套模板预设，攻击方顿帧 tick 取自该模板的数据行，反馈包与镜头震屏档也随之换成该模板的。
    /// 复现：同一局里先后切到 tpl_heavy 与 tpl_agile，顿帧与震屏档必须不同；不变量：每次的数值等于模板数据按规则算出的值，不写裸数。
    /// </summary>
    public sealed class PlaygroundTemplatesTests
    {
        private const string Cell = "2d_action";
        private const string Attack = "input.action.lab_a_attack";
        private const double Frame = 1.0 / 60.0;

        /// <summary>试玩宿主缺省的数据根（<c>LabPlayground</c> 的 <c>extraDataRoots</c>）：手感根、默认手感模板根、实验室动作式根、实验室对模板的标定行。</summary>
        private static readonly string[] PlaygroundRoots =
        {
            "data/_feel", "data/_feel_templates", "data/_lab_action", "lab/fixtures/data/feel_templates",
        };

        private static string PresetId(string style) => "feel.preset.tpl_" + style;

        private static void Frames(LabSession session, int n)
        {
            for (var i = 0; i < n; i++)
            {
                session.Advance(Frame);
            }
        }

        /// <summary>开一局：出木桩、切到模板预设、按一下攻击，返回这一局的手感指纹读数。</summary>
        private static FeelFp PlayWith(string style)
        {
            var runner = LabTestSupport.Runner;
            var script = LabLive.CreateScript("playground_template_" + style, 60, 60, PlaygroundRoots);
            var session = runner.StartLive(script, Cell, null, null);
            Frames(session, 3);
            session.Inject(new ScriptEvent(0, "stake", ScriptEventKind.Spawn, new Vec2(2.0, 0.0)));
            Frames(session, 5);
            session.Inject(new ScriptEvent(0, PresetId(style), ScriptEventKind.Preset));
            session.Inject(new ScriptEvent(0, Attack, ScriptEventKind.Press));
            Frames(session, 2);
            session.Inject(new ScriptEvent(0, Attack, ScriptEventKind.Release));
            Frames(session, 80);
            var recording = session.Finish();
            return new FeelFp(runner.FingerprintOf(session.Script, Cell, recording));
        }

        /// <summary>第一次命中里玩家（攻击方）这一侧的顿帧 tick（条目形如 <c>tick:player:N</c> 或 <c>tick:player+stake:N</c>；这一侧没有顿帧时为 0）。</summary>
        private static int AttackerHitstopTicks(FeelFp fp)
        {
            var started = fp.Items("hitstop.started");
            var item = started.FirstOrDefault(i => i.Split(':')[1].Split('+').Contains("player"));
            return item == null ? 0 : int.Parse(item.Split(':')[2], System.Globalization.CultureInfo.InvariantCulture);
        }

        private static int ExpectedAttackerTicks(string style)
        {
            var p = FeelRules.Preset(PresetId(style));
            return Math.Min(p.Ticks("attacker_hitstop_ms"), p.Ticks("attacker_hitstop_cap_ms"));
        }

        [Fact]
        public void SwitchingTemplatePreset_ChangesAttackerHitstop_AsComputedFromTheTemplateData()
        {
            var heavy = AttackerHitstopTicks(PlayWith("heavy"));
            var agile = AttackerHitstopTicks(PlayWith("agile"));
            Assert.Equal(ExpectedAttackerTicks("heavy"), heavy);
            Assert.Equal(ExpectedAttackerTicks("agile"), agile);
            Assert.NotEqual(heavy, agile);
        }

        [Theory]
        [InlineData("classic")]
        [InlineData("agile")]
        [InlineData("heavy")]
        [InlineData("horde")]
        [InlineData("precise")]
        public void EveryTemplate_IsListedAndPlayable_WithItsOwnHitstop(string style)
        {
            Assert.Equal(ExpectedAttackerTicks(style), AttackerHitstopTicks(PlayWith(style)));
        }

        [Theory]
        [InlineData("agile")]
        [InlineData("heavy")]
        [InlineData("horde")]
        [InlineData("precise")]
        public void SwitchingTemplatePreset_AlsoSwitchesTheImpactProfile_SoTheCameraCueCarriesThatTemplatesShakePreset(string style)
        {
            // 镜头冲击提示的第三段是震屏档 id：来自该模板反馈档案变体里声明的 camera.shake_profile，不是实验室缺省包（缺省包没有震屏档）。
            var cues = PlayWith(style).Items("presentation.camera_cues");
            Assert.NotEmpty(cues);
            Assert.All(cues, c => Assert.StartsWith("feedback.shake.tpl_" + style + "_", c.Split(':')[2], StringComparison.Ordinal));
        }

        [Fact]
        public void TemplateWithoutShakePresets_KeepsTheCameraCueWithoutAShakeProfile()
        {
            var cues = PlayWith("classic").Items("presentation.camera_cues");
            Assert.All(cues, c => Assert.Equal(string.Empty, c.Split(':')[2]));
        }
    }
}
