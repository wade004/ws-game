using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Rules.Combat;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 手感场景脚本（S7b）的运行期验收：每个新脚本的实测值等于由"预设字段 × 毫秒换算 × 受击裁决规则"在用例里算出的期望值
    /// （不写死裸数），并覆盖基线、双跑一致、帧率上限不变、表现组按格子分别有基线。
    /// 记号：动作式格子 = <c>2d_action</c>（缺省预设 arpg_responsive）；目标选择式格子 = <c>2d_targeted</c>（rpg_classic，timeline 被剥掉）。
    /// </summary>
    public sealed class FeelSceneTests
    {
        private const string Action = "2d_action";
        private const string Targeted = "2d_targeted";
        private const string AttackAction = "input.action.lab_a_attack";
        private const string DodgeAction = "input.action.lab_a_dodge";
        private const string SkillAction = "input.action.lab_a_skill";
        private const string ChargeAction = "input.action.lab_a_charge";

        // ---------- 辅助 ----------

        private static int T(double ms) => FeelRules.T(ms);

        private static List<ScriptEvent> Events(string script, string action, ScriptEventKind kind)
        {
            var list = new List<ScriptEvent>();
            foreach (var e in LabTestSupport.Script(script).Events)
            {
                if (e.Action == action && e.Kind == kind)
                {
                    list.Add(e);
                }
            }

            return list;
        }

        private static int Press(string script, string action, int nth = 0) => Events(script, action, ScriptEventKind.Press)[nth].Tick;

        private static int Release(string script, string action, int nth = 0) => Events(script, action, ScriptEventKind.Release)[nth].Tick;

        /// <summary>预设里"冲击等级 → 受击反应"的映射（生产缺省表）。</summary>
        private static string ReactionFor(string impactClass) => new HitFeelOptions().ImpactReactions[impactClass].ToString();

        /// <summary>攻击方顿帧 tick：<c>min(T(攻击方 ms × 击杀倍率), T(攻击方上限))</c>（击杀才乘倍率）。</summary>
        private static int AttackerHitstop(double attackerMs, double killScale, double capMs, bool kill) =>
            Math.Min(T(kill ? attackerMs * killScale : attackerMs), T(capMs));

        private static double CalibrationBaseSpeed(string cell)
        {
            var preset = LabTestSupport.Runner.Dataset.Catalog.GetScenario(cell).DefaultPreset;
            var id = "feel.calibration.lab_" + preset.Substring("feel.preset.".Length);
            var row = FeelRules.Row(System.IO.Path.Combine("data", "_lab_action", "feel", "feel.calibration.json"), id);
            return ((JsonNumber)row["base_speed"]).Value;
        }

        /// <summary>格子标定的参考身高（身高倍数 → 世界单位的换算系数）。</summary>
        private static double CalibrationReferenceHeight(string cell)
        {
            var preset = LabTestSupport.Runner.Dataset.Catalog.GetScenario(cell).DefaultPreset;
            var id = "feel.calibration.lab_" + preset.Substring("feel.preset.".Length);
            var row = FeelRules.Row(System.IO.Path.Combine("data", "_lab_action", "feel", "feel.calibration.json"), id);
            return ((JsonNumber)row["reference_height"]).Value;
        }

        // ---------- 基线、双跑、帧率 ----------

        [Fact]
        public void FeelScripts_AreVersion3_RoundTrip_AndRunAt60Hz()
        {
            var scripts = LabTestSupport.FeelScripts();
            Assert.Equal(21, scripts.Count);
            foreach (var s in scripts)
            {
                Assert.Equal(InputScript.FeelFormatVersion, s.EffectiveFormatVersion);
                Assert.Contains("\"formatVersion\": 3", s.ToJson());
                Assert.Equal(s.ToJson(), InputScript.Parse(s.ToJson()).ToJson());
                // 动作时间线按 1/60 秒换算：手感脚本固定 60 Hz（同换装脚本）。
                Assert.Equal(60, s.Meta.TickRate);
                Assert.True(s.Meta.Feel);
            }
        }

        [Fact]
        public void FeelScripts_OnAllSixCells_MatchCommittedBaselines()
        {
            var results = new List<CellResult>();
            foreach (var script in LabTestSupport.FeelScripts())
            {
                results.AddRange(LabSuite.Check(LabTestSupport.Runner, LabTestSupport.FixturesDir, script.Meta.ScriptId));
            }

            Assert.Equal(LabTestSupport.FeelScripts().Count * 6, results.Count);
            var failures = new StringBuilder();
            foreach (var r in results)
            {
                if (r.Status != CellStatus.Pass)
                {
                    failures.Append(r.Script).Append(" @ ").Append(r.Cell).Append(' ').Append(r.Status).Append('\n');
                    if (r.Diff != null)
                    {
                        failures.Append(r.Diff.Format());
                    }
                }
            }

            Assert.True(failures.Length == 0, "基线比较失败：\n" + failures);
        }

        [Fact]
        public void FeelScripts_DoubleRun_IsByteIdentical_IncludingPresentation()
        {
            var runner = LabTestSupport.Runner;
            foreach (var script in LabTestSupport.FeelScripts())
            {
                foreach (var cell in new[] { Action, Targeted })
                {
                    var a = runner.Run(script, cell);
                    var b = runner.Run(script, cell);
                    Assert.Equal(
                        a.Project(runner.Registry, MetricClass.Logic, MetricClass.Presentation),
                        b.Project(runner.Registry, MetricClass.Logic, MetricClass.Presentation));
                }
            }
        }

        [Theory]
        [InlineData("feel_combo3")]
        [InlineData("feel_group_hit")]
        [InlineData("feel_buffer_lead")]
        [InlineData("feel_motion_turn")]
        public void FeelScripts_LogicGroups_AreIdentical_At30_60_120FrameRateCaps(string scriptId)
        {
            var runner = LabTestSupport.Runner;
            var baseScript = LabTestSupport.Script(scriptId);
            var logic = new List<string>();
            foreach (var cap in new[] { 30, 60, 120 })
            {
                var script = LabTestSupport.CloneWithFrameRate(baseScript, cap);
                logic.Add(runner.Run(script, Action).Project(runner.Registry, MetricClass.Logic));
            }

            Assert.Equal(logic[0], logic[1]);
            Assert.Equal(logic[0], logic[2]);
        }

        [Fact]
        public void FeelMetricGroups_AppearOnlyInFeelFingerprints_LegacyFingerprintsAreUntouched()
        {
            var groups = new[] { "inputbuf", "actiontl", "hitstop", "reaction", "motion", "spatialhit", "presentation" };
            foreach (var script in LabTestSupport.StandardScripts())
            {
                var fp = FeelFp.Of(script.Meta.ScriptId, Action);
                foreach (var g in groups)
                {
                    Assert.False(fp.Has(g), $"{script.Meta.ScriptId} 的指纹不应出现手感度量组 {g}");
                }
            }

            foreach (var script in LabTestSupport.FeelScripts())
            {
                foreach (var cell in LabTestSupport.AllCells)
                {
                    var fp = FeelFp.Of(script.Meta.ScriptId, cell);
                    foreach (var g in groups)
                    {
                        Assert.True(fp.Has(g), $"{script.Meta.ScriptId} @ {cell} 的指纹应有手感度量组 {g}");
                    }
                }
            }
        }

        [Fact]
        public void PresentationGroup_IsPerCell_ActionAndTargetedDiffer_PlanarCombosDoNotNeedToAgree()
        {
            // 动作式格子有命中顿帧/镜头/表现冻结，目标选择式（经典预设，无顿帧）没有：表现组必须按格子各存基线，不能跨格子比较。
            var action = FeelFp.Of("feel_melee", Action);
            var targeted = FeelFp.Of("feel_melee", Targeted);
            Assert.NotEqual(action.Text("presentation.freeze_ops"), targeted.Text("presentation.freeze_ops"));
            Assert.NotEqual(action.Text("presentation.camera_cues"), targeted.Text("presentation.camera_cues"));
            // 表现组度量类别是 Presentation（绝对允差），不是 Logic。
            foreach (var group in LabTestSupport.Runner.Registry.Groups)
            {
                if (group.Name != "presentation")
                {
                    continue;
                }

                Assert.All(group.Specs, s => Assert.Equal(MetricClass.Presentation, s.Class));
            }
        }

        // ---------- melee / lunge ----------

        [Fact]
        public void Melee_PhasesHitstopAndReaction_EqualRuleComputedExpectation()
        {
            const string skill = "skill.lab_a_slash";
            var fp = FeelFp.Of("feel_melee", Action);
            var p = FeelRules.ForCell(Action);
            var press = Press("feel_melee", AttackAction);

            var startup = T(FeelRules.TimelineMs(skill, "startup_ms") * p.N("phase_scale.startup"));
            var active = T(FeelRules.TimelineMs(skill, "active_ms") * p.N("phase_scale.active"));
            var recovery = T(FeelRules.TimelineMs(skill, "recovery_ms") * p.N("phase_scale.recovery"));
            var attacker = p.Ticks("attacker_hitstop_ms");
            var target = p.Ticks("target_hitstop_ms");
            var hit = press + T(FeelRules.MarkerMs(skill, "hit"));

            // 输入缓冲：空闲时按下的当 tick 即消费，延迟 0；动作开始 tick = 按下 tick。
            Assert.Equal(new[] { 0.0 }, fp.Nums("inputbuf.accept_latency_ticks"));
            Assert.Equal(new[] { press + ":" + skill }, fp.Items("inputbuf.consume_ticks"));

            // 相位：命中标记落在前摇结束；生效相里叠了攻击方顿帧（动作时钟暂停），所以相位实测 = 时间线 tick + 顿帧 tick。
            Assert.Equal(
                $"player:{skill}:startup={startup}/active={active + attacker}/recovery={recovery}", fp.Items("actiontl.phase_ticks")[0]);
            Assert.Equal(new[] { startup + active + attacker + recovery + 0.0 }, fp.Nums("actiontl.action_total_ticks"));
            Assert.Equal(hit, FeelFp.FirstTickWhere(fp.Items("actiontl.markers"), i => i.EndsWith(":hit", StringComparison.Ordinal)));

            // 命中确认与顿帧：同一 tick 起算，攻击方 / 受击方各自的预设时长；反应 = 冲击等级映射，硬直 = hit_stun_ms。
            Assert.Equal(new[] { $"{hit}:player>stake" }, fp.Items("spatialhit.confirm_ticks"));
            Assert.Equal(new[] { $"{hit}:player:{attacker}", $"{hit}:stake:{target}" }, fp.Items("hitstop.started"));
            Assert.Equal(attacker, fp.Num("hitstop.frozen_ticks_player"));
            Assert.Equal("stake=" + target, fp.Text("hitstop.frozen_ticks_targets"));
            var stun = p.Ticks("hit_stun_ms");
            var reaction = ReactionFor(p.S("impact_class"));
            Assert.Equal(new[] { $"{hit}:stake:{reaction}:{stun}" }, fp.Items("reaction.reactions"));
            Assert.Equal("stake=" + stun, fp.Text("reaction.staggered_ticks"));

            // 取消窗口：打开 / 关闭 tick = 开始 tick + 毫秒换算 + 命中顿帧（窗口在顿帧期间被暂停的动作时钟推迟）。
            var (open, close) = FeelRules.CancelWindowMs(skill, "dodge");
            Assert.Contains($"player@{press + T(open * p.N("cancel_window_scale")) + attacker}:cancel_open:dodge", fp.Items("actiontl.markers"));
            Assert.Contains($"player@{press + T(close * p.N("cancel_window_scale")) + attacker}:cancel_close:dodge", fp.Items("actiontl.markers"));

            Assert.Equal(1.0, fp.Num("spatialhit.hit_confirmed"));
            Assert.Equal(0.0, fp.Num("spatialhit.unconfirmed_damage"));
        }

        [Fact]
        public void Melee_TargetedCell_HitsInstantly_WithoutTimelineHitstopOrReaction()
        {
            // 目标选择式（经典预设、timeline 被剥掉）：按下的当 tick 就结算命中，没有相位、顿帧、受击硬直。
            var fp = FeelFp.Of("feel_melee", Targeted);
            var p = FeelRules.ForCell(Targeted);
            var press = Press("feel_melee", AttackAction);

            Assert.Equal(new[] { $"{press}:player>stake" }, fp.Items("spatialhit.confirm_ticks"));
            Assert.Equal(0, p.N("target_hitstop_ms"));
            Assert.Equal(string.Empty, fp.Text("hitstop.started"));
            Assert.Equal(string.Empty, fp.Text("actiontl.starts"));
            Assert.Equal(string.Empty, fp.Text("reaction.reactions"));
            Assert.Equal("None:1", fp.Text("reaction.reaction_counts"));
        }

        [Fact]
        public void Lunge_TargetAssistTurnsTowardTargetWithinLimit_AndMotionSegmentRunsBeforeHit()
        {
            const string skill = "skill.lab_a_lunge";
            var fp = FeelFp.Of("feel_lunge", Action);
            var p = FeelRules.ForCell(Action);
            var press = Press("feel_lunge", AttackAction);

            // 木桩在玩家正 +y 侧、玩家朝 +x：夹角 90 度，大于预设的转向辅助上限，所以修正量被限幅在上限（度）。
            var assists = fp.Items("actiontl.target_assists");
            Assert.Single(assists);
            Assert.StartsWith($"player@{press}:stake,", assists[0], StringComparison.Ordinal);
            var applied = double.Parse(assists[0].Substring(assists[0].IndexOf(',') + 1), CultureInfo.InvariantCulture);
            Assert.Equal(p.N("turn_assist_deg"), applied);
            Assert.Equal(p.N("turn_assist_deg") * Math.PI / 180.0, fp.Num("motion.facing_final"), 6);

            // 位移段标记先于命中标记（扑上去再打）。
            var markers = fp.Items("actiontl.markers");
            var motionStart = FeelFp.FirstTickWhere(markers, i => i.EndsWith(":motion_start", StringComparison.Ordinal));
            var motionEnd = FeelFp.FirstTickWhere(markers, i => i.EndsWith(":motion_end", StringComparison.Ordinal));
            var hit = FeelFp.FirstTickWhere(markers, i => i.EndsWith(":hit", StringComparison.Ordinal));
            Assert.Equal(press + T(FeelRules.MarkerMs(skill, "motion_start")), motionStart);
            Assert.Equal(press + T(FeelRules.MarkerMs(skill, "hit")), hit);
            Assert.True(motionStart < hit && hit < motionEnd + p.Ticks("attacker_hitstop_ms") + 1);
            Assert.Equal(new[] { $"{hit}:player>stake" }, fp.Items("spatialhit.confirm_ticks"));
        }

        // ---------- dash ----------

        [Fact]
        public void Dash_SpeedIsDistanceOverMotionWindow_EndsExactlyAtTheDeclaredDistance_AndPlaysNoWhiff()
        {
            const string skill = "skill.lab_a_dodge";
            var fp = FeelFp.Of("feel_dash", Action);
            var press = Press("feel_dash", DodgeAction);

            // 声明距离是身高倍数，世界距离 = 倍数 × 标定参考身高。
            var distance = ((JsonNumber)((JsonObject)FeelRules.Timeline(skill)["motion"])["distance"]).Value * CalibrationReferenceHeight(Action);
            var startTick = press + T(FeelRules.MarkerMs(skill, "motion_start"));
            var endTick = press + T(FeelRules.MarkerMs(skill, "motion_end"));
            var segmentTicks = endTick - startTick;

            Assert.Equal(distance / (segmentTicks * FeelRules.StepSeconds), fp.Num("motion.speed_max"), 6);
            var markers = fp.Items("actiontl.markers");
            Assert.Contains($"player@{press + T(FeelRules.MarkerMs(skill, "invuln_start"))}:invuln_start", markers);
            Assert.Contains($"player@{press + T(FeelRules.MarkerMs(skill, "invuln_end"))}:invuln_end", markers);

            // 位移段：来源 Action 恰好 segmentTicks 个 tick。S12：窗口结束时末速度清零（keep_momentum_on_motion_end 缺省假），
            // 后摇里不再滑行，终点 = 声明距离换算后的世界距离。
            Assert.Contains($"Action/Actionx{segmentTicks}", fp.Text("motion.mode_sequence"));
            Assert.DoesNotContain("Action/Regular", fp.Text("motion.mode_sequence"));
            var endX = double.Parse(fp.Text("motion.end_position").Split(',')[0], CultureInfo.InvariantCulture);
            Assert.Equal(distance, endX, 6);

            // 非攻击技能没有命中：没有命中确认、没有顿帧；S12：没有攻击效果也没有命中标记的动作不发挥空提示。
            Assert.Equal(0.0, fp.Num("spatialhit.hit_confirmed"));
            Assert.Equal(string.Empty, fp.Text("hitstop.started"));
            Assert.Equal(string.Empty, fp.Text("presentation.sfx_ids"));
            Assert.Equal(0.0, fp.Num("presentation.sfx_count"));
        }

        // ---------- projectile（含 S11 缺口修复的运行时凭据） ----------

        [Fact]
        public void Projectile_EveryHitIsConfirmedExactlyOnce_ForReleaseMarkerAndHitMarkerOnlySkills()
        {
            var fp = FeelFp.Of("feel_projectile", Action);
            var p = FeelRules.ForCell(Action);
            const string releaseSkill = "skill.lab_a_projectile";
            const string boltSkill = "skill.lab_a_bolt";

            var pressA = Press("feel_projectile", AttackAction);
            var pressB = Press("feel_projectile", SkillAction);
            var releaseTick = pressA + T(FeelRules.MarkerMs(releaseSkill, "release"));
            var boltTick = pressB + T(FeelRules.MarkerMs(boltSkill, "hit"));

            // 两次施放（有 release 标记 / 只有 hit 标记）各造成一次伤害，各恰有一条命中确认（S11：时间线路径此前只确认
            // school_damage / weapon_damage_pct，投射物效果的命中既无确认也无即时适配）。
            Assert.Equal(2.0, fp.Num("attack.damage_events"));
            Assert.Equal(2.0, fp.Num("spatialhit.hit_confirmed"));
            Assert.Equal(0.0, fp.Num("spatialhit.unconfirmed_damage"));
            Assert.Equal(0.0, fp.Num("spatialhit.duplicate_confirmations"));
            Assert.Equal(new[] { "0.0=stake", "1.0=stake" }, fp.Items("spatialhit.hit_sets"));

            // 投射物飞行：命中确认落在发射标记之后，且不晚于"最大射程 / 速度"换算的 tick 数。
            var confirms = fp.Items("spatialhit.confirm_ticks");
            var effect = (JsonObject)((JsonObject)((JsonArray)FeelRules.Skill(releaseSkill)["effects"])[0])["params"];
            var maxFlight = (int)Math.Ceiling(((JsonNumber)effect["max_range"]).Value / ((JsonNumber)effect["speed"]).Value / FeelRules.StepSeconds);
            Assert.InRange(FeelFp.TickOf(confirms[0]), releaseTick + 1, releaseTick + maxFlight);
            Assert.InRange(FeelFp.TickOf(confirms[1]), boltTick + 1, boltTick + maxFlight);

            // 命中带来的顿帧 / 反应与近战同规则。
            Assert.Equal(2 * p.Ticks("attacker_hitstop_ms") + 0.0, fp.Num("hitstop.started_ticks_player"));
            Assert.Equal("Stagger:2".Replace("Stagger", ReactionFor(p.S("impact_class"))), fp.Text("reaction.reaction_counts"));
        }

        [Fact]
        public void Projectile_WhiffWaitsForTheProjectileOutcome_AHitMeansNoWhiffAtAll_AndAMissWhiffsWhenTheProjectileEnds()
        {
            const string releaseSkill = "skill.lab_a_projectile";
            const string boltSkill = "skill.lab_a_bolt";
            var effect = (JsonObject)((JsonObject)((JsonArray)FeelRules.Skill(releaseSkill)["effects"])[0])["params"];
            var speed = ((JsonNumber)effect["speed"]).Value;

            // 命中：两次施放的投射物都命中了木桩，没有任何挥空提示音（此前判定相一结束、投射物还在飞就出了挥空提示）。
            var hit = FeelFp.Of("feel_projectile", Action);
            Assert.DoesNotContain(hit.Items("presentation.sfx_ids"), i => i.Contains("whiff", StringComparison.Ordinal));

            // 未命中：场上没有靶子，投射物沿朝向直飞，在竞技场的直墙前被挡住（结局 = 被地形挡住）。挥空提示必须落在投射物结局上：
            // 发射 tick + 飞到墙前的 tick 数（墙距 / (速度 × 步长)，发射当 tick 就推进一步），而不是判定相结束的那一刻。
            var wallX = ((JsonNumber)((JsonObject)ArenaBlock("straight_wall")["min"])["x"]).Value;
            var flightTicks = (int)Math.Ceiling(wallX / (speed * FeelRules.StepSeconds));
            var miss = FeelFp.Of("feel_projectile_miss", Action);
            var pressA = Press("feel_projectile_miss", AttackAction);
            var pressB = Press("feel_projectile_miss", SkillAction);
            var releaseA = pressA + T(FeelRules.MarkerMs(releaseSkill, "release"));
            var activeEndA = pressA + T(FeelRules.TimelineMs(releaseSkill, "startup_ms") + FeelRules.TimelineMs(releaseSkill, "active_ms"));
            var activeEndB = pressB + T(FeelRules.TimelineMs(boltSkill, "startup_ms") + FeelRules.TimelineMs(boltSkill, "active_ms"));
            Assert.Equal(0.0, miss.Num("spatialhit.hit_confirmed"));
            var whiffTicks = miss.Items("presentation.sfx_ids")
                .Where(i => i.Contains("whiff", StringComparison.Ordinal)).Select(FeelFp.TickOf).ToList();
            Assert.Equal(2, whiffTicks.Count);
            Assert.True(whiffTicks[0] > activeEndA + 1, $"挥空提示 {whiffTicks[0]} 不应在判定相结束（{activeEndA}）附近出");
            Assert.InRange(whiffTicks[0], releaseA + flightTicks - 1, releaseA + flightTicks + 1);

            // 对照：只有 hit 标记的 bolt 在没有目标时根本没有发射投射物，没有东西在飞，挥空照旧在判定相结束时判定。
            Assert.Equal(activeEndB, whiffTicks[1]);
        }

        /// <summary>竞技场地形里按名字取一块阻挡。</summary>
        private static JsonObject ArenaBlock(string name)
        {
            var arena = FeelRules.Row(System.IO.Path.Combine("data", "_lab", "lab", "lab.arena.json"), "lab.arena.lab_arena");
            foreach (var b in (JsonArray)arena["blocks"])
            {
                var o = (JsonObject)b;
                if (((JsonString)o["name"]).Value == name)
                {
                    return o;
                }
            }

            throw new InvalidOperationException($"竞技场没有阻挡 {name}");
        }

        // ---------- combo3 ----------

        [Fact]
        public void Combo3_ChainsThroughComboWindows_AndFinisherUsesFeelRefOverride()
        {
            var fp = FeelFp.Of("feel_combo3", Action);
            var p = FeelRules.ForCell(Action);
            var skills = new[] { "skill.lab_a_combo1", "skill.lab_a_combo2", "skill.lab_a_combo3" };

            // 三次按下各自在连招窗口里被消费，连招序号 0/1/2，段间用取消原因 CancelInto 串起来。
            Assert.Equal("skill.lab_a_combo1>skill.lab_a_combo2>skill.lab_a_combo3", fp.Text("actiontl.combo_chain"));
            Assert.Equal(2.0, fp.Num("actiontl.combo_segments"));
            var starts = fp.Items("actiontl.starts");
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal($"player@{Press("feel_combo3", AttackAction, i)}:{skills[i]}#{i}", starts[i]);
            }

            Assert.Equal(
                new[]
                {
                    $"player@{Press("feel_combo3", AttackAction, 1)}:CancelInto>skill.lab_a_combo2",
                    $"player@{Press("feel_combo3", AttackAction, 2)}:CancelInto>skill.lab_a_combo3",
                },
                fp.Items("actiontl.cancels"));

            // 连招窗口在"开始 tick + 毫秒换算 + 上一段命中顿帧"打开；按下落在窗口内，所以消费延迟为 0。
            var markers = fp.Items("actiontl.markers");
            var combo = (JsonObject)FeelRules.Timeline(skills[0])["combo"];
            var openMs = ((JsonNumber)combo["open_ms"]).Value * p.N("combo_window_scale");
            var firstOpen = Press("feel_combo3", AttackAction, 0) + T(openMs) + p.Ticks("attacker_hitstop_ms");
            Assert.Contains($"player@{firstOpen}:combo_open", markers);
            Assert.True(Press("feel_combo3", AttackAction, 1) >= firstOpen);
            Assert.Equal(new[] { 0.0, 0.0, 0.0 }, fp.Nums("inputbuf.accept_latency_ticks"));

            // 前两段是预设缺省的 medium；第三段 feel_ref 指到 lab_a_finisher 覆盖行：heavy、更长顿帧、更大 stagger_power → 击退。
            var finisher = "feel.action.lab_a_finisher";
            var heavy = FeelRules.OverrideText("feel.action", finisher, "impact_class")!;
            var attackerHeavy = T(FeelRules.OverrideNumber("feel.action", finisher, "attacker_hitstop_ms")!.Value);
            var targetHeavy = T(FeelRules.OverrideNumber("feel.action", finisher, "target_hitstop_ms")!.Value);
            Assert.Equal(p.S("impact_class") + "," + p.S("impact_class") + "," + heavy, fp.Text("reaction.hit_classes"));
            var hit3 = Press("feel_combo3", AttackAction, 2) + T(FeelRules.MarkerMs(skills[2], "hit"));
            Assert.Contains($"{hit3}:player:{attackerHeavy}", fp.Items("hitstop.started"));
            Assert.Contains($"{hit3}:stake:{targetHeavy}", fp.Items("hitstop.started"));
            Assert.Equal(ReactionFor(heavy), fp.Items("reaction.reactions")[2].Split(':')[2]);
            Assert.True(FeelRules.OverrideNumber("feel.action", finisher, "stagger_power")!.Value > p.N("stagger_power"));
        }

        // ---------- dodge cancel / buffer lead ----------

        [Fact]
        public void DodgeCancel_EarlyPressExpiresInBuffer_PressInsideWindowCancelsIntoDodge()
        {
            const string slash = "skill.lab_a_slash";
            const string dodge = "skill.lab_a_dodge";
            var fp = FeelFp.Of("feel_dodge_cancel", Action);
            var p = FeelRules.ForCell(Action);
            var bufferTicks = p.Ticks("buffer_ms");
            var hitstop = p.Ticks("attacker_hitstop_ms");
            var (open, _) = FeelRules.CancelWindowMs(slash, "dodge");

            // 第一次：闪避按在窗口打开之前 > 缓冲时长，缓冲里过期，不取消；过期 tick = 按下 + 缓冲 tick + 1 + 期间动作时钟被顿帧暂停的 tick。
            var slash1 = Press("feel_dodge_cancel", AttackAction, 0);
            var dodgeEarly = Press("feel_dodge_cancel", DodgeAction, 0);
            var window1 = slash1 + T(open) + hitstop;
            Assert.True(window1 - dodgeEarly > bufferTicks, "第一次闪避应早于窗口打开超过缓冲时长");
            Assert.Equal(new[] { $"{dodgeEarly + bufferTicks + 1 + hitstop}:lab_a_dodge" }, fp.Items("inputbuf.expire_ticks"));
            Assert.Equal("Expired:1", fp.Text("inputbuf.drop_counts"));

            // 第二次：按在窗口内，同 tick 取消挥击进闪避（取消原因 CancelInto），闪避从该 tick 开始。
            var slash2 = Press("feel_dodge_cancel", AttackAction, 1);
            var dodgeInside = Press("feel_dodge_cancel", DodgeAction, 1);
            var window2 = slash2 + T(open) + hitstop;
            Assert.True(dodgeInside >= window2);
            Assert.Equal(new[] { $"player@{dodgeInside}:CancelInto>{dodge}" }, fp.Items("actiontl.cancels"));
            Assert.Contains($"player@{dodgeInside}:{dodge}#0", fp.Items("actiontl.starts"));
            // 被切掉的挥击：收招只走了"窗口打开后到取消"的 tick。
            Assert.Contains($"recovery={dodgeInside - (slash2 + T(FeelRules.TimelineMs(slash, "startup_ms")) + T(FeelRules.TimelineMs(slash, "active_ms")) + hitstop)}", fp.Text("actiontl.phase_ticks"));
        }

        [Fact]
        public void BufferLead_BoundaryIsTheBufferLengthInTicks_PlusNothing()
        {
            const string slash = "skill.lab_a_slash";
            var fp = FeelFp.Of("feel_buffer_lead", Action);
            var p = FeelRules.ForCell(Action);
            var bufferTicks = p.Ticks("buffer_ms");
            var (open, _) = FeelRules.CancelWindowMs(slash, "dodge");

            // 三次试验：挥击 → 窗口打开 tick（无命中，没有顿帧）→ 在窗口打开前 lead 个 tick 按闪避。
            var leads = new List<int>();
            for (var i = 0; i < 3; i++)
            {
                var windowOpen = Press("feel_buffer_lead", AttackAction, i) + T(open);
                leads.Add(windowOpen - Press("feel_buffer_lead", DodgeAction, i));
            }

            Assert.Equal(new[] { bufferTicks - 1, bufferTicks, bufferTicks + 1 }, leads);

            // lead <= 缓冲时长：被缓冲住，窗口打开那个 tick 取消进闪避，输入到动作开始的延迟恰为 lead；
            // lead = 缓冲时长 + 1：窗口打开的同一 tick 已经过期，没有取消。
            var accepted = fp.Items("inputbuf.consume_ticks").FindAll(i => i.EndsWith(":skill.lab_a_dodge", StringComparison.Ordinal));
            Assert.Equal(2, accepted.Count);
            Assert.Equal(Press("feel_buffer_lead", AttackAction, 0) + T(open), FeelFp.TickOf(accepted[0]));
            Assert.Equal(Press("feel_buffer_lead", AttackAction, 1) + T(open), FeelFp.TickOf(accepted[1]));
            var latencies = fp.Nums("inputbuf.accept_latency_ticks");
            Assert.Equal(new[] { 0.0, leads[0], 0.0, leads[1], 0.0 }, latencies);
            var expiredAt = Press("feel_buffer_lead", AttackAction, 2) + T(open);
            Assert.Equal(new[] { $"{expiredAt}:lab_a_dodge" }, fp.Items("inputbuf.expire_ticks"));
            Assert.Equal(2, fp.Items("actiontl.cancels").Count);
        }

        // ---------- charge ----------

        [Fact]
        public void Charge_RatioIsHoldTimeBetweenMinAndMax_RecordedAsIs()
        {
            const string skill = "skill.lab_a_charge";
            var fp = FeelFp.Of("feel_charge", Action);
            var charge = (JsonObject)FeelRules.Timeline(skill)["charge"];
            var minMs = ((JsonNumber)charge["min_ms"]).Value;
            var maxMs = ((JsonNumber)charge["max_ms"]).Value;

            var ratios = fp.Nums("actiontl.charge_ratios");
            Assert.Equal(3, ratios.Count);
            for (var i = 0; i < 3; i++)
            {
                // 蓄力类按钮：按住的 tick 数 × 步长 = 按住毫秒；比例 = (按住 − 最小) / (最大 − 最小)，夹在 [0,1]；低于最小蓄力为 0。
                var holdMs = (Release("feel_charge", ChargeAction, i) - Press("feel_charge", ChargeAction, i)) * FeelRules.StepSeconds * 1000.0;
                var expected = Math.Max(0.0, Math.Min(1.0, (holdMs - minMs) / (maxMs - minMs)));
                Assert.Equal(expected, ratios[i], 6);
            }

            Assert.True(ratios[0] == 0.0 && ratios[1] > 0.0 && ratios[1] < 1.0 && ratios[2] == 1.0);

            // 蓄力按钮松开才出手：动作开始 tick = 松开 tick（输入到动作开始的延迟 = 按住 tick 数）。
            var starts = fp.Items("actiontl.starts");
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal($"player@{Release("feel_charge", ChargeAction, i)}:{skill}#0", starts[i]);
            }

            // 蓄力比例此刻只被记录，不改变命中结算（三次命中同形：同顿帧、同反应）。
            Assert.Equal(3.0, fp.Num("spatialhit.hit_confirmed"));
            Assert.Single(new HashSet<string>(fp.Text("reaction.hit_classes").Split(',')));
        }

        // ---------- elite armor / flinch / interrupt ----------

        [Fact]
        public void EliteArmor_HitInsideArmorWindowGivesNoReaction_ActionIsNotInterrupted()
        {
            const string swing = "skill.lab_a_elite_swing";
            var fp = FeelFp.Of("feel_elite_armor", Action);
            var p = FeelRules.ForCell(Action);
            var castTick = LabTestSupport.Script("feel_elite_armor").Events.First(e => e.Kind == ScriptEventKind.Cast).Tick;
            var press = Press("feel_elite_armor", SkillAction);

            // 霸体窗口 = 精英挥击 armor_start..armor_end 标记（0..400 ms），窗口里命中确认的受击反应为 None，且没有硬直。
            var armorStart = castTick + T(FeelRules.MarkerMs(swing, "armor_start"));
            var impact = FeelFp.TickOf(fp.Items("spatialhit.confirm_ticks")[0]);
            var markers = fp.Items("actiontl.markers");
            var armorEnd = FeelFp.FirstTickWhere(markers, i => i.EndsWith(":armor_end", StringComparison.Ordinal));
            Assert.True(armorStart <= impact && impact < armorEnd, $"命中 tick {impact} 应落在霸体窗口 [{armorStart},{armorEnd})");
            Assert.Equal("None:1", fp.Text("reaction.reaction_counts"));
            Assert.Equal(string.Empty, fp.Text("reaction.reactions"));
            Assert.Equal(string.Empty, fp.Text("reaction.staggered_ticks"));
            Assert.Equal(string.Empty, fp.Text("actiontl.cancels"));
            Assert.Contains(fp.Items("actiontl.finishes"), f => f.StartsWith("elite@", StringComparison.Ordinal));

            // 顿帧不被霸体取消：攻击方（玩家）照常顿帧，受击方（精英）仍按预设受击方时长顿帧。
            Assert.Contains($"{impact}:player:{p.Ticks("attacker_hitstop_ms")}", fp.Items("hitstop.started"));
            Assert.Contains($"{impact}:elite:{p.Ticks("target_hitstop_ms")}", fp.Items("hitstop.started"));
            Assert.True(press < impact);
        }

        [Fact]
        public void EliteFlinch_HitAfterArmorWindowIsCappedByCharacterReactionCap_ActionContinues()
        {
            const string swing = "skill.lab_a_elite_swing";
            var fp = FeelFp.Of("feel_elite_flinch", Action);
            var castTick = LabTestSupport.Script("feel_elite_flinch").Events.First(e => e.Kind == ScriptEventKind.Cast).Tick;

            var markers = fp.Items("actiontl.markers");
            var armorEnd = FeelFp.FirstTickWhere(markers, i => i.EndsWith(":armor_end", StringComparison.Ordinal));
            var impact = FeelFp.TickOf(fp.Items("spatialhit.confirm_ticks")[0]);
            Assert.True(impact >= armorEnd, "命中应落在霸体窗口结束之后");
            Assert.Equal(castTick + T(FeelRules.MarkerMs(swing, "armor_end")), armorEnd);

            // 精英表现档案（creature.feel_ref → feel.character 行）把 reaction_cap 封到 flinch：
            // 冲击等级映射本应是 stagger，被封顶成 flinch；flinch 不是硬直类反应，时长 0，精英的挥击不被打断。
            var cap = FeelRules.OverrideText("feel.character", "feel.character.lab_a_elite", "reaction_cap")!;
            Assert.Equal("flinch", cap);
            Assert.Equal(new[] { $"{impact}:elite:Flinch:0" }, fp.Items("reaction.reactions"));
            Assert.Equal(string.Empty, fp.Text("reaction.staggered_ticks"));
            Assert.Equal(string.Empty, fp.Text("actiontl.cancels"));
            Assert.Contains(fp.Items("actiontl.finishes"), f => f.StartsWith("elite@", StringComparison.Ordinal));
        }

        [Fact]
        public void Interrupt_StaggerCancelsTheTargetsActionBeforeItsHitMarker_SoThePlayerIsNeverHit()
        {
            var fp = FeelFp.Of("feel_interrupt", Action);
            var p = FeelRules.ForCell(Action);
            var script = LabTestSupport.Script("feel_interrupt");
            var mobCast = script.Events.First(e => e.Kind == ScriptEventKind.Cast).Tick;
            var press = Press("feel_interrupt", AttackAction);
            const string slash = "skill.lab_a_slash";

            var playerHit = press + T(FeelRules.MarkerMs(slash, "hit"));
            var mobHitWouldBe = mobCast + T(FeelRules.MarkerMs(slash, "hit"));
            Assert.True(playerHit < mobHitWouldBe, "玩家的命中应早于靶子自己的命中标记");

            // 玩家命中靶子 → 反应 Stagger → 靶子动作在同 tick 被取消（原因 Stagger），其命中标记不出现，玩家没有受击确认。
            Assert.Equal(new[] { $"mob@{playerHit}:Stagger" }, fp.Items("actiontl.cancels"));
            Assert.DoesNotContain(fp.Items("actiontl.markers"), m => m.StartsWith("mob@", StringComparison.Ordinal));
            Assert.Equal(new[] { $"{playerHit}:player>mob" }, fp.Items("spatialhit.confirm_ticks"));
            Assert.Equal(1.0, fp.Num("spatialhit.hit_confirmed"));
            Assert.Equal(ReactionFor(p.S("impact_class")) + ":1", fp.Text("reaction.reaction_counts"));

            // 对照：靶子在被打断前只走了前摇的一部分（相位记录里有 startup，没有 active/recovery）。
            Assert.Contains("mob:skill.lab_a_slash:startup=" + (playerHit - mobCast), fp.Text("actiontl.phase_ticks"));
        }

        // ---------- kill / group hit ----------

        [Fact]
        public void Kill_AttackerHitstopIsKillAmplifiedAndCapped_TargetDoesNotFreeze_ReactionIsDeath()
        {
            var fp = FeelFp.Of("feel_kill", Action);
            var plain = FeelFp.Of("feel_melee", Action);
            var p = FeelRules.ForCell(Action);
            var press = Press("feel_kill", AttackAction);
            var hit = press + T(FeelRules.MarkerMs("skill.lab_a_slash", "hit"));

            var amplified = AttackerHitstop(p.N("attacker_hitstop_ms"), p.N("kill_hitstop_scale"), p.N("attacker_hitstop_cap_ms"), true);
            var normal = AttackerHitstop(p.N("attacker_hitstop_ms"), p.N("kill_hitstop_scale"), p.N("attacker_hitstop_cap_ms"), false);
            Assert.True(amplified > normal, "击杀放大应让攻击方顿帧比普通命中更长");

            Assert.Equal(new[] { $"{hit}:player:{amplified}" }, fp.Items("hitstop.started"));
            Assert.Equal(string.Empty, fp.Text("hitstop.frozen_ticks_targets"));
            Assert.Equal(new[] { $"{hit}:frail:Death:0" }, fp.Items("reaction.reactions"));
            Assert.Equal(1.0, fp.Num("reaction.kills"));

            // 表现：击杀变体（含"收尾"音效层）与更大的镜头冲击（击杀强度倍率 2 × 同等级增益）。
            Assert.Equal(new[] { $"{hit}:sfx.lab_impact_t2", $"{hit}:sfx.lab_sweetener_t1" }, fp.Items("presentation.sfx_ids"));
            Assert.Equal(2.0, fp.Nums("presentation.camera_magnitudes")[0] / plain.Nums("presentation.camera_magnitudes")[0], 6);
            Assert.Equal(new[] { $"{hit}:freeze:player:{amplified}", $"{hit + amplified}:release:player:0" }, fp.Items("presentation.freeze_ops"));
        }

        [Fact]
        public void GroupHit_SameTickMultiTargetHitstopTakesMaxAndCapsAttackerSide()
        {
            const string slam = "feel.action.lab_a_slam";
            var fp = FeelFp.Of("feel_group_hit", Action);
            var p = FeelRules.ForCell(Action);
            var hit1 = Press("feel_group_hit", AttackAction, 0) + T(FeelRules.MarkerMs("skill.lab_a_slam", "hit"));
            var hit2 = Press("feel_group_hit", AttackAction, 1) + T(FeelRules.MarkerMs("skill.lab_a_slam", "hit"));

            var attackerMs = FeelRules.OverrideNumber("feel.action", slam, "attacker_hitstop_ms")!.Value;
            var killScale = FeelRules.OverrideNumber("feel.action", slam, "kill_hitstop_scale")!.Value;
            var capMs = p.N("attacker_hitstop_cap_ms");
            var normal = AttackerHitstop(attackerMs, killScale, capMs, false);
            var killed = AttackerHitstop(attackerMs, killScale, capMs, true);
            Assert.True(killed == T(capMs) && T(attackerMs * killScale) > T(capMs), "击杀放大应超过上限并被压到上限");
            Assert.True(killed > normal);

            // 第一击扫三个靶子（含一个低血量靶子被击杀）：同 tick 三条命中确认；攻击方顿帧 = 各命中的最大值 = 被压到上限的击杀放大值；
            // 两个木桩的受击方顿帧同 tick 合并成一条（各自按受击方上限限幅后取最大）。
            Assert.Equal(
                new[] { $"{hit1}:player>pack_c", $"{hit1}:player>pack_l", $"{hit1}:player>pack_r", $"{hit2}:player>pack_l", $"{hit2}:player>pack_r" },
                fp.Items("spatialhit.confirm_ticks"));
            var target = Math.Min(p.Ticks("target_hitstop_ms"), p.Ticks("hitstop_cap_ms"));
            Assert.Equal(
                new[] { $"{hit1}:player:{killed}", $"{hit1}:pack_l+pack_r:{target}", $"{hit2}:player:{normal}", $"{hit2}:pack_l+pack_r:{target}" },
                fp.Items("hitstop.started"));
            Assert.Equal(0.0, fp.Num("hitstop.nested_starts"));

            // 第二击只剩两个木桩：无击杀，取覆盖行的 90 ms（不被放大）。
            Assert.Equal(new[] { "0.0=pack_c,pack_l,pack_r", "1.0=pack_l,pack_r" }, fp.Items("spatialhit.hit_sets"));
            Assert.Equal(1.0, fp.Num("reaction.kills"));
            Assert.Equal(5.0, fp.Num("spatialhit.hit_confirmed"));
            Assert.Equal(0.0, fp.Num("spatialhit.duplicate_confirmations"));

            // 表现：同 tick 三条命中合并成一次镜头冲击（合并命中数 3），第二击合并数 2。
            Assert.Equal(new[] { $"{hit1}:3:", $"{hit2}:2:" }, fp.Items("presentation.camera_cues"));
        }

        // ---------- patrol / breakable ----------

        [Fact]
        public void Patrol_EachSwingHitsExactlyWhenTheTargetIsInsideTheConeAtTheHitMarkerTick()
        {
            const string skill = "skill.lab_a_slash";
            var recording = LabTestSupport.Runner.Record(LabTestSupport.Script("feel_patrol"), Action);
            var fp = FeelFp.Of("feel_patrol", Action);
            var chain = FeelRules.Row(System.IO.Path.Combine("data", "_lab_action", "target", "target.chain_def.json"), "target.chain.lab_a_arc");
            var shape = (JsonObject)chain["shape"];
            var half = ((JsonNumber)shape["angle"]).Value / 2.0;
            var radius = ((JsonNumber)shape["radius"]).Value;

            var hitTicks = new HashSet<int>();
            foreach (var item in fp.Items("spatialhit.confirm_ticks"))
            {
                hitTicks.Add(FeelFp.TickOf(item));
            }

            var presses = Events("feel_patrol", AttackAction, ScriptEventKind.Press);
            var misses = 0;
            foreach (var e in presses)
            {
                var hitTick = e.Tick + T(FeelRules.MarkerMs(skill, "hit"));
                var sample = recording.Feel!.Ticks[hitTick];
                var position = sample.TargetPositions.First(kv => kv.Key == "patrol").Value;
                var player = new Vec2(0, 0);
                var d = position - player;
                var inside = d.Length <= radius && Math.Abs(Math.Atan2(d.Y, d.X)) <= half;
                Assert.Equal(inside, hitTicks.Contains(hitTick));
                if (!inside)
                {
                    misses++;
                }
            }

            // 靶子确实在动：第一刀落在它走进扇形之前（空挥），之后才有命中。
            Assert.True(misses >= 1 && hitTicks.Count >= 1);
            var track = fp.Text("motion.target_track");
            Assert.StartsWith("patrol@0=2,-3;", track, StringComparison.Ordinal);
            Assert.Contains("patrol@30=", track, StringComparison.Ordinal);
        }

        [Fact]
        public void Breakable_BlocksTheWalkUntilBrokenThenBlockingVersionUpdatesAndPlayerWalksThrough()
        {
            var fp = FeelFp.Of("feel_breakable", Action);
            var recording = LabTestSupport.Runner.Record(LabTestSupport.Script("feel_breakable"), Action);
            var p = FeelRules.ForCell(Action);

            // 打碎所需的命中数 = 障碍生命 / 单次伤害（生命来自创建物模板的耐力）。
            var hp = ((JsonNumber)((JsonObject)FeelRules.Row(System.IO.Path.Combine("data", "_lab", "creature", "creature.template.json"), "creature.lab_breakable")["base_stats"])["stat.stamina"]).Value;
            var perHit = fp.Num("attack.damage_total") / fp.Num("attack.damage_events");
            var hitsNeeded = (int)Math.Ceiling(hp / perHit);
            Assert.Equal(hitsNeeded, (int)fp.Num("spatialhit.hit_confirmed"));
            Assert.Equal(1.0, fp.Num("reaction.kills"));
            var killTick = FeelFp.TickOf(fp.Items("spatialhit.confirm_ticks")[hitsNeeded - 1]);
            var reactions = fp.Items("reaction.reactions");
            Assert.Equal($"{killTick}:breakable:Death:0", reactions[reactions.Count - 1]);

            // 阻挡变更恰好一次，且就在击杀 tick（障碍的动态阻挡矩形随死亡移除，nav 阻挡版本递增）。
            var updates = fp.Items("motion.blocking_updates");
            Assert.Single(updates);
            Assert.Equal(killTick, FeelFp.TickOf(updates[0]));

            // 击杀前玩家被挡在障碍面前（不穿过障碍的左缘），击杀后走过去。
            var obstacleX = 2.0; // 靶子集里 breakable 的 x；障碍矩形半宽 0.5
            var before = recording.Ticks[killTick - 1].Position.X;
            Assert.True(before < obstacleX, $"击杀前玩家 x={before} 应被挡在障碍中心 {obstacleX} 之前");
            var endX = recording.Ticks[recording.Ticks.Count - 1].Position.X;
            Assert.True(endX > obstacleX, $"击杀后玩家应走过障碍，终点 x={endX}");
            Assert.True(p.N("walk_speed_ratio") > 0);
        }

        // ---------- motion ----------

        [Fact]
        public void MotionAccelDecel_TicksEqualProfileRule_AndSteadySpeedIsWalkRatioTimesBaseSpeed()
        {
            var fp = FeelFp.Of("feel_motion_accel_decel", Action);
            var p = FeelRules.ForCell(Action);
            Assert.Equal("linear", p.S("accel_curve"));
            Assert.Equal("linear", p.S("brake_curve"));

            var baseSpeed = CalibrationBaseSpeed(Action);
            var steady = p.N("walk_speed_ratio") * baseSpeed;
            Assert.Equal(steady, fp.Num("motion.speed_max"), 6);
            Assert.Equal(steady, fp.Num("movement.speed_steady"), 6);

            // 线性加速：accel_ms 换算成 tick 数，每 tick 前进 1/N，N 个 tick 到达目标速度。
            Assert.Equal(p.Ticks("accel_ms"), fp.Num("movement.accel_ticks"));
            Assert.Equal(p.Ticks("accel_ms"), fp.Num("motion.ticks_to_full_speed"));

            // 线性制动以"当前速度与基础移速较大者"为参照：从 steady 起，制动进度从 1 - steady/参照 开始、每 tick 加 1/N，到 1 为止。
            var reference = Math.Max(steady, baseSpeed);
            var decelTicks = p.Ticks("decel_ms");
            var stopTicks = (int)Math.Ceiling(steady / reference * decelTicks - 1e-9);
            Assert.Equal(stopTicks, fp.Num("movement.stop_ticks"));

            // 起步段：Grounded，来源 Regular 的 tick 数 = 推杆持续 + 刹停（来源回到 None 才算停稳）。
            Assert.Contains("Grounded/Regularx", fp.Text("motion.mode_sequence"));
        }

        [Fact]
        public void MotionTurn_FacingReachesNewDirectionAtTurnRate()
        {
            var fp = FeelFp.Of("feel_motion_turn", Action);
            var p = FeelRules.ForCell(Action);
            Assert.Equal("instant", p.S("reverse_policy"));

            // +x → +y 转 90 度，转向速率 turn_rate_deg_s：每 tick 转 rate × 步长；到位所需 tick 数向上取整，
            // 其中第一个 tick 就是方向改变的那个 tick（所以"请求后第几个 tick 到位"比所需 tick 数少 1）。
            var perTick = p.N("turn_rate_deg_s") * FeelRules.StepSeconds;
            var ticks = (int)Math.Ceiling(90.0 / perTick - 1e-9);
            Assert.Equal(ticks - 1, fp.Num("motion.turn_ticks"));

            // 终点朝向：最后一次方向 -y（-90 度）。
            Assert.Equal(-Math.PI / 2, fp.Num("motion.facing_final"), 6);
        }

        [Fact]
        public void MotionWall_PlayerStopsAtTheWallFace_AndSlantedApproachSlidesAlongIt()
        {
            var arena = FeelRules.Row(System.IO.Path.Combine("data", "_lab", "lab", "lab.arena.json"), "lab.arena.lab_arena");
            double wallX = 0;
            foreach (var b in (JsonArray)arena["blocks"])
            {
                var o = (JsonObject)b;
                if (((JsonString)o["name"]).Value == "straight_wall")
                {
                    wallX = ((JsonNumber)((JsonObject)o["min"])["x"]).Value;
                }
            }

            var p = FeelRules.ForCell(Action);
            Assert.True(p.B("wall_slide"));

            // 正撞：停在墙面前（差一个到达容差），速度归零，被挡的 tick 数大于 0。
            var straight = FeelFp.Of("feel_motion_wall", Action);
            var endStraight = straight.Text("motion.end_position").Split(',');
            Assert.InRange(double.Parse(endStraight[0], CultureInfo.InvariantCulture), wallX - 0.01, wallX);
            Assert.Equal(0.0, double.Parse(endStraight[1], CultureInfo.InvariantCulture));
            Assert.True(straight.Num("movement.blocked_ticks") > 0);

            // 斜撞：沿墙滑行——x 同样被墙拦住，y 方向的位移保留（起点 y=0，终点 y>0）。
            var slant = FeelFp.Of("feel_motion_wall_slant", Action);
            var endSlant = slant.Text("motion.end_position").Split(',');
            Assert.InRange(double.Parse(endSlant[0], CultureInfo.InvariantCulture), wallX - 0.2, wallX);
            Assert.True(double.Parse(endSlant[1], CultureInfo.InvariantCulture) > 1.0);
        }

        // ---------- 单位间体积阻挡（手感设计/02 第 9 节） ----------

        private const string UnitBlockScript = "feel_unit_block";
        private const string UnitBlockPreset = "feel.preset.unit_block";
        private static readonly string UnitBlockDataDir = System.IO.Path.Combine("lab", "fixtures", "data", "unit_block", "feel");

        /// <summary>两个单位的体积半径之和（世界单位）：预设里的半径（身高倍数）× 标定参考身高 × 2（玩家与木桩共用同一份档案）。</summary>
        private static double UnitBlockSumRadius()
        {
            var preset = FeelRules.Row(System.IO.Path.Combine(UnitBlockDataDir, "feel.preset.json"), UnitBlockPreset);
            var radius = ((JsonNumber)((JsonObject)preset["values"])["unit_body_radius"]).Value;
            var calibration = FeelRules.Row(System.IO.Path.Combine(UnitBlockDataDir, "feel.calibration.json"), "feel.calibration.lab_unit_block");
            return 2.0 * radius * ((JsonNumber)calibration["reference_height"]).Value;
        }

        private static (double MinGap, Vec2 End, Vec2 Stake, LabRecording Record) UnitBlockRun(string cell, LabRunVariant? variant = null)
        {
            var record = LabTestSupport.Runner.Record(LabTestSupport.Script(UnitBlockScript), cell, variant);
            Assert.Single(record.Dummies);
            var stake = record.Dummies[0].Value;
            var min = double.MaxValue;
            foreach (var tick in record.Ticks)
            {
                min = Math.Min(min, (tick.Position - stake).Length);
            }

            return (min, record.Ticks[record.Ticks.Count - 1].Position, stake, record);
        }

        [Theory]
        [InlineData("2d_action")]
        [InlineData("2d_targeted")]
        [InlineData("3d_action")]
        public void UnitBlock_WalkingIntoTheStake_StopsAtTheVolumeBoundary_AndTheDashIsStoppedByItToo(string cell)
        {
            var sum = UnitBlockSumRadius();
            var pull = new Core.Carriers.Unit.MovementOptions().ArrivalEpsilon;
            var run = UnitBlockRun(cell);

            // 不变量：任意 tick 玩家与木桩的中心距不小于半径之和；复现：玩家停在木桩体积边界前（差一个到达容差）。
            Assert.True(run.MinGap >= sum - 1e-9, $"最小中心距 {run.MinGap:R} 小于半径之和 {sum}");
            var boundary = run.Stake.X - sum;
            Assert.InRange(run.End.X, boundary - pull, boundary);
            Assert.Equal(0.0, run.End.Y);

            // 贴着体积继续推：松开前的最后几个 tick 位置不再变化（不蠕动）。
            var release = Events(UnitBlockScript, "input.action.move", ScriptEventKind.Axis)[1].Tick;
            var held = run.Record.Ticks[release - 1].Position;
            Assert.Equal(run.Record.Ticks[release - 20].Position, held);

            // 闪避（dash，blocking: stop）从体积边界出发，仍被体积挡住：按下之后位置不变。
            var press = Press(UnitBlockScript, DodgeAction);
            Assert.Equal(held, run.Record.Ticks[press - 1].Position);
            Assert.Equal(held, run.End);
        }

        [Fact]
        public void UnitBlock_DodgeThroughUnits_LetsTheDashPassTheStake_AndWithoutAVolumeTheWalkDoesToo()
        {
            var sum = UnitBlockSumRadius();
            var blocked = UnitBlockRun("2d_action");

            // dodge_through_units：同一脚本，只换预设（运行变体）——闪避穿过木桩，净位移 = 声明距离 × 参考身高；
            // 地形之外没有别的阻挡，所以终点 = 被挡位置 + 声明距离。
            var pass = UnitBlockRun("2d_action", new LabRunVariant { PresetId = "feel.preset.unit_block_pass" });
            var skill = "skill.lab_a_dodge";
            var distance = ((JsonNumber)((JsonObject)FeelRules.Timeline(skill)["motion"])["distance"]).Value;
            var calibration = FeelRules.Row(System.IO.Path.Combine(UnitBlockDataDir, "feel.calibration.json"), "feel.calibration.lab_unit_block_pass");
            var expectedEnd = blocked.End.X + distance * ((JsonNumber)calibration["reference_height"]).Value;
            Assert.Equal(expectedEnd, pass.End.X, 6);
            Assert.True(pass.End.X > pass.Stake.X);
            Assert.True(pass.MinGap < sum, "穿过：闪避途中确实进入过木桩的体积");
            var release = Events(UnitBlockScript, "input.action.move", ScriptEventKind.Axis)[1].Tick;
            Assert.True(pass.Record.Ticks[release - 1].Position.X <= pass.Stake.X - sum + 1e-9, "行走段（闪避之前）仍被挡在体积之外");

            // 档案没声明体积（缺省的动作式预设）：玩家一路走进木桩所在位置——体积阻挡是可选开启的。
            var off = UnitBlockRun("2d_action", new LabRunVariant { PresetId = "feel.preset.arpg_responsive" });
            Assert.True(off.MinGap < sum, $"没有声明体积时玩家应能走进木桩的体积（最小中心距 {off.MinGap:R}）");
            Assert.True(off.End.X > off.Stake.X);
        }
    }
}
