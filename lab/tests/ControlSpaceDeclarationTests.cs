using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 脚本携带控制空间声明与偏航流（ADR-0161，补 ADR-0159 的跨宿主回放缺口）：声明 <c>camera_relative</c> 的脚本在任何宿主（含无头）里按脚本里的 <c>camera_yaw</c> 标记流复现同一串逻辑；
    /// 没有声明的旧脚本序列化与重放逐位不变。期望值由规则（偏航旋转公式、另一条独立路径的运行）算出，不写死裸数。
    /// </summary>
    public sealed class ControlSpaceDeclarationTests
    {
        private const string Cell = "2d_action";
        private const string Move = "input.action.move";
        private const double Frame = 1.0 / 60.0;

        /// <summary>旧机制（本 ADR 之前宿主自己提供朝向查询的做法）：扩展给出可变的偏航并要求 camera_relative，不依赖脚本声明——用作独立参照。</summary>
        private sealed class LegacyYawHost : LabHostExtension
        {
            private readonly Orientation _orientation = new Orientation();

            public void SetYaw(double degrees) => _orientation.YawRadians = degrees * Math.PI / 180.0;

            public override ICameraOrientation? CameraOrientation => _orientation;

            public override string? ControlSpaceOverride => ControlSpace.CameraRelative;

            private sealed class Orientation : ICameraOrientation
            {
                public double YawRadians { get; set; }
            }
        }

        private static void Frames(LabSession session, int n)
        {
            for (var i = 0; i < n; i++)
            {
                session.Advance(Frame);
            }
        }

        private static void Stick(LabSession session, double x, double y) =>
            session.Inject(new ScriptEvent(0, Move, ScriptEventKind.Axis, new Vec2(x, y)));

        private static void Turn(LabSession session, LegacyYawHost? host, double yawDegrees)
        {
            host?.SetYaw(yawDegrees);
            session.Inject(new ScriptEvent(0, ControlSpace.YawMarker, ScriptEventKind.Marker, new Vec2(yawDegrees, 0.0)));
        }

        /// <summary>一局"环绕镜头"会话：偏航在若干固定步边界上变化，每次转完推摇杆走一段。<paramref name="host"/> 为空时不借助任何宿主扩展（纯脚本）。</summary>
        private static LabSession PlayOrbit(InputScript script, LegacyYawHost? host, double[] yaws)
        {
            var session = LabTestSupport.Runner.StartLive(script, Cell, null, host);
            Frames(session, 3);
            foreach (var yaw in yaws)
            {
                Turn(session, host, yaw);
                Stick(session, 0.6, 0.8);
                Frames(session, 12);
                Stick(session, 0.0, 1.0);
                Frames(session, 18);
                Stick(session, -1.0, 0.0);
                Frames(session, 9);
                Stick(session, 0.0, 0.0);
                Frames(session, 3);
            }

            return session;
        }

        private static string Logic(InputScript script, LabRecording recording) =>
            LabTestSupport.Runner.FingerprintOf(script, Cell, recording).Project(LabTestSupport.Runner.Registry, MetricClass.Logic);

        private static string ReplayLogic(InputScript script) => Logic(script, LabTestSupport.Runner.Record(script, Cell));

        private static InputScript Declared(InputScript recorded, string space = ControlSpace.CameraRelative)
        {
            var copy = InputScript.Parse(recorded.ToJson());
            copy.Meta.ControlSpace = space;
            return InputScript.Parse(copy.ToJson());
        }

        [Fact]
        public void ScriptYawOrientation_ReportsTheCommittedYawAsRadians_StartingAtZero()
        {
            var orientation = new ControlSpace.ScriptYawOrientation();
            Assert.Equal(0.0, orientation.YawRadians, 0);
            orientation.Commit(90.0);
            Assert.Equal(Math.PI / 2.0, orientation.YawRadians, 12);
            orientation.Commit(-135.0);
            Assert.Equal(-135.0 * Math.PI / 180.0, orientation.YawRadians, 12);
        }

        [Fact]
        public void Declaration_RoundTripsThroughJson_AndOnlyDeclaredScriptsUseFormatVersion6()
        {
            var script = LabLive.CreateScript("decl_roundtrip");
            Assert.Equal(string.Empty, script.Meta.ControlSpace);
            Assert.DoesNotContain("controlSpace", script.ToJson());
            Assert.True(script.EffectiveFormatVersion < InputScript.ControlSpaceFormatVersion);

            script.Meta.ControlSpace = ControlSpace.CameraRelative;
            Assert.Equal(InputScript.ControlSpaceFormatVersion, script.EffectiveFormatVersion);
            var text = script.ToJson();
            Assert.Contains("\"formatVersion\": " + InputScript.ControlSpaceFormatVersion, text);
            Assert.Contains("\"controlSpace\": \"camera_relative\"", text);
            var back = InputScript.Parse(text);
            Assert.Equal(ControlSpace.CameraRelative, back.Meta.ControlSpace);
            Assert.Equal(text, back.ToJson());
        }

        [Fact]
        public void Declaration_InvalidValue_IsAFormatError_AndNewerFormatIsRejectedByOlderReaders()
        {
            var script = LabLive.CreateScript("decl_bad");
            script.Meta.ControlSpace = "sideways";
            Assert.Throws<LabFormatException>(() => InputScript.Parse(script.ToJson()));

            var tooNew = "{\"formatVersion\":" + (InputScript.ControlSpaceFormatVersion + 1) + ",\"meta\":{},\"events\":[]}";
            Assert.Throws<LabFormatException>(() => InputScript.Parse(tooNew));
        }

        [Fact]
        public void OldScripts_WithoutTheField_SerializeAndReplayExactlyAsBefore_EvenWhenTheyCarryYawMarkers()
        {
            // 向后兼容夹具：版本 5 的试玩脚本（含 camera_yaw 呈现标记、没有 controlSpace）——此前标记逻辑不读，按 world 解释；现在仍然如此。
            var old = InputScript.Parse(
                "{\"formatVersion\":5,\"meta\":{\"scriptId\":\"old_orbit_session\",\"scriptVersion\":1,\"description\":\"\",\"datasetRoot\":\"data/_lab\","
                + "\"presetId\":\"\",\"calibrationVersion\":\"none\",\"poseSet\":\"none\",\"tickRate\":60,\"frameRateCap\":60,\"durationTicks\":90,"
                + "\"playerStart\":{\"x\":0,\"y\":0},\"dummyGroups\":[]},\"events\":["
                + "{\"tick\":2,\"action\":\"camera_yaw\",\"kind\":\"marker\",\"value\":{\"x\":75,\"y\":0}},"
                + "{\"tick\":3,\"action\":\"input.action.move\",\"kind\":\"axis\",\"value\":{\"x\":0,\"y\":1}},"
                + "{\"tick\":60,\"action\":\"input.action.move\",\"kind\":\"axis\",\"value\":{\"x\":0,\"y\":0}}]}");
            Assert.Equal(InputScript.InteractiveFormatVersion, old.EffectiveFormatVersion);
            Assert.Equal(string.Empty, old.Meta.ControlSpace);
            Assert.DoesNotContain("controlSpace", old.ToJson());

            // 同一个脚本去掉标记：逻辑逐字节相同（没声明时标记仍然只是呈现标记）。
            var stripped = new InputScript(old.Meta, old.Events.Where(e => e.Kind != ScriptEventKind.Marker).ToList());
            Assert.Equal(ReplayLogic(stripped), ReplayLogic(old));

            // 显式 world 声明与不声明逻辑一致；camera_relative 声明才改变方向（75 度偏航，向上的摇杆朝世界 (-sin75, cos75) 走）。
            Assert.Equal(ReplayLogic(old), ReplayLogic(Declared(old, ControlSpace.World)));
            var relative = LabTestSupport.Runner.Record(Declared(old), Cell);
            var plain = LabTestSupport.Runner.Record(old, Cell);
            var last = relative.Ticks.Count - 1;
            var yaw = 75.0 * Math.PI / 180.0;
            var displacement = relative.Ticks[last].Position - relative.Ticks[0].Position;
            var length = Math.Sqrt(displacement.X * displacement.X + displacement.Y * displacement.Y);
            Assert.True(length > 1e-3, "前置条件：玩家确实走动了");
            Assert.Equal(-Math.Sin(yaw), displacement.X / length, 6);
            Assert.Equal(Math.Cos(yaw), displacement.Y / length, 6);
            Assert.NotEqual(plain.Ticks[last].Position, relative.Ticks[last].Position);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(30.0)]
        [InlineData(90.0)]
        [InlineData(180.0)]
        [InlineData(-135.0)]
        [InlineData(405.0)]
        public void ConstantYaw_DeclaredScript_MovesAlongTheYawRotatedDirection_InTheHeadlessHost(double yawDegrees)
        {
            // 规则算出的期望：偏航 θ 下摇杆向上 = 世界方向 (-sin θ, cos θ)；脚本只有偏航标记与摇杆事件，无头宿主按声明复现。
            var script = LabLive.CreateScript("decl_constant_yaw_" + yawDegrees);
            script.Meta.ControlSpace = ControlSpace.CameraRelative;
            var session = LabTestSupport.Runner.StartLive(script, Cell, null, null);
            Frames(session, 2);
            Turn(session, null, yawDegrees);
            Stick(session, 0.0, 1.0);
            Frames(session, 30);
            var recording = session.Finish();

            var yaw = yawDegrees * Math.PI / 180.0;
            var displacement = recording.Ticks[recording.Ticks.Count - 1].Position - recording.Ticks[0].Position;
            var length = Math.Sqrt(displacement.X * displacement.X + displacement.Y * displacement.Y);
            Assert.True(length > 1e-3, "前置条件：玩家确实走动了");
            Assert.Equal(-Math.Sin(yaw), displacement.X / length, 6);
            Assert.Equal(Math.Cos(yaw), displacement.Y / length, 6);

            // 同一份脚本（序列化往返）重放，逻辑组逐字节相同。
            var replayed = InputScript.Parse(session.Script.ToJson());
            Assert.Equal(Logic(session.Script, recording), ReplayLogic(replayed));
        }

        [Fact]
        public void OrbitSession_RecordedByTheLegacyHostMechanism_ReplaysHeadlessFromTheDeclaredScript_WithIdenticalLogicFingerprint()
        {
            // 独立参照：旧机制（宿主扩展提供可变偏航，脚本没有声明）录下一局多次转镜头的会话；把同一份录下的脚本加上控制空间声明后，
            // 在没有任何扩展的无头宿主里重放——逻辑指纹必须与旧机制那一次逐字节相同。声明前的无头重放（world 解释）则不同（证明不是空转）。
            var yaws = new[] { 0.0, 90.0, 213.5, -47.0, 405.0, 30.0 };
            var host = new LegacyYawHost();
            var legacyScript = LabLive.CreateScript("decl_legacy_reference");
            var legacySession = PlayOrbit(legacyScript, host, yaws);
            var legacyRecording = legacySession.Finish();
            var legacyLogic = Logic(legacySession.Script, legacyRecording);

            var recordedText = legacySession.Script.ToJson();
            var undeclaredReplay = ReplayLogic(InputScript.Parse(recordedText));
            Assert.NotEqual(legacyLogic, undeclaredReplay);

            var declaredReplay = ReplayLogic(Declared(InputScript.Parse(recordedText)));
            Assert.Equal(legacyLogic, declaredReplay);

            // 新机制本身：脚本自带声明的实时会话（没有任何宿主扩展）录下来，再无头重放，也逐字节一致。
            var selfScript = LabLive.CreateScript("decl_self_recorded");
            selfScript.Meta.ControlSpace = ControlSpace.CameraRelative;
            var selfSession = PlayOrbit(selfScript, null, yaws);
            var selfRecording = selfSession.Finish();
            var selfLogic = Logic(selfSession.Script, selfRecording);
            Assert.Equal(selfLogic, ReplayLogic(InputScript.Parse(selfSession.Script.ToJson())));
            // 新旧两条路径对同一串偏航与摇杆给出同一个逻辑指纹。
            Assert.Equal(legacyLogic, selfLogic);
        }

        [Fact]
        public void DeclaredScript_ExtensionOverrideStillWins_AndZeroYawIsTheIdentity()
        {
            var yaws = new[] { 0.0, 120.0 };
            var script = LabLive.CreateScript("decl_override");
            script.Meta.ControlSpace = ControlSpace.CameraRelative;
            var worldHost = new WorldOverrideHost();
            var session = PlayOrbit(script, null, yaws);
            var recorded = InputScript.Parse(session.Script.ToJson());
            session.Finish();

            // 扩展显式要求 world：按 world 运行，声明不起作用（与去掉声明的同一脚本逻辑一致）。
            var withOverride = Logic(recorded, LabTestSupport.Runner.Record(recorded, Cell, null, worldHost));
            var undeclared = InputScript.Parse(recorded.ToJson());
            undeclared.Meta.ControlSpace = string.Empty;
            Assert.Equal(ReplayLogic(undeclared), withOverride);

            // 偏航恒 0 的声明脚本：相机相对换算是恒等变换，逻辑与 world 一致。
            var zero = LabLive.CreateScript("decl_zero_yaw");
            zero.Meta.ControlSpace = ControlSpace.CameraRelative;
            var zeroSession = PlayOrbit(zero, null, new[] { 0.0 });
            var zeroScript = InputScript.Parse(zeroSession.Script.ToJson());
            zeroSession.Finish();
            var zeroWorld = InputScript.Parse(zeroScript.ToJson());
            zeroWorld.Meta.ControlSpace = string.Empty;
            Assert.Equal(ReplayLogic(zeroWorld), ReplayLogic(zeroScript));
        }

        private sealed class WorldOverrideHost : LabHostExtension
        {
            public override string? ControlSpaceOverride => ControlSpace.World;
        }
    }
}
