#nullable enable
// EngineLabHostMechanismTests：引擎宿主逐项机制的复现与不变量（手感设计/06 第 4 节）。
// 每项机制一条复现用例（真实适配器上观测到的量，期望值由规则算出，不写裸数）+ 一条不变量用例。
// 渲染隔离：舞台自己用专用层与相机剔除遮罩，并在销毁时恢复（IsolationRestoresOtherCameras 用例直接证明）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.LabHost;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Lab;
using NUnit.Framework;
using UnityEngine;

namespace Adapter.Unity.Tests.LabHost
{
    [Category("module:lab")]
    public sealed class EngineLabHostMechanismTests
    {
        private static EngineLabHost Host => LabHostTestSupport.Host;

        private static string LogicOf(Fingerprint fingerprint) =>
            fingerprint.Project(Host.HeadlessRunner.Registry, MetricClass.Logic);

        private static double Num(EngineLabRun run, string metric) =>
            ((Core.Foundation.Common.Json.JsonNumber)((Core.Foundation.Common.Json.JsonObject)run.Fingerprint.Groups["engine"])[metric]).Value;

        // ───────── 动画命中帧与逻辑命中 tick 对齐 ─────────

        /// <summary>数据里 cast 剪辑的 release 关键帧在剪辑内的时刻（秒）：<c>round(time_pct × (帧数 − 1)) / 帧率</c>，帧率取首帧时长的倒数（与适配器登记剪辑同一规则）。</summary>
        private static double CastReleaseSeconds(string clip = "sprite_anim.std_dummy_cast")
        {
            var root = EngineLabHost.LocateRepoRoot();
            var animSet = File.ReadAllText(Path.Combine(root, "data", "_framework", "display", "display.anim_set.json"));
            var pct = double.Parse(
                Regex.Match(animSet, Regex.Escape(clip) + @"""[^\]]*?""release""[^}]*?""time_pct""\s*:\s*([0-9.]+)").Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            var frames = FramesJson(clip + "__front__body");
            var text = File.ReadAllText(Path.Combine(root, "assets", "_placeholder", "sprite_anim", clip.Substring("sprite_anim.".Length) + "__front__body", "frames.json"));
            var duration = double.Parse(
                Regex.Match(text, @"""duration""\s*:\s*([0-9.]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var index = (int)Math.Round(pct * (frames.Frames - 1), MidpointRounding.AwayFromZero);
            return index * duration;
        }

        [Test]
        public void HitAlignment_SpritePlane_EngineEventTimeEqualsCastStartPlusTheClipKeyframe_AndCutClipsAreReportedMissing()
        {
            var cast = CastReleaseSeconds();
            var quick = CastReleaseSeconds("sprite_anim.std_dummy_cast_quick");
            foreach (var scriptId in new[] { "feel_melee", "feel_combo3" })
            {
                foreach (var cell in new[] { "2d_action", "2_5d_action" })
                {
                    var script = LabHostTestSupport.Script(scriptId);
                    var run = Host.Run(script, cell);
                    var frame = 1.0 / script.Meta.FrameRateCap;
                    var step = 1.0 / 60.0;
                    var events = run.Recording.Feel!.Events;
                    var markers = events.Where(e => e.Kind == "action_marker" && (e.Detail == "hit" || e.Detail == "hit_frame")).ToList();
                    Assert.Greater(markers.Count, 0, scriptId + "@" + cell + "：前置条件——脚本里应有逻辑命中标记");
                    Assert.AreEqual(markers.Count, run.Engine.HitAlignments.Count, "对齐样本数应等于逻辑命中标记数");

                    var startEvents = events.Where(e => e.Kind == "action_started" && e.Actor == "player").ToList();
                    var starts = startEvents.Select(e => e.Tick).ToList();
                    // 每次施法播放的剪辑：实验室武器风格把 combo1/combo2 指到 cast.quick，其余用 cast（M4-W6）。
                    var releases = startEvents.Select(e => e.SkillId == "skill.lab_a_combo1" || e.SkillId == "skill.lab_a_combo2" ? quick : cast).ToList();
                    var finishes = events.Where(e => e.Kind == "action_finished" && e.Actor == "player").Select(e => e.Tick).ToList();
                    var expectedPresent = 0;
                    var ambiguous = 0;
                    var detail = string.Join(",", starts) + " | " + string.Join(",", finishes) + " | release=" + string.Join(",", releases);
                    for (var i = 0; i < starts.Count; i++)
                    {
                        var expectedRelease = starts[i] * step + releases[i];
                        // 读条剪辑被切断：下一次施法开始，或这次施法的逻辑动作结束（cast 状态随施法收尾回落）。
                        var endTicks = new List<int>();
                        if (i + 1 < starts.Count) endTicks.Add(starts[i + 1]);
                        endTicks.AddRange(finishes.Where(t => t > starts[i]).Take(1));
                        var gap = endTicks.Count > 0 ? endTicks.Min() * step - expectedRelease : double.PositiveInfinity;
                        if (Math.Abs(gap) <= 3 * frame)
                        {
                            ambiguous++; // 切断时刻与施放点相差在帧量化之内，谁先到不由规则决定
                        }
                        else if (gap > 0)
                        {
                            expectedPresent++;
                        }
                    }

                    var present = run.Engine.HitAlignments.Where(h => h.Present).ToList();
                    Assert.GreaterOrEqual(present.Count, expectedPresent, scriptId + "@" + cell + "：读条剪辑到得了施放点的次数 = 配上引擎事件的逻辑命中数（被切断的剪辑算缺失）。起点|终点|release：" + detail);
                    Assert.LessOrEqual(present.Count, expectedPresent + ambiguous, scriptId + "@" + cell + "：到不了施放点的剪辑不应配上引擎事件。" + detail);
                    Assert.AreEqual(markers.Count - present.Count, (int)Num(run, "hit_align_missing"));
                    for (var k = 0; k < present.Count; k++)
                    {
                        var sample = present[k];
                        var startIndex = starts.FindLastIndex(t => t * step <= sample.EngineSeconds + 1e-9);
                        var start = starts[startIndex];
                        var expected = start * step + releases[startIndex];
                        // 引擎事件不可能早于"施法起点 + 剪辑里关键帧的时刻"；从待机进入的第一次施法，误差只来自帧量化。
                        Assert.GreaterOrEqual(sample.EngineSeconds, expected - frame, scriptId + "@" + cell + "：引擎事件不应早于施法起点加关键帧时刻");
                        if (k == 0 && starts.Count > 0 && start == starts[0])
                        {
                            Assert.AreEqual(expected, sample.EngineSeconds, 3 * frame, scriptId + "@" + cell + "：第一次施法的引擎事件时刻应等于施法起点加剪辑关键帧时刻（误差为帧量化）");
                        }
                    }
                }
            }
        }

        [Test]
        public void HitAlignment_PairedSampleLag_EqualsTheClipReleaseMinusTheLogicMarkerOffset_ProvingItIsPoseDataNotPairing()
        {
            // 根因核对（M4-W4 第 5 项）：施法技能走 cast 剪辑，其 release 关键帧在剪辑里的时刻是姿势集数据；逻辑命中标记在技能时间线里的偏移是技能数据。
            // 两者不等就表现为恒定的对齐误差——这是数据口径差，不是宿主的配对/时基问题。规则：误差 = 剪辑 release 时刻 − 标记相对施法起点的偏移（帧量化内）。
            var script = LabHostTestSupport.Script("feel_melee");
            var run = Host.Run(script, "2d_action");
            var step = 1.0 / 60.0;
            var frame = 1.0 / script.Meta.FrameRateCap;
            var events = run.Recording.Feel!.Events;
            var start = events.First(e => e.Kind == "action_started" && e.Actor == "player").Tick;
            var marker = events.First(e => e.Kind == "action_marker" && e.Actor == "player" && (e.Detail == "hit" || e.Detail == "hit_frame") && e.Tick >= start).Tick;
            var sample = run.Engine.HitAlignments.First(h => h.Actor == "player" && h.LogicTick == marker);
            Assert.IsTrue(sample.Present, "前置条件：第一次施法的逻辑命中配上了引擎事件");
            var expectedLagMs = (CastReleaseSeconds() - (marker - start) * step) * 1000.0;
            Assert.AreEqual(expectedLagMs, sample.ErrorMilliseconds, 3 * frame * 1000.0, "对齐误差 = 剪辑 release 时刻 − 逻辑标记偏移（数据口径差，不是配对错误）");
        }

        [Test]
        public void HitAlignment_ModelPlane_SamplesMatchLogicMarkers_AndMissingMetricIsConsistent()
        {
            var script = LabHostTestSupport.Script("feel_combo3");
            var run = Host.Run(script, "3d_action");
            var markers = run.Recording.Feel!.Events.Count(e => e.Kind == "action_marker" && (e.Detail == "hit" || e.Detail == "hit_frame"));
            Assert.AreEqual(markers, run.Engine.HitAlignments.Count);
            Assert.AreEqual(run.Engine.HitAlignments.Count(h => !h.Present), (int)Num(run, "hit_align_missing"), "缺失度量应等于没配上引擎事件的样本数");
        }

        /// <summary>
        /// 覆盖全部实验室技能命中标记的脚本（每个有命中标记的实验室技能至少被其中一个脚本施放）：
        /// 150 毫秒档（slash、combo3、charge、projectile、projectile_short、bolt、slam）、100 毫秒档（combo1、combo2、poise_chip、lunge、space_ext 的 jab）、
        /// 300 毫秒档（elite_swing，由精英怪自己施放）。
        /// </summary>
        private static readonly string[] LabSkillScripts =
        {
            "feel_melee", "feel_combo3", "feel_poise_dynamic", "feel_lunge", "feel_charge", "feel_projectile",
            "feel_projectile_expire", "feel_group_hit", "feel_elite_armor", "space.air_combo", "space.air_hit", "space.height_offset", "space.range_action",
        };

        /// <summary>逐命中样本归到施放它的技能：同一施放者最近一次 <c>action_started</c> 的技能 id。</summary>
        private static List<(string Skill, HitAlignSample Sample)> HitsBySkill(EngineLabRun run)
        {
            var events = run.Recording.Feel!.Events;
            var result = new List<(string, HitAlignSample)>();
            foreach (var sample in run.Engine.HitAlignments)
            {
                var started = events.LastOrDefault(e => e.Kind == "action_started" && e.Actor == sample.Actor && e.Tick <= sample.LogicTick);
                Assert.IsNotNull(started, "前置条件：每个命中样本前都有同一施放者的 action_started（" + sample.Actor + " tick " + sample.LogicTick + "）");
                result.Add((started!.SkillId, sample));
            }

            return result;
        }

        /// <summary>数据里带 hit 命中标记的实验室技能（动作式技能库 + space_ext 的 jab），与其时间线上的标记偏移（毫秒）。</summary>
        private static Dictionary<string, int> LabSkillHitMarkers()
        {
            var root = EngineLabHost.LocateRepoRoot();
            var result = new Dictionary<string, int>();
            foreach (var path in new[]
            {
                Path.Combine(root, "data", "_lab_action", "skill", "skill.def.json"),
                Path.Combine(root, "lab", "fixtures", "data", "space_ext", "skill", "skill.def.json"),
            })
            {
                var doc = (Core.Foundation.Common.Json.JsonObject)Core.Foundation.Common.Json.JsonReader.Parse(File.ReadAllText(path));
                foreach (var row in (Core.Foundation.Common.Json.JsonArray)doc["rows"])
                {
                    var obj = (Core.Foundation.Common.Json.JsonObject)row;
                    var timeline = (Core.Foundation.Common.Json.JsonObject)obj["timeline"];
                    foreach (var marker in (Core.Foundation.Common.Json.JsonArray)timeline["markers"])
                    {
                        var m = (Core.Foundation.Common.Json.JsonObject)marker;
                        if (((Core.Foundation.Common.Json.JsonString)m["name"]).Value == "hit")
                        {
                            result[((Core.Foundation.Common.Json.JsonString)obj["id"]).Value] = (int)((Core.Foundation.Common.Json.JsonNumber)m["at_ms"]).Value;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// M4-W6 不变量：每个实验室技能的命中都和它的释放事件配上（精灵位面与模型位面），引擎事件不早于逻辑命中，滞后不超过帧量化。
        /// 此前（M4-W5）施放点只有 150 毫秒一档，100 毫秒的连段/突进/破韧技能晚 50 毫秒，300 毫秒的精英重挥早 150 毫秒（早于逻辑命中，被判定为"已知设计判定"）；
        /// 现在施放点变体（<c>cast.quick</c> / <c>cast.heavy</c>）经 <c>display.weapon_style.lab_*</c> 的 <c>cast_anim_override</c> 把这些技能指到释放点与命中标记同刻的剪辑。
        /// 额外断言：每个有命中标记的实验室技能都被某个脚本施放过（覆盖不靠假设）。
        /// </summary>
        [Test]
        public void HitAlignment_EveryLabSkillHit_IsPairedOnSpriteAndModelPlanes_NeverEarlierThanTheLogicMarker_WithinOneFrame()
        {
            var covered = new HashSet<string>();
            foreach (var scriptId in LabSkillScripts)
            {
                foreach (var cell in new[] { "2d_action", "3d_action" })
                {
                    var script = LabHostTestSupport.Script(scriptId);
                    var run = Host.Run(script, cell);
                    var frameMs = 1000.0 / script.Meta.FrameRateCap;
                    var stepMs = 1000.0 / script.Meta.TickRate;
                    Assert.AreEqual(0.0, Num(run, "hit_align_missing"), scriptId + "@" + cell + "：全部逻辑命中都应配上引擎命中帧事件");
                    foreach (var (skill, sample) in HitsBySkill(run))
                    {
                        covered.Add(skill);
                        var where = scriptId + "@" + cell + " " + skill + " tick " + sample.LogicTick;
                        Assert.IsTrue(sample.Present, where);
                        Assert.GreaterOrEqual(sample.ErrorMilliseconds, -1e-6, where + "：引擎事件不应早于逻辑命中");
                        // 模型位面的 Animator 事件在下一次动画求值时才派发；命中时刻施放者已被顿帧冻结（rig 速度 0），事件要等冻结结束才发出，
                        // 所以滞后 = 一帧量化 + 施放者这次命中的顿帧时长。精灵位面的关键帧与命中同帧触发，不受顿帧影响。
                        var frozenMs = cell == "3d_action"
                            ? run.Engine.Freezes.Where(f => f.Unit == sample.Actor && f.StartTick >= sample.LogicTick - 1 && f.StartTick <= sample.LogicTick + 2).Sum(f => f.Ticks) * stepMs
                            : 0.0;
                        Assert.LessOrEqual(sample.ErrorMilliseconds, frameMs + stepMs + frozenMs + 1e-6,
                            where + "：滞后应在一帧量化（加上模型位面里施放者的顿帧时长 " + frozenMs.ToString("F1") + " 毫秒）之内");
                    }
                }
            }

            foreach (var skill in LabSkillHitMarkers().Keys)
            {
                CollectionAssert.Contains(covered, skill, "没有脚本覆盖实验室技能 " + skill + "：命中对位不变量对它没有证明力");
            }
        }

        // ───────── 镜头冲量插值曲线 ─────────

        [Test]
        public void CameraImpulse_CurveFollowsTheDeclaredLinearDecay_ForEveryFeedbackCue()
        {
            var script = LabHostTestSupport.Script("feel_combo3");
            var run = Host.Run(script, "2d_action");
            var cues = run.Recording.Feel!.Presentation.Where(p => p.Kind == "camera").ToList();
            Assert.Greater(cues.Count, 0, "前置条件：脚本应触发镜头冲量");
            Assert.AreEqual(cues.Count, run.Engine.CameraImpulses.Count, "引擎相机收到的冲量数应等于反馈指令里的镜头冲量数");
            for (var i = 0; i < cues.Count; i++)
            {
                var trace = run.Engine.CameraImpulses[i];
                Assert.AreEqual(cues[i].Value, trace.Magnitude, 1e-9, "幅度来自反馈包");
                Assert.AreEqual(cues[i].Value2, trace.DecayMs, 1e-9, "衰减时长来自反馈包");
                Assert.AreEqual(trace.PeakOffset, trace.Curve[0].Value, 1e-6, "t=0 的实测位移应等于 幅度×画面可视高度");
                Assert.IsFalse(trace.Truncated, "示例脚本的冲量间隔长于衰减时长，不应相互截断");
                Assert.LessOrEqual(trace.MaxLinearDeviation(), 1e-3, "实测曲线与线性衰减的偏差应在度量允差内");
                Assert.AreEqual(0.0, trace.Curve[trace.Curve.Count - 1].Value, 1e-6, "衰减结束后相机残余位移为 0");
            }

            Assert.AreEqual(1.0, Num(run, "camera_impulse_monotone"));
        }

        [Test]
        public void CameraImpulse_NeverFabricated_WhenTheFeelPipelineIsOff()
        {
            var script = LabHostTestSupport.Script("feel_combo3");
            var run = Host.Run(script, "2d_action", null, null, new LabRunVariant { FeelOff = true });
            Assert.AreEqual(0, run.Recording.Feel?.Presentation.Count(p => p.Kind == "camera") ?? 0, "前置条件：关掉手感装配后没有镜头冲量指令");
            Assert.AreEqual(0, run.Engine.CameraImpulses.Count, "没有反馈指令，引擎相机就不应有冲量曲线");
            Assert.AreEqual(0, run.Engine.Freezes.Count, "没有反馈指令，也不应有顿帧冻结观测");
        }

        [Test]
        public void CameraImpulse_OverlappingImpulses_AreMarkedTruncated_AndNeverCountedAsCleanCurves()
        {
            var script = LabHostTestSupport.Script("feel_group_hit");
            var run = Host.Run(script, "2d_action");
            // 不变量：被截断的曲线不进入单调/线性偏差统计，所以偏差度量不会因叠加而被误报。
            Assert.LessOrEqual(Num(run, "camera_impulse_curve_dev"), 1e-3);
            foreach (var trace in run.Engine.CameraImpulses.Where(t => t.Truncated))
            {
                Assert.IsTrue(run.Engine.CameraImpulses.Any(o => o != trace && o.Tick <= trace.Tick + 1 + (int)(trace.DecayMs / 1000.0 * 60) + 1),
                    "被标记截断的曲线前后应有另一条尚在衰减的冲量");
            }
        }

        // ───────── 顿帧期间 rig 与粒子冻结 ─────────

        [Test]
        public void Freeze_FrozenRigAndParticleDoNotAdvance_WhileBystanderRigAndControlParticleDo()
        {
            foreach (var cell in new[] { "2d_action", "3d_action" })
            {
                var script = LabHostTestSupport.Script("feel_group_hit");
                var run = Host.Run(script, cell);
                var freezes = run.Engine.Freezes;
                Assert.Greater(freezes.Count, 0, cell + "：前置条件——脚本应触发顿帧");
                foreach (var freeze in freezes)
                {
                    Assert.AreEqual(0.0, freeze.RigAdvanceSeconds, 1e-9, cell + "：被冻结单位 " + freeze.Unit + " 的 rig 动画时间不应推进");
                    if (!double.IsNaN(freeze.ParticleAdvanceSeconds))
                    {
                        Assert.AreEqual(0.0, freeze.ParticleAdvanceSeconds, 1e-9, cell + "：被冻结单位名下的粒子播放时间不应推进");
                    }
                }

                Assert.IsTrue(freezes.Any(f => !double.IsNaN(f.ParticleAdvanceSeconds)), cell + "：至少有一次冻结观测到了探针粒子（粒子冻结确实被检验过）");
                // 对照：同一区间里没被冻结的旁观 rig 与旁观粒子照常推进——证明"冻结的零推进"不是因为测量本身看不到推进。
                var bystanders = freezes.Where(f => !double.IsNaN(f.BystanderAdvanceSeconds)).ToList();
                Assert.IsNotEmpty(bystanders, cell + "：脚本里有旁观单位，应有旁观 rig 观测");
                Assert.IsTrue(bystanders.Any(f => f.BystanderAdvanceSeconds > 0.0), cell + "：旁观 rig 在冻结区间里应推进");
                var controls = freezes.Where(f => !double.IsNaN(f.ParticleControlAdvanceSeconds)).ToList();
                Assert.IsTrue(controls.Any(f => f.ParticleControlAdvanceSeconds > 0.0), cell + "：对照粒子在冻结区间里应推进");
            }
        }

        [Test]
        public void Freeze_ProbeParticlesAreHostOwned_AndTurningThemOffNeverChangesLogicOrFreezeCount()
        {
            var script = LabHostTestSupport.Script("feel_group_hit");
            var with = Host.Run(script, "2d_action");
            var without = Host.Run(script, "2d_action", new EngineLabOptions { ProbeParticles = false });
            Assert.AreEqual(with.LogicProjection, without.LogicProjection, "探针粒子只是引擎侧观测件，不得影响逻辑");
            Assert.AreEqual(with.Engine.Freezes.Count, without.Engine.Freezes.Count, "冻结次数不依赖探针粒子");
            Assert.IsTrue(without.Engine.Freezes.All(f => double.IsNaN(f.ParticleAdvanceSeconds)), "关掉探针后没有粒子观测");
        }

        // ───────── 每帧耗时分布 ─────────

        [Test]
        public void FrameTime_OneSamplePerDrivenFrame_AndThePercentilesFollowTheRule()
        {
            var script = LabHostTestSupport.Script("feel_melee");
            var run = Host.Run(script, "2d_action");
            Assert.AreEqual(run.Recording.Frames.Count, run.Engine.FramesDriven, "驱动的帧数应等于内核记录的帧数");
            Assert.AreEqual(run.Engine.FramesDriven, run.Engine.FrameMilliseconds.Count, "每个驱动的帧一个耗时样本");
            Assert.IsTrue(run.Engine.FrameMilliseconds.All(ms => ms >= 0.0), "耗时不为负");
            var sorted = run.Engine.FrameMilliseconds.OrderBy(x => x).ToList();
            Assert.AreEqual(sorted[(int)Math.Ceiling(0.5 * sorted.Count) - 1], Num(run, "frame_ms_p50"), 1e-9);
            Assert.AreEqual(sorted[(int)Math.Ceiling(0.95 * sorted.Count) - 1], Num(run, "frame_ms_p95"), 1e-9);
            Assert.AreEqual(sorted[sorted.Count - 1], Num(run, "frame_ms_max"), 1e-9);
            Assert.LessOrEqual(Num(run, "frame_ms_p50"), Num(run, "frame_ms_p95"));
            Assert.LessOrEqual(Num(run, "frame_ms_p95"), Num(run, "frame_ms_max"));
            Assert.Less(Num(run, "frame_ms_p95"), 250.0, "引擎侧每帧驱动耗时的 95 分位应在一个量级合理的上限内（真实时钟，只做粗上限）");
        }

        // ───────── GPU 帧耗时（M4-W4） ─────────

        private static string Str(EngineLabRun run, string metric) =>
            ((Core.Foundation.Common.Json.JsonString)((Core.Foundation.Common.Json.JsonObject)run.Fingerprint.Groups["engine"])[metric]).Value;

        [Test]
        public void GpuFrameTime_StatusMetricIsAlwaysPresent_AndFollowsTheGraphicsDevice()
        {
            // 复现 + 不变量：有图形设备（且支持回读）时每个驱动的帧一个 GPU 耗时样本、分位数按规则折算；没有（批处理无图形模式）时度量照样在场——
            // 状态标 unavailable、数值是 -1 哨兵、原因写进记录——不是缺失。关掉选项同样标不可用。任一情形逻辑都与无头宿主一致。
            var script = LabHostTestSupport.Script("feel_melee");
            var run = Host.Run(script, "2d_action");
            var hasDevice = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null && SystemInfo.supportsAsyncGPUReadback;
            Assert.AreEqual(0, run.Engine.Errors.Count, string.Join(" | ", run.Engine.Errors));
            Assert.AreEqual(hasDevice ? "available" : "unavailable", Str(run, "gpu_frame_status"), "状态应与图形设备是否可用一致：" + run.Engine.GpuUnavailableReason);
            if (hasDevice)
            {
                Assert.IsTrue(run.Engine.GpuAvailable);
                Assert.AreEqual(run.Engine.FramesDriven, run.Engine.GpuFrameMilliseconds.Count, "每个驱动的帧一个 GPU 耗时样本");
                Assert.IsTrue(run.Engine.GpuFrameMilliseconds.All(ms => ms > 0.0), "GPU 完成耗时为正");
                var sorted = run.Engine.GpuFrameMilliseconds.OrderBy(x => x).ToList();
                Assert.AreEqual(sorted[(int)Math.Ceiling(0.5 * sorted.Count) - 1], Num(run, "gpu_ms_p50"), 1e-9);
                Assert.AreEqual(sorted[(int)Math.Ceiling(0.95 * sorted.Count) - 1], Num(run, "gpu_ms_p95"), 1e-9);
                Assert.AreEqual(sorted[sorted.Count - 1], Num(run, "gpu_ms_max"), 1e-9);
            }
            else
            {
                Assert.IsFalse(run.Engine.GpuAvailable);
                Assert.AreEqual(0, run.Engine.GpuFrameMilliseconds.Count);
                Assert.IsNotEmpty(run.Engine.GpuUnavailableReason, "不可用必须写明原因");
                foreach (var name in new[] { "gpu_ms_p50", "gpu_ms_p95", "gpu_ms_max" })
                {
                    Assert.AreEqual(-1.0, Num(run, name), 0.0, name + "：不可用时是 -1 哨兵值（度量在场，不缺失）");
                }
            }

            var off = Host.Run(script, "2d_action", new EngineLabOptions { GpuTiming = false });
            Assert.AreEqual("unavailable", Str(off, "gpu_frame_status"), "关掉 GpuTiming 始终标不可用");
            Assert.AreEqual(-1.0, Num(off, "gpu_ms_p95"), 0.0);
            StringAssert.Contains("GpuTiming", off.Engine.GpuUnavailableReason);
            Assert.AreEqual(run.LogicProjection, off.LogicProjection, "GPU 计时（渲染到离屏纹理）不得改变逻辑");
            Assert.AreEqual(Host.RunHeadlessLogic(script, "2d_action"), run.LogicProjection, "与无头宿主逻辑一致");
        }

        // ───────── 三个平面组合 ─────────

        [Test]
        public void ThreePlanes_EachRunsItsOwnPresentation_AndLogicFingerprintsAreIdentical()
        {
            var script = LabHostTestSupport.Script("feel_melee");
            var expected = new Dictionary<string, (string Plane, string Rig)>
            {
                ["2d_action"] = ("2d", "sprite"),
                ["2_5d_action"] = ("2_5d", "sprite"),
                ["3d_action"] = ("3d", "model"),
            };
            string? reference = null;
            foreach (var pair in expected)
            {
                var run = Host.Run(script, pair.Key);
                Assert.AreEqual(pair.Value.Plane, run.Engine.Plane, pair.Key);
                Assert.AreEqual(pair.Value.Rig, run.Engine.RigKind, pair.Key + "：玩家 rig 种类由格子的外形决定");
                Assert.AreEqual(0, run.Engine.Errors.Count, pair.Key + "：" + string.Join(" | ", run.Engine.Errors));
                Assert.AreEqual(Host.RunHeadlessLogic(script, pair.Key), run.LogicProjection, pair.Key + "：与同格子无头宿主逻辑一致");
                reference ??= run.LogicProjection;
                // 逻辑文本含格子名之外的全部逻辑度量；三个平面格子在该脚本上的逻辑判定必须相同。
                Assert.AreEqual(reference, run.LogicProjection, pair.Key + "：三个平面组合的逻辑组指纹逐字节一致");
            }
        }

        // ───────── 相机相对输入 ─────────

        /// <summary>无头参照：提供固定偏航的朝向并要求 camera_relative（原生换算），用来和真实舞台相机的引擎运行比逻辑结果。</summary>
        private sealed class FixedYawReference : LabHostExtension
        {
            private readonly Orientation _orientation;

            public FixedYawReference(double yawRadians)
            {
                _orientation = new Orientation(yawRadians);
            }

            public override Core.Foundation.EngineAdapter.ICameraOrientation? CameraOrientation => _orientation;

            public override string? ControlSpaceOverride => ControlSpace.CameraRelative;

            private sealed class Orientation : Core.Foundation.EngineAdapter.ICameraOrientation
            {
                public Orientation(double yaw) => YawRadians = yaw;

                public double YawRadians { get; }
            }
        }

        [Test]
        public void CameraRelative_NativeConversionMatchesTheRealCameraAxes_ForEveryYawAndPitch()
        {
            // 复现 + 不变量：移动动作按框架原生 camera_relative 声明，输入映射按舞台相机的真实偏航换算；三向检验（真实相机右/上轴、偏航公式、
            // 回到屏幕）对每个偏航、每个俯仰（含透视）都在量化误差之内。期望方向由规则（相机轴、旋转公式）算出。
            var script = LabHostTestSupport.Script("diagonal");
            foreach (var pitch in new double?[] { null, 30.0, 60.0 })
            {
                foreach (var yaw in new[] { 0.0, 30.0, 90.0, 180.0, -45.0, 137.5 })
                {
                    var options = new EngineLabOptions
                    {
                        ControlSpaceOverride = ControlSpace.CameraRelative,
                        CameraYawDegrees = yaw,
                        CameraPitchDegrees = pitch,
                        CameraPerspective = pitch.HasValue,
                    };
                    var tag = $"yaw={yaw} pitch={(pitch.HasValue ? pitch.Value.ToString() : "无")}";
                    var run = Host.Run(script, "3d_targeted", options);
                    Assert.AreEqual(0, run.Engine.Errors.Count, tag + "：" + string.Join(" | ", run.Engine.Errors));
                    Assert.Greater(run.Engine.Controls.Count, 0, $"{tag}：前置条件——脚本里应有摇杆输入");
                    Assert.LessOrEqual(Num(run, "control_max_error_deg"), 0.01, $"{tag}：原生换算结果与真实相机轴/偏航公式的最大夹角（度）");
                    Assert.LessOrEqual(Num(run, "control_max_screen_error_deg"), 0.05, $"{tag}：世界方向经真实相机（含俯仰与透视）投影回屏幕与摇杆方向的最大夹角（度）");

                    // 世界移动方向：玩家实际位移 = 同偏航的无头原生换算（相同的框架实现，真实相机只提供偏航）。
                    var reference = Host.HeadlessRunner.Record(script, "3d_targeted", null, new FixedYawReference(yaw * Math.PI / 180.0));
                    var last = run.Recording.Ticks.Count - 1;
                    Assert.AreEqual(reference.Ticks[last].Position.X, run.Recording.Ticks[last].Position.X, 1e-3, $"{tag}：终点 X");
                    Assert.AreEqual(reference.Ticks[last].Position.Y, run.Recording.Ticks[last].Position.Y, 1e-3, $"{tag}：终点 Y");
                }
            }
        }

        [Test]
        public void CameraRelative_ZeroYawIsIdentity_AndWorldControlSpaceIsNeverConverted()
        {
            var script = LabHostTestSupport.Script("diagonal");
            var headless = Host.RunHeadlessLogic(script, "3d_targeted");
            var zero = Host.Run(script, "3d_targeted", new EngineLabOptions { ControlSpaceOverride = ControlSpace.CameraRelative, CameraYawDegrees = 0.0 });
            Assert.AreEqual(headless, zero.LogicProjection, "偏航 0 的相机相对转换是恒等变换，逻辑与世界控制空间逐字节一致");
            var world = Host.Run(script, "3d_targeted", new EngineLabOptions { CameraYawDegrees = 90.0 });
            Assert.AreEqual(headless, world.LogicProjection, "世界控制空间下相机偏航不得改变逻辑输入");
            Assert.AreEqual(0, world.Engine.Controls.Count, "世界控制空间不做转换，不产生转换样本");
            var pitched = Host.Run(script, "3d_targeted", new EngineLabOptions { CameraPitchDegrees = 50.0, CameraPerspective = true });
            Assert.AreEqual(headless, pitched.LogicProjection, "俯仰与透视只改相机姿态，不得改变逻辑");
        }

        [Test]
        public void CameraRelative_StageFailureFallsBackToTheOptionYaw_SoLogicDoesNotFork()
        {
            // 不变量：舞台装配失败（隔离层非法）时，相机朝向退回选项偏航，逻辑与装配成功时逐字节一致（引擎侧失败不得改变逻辑）。
            var script = LabHostTestSupport.Script("diagonal");
            var good = Host.Run(script, "3d_targeted", new EngineLabOptions { ControlSpaceOverride = ControlSpace.CameraRelative, CameraYawDegrees = 70.0 });
            var broken = Host.Run(
                script, "3d_targeted",
                new EngineLabOptions { ControlSpaceOverride = ControlSpace.CameraRelative, CameraYawDegrees = 70.0, IsolationLayer = 99 });
            Assert.Greater(broken.Engine.Errors.Count, 0);
            Assert.AreEqual(good.LogicProjection, broken.LogicProjection);
        }

        // ───────── 输入噪声记录与回放 ─────────

        [Test]
        public void InputNoise_LatencyDelaysTheLogicResponseByExactlyTheLatency_AndReplayReproducesIt()
        {
            var script = LabHostTestSupport.Script("move_tap");
            var plain = Host.Run(script, "2d_targeted");
            var firstBase = plain.Recording.Ticks.First(t => t.MoveRequested).Tick;
            var noise = new InputNoiseModel(5UL, 4, 0);
            var noisy = Host.Run(script, "2d_targeted", new EngineLabOptions { Noise = noise });
            Assert.AreEqual(firstBase + 4, noisy.Recording.Ticks.First(t => t.MoveRequested).Tick, "纯延迟噪声使逻辑响应恰好晚延迟的 tick 数");
            Assert.AreEqual(noise.Id, noisy.Engine.InputNoise, "引擎记录里带噪声模型标签");
            Assert.IsNotNull(noisy.NoiseRecord);

            var replay = Host.Run(script, "2d_targeted", new EngineLabOptions { NoiseReplay = noisy.NoiseRecord });
            Assert.AreEqual(noisy.LogicProjection, replay.LogicProjection, "按记录回放得到逐字节相同的逻辑指纹");
            Assert.AreEqual(Host.RunHeadlessLogic(noisy.EffectiveScript, "2d_targeted"), noisy.LogicProjection, "带噪脚本在无头宿主上的逻辑与引擎宿主一致");
        }

        // ───────── 引擎侧失败不改变逻辑 ─────────

        [Test]
        public void EngineFailure_IsRecordedAsAMetric_AndNeverChangesTheLogicFingerprint()
        {
            var script = LabHostTestSupport.Script("feel_melee");
            var broken = Host.Run(script, "2d_action", new EngineLabOptions { IsolationLayer = 99 });
            Assert.Greater(broken.Engine.Errors.Count, 0, "无效的隔离层应让舞台装配失败并被记录");
            Assert.Greater(Num(broken, "engine_errors"), 0.0, "失败折成 engine_errors 度量");
            Assert.AreEqual(Host.RunHeadlessLogic(script, "2d_action"), broken.LogicProjection, "引擎侧失败不得改变逻辑组指纹");
        }

        // ───────── 渲染隔离 ─────────

        private sealed class MaskProbe : LabHostExtension
        {
            public readonly List<string> Violations = new List<string>();
            public int Frames;
            private readonly Camera _other;
            private readonly int _layer;

            public MaskProbe(Camera other, int layer)
            {
                _other = other;
                _layer = layer;
            }

            public override void OnFrame(int frame, double alpha, double frameSeconds)
            {
                Frames++;
                if ((_other.cullingMask & (1 << _layer)) != 0)
                {
                    Violations.Add("frame " + frame + "：场内其它相机仍会渲染隔离层");
                }

                var root = GameObject.Find("EngineLabStage");
                if (root == null || root.layer != _layer)
                {
                    Violations.Add("frame " + frame + "：舞台根物体不在隔离层");
                }
            }
        }

        [Test]
        public void Isolation_StageHidesItsLayerFromOtherCameras_AndRestoresEverythingAfterwards()
        {
            var layer = 29;
            var go = new GameObject("IsolationOtherCamera");
            try
            {
                var other = go.AddComponent<Camera>();
                other.cullingMask = -1;
                var probe = new MaskProbe(other, layer);
                var stage = new EngineLabStage(new EngineLabOptions { IsolationLayer = layer });
                try
                {
                    var script = LabHostTestSupport.Script("feel_melee");
                    Host.Runner.Record(script, "2d_action", null, new CompositeLabHostExtension(stage, probe));
                    Assert.Greater(probe.Frames, 0);
                    Assert.IsEmpty(probe.Violations, string.Join("\n", probe.Violations));
                    Assert.AreEqual(1 << layer, stage.StageCamera!.cullingMask, "舞台相机只渲染隔离层");
                }
                finally
                {
                    stage.Dispose();
                }

                Assert.AreEqual(-1, other.cullingMask, "销毁后场内其它相机的剔除遮罩应恢复原值");
                Assert.IsNull(GameObject.Find("EngineLabStage"), "销毁后舞台物体应清除干净");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ───────── 冷热加载路径一致 ─────────

        [Test]
        public void ColdLoadPath_GivesTheSameFreezeAndImpulseObservationsAsTheHotPath()
        {
            var script = LabHostTestSupport.Script("feel_group_hit");
            var hot = Host.Run(script, "2d_action");
            var loader = UnityEngineHost.Ensure().ResourceLoader;
            loader.Unload(EngineLabStage.ProbeResourceId);
            loader.Unload(EngineLabStage.SwingSfxResource);
            loader.Unload(EngineLabStage.ImpactSfxResource);
            var cold = Host.Run(script, "2d_action");
            Assert.AreEqual(hot.Engine.Freezes.Count, cold.Engine.Freezes.Count);
            for (var i = 0; i < hot.Engine.Freezes.Count; i++)
            {
                Assert.AreEqual(hot.Engine.Freezes[i].ParticleAdvanceSeconds, cold.Engine.Freezes[i].ParticleAdvanceSeconds, 1e-9, "冻结 " + i);
                Assert.AreEqual(hot.Engine.Freezes[i].RigAdvanceSeconds, cold.Engine.Freezes[i].RigAdvanceSeconds, 1e-9, "冻结 " + i);
            }

            Assert.AreEqual(hot.Engine.CameraImpulses.Count, cold.Engine.CameraImpulses.Count);
            Assert.AreEqual(hot.LogicProjection, cold.LogicProjection);
        }

        // ───────── 脚本期望清单在引擎宿主上判定 ─────────

        [Test]
        public void Expectations_JudgedOnTheEngineHost_AgreeWithTheHeadlessJudgement()
        {
            var filter = LabHostTestSupport.CellFilter();
            var judged = 0;
            foreach (var script in Host.LoadScripts().Where(s => s.Expectations.Count > 0))
            {
                foreach (var cell in Host.RunnableCells(script))
                {
                    if (filter != null && !filter.Contains(cell))
                    {
                        continue;
                    }

                    var run = Host.Run(script, cell);
                    var onEngine = Host.JudgeExpectations(run);
                    var headlessFingerprint = Host.RunHeadless(script, cell);
                    var onHeadless = LabSuite.EvaluateExpectations(
                        Host.HeadlessRunner, script, cell, headlessFingerprint, new Dictionary<string, Fingerprint>(StringComparer.Ordinal));
                    Assert.AreEqual(onHeadless.Count, onEngine.Count, script.Meta.ScriptId + "@" + cell);
                    for (var i = 0; i < onEngine.Count; i++)
                    {
                        Assert.AreEqual(onHeadless[i].Status, onEngine[i].Status, script.Meta.ScriptId + "@" + cell + " " + onEngine[i].Expectation.Id);
                    }

                    judged += onEngine.Count;
                }
            }

            Assert.Greater(judged, 0, "至少应判定过一条期望");
        }

        // ───────── 换装场景：图标与逐层剪辑 ─────────

        private static (int W, int H) PngSize(string path)
        {
            using var stream = File.OpenRead(path);
            var header = new byte[24];
            Assert.AreEqual(24, stream.Read(header, 0, 24));
            int Be(int o) => (header[o] << 24) | (header[o + 1] << 16) | (header[o + 2] << 8) | header[o + 3];
            return (Be(16), Be(20));
        }

        private static (int Frames, int W, int H) FramesJson(string reference)
        {
            var path = Path.Combine(Application.streamingAssetsPath, "GameFoundation",
                AssetRefConventions.SpriteAnimFramesFile(new Id(reference)).Replace('/', Path.DirectorySeparatorChar));
            var text = File.ReadAllText(path);
            var frames = Regex.Matches(text, "\"index\"\\s*:").Count;
            var w = int.Parse(Regex.Match(text, "\"frame_w\"\\s*:\\s*(\\d+)").Groups[1].Value);
            var h = int.Parse(Regex.Match(text, "\"frame_h\"\\s*:\\s*(\\d+)").Groups[1].Value);
            return (frames, w, h);
        }

        [Test]
        public void EquipAudit_LoadedIconsAndLayerClips_MatchTheirFilesOnDisk()
        {
            var script = LabHostTestSupport.Script("equip_cycle");
            var run = Host.Run(script, "2d_action");
            var audits = run.Engine.LayerAudits;
            Assert.Greater(audits.Count, 0, "换装脚本应产生图层/图标核对样本");
            Assert.AreEqual(audits.Count, (int)Num(run, "layer_audit_count"));

            var icons = audits.Where(a => a.Layer.StartsWith("icon:", StringComparison.Ordinal)).ToList();
            Assert.Greater(icons.Count, 0);
            Assert.IsTrue(icons.All(i => !i.Mismatch), "图标经适配器资源加载器的 icon 路径加载：占位美术里的图标都应加载得到（缺失会标不一致）");
            foreach (var icon in icons.Where(i => !i.Mismatch))
            {
                // 文件位置由适配器自己的路径规则给出（宿主不再自己找文件、自己解码）。
                var file = UnityResourceLoader.ResolvePath(new Id(icon.Resource), ResourceKind.Image);
                var size = PngSize(file);
                Assert.AreEqual(size.W, icon.Width, icon.Resource + "：引擎解码的图标宽应等于 PNG 文件头声明的宽");
                Assert.AreEqual(size.H, icon.Height, icon.Resource + "：引擎解码的图标高应等于 PNG 文件头声明的高");
            }

            foreach (var layer in audits.Where(a => !a.Layer.StartsWith("icon:", StringComparison.Ordinal) && !a.Mismatch))
            {
                var expected = FramesJson(layer.Resource);
                Assert.AreEqual(expected.Frames, layer.FrameCount, layer.Resource + "：实际加载的帧数应等于 frames.json 声明的帧数");
                Assert.AreEqual(expected.W, layer.Width, layer.Resource + "：实际加载的帧宽");
                Assert.AreEqual(expected.H, layer.Height, layer.Resource + "：实际加载的帧高");
            }

            Assert.AreEqual(audits.Count(a => a.Mismatch), (int)Num(run, "layer_audit_mismatch"), "不一致数度量应等于样本里标不一致的个数");
        }

        [Test]
        public void EquipAudit_FlagsMissingClipsAndFrameCountMismatches_AndPassesMatchingPairs()
        {
            var loader = UnityEngineHost.Ensure().ResourceLoader;
            void Pump() => EngineLabStage.PumpLoader(loader, 5000);
            const string body = "sprite_anim.std_dummy_attack_2h__front__body";
            var same = EquipLayerAudit.CompareClips(loader, Pump, "player", "same", body, body);
            Assert.IsFalse(same.Mismatch, "同一剪辑与自身对照应一致");
            Assert.Greater(same.FrameCount, 0);

            var missing = EquipLayerAudit.CompareClips(loader, Pump, "player", "missing", "sprite_anim.no_such_clip__front__hand_main", body);
            Assert.IsTrue(missing.Mismatch, "图层剪辑加载不出来必须标不一致");
            Assert.AreEqual(0, missing.FrameCount);

            var other = EquipLayerAudit.CompareClips(loader, Pump, "player", "other", "sprite_anim.std_dummy_idle__front__body", body);
            Assert.AreNotEqual(FramesJson("sprite_anim.std_dummy_idle__front__body").Frames, FramesJson(body).Frames, "前置条件：两条剪辑帧数不同");
            Assert.IsTrue(other.Mismatch, "帧数与参照不一致必须标不一致");
        }
    }
}
