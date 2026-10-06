#nullable enable
// ExtensionPointsPlayModeTests：手感实验室两个公开扩展点（EngineStageExtension、LabPlaygroundExtension，ADR-0160）的运行时验收。
// 判断记录（为什么要有这组用例）：演示场景迁往样板仓库后，样板只能建立在这两个扩展点上；扩展点必须被框架自己的用例钉住——
// 调用次序、传给扩展的上下文、扩展替换缺省行为（外形登记、场景地面、广告牌投影）与会话结束时的释放，
// 否则样板是框架公开接口的唯一证据，而它在另一个仓库里。做法同 LabPlaygroundTests：假输入、手动驱动、从记录与扩展自己的计数断言，
// 期望值由规则或会话数据得出，不写裸数。
using Adapter.Unity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using FeelLab.Unity;
using NUnit.Framework;
using Presentation.Common;
using UnityEngine;

namespace FeelLab.Unity.Tests
{
    [Category("module:lab")]
    public sealed class ExtensionPointsPlayModeTests
    {
        private const double Frame = 1.0 / 60.0;
        private const string AttackKey = "j";

        private sealed class FakeProjection : IStageProjection
        {
            public bool Upright { get; set; } = true;

            public Vector3 Up { get; } = Vector3.up;

            public int FacingCalls { get; private set; }

            public bool? FineDepthSortWritten { get; private set; }

            public Quaternion Facing(float screenDegrees)
            {
                FacingCalls++;
                return Quaternion.identity;
            }

            public bool FineDepthSort
            {
                set => FineDepthSortWritten = value;
            }
        }

        private sealed class RecordingStageExtension : EngineStageExtension
        {
            public bool BuildsScene = true;
            public FakeProjection? ProjectionToUse;
            public string Prefix = "lab_";
            public int RegistryCalls;
            public string RegistryForm = string.Empty;
            public int BuildSceneCalls;
            public StageSceneContext? Scene;
            public bool GroundExistedDuringBuildScene;
            public readonly List<(Id Entity, bool IsPlayer)> Bound = new List<(Id, bool)>();
            public readonly List<int> LogicTicks = new List<int>();
            public int Swings;
            public int Steps;
            public double StepSeconds;
            public int Billboards;
            public int FacingCalls;
            public int Disposed;

            public override IDisplayInfoRegistry? CreateDisplayRegistry(IDisplayInfoRegistry core, string form)
            {
                RegistryCalls++;
                RegistryForm = form;
                return null;
            }

            public override string WeaponStylePrefix => Prefix;

            public override bool BuildScene(StageSceneContext scene)
            {
                BuildSceneCalls++;
                Scene = scene;
                GroundExistedDuringBuildScene = GameObject.Find("LabGround") != null;
                Projection = ProjectionToUse;
                return BuildsScene;
            }

            public override void OnDisplayBound(Id entityId, Id displayId, bool isPlayer) => Bound.Add((entityId, isPlayer));

            public override void OnLogicEvent(IEvent evt, int tick) => LogicTicks.Add(tick);

            public override void OnSwing(Id entity) => Swings++;

            public override void OnStep(double dt)
            {
                Steps++;
                StepSeconds += dt;
            }

            public override void OnBillboardsApplied() => Billboards++;

            public override Direction FacingFor(Id entity, Vec2 position, Direction logicalFacing)
            {
                FacingCalls++;
                return logicalFacing;
            }

            public override void Dispose() => Disposed++;
        }

        private sealed class RecordingPlaygroundExtension : LabPlaygroundExtension
        {
            public RecordingStageExtension? Stage;
            public bool PanelVisible = true;
            public string? Hint;
            public IReadOnlyList<string> Roots = Array.Empty<string>();
            public readonly List<string> RootRequests = new List<string>();
            public readonly List<string> StageRequests = new List<string>();
            public EngineLabStage? StartedWith;
            public int Started;
            public int Ended;

            public override IReadOnlyList<string> ExtraDataRoots(string cell)
            {
                RootRequests.Add(cell);
                return Roots;
            }

            public override EngineStageExtension? CreateStageExtension(string cell)
            {
                StageRequests.Add(cell);
                return Stage;
            }

            public override bool PanelInitiallyVisible => PanelVisible;

            public override string? PanelHint(string cell) => Hint;

            public override void OnSessionStarted(LabPlayground playground, EngineLabStage stage)
            {
                Started++;
                StartedWith = stage;
            }

            public override void OnSessionEnded() => Ended++;
        }

        private GameObject? _go;
        private LabPlayground? _pg;
        private Adapters.Stub.StubInput? _input;
        private string _saveDir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "lab_ext_test_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            _pg?.End();
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

        private LabPlayground NewPlayground(string cell, LabPlaygroundExtension? extension)
        {
            _go = new GameObject("ExtensionPointsTest");
            var pg = _go.AddComponent<LabPlayground>();
            pg.Configure(cell);
            pg.Extension = extension;
            pg.ManualDrive = true;
            pg.FlashIntensitySource = () => 1.0;
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

        [Test]
        public void StageExtension_ReceivesEveryHook_WithTheDocumentedContext_AndIsDisposedWithTheSession()
        {
            var stageExt = new RecordingStageExtension();
            var playgroundExt = new RecordingPlaygroundExtension { Stage = stageExt };
            var pg = NewPlayground("2d_action", playgroundExt);
            var ctx = pg.Session!.Context!;
            Frames(2);
            pg.SpawnDummy("stake");
            Frames(30);
            Tap(AttackKey, 3);
            Frames(60);

            Assert.AreEqual(1, stageExt.RegistryCalls, "外形登记在建视图工厂前只问一次");
            Assert.AreEqual(ctx.Cell.Form, stageExt.RegistryForm, "传给扩展的外形是格子的外形");
            Assert.AreEqual(1, stageExt.BuildSceneCalls, "试玩模式建场景只调一次");
            var scene = stageExt.Scene!;
            Assert.AreEqual(pg.Stage!.StageCamera, scene.Camera, "场景上下文里的相机就是舞台相机");
            Assert.AreEqual(ctx.PlayerId, scene.Host.PlayerId, "场景上下文带内核宿主上下文");
            Assert.IsNotNull(scene.Loader, "场景上下文带共用的资源加载器");
            Assert.AreEqual(scene.IsolationLayer, scene.Root.gameObject.layer, "场景根在舞台的隔离层");
            Assert.AreEqual(string.Equals(ctx.Cell.Form, "model", StringComparison.Ordinal), scene.IsModelForm, "模型型格子标志按格子外形给出");
            Assert.IsFalse(scene.OrbitFloorSize.HasValue, "没有装环绕镜头时不给地面尺寸");
            Assert.IsFalse(scene.OrbitEnabled);
            Assert.IsFalse(stageExt.GroundExistedDuringBuildScene, "扩展建场景时舞台还没有建缺省地面");
            Assert.IsNull(GameObject.Find("LabGround"), "扩展自己建了场景（返回 true）：舞台不再建缺省地面网格");

            Assert.AreEqual(1, stageExt.Bound.Count(b => b.IsPlayer), "玩家单位恰有一次带玩家标志的视图绑定");
            Assert.AreEqual(ctx.PlayerId, stageExt.Bound.Single(b => b.IsPlayer).Entity, "带玩家标志的就是玩家 id");
            Assert.GreaterOrEqual(stageExt.Bound.Count(b => !b.IsPlayer), 1, "靶子的视图绑定不带玩家标志");

            Assert.Greater(stageExt.LogicTicks.Count, 0, "每个固定步末尾新产生的逻辑事件都交给扩展");
            for (var i = 1; i < stageExt.LogicTicks.Count; i++)
            {
                Assert.GreaterOrEqual(stageExt.LogicTicks[i], stageExt.LogicTicks[i - 1], "逻辑事件按 tick 非递减交付");
            }

            Assert.Greater(stageExt.Steps, 0, "每个渲染帧都调 OnStep");
            Assert.Greater(stageExt.StepSeconds, 0.0, "OnStep 带推进的模拟秒数");
            Assert.Greater(stageExt.FacingCalls, 0, "视图同步位姿时经扩展换算朝向");
            Assert.GreaterOrEqual(stageExt.Swings, 1, "出手动画走到命中帧时通知扩展");
            Assert.AreEqual(0, pg.Stage.Record.Errors.Count, string.Join(" | ", pg.Stage.Record.Errors));
            Assert.AreSame(stageExt, pg.Stage.Extension, "舞台暴露它的扩展");

            Assert.AreEqual(0, stageExt.Disposed);
            Assert.AreEqual(0, playgroundExt.Ended);
            pg.End();
            Assert.AreEqual(1, stageExt.Disposed, "舞台释放时恰好调一次 Dispose");
            Assert.AreEqual(1, playgroundExt.Ended, "会话结束时恰好调一次 OnSessionEnded");
        }

        [Test]
        public void StageExtension_ReturningFalseFromBuildScene_KeepsTheDefaultGround()
        {
            var stageExt = new RecordingStageExtension { BuildsScene = false };
            NewPlayground("2d_action", new RecordingPlaygroundExtension { Stage = stageExt });
            Frames(2);
            Assert.AreEqual(1, stageExt.BuildSceneCalls);
            Assert.IsNotNull(GameObject.Find("LabGround"), "返回 false：舞台照旧建缺省地面网格");
        }

        [Test]
        public void StageExtension_Projection_DrivesBillboardsAndFineDepthSort_InAnUprightCell()
        {
            var projection = new FakeProjection { Upright = true };
            var stageExt = new RecordingStageExtension { ProjectionToUse = projection };
            var pg = NewPlayground("2_5d_action", new RecordingPlaygroundExtension { Stage = stageExt });
            Frames(2);
            pg.SpawnDummy("stake");
            Frames(20);
            Assert.IsTrue(stageExt.Scene!.CameraAppliesPitch, "2.5D 格子的相机带俯仰");
            Assert.IsTrue(projection.FineDepthSortWritten.HasValue, "舞台在 BuildScene 之后把环绕开关写进投影");
            Assert.IsFalse(projection.FineDepthSortWritten!.Value, "没有环绕镜头：细粒度深度排序关");
            Assert.Greater(projection.FacingCalls, 0, "舞台按投影摆每个单位的广告牌朝向");
            Assert.Greater(stageExt.Billboards, 0, "摆完广告牌后通知扩展摆自己的道具");
        }

        [Test]
        public void StageExtension_FlatProjection_DoesNotBillboard()
        {
            var projection = new FakeProjection { Upright = false };
            var stageExt = new RecordingStageExtension { ProjectionToUse = projection };
            var pg = NewPlayground("2d_action", new RecordingPlaygroundExtension { Stage = stageExt });
            Frames(2);
            pg.SpawnDummy("stake");
            Frames(20);
            Assert.AreEqual(0, projection.FacingCalls, "平面投影（Upright 为假）：舞台不摆广告牌");
            Assert.AreEqual(0, stageExt.Billboards, "平面模式不调 OnBillboardsApplied");
        }

        [Test]
        public void PlaygroundExtension_IsAskedOncePerBegin_AndItsSettingsApply()
        {
            var stageExt = new RecordingStageExtension();
            var playgroundExt = new RecordingPlaygroundExtension { Stage = stageExt, PanelVisible = false, Hint = "ext hint", Roots = new[] { "data/_feel" } };
            var pg = NewPlayground("2d_action", playgroundExt);

            CollectionAssert.AreEqual(new[] { "2d_action" }, playgroundExt.RootRequests, "额外数据根按格子问一次");
            CollectionAssert.AreEqual(new[] { "2d_action" }, playgroundExt.StageRequests, "舞台扩展按格子要一次");
            Assert.AreEqual(1, pg.Session!.Script.Meta.ExtraDataRoots.Count(r => r == "data/_feel"), "扩展给的根已在宿主缺省根里时不重复加");
            Assert.IsFalse(pg.Model.PanelVisible, "扩展让调试面板开局收起");
            Assert.AreEqual("ext hint", pg.PanelHint, "面板提示来自扩展");
            Assert.AreEqual(1, playgroundExt.Started, "会话装配完成后通知一次");
            Assert.AreSame(pg.Stage, playgroundExt.StartedWith, "通知里带的是这次会话的舞台");
        }

        [Test]
        public void WithoutAnExtension_TheHostIsTheDefaultPlaceholderPlayground()
        {
            var pg = NewPlayground("2d_action", null);
            Frames(2);
            Assert.IsNull(pg.PanelHint, "没有扩展：没有面板提示");
            Assert.IsTrue(pg.Model.PanelVisible, "没有扩展：面板缺省展开");
            Assert.IsNull(pg.Stage!.Extension, "没有扩展：舞台没有扩展");
            Assert.IsNotNull(GameObject.Find("LabGround"), "没有扩展：缺省地面网格");
        }
    }
}
