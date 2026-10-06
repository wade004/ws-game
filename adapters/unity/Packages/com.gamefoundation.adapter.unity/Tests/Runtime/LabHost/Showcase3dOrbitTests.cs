#nullable enable
// Showcase3dOrbitTests：3D 演示场景的鼠标环绕镜头与相机相对移动（ADR-0159）的运行时验收（LabHost 风格，手动驱动）。
// 期望值都由规则算出（相机相对换算 (-sin θ, cos θ)、偏航步长 37 度的整数倍、容差取模拟量化），不写裸数；
// 约束：固定镜头与偏航 0 的环绕镜头，逻辑指纹与原 3D 试玩场景逐字节一致（演示场景只改呈现）。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.LabHost;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Lab;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.LabHost
{
    [Category("module:lab")]
    public sealed class Showcase3dOrbitTests
    {
        private const double Frame = 1.0 / 60.0;
        private const string AttackKey = "j";
        private GameObject? _go;
        private LabPlayground? _pg;
        private Adapters.Stub.StubInput? _input;
        private string _saveDir = string.Empty;
        private string _logic = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "lab_showcase3d_orbit_test_" + Guid.NewGuid().ToString("N"));
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

        private LabPlayground NewPlayground(bool showcase, Action<LabPlayground>? configure = null)
        {
            _go = new GameObject("Showcase3dOrbitTest");
            var pg = _go.AddComponent<LabPlayground>();
            pg.Configure("3d_action");
            pg.Showcase = showcase;
            pg.ManualDrive = true;
            pg.FlashIntensitySource = () => 1.0;   // 不读本机设置文件
            _input = new Adapters.Stub.StubInput();
            pg.InputSource = _input;
            pg.PadReader = _ => false;
            pg.SaveDirectory = _saveDir;
            configure?.Invoke(pg);
            Assert.IsTrue(pg.Begin(), pg.Model.Status);
            _pg = pg;
            return pg;
        }

        private IEnumerator Frames(int n)
        {
            for (var i = 0; i < n; i++)
            {
                _pg!.Tick(Frame);
                _pg.FinishFrame();
                if (i % 4 == 3)
                {
                    yield return null;
                }
            }
        }

        private IEnumerator Tap(string key, int hold = 3)
        {
            _input!.Press(key);
            yield return Frames(hold);
            _input.Release(key);
        }

        private static (double X, double Y) HeroPosition(LabPlayground pg)
        {
            var ctx = pg.Session!.Context!;
            var p = ctx.World.World.GetEntity(ctx.PlayerId)!.Position;
            return (p.X, p.Y);
        }

        private static void ApplyUpright(GameObject root)
        {
            foreach (var u in root.GetComponentsInChildren<ModelGroundUpright>(true))
            {
                u.Apply();
            }
        }

        // ───────── 逻辑不变：固定镜头与偏航 0 的环绕镜头，指纹与原 3D 试玩场景逐字节一致 ─────────

        private IEnumerator Scripted()
        {
            var pg = _pg!;
            pg.SpawnDummy("stake");
            pg.SpawnDummy("elite");
            yield return Frames(6);
            _input!.Press("d");
            yield return Frames(12);
            _input.Release("d");
            yield return Tap(AttackKey);
            yield return Frames(16);
            yield return Tap(AttackKey);
            yield return Frames(16);
            yield return Tap(AttackKey);
            yield return Frames(40);
            pg.EliteSwing();
            yield return Frames(50);
            _input.Press("k");
            yield return Frames(3);
            _input.Release("k");
            yield return Frames(30);
        }

        private static string LogicOf(LabPlayground pg)
        {
            var script = pg.Session!.Script;
            pg.End();
            var recording = pg.FinalRecording!;
            return pg.Host!.Runner.FingerprintOf(script, "3d_action", recording).Project(pg.Host.HeadlessRunner.Registry, MetricClass.Logic);
        }

        private IEnumerator RunScriptedLogic(bool showcase, Action<LabPlayground>? configure, Action<LabPlayground>? afterBegin = null)
        {
            NewPlayground(showcase, configure);
            afterBegin?.Invoke(_pg!);
            yield return Scripted();
            _logic = LogicOf(_pg!);
            Dispose();
        }

        [UnityTest]
        public IEnumerator LogicFingerprint_IsByteIdentical_ToOriginalPlayground_InFixedMode_AndInOrbitAtYawZero()
        {
            yield return RunScriptedLogic(false, null);
            var original = _logic;
            Assert.Greater(original.Length, 200, "指纹不是空的");

            yield return RunScriptedLogic(true, pg => pg.OrbitCameraStartsEnabled = false);
            Assert.AreEqual(original, _logic, "固定镜头：同一段输入的逻辑指纹与原 3D 试玩场景逐字节一致");

            yield return RunScriptedLogic(true, null);
            Assert.AreEqual(original, _logic, "鼠标环绕、偏航 0：相机相对换算是恒等变换，逻辑指纹与原 3D 试玩场景逐字节一致");

            // 运行中开关一轮（偏航仍为 0）也不改变逻辑。
            yield return RunScriptedLogic(true, null, pg =>
            {
                pg.SetOrbitCamera(false);
                pg.SetOrbitCamera(true);
            });
            Assert.AreEqual(original, _logic, "开关一轮后偏航仍为 0，逻辑指纹不变");
        }

        // ───────── 相机相对移动：W 永远朝屏幕上方走 ─────────

        /// <summary>偏航步长（度）：每次取其整数倍，不取 90 度的倍数，避免只测到轴对齐的方向。</summary>
        private const double YawStepDegrees = 37.0;

        [UnityTest]
        public IEnumerator MoveUp_WalksTowardScreenUp_AtEveryYaw_WorldDirectionIsMinusSinCos()
        {
            var pg = NewPlayground(true);
            Assert.IsTrue(pg.OrbitCameraOn, "3D 演示场景缺省是鼠标环绕");
            const int HoldFrames = 24;
            var yaws = Enumerable.Range(0, 10).Select(k => k * YawStepDegrees).ToList();
            foreach (var yaw in yaws)
            {
                pg.Orbit!.SnapTo(yaw, 0.0, 1.0);
                yield return Frames(4);                       // 偏航标记在固定步边界提交给相机的朝向查询
                var before = HeroPosition(pg);
                var controlsBefore = pg.Stage!.Record.Controls.Count;
                _input!.Press("w");
                yield return Frames(HoldFrames);
                _input.Release("w");
                yield return Frames(6);
                var after = HeroPosition(pg);
                var dx = after.X - before.X;
                var dy = after.Y - before.Y;
                var length = Math.Sqrt(dx * dx + dy * dy);
                Assert.Greater(length, 0.0, "偏航 " + yaw + " 度：玩家应当移动了");

                var rad = yaw * Math.PI / 180.0;
                var expected = Math.Atan2(Math.Cos(rad), -Math.Sin(rad));
                var actual = Math.Atan2(dy, dx);
                var errorRad = Math.Abs(Math.IEEERemainder(actual - expected, 2.0 * Math.PI));
                // 容差 = 一个模拟步的位置量化：位移 length 由 HoldFrames 个步累出，一步的量化（含起步与收尾各差一步）换成角度。
                var quantum = 2.0 * length / HoldFrames;
                var tolerance = Math.Atan2(quantum, length);
                Assert.LessOrEqual(errorRad, tolerance, "偏航 " + yaw + " 度：世界移动方向应为 (-sin θ, cos θ)；实际位移 (" + dx + ", " + dy + ")");

                // 舞台对输入映射换算结果的三向核对（真实相机右/上轴、偏航公式、回到屏幕）。
                var samples = pg.Stage.Record.Controls.Skip(controlsBefore).ToList();
                Assert.Greater(samples.Count, 0, "偏航 " + yaw + " 度：应有相机相对样本");
                Assert.LessOrEqual(samples.Max(s => s.ErrorDegrees), 0.01, "偏航 " + yaw + " 度：换算与真实相机轴的夹角");
                Assert.LessOrEqual(samples.Max(s => s.ScreenErrorDegrees), 0.05, "偏航 " + yaw + " 度：沿换算方向走回屏幕应是摇杆方向（上）");
            }

            Assert.AreEqual(0, pg.Stage!.Record.Errors.Count, string.Join(" | ", pg.Stage.Record.Errors));
        }

        [UnityTest]
        public IEnumerator FixedMode_MoveUp_WalksWorldUp_RegardlessOfAnyMouseInput()
        {
            var pg = NewPlayground(true, p => p.OrbitCameraStartsEnabled = false);
            Assert.IsFalse(pg.OrbitCameraOn);
            pg.OrbitInput(300.0, 120.0, 3.0);       // 固定镜头下忽略
            yield return Frames(4);
            Assert.IsTrue(pg.Orbit!.IsAtDefault, "固定镜头忽略鼠标");
            var before = HeroPosition(pg);
            _input!.Press("w");
            yield return Frames(24);
            _input.Release("w");
            var after = HeroPosition(pg);
            Assert.AreEqual(before.X, after.X, 1e-6, "固定镜头 W = 世界 +Y，X 不变");
            Assert.Greater(after.Y, before.Y);
        }

        // ───────── 声明：没有偏航来源的 camera_relative 仍在声明期报错 ─────────

        private sealed class NoYawSourceExtension : LabHostExtension
        {
            public override string? ControlSpaceOverride => ControlSpace.CameraRelative;
        }

        [Test]
        public void CameraRelative_WithoutAYawSource_StillErrorsAtDeclaration()
        {
            var host = LabHostTestSupport.Host;
            var script = LabHostTestSupport.Script("diagonal");
            var ex = Assert.Catch(() => host.HeadlessRunner.Record(script, "3d_targeted", null, new NoYawSourceExtension()));
            Assert.IsNotNull(ex);
            var text = ex!.ToString();
            Assert.IsTrue(text.Contains("camera_relative") && text.Contains("CameraOrientation"), "应指明缺相机朝向查询：" + text);
        }

        [UnityTest]
        public IEnumerator Showcase3d_DeclaresCameraRelative_ThroughItsOwnOptions_NotThroughCellData()
        {
            var pg = NewPlayground(true);
            yield return Frames(2);
            Assert.AreEqual(ControlSpace.CameraRelative, pg.Stage!.ControlSpaceOverride, "演示场景在自己的选项里声明相机相对");
            Assert.IsNotNull(pg.Stage.CameraOrientation);
            Assert.AreSame(pg.Stage.StageUnityCamera, pg.Stage.CameraOrientation, "偏航来源就是舞台上那台真实相机（框架的 ICameraOrientation 接口）");
            Dispose();

            pg = NewPlayground(false);
            yield return Frames(2);
            Assert.IsNull(pg.Stage!.ControlSpaceOverride, "原 3D 试玩场景保持格子数据的 world，不覆盖");
            Assert.IsFalse(pg.HasOrbitCamera);
        }

        // ───────── 视图一致性：任意偏航与俯角下，广告牌、血条、特效都正对相机 ─────────

        private static void AssertCameraSane(Camera cam, string tag)
        {
            Assert.Less(cam.transform.position.z, 0f, tag + "：相机在地面之上（-Z 一侧）");
            Assert.Greater(cam.transform.forward.z, 0f, tag + "：相机朝下看地面");
            Assert.Less(cam.transform.up.z, 0f, tag + "：屏幕上方朝物理上方（-Z），画面没有翻转");
        }

        /// <summary>视口四角的视线都落在地面砖范围内（没有露出地面之外的天空/背面）。</summary>
        private static string? FloorCoverageProblem(Camera cam, float halfExtent)
        {
            foreach (var (vx, vy) in new[] { (0f, 0f), (1f, 0f), (0f, 1f), (1f, 1f) })
            {
                var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f));
                if (ray.direction.z <= 1e-4f)
                {
                    return "视口角 (" + vx + "," + vy + ") 的视线不落向地面";
                }

                var t = -ray.origin.z / ray.direction.z;
                var hit = ray.origin + ray.direction * t;
                if (t <= 0f || Mathf.Abs(hit.x) > halfExtent || Mathf.Abs(hit.y) > halfExtent)
                {
                    return "视口角 (" + vx + "," + vy + ") 落在地面砖之外：" + hit;
                }
            }

            return null;
        }

        [UnityTest]
        public IEnumerator Billboards_PropsFxAndHpBars_FaceTheCamera_AtNonZeroYaw_AndTwoPitches()
        {
            var pg = NewPlayground(true);
            pg.SpawnDummy("mob", 3);
            pg.SpawnDummy("elite");
            yield return Frames(60);
            var director = pg.Stage!.Showcase!;
            var camera = pg.Stage.StageCamera!;
            var ctx = pg.Session!.Context!;
            var hud = pg.Hud!;
            Assert.Greater(director.UprightPropCount, 0);
            var floor = GameObject.Find("ShowcaseGround")!.GetComponent<SpriteRenderer>().bounds.extents.x;
            var combos = new List<(double Yaw, double Pitch, double Zoom)>
            {
                (53.0, -pg.Orbit!.Options.PitchRangeBelowBase, 1.0),
                (53.0, pg.Orbit.Options.PitchRangeAboveBase, 1.0),
                (200.0, -pg.Orbit.Options.PitchRangeBelowBase, pg.Orbit.Options.ZoomMaxFactor),
                (-120.0, pg.Orbit.Options.PitchRangeAboveBase, pg.Orbit.Options.ZoomMinFactor),
            };
            var barsChecked = 0;
            var fxChecked = 0;
            foreach (var (yaw, pitch, zoom) in combos)
            {
                var tag = "yaw=" + yaw + " pitch偏移=" + pitch + " 缩放系数=" + zoom;
                pg.ClearDummies();
                pg.SpawnDummy("mob", 3);
                pg.SpawnDummy("elite");
                pg.Orbit.SnapTo(yaw, pitch, zoom);
                yield return Frames(30);
                AssertCameraSane(camera, tag);
                Assert.IsNull(FloorCoverageProblem(camera, floor), tag + "：地面砖盖满视口");
                foreach (var prop in director.UprightProps)
                {
                    Assert.Less(Vector3.Angle(prop.forward, camera.transform.forward), 0.5f, tag + "：道具广告牌与相机平面平行");
                    Assert.Less(Vector3.Angle(prop.up, camera.transform.up), 0.5f, tag + "：道具广告牌不歪");
                }

                // 打出一串命中：火花、挥砍拖影、尘土都出现，逐帧核对它们正对相机；命中过的敌人头顶血条应在其头顶的投影点。
                var hits = director.HitLog.Count;
                _input!.Press(AttackKey);
                for (var i = 0; i < 40; i++)
                {
                    yield return Frames(1);
                    foreach (var (tr, billboard) in director.ActiveFx())
                    {
                        if (billboard)
                        {
                            fxChecked++;
                            Assert.Less(Vector3.Angle(tr.forward, camera.transform.forward), 0.5f, tag + "：特效广告牌正对相机");
                        }
                    }
                }

                _input.Release(AttackKey);
                yield return Frames(6);
                foreach (var pair in ctx.Dummies)
                {
                    var anchor = hud.EnemyBarAnchor(pair.Value);
                    var pos = director.PositionOf(pair.Value);
                    if (!anchor.HasValue || !pos.HasValue)
                    {
                        continue;
                    }

                    var head = hud.CanvasPointOf(director.Lift(pos.Value, director.HeadHeightOf(pair.Value) + 0.1f));
                    Assert.IsTrue(head.HasValue, tag + "：血条显示着，头顶点必在相机前");
                    Assert.Less((anchor.Value - head!.Value).magnitude, 1.0f, tag + "：血条跟着头顶点的投影走 " + pair.Key);
                    barsChecked++;
                }

                foreach (var m in pg.Stage.RenderedModels())
                {
                    ApplyUpright(m.Root);
                    var model = m.Root.transform.Find("Visual/UprightPivot/Model");
                    Assert.IsNotNull(model);
                    Assert.Greater(Vector3.Dot(model!.up, Vector3.back), 0.99f, tag + "：" + m.Entity.Value + " 站在地面上，头顶朝 -Z");
                }

                Assert.Greater(director.HitLog.Count, hits, tag + "：这一轮应当有命中");
            }

            Assert.Greater(fxChecked, 0, "核对过至少一个在播的广告牌特效");
            Assert.Greater(barsChecked, 0, "核对过至少一个显示着的血条");
            Assert.AreEqual(0, pg.Stage.Record.Errors.Count, string.Join(" | ", pg.Stage.Record.Errors));
        }

        [UnityTest]
        public IEnumerator PropAndWallSortOrder_FollowsGroundDepthAlongTheCameraUp_AtAnyYaw()
        {
            var pg = NewPlayground(true);
            yield return Frames(30);
            var director = pg.Stage!.Showcase!;
            var camera = pg.Stage.StageCamera!;
            Assert.Greater(director.UprightPropRenderers.Count, 1, "至少两个直立道具才有次序可比");
            foreach (var yaw in new[] { 0.0, 90.0, 200.0, -75.0 })
            {
                pg.Orbit!.SnapTo(yaw, 0.0, 1.0);
                yield return Frames(4);
                var up = camera.transform.up;
                var dir = new Vector2(up.x, up.y).normalized;
                var props = director.UprightPropRenderers.Select((r, i) => (Order: r.sortingOrder, Depth: Vector2.Dot(director.UprightPropFeet[i], dir))).ToList();
                for (var i = 0; i < props.Count; i++)
                {
                    for (var j = 0; j < props.Count; j++)
                    {
                        // 脚在屏幕更靠上（更远）的道具必须先画（次序更小）；深度差小于一个次序档的忽略。
                        if (props[i].Depth - props[j].Depth > 0.25)
                        {
                            Assert.Less(props[i].Order, props[j].Order, "偏航 " + yaw + " 度：更远的道具（深度 " + props[i].Depth + "）应先于更近的（" + props[j].Depth + "）画");
                        }
                    }
                }
            }
        }

        // ───────── 切回固定：缺省姿态逐位还原；复位按钮；鼠标输入入口 ─────────

        private static (Vector3 Position, Quaternion Rotation, float Fov, float Size, bool Ortho) PoseOf(Camera c) =>
            (c.transform.position, c.transform.rotation, c.fieldOfView, c.orthographicSize, c.orthographic);

        [UnityTest]
        public IEnumerator ToggleBackToFixed_RestoresTheDefaultPose_Exactly()
        {
            // 参照：不装环绕镜头的演示场景（= 此前的固定镜头）。
            NewPlayground(true, p => p.OrbitCameraAllowed = false);
            yield return Frames(30);
            var reference = PoseOf(_pg!.Stage!.StageCamera!);
            Assert.IsFalse(_pg.HasOrbitCamera);
            Dispose();

            var pg = NewPlayground(true, p => p.OrbitCameraStartsEnabled = false);
            yield return Frames(30);
            var camera = pg.Stage!.StageCamera!;
            Assert.AreEqual(reference, PoseOf(camera), "固定模式的姿态与没有环绕镜头时逐位一致");

            pg.SetOrbitCamera(true);
            pg.OrbitInput(-410.0, 90.0, 4.0);
            yield return Frames(40);
            Assert.AreNotEqual(reference.Rotation, camera.transform.rotation, "环绕之后姿态变了");
            Assert.AreNotEqual(0.0, pg.Orbit!.Yaw);
            pg.SetOrbitCamera(false);
            yield return Frames(4);
            Assert.AreEqual(reference, PoseOf(camera), "切回固定后缺省姿态逐位还原（位置、旋转、视场、缩放）");
            Assert.IsTrue(pg.Orbit.IsAtDefault);
            Assert.AreEqual(0.0, pg.Stage.StageUnityCamera!.YawRadians, 0.0, "偏航来源也归零");
        }

        [UnityTest]
        public IEnumerator ResetCamera_SmoothlyReturnsToTheDefaultPose_AndZeroYawSample()
        {
            var pg = NewPlayground(true);
            yield return Frames(30);
            var camera = pg.Stage!.StageCamera!;
            var reference = PoseOf(camera);
            pg.OrbitInput(500.0, -80.0, -2.0);
            yield return Frames(30);
            Assert.AreNotEqual(reference, PoseOf(camera));
            pg.ResetCamera();
            yield return Frames(240);
            Assert.IsTrue(pg.Orbit!.IsAtDefault, "复位后控制器回到缺省：" + pg.Orbit.Yaw + "/" + pg.Orbit.PitchOffset + "/" + pg.Orbit.ZoomFactor);
            Assert.AreEqual(0.0, pg.Stage.StageUnityCamera!.YawRadians, 0.0, "复位后偏航来源为 0（标记已提交）");
            Assert.AreEqual(reference.Position.x, camera.transform.position.x, 1e-4);
            Assert.AreEqual(reference.Position.y, camera.transform.position.y, 1e-4);
            Assert.AreEqual(reference.Position.z, camera.transform.position.z, 1e-4);
        }

        [UnityTest]
        public IEnumerator MouseInput_DragMapsToYawAndPitch_WheelToZoom_WithinLimits_AndYawIsUnbounded()
        {
            var pg = NewPlayground(true);
            yield return Frames(4);
            var o = pg.Orbit!;
            var options = o.Options;
            pg.OrbitInput(100.0, 0.0, 0.0);
            Assert.AreEqual(-100.0 * options.YawDegreesPerPixel, o.TargetYaw, 1e-9, "向右拖 = 偏航减小");
            // 偏航不设上下限：连续拖出几圈。
            var turns = 5.0;
            var pixels = 360.0 * turns / options.YawDegreesPerPixel;
            pg.OrbitInput(-pixels, 0.0, 0.0);
            Assert.Greater(o.TargetYaw, 360.0 * (turns - 1), "偏航不受限");
            yield return Frames(120);
            Assert.AreEqual(o.TargetYaw, o.Yaw, 1e-3, "平滑后追上目标");
            pg.OrbitInput(0.0, 100000.0, 0.0);
            Assert.AreEqual(options.PitchRangeAboveBase, o.TargetPitchOffset, 1e-9, "俯角上限");
            pg.OrbitInput(0.0, -100000.0, 0.0);
            Assert.AreEqual(-options.PitchRangeBelowBase, o.TargetPitchOffset, 1e-9, "俯角下限");
            pg.OrbitInput(0.0, 0.0, 100.0);
            Assert.AreEqual(options.ZoomMinFactor, o.TargetZoomFactor, 1e-9, "拉近下限");
            pg.OrbitInput(0.0, 0.0, -100.0);
            Assert.AreEqual(options.ZoomMaxFactor, o.TargetZoomFactor, 1e-9, "拉远上限");
            yield return Frames(120);
            var pitch = pg.Stage!.StageUnityCamera!.EffectivePitchDegrees;
            Assert.GreaterOrEqual(pitch, options.PitchMinDegrees - 1e-6);
            Assert.LessOrEqual(pitch, options.PitchMaxDegrees + 1e-6);
        }

        [UnityTest]
        public IEnumerator YawMarker_IsRecorded_AndTheSimSeesOneYawPerFixedStep()
        {
            var pg = NewPlayground(true);
            yield return Frames(4);
            var markers = pg.Session!.Script.Events.Count(e => e.Kind == ScriptEventKind.Marker && e.Action == EngineLabStage.ScriptYawMarker);
            pg.OrbitInput(-200.0, 0.0, 0.0);
            yield return Frames(30);
            var after = pg.Session.Script.Events.Count(e => e.Kind == ScriptEventKind.Marker && e.Action == EngineLabStage.ScriptYawMarker);
            Assert.Greater(after, markers, "偏航变化录成脚本标记");
            var committed = pg.Stage!.StageUnityCamera!.CommittedYawDegrees;
            Assert.AreEqual(pg.Orbit!.Yaw, committed, 1e-9, "稳定后提交的偏航 = 控制器偏航");
            Assert.AreEqual(committed * Math.PI / 180.0, pg.Stage.StageUnityCamera.YawRadians, 1e-12);
        }

        // ───────── 可选：环绕镜头截图（GF_LAB_SCREENSHOT_DIR 指定目录才跑；要有窗口的编辑器，批处理下不渲染屏幕；门禁不设）─────────

        private IEnumerator Shot(string dir, string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Screenshots_WhenRequested()
        {
            var dir = Environment.GetEnvironmentVariable("GF_LAB_SCREENSHOT_DIR");
            if (string.IsNullOrEmpty(dir) || Application.isBatchMode)
            {
                Assert.Pass("未设置 GF_LAB_SCREENSHOT_DIR（或批处理模式），跳过截图。");
            }

            Directory.CreateDirectory(dir!);
            var pg = NewPlayground(true);
            var director = pg.Stage!.Showcase!;
            var o = pg.Orbit!;
            pg.SpawnDummy("mob", 3);
            pg.SpawnDummy("elite");
            pg.SpawnDummy("stake");
            yield return Frames(90);

            var pitches = new[] { ("default", 0.0), ("low", -o.Options.PitchRangeBelowBase), ("high", o.Options.PitchRangeAboveBase) };
            foreach (var yaw in new[] { 0.0, 90.0, 200.0 })
            {
                foreach (var (pitchName, pitch) in pitches)
                {
                    o.SnapTo(yaw, pitch, 1.0);
                    yield return Frames(20);
                    yield return Shot(dir!, "yaw" + (int)yaw + "_pitch_" + pitchName);
                }
            }

            // 拉近的连击命中瞬间（偏航 35 度）。
            o.SnapTo(35.0, 0.0, o.Options.ZoomMinFactor);
            yield return Frames(30);
            var n0 = director.HitLog.Count;
            _input!.Press(AttackKey);
            for (var i = 0; i < 90 && director.HitLog.Count <= n0; i++)
            {
                yield return Frames(1);
            }

            _input.Release(AttackKey);
            yield return Frames(3);
            yield return Shot(dir!, "zoomed_in_combo_impact");

            // 拉远。
            o.SnapTo(-60.0, 0.0, o.Options.ZoomMaxFactor);
            yield return Frames(40);
            yield return Shot(dir!, "zoomed_out");

            // F1 面板上的镜头开关：环绕 → 固定。
            o.SnapTo(0.0, 0.0, 1.0);
            pg.Model.PanelVisible = true;
            pg.SetTab(LabTab.Scene);
            yield return Frames(12);
            yield return Shot(dir!, "f1_toggle_orbit");
            pg.SetOrbitCamera(false);
            yield return Frames(12);
            yield return Shot(dir!, "f1_toggle_fixed");
            Debug.Log("[Showcase3dOrbitTests] screenshots done: hits=" + director.HitLog.Count);
        }

        // ───────── 控制器与相机的单元用例（不依赖场景）─────────

        [Test]
        public void OrbitController_IsDeterministic_GivenTheSameInputAndFrameIntervals()
        {
            OrbitCameraController Run()
            {
                var c = new OrbitCameraController();
                c.SetEnabled(true);
                var dts = new[] { 1.0 / 60.0, 1.0 / 30.0, 0.02, 1.0 / 144.0, 0.1 };
                for (var i = 0; i < 200; i++)
                {
                    if (i % 7 == 0)
                    {
                        c.AddDrag(13.0 - i % 5, 4.0 * (i % 3 - 1));
                    }

                    if (i % 11 == 0)
                    {
                        c.AddWheel(i % 2 == 0 ? 1.0 : -2.0);
                    }

                    c.Step(dts[i % dts.Length]);
                }

                return c;
            }

            var a = Run();
            var b = Run();
            Assert.AreEqual(a.Yaw, b.Yaw, 0.0);
            Assert.AreEqual(a.PitchOffset, b.PitchOffset, 0.0);
            Assert.AreEqual(a.ZoomFactor, b.ZoomFactor, 0.0);
        }

        [Test]
        public void OrbitController_Disabled_IgnoresInput_AndDisablingSnapsToDefault()
        {
            var c = new OrbitCameraController();
            c.SetEnabled(false);
            c.AddDrag(100.0, 100.0);
            c.AddWheel(3.0);
            Assert.IsTrue(c.IsAtDefault);
            c.SetEnabled(true);
            c.SnapTo(123.0, 5.0, 0.7);
            Assert.IsFalse(c.IsAtDefault);
            c.SetEnabled(false);
            Assert.IsTrue(c.IsAtDefault, "关闭 = 逐位回到缺省");
            Assert.AreEqual(0.0, c.EffectivePitch(0.0) - c.Options.PitchMinDegrees, 0.0, "俯角取区间下沿（基准 0 时）");
        }

        [Test]
        public void OrbitController_ResetSmooth_TakesTheShortWayBackAfterManyTurns()
        {
            var c = new OrbitCameraController();
            c.SetEnabled(true);
            c.SnapTo(3.0 * 360.0 + 20.0, 0.0, 1.0);
            c.ResetSmooth();
            Assert.AreEqual(20.0, c.Yaw, 1e-9, "先折到 ±180 度内再回头，走最短的路");
            for (var i = 0; i < 600; i++)
            {
                c.Step(1.0 / 60.0);
            }

            Assert.IsTrue(c.IsAtDefault);
        }

        [Test]
        public void UnityCamera_YawSampledAtCommit_ReportsOnlyTheCommittedYaw_WhileTheViewTurnsSmoothly()
        {
            var go = new GameObject("OrbitCamera");
            try
            {
                var cam = go.AddComponent<Camera>();
                var uc = new UnityCamera(cam);
                uc.Configure(45, 0, new ZoomRange(1, 10));
                uc.ApplyYawRotation = true;
                uc.SampleYawAtCommit = true;
                Assert.AreEqual(0.0, uc.YawRadians, 0.0);
                uc.SetView(30.0, 45.0);
                Assert.AreEqual(0.0, uc.YawRadians, 0.0, "画面转了，但逻辑侧取样的偏航还没提交");
                var right = cam.transform.right;
                Assert.AreEqual(Math.Cos(30.0 * Math.PI / 180.0), right.x, 1e-5, "画面上的相机右轴已按 30 度转过");
                uc.CommitYaw(30.0);
                Assert.AreEqual(30.0 * Math.PI / 180.0, uc.YawRadians, 1e-12, "提交后查询报新偏航");
                Assert.IsInstanceOf<Core.Foundation.EngineAdapter.ICameraOrientation>(uc, "相机实现框架的相机朝向接口");
                uc.SampleYawAtCommit = false;
                Assert.AreEqual(30.0 * Math.PI / 180.0, uc.YawRadians, 1e-6, "关闭后回到报实时偏航");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
