using System;
using System.Collections.Generic;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Xunit;

namespace Tests.Carriers.Unit
{
    /// <summary>
    /// 玩家移动意图解析器（ADR-0153）：没有声明冲刺动作时生成的移动请求与此前宿主手写的 <c>MoveRequest.InDirection(unit, axis)</c> 逐位一致
    /// （默认路径零差异）；声明后按住且有移动输入才升冲刺，原地按住不产生请求，松键回走路。
    /// </summary>
    public class PlayerMoveIntentResolverTests
    {
        private static readonly Id Unit = new Id("unit.hero");
        private static readonly Id Sprint = PlayerMoveIntentResolver.DefaultSprintAction;

        private static readonly Vec2[] Axes =
        {
            new Vec2(1, 0), new Vec2(-1, 0), new Vec2(0, 1), new Vec2(0, -1), new Vec2(0.7, 0.7), new Vec2(-0.3, 0.9),
            new Vec2(0.01, 0), new Vec2(0.02, 0.0), new Vec2(0, 0), new Vec2(0.009, 0.0),
        };

        private static InputBufferHost NewBuffer(bool declareSprint)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var buffer = new InputBufferHost(bus);
            var defs = new List<ActionDefinition>();
            if (declareSprint)
            {
                defs.Add(new ActionDefinition(Sprint, ActionKind.Button, new[] { "key:l" }, "default", null, ActionClass.Move, null, null, null,
                    InputRepeatPolicy.Refresh, null, null));
            }
            buffer.DeclareActions(defs);
            return buffer;
        }

        private static bool Same(MoveRequest a, MoveRequest b) =>
            a.UnitId.Equals(b.UnitId) && Nullable.Equals(a.Target, b.Target) && Nullable.Equals(a.Direction, b.Direction)
            && a.Mode == b.Mode && Nullable.Equals(a.TargetUnitId, b.TargetUnitId) && a.StopRange == b.StopRange;

        [Fact]
        public void NoSprintDeclared_RequestEqualsLegacyHandWrittenRequest_ForEveryAxis()
        {
            var resolvers = new[]
            {
                new PlayerMoveIntentResolver((InputBufferHost?)null),
                new PlayerMoveIntentResolver(NewBuffer(declareSprint: false)),
                new PlayerMoveIntentResolver(NewBuffer(declareSprint: false), null),
            };

            foreach (var resolver in resolvers)
            {
                Assert.Null(resolver.SprintAction);
                foreach (var axis in Axes)
                {
                    // 此前各宿主的写法：平方长 > 0.0001 才下请求，请求 = InDirection(玩家, 轴)（缺省走路）。
                    var legacyRequested = axis.SqrLength > 0.0001;
                    var requested = resolver.TryResolve(Unit, axis, out var request);

                    Assert.Equal(legacyRequested, requested);
                    if (requested) Assert.True(Same(MoveRequest.InDirection(Unit, axis), request), axis.ToString());
                }
            }
        }

        [Fact]
        public void NoSprintDeclared_HoldingTheKeyChangesNothing_EvenWhenTheBufferSawThePress()
        {
            var buffer = NewBuffer(declareSprint: false);
            buffer.Press(Unit, Sprint); // 没声明的动作被忽略
            var resolver = new PlayerMoveIntentResolver(buffer);

            Assert.True(resolver.TryResolve(Unit, new Vec2(1, 0), out var request));
            Assert.Equal(MoveMode.Walk, request.Mode);
        }

        [Fact]
        public void Declared_HeldWithMoveInput_RequestsSprint_ReleaseReturnsToWalk_AndBaseRunIsKept()
        {
            var buffer = NewBuffer(declareSprint: true);
            var resolver = new PlayerMoveIntentResolver(buffer);
            Assert.Equal(Sprint, resolver.SprintAction);
            var axis = new Vec2(0.6, 0.8);

            Assert.True(resolver.TryResolve(Unit, axis, out var walking));
            Assert.Equal(MoveMode.Walk, walking.Mode);

            buffer.Press(Unit, Sprint);
            Assert.True(resolver.TryResolve(Unit, axis, out var sprinting));
            Assert.Equal(MoveMode.Sprint, sprinting.Mode);
            Assert.Equal(axis, sprinting.Direction);
            Assert.True(resolver.TryResolve(Unit, axis, out var fromRun, MoveMode.Run));
            Assert.Equal(MoveMode.Sprint, fromRun.Mode);

            buffer.Release(Unit, Sprint);
            Assert.True(resolver.TryResolve(Unit, axis, out var released));
            Assert.Equal(MoveMode.Walk, released.Mode);
            Assert.True(resolver.TryResolve(Unit, axis, out var run, MoveMode.Run));
            Assert.Equal(MoveMode.Run, run.Mode);
        }

        [Fact]
        public void Declared_HeldWithoutMoveInput_ProducesNoRequest_AndOtherUnitsAreUnaffected()
        {
            var buffer = NewBuffer(declareSprint: true);
            var resolver = new PlayerMoveIntentResolver(buffer);
            var other = new Id("unit.other");

            buffer.Press(Unit, Sprint);

            foreach (var idle in new[] { new Vec2(0, 0), new Vec2(0.009, 0) })
            {
                Assert.False(resolver.TryResolve(Unit, idle, out _));
            }
            Assert.True(resolver.TryResolve(other, new Vec2(1, 0), out var otherRequest));
            Assert.Equal(MoveMode.Walk, otherRequest.Mode);
            // 非自主移动的基础模式不被冲刺覆盖。
            Assert.Equal(MoveMode.Forced, resolver.ModeFor(Unit, MoveMode.Forced));
        }

        [Fact]
        public void ExplicitSprintAction_IsUsedAsGiven()
        {
            var buffer = NewBuffer(declareSprint: false);
            var custom = new Id("input.action.dash_hold");
            buffer.DeclareActions(new[]
            {
                new ActionDefinition(custom, ActionKind.Button, new[] { "key:m" }, "default", null, ActionClass.Move, null, null, null,
                    InputRepeatPolicy.Refresh, null, null),
            });
            var resolver = new PlayerMoveIntentResolver(buffer, custom);

            buffer.Press(Unit, custom);

            Assert.True(resolver.TryResolve(Unit, new Vec2(1, 0), out var request));
            Assert.Equal(MoveMode.Sprint, request.Mode);
        }
    }
}
