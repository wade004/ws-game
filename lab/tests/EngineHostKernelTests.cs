using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Feel;
using Lab;
using Presentation.FeedbackBinder.Contracts;
using Presentation.VfxSfx.Contracts;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 引擎宿主在内核侧的部件（06 第 4 节）：宿主扩展点、相机相对输入（框架原生控制空间）、输入噪声模型、覆盖存储与 A/B、引擎度量组。
    /// 每个机制一条复现用例（机制真的起作用）加一条不变量用例（缺省与旁路不改变既有行为）；期望值由规则算出，不写死裸数。
    /// 引擎宿主本身（Unity 适配层里的实现）的用例在 Unity 的 PlayMode 测试里。
    /// </summary>
    public sealed class EngineHostKernelTests
    {
        // ------------------------------------------------------------------
        // 宿主扩展点
        // ------------------------------------------------------------------

        private sealed class CountingExtension : LabHostExtension
        {
            public int Attached;
            public int Ready;
            public int StepEnds;
            public int Frames;
            public int Finished;
            public readonly List<int> StepTicks = new List<int>();
            public readonly List<string> TeeKinds = new List<string>();

            public override void OnAttach(LabHostContext context) => Attached++;

            public override void OnReady(LabHostContext context) => Ready++;

            public override void OnFixedStepEnd(int tick)
            {
                StepEnds++;
                StepTicks.Add(tick);
            }

            public override void OnFrame(int frame, double alpha, double frameSeconds) => Frames++;

            public override void OnFinished(LabRecording recording) => Finished++;

            public override IFeedbackSink? FeedbackTee => new Tee(TeeKinds);
        }

        private sealed class Tee : IFeedbackSink
        {
            private readonly List<string> _kinds;

            public Tee(List<string> kinds)
            {
                _kinds = kinds;
            }

            public void FloatingText(Id entityId, Id styleId, string text) => _kinds.Add("text");

            public void PlayVfx(Id vfxId, FeedbackAttachSpec attach) => _kinds.Add("vfx");

            public void PlaySfx(Id sfxId, Vec2? at) => _kinds.Add("sfx");

            public void Freeze(double durationMs) => _kinds.Add("freeze_ms");

            public void ShakeCamera(Id profileId) => _kinds.Add("shake");

            public void Flash(Id entityId, Id profileId) => _kinds.Add("flash");

            public void ImpactCamera(ImpactCameraCue cue) => _kinds.Add("camera");

            public void FreezePresentation(IReadOnlyList<Id> unitIds, int ticks, ImpactFreezeLayers layers) => _kinds.Add("freeze");

            public void ReleasePresentation(IReadOnlyList<Id> unitIds) => _kinds.Add("release");

            public bool HasPendingPlayback => false;

            public event Action? PendingPlaybackChanged
            {
                add { }
                remove { }
            }
        }

        private sealed class FixedYaw : Core.Foundation.EngineAdapter.ICameraOrientation
        {
            public FixedYaw(double yaw)
            {
                YawRadians = yaw;
            }

            public double YawRadians { get; }
        }

        /// <summary>提供固定偏航的相机朝向查询并要求 camera_relative：换算由框架的输入映射原生完成，扩展只给朝向、只读观测核对点。</summary>
        private sealed class AxisRotateExtension : LabHostExtension
        {
            private readonly FixedYaw _orientation;

            public readonly List<(int Tick, Vec2 Stick, Vec2 Mapped)> Axes = new List<(int, Vec2, Vec2)>();

            public AxisRotateExtension(double yaw)
            {
                _orientation = new FixedYaw(yaw);
            }

            public override Core.Foundation.EngineAdapter.ICameraOrientation? CameraOrientation => _orientation;

            public override string? ControlSpaceOverride => ControlSpace.CameraRelative;

            public override void OnMoveAxis(int tick, Vec2 stick, Vec2 mapped) => Axes.Add((tick, stick, mapped));
        }

        /// <summary>只提供朝向、不覆盖控制空间：格子自己声明 world 时轴值不能被换算。</summary>
        private sealed class OrientationOnlyExtension : LabHostExtension
        {
            public readonly List<(int Tick, Vec2 Stick, Vec2 Mapped)> Axes = new List<(int, Vec2, Vec2)>();

            public override Core.Foundation.EngineAdapter.ICameraOrientation? CameraOrientation { get; } = new FixedYaw(1.0);

            public override void OnMoveAxis(int tick, Vec2 stick, Vec2 mapped) => Axes.Add((tick, stick, mapped));
        }

        private sealed class OverrideWithoutOrientationExtension : LabHostExtension
        {
            public override string? ControlSpaceOverride => ControlSpace.CameraRelative;
        }

        private static string LogicOf(LabRunner runner, Fingerprint fp) => fp.Project(runner.Registry, MetricClass.Logic);

        [Fact]
        public void Extension_Hooks_FireByTheHostLoop_AndTeeSeesEveryPresentationInstruction()
        {
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("feel_melee");
            var ext = new CountingExtension();
            var recording = runner.Record(script, "2d_action", null, ext);

            // 期望由规则给出：每个固定步一次步末回调、步序号连续；每帧一次帧回调且与帧记录一一对应；其余各一次。
            Assert.Equal(script.Meta.DurationTicks, ext.StepEnds);
            Assert.Equal(Enumerable.Range(0, script.Meta.DurationTicks), ext.StepTicks);
            Assert.Equal(recording.Frames.Count, ext.Frames);
            Assert.Equal(1, ext.Attached);
            Assert.Equal(1, ext.Ready);
            Assert.Equal(1, ext.Finished);

            // 转发给扩展的反馈指令与记入表现时间线的逐条对应（同顺序同种类）。
            var recorded = recording.Feel!.Presentation.Select(p => p.Kind).ToList();
            Assert.NotEmpty(recorded);
            Assert.Equal(recorded, ext.TeeKinds);
        }

        [Fact]
        public void Extension_IsLogicTransparent_AcrossEveryScriptAndApplicableCell()
        {
            // 不变量：带一个只观测的扩展跑，逻辑组与无扩展逐字节一致（引擎侧表现驱动不回流逻辑）。
            var runner = LabTestSupport.Runner;
            var mismatches = new List<string>();
            var runs = 0;
            foreach (var script in LabTestSupport.AllScripts())
            {
                foreach (var cell in runner.ApplicableCells(script))
                {
                    if (!cell.CheckRunnable(LabHost.AvailableCapabilities).Runnable)
                    {
                        continue;
                    }

                    var plain = runner.Run(script, cell.Cell);
                    var observed = runner.Run(script, cell.Cell, null, new CountingExtension());
                    runs++;
                    if (!string.Equals(LogicOf(runner, plain), LogicOf(runner, observed), StringComparison.Ordinal))
                    {
                        mismatches.Add(script.Meta.ScriptId + "@" + cell.Cell);
                    }
                }
            }

            Assert.True(runs > 100, "应当覆盖全部脚本 × 适用且可运行的格子，实际只跑了 " + runs);
            Assert.True(mismatches.Count == 0, "扩展改变了逻辑组：" + string.Join(", ", mismatches));
        }

        [Fact]
        public void Extension_NullOverload_IsIdenticalToTheOldEntry()
        {
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("feel_combo3");
            var a = runner.Run(script, "3d_action");
            var b = runner.Run(script, "3d_action", null, null);
            Assert.Equal(LogicOf(runner, a), LogicOf(runner, b));
            Assert.Equal(
                a.Project(runner.Registry, MetricClass.Presentation), b.Project(runner.Registry, MetricClass.Presentation));
        }

        [Fact]
        public void NativeCameraRelative_IsLive_AndIdentityByDefault()
        {
            // 复现：脚本里一个朝 +X 的摇杆，经 90 度偏航的原生相机相对控制空间换算后，玩家最终位置沿 +Y（不再沿 +X）；
            // 不声明相机相对（计数扩展）与无扩展一致。
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("move_tap");
            var plain = runner.Record(script, "2d_targeted");
            var identity = runner.Record(script, "2d_targeted", null, new CountingExtension());
            var rotated = runner.Record(script, "2d_targeted", null, new AxisRotateExtension(Math.PI / 2.0));

            var start = plain.StartPosition;
            var endPlain = plain.Ticks[plain.Ticks.Count - 1].Position;
            var endIdentity = identity.Ticks[identity.Ticks.Count - 1].Position;
            var endRotated = rotated.Ticks[rotated.Ticks.Count - 1].Position;
            Assert.Equal(endPlain.X, endIdentity.X, 12);
            Assert.Equal(endPlain.Y, endIdentity.Y, 12);

            // 规则：位移向量按偏航旋转（旋转保持位移模长）。
            var displacementPlain = new Vec2(endPlain.X - start.X, endPlain.Y - start.Y);
            var displacementRotated = new Vec2(endRotated.X - start.X, endRotated.Y - start.Y);
            Assert.True(Math.Abs(displacementPlain.X) > 1e-9, "测试前置条件：脚本里玩家确实沿 X 走了一段");
            var expected = ControlSpace.CameraRelativeToWorld(displacementPlain, Math.PI / 2.0);
            Assert.Equal(expected.X, displacementRotated.X, 6);
            Assert.Equal(expected.Y, displacementRotated.Y, 6);
        }

        [Fact]
        public void NativeCameraRelative_ObservationPoint_ReportsDeviceStickAndTheInputMapsWorldAxis()
        {
            // 复现：每个脚本轴事件的固定步里，观测点给出"设备轴"与"输入映射的移动轴"，后者 = 前者按偏航旋转（规则算期望，不写死裸数）。
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("move_tap");
            var yaw = 40.0 * Math.PI / 180.0;
            var ext = new AxisRotateExtension(yaw);
            runner.Record(script, "2d_targeted", null, ext);
            Assert.NotEmpty(ext.Axes);
            foreach (var sample in ext.Axes)
            {
                var expected = ControlSpace.CameraRelativeToWorld(sample.Stick, yaw);
                Assert.Equal(expected.X, sample.Mapped.X, 9);
                Assert.Equal(expected.Y, sample.Mapped.Y, 9);
            }
        }

        [Fact]
        public void NativeCameraRelative_WorldCellWithOnlyAnOrientation_IsNeverConverted()
        {
            // 不变量：格子声明 world、扩展没有覆盖时，即使扩展提供了朝向，移动轴也原样等于设备轴，逻辑与无扩展逐位一致。
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("move_tap");
            var ext = new OrientationOnlyExtension();
            var withExt = runner.Record(script, "2d_targeted", null, ext);
            var plain = runner.Record(script, "2d_targeted");
            Assert.NotEmpty(ext.Axes);
            foreach (var sample in ext.Axes)
            {
                Assert.Equal(sample.Stick, sample.Mapped);
            }

            Assert.Equal(plain.Ticks[plain.Ticks.Count - 1].Position, withExt.Ticks[withExt.Ticks.Count - 1].Position);
        }

        [Fact]
        public void NativeCameraRelative_OverrideWithoutAnOrientationQuery_IsAnAssemblyError()
        {
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("move_tap");
            var ex = Assert.Throws<LabFormatException>(() =>
                runner.Record(script, "2d_targeted", null, new OverrideWithoutOrientationExtension()));
            Assert.Contains("CameraOrientation", ex.Message);
        }

        // ------------------------------------------------------------------
        // 控制空间
        // ------------------------------------------------------------------

        [Fact]
        public void ControlSpace_CameraRelative_RotatesStickByYaw_PreservingLength()
        {
            var sticks = new[] { new Vec2(0, 1), new Vec2(1, 0), new Vec2(-1, 0), new Vec2(0.6, -0.8), new Vec2(0.2, 0.1) };
            for (var yawDegrees = -180; yawDegrees <= 180; yawDegrees += 15)
            {
                var yaw = yawDegrees * Math.PI / 180.0;
                foreach (var stick in sticks)
                {
                    var world = ControlSpace.CameraRelativeToWorld(stick, yaw);
                    var stickLength = Math.Sqrt(stick.X * stick.X + stick.Y * stick.Y);
                    var worldLength = Math.Sqrt(world.X * world.X + world.Y * world.Y);
                    Assert.Equal(stickLength, worldLength, 12);

                    // 期望：相机的右/上轴在世界平面上的方向（由偏航直接给出）分量加权。
                    var right = new Vec2(Math.Cos(yaw), Math.Sin(yaw));
                    var up = new Vec2(-Math.Sin(yaw), Math.Cos(yaw));
                    var expected = ControlSpace.ExpectedFromAxes(stick, right, up);
                    Assert.Equal(expected.X, world.X, 12);
                    Assert.Equal(expected.Y, world.Y, 12);
                    Assert.True(ControlSpace.AngleDegrees(world, expected) < 1e-6);
                }
            }

            // 偏航 0：恒等；摇杆向上 + 偏航 θ → 世界方向与 +Y 的夹角恰为 |θ|。
            Assert.Equal(0.0, ControlSpace.AngleDegrees(ControlSpace.CameraRelativeToWorld(new Vec2(0, 1), 0), new Vec2(0, 1)), 9);
            Assert.Equal(30.0, ControlSpace.AngleDegrees(ControlSpace.CameraRelativeToWorld(new Vec2(0, 1), 30 * Math.PI / 180.0), new Vec2(0, 1)), 9);
        }

        [Fact]
        public void ControlSpace_AngleDegrees_ZeroVectorAndSymmetry()
        {
            Assert.Equal(0.0, ControlSpace.AngleDegrees(Vec2.Zero, new Vec2(1, 0)));
            Assert.Equal(90.0, ControlSpace.AngleDegrees(new Vec2(1, 0), new Vec2(0, 5)), 9);
            Assert.Equal(ControlSpace.AngleDegrees(new Vec2(1, 2), new Vec2(-3, 1)), ControlSpace.AngleDegrees(new Vec2(-3, 1), new Vec2(1, 2)), 12);
        }

        // ------------------------------------------------------------------
        // 输入噪声模型
        // ------------------------------------------------------------------

        private static List<ScriptEvent> DeviceEvents(InputScript script) =>
            script.Events.Where(e => e.Kind == ScriptEventKind.Press || e.Kind == ScriptEventKind.Release || e.Kind == ScriptEventKind.Axis).ToList();

        [Fact]
        public void InputNoise_PureLatency_ShiftsEveryDeviceEventByExactlyTheLatency()
        {
            var script = LabTestSupport.Script("attack_while_moving");
            var model = new InputNoiseModel(seed: 7UL, latencyTicks: 3, jitterTicks: 0);
            var noisy = model.Apply(script, out var record);

            Assert.Equal(script.Events.Count, noisy.Events.Count);
            Assert.All(record.Offsets, o => Assert.Equal(3, o));
            // 规则：每条设备事件的新 tick = 旧 tick + 延迟；动作/种类/取值不变。
            for (var i = 0; i < script.Events.Count; i++)
            {
                Assert.Equal(script.Events[i].Tick + 3, noisy.Events[i].Tick);
                Assert.Equal(script.Events[i].Action, noisy.Events[i].Action);
                Assert.Equal(script.Events[i].Kind, noisy.Events[i].Kind);
                Assert.Equal(script.Events[i].Value.X, noisy.Events[i].Value.X);
            }
        }

        [Fact]
        public void InputNoise_Jitter_StaysInsideBounds_KeepsPerActionOrder_AndOnlyShiftsDeviceEvents()
        {
            const int latency = 2;
            const int jitter = 4;
            foreach (var script in LabTestSupport.AllScripts())
            {
                var model = new InputNoiseModel(seed: 20261003UL, latencyTicks: latency, jitterTicks: jitter);
                model.Apply(script, out var record);
                Assert.Equal(script.Events.Count, record.Offsets.Count);

                var lastByAction = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var i = 0; i < script.Events.Count; i++)
                {
                    var original = script.Events[i];
                    var device = original.Kind == ScriptEventKind.Press || original.Kind == ScriptEventKind.Release || original.Kind == ScriptEventKind.Axis;
                    if (!device)
                    {
                        Assert.Equal(0, record.Offsets[i]);
                        continue;
                    }

                    var newTick = original.Tick + record.Offsets[i];
                    Assert.True(newTick >= 0);
                    // 界：未被"下限 0"与"同动作不倒序"抬高时，偏移落在 [延迟 − 抖动, 延迟 + 抖动]；抬高只会让偏移更大。
                    Assert.True(record.Offsets[i] >= latency - jitter || newTick == 0 || newTick == (lastByAction.TryGetValue(original.Action, out var floor) ? floor : -1));
                    if (lastByAction.TryGetValue(original.Action, out var last))
                    {
                        Assert.True(newTick >= last, $"{script.Meta.ScriptId}：动作 {original.Action} 的事件被倒序");
                    }

                    lastByAction[original.Action] = newTick;
                }
            }
        }

        [Fact]
        public void InputNoise_ReplayOfRecord_ReproducesTheNoisyScriptEventForEvent_AndNoneIsIdentity()
        {
            var script = LabTestSupport.Script("attack_while_moving");
            var model = new InputNoiseModel(seed: 99UL, latencyTicks: 1, jitterTicks: 3);
            var noisy = model.Apply(script, out var record);
            var again = model.Apply(script, out var record2);
            Assert.Equal(record.ToJson(), record2.ToJson());

            var parsed = InputNoiseRecord.Parse(record.ToJson());
            var replayed = InputNoiseModel.Replay(script, parsed);
            Assert.Equal(noisy.Events.Count, replayed.Events.Count);
            for (var i = 0; i < noisy.Events.Count; i++)
            {
                Assert.Equal(noisy.Events[i].Tick, replayed.Events[i].Tick);
                Assert.Equal(noisy.Events[i].Action, replayed.Events[i].Action);
                Assert.Equal(noisy.Events[i].Kind, replayed.Events[i].Kind);
                Assert.Equal(again.Events[i].Tick, replayed.Events[i].Tick);
            }

            // 不同种子不同结果（噪声确实在起作用，不是恒等）。
            var other = new InputNoiseModel(seed: 100UL, latencyTicks: 1, jitterTicks: 3).Apply(script, out var otherRecord);
            Assert.NotEqual(record.ToJson(), otherRecord.ToJson());
            _ = other;

            // 无噪声模型：逐事件不变。
            var none = InputNoiseModel.None.Apply(script, out var noneRecord);
            Assert.True(InputNoiseModel.None.IsNone);
            Assert.Equal("none", InputNoiseModel.None.Id);
            Assert.All(noneRecord.Offsets, o => Assert.Equal(0, o));
            Assert.Equal(script.Events.Select(e => (e.Tick, e.Action, e.Kind)), none.Events.Select(e => (e.Tick, e.Action, e.Kind)));

            // 记录与脚本不匹配时显式失败，不静默回放。
            Assert.Throws<LabFormatException>(() => InputNoiseModel.Replay(LabTestSupport.Script("move_tap"), parsed));
        }

        [Fact]
        public void InputNoise_Latency_DelaysTheLogicResponseByExactlyTheLatencyTicks()
        {
            // 复现：同一脚本加 L tick 延迟后，首次提交移动请求的 tick 恰好晚 L（由规则算出，不写死）。
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("move_tap");
            var baseline = runner.Record(script, "2d_targeted");
            var firstBase = baseline.Ticks.First(t => t.MoveRequested).Tick;

            foreach (var latency in new[] { 1, 4, 9 })
            {
                var noisy = new InputNoiseModel(1UL, latency, 0).Apply(script, out _);
                var recording = runner.Record(noisy, "2d_targeted");
                Assert.Equal(firstBase + latency, recording.Ticks.First(t => t.MoveRequested).Tick);
            }
        }

        // ------------------------------------------------------------------
        // 覆盖存储与 A/B
        // ------------------------------------------------------------------

        private static OverrideSet BufferSet(string name, double bufferMs) =>
            new OverrideSet(name).Add(new OverrideWrite(FeelFieldNames.BufferMs, FeelOp.Set, FeelValue.Of(bufferMs)));

        [Fact]
        public void OverrideStore_RoundTripsThroughJsonAndFile_WithoutTouchingSourceData()
        {
            var store = new OverrideStore();
            store.Upsert(BufferSet("A", 20).Add(new OverrideWrite(FeelFieldNames.TargetHitstopMs, FeelOp.Multiply, FeelValue.Of(2.0), "player")));
            store.Upsert(BufferSet("B", 400));
            var json = store.ToJson();
            var parsed = OverrideStore.Parse(json);
            Assert.Equal(json, parsed.ToJson());
            Assert.Equal(new[] { "A", "B" }, parsed.Sets.Select(s => s.Name).ToArray());
            Assert.Equal("player", parsed.Find("A")!.Writes[1].Unit);

            // 写文件再读：内容一致；源数据的数据集哈希前后不变（覆盖不改源数据）。
            var runner = LabTestSupport.Runner;
            var before = runner.Dataset.Hash;
            var path = Path.Combine(Path.GetTempPath(), "lab_override_" + Guid.NewGuid().ToString("N"), "overrides.json");
            try
            {
                store.Save(path);
                var loaded = OverrideStore.Load(path);
                Assert.Equal(json, loaded.ToJson());
                Assert.False(File.Exists(path + ".tmp"), "原子写入不应遗留临时文件");
                Assert.Empty(OverrideStore.Load(path + ".missing").Sets);
            }
            finally
            {
                var dir = Path.GetDirectoryName(path)!;
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }

            Assert.Equal(before, LabHost.ComputeDatasetHash(runner.Dataset.Sources));
        }

        [Fact]
        public void OverrideStore_Validate_ReportsUnknownFieldIllegalOpAndWrongValueKind()
        {
            var fields = FeelFields.Default;
            Assert.Empty(OverrideStore.Validate(BufferSet("ok", 50), fields));

            var bad = new OverrideSet("bad")
                .Add(new OverrideWrite("no_such_field", FeelOp.Set, FeelValue.Of(1.0)))
                .Add(new OverrideWrite(FeelFieldNames.BufferMs, FeelOp.Remove, FeelValue.Of(1.0)))
                .Add(new OverrideWrite(FeelFieldNames.BufferMs, FeelOp.Set, FeelValue.Of("text")));
            var problems = OverrideStore.Validate(bad, fields);
            Assert.Equal(3, problems.Count);
            Assert.Contains("未登记", problems[0], StringComparison.Ordinal);
            Assert.Contains("不允许操作", problems[1], StringComparison.Ordinal);
            Assert.Contains("值种类", problems[2], StringComparison.Ordinal);

            Assert.Throws<LabFormatException>(() => OverrideStore.Parse("{\"formatVersion\":9,\"sets\":[]}"));
            Assert.Throws<LabFormatException>(() => OverrideStore.Parse(
                "{\"formatVersion\":1,\"sets\":[{\"name\":\"x\",\"writes\":[{\"field\":\"buffer_ms\",\"op\":\"nope\",\"value\":1}]}]}"));
        }

        [Fact]
        public void OverrideExtension_ChangesTheRunThroughTheDebugLayer_AndEmptySetIsTransparent()
        {
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("feel_buffer_lead");
            var plain = runner.Run(script, "2d_action");

            // 不变量：空覆盖组与无覆盖逐字节一致（逻辑组与表现组；实时组随时钟抖动，不比）。
            var empty = runner.Run(script, "2d_action", null, new OverrideExtension(new OverrideSet("empty")));
            Assert.Equal(
                plain.Project(runner.Registry, MetricClass.Logic, MetricClass.Presentation),
                empty.Project(runner.Registry, MetricClass.Logic, MetricClass.Presentation));

            // 复现：把缓冲窗口收窄到 0 ms 后，输入缓冲相关度量变了（缓冲提前量脚本正是测这个窗口）。
            var narrowed = runner.Run(script, "2d_action", null, new OverrideExtension(BufferSet("narrow", 0)));
            var report = AbComparison.Compare(script.Meta.ScriptId, "2d_action", "none", plain, "narrow", narrowed, runner.Registry);
            Assert.False(report.Identical);
            Assert.Contains(report.Differences, d => d.Group == "inputbuf");
        }

        [Fact]
        public void OverrideExtension_NonFeelScript_FailsLoudly_AndUnknownUnitIsRejected()
        {
            var runner = LabTestSupport.Runner;
            Assert.Throws<LabFormatException>(() =>
                runner.Run(LabTestSupport.Script("move_tap"), "2d_targeted", null, new OverrideExtension(BufferSet("x", 50))));

            var unknownUnit = new OverrideSet("u").Add(new OverrideWrite(FeelFieldNames.BufferMs, FeelOp.Set, FeelValue.Of(50.0), "ghost"));
            Assert.Throws<LabFormatException>(() =>
                runner.Run(LabTestSupport.Script("feel_buffer_lead"), "2d_action", null, new OverrideExtension(unknownUnit)));

            // 单位覆盖：作用在玩家上与全局覆盖结果一致（脚本里只有玩家用缓冲窗口）。
            var script = LabTestSupport.Script("feel_buffer_lead");
            var global = runner.Run(script, "2d_action", null, new OverrideExtension(BufferSet("g", 0)));
            var unit = runner.Run(
                script, "2d_action", null,
                new OverrideExtension(new OverrideSet("p").Add(new OverrideWrite(FeelFieldNames.BufferMs, FeelOp.Set, FeelValue.Of(0.0), "player"))));
            Assert.Equal(LogicOf(runner, global), LogicOf(runner, unit));
        }

        [Fact]
        public void AbComparison_DeltaIsBMinusA_IsAntisymmetric_AndIdenticalRunsHaveNoDifference()
        {
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.Script("feel_melee");
            var a = runner.Run(script, "2d_action", null, new OverrideExtension(new OverrideSet("a").Add(
                new OverrideWrite(FeelFieldNames.AttackerHitstopMs, FeelOp.Set, FeelValue.Of(0.0)))));
            var b = runner.Run(script, "2d_action", null, new OverrideExtension(new OverrideSet("b").Add(
                new OverrideWrite(FeelFieldNames.AttackerHitstopMs, FeelOp.Set, FeelValue.Of(100.0)))));

            var ab = AbComparison.Compare(script.Meta.ScriptId, "2d_action", "A", a, "B", b, runner.Registry);
            var ba = AbComparison.Compare(script.Meta.ScriptId, "2d_action", "B", b, "A", a, runner.Registry);
            Assert.False(ab.Identical);
            Assert.Equal(ab.Differences.Count, ba.Differences.Count);

            var hitstop = ab.Differences.First(d => d.FullName == "hitstop.started_ticks_player");
            // 规则：攻击方顿帧毫秒按标定换算成 tick；A = 0 ms，B = 100 ms，差为正，且与指纹里的值一致。
            Assert.True(hitstop.Delta > 0);
            var aValue = ((JsonNumber)((JsonObject)a.Groups["hitstop"])["started_ticks_player"]).Value;
            var bValue = ((JsonNumber)((JsonObject)b.Groups["hitstop"])["started_ticks_player"]).Value;
            Assert.Equal(bValue - aValue, hitstop.Delta!.Value, 9);
            foreach (var d in ab.Differences)
            {
                var mirror = ba.Differences.First(x => x.FullName == d.FullName);
                Assert.Equal(d.A, mirror.B);
                Assert.Equal(d.B, mirror.A);
                if (d.Delta.HasValue)
                {
                    Assert.Equal(-d.Delta.Value, mirror.Delta!.Value, 9);
                }
            }

            var same = AbComparison.Compare(script.Meta.ScriptId, "2d_action", "A", a, "A2", a, runner.Registry);
            Assert.True(same.Identical);
            Assert.Contains("完全一致", same.Format(), StringComparison.Ordinal);
        }

        // ------------------------------------------------------------------
        // 引擎度量组
        // ------------------------------------------------------------------

        private static LabRecording EmptyRecording()
        {
            var script = LabTestSupport.Script("move_tap");
            return new LabRecording(script, LabTestSupport.Runner.Dataset.Catalog.GetScenario("2d_targeted"), 0.02);
        }

        [Fact]
        public void EngineMetricGroup_FoldsSamplesByRule()
        {
            var recording = EmptyRecording();
            var engine = new EngineRecording { Host = "test", Plane = "3d", RigKind = "model", InputNoise = "none", FramesDriven = 4 };
            recording.Engine = engine;
            engine.HitAlignments.Add(new HitAlignSample("player", 10, 0.2, 0.2 + 0.004));
            engine.HitAlignments.Add(new HitAlignSample("player", 30, 0.6, 0.6 - 0.010));
            engine.HitAlignments.Add(new HitAlignSample("player", 50, 1.0, double.NaN));
            var trace = new CameraImpulseTrace(12, 0.05, 120, 0.8);
            trace.Curve.Add(new KeyValuePair<double, double>(0.0, 0.8));
            trace.Curve.Add(new KeyValuePair<double, double>(0.06, 0.4));
            trace.Curve.Add(new KeyValuePair<double, double>(0.2, 0.0));
            engine.CameraImpulses.Add(trace);
            // 各向同性（零方向）与被后续冲量截断的曲线形状不是单条线性衰减：只计数与峰值，不进单调/残余/线性偏差。
            var noise = new CameraImpulseTrace(14, 0.02, 100, 0.3, directional: false);
            noise.Curve.Add(new KeyValuePair<double, double>(0.0, 0.3));
            noise.Curve.Add(new KeyValuePair<double, double>(0.05, 0.9));
            engine.CameraImpulses.Add(noise);
            var truncated = new CameraImpulseTrace(16, 0.02, 100, 0.3) { Truncated = true };
            truncated.Curve.Add(new KeyValuePair<double, double>(0.0, 0.3));
            truncated.Curve.Add(new KeyValuePair<double, double>(0.05, 0.9));
            engine.CameraImpulses.Add(truncated);
            engine.Freezes.Add(new FreezeTrace("player", 11, 3) { RigAdvanceSeconds = 0.0, BystanderAdvanceSeconds = 0.06, ParticleControlAdvanceSeconds = 0.05 });
            engine.Freezes.Add(new FreezeTrace("dummy", 11, 5) { RigAdvanceSeconds = 0.002, ParticleAdvanceSeconds = 0.0, BystanderAdvanceSeconds = 0.1, ParticleControlAdvanceSeconds = 0.03 });
            engine.Controls.Add(new ControlSample(1, new Vec2(0, 1), 30, new Vec2(-0.5, 0.866), 0.002, 0.003));
            engine.Controls.Add(new ControlSample(2, new Vec2(1, 0), 30, new Vec2(0.866, 0.5), 0.004, 0.001));
            engine.Errors.Add("x");
            engine.LayerAudits.Add(new LayerAuditSample("player", "body", "res.a", 8, 64, 64, false));
            engine.LayerAudits.Add(new LayerAuditSample("player", "hand_main", "res.b", 0, 64, 64, true));
            engine.FrameMilliseconds.AddRange(new[] { 1.0, 2.0, 3.0, 4.0, 100.0 });

            var registry = MetricRegistry.CreateWithEngine();
            var built = (JsonObject)registry.Compute(recording)["engine"];

            double Num(string key) => ((JsonNumber)built[key]).Value;
            Assert.Equal("3d", ((JsonString)built["plane"]).Value);
            Assert.Equal(3.0, Num("hit_align_count"));
            Assert.Equal(1.0, Num("hit_align_missing"));
            Assert.Equal(10.0, Num("hit_align_max_abs_ms"), 6);
            Assert.Equal((4.0 + -10.0) / 2.0, Num("hit_align_mean_ms"), 6);
            Assert.Equal(3.0, Num("camera_impulse_count"));
            Assert.Equal(0.8, Num("camera_impulse_peak_max"), 9);
            Assert.Equal(1.0, Num("camera_impulse_monotone"));
            Assert.Equal(0.0, Num("camera_impulse_end_residual"), 9);
            Assert.Equal(0.0, Num("camera_impulse_curve_dev"), 9);
            Assert.Equal(2.0, Num("freeze_count"));
            Assert.Equal(2.0, Num("freeze_rig_advance_max_ms"), 6);
            Assert.Equal(0.0, Num("freeze_particle_advance_max_ms"), 6);
            Assert.Equal(60.0, Num("freeze_bystander_advance_min_ms"), 6);
            Assert.Equal(30.0, Num("freeze_particle_control_min_ms"), 6);
            Assert.Equal(2.0, Num("control_samples"));
            Assert.Equal(0.004, Num("control_max_error_deg"), 9);
            Assert.Equal(0.003, Num("control_max_screen_error_deg"), 9);
            Assert.Equal(1.0, Num("engine_errors"));
            Assert.Equal(2.0, Num("layer_audit_count"));
            Assert.Equal(1.0, Num("layer_audit_mismatch"));
            // 最近秩分位数：5 个样本，p50 取第 3 个，p95 与 max 取最后一个。
            Assert.Equal(3.0, Num("frame_ms_p50"));
            Assert.Equal(100.0, Num("frame_ms_p95"));
            Assert.Equal(100.0, Num("frame_ms_max"));
        }

        [Fact]
        public void EngineMetricGroup_GpuFrameTime_AvailableFoldsByRule_AndUnavailableIsAMarkedSentinelNotAMissingMetric()
        {
            // 复现：取得到 GPU 样本时 gpu_ms_* 按最近秩分位数折算、状态 available；
            // 不变量：没取到（默认记录、或标不可用却带了样本）时度量照样在场——状态 unavailable、数值 -1 哨兵——而不是缺失。
            var registry = MetricRegistry.CreateWithEngine();
            var recording = EmptyRecording();
            var engine = new EngineRecording { Plane = "2d", RigKind = "sprite", GpuAvailable = true };
            engine.GpuFrameMilliseconds.AddRange(new[] { 2.0, 1.0, 4.0, 3.0, 50.0 });
            recording.Engine = engine;
            var built = (JsonObject)registry.Compute(recording)["engine"];
            double Num(JsonObject o, string key) => ((JsonNumber)o[key]).Value;
            Assert.Equal("available", ((JsonString)built["gpu_frame_status"]).Value);
            Assert.Equal(3.0, Num(built, "gpu_ms_p50"));
            Assert.Equal(50.0, Num(built, "gpu_ms_p95"));
            Assert.Equal(50.0, Num(built, "gpu_ms_max"));

            foreach (var unavailable in new[] { new EngineRecording { Plane = "2d", RigKind = "sprite" }, MarkedUnavailableWithSamples() })
            {
                var rec = EmptyRecording();
                rec.Engine = unavailable;
                var folded = (JsonObject)registry.Compute(rec)["engine"];
                Assert.Equal("unavailable", ((JsonString)folded["gpu_frame_status"]).Value);
                foreach (var name in new[] { "gpu_ms_p50", "gpu_ms_p95", "gpu_ms_max" })
                {
                    Assert.True(folded.ContainsKey(name), name + "：不可用时度量也必须在场");
                    Assert.Equal(-1.0, Num(folded, name));
                }
            }
        }

        private static EngineRecording MarkedUnavailableWithSamples()
        {
            var engine = new EngineRecording { Plane = "2d", RigKind = "sprite", GpuAvailable = false, GpuUnavailableReason = "图形设备为 Null" };
            engine.GpuFrameMilliseconds.Add(5.0);
            return engine;
        }

        [Fact]
        public void EngineMetricGroup_CurveThatRisesIsReportedAsNonMonotone_AndEmptyRecordingFoldsToNeutralValues()
        {
            var recording = EmptyRecording();
            var engine = new EngineRecording { Plane = "2d", RigKind = "sprite" };
            recording.Engine = engine;
            var trace = new CameraImpulseTrace(1, 0.1, 100, 1.0);
            trace.Curve.Add(new KeyValuePair<double, double>(0.0, 1.0));
            trace.Curve.Add(new KeyValuePair<double, double>(0.05, 0.5));
            trace.Curve.Add(new KeyValuePair<double, double>(0.1, 0.7));
            engine.CameraImpulses.Add(trace);

            var registry = MetricRegistry.CreateWithEngine();
            var built = (JsonObject)registry.Compute(recording)["engine"];
            Assert.Equal(0.0, ((JsonNumber)built["camera_impulse_monotone"]).Value);
            Assert.Equal(0.7, ((JsonNumber)built["camera_impulse_end_residual"]).Value, 9);
            // 线性偏差：t=0.05 声明值 1.0×(1−0.5)=0.5 与实测一致；t=0.1 已到衰减结束，声明值 0，实测 0.7，偏差 0.7。
            Assert.Equal(0.7, ((JsonNumber)built["camera_impulse_curve_dev"]).Value, 9);

            // 空记录：没有样本时中性值（旁观 rig 推进量最小值为 -1，其余为 0），不抛异常。
            var empty = EmptyRecording();
            empty.Engine = new EngineRecording { Plane = "2d", RigKind = "sprite" };
            var neutral = (JsonObject)registry.Compute(empty)["engine"];
            Assert.Equal(-1.0, ((JsonNumber)neutral["freeze_bystander_advance_min_ms"]).Value);
            Assert.Equal(-1.0, ((JsonNumber)neutral["freeze_particle_control_min_ms"]).Value);
            Assert.Equal(0.0, ((JsonNumber)neutral["engine_errors"]).Value);
            Assert.Equal(0.0, ((JsonNumber)neutral["frame_ms_p95"]).Value);
            Assert.Equal(1.0, ((JsonNumber)neutral["camera_impulse_monotone"]).Value);
        }

        [Fact]
        public void EngineMetricGroup_InputToFirstVisibleResponse_PairsPressesWithPlayerClipTransitions()
        {
            // 复现（M5-S7）：输入类别取自手感记录；按下 tick 起点之后玩家 rig 的第一次剪辑切换算"首次可见响应"，毫秒按模拟时刻差折算；
            // 不变量：没有对应切换的按下计入 missing，不进毫秒清单；非玩家的切换与非攻击类输入不参与。
            var recording = EmptyRecording();
            var step = recording.StepSeconds;
            recording.Feel = new FeelRecording { Assembled = true };
            recording.Feel.InputClasses["input.action.atk"] = "attack";
            recording.Feel.InputClasses["input.action.move"] = "move";
            recording.InjectedInputs.Add(new ScriptEvent(10, "input.action.atk", ScriptEventKind.Press));
            recording.InjectedInputs.Add(new ScriptEvent(12, "input.action.move", ScriptEventKind.Press));
            recording.InjectedInputs.Add(new ScriptEvent(80, "input.action.atk", ScriptEventKind.Press));
            var engine = new EngineRecording { Plane = "2d", RigKind = "sprite" };
            engine.ClipTransitions.Add("dummy:clip.hit");
            engine.ClipTransitionSeconds.Add(10 * step + 0.003);
            engine.ClipTransitions.Add("player:clip.attack");
            engine.ClipTransitionSeconds.Add(10 * step + 0.0125);
            recording.Engine = engine;

            var built = (JsonObject)MetricRegistry.CreateWithEngine().Compute(recording)["engine"];
            double Num(string key) => ((JsonNumber)built[key]).Value;
            Assert.Equal(2.0, Num("input_visible_count"));
            Assert.Equal(1.0, Num("input_visible_missing"));
            Assert.Equal(12.5, Num("input_visible_ms_max"), 6);
            Assert.Equal(2.0, Num("clip_transition_count"));
            Assert.Equal("dummy:clip.hit;player:clip.attack", ((JsonString)built["clip_transitions"]).Value);
        }

        [Fact]
        public void EngineGroup_IsConditional_NotInTheDefaultRegistry_AndHeadlessFingerprintsNeverCarryIt()
        {
            var runner = LabTestSupport.Runner;
            Assert.Null(MetricRegistry.CreateDefault().Find("engine"));
            Assert.NotNull(MetricRegistry.CreateWithEngine().Find("engine"));
            Assert.Equal(MetricRegistry.CreateDefault().Groups.Count + 1, MetricRegistry.CreateWithEngine().Groups.Count);

            foreach (var script in new[] { "move_tap", "feel_melee" })
            {
                var recording = runner.Record(LabTestSupport.Script(script), script == "move_tap" ? "2d_targeted" : "2d_action");
                Assert.Null(recording.Engine);
                var withEngineRegistry = Fingerprint.Build(recording, MetricRegistry.CreateWithEngine(), runner.Dataset.Hash);
                Assert.False(withEngineRegistry.Groups.ContainsKey("engine"));
            }
        }

        [Fact]
        public void EvaluateExpectations_AllowsTheCallerToChooseWhereOtherCellsRun()
        {
            // 复现：期望清单里引用另一个格子时，"怎么跑它"由调用方给出（引擎宿主在引擎宿主上跑对照格子）；缺省与旧入口一致。
            var runner = LabTestSupport.Runner;
            var script = LabTestSupport.AllScripts().First(s => s.Expectations.Count > 0 && runner.ApplicableCells(s).Count > 1);
            var cell = runner.ApplicableCells(script).First().Cell;
            var actual = runner.Run(script, cell);
            var asked = new List<string>();

            var viaDelegate = LabSuite.EvaluateExpectations(
                runner, script, cell, actual, new Dictionary<string, Fingerprint>(StringComparer.Ordinal),
                other =>
                {
                    asked.Add(other);
                    return runner.Run(script, other);
                });
            var viaDefault = LabSuite.EvaluateExpectations(runner, script, cell, actual, new Dictionary<string, Fingerprint>(StringComparer.Ordinal));
            Assert.Equal(viaDefault.Select(r => r.ToString()), viaDelegate.Select(r => r.ToString()));
            Assert.All(asked, other => Assert.NotEqual(cell, other));
        }
    }
}
