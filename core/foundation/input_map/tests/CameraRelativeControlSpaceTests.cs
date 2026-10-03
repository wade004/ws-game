using System;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Xunit;

namespace Tests.Foundation.InputMap
{
    /// <summary>
    /// 相机相对控制空间（<c>found.input_action.control_space = camera_relative</c>）：摇杆轴值按相机偏航换算成世界方向；
    /// 缺省 <c>world</c> 逐位不变；声明了 camera_relative 却没有相机朝向查询时在声明期报错，不静默当偏航 0。
    /// 期望值由规则算出：世界方向 = x·右 + y·上，右 = (cos yaw, sin yaw)、上 = (−sin yaw, cos yaw)。
    /// </summary>
    public sealed class CameraRelativeControlSpaceTests
    {
        private const string Move = "input.action.move";

        private sealed class FixedYaw : ICameraOrientation
        {
            public double YawRadians { get; set; }
        }

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        private static ActionDefinition MoveAction(string? controlSpace) =>
            new ActionDefinition(
                new Id(Move), ActionKind.Axis2D, new[] { "pad_stick:left" }, "default", null,
                actionClass: null, bufferMs: null, priority: null, holdThresholdMs: null,
                repeatPolicy: InputRepeatPolicy.Refresh, faceOnAccept: null, graceConditions: null, skillSlot: null,
                controlSpace: controlSpace);

        private static Vec2 Expected(Vec2 stick, double yaw)
        {
            var right = new Vec2(Math.Cos(yaw), Math.Sin(yaw));
            var up = new Vec2(-Math.Sin(yaw), Math.Cos(yaw));
            return new Vec2(stick.X * right.X + stick.Y * up.X, stick.X * right.Y + stick.Y * up.Y);
        }

        private static Vec2 Sample(InputMapHost host, StubInput input, Vec2 stick)
        {
            input.SetAxis(0, "leftx", stick.X);
            input.SetAxis(0, "lefty", stick.Y);
            host.Update(input);
            return host.GetActionAxis(Move);
        }

        [Fact]
        public void CameraRelative_RotatesStickByTheCurrentYaw_ForEveryYawAndStick()
        {
            var orientation = new FixedYaw();
            var host = new InputMapHost(NewBus(), new InputMapOptions { CameraOrientation = orientation });
            host.DeclareActionSet(new Id("input.set.a"), new[] { MoveAction(InputControlSpace.CameraRelative) });
            var input = new StubInput();
            var sticks = new[] { new Vec2(0, 1), new Vec2(1, 0), new Vec2(-1, 0), new Vec2(0, -1), new Vec2(0.5, 0.5), new Vec2(0.3, -0.8) };
            foreach (var yawDeg in new[] { 0.0, 30.0, 90.0, 135.0, 180.0, -60.0 })
            {
                orientation.YawRadians = yawDeg * Math.PI / 180.0;
                foreach (var stick in sticks)
                {
                    var world = Sample(host, input, stick);
                    var expected = Expected(stick, orientation.YawRadians);
                    Assert.Equal(expected.X, world.X, 9);
                    Assert.Equal(expected.Y, world.Y, 9);
                    Assert.Equal(stick.SqrLength, world.SqrLength, 9);
                }
            }
        }

        [Fact]
        public void CameraRelative_SamplesYawAtEveryUpdate_NotAtDeclaration()
        {
            var orientation = new FixedYaw { YawRadians = 0.0 };
            var host = new InputMapHost(NewBus(), new InputMapOptions { CameraOrientation = orientation });
            host.DeclareActionSet(new Id("input.set.a"), new[] { MoveAction(InputControlSpace.CameraRelative) });
            var input = new StubInput();

            var before = Sample(host, input, new Vec2(0, 1));
            Assert.Equal(0.0, before.X, 12);
            Assert.Equal(1.0, before.Y, 12);

            orientation.YawRadians = Math.PI / 2.0;
            var after = Sample(host, input, new Vec2(0, 1));
            Assert.Equal(-1.0, after.X, 9);
            Assert.Equal(0.0, after.Y, 9);
        }

        [Fact]
        public void World_DefaultAndExplicit_AreBitIdenticalToTheRawStick_EvenWithAnOrientationConfigured()
        {
            var orientation = new FixedYaw { YawRadians = 1.234 };
            foreach (var space in new string?[] { null, InputControlSpace.World })
            {
                var host = new InputMapHost(NewBus(), new InputMapOptions { CameraOrientation = orientation });
                host.DeclareActionSet(new Id("input.set.a"), new[] { MoveAction(space) });
                var input = new StubInput();
                var stick = new Vec2(0.37, -0.62);
                var world = Sample(host, input, stick);
                Assert.Equal(stick, world);
            }
        }

        [Fact]
        public void CameraRelative_ZeroYaw_IsTheIdentity()
        {
            var host = new InputMapHost(NewBus(), new InputMapOptions { CameraOrientation = new FixedYaw() });
            host.DeclareActionSet(new Id("input.set.a"), new[] { MoveAction(InputControlSpace.CameraRelative) });
            var stick = new Vec2(0.37, -0.62);
            Assert.Equal(stick, Sample(host, new StubInput(), stick));
        }

        [Fact]
        public void CameraRelative_WithoutAnOrientationQuery_FailsAtDeclaration_NeverSilentlyYawZero()
        {
            var host = new InputMapHost(NewBus());
            var ex = Assert.Throws<InvalidOperationException>(() =>
                host.DeclareActionSet(new Id("input.set.a"), new[] { MoveAction(InputControlSpace.CameraRelative) }));
            Assert.Contains("CameraOrientation", ex.Message);
        }

        [Fact]
        public void ControlSpace_InvalidValueAndNonAxis2D_AreRejected()
        {
            Assert.Throws<ArgumentException>(() => MoveAction("sideways"));
            Assert.Throws<ArgumentException>(() =>
                new ActionDefinition(
                    new Id("input.action.jump"), ActionKind.Button, new[] { "key:space" }, "default", null,
                    null, null, null, null, InputRepeatPolicy.Refresh, null, null, null, InputControlSpace.CameraRelative));
        }

        [Fact]
        public void WithControlSpace_CopiesEveryOtherField()
        {
            var original = new ActionDefinition(
                new Id(Move), ActionKind.Axis2D, new[] { "pad_stick:left" }, "grp", "d",
                ActionClass.Move, 10.0, 3, null, InputRepeatPolicy.Ignore, true, null, "slot_1");
            var switched = original.WithControlSpace(InputControlSpace.CameraRelative);
            Assert.Equal(InputControlSpace.World, original.ControlSpace);
            Assert.Equal(InputControlSpace.CameraRelative, switched.ControlSpace);
            Assert.Equal(original.ActionId, switched.ActionId);
            Assert.Equal(original.RebindGroup, switched.RebindGroup);
            Assert.Equal(original.Class, switched.Class);
            Assert.Equal(original.BufferMs, switched.BufferMs);
            Assert.Equal(original.Priority, switched.Priority);
            Assert.Equal(original.RepeatPolicy, switched.RepeatPolicy);
            Assert.Equal(original.SkillSlot, switched.SkillSlot);
        }

        [Fact]
        public void Schema_ControlSpaceField_RoundTripsThroughTheRegistryAndFromRecord_AndBadValuesAreReported()
        {
            string Rows(string cs) =>
                "[{\"key\":\"input.action.cr\",\"kind\":\"axis2d\",\"default_bindings\":[\"pad_stick:left\"],\"control_space\":\"" + cs + "\"}]";
            string Envelope(string cs) =>
                "{\"table\":\"found.input_action\",\"schema_version\":1,\"rows\":" + Rows(cs) + "}";

            var good = new InMemoryDataSource().Add(InputActionSchema.Table.Name, Envelope("camera_relative"));
            var registry = new DataRegistry(good, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(InputActionSchema.Table);
            Assert.False(registry.LoadAll().IsBlocking);
            var def = ActionDefinition.FromRecord(registry.Get(InputActionSchema.Table.Name, new Id("input.action.cr"))!);
            Assert.Equal(InputControlSpace.CameraRelative, def.ControlSpace);

            var bad = new InMemoryDataSource().Add(InputActionSchema.Table.Name, Envelope("sideways"));
            var badRegistry = new DataRegistry(bad, NewBus(), new DataRegistryOptions());
            badRegistry.RegisterSchema(InputActionSchema.Table);
            Assert.True(badRegistry.LoadAll().IsBlocking);
        }

        [Fact]
        public void StubCamera_ReportsTheConfiguredYawAsRadians()
        {
            var camera = new StubCamera();
            camera.Configure(0.0, 90.0, new ZoomRange(1, 2));
            Assert.Equal(Math.PI / 2.0, ((ICameraOrientation)camera).YawRadians, 12);
        }
    }
}
