using System;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Xunit;

namespace Tests.Foundation.InputMap
{
    /// <summary>
    /// 摇杆处理（死区 / 响应曲线 / 平滑，ADR-0143）：归设备与玩家设置，消费方是输入映射宿主。每个机制一条复现、一条不变量；
    /// 期望值由规则公式（<c>(len − dz) / (1 − dz)</c>、平方、<c>步长 = 一个 tick / 平滑时间</c>）在用例里算出。
    /// </summary>
    public class AxisProcessingTests
    {
        private const string Move = "input.action.move";
        private const double Step = 1.0 / 60.0;

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        private static ActionDefinition StickAction(AxisProcessing? processing, string binding = "pad_stick:left") =>
            new ActionDefinition(
                new Id(Move), ActionKind.Axis2D, new[] { binding }, "default", null,
                actionClass: null, bufferMs: null, priority: null, holdThresholdMs: null,
                repeatPolicy: InputRepeatPolicy.Refresh, faceOnAccept: null, graceConditions: null, skillSlot: null,
                controlSpace: null, axisProcessing: processing, holdSkillSlot: null, jumpCutRatio: null);

        private static InputMapHost NewHost(AxisProcessing? processing, string binding = "pad_stick:left", Func<string, PiecewiseCurve?>? curves = null)
        {
            var host = new InputMapHost(NewBus(), new InputMapOptions { StepSeconds = Step, CurveResolver = curves });
            host.DeclareActionSet(new Id("input.set.a"), new[] { StickAction(processing, binding) });
            return host;
        }

        private static Vec2 Sample(InputMapHost host, StubInput input, Vec2 stick)
        {
            input.SetAxis(0, "leftx", stick.X);
            input.SetAxis(0, "lefty", stick.Y);
            host.Update(input);
            return host.GetActionAxis(Move);
        }

        // ---------------------------------------------------------------- 恒等（未声明）

        [Fact]
        public void Undeclared_IsBitIdenticalToRawPassthrough_AndIdentityProcessingIsToo()
        {
            var plain = NewHost(null);
            var identity = NewHost(new AxisProcessing());
            var input = new StubInput();
            var sticks = new[] { new Vec2(0, 0), new Vec2(0.05, 0.02), new Vec2(0.3, -0.8), new Vec2(-1, 0), new Vec2(0.7071, 0.7071), new Vec2(0.123456789, -0.987654321) };
            foreach (var stick in sticks)
            {
                var a = Sample(plain, input, stick);
                var b = Sample(identity, input, stick);
                Assert.Equal(BitConverter.DoubleToInt64Bits(a.X), BitConverter.DoubleToInt64Bits(b.X));
                Assert.Equal(BitConverter.DoubleToInt64Bits(a.Y), BitConverter.DoubleToInt64Bits(b.Y));
                Assert.Equal(stick.X, a.X);
                Assert.Equal(stick.Y, a.Y);
            }

            Assert.Null(plain.GetAxisProcessing(Move));
        }

        // ---------------------------------------------------------------- 死区

        [Fact]
        public void DeadZone_ZeroesInsideAndRescalesOutside_KeepingDirection()
        {
            const double dz = 0.2;
            var host = NewHost(new AxisProcessing(dz));
            var input = new StubInput();

            // 复现：长度不超过死区 → 零。
            var inside = Sample(host, input, new Vec2(dz * 0.9, 0));
            Assert.Equal(0.0, inside.X);
            Assert.Equal(0.0, inside.Y);

            // 死区之外：长度 (len − dz) / (1 − dz)，方向不变。
            var stick = new Vec2(0.6, 0.3);
            var len = stick.Length;
            var expectedLen = (len - dz) / (1.0 - dz);
            var outside = Sample(host, input, stick);
            Assert.Equal(expectedLen, outside.Length, 9);
            Assert.Equal(stick.Y / stick.X, outside.Y / outside.X, 9);
        }

        [Fact]
        public void DeadZone_Invariants_FullDeflectionStaysFull_AndOutputNeverExceedsInput()
        {
            var host = NewHost(new AxisProcessing(0.3));
            var input = new StubInput();
            var full = Sample(host, input, new Vec2(0, 1));
            Assert.Equal(1.0, full.Length, 9);

            // 单调：幅值越大输出越大；输出不超过输入。
            var previous = -1.0;
            for (var i = 0; i <= 10; i++)
            {
                var len = i / 10.0;
                var o = Sample(host, input, new Vec2(len, 0)).Length;
                Assert.True(o >= previous);
                Assert.True(o <= len + 1e-12);
                previous = o;
            }
        }

        // ---------------------------------------------------------------- 曲线

        [Fact]
        public void ExpoCurve_MapsMagnitudeToItsSquare_AndFullAndZeroAreFixedPoints()
        {
            var host = NewHost(new AxisProcessing(0.0, AxisProcessing.Expo));
            var input = new StubInput();
            var half = Sample(host, input, new Vec2(0.5, 0));
            Assert.Equal(0.5 * 0.5, half.Length, 9);
            Assert.Equal(1.0, Sample(host, input, new Vec2(1, 0)).Length, 9);
            Assert.Equal(0.0, Sample(host, input, new Vec2(0, 0)).Length, 9);
        }

        [Fact]
        public void CustomCurve_UsesTheResolvedPiecewiseCurve_AndUnknownCurveIsRejected()
        {
            var curve = new PiecewiseCurve(new[] { new CurvePoint(0.0, 0.0), new CurvePoint(0.5, 0.25), new CurvePoint(1.0, 1.0) });
            var host = NewHost(new AxisProcessing(0.0, "custom:my_curve"), curves: id => id == "my_curve" ? curve : null);
            var input = new StubInput();
            var o = Sample(host, input, new Vec2(0.5, 0));
            Assert.Equal(curve.Evaluate(0.5), o.Length, 9);

            Assert.Throws<InvalidOperationException>(() =>
                NewHost(new AxisProcessing(0.0, "custom:missing"), curves: id => null));
        }

        // ---------------------------------------------------------------- 平滑

        [Fact]
        public void Smoothing_RisesImmediately_FallsLinearlyAtFullScaleOverSmoothingMs()
        {
            const double smoothingMs = 200;
            var host = NewHost(new AxisProcessing(0.0, null, smoothingMs));
            var input = new StubInput();

            // 上升沿即时：按下当 tick 就是满幅（一次按下至少产生一个 tick 的满幅意图）。
            Assert.Equal(1.0, Sample(host, input, new Vec2(1, 0)).Length, 9);

            // 下降沿：每 tick 最多回落 step = 一个 tick / 平滑时间。
            var step = Step / (smoothingMs / 1000.0);
            var expected = 1.0;
            for (var i = 0; i < 6; i++)
            {
                expected = Math.Max(0.0, expected - step);
                var o = Sample(host, input, new Vec2(0, 0));
                Assert.Equal(expected, o.Length, 9);
            }
        }

        [Fact]
        public void Smoothing_NeverSwallowsAOneTickPress_AndDirectionChangesAreImmediate()
        {
            var host = NewHost(new AxisProcessing(0.0, null, 500));
            var input = new StubInput();

            // 不变量：只持续一个 tick 的满幅按下，输出里恰有那一个 tick 的满幅。
            var peak = 0.0;
            peak = Math.Max(peak, Sample(host, input, new Vec2(0, 1)).Length);
            for (var i = 0; i < 30; i++) peak = Math.Max(peak, Sample(host, input, new Vec2(0, 0)).Length);
            Assert.Equal(1.0, peak, 9);

            // 方向改变即时：从满幅向上直接拨到满幅向右，输出方向立即是向右、幅值不被拉低。
            Sample(host, input, new Vec2(0, 1));
            var turned = Sample(host, input, new Vec2(1, 0));
            Assert.Equal(1.0, turned.X, 9);
            Assert.Equal(0.0, turned.Y, 9);
        }

        // ---------------------------------------------------------------- 键盘合成轴不经处理

        [Fact]
        public void KeyboardCompositeAxis_IsDigitalAndNotProcessed()
        {
            var host = NewHost(new AxisProcessing(0.5, AxisProcessing.Expo, 300), "composite2d:key:w|key:s|key:a|key:d");
            var input = new StubInput();
            input.Press("d");
            host.Update(input);
            var right = host.GetActionAxis(Move);
            Assert.Equal(1.0, right.X);
            input.Release("d");
            host.Update(input);
            Assert.Equal(0.0, host.GetActionAxis(Move).X); // 不平滑：松开当 tick 即归零
        }

        // ---------------------------------------------------------------- 玩家覆盖与设置通道

        [Fact]
        public void PlayerOverride_WinsOverDefinition_RoundTripsThroughSettings_AndNullRestoresDefinition()
        {
            var host = NewHost(new AxisProcessing(0.1));
            Assert.Equal(0.1, host.GetAxisProcessing(Move)!.DeadZone);

            host.SetAxisProcessing(Move, new AxisProcessing(0.25, AxisProcessing.Expo, 120));
            var effective = host.GetAxisProcessing(Move)!;
            Assert.Equal(0.25, effective.DeadZone);
            Assert.Equal(AxisProcessing.Expo, effective.ResponseCurve);
            Assert.Equal(120, effective.SmoothingMs);

            // 设置文件往返：导出 → 新宿主导入 → 生效值一致。
            var exported = host.ExportAxisSettings();
            var other = NewHost(null);
            other.ImportAxisSettings(exported);
            var imported = other.GetAxisProcessing(Move)!;
            Assert.Equal(effective.DeadZone, imported.DeadZone);
            Assert.Equal(effective.ResponseCurve, imported.ResponseCurve);
            Assert.Equal(effective.SmoothingMs, imported.SmoothingMs);

            // 清除覆盖：回到动作定义里的默认值；没有覆盖时导出为空。
            host.SetAxisProcessing(Move, null);
            Assert.Equal(0.1, host.GetAxisProcessing(Move)!.DeadZone);
            Assert.Empty(host.ExportAxisSettings());
        }

        [Fact]
        public void ImportAxisSettings_RejectsTheWholeBatchOnAnyBadEntry_AndKeepsTheExistingOverride()
        {
            var host = NewHost(null);
            host.SetAxisProcessing(Move, new AxisProcessing(0.4));

            var bad = new JsonObjectBuilder()
                .Add("input.action.nope", new JsonObjectBuilder().Add("dead_zone", new JsonNumber(0.2)).Build())
                .Build();
            Assert.ThrowsAny<Exception>(() => host.ImportAxisSettings(bad));
            Assert.Equal(0.4, host.GetAxisProcessing(Move)!.DeadZone);

            var outOfRange = new JsonObjectBuilder()
                .Add(Move, new JsonObjectBuilder().Add("dead_zone", new JsonNumber(1.5)).Build())
                .Build();
            Assert.ThrowsAny<Exception>(() => host.ImportAxisSettings(outOfRange));
            Assert.Equal(0.4, host.GetAxisProcessing(Move)!.DeadZone);
        }

        // ---------------------------------------------------------------- 数据行解析

        [Fact]
        public void FromRecord_ParsesDeadZoneCurveAndSmoothing_AndRejectsThemOnButtons()
        {
            var def = StickAction(new AxisProcessing(0.15, AxisProcessing.Expo, 80));
            Assert.Equal(0.15, def.AxisProcessing!.DeadZone);

            Assert.Throws<ArgumentException>(() => new ActionDefinition(
                new Id("input.action.btn"), ActionKind.Button, new[] { "key:x" }, "default", null,
                actionClass: null, bufferMs: null, priority: null, holdThresholdMs: null,
                repeatPolicy: InputRepeatPolicy.Refresh, faceOnAccept: null, graceConditions: null, skillSlot: null,
                controlSpace: null, axisProcessing: new AxisProcessing(0.1), holdSkillSlot: null, jumpCutRatio: null));
        }
    }
}
