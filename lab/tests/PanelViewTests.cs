using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Feel;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 帧数据时间轴、轨迹叠层与评分面板的视图模型（ADR-0150）：全部派生自录制，不改逻辑；
    /// 期望值取自录制里的原始事件与数据（相位 tick、命中几何、顿帧时长），不写裸数。
    /// </summary>
    public sealed class PanelViewTests
    {
        private const string Cell = "2d_action";
        private const string Attack = "input.action.lab_a_attack";

        private static void Frames(LabSession s, int n) => PanelTuningTests.Frames(s, n);

        private static void Tap(LabSession session, string action, int holdFrames = 2)
        {
            session.Inject(new ScriptEvent(0, action, ScriptEventKind.Press));
            Frames(session, holdFrames);
            session.Inject(new ScriptEvent(0, action, ScriptEventKind.Release));
        }

        /// <summary>一局：出木桩（略偏离正前方，让目标辅助有转角）、连按两次攻击打中，再跑到动作结束。</summary>
        private static (LabRunner Runner, LabSession Session, TuningPanel Panel) PlayHit(Action<LabSession>? between = null)
        {
            var (runner, session, panel) = PanelTuningTests.Open(null, "panel_view_test");
            session.Inject(new ScriptEvent(0, "stake", ScriptEventKind.Spawn, new Vec2(1.6, 0.9)));
            Frames(session, 5);
            Tap(session, Attack);
            Frames(session, 6);
            between?.Invoke(session);
            Tap(session, Attack);
            Frames(session, 70);
            return (runner, session, panel);
        }

        // ---------------------------------------------------------------- 时间轴

        [Fact]
        public void Timeline_BuildsPhaseBarsInputMarksWindowsHitstopAndResults_FromTheRecording()
        {
            var (_, session, _) = PlayHit();
            var recording = session.Recording;
            var feel = recording.Feel!;
            var model = new TimelineModel();
            model.Sync(recording, session.Script, session.Tick);
            model.Sync(recording, session.Script, session.Tick); // 增量同步幂等

            var player = model.Track(TimelineModel.PlayerLabel)!;
            // 相位色条：起点就是录制里 action_phase 事件的 tick，且首尾相接（前摇 → 判定 → 后摇）；零长度的相位（缓冲输入一进后摇就取消）不进轨道。
            Assert.DoesNotContain(player.Segments, s => !s.IsOpen && s.End <= s.Start);
            var phaseTicks = feel.Events.Where(e => e.Kind == "action_phase" && e.Actor == "player").ToList();
            var startup = player.Segments.Last(s => s.Kind == SegmentKind.Startup);
            var active = player.Segments.Last(s => s.Kind == SegmentKind.Active);
            var recovery = player.Segments.Last(s => s.Kind == SegmentKind.Recovery);
            Assert.Equal(phaseTicks.Last(e => e.Detail == "Startup").Tick, startup.Start);
            Assert.Equal(phaseTicks.Last(e => e.Detail == "Active").Tick, active.Start);
            Assert.Equal(phaseTicks.Last(e => e.Detail == "Recovery").Tick, recovery.Start);
            Assert.Equal(startup.End, active.Start);
            Assert.Equal(active.End, recovery.Start);
            Assert.Equal(feel.Events.Last(e => e.Kind == "action_finished").Tick, recovery.End);

            // 输入标记：按下与抬起都在玩家轨上，tick 取自会话脚本里盖的戳。
            foreach (var e in session.Script.Events.Where(x => x.Kind == ScriptEventKind.Press && x.Action == Attack))
            {
                Assert.Contains(player.Marks, m => m.Kind == MarkKind.Input && m.Tick == e.Tick && m.Text.StartsWith("↓", StringComparison.Ordinal));
            }

            // 窗口：连招窗口开闭成一条有长度的色条，并随动作结束收尾（没有残留开着的窗口）。
            Assert.Contains(player.Segments, s => s.Kind == SegmentKind.ComboWindow && !s.IsOpen && s.End > s.Start);
            Assert.DoesNotContain(player.Segments, s => s.IsOpen && s.Kind != SegmentKind.Recovery && s.End < 0 && model.LastTick > s.Start + 120);

            // 命中点与裁决结果：攻击方轨有命中点，靶子轨有裁决结果与受击反应；顿帧色条长度 = 命中事件携带的顿帧 tick。
            var hit = feel.Events.First(e => e.Kind == "hit_confirmed");
            Assert.Contains(player.Marks, m => m.Kind == MarkKind.Hit && m.Tick == hit.Tick);
            var stake = model.Tracks.First(t => t.Entity != TimelineModel.PlayerLabel);
            Assert.Contains(stake.Marks, m => m.Kind == MarkKind.Result && m.Tick == hit.Tick && m.Text.StartsWith(hit.Detail, StringComparison.Ordinal));
            Assert.Contains(stake.Segments, s => s.Kind == SegmentKind.Reaction);
            var hitstop = feel.Events.First(e => e.Kind == "hitstop_started");
            Assert.Contains(player.Segments, s => s.Kind == SegmentKind.Hitstop && s.Start == hitstop.Tick && s.End - s.Start == hitstop.A);
        }

        [Fact]
        public void Timeline_BufferSlots_AndScrubbingAreReadOnly_LogicIsByteIdenticalWithOrWithoutScrubbing()
        {
            string Run(bool scrub)
            {
                var model = new TimelineModel();
                var (runner, session, _) = PlayHit(s =>
                {
                    if (scrub)
                    {
                        model.Sync(s.Recording, s.Script, s.Tick);
                        model.Pin(2);
                        model.StepCursor(3);
                        model.Describe(s.Recording);
                        model.Follow();
                    }
                });
                var live = session.Finish();
                return runner.FingerprintOf(session.Script, Cell, live).Project(runner.Registry, MetricClass.Logic);
            }

            Assert.Equal(Run(false), Run(true));

            // 缓冲槽：第二次按攻击发生在第一击动作进行中，那几个 tick 的缓冲槽快照里有它。
            var (_, session2, _) = PlayHit();
            var feel = session2.Recording.Feel!;
            Assert.Contains(feel.Ticks, t => t.BufferSlots.Length > 0);
            var withBuffer = feel.Ticks.First(t => t.BufferSlots.Length > 0);
            Assert.Equal(withBuffer.BufferSlots, TimelineModel.BufferAt(session2.Recording, withBuffer.Tick));

            var model2 = new TimelineModel();
            model2.Sync(session2.Recording, session2.Script, session2.Tick);
            Assert.Equal(model2.LastTick, model2.Cursor);
            model2.Pin(withBuffer.Tick);
            Assert.Equal(withBuffer.Tick, model2.Cursor);
            Assert.Contains(model2.Describe(session2.Recording), l => l.StartsWith("player：", StringComparison.Ordinal) && l.Contains("缓冲槽[" + withBuffer.BufferSlots + "]"));
            model2.Pin(100000);
            Assert.Equal(model2.LastTick, model2.Cursor);
            model2.Pin(-5);
            Assert.Equal(0, model2.Cursor);
        }

        // ---------------------------------------------------------------- 轨迹

        [Fact]
        public void Trajectory_ContactsLieOnTheRecordedHitShape_NormalsAreUnit_AssistAnglesAreTheRecordedOnes()
        {
            var (_, session, _) = PlayHit();
            var recording = session.Recording;
            var feel = recording.Feel!;
            var last = session.Tick;

            var shapes = TrajectoryModel.HitShapes(recording, 0, last);
            Assert.NotEmpty(shapes);
            foreach (var view in shapes)
            {
                Assert.NotEmpty(view.Outline);
                Assert.Equal("player", view.Source.Actor);
                Assert.NotEmpty(view.Sweep); // 判定相内逐 tick 的扫掠体
                foreach (var pair in view.Sweep)
                {
                    Assert.NotEmpty(pair.Value);
                }
            }

            var contacts = TrajectoryModel.Contacts(recording, 0, last);
            var hits = feel.Events.Where(e => e.Kind == "hit_confirmed" && e.HasGeometry).ToList();
            Assert.Equal(hits.Count, contacts.Count);
            Assert.NotEmpty(contacts);
            foreach (var contact in contacts)
            {
                // 接触点是"形状区域内离目标中心最近的点"：必然落在该次判定形状内或边界上。
                var owner = shapes.Where(v => v.Source.Tick <= contact.Tick).OrderByDescending(v => v.Source.Tick).First();
                var closest = ShapeGeometry.ClosestPoint(owner.Source.Shape, contact.Point);
                Assert.True((closest - contact.Point).Length < 1e-6, "接触点 " + contact.Point + " 不在形状上");
                Assert.InRange(contact.Normal.Length, 0.999, 1.001);
            }

        }

        [Fact]
        public void Trajectory_AssistAngles_AreTheRecordedOnes_AndTheTimelineMarksThem()
        {
            // 目标辅助由扑击技能声明（实验室动作式数据）：把攻击键绑到扑击，木桩在 45 度方向。
            var (_, session, _) = PanelTuningTests.Open(
                null, "panel_assist_test", new[] { new KeyValuePair<string, string>("lab_a.attack", "skill.lab_a_lunge") });
            session.Inject(new ScriptEvent(0, "stake", ScriptEventKind.Spawn, new Vec2(1.6, 1.6)));
            Frames(session, 5);
            Tap(session, Attack);
            Frames(session, 80);
            var recording = session.Recording;
            var assistEvents = recording.Feel!.Events.Where(e => e.Kind == "target_assisted").ToList();
            Assert.NotEmpty(assistEvents);
            var assists = TrajectoryModel.Assists(recording, 0, session.Tick);
            Assert.Equal(assistEvents.Count, assists.Count);
            for (var i = 0; i < assists.Count; i++)
            {
                Assert.Equal(assistEvents[i].D, assists[i].DeltaDegrees, 9);
                Assert.Equal(assistEvents[i].Tick, assists[i].Tick);
                Assert.NotEqual(assists[i].From, assists[i].To);
            }

            Assert.NotEqual(0.0, assists[0].DeltaDegrees);
            var model = new TimelineModel();
            model.Sync(recording, session.Script, session.Tick);
            Assert.Contains(model.Track("player")!.Marks, m => m.Kind == MarkKind.Assist && m.Tick == assistEvents[0].Tick);
        }

        [Fact]
        public void Trajectory_PathsAndVelocityVectors_FollowTheRecordedPositions()
        {
            var (runner, session, _) = PanelTuningTests.Open(null, "panel_path_test");
            session.Inject(new ScriptEvent(0, "input.action.move", ScriptEventKind.Axis, new Vec2(1.0, 0.0)));
            Frames(session, 30);
            var recording = session.Recording;
            var path = TrajectoryModel.PlayerPath(recording, 0, session.Tick);
            Assert.Equal(recording.Ticks.Count, path.Count);
            Assert.True(path[path.Count - 1].X > path[0].X, "向右移动，路径 x 应增大");

            var velocities = TrajectoryModel.PlayerVelocities(recording, 0, session.Tick, 5);
            Assert.NotEmpty(velocities);
            foreach (var v in velocities)
            {
                // 速度矢量 = 相邻两个 tick 位置之差 / 步长，与录制里的速度模长一致。
                var sample = recording.Feel!.Ticks.First(t => t.Tick == v.Tick);
                Assert.Equal(sample.Speed, v.Velocity.Length, 3);
            }

            Assert.Empty(TrajectoryModel.HitShapes(recording, 0, session.Tick));
            Assert.Empty(TrajectoryModel.Contacts(recording, 0, session.Tick));
            Assert.NotEmpty(runner.Registry.Groups);
        }

        [Fact]
        public void ShapeOutlines_AreClosedPolylinesAroundTheShape()
        {
            var circle = TrajectoryModel.Outline(Shape.Circle(new Vec2(1, 2), 3));
            Assert.All(circle, p => Assert.Equal(3.0, (p - new Vec2(1, 2)).Length, 9));
            var cone = TrajectoryModel.Outline(Shape.Cone(Vec2.Zero, 0.0, Math.PI / 2, 2.0));
            Assert.Equal(Vec2.Zero, cone[0]);
            Assert.All(cone.Skip(1), p => Assert.Equal(2.0, p.Length, 9));
            var line = TrajectoryModel.Outline(Shape.Line(Vec2.Zero, 0.0, 4.0, 2.0));
            Assert.Equal(4, line.Count);
            Assert.All(line, p => Assert.True(ShapeGeometry.ClosestPoint(Shape.Line(Vec2.Zero, 0.0, 4.0, 2.0), p).Equals(p) || (ShapeGeometry.ClosestPoint(Shape.Line(Vec2.Zero, 0.0, 4.0, 2.0), p) - p).Length < 1e-9));
            var rect = TrajectoryModel.Outline(Shape.Rect(new Vec2(5, 5), new Vec2(1, 2), 0.0));
            Assert.Equal(4, rect.Count);
            Assert.Contains(new Vec2(6, 7), rect);
        }

        // ---------------------------------------------------------------- 评分

        [Fact]
        public void Rating_ScoresTagsAndSummary_AreValidated_AndSavedLocallyWithPresetVersionFingerprintAndDevice()
        {
            var (runner, session, panel) = PlayHit();
            var rating = new RatingModel();
            Assert.False(rating.Complete);
            Assert.Throws<InvalidOperationException>(() => rating.Submit(session.Tick, panel.ActiveSlot));
            Assert.Throws<ArgumentOutOfRangeException>(() => rating.Rate(RatingDimensions.Move, 6));
            Assert.Throws<ArgumentException>(() => rating.Rate("speed", 3));
            Assert.Throws<ArgumentException>(() => rating.ToggleTag("不存在"));

            rating.Rate(RatingDimensions.Move, 4);
            rating.Rate(RatingDimensions.Turn, 3);
            rating.Rate(RatingDimensions.HitWeight, 5);
            rating.Rate(RatingDimensions.Combo, 4);
            rating.ToggleTag("飘");
            rating.ToggleTag("空挥");
            rating.ToggleTag("慢");
            rating.ToggleTag("慢"); // 再点一次取消
            rating.Note = "第一次";
            var first = rating.Submit(session.Tick, panel.ActiveSlot);
            Assert.Equal(new[] { "飘", "空挥" }, first.Tags.ToArray());
            Assert.False(rating.Complete); // 提交后草稿清空

            foreach (var d in RatingDimensions.All)
            {
                rating.Rate(d, 2);
            }

            rating.Submit(session.Tick, panel.ActiveSlot);
            var summary = rating.Summary();
            Assert.Equal(3.0, summary[RatingDimensions.Move], 6);
            Assert.Equal(2.5, summary[RatingDimensions.Turn], 6);
            Assert.Equal(3.5, summary[RatingDimensions.HitWeight], 6);
            Assert.Equal(3.0, summary[RatingDimensions.Combo], 6);

            // 上下文：预设版本取自数据行；指纹哈希同一局稳定、换了输入就变。
            var context = RatingModel.BuildContext(runner, session, panel, Cell, "pc_keyboard_mouse; 测试机", "2026-10-04");
            Assert.Equal(panel.ActivePreset, context.ProfileRef);
            Assert.Equal(1, context.ProfileVersion);
            Assert.Equal(runner.DatasetHashFor(session.Script, Cell), context.DataRootVersion);
            Assert.Equal(context.FingerprintHash, RatingModel.FingerprintHash(runner, session.Script, session.Tick, Cell));
            Assert.Equal(16, context.FingerprintHash.Length);
            Assert.NotEqual(context.FingerprintHash, RatingModel.FingerprintHash(runner, session.Script, session.Tick - 20, Cell));

            var dir = Path.Combine(Path.GetTempPath(), "feel_panel_tests", "rating_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var file = Path.Combine(dir, "rating.json");
            rating.SaveLocal(file, context);
            var text = File.ReadAllText(file);
            Assert.Contains("\"fingerprint_hash\": \"" + context.FingerprintHash + "\"", text);
            Assert.Contains("\"device\": \"pc_keyboard_mouse; 测试机\"", text);
            Assert.Contains("\"profile_version\": 1", text);
            Assert.Contains("\"飘\"", text);
            Assert.Contains("\"note\": \"第一次\"", text);
        }

        [Fact]
        public void Rating_ExportsAFeelValidationTable_ThatLoadsAsADataRoot_AndFeedsTheMaturityLedger()
        {
            var (runner, session, panel) = PlayHit();
            var rating = new RatingModel();
            foreach (var d in RatingDimensions.All)
            {
                rating.Rate(d, 4);
            }

            rating.ToggleTag("黏");
            rating.Submit(session.Tick, panel.ActiveSlot);
            var context = RatingModel.BuildContext(runner, session, panel, Cell, "pc_keyboard_mouse", "2026-10-04");
            var dir = Path.Combine(Path.GetTempPath(), "feel_panel_tests", "validation_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Assert.Throws<ArgumentException>(() => rating.ExportValidation(dir, "bad.id", context));
            var path = rating.ExportValidation(dir, "feel.validation.panel_test", context);
            Assert.Equal(Path.Combine(dir, "feel", "feel.validation.json"), path);
            rating.ExportValidation(dir, "feel.validation.panel_test", context); // 同 id 替换，不重复
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(path), "\"id\": \"feel.validation.panel_test\""));

            // 表文件是合法数据根：按 feel.validation 的 schema 校验通过（装配会校验），账本里能读到这条记录，数值与评分摘要一致。
            var script = LabLive.CreateScript("panel_validation_load", 60, 60, PanelTuningTests.PlaygroundRoots.Concat(new[] { dir }).ToArray());
            var fresh = runner.StartLive(script, Cell, null, null);
            Frames(fresh, 2);
            var ledger = FeelValidationLedger.FromRegistry(fresh.Context!.World.Registry);
            var record = Assert.Single(ledger.Records);
            Assert.Equal(context.ProfileRef, record.ProfileRef);
            Assert.Equal(context.ProfileVersion, record.ProfileVersion);
            Assert.Equal(Cell, record.Cell);
            Assert.Equal(context.Device, record.Device);
            foreach (var d in RatingDimensions.All)
            {
                Assert.Equal(4.0, record.Scores[d], 6);
            }

            Assert.True(record.MeetsThreshold); // 四项都是 4，达到升级门槛（是否标 validated 仍是游戏仓库的人工决定）

            Assert.Throws<InvalidOperationException>(() => new RatingModel().ExportValidation(dir, "feel.validation.x", context));
        }
        [Fact]
        public void CameraFraming_FollowsTheTemplatePreset_AndIsNoneForOtherPresets()
        {
            var (_, session, panel) = PanelTuningTests.Open();
            var registry = session.Context!.World.Registry;
            Assert.False(CameraFraming.For(registry, panel.ActivePreset).Found, "缺省预设不是模板，没有镜头配置行");
            Assert.Equal(1.0, CameraFraming.For(registry, panel.ActivePreset).ZoomRatio);
            var ratios = new System.Collections.Generic.Dictionary<string, double>();
            foreach (var style in new[] { "classic", "agile", "heavy", "horde", "precise" })
            {
                var framing = CameraFraming.For(registry, "feel.preset.tpl_" + style);
                Assert.True(framing.Found, style);
                var row = registry.Get("camera_profile", "camera_profile.tpl_" + style)!;
                Assert.Equal(row.GetNumber("zoom_default") / CameraFraming.ReferenceZoom, framing.ZoomRatio, 9);
                Assert.Equal(row.GetNumber("pitch_degrees"), framing.PitchDegrees);
                Assert.Equal(row.GetNumber("follow_lerp"), framing.FollowLerp);
                ratios[style] = framing.ZoomRatio;
            }

            Assert.NotEqual(ratios["agile"], ratios["heavy"]);
        }
        [Fact]
        public void Fingerprint_BuildsAndReplays_WhenDummiesComeAndGoDuringARun()
        {
            // 复现：交互式试玩里靶子中途出场、被清掉，每 tick 的靶子位置清单长度不同；移动指标组不得因此越界，且无头重放逐字节一致。
            var (runner, session, _) = PanelTuningTests.Open(id: "panel_dummies_come_and_go");
            session.Inject(new ScriptEvent(0, "stake", ScriptEventKind.Spawn, new Vec2(1.6, 0.9)));
            Frames(session, 20);
            session.Inject(new ScriptEvent(0, "mob", ScriptEventKind.Spawn, new Vec2(-1.6, 0.9)));
            Frames(session, 20);
            session.Inject(new ScriptEvent(0, "clear", ScriptEventKind.ClearDummies));
            Frames(session, 20);
            var script = session.Script;
            var recording = session.Finish();
            var tickCounts = recording.Feel!.Ticks.Select(t => t.TargetPositions.Count).Distinct().OrderBy(n => n).ToList();
            Assert.True(tickCounts.Count > 1, "测试前提：各 tick 的靶子清单长度确实不同");
            var live = runner.FingerprintOf(script, Cell, recording).Project(runner.Registry, MetricClass.Logic);
            var replay = runner.FingerprintOf(script, Cell, runner.Record(script, Cell)).Project(runner.Registry, MetricClass.Logic);
            Assert.Equal(live, replay);
        }
    }
}
