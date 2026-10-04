#nullable enable
// LabPlaygroundTests：人手试玩宿主的 PlayMode 冒烟（ADR-0141，手感设计 06 第 4 节）。
// 做法：载入试玩场景（或用代码建同样的物体），把真实输入换成假输入（按键名就是数据 found.input_action 里声明的键），
// 手动驱动控制器（ManualDrive：每个控制器帧 = 一次 Tick），再从数据断言——期望值由规则/另一份预设行算出，不写裸数。
// 这些用例不画任何界面：断言读的是内核记录与面板视图模型。
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Adapter.Unity.LabHost;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Lab;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace Adapter.Unity.Tests.LabHost
{
    [Category("module:lab")]
    public sealed class LabPlaygroundTests
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
            _saveDir = Path.Combine(Path.GetTempPath(), "lab_playground_test_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (_pg != null)
            {
                _pg.End();
            }

            if (_go != null)
            {
                UnityEngine.Object.DestroyImmediate(_go);
            }

            _go = null;
            _pg = null;
            if (Directory.Exists(_saveDir))
            {
                Directory.Delete(_saveDir, true);
            }
        }

        private LabPlayground NewPlayground(string cell = "2d_action")
        {
            _go = new GameObject("LabPlaygroundTest");
            var pg = _go.AddComponent<LabPlayground>();
            pg.Configure(cell);
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

        private Vec2 PlayerPosition()
        {
            var ticks = _pg!.Session!.Recording.Ticks;
            return ticks.Count > 0 ? ticks[ticks.Count - 1].Position : Vec2.Zero;
        }

        private System.Collections.Generic.List<FeelEventRecord> PlayerHits() =>
            _pg!.Session!.Recording.Feel!.Events
                .Where(e => e.Kind == "hit_confirmed" && e.Actor == "player")
                .ToList();

        private double ResolvedPlayer(string field)
        {
            var ctx = _pg!.Session!.Context!;
            var feel = ctx.World.Gameplay.Feel!.Feel;
            return feel.Resolver.Resolve(ctx.PlayerId).GetAbsolute(field).AsNumber();
        }

        /// <summary>期望的攻击方顿帧 tick：取 <paramref name="preset"/> 行里写的时长与上限（规则同命中手感裁决：min(ticks(时长), ticks(上限))，非击杀）。</summary>
        private int ExpectedAttackerTicks(string preset)
        {
            var ctx = _pg!.Session!.Context!;
            var feel = ctx.World.Gameplay.Feel!.Feel;
            var row = feel.Profiles.GetPreset(preset)!;
            double Value(string field) => row.Values.First(w => w.Field == field).Value.AsNumber();
            var step = _pg.Session.StepSeconds;
            return Math.Min(
                FeelCalibration.MillisecondsToTicks(Value(FeelFieldNames.AttackerHitstopMs), step),
                FeelCalibration.MillisecondsToTicks(Value(FeelFieldNames.AttackerHitstopCapMs), step));
        }

        // ───────── 移动 ─────────

        [Test]
        public void Move_HeldKey_MovesThePlayerAlongTheStickDirection_AndTheModelTicksAdvance()
        {
            var pg = NewPlayground();
            Frames(2);
            var start = PlayerPosition();
            var tick0 = pg.Model.Tick;
            _input!.Press("d");
            Frames(30);
            _input.Release("d");
            Frames(2);
            var end = PlayerPosition();
            Debug.Log("[LabPlaygroundTests] move: dx=" + (end.X - start.X).ToString("0.###") + " dy=" + (end.Y - start.Y).ToString("0.###") + " ticks=" + (pg.Model.Tick - tick0));
            Assert.Greater(end.X - start.X, 0.5, "按住右移 30 帧应明显向 +X 移动");
            Assert.Less(Math.Abs(end.Y - start.Y), 1e-6, "右移不改变 Y");
            Assert.LessOrEqual(Math.Abs(34 - (pg.Model.Tick - tick0)), 2, "60Hz 固定步、60fps 帧：34 个控制器帧推进约 34 个 tick（浮点累加器取整，至多差 2；实测 " + (pg.Model.Tick - tick0) + "）");
            // 记录里这次移动是一对轴事件（按下、松开），不是别的东西。
            var axes = pg.Session!.Script.Events.Where(e => e.Kind == ScriptEventKind.Axis).ToList();
            Assert.AreEqual(2, axes.Count);
            Assert.Greater(axes[0].Value.X, 0.5);
            Assert.AreEqual(0.0, axes[1].Value.X, 1e-9);
        }

        // ───────── 攻击靶子：命中、顿帧时长取自当前预设 ─────────

        [Test]
        public void Attack_OnStake_LandsAHit_WithHitStopTicksFromTheActivePreset()
        {
            var pg = NewPlayground();
            pg.SpawnDummy("stake");
            Frames(4);
            Assert.AreEqual(1, pg.Model.DummyCount);
            Tap(AttackKey);
            Frames(40);

            var hits = PlayerHits();
            Assert.GreaterOrEqual(hits.Count, 1, "站在木桩前按攻击键应命中");
            var hit = hits[0];
            Assert.Greater(hit.D, 0.0, "命中应扣血（伤害量由数据算出，这里只断言有变化）");
            var expected = ExpectedAttackerTicks(pg.Model.Preset);
            Debug.Log("[LabPlaygroundTests] attack: preset=" + pg.Model.Preset + " damage=" + hit.D.ToString("0.##") + " attackerHitStopTicks=" + hit.B + " expected=" + expected);
            Assert.Greater(expected, 0, "测试前提：激活预设给了正的顿帧时长");
            Assert.AreEqual(expected, hit.B, "攻击方顿帧 tick = 激活预设行里的时长按规则折算");
            var started = pg.Session!.Recording.Feel!.Events.Where(e => e.Kind == "hitstop_started").ToList();
            Assert.IsNotEmpty(started, "顿帧真的发出了（hitstop_started）");
            Assert.IsTrue(started.Any(e => e.A == expected && e.Detail.Split('+').Contains("player")), "玩家（攻击方）的顿帧时长 = 预设折算值");
            Assert.IsTrue(pg.Model.InputToAcceptTicks.HasData, "指标：输入→动作接受已有样本");
            Assert.IsTrue(pg.Model.HitToFeedbackTicks.HasData, "指标：命中确认→首个反馈已有样本");
        }

        // ───────── A/B 切换：下一个动作按另一预设的顿帧 ─────────

        [Test]
        public void AbSwitch_NextAttackUsesTheOtherPresetsHitStop_AndTheSwitchIsRecordedAtItsTick()
        {
            var pg = NewPlayground();
            var a = pg.Model.PresetA;
            var b = pg.Model.PresetB;
            Assert.IsNotEmpty(b, "数据里至少有两个预设可供 A/B");
            Assert.AreNotEqual(a, b);
            pg.SpawnDummy("stake");
            Frames(4);
            Tap(AttackKey);
            Frames(40);
            var firstHits = PlayerHits();
            Assert.GreaterOrEqual(firstHits.Count, 1);
            var ticksA = firstHits[0].B;
            Assert.AreEqual(ExpectedAttackerTicks(a), ticksA, "A 槽：按 A 预设行折算");

            var tickBeforeSwitch = pg.Session!.Tick;
            pg.SwitchAb();
            Assert.AreEqual('B', pg.Model.ActiveSlot);
            Assert.AreEqual(b, pg.Model.Preset);
            Frames(100); // 等连击窗口过去，下一次攻击从第一段开始
            Tap(AttackKey);
            Frames(40);
            var hits = PlayerHits();
            Assert.Greater(hits.Count, firstHits.Count, "切到 B 之后再次命中");
            var ticksB = hits[hits.Count - 1].B;
            Assert.AreEqual(ExpectedAttackerTicks(b), ticksB, "B 槽：按另一预设行折算");
            Debug.Log("[LabPlaygroundTests] ab: A=" + a + " ticks=" + ticksA + " B=" + b + " ticks=" + ticksB + " expectedB=" + ExpectedAttackerTicks(b));
            Assert.AreNotEqual(ticksA, ticksB, "两个预设的顿帧不同，切换必须改变时长");

            // 切换是一条脚本事件，盖的是它生效的 tick。
            var switchEvent = pg.Session.Script.Events.Last(e => e.Kind == ScriptEventKind.Preset);
            Assert.AreEqual(b, switchEvent.Action);
            Assert.GreaterOrEqual(switchEvent.Tick, tickBeforeSwitch);
            Assert.LessOrEqual(switchEvent.Tick, tickBeforeSwitch + 1);
        }

        // ───────── 默认手感模板（ADR-0142）进试玩：预设列表带模板，切模板顿帧随模板数据变 ─────────

        private int FirstHitAttackerTicksAfterSwitchingTo(string preset)
        {
            var pg = NewPlayground();
            pg.SetPreset(preset);
            pg.SpawnDummy("stake");
            Frames(4);
            Tap(AttackKey);
            Frames(60);
            var hits = PlayerHits();
            Assert.GreaterOrEqual(hits.Count, 1, preset);
            var ticks = hits[0].B;
            Assert.AreEqual(ExpectedAttackerTicks(preset), ticks, preset + "：顿帧取自模板数据行");
            return ticks;
        }

        [Test]
        public void TemplatePresets_AreListed_DefaultPresetUnchanged_AndHeavyVsAgileHitStopDiffer()
        {
            var pg = NewPlayground();
            var ids = pg.Model.Presets.Select(p => p.Id).ToList();
            foreach (var style in new[] { "classic", "agile", "heavy", "horde", "precise" })
            {
                Assert.Contains("feel.preset.tpl_" + style, ids);
            }

            Assert.AreEqual(pg.Session!.Context!.Cell.DefaultPreset, pg.Model.PresetA, "缺省预设不因加入模板而改变");
            pg.End();
            UnityEngine.Object.DestroyImmediate(_go);
            _go = null;
            _pg = null;

            var heavy = FirstHitAttackerTicksAfterSwitchingTo("feel.preset.tpl_heavy");
            _pg!.End();
            UnityEngine.Object.DestroyImmediate(_go);
            _go = null;
            _pg = null;
            var agile = FirstHitAttackerTicksAfterSwitchingTo("feel.preset.tpl_agile");
            Debug.Log("[LabPlaygroundTests] templates: heavy=" + heavy + " agile=" + agile);
            Assert.AreNotEqual(heavy, agile, "重型与敏捷模板的攻击方顿帧不同");
        }

        // ───────── 顿帧开关 = 录进脚本的覆盖 ─────────

        [Test]
        public void HitStopOff_IsARecordedOverride_AndTheNextHitHasZeroHitStop()
        {
            var pg = NewPlayground();
            pg.SpawnDummy("stake");
            Frames(4);
            pg.SetHitStop(false);
            Tap(AttackKey);
            Frames(40);
            var hits = PlayerHits();
            Assert.GreaterOrEqual(hits.Count, 1);
            Assert.AreEqual(0, hits[0].B, "关顿帧 = 覆盖攻击方顿帧时长为 0（逻辑，已录入脚本）");
            Assert.IsTrue(pg.Session!.Script.Events.Any(e => e.Kind == ScriptEventKind.Override && e.Action == FeelFieldNames.AttackerHitstopMs));
        }

        // ───────── 呈现开关：只拦呈现，不改逻辑 ─────────

        [Test]
        public void EffectToggle_SuppressesPresentationOnly_LogicFingerprintUnchanged()
        {
            string Run(bool shakeOn)
            {
                var pg = NewPlayground();
                if (!shakeOn)
                {
                    pg.ToggleEffect(LabEffectFilter.CameraImpulse);
                    pg.ToggleEffect(LabEffectFilter.Sfx);
                }

                pg.SpawnDummy("stake");
                Frames(4);
                Tap(AttackKey);
                Frames(40);
                var submitted = pg.Effects!.SubmittedCount(LabEffectFilter.CameraImpulse);
                var suppressed = pg.Effects.SuppressedCount(LabEffectFilter.CameraImpulse);
                if (shakeOn)
                {
                    Assert.AreEqual(0, suppressed);
                }
                else
                {
                    Assert.AreEqual(submitted, suppressed, "关掉的通道：流水线发出的每一次都被拦下并计数");
                }

                Assert.Greater(submitted, 0, "流水线确实发了镜头冲击");
                var recording = pg.Session!.Recording;
                var stepsRun = pg.Session.Tick;
                var logicHits = recording.Feel!.Events.Count(e => e.Kind == "hit_confirmed");
                pg.End();
                UnityEngine.Object.DestroyImmediate(_go);
                _go = null;
                _pg = null;
                return stepsRun + ":" + logicHits;
            }

            var on = Run(true);
            var off = Run(false);
            Assert.AreEqual(on, off, "呈现开关不改变逻辑（同样的步数、同样的命中数）");
        }

        // ───────── 录制 → 无头重放，逻辑指纹逐字节一致 ─────────

        [Test]
        public void RecordedSession_FromDisk_ReplaysHeadless_WithByteIdenticalLogicFingerprint()
        {
            var pg = NewPlayground();
            pg.SpawnDummy("stake");
            pg.SpawnDummy("mob");
            Frames(4);
            _input!.Press("d");
            Frames(12);
            _input.Release("d");
            Tap(AttackKey);
            Frames(20);
            Tap(AttackKey);
            Frames(30);
            pg.SwitchAb();
            Frames(60);
            Tap(AttackKey);
            Frames(30);
            pg.SetWeapon("feel.weapon.greatsword");
            pg.SetArchetype("feel.archetype.heavy");
            pg.SetTimeScale(0.5); // 呈现标记：逻辑不读
            pg.SetHitStop(false);
            Tap(AttackKey);
            Frames(40);
            pg.SetHitStop(true);
            pg.ClearDummies();
            pg.SpawnDummy("stake");
            Frames(10);
            Tap(AttackKey);
            Frames(30);
            pg.ToggleEffect(LabEffectFilter.Flash); // 呈现标记
            Frames(5);

            var path = pg.End();
            Assert.IsNotEmpty(path, "End 应把整局存成本地脚本");
            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(path.StartsWith(_saveDir, StringComparison.Ordinal), "保存目录可配");

            // 调试用：设了环境变量 GF_LAB_KEEP_SCRIPT 就把录下的脚本另存一份（用来手动试无头命令行重放，不影响断言）。
            var keep = Environment.GetEnvironmentVariable("GF_LAB_KEEP_SCRIPT");
            if (!string.IsNullOrEmpty(keep))
            {
                File.Copy(path, keep, true);
            }

            // 活会话的逻辑指纹（引擎宿主、带舞台）。
            var live = pg.FinalRecording!;
            var script = pg.Session!.Script;
            var liveLogic = pg.Host!.Runner.FingerprintOf(script, "2d_action", live)
                .Project(pg.Host.HeadlessRunner.Registry, MetricClass.Logic);

            // 从磁盘读回来的脚本，在无头宿主上逐 tick 重放。
            var fromDisk = InputScript.Parse(File.ReadAllText(path));
            var replayLogic = pg.Host.RunHeadlessLogic(fromDisk, "2d_action");
            Debug.Log("[LabPlaygroundTests] replay: ticks=" + live.Ticks.Count + " events=" + script.Events.Count + " logicLen=" + liveLogic.Length + " identical=" + (liveLogic == replayLogic));
            Assert.AreEqual(liveLogic, replayLogic, "试玩会话录下的脚本，无头重放的逻辑组指纹必须逐字节一致");

            // 不是空转：这一局真的命中了靶子，并含新增事件种类。
            Assert.IsTrue(live.Events.Any(e => e.Kind == "damage" && e.Source == "player"));
            foreach (var kind in new[] { ScriptEventKind.Spawn, ScriptEventKind.ClearDummies, ScriptEventKind.Preset, ScriptEventKind.Loadout, ScriptEventKind.Override, ScriptEventKind.ClearOverrides, ScriptEventKind.Marker })
            {
                Assert.IsTrue(fromDisk.Events.Any(e => e.Kind == kind), "脚本里应有事件种类 " + kind);
            }

            // 按预设分段的指纹也能算出来（06 第 3.4 节）：A、B 各一段。
            var segments = LabLive.PresetSegments(script, pg.Model.PresetA);
            Assert.GreaterOrEqual(segments.Count, 2);
            _pg = null;
            UnityEngine.Object.DestroyImmediate(_go);
            _go = null;
        }

        // ───────── 场景：载入试玩场景，注入移动 + 攻击 ─────────

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator Scene_LoadsAndPlays_MoveAndAttackFromData([ValueSource(nameof(Cells))] string cell)
        {
            var path = "Assets/Framework/Scenes/LabPlayground_" + cell + ".unity";
            var op = EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            yield return op;
            var pg = UnityEngine.Object.FindFirstObjectByType<LabPlayground>();
            Assert.IsNotNull(pg, "场景里应有试玩控制器");
            pg!.ManualDrive = true;
            pg.SaveDirectory = _saveDir;
            var guard = 0;
            while (!pg.IsBegun && ++guard < 600)
            {
                yield return null;
            }

            Assert.IsTrue(pg.IsBegun, pg.Model.Status);
            Assert.AreEqual(cell, pg.Model.Cell);
            _pg = pg;
            _input = new Adapters.Stub.StubInput();
            pg.UseInput(_input, _ => false);
            pg.SaveDirectory = _saveDir;
            Assert.IsNotNull(pg.Stage!.StageCamera, "舞台在场景里建出了渲染相机");
            Assert.IsTrue(pg.Stage.StageCamera!.enabled, "试玩模式的舞台相机是开着的（人要看画面）");

            pg.SpawnDummy("stake");
            Frames(4);
            Tap(AttackKey);
            Frames(40);
            Assert.GreaterOrEqual(PlayerHits().Count, 1, "攻击键命中靶子");
            var start = PlayerPosition();
            _input.Press("d");
            Frames(20);
            _input.Release("d");
            Frames(2);
            var end = PlayerPosition();
            var moved = Math.Sqrt((end.X - start.X) * (end.X - start.X) + (end.Y - start.Y) * (end.Y - start.Y));
            Assert.Greater(moved, 0.3, "移动输入让玩家位移（距离取自内核记录）");
            _pg = pg;
        }

        public static readonly string[] Cells = { "2d_action", "2_5d_action", "3d_action" };
#endif
    }
}
