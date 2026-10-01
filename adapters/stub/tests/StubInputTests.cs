using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.StubAdapters
{
    public class StubInputTests
    {
        [Fact]
        public void InitialState_NothingDown_NoEvents_MouseAtOrigin_AxesZero()
        {
            var input = new StubInput();
            Assert.Empty(input.PollEvents());
            Assert.False(input.IsKeyDown("Space"));
            Assert.Equal(Vec2.Zero, input.GetMousePosition());
            Assert.Equal(0.0, input.GetGamepadAxis(0, "LeftX"));
        }

        [Fact]
        public void PressThenRelease_TogglesIsKeyDown_AndEmitsKeyDownThenKeyUpInOrder()
        {
            var input = new StubInput();
            input.Press("W");
            Assert.True(input.IsKeyDown("W"));
            input.Release("W");
            Assert.False(input.IsKeyDown("W"));

            var events = input.PollEvents();
            Assert.Equal(new[] { InputEventKind.KeyDown, InputEventKind.KeyUp }, events.Select(e => e.Kind).ToArray());
            Assert.All(events, e => Assert.Equal("W", e.Key));
        }

        [Fact]
        public void PollEvents_DrainsQueue_SecondCallIsEmpty_FirstResultIsDetachedFromStub()
        {
            var input = new StubInput();
            input.Press("A");
            input.Press("A");   // 同一个键连按两次必须保留两条事件（IInput 契约：不得丢事件）。

            var first = input.PollEvents();
            Assert.Equal(2, first.Count);
            Assert.Empty(input.PollEvents());

            input.Press("B");
            Assert.Equal(2, first.Count);               // 旧结果不被后续事件污染
            Assert.Single(input.PollEvents());
        }

        [Fact]
        public void Keys_AreTrackedIndependently_AndKeyNamesAreCaseSensitive()
        {
            var input = new StubInput();
            input.Press("A");
            input.Press("B");
            input.Release("A");

            Assert.False(input.IsKeyDown("A"));
            Assert.True(input.IsKeyDown("B"));
            Assert.False(input.IsKeyDown("b"));
        }

        [Fact]
        public void Release_OfKeyNeverPressed_StillEmitsKeyUp_WithoutThrowing()
        {
            var input = new StubInput();
            input.Release("Z");
            var e = Assert.Single(input.PollEvents());
            Assert.Equal(InputEventKind.KeyUp, e.Kind);
            Assert.Equal("Z", e.Key);
        }

        [Fact]
        public void MoveMouse_UpdatesPosition_AndEmitsMouseMovedWithThatPosition()
        {
            var input = new StubInput();
            var target = new Vec2(120.5, -33);
            input.MoveMouse(target);

            Assert.Equal(target, input.GetMousePosition());
            var e = Assert.Single(input.PollEvents());
            Assert.Equal(InputEventKind.MouseMoved, e.Kind);
            Assert.Equal(target, e.Position);
        }

        [Fact]
        public void MouseButtons_EmitDedicatedEventKinds_WithButtonNameAsKey_AndDoNotAffectIsKeyDown()
        {
            var input = new StubInput();
            input.PressMouseButton("Left");
            input.ReleaseMouseButton("Left");

            var events = input.PollEvents();
            Assert.Equal(new[] { InputEventKind.MouseButtonDown, InputEventKind.MouseButtonUp }, events.Select(e => e.Kind).ToArray());
            Assert.All(events, e => Assert.Equal("Left", e.Key));
            Assert.False(input.IsKeyDown("Left"));
        }

        [Fact]
        public void GamepadButtons_CarryGamepadIndex_AndKeyboardEventsUseSentinelIndex()
        {
            var input = new StubInput();
            input.PressGamepadButton(2, "South");
            input.ReleaseGamepadButton(2, "South");
            input.Press("K");

            var events = input.PollEvents();
            Assert.Equal(InputEventKind.GamepadButtonDown, events[0].Kind);
            Assert.Equal(InputEventKind.GamepadButtonUp, events[1].Kind);
            Assert.Equal(2, events[0].GamepadIndex);
            Assert.Equal(2, events[1].GamepadIndex);
            Assert.Equal("South", events[0].Key);
            // InputEvent 构造的缺省手柄序号即“非手柄事件”哨兵。
            Assert.Equal(new InputEvent(InputEventKind.KeyDown).GamepadIndex, events[2].GamepadIndex);
        }

        [Fact]
        public void GamepadAxis_IsKeyedByIndexAndName_LatestValueWins_UnsetIsZero()
        {
            var input = new StubInput();
            input.SetAxis(0, "LeftX", 0.5);
            input.SetAxis(1, "LeftX", -1.0);
            input.SetAxis(0, "LeftX", 0.25);

            Assert.Equal(0.25, input.GetGamepadAxis(0, "LeftX"));
            Assert.Equal(-1.0, input.GetGamepadAxis(1, "LeftX"));
            Assert.Equal(0.0, input.GetGamepadAxis(0, "LeftY"));
            Assert.Equal(0.0, input.GetGamepadAxis(2, "LeftX"));
            // 设轴不产生离散事件。
            Assert.Empty(input.PollEvents());
        }

        [Fact]
        public void TextInput_EchoesPlaceholderUnlessOverridden()
        {
            var input = new StubInput();
            input.BeginTextInput("slot name");
            Assert.Equal("slot name", input.EndTextInput());

            input.SetPendingTextInput("typed by test");
            Assert.Equal("typed by test", input.EndTextInput());

            input.BeginTextInput("again");
            Assert.Equal("again", input.EndTextInput());
        }
    }
}
