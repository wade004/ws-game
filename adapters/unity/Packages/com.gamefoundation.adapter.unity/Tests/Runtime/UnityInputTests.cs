#nullable enable
using Adapter.Unity.EngineAdapter;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class UnityInputTests
    {
        private UnityInput _input = null!;

        [SetUp]
        public void SetUp()
        {
            _input = new UnityInput();
        }

        [TearDown]
        public void TearDown()
        {
            _input.Dispose();
        }

        [Test]
        public void IsKeyDown_UnknownOrUnpressedKey_ReturnsFalse()
        {
            Assert.IsFalse(_input.IsKeyDown("A"));
            Assert.IsFalse(_input.IsKeyDown("NotARealKeyName"));
        }

        [Test]
        public void GetMousePosition_ReturnsFiniteVector()
        {
            var pos = _input.GetMousePosition();

            Assert.IsFalse(double.IsNaN(pos.X));
            Assert.IsFalse(double.IsNaN(pos.Y));
        }

        [Test]
        public void GetGamepadAxis_NoGamepad_ReturnsZero()
        {
            Assert.AreEqual(0.0, _input.GetGamepadAxis(0, "LeftStickX"));
        }

        [Test]
        public void SimulateGamepadButtonForTest_EnqueuesEvent_PollEventsReturnsIt()
        {
            _input.SimulateGamepadButtonForTest(0, "South", down: true);

            var events = _input.PollEvents();

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(InputEventKind.GamepadButtonDown, events[0].Kind);
            Assert.AreEqual("South", events[0].Key);
            Assert.AreEqual(0, events[0].GamepadIndex);
        }

        [Test]
        public void SimulateKeyForTest_EnqueuesEvent_PollEventsReturnsIt()
        {
            _input.SimulateKeyForTest("t", down: true);

            var events = _input.PollEvents();

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(InputEventKind.KeyDown, events[0].Kind);
            Assert.AreEqual("t", events[0].Key);
        }

        [Test]
        public void SimulateKeyForTest_KeyUp_EnqueuesKeyUpEvent()
        {
            _input.SimulateKeyForTest("t", down: false);

            var events = _input.PollEvents();

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(InputEventKind.KeyUp, events[0].Kind);
        }

        [Test]
        public void PollEvents_DrainsQueue_SecondCallReturnsEmpty()
        {
            _input.SimulateGamepadButtonForTest(0, "South", down: true);
            _input.PollEvents();

            var second = _input.PollEvents();

            Assert.AreEqual(0, second.Count);
        }

        [Test]
        public void BeginAndEndTextInput_WithoutTyping_ReturnsEmptyString()
        {
            _input.BeginTextInput("placeholder");
            var result = _input.EndTextInput();

            Assert.AreEqual(string.Empty, result);
        }
    }
}
