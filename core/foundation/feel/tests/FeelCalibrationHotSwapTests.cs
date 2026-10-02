using System;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Xunit;
using static Tests.Foundation.Feel.FeelTestSupport;

namespace Tests.Foundation.Feel
{
    /// <summary>
    /// 标定热换（手感落地 M3-B，手感设计/05 第 8 节）：<see cref="FeelResolver.Reload(FeelProfileSet, FeelCalibration)"/> 换入新标定后，
    /// 此后的每次解析与下一个动作的快照按新标定换算；已挂在施法实例上的快照保持原标定下的结果。
    /// </summary>
    public class FeelCalibrationHotSwapTests
    {
        private static FeelResolver Make(FeelCalibration calibration) =>
            new FeelResolver(SmallProfiles(), calibration, 1.0 / 60.0, new FakeState().AsProviders());

        /// <summary>复现：参考身高 2 → 3 热换后，身高倍数字段的绝对值从"相对值 × 2"变为"相对值 × 3"；同一份档案，原始值（相对值）不变。</summary>
        [Fact]
        public void Reload_WithNewCalibration_ReconvertsTheBodyHeightFieldsToTheNewAbsoluteValue()
        {
            var oldCal = new FeelCalibration("cal.small", "p.base", 2, 4, 30, 10, 1, 32, 50);
            var newCal = new FeelCalibration("cal.small", "p.base", 3, 4, 30, 10, 1, 32, 50);
            var resolver = Make(oldCal);
            var raw = resolver.Resolve(Unit1).GetRaw(Reach).AsNumber();

            Assert.Equal(raw * oldCal.ReferenceHeight, resolver.Resolve(Unit1).GetAbsolute(Reach).AsNumber());

            resolver.Reload(SmallProfiles(), newCal);

            Assert.Same(newCal, resolver.Calibration);
            Assert.Equal(raw * newCal.ReferenceHeight, resolver.Resolve(Unit1).GetAbsolute(Reach).AsNumber());
            Assert.Equal(raw, resolver.Resolve(Unit1).GetRaw(Reach).AsNumber());
        }

        /// <summary>复现 + 不变量：进行中的动作快照保持旧标定下的绝对值，下一个动作的快照用新标定；毫秒到 tick 的换算只取决于步长，标定变化不改它。</summary>
        [Fact]
        public void Reload_WithNewCalibration_KeepsTheInFlightSnapshot_AndTheNextActionUsesTheNewCalibration()
        {
            var oldCal = new FeelCalibration("cal.small", "p.base", 2, 4, 30, 10, 1, 32, 50);
            var newCal = new FeelCalibration("cal.small", "p.base", 5, 8, 30, 10, 1, 32, 50);
            var resolver = Make(oldCal);
            var cast1 = new Id("cast.one");
            var ticksBefore = resolver.Resolve(Unit1).Judging.GetTicks(AccMs);

            var snapshot = resolver.BeginAction(Unit1, cast1, null);
            var oldAbsolute = snapshot.GetAbsolute(Reach).AsNumber();
            var oldMoveRatio = snapshot.GetAbsolute(MoveRatio).AsNumber();
            Assert.Equal(1.0 * oldCal.ReferenceHeight, oldAbsolute);

            resolver.Reload(SmallProfiles(), newCal);

            Assert.Same(snapshot, resolver.GetSnapshot(cast1));
            Assert.Equal(oldAbsolute, resolver.GetSnapshot(cast1)!.GetAbsolute(Reach).AsNumber());
            Assert.Equal(oldMoveRatio, resolver.GetSnapshot(cast1)!.GetAbsolute(MoveRatio).AsNumber());

            var cast2 = new Id("cast.two");
            var next = resolver.BeginAction(Unit1, cast2, null);
            Assert.Equal(1.0 * newCal.ReferenceHeight, next.GetAbsolute(Reach).AsNumber());
            Assert.Equal(0.5 * newCal.BaseSpeed, next.GetAbsolute(MoveRatio).AsNumber());
            Assert.Equal(oldAbsolute, resolver.GetSnapshot(cast1)!.GetAbsolute(Reach).AsNumber());

            Assert.Equal(ticksBefore, resolver.Resolve(Unit1).Judging.GetTicks(AccMs)); // 步长没变：tick 换算不变
        }

        /// <summary>不变量：重载时传入与当前相同的标定，等价于只换档案的旧重载，解析结果逐位一致。</summary>
        [Fact]
        public void Reload_WithTheSameCalibration_IsIdenticalToTheProfilesOnlyOverload()
        {
            var cal = SmallCal();
            var a = Make(cal);
            var b = Make(cal);
            a.Reload(SmallProfiles());
            b.Reload(SmallProfiles(), cal);

            Assert.Same(cal, a.Calibration);
            Assert.Same(cal, b.Calibration);
            Assert.Equal(a.Resolve(Unit1).GetAbsolute(Reach).AsNumber(), b.Resolve(Unit1).GetAbsolute(Reach).AsNumber());
            Assert.Equal(a.Resolve(Unit1).GetAbsolute(MoveRatio).AsNumber(), b.Resolve(Unit1).GetAbsolute(MoveRatio).AsNumber());
        }

        [Fact]
        public void Reload_WithNewCalibration_RejectsProfilesWithoutTheNewBasePreset_AndKeepsTheCurrentCalibration()
        {
            var cal = SmallCal();
            var resolver = Make(cal);
            var other = new FeelCalibration("cal.other", "p.missing", 2, 4, 30, 10, 1, 32, 50);

            Assert.Throws<ArgumentException>(() => resolver.Reload(SmallProfiles(), other));

            Assert.Same(cal, resolver.Calibration);
            Assert.Equal(cal.ReferenceHeight, resolver.Resolve(Unit1).GetAbsolute(Reach).AsNumber() / resolver.Resolve(Unit1).GetRaw(Reach).AsNumber());
        }
    }
}
