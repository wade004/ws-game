using System;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Xunit;

namespace Tests.Foundation.InputMap
{
    /// <summary>
    /// 一个二维轴动作同时绑键盘合成轴与手柄摇杆（消费方反馈 P2 缺口 4）。症状：只有列在最前面的那条轴型绑定被读，另一个设备永远读不到，且没有诊断。
    /// 不变量：多条轴型绑定按列出顺序求值，第一条求值非零的胜出，全零则零；摇杆先按本动作的死区处理再判断非零；单条绑定的结果与此前逐位相同。
    /// 期望值由规则（合成轴归一化、径向死区重标度公式）在用例里算出。
    /// </summary>
    public class AxisDeviceMergeTests
    {
        private const string Move = "input.action.move";
        private const string Keys = "composite2d:key:w|key:s|key:a|key:d";
        private const string Stick = "pad_stick:left";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        private static InputMapHost NewHost(string[] bindings, AxisProcessing? processing = null)
        {
            var host = new InputMapHost(NewBus(), new InputMapOptions { StepSeconds = 1.0 / 60.0 });
            host.DeclareActionSet(new Id("input.set.a"), new[]
            {
                new ActionDefinition(
                    new Id(Move), ActionKind.Axis2D, bindings, "default", null,
                    actionClass: null, bufferMs: null, priority: null, holdThresholdMs: null,
                    repeatPolicy: InputRepeatPolicy.Refresh, faceOnAccept: null, graceConditions: null, skillSlot: null,
                    controlSpace: null, axisProcessing: processing, holdSkillSlot: null, jumpCutRatio: null),
            });
            return host;
        }

        private static Vec2 Sample(InputMapHost host, StubInput input)
        {
            host.Update(input);
            return host.GetActionAxis(Move);
        }

        private static void SetStick(StubInput input, double x, double y)
        {
            input.SetAxis(0, "leftx", x);
            input.SetAxis(0, "lefty", y);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void KeyboardAndStick_BothReadable_RegardlessOfListOrder(bool keysFirst)
        {
            var bindings = keysFirst ? new[] { Keys, Stick } : new[] { Stick, Keys };

            // 只有键盘：合成轴方向（向右 = (1,0)）。
            var host = NewHost(bindings);
            var input = new StubInput();
            input.Press("d");
            Assert.Equal(new Vec2(1, 0), Sample(host, input));

            // 只有摇杆：原样透传。
            host = NewHost(bindings);
            input = new StubInput();
            SetStick(input, 0.3, -0.6);
            var axis = Sample(host, input);
            Assert.Equal(0.3, axis.X);
            Assert.Equal(-0.6, axis.Y);

            // 都没动：零。
            host = NewHost(bindings);
            Assert.Equal(Vec2.Zero, Sample(host, new StubInput()));
        }

        [Fact]
        public void BothActive_FirstListedWins()
        {
            var host = NewHost(new[] { Keys, Stick });
            var input = new StubInput();
            input.Press("w");
            SetStick(input, 1, 0);
            Assert.Equal(new Vec2(0, 1), Sample(host, input));

            host = NewHost(new[] { Stick, Keys });
            input = new StubInput();
            input.Press("w");
            SetStick(input, 1, 0);
            Assert.Equal(new Vec2(1, 0), Sample(host, input));
        }

        [Fact]
        public void StickInsideDeadZone_DoesNotMaskKeyboard()
        {
            const double deadZone = 0.2;
            var host = NewHost(new[] { Stick, Keys }, new AxisProcessing(deadZone: deadZone));
            var input = new StubInput();
            SetStick(input, 0.05, 0.05); // 长度小于死区：处理后为零
            input.Press("a");
            Assert.Equal(new Vec2(-1, 0), Sample(host, input));

            // 摇杆超出死区：摇杆（排在前面）胜出，幅值按径向死区重标度 (len - dz) / (1 - dz)。
            SetStick(input, 0, 0.6);
            var axis = Sample(host, input);
            Assert.Equal(0.0, axis.X, 9);
            Assert.Equal((0.6 - deadZone) / (1 - deadZone), axis.Y, 9);
        }
    }
}
