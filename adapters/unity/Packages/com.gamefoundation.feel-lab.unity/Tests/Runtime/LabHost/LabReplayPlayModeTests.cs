#nullable enable
// LabReplayPlayModeTests：脚本回放与慢放（M5-S7，ADR-0151，手感设计 06 第 3.5 节）的 PlayMode 证明。
// 核心断言：慢放（0.25 倍）只缩放表现时钟，逻辑逐位不变——回放结束后的逻辑组指纹必须逐字节等于无头宿主跑同一脚本同一格子的结果，
// 也等于 1 倍回放的结果；表现侧的差别（驱动帧数约四倍、首次可见响应不晚于 1 倍）由数据对照，不写裸数。
using Adapter.Unity;
using System;
using System.IO;
using System.Linq;
using FeelLab.Unity;
using Lab;
using NUnit.Framework;
using UnityEngine;

namespace FeelLab.Unity.Tests
{
    [Category("module:lab")]
    public sealed class LabReplayPlayModeTests
    {
        private const double Frame = 1.0 / 60.0;
        private const string ScriptId = "feel_x_latency";
        private const string Cell = "2d_action";
        private GameObject? _go;
        private LabPlayground? _pg;
        private string _saveDir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "lab_replay_test_" + Guid.NewGuid().ToString("N"));
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

        private LabPlayground NewReplay(double timeScale, out int frames)
        {
            var script = LabHostTestSupport.Script(ScriptId);
            _go = new GameObject("LabReplayTest");
            var pg = _go.AddComponent<LabPlayground>();
            pg.Configure(Cell);
            pg.ManualDrive = true;
            pg.SaveDirectory = _saveDir;
            Assert.IsTrue(pg.BeginReplay(script, timeScale), pg.Model.Status);
            _pg = pg;
            Assert.IsTrue(pg.IsReplay);
            frames = 0;
            var limit = (int)(script.Meta.DurationTicks / timeScale) + 600;
            while (!pg.ReplayDone && frames < limit)
            {
                pg.Tick(Frame);
                pg.FinishFrame();
                frames++;
            }

            Assert.IsTrue(pg.ReplayDone, "回放应在预期帧数内推进到脚本时长（实际推进 " + frames + " 帧，tick " + pg.Model.Tick + "）");
            return pg;
        }

        [Test]
        public void Replay_QuarterSpeed_LogicFingerprintEqualsHeadlessAndRealtimeReplay()
        {
            var script = LabHostTestSupport.Script(ScriptId);
            var slow = NewReplay(0.25, out var slowFrames);
            var slowLogic = slow.ReplayLogicProjection();
            Assert.IsNotEmpty(slowLogic);
            Assert.AreEqual(script.Meta.DurationTicks, slow.Session!.Tick, "慢放也推进到脚本时长，不多不少");
            Assert.AreEqual(slow.Host!.RunHeadlessLogic(script, Cell), slowLogic, "0.25 倍回放的逻辑组指纹与无头宿主逐字节一致");

            slow.End();
            UnityEngine.Object.DestroyImmediate(_go);
            _go = null;
            _pg = null;

            var normal = NewReplay(1.0, out var normalFrames);
            Assert.AreEqual(slowLogic, normal.ReplayLogicProjection(), "1 倍与 0.25 倍回放的逻辑组指纹逐字节一致");
            Assert.GreaterOrEqual(slowFrames, normalFrames * 3, "0.25 倍回放的表现帧数约为 1 倍的四倍（每帧推进的模拟时间只有四分之一；实测 " + slowFrames + " 对 " + normalFrames + "）");
        }

        [Test]
        public void Replay_PauseStepAndTimeScale_WriteNoEvents_AndNothingIsSaved()
        {
            var script = LabHostTestSupport.Script(ScriptId);
            _go = new GameObject("LabReplayTest");
            var pg = _go.AddComponent<LabPlayground>();
            pg.Configure(Cell);
            pg.ManualDrive = true;
            pg.SaveDirectory = _saveDir;
            Assert.IsTrue(pg.BeginReplay(script, 1.0), pg.Model.Status);
            _pg = pg;
            for (var i = 0; i < 20; i++)
            {
                pg.Tick(Frame);
                pg.FinishFrame();
            }

            var tick = pg.Session!.Tick;
            pg.TogglePause();
            for (var i = 0; i < 10; i++)
            {
                pg.Tick(Frame);
            }

            Assert.AreEqual(tick, pg.Session.Tick, "暂停时回放不推进");
            pg.StepTick();
            Assert.GreaterOrEqual(pg.Session.Tick, tick + 1, "暂停时单步推进一个固定步（浮点累加器取整，至多多一步）");
            Assert.LessOrEqual(pg.Session.Tick, tick + 2);
            pg.SetTimeScale(0.25);
            Assert.AreEqual(0.25, pg.Model.TimeScale, 0.0);
            pg.TogglePause();
            while (!pg.ReplayDone)
            {
                pg.Tick(Frame);
                pg.FinishFrame();
            }

            Assert.AreEqual(script.Events.Count, pg.Session.Script.Events.Count, "回放不往脚本里写任何标记事件");
            Assert.AreEqual(script.ToJson(), pg.Session.Script.ToJson());
            Assert.AreEqual(pg.Host!.RunHeadlessLogic(script, Cell), pg.ReplayLogicProjection(), "暂停、单步、变速都不改变逻辑");
            Assert.AreEqual(string.Empty, pg.End(), "回放结束不保存录制");
            Assert.IsFalse(Directory.Exists(_saveDir) && Directory.GetFiles(_saveDir, "*", SearchOption.AllDirectories).Length > 0, "回放模式不落盘");
        }

        [Test]
        public void EngineHost_TimeScaleVariant_KeepsLogic_AndOnlyRefinesPresentation()
        {
            var host = LabHostTestSupport.Host;
            var script = LabHostTestSupport.Script(ScriptId);
            var normal = host.Run(script, Cell);
            var slow = host.Run(script, Cell, null, null, new LabRunVariant { TimeScale = 0.25 });
            Assert.AreEqual(normal.LogicProjection, slow.LogicProjection, "0.25 倍的逻辑组指纹与 1 倍逐字节一致");
            Assert.AreEqual(host.RunHeadlessLogic(script, Cell), slow.LogicProjection, "与无头宿主逻辑一致");

            var normalFrames = Num(normal, "frames_driven");
            var slowFrames = Num(slow, "frames_driven");
            Assert.GreaterOrEqual(slowFrames, normalFrames * 3.5, "0.25 倍时驱动的帧数约为四倍（实测 " + slowFrames + " 对 " + normalFrames + "）");

            // 首次可见响应：表现帧更细，可见时刻只会更早或相同（同一逻辑响应 tick，被更细的帧量化）。
            var normalMax = Num(normal, "input_visible_ms_max");
            var slowMax = Num(slow, "input_visible_ms_max");
            Assert.GreaterOrEqual(Num(normal, "input_visible_count"), 1.0, "脚本里有攻击按下，引擎侧度量到输入");
            Assert.AreEqual(Num(normal, "input_visible_count"), Num(slow, "input_visible_count"), "同一脚本的按下次数不随慢放变化");
            Assert.LessOrEqual(slowMax, normalMax + 1e-6, "慢放时首次可见响应不晚于 1 倍（实测 " + slowMax + " 对 " + normalMax + "）");
        }

        private static double Num(EngineLabRun run, string metric) =>
            ((Core.Foundation.Common.Json.JsonNumber)((Core.Foundation.Common.Json.JsonObject)run.Fingerprint.Groups["engine"])[metric]).Value;
    }
}
