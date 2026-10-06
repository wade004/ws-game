#nullable enable
// LabPlaygroundPanelsTests：试玩宿主的实验室面板冒烟（ADR-0150，手感设计 06 第 3.4 节、第 4 节）。
// 内核里的视图模型（字段覆盖、来源链、改值→覆盖事件→无头逐字节重放、写回差异）由无头测试 lab/tests 守住；这里只确认引擎宿主里
// "分页能切、改一个字段的结果与数据折算一致、模板取景随模板变、本地产物写得出来"。期望值由数据与规则算出，不写裸数。
using Adapter.Unity;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using FeelLab.Unity;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Lab;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FeelLab.Unity.Tests
{
    [Category("module:lab")]
    public sealed class LabPlaygroundPanelsTests
    {
        private const double Frame = 1.0 / 60.0;
        private const string AttackKey = "j";
        private GameObject? _go;
        private LabPlayground? _pg;
        private Adapters.Stub.StubInput? _input;
        private string _saveDir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "lab_panels_test_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            Dispose();
            if (Directory.Exists(_saveDir))
            {
                Directory.Delete(_saveDir, true);
            }
        }

        private void Dispose()
        {
            _pg?.End();
            if (_go != null)
            {
                UnityEngine.Object.DestroyImmediate(_go);
            }

            _go = null;
            _pg = null;
        }

        private LabPlayground NewPlayground()
        {
            _go = new GameObject("LabPlaygroundPanelsTest");
            var pg = _go.AddComponent<LabPlayground>();
            pg.Configure("2d_action");
            pg.ManualDrive = true;
            _input = new Adapters.Stub.StubInput();
            pg.InputSource = _input;
            pg.PadReader = _ => false;
            pg.SaveDirectory = _saveDir;
            Assert.IsTrue(pg.Begin(), pg.Model.Status);
            _pg = pg;
            return pg;
        }

        private void Frames(int n)
        {
            for (var i = 0; i < n; i++)
            {
                _pg!.Tick(Frame);
                _pg.FinishFrame();
            }
        }

        private void Tap(string key, int hold = 3)
        {
            _input!.Press(key);
            Frames(hold);
            _input.Release(key);
        }

        private System.Collections.Generic.List<FeelEventRecord> PlayerHits() =>
            _pg!.Session!.Recording.Feel!.Events.Where(e => e.Kind == "hit_confirmed" && e.Actor == "player").ToList();

        private double Resolved(string field)
        {
            var ctx = _pg!.Session!.Context!;
            return ctx.World.Gameplay.Feel!.Feel.Resolver.Resolve(ctx.PlayerId).GetAbsolute(field).AsNumber();
        }

        // ───────── 分页 ─────────

        [Test]
        public void Tabs_Cycle_ThroughAllFivePages_AndBackToScene()
        {
            var pg = NewPlayground();
            Assert.AreEqual(LabTab.Scene, pg.Model.Tab);
            Assert.IsNotNull(pg.Tuning, "试玩会话带调参面板");
            var seen = new System.Collections.Generic.List<LabTab> { pg.Model.Tab };
            for (var i = 0; i < 5; i++)
            {
                pg.CycleTab();
                seen.Add(pg.Model.Tab);
            }

            CollectionAssert.AreEqual(
                new[] { LabTab.Scene, LabTab.Tuning, LabTab.Timeline, LabTab.Trajectory, LabTab.Rating, LabTab.Scene }, seen);
            foreach (var tab in new[] { LabTab.Tuning, LabTab.Timeline, LabTab.Trajectory, LabTab.Rating })
            {
                pg.SetTab(tab);
                Frames(2);
                Assert.AreEqual(tab, pg.Model.Tab);
            }
        }

        [Test]
        public void TuningPage_RowsCoverEveryRegisteredField_InSevenGroups()
        {
            var pg = NewPlayground();
            var tuning = pg.Tuning!;
            Assert.AreEqual(7, tuning.Groups.Count);
            var rows = tuning.AllRows();
            Assert.AreEqual(tuning.Fields.Count, rows.Count, "每个登记字段恰好一行");
            Assert.IsTrue(rows.All(r => r.Provenance != null));
        }

        // ───────── 改一个顿帧字段：命中顿帧 tick 数 = 数据折算 ─────────

        private static int Ticks(double ms, double step) => FeelCalibration.MillisecondsToTicks(ms, step);

        [Test]
        public void ChangingAttackerHitstop_ChangesTheHitStopTickByDataDerivedConversion_AndReplaysByteIdentical()
        {
            var pg = NewPlayground();
            var step = pg.Session!.StepSeconds;
            pg.SpawnDummy("stake");
            Frames(4);
            var baselineMs = Resolved(FeelFieldNames.AttackerHitstopMs);
            var capMs = Resolved(FeelFieldNames.AttackerHitstopCapMs);
            double newMs = baselineMs;
            foreach (var factor in new[] { 0.5, 0.25, 0.1 })
            {
                var candidate = Math.Round(baselineMs * factor);
                if (Ticks(candidate, step) != Ticks(baselineMs, step))
                {
                    newMs = candidate;
                    break;
                }
            }

            Assert.AreNotEqual(Ticks(baselineMs, step), Ticks(newMs, step), "测试前提：新时长折算后的 tick 数必须与基准不同");

            pg.SetTab(LabTab.Tuning);
            pg.Tuning!.SetValue(FeelFieldNames.AttackerHitstopMs, FeelValue.Of(newMs));
            Assert.IsTrue(pg.Tuning.Row(FeelFieldNames.AttackerHitstopMs).Pending, "事件已注入但还没经过一个固定步");
            Frames(2);
            var row = pg.Tuning.Row(FeelFieldNames.AttackerHitstopMs);
            Assert.IsFalse(row.Pending);
            Assert.AreEqual(newMs, row.Raw.AsNumber(), 1e-9);
            Assert.AreEqual(8, row.SourceLayer, "第 8 层（调试覆盖）是来源");
            Assert.IsTrue(row.Overridden);

            Tap(AttackKey);
            Frames(40);
            var hits = PlayerHits();
            Assert.GreaterOrEqual(hits.Count, 1);
            var expected = Math.Min(Ticks(newMs, step), Ticks(capMs, step));
            Assert.AreEqual(expected, hits[0].B, "命中的攻击方顿帧 tick = min(ticks(新时长), ticks(上限))");
            Debug.Log("[LabPlaygroundPanelsTests] hitstop: baseline=" + Ticks(baselineMs, step) + " tick new=" + hits[0].B + " (ms " + baselineMs + " -> " + newMs + ", cap " + capMs + ")");

            var path = pg.End();
            var live = pg.FinalRecording!;
            var liveLogic = pg.Host!.Runner.FingerprintOf(pg.Session.Script, "2d_action", live)
                .Project(pg.Host.HeadlessRunner.Registry, MetricClass.Logic);
            var replay = pg.Host.RunHeadlessLogic(InputScript.Parse(File.ReadAllText(path)), "2d_action");
            Assert.AreEqual(liveLogic, replay, "面板改值落成的覆盖事件，无头重放逻辑指纹逐字节一致");
            Assert.IsTrue(InputScript.Parse(File.ReadAllText(path)).Events.Any(e => e.Kind == ScriptEventKind.Override && e.Action == FeelFieldNames.AttackerHitstopMs));
        }

        [Test]
        public void AbSlots_EachKeepTheirOwnOverrides()
        {
            var pg = NewPlayground();
            var baseline = Resolved(FeelFieldNames.AttackerHitstopMs);
            var marker = baseline + 7.0;
            pg.Tuning!.SetValue(FeelFieldNames.AttackerHitstopMs, FeelValue.Of(marker));
            Frames(2);
            Assert.AreEqual(marker, Resolved(FeelFieldNames.AttackerHitstopMs), 1e-9);

            pg.SwitchAb();
            Frames(2);
            Assert.AreEqual('B', pg.Model.ActiveSlot);
            Assert.AreNotEqual(marker, Resolved(FeelFieldNames.AttackerHitstopMs), "B 槽没有这条覆盖");
            Assert.AreEqual(0, pg.Tuning.ActiveWrites.Count);

            pg.SwitchAb();
            Frames(2);
            Assert.AreEqual('A', pg.Model.ActiveSlot);
            Assert.AreEqual(marker, Resolved(FeelFieldNames.AttackerHitstopMs), 1e-9, "切回 A 槽，覆盖原样回来");
        }

        // ───────── 时间轴 ─────────

        [Test]
        public void Timeline_ShowsTheSwingPhases_AndScrubbingDoesNotAdvanceTheSession()
        {
            var pg = NewPlayground();
            pg.SpawnDummy("stake");
            Frames(4);
            Tap(AttackKey);
            Frames(40);
            var player = pg.Timeline.Track(TimelineModel.PlayerLabel);
            Assert.IsNotNull(player);
            Assert.IsTrue(player!.Segments.Any(s => s.Kind == SegmentKind.Active), "判定相色条");
            Assert.IsTrue(player.Marks.Any(m => m.Kind == MarkKind.Hit), "命中点标记");
            Assert.AreEqual(pg.Session!.Tick - 1, pg.Timeline.LastTick, "时间轴同步到最新 tick");

            pg.TogglePause();
            var tick = pg.Session.Tick;
            var hit = player.Marks.First(m => m.Kind == MarkKind.Hit);
            pg.Timeline.Pin(hit.Tick);
            Frames(5);
            Assert.AreEqual(tick, pg.Session.Tick, "暂停后拖动游标只读录制，会话不前进");
            Assert.AreEqual(hit.Tick, pg.Timeline.Cursor);
            var lines = pg.Timeline.Describe(pg.Session.Recording);
            Assert.IsTrue(lines.Any(l => l.StartsWith("player", StringComparison.Ordinal)));
            pg.TogglePause();
            Assert.IsFalse(pg.Timeline.Pinned.HasValue, "继续运行后游标回到跟随最新");
        }

        // ───────── 模板取景 ─────────

        [Test]
        public void StageCamera_FollowsTheTemplatePreset_AndRestoresDefaultsForNonTemplatePresets()
        {
            var pg = NewPlayground();
            Frames(3);
            var stage = pg.Stage!;
            var camera = stage.StageUnityCamera!;
            Assert.IsFalse(stage.TemplateFraming.Found, "缺省预设不是模板");
            var defaultZoom = camera.CurrentZoom;
            var defaultSmoothing = stage.FollowSmoothingSeconds;
            var registry = pg.Session!.Context!.World.Registry;
            double Zoom(string style) => registry.Get("camera_profile", "camera_profile.tpl_" + style)!.GetNumber("zoom_default");

            pg.SetPreset("feel.preset.tpl_heavy");
            Frames(3);
            var heavy = camera.CurrentZoom;
            Assert.IsTrue(stage.TemplateFraming.Found);
            Assert.AreEqual("camera_profile.tpl_heavy", stage.TemplateFraming.ProfileId);
            Assert.AreEqual(defaultZoom * Zoom("heavy") / CameraFraming.ReferenceZoom, heavy, 1e-4, "缩放 = 缺省取景 × 模板 zoom_default / 参照缩放");

            pg.SetPreset("feel.preset.tpl_agile");
            Frames(3);
            var agile = camera.CurrentZoom;
            Assert.AreEqual(heavy * Zoom("agile") / Zoom("heavy"), agile, 1e-4, "模板之间的相对取景保留");
            Assert.AreNotEqual(heavy, agile);
            var followLerp = registry.Get("camera_profile", "camera_profile.tpl_agile")!.GetNumber("follow_lerp");
            Assert.AreEqual(-(1.0 / 60.0) / Math.Log(1.0 - followLerp), stage.FollowSmoothingSeconds, 1e-9, "跟随平滑由 follow_lerp 折成时间常数");
            Debug.Log("[LabPlaygroundPanelsTests] camera: default zoom=" + defaultZoom + " heavy=" + heavy + " agile=" + agile);

            pg.SetPreset(pg.Session.Context!.Cell.DefaultPreset);
            Frames(3);
            Assert.IsFalse(stage.TemplateFraming.Found);
            Assert.AreEqual(defaultZoom, camera.CurrentZoom, 1e-6, "非模板预设恢复舞台缺省取景");
            Assert.AreEqual(defaultSmoothing, stage.FollowSmoothingSeconds, 1e-9);
        }

        // ───────── 本地产物 ─────────

        [Test]
        public void SaveAsPreset_WritesALocalRoot_ThatTheNextSessionListsAsAPreset()
        {
            var pg = NewPlayground();
            pg.Tuning!.SetValue(FeelFieldNames.AttackerHitstopMs, FeelValue.Of(Resolved(FeelFieldNames.AttackerHitstopMs) + 5.0));
            var path = pg.SaveTuningAsPreset("panel_smoke");
            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(path.StartsWith(_saveDir, StringComparison.Ordinal), "写在本地输出目录，不碰仓库");
            Dispose();

            var next = NewPlayground();
            Assert.IsTrue(next.Model.Presets.Any(p => p.Id == "feel.preset.panel_smoke"), "下次开局预设列表里能选到");
        }

        [Test]
        public void WriteBack_ProducesALocalDiff_AndNeverEditsRepositoryData()
        {
            var pg = NewPlayground();
            var tuning = pg.Tuning!;
            var value = Resolved(FeelFieldNames.AttackerHitstopMs) + 5.0;
            tuning.SetValue(FeelFieldNames.AttackerHitstopMs, FeelValue.Of(value));
            var plan = tuning.BuildWriteBack();
            Assert.GreaterOrEqual(plan.Edits.Count + plan.Manual.Count, 1);
            var before = plan.Files.ToDictionary(f => f, f => File.ReadAllText(Path.Combine(pg.RepoRoot, f.Replace('/', Path.DirectorySeparatorChar))));
            var diff = pg.WriteBackTuning();
            Assert.IsTrue(File.Exists(diff));
            Assert.IsTrue(diff.StartsWith(_saveDir, StringComparison.Ordinal));
            foreach (var file in before)
            {
                Assert.AreEqual(file.Value, File.ReadAllText(Path.Combine(pg.RepoRoot, file.Key.Replace('/', Path.DirectorySeparatorChar))), "仓库里的数据文件不被改动：" + file.Key);
            }
        }

        // ───────── 评分 ─────────

        [Test]
        public void Rating_SavesLocally_AndExportsAValidationRecord()
        {
            var pg = NewPlayground();
            Frames(5);
            foreach (var dimension in RatingDimensions.All)
            {
                pg.Rating.Rate(dimension, 4);
            }

            pg.Rating.ToggleTag("慢");
            var saved = pg.SubmitRating();
            Assert.IsTrue(File.Exists(saved));
            Assert.IsTrue(saved.StartsWith(_saveDir, StringComparison.Ordinal));
            pg.RatingRowId = "feel.validation.panel_smoke";
            var exported = pg.ExportRatingValidation();
            Assert.IsTrue(File.Exists(exported));
            StringAssert.Contains("feel.validation.panel_smoke", File.ReadAllText(exported));
        }

        // ───────── 可选：把几页画出来存成截图（GF_LAB_SCREENSHOT_DIR 指定目录才跑；手工看界面用，门禁不设）─────────

        [UnityTest]
        public IEnumerator Screenshots_EachTab_WhenRequested()
        {
            var dir = Environment.GetEnvironmentVariable("GF_LAB_SCREENSHOT_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                Assert.Pass("未设置 GF_LAB_SCREENSHOT_DIR，跳过截图。");
            }

            Directory.CreateDirectory(dir!);
            var pg = NewPlayground();
            pg.ManualDrive = false;
            pg.SpawnDummy("stake");
            yield return null;
            foreach (var tab in new[] { LabTab.Scene, LabTab.Tuning, LabTab.Timeline, LabTab.Trajectory, LabTab.Rating })
            {
                pg.SetTab(tab);
                for (var i = 0; i < 20; i++)
                {
                    yield return null;
                }

                if (tab == LabTab.Scene)
                {
                    Tap(AttackKey);
                }

                if (!Application.isBatchMode)
                {
                    yield return new WaitForEndOfFrame();
                }

                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "panel_" + tab + ".png"));
                for (var i = 0; i < 5; i++)
                {
                    yield return null;
                }
            }
        }
    }
}
