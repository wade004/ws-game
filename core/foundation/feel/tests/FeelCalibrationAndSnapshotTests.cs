using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;
using Xunit;
using static Tests.Foundation.Feel.FeelTestSupport;

namespace Tests.Foundation.Feel
{
    /// <summary>标定（05 第 10 节第 7 条）、动作快照（第 5 条）、临时状态精确恢复（第 4 条）。</summary>
    public class FeelCalibrationAndSnapshotTests
    {
        private static FeelResolver Make(FeelProfileSet profiles, FeelCalibration cal, FakeState state, double step = 1.0 / 60.0) =>
            new FeelResolver(profiles, cal, step, state.AsProviders());

        // ------------------------------------------------------------------ 05 §10-7：标定

        [Fact]
        public void SameRelativeValue_UnderTwoCalibrations_ConvertsToFormulaAbsoluteValues()
        {
            var character = FeelRow.Overlay(FeelTables.Character, "c.mix", new[] { Set(Reach, 2.5), Set(MoveRatio, 1.5) });
            var profiles = SmallProfiles(character);
            var state = new FakeState { Character = "c.mix" };
            var calA = new FeelCalibration("cal.a", "p.base", 2.0, 4.0, 30, 10, 1, 32, 50);
            var calB = new FeelCalibration("cal.b", "p.base", 1.5, 6.0, 60, 12, 1.5, 16, 30);

            var a = Make(profiles, calA, state).Resolve(Unit1);
            var b = Make(profiles, calB, state).Resolve(Unit1);

            // 相对值不随标定变化。
            Assert.Equal(a.GetRaw(Reach), b.GetRaw(Reach));
            // 绝对值 = 相对值 × 标定：身高倍数 × 参考身高；基础移速倍数 × 基础移速。
            Assert.Equal(2.5 * 2.0, a.GetAbsolute(Reach).AsNumber());
            Assert.Equal(2.5 * 1.5, b.GetAbsolute(Reach).AsNumber());
            Assert.Equal(1.5 * 4.0, a.GetAbsolute(MoveRatio).AsNumber());
            Assert.Equal(1.5 * 6.0, b.GetAbsolute(MoveRatio).AsNumber());
            // 倍率类单位不换算。
            Assert.Equal(a.GetRaw(WeaponScale), a.GetAbsolute(WeaponScale));
        }

        [Theory]
        [InlineData(FeelUnit.BodyHeights, 3.0, 6.0)]
        [InlineData(FeelUnit.BaseSpeedRatio, 3.0, 12.0)]
        [InlineData(FeelUnit.BaseSpeedSeconds, 3.0, 12.0)]
        [InlineData(FeelUnit.ScreenHeightRatio, 0.05, 0.5)]
        [InlineData(FeelUnit.Milliseconds, 123.0, 123.0)]
        [InlineData(FeelUnit.DegreesPerSecond, 90.0, 90.0)]
        [InlineData(FeelUnit.Ratio, 1.25, 1.25)]
        [InlineData(FeelUnit.IntensityTier, 2.0, 2.0)]
        public void ToAbsolute_FollowsTheUnitTable(FeelUnit unit, double relative, double expected)
        {
            var cal = new FeelCalibration("cal.a", "p", 2.0, 4.0, 30, 10, 1, 32, 50);
            Assert.Equal(expected, cal.ToAbsolute(unit, relative), 12);
        }

        [Theory]
        [InlineData(100.0, 60, 6)]      // 100 ms / 16.667 ms = 6.0
        [InlineData(1.0, 60, 1)]        // 0.06 → 四舍五入为 0，但非零至少 1
        [InlineData(0.0, 60, 0)]        // 零保持零
        [InlineData(25.0, 60, 2)]       // 1.5 → 远离零取整为 2
        [InlineData(8.0, 60, 1)]        // 0.48 → 0 → 至少 1
        [InlineData(100.0, 30, 3)]      // 100 / 33.333 = 3.0
        [InlineData(100.0, 120, 12)]    // 100 / 8.333 = 12.0
        [InlineData(50.0, 30, 2)]       // 1.5 → 2
        public void MillisecondsToTicks_RoundsToNearest_NonZeroAtLeastOne(double ms, int tickRate, int expected)
        {
            Assert.Equal(expected, FeelCalibration.MillisecondsToTicks(ms, 1.0 / tickRate));
        }

        [Fact]
        public void JudgingMillisecondFields_ExposeTicks_AndPresentingOnesDoNot()
        {
            var character = FeelRow.Overlay(FeelTables.Character, "c.t", new[] { Set(AccMs, 100), Set(Opt, 1) });
            var state = new FakeState { Character = "c.t" };
            var step = 1.0 / 30.0;
            var r = Make(SmallProfiles(character), SmallCal(), state, step).Resolve(Unit1);

            Assert.True(r.Judging.HasTicks(AccMs));
            Assert.Equal(3, r.Judging.GetTicks(AccMs));       // 100 / 33.33
            Assert.Equal(1, r.Judging.GetTicks(Opt));          // 1 ms 非零至少 1
            Assert.Throws<InvalidOperationException>(() => r.Judging.GetTicks(Level));
            Assert.False(r.Judging.HasTicks(Level));
        }

        [Fact]
        public void Calibration_RejectsNonPositiveValues()
        {
            Assert.Throws<ArgumentException>(() => new FeelCalibration("c", "p", 0, 4, 30, 10, 1, 32, 0));
            Assert.Throws<ArgumentException>(() => new FeelCalibration("c", "p", 2, -1, 30, 10, 1, 32, 0));
            Assert.Throws<ArgumentException>(() => new FeelCalibration("c", "", 2, 4, 30, 10, 1, 32, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeelCalibration.MillisecondsToTicks(10, 0));
        }

        // ------------------------------------------------------------------ 05 §10-5：动作快照

        [Fact]
        public void ActionSnapshot_IsUnchangedByHotReloadAndRecompute_AndNextActionSeesNewData()
        {
            var oldWeapon = FeelRow.Weapon("w.sword", new[] { Set(WeaponScale, 2), Set(MoveRatio, 0.1) });
            var state = new FakeState { Main = "w.sword" };
            var resolver = Make(SmallProfiles(oldWeapon), SmallCal(), state);
            var cast1 = new Id("cast.one");

            var snapshot = resolver.BeginAction(Unit1, cast1, null);
            Assert.Equal(2.0, snapshot.GetRaw(WeaponScale).AsNumber());
            Assert.Equal(0.1, snapshot.GetRaw(MoveRatio).AsNumber()); // 快照视单位处于动作中：攻击期间覆盖生效

            // 动作进行中热加载：武器数值改了。
            var newWeapon = FeelRow.Weapon("w.sword", new[] { Set(WeaponScale, 6), Set(MoveRatio, 0.9) });
            resolver.Reload(SmallProfilesWith(newWeapon));

            Assert.Same(snapshot, resolver.GetSnapshot(cast1));
            Assert.Equal(2.0, resolver.GetSnapshot(cast1)!.GetRaw(WeaponScale).AsNumber());
            Assert.Equal(0.1, resolver.GetSnapshot(cast1)!.GetRaw(MoveRatio).AsNumber());

            // 单位自己的解析（缓存）已经是新数据；下一次动作的快照也是新数据。
            Assert.Equal(6.0, resolver.Resolve(Unit1).GetRaw(WeaponScale).AsNumber());
            var cast2 = new Id("cast.two");
            Assert.Equal(6.0, resolver.BeginAction(Unit1, cast2, null).GetRaw(WeaponScale).AsNumber());
            Assert.Equal(2.0, resolver.GetSnapshot(cast1)!.GetRaw(WeaponScale).AsNumber());
            Assert.Equal(2, resolver.SnapshotCount);

            resolver.EndAction(cast1);
            resolver.EndAction(cast1); // 幂等
            Assert.Null(resolver.GetSnapshot(cast1));
            Assert.Equal(1, resolver.SnapshotCount);
        }

        [Fact]
        public void ActionSnapshot_UsesTheGivenActionFeelRefAsLayer6()
        {
            var action = FeelRow.Overlay(FeelTables.Action, "x.slash", new[] { Mul(WeaponScale, 3) });
            var state = new FakeState();
            var resolver = Make(SmallProfiles(action), SmallCal(), state);

            var snap = resolver.BeginAction(Unit1, new Id("cast.x"), "x.slash");

            Assert.Equal(3.0, snap.GetRaw(WeaponScale).AsNumber());
            Assert.Equal(1.0, resolver.Resolve(Unit1).GetRaw(WeaponScale).AsNumber()); // 缓存不受动作快照污染
        }

        [Fact]
        public void Reload_RejectsProfilesWithoutTheBasePreset()
        {
            var resolver = Make(SmallProfiles(), SmallCal(), new FakeState());
            var noPreset = new FeelProfileSet(SmallFields(), Array.Empty<FeelRow>());
            Assert.Throws<ArgumentException>(() => resolver.Reload(noPreset));
        }

        private static FeelProfileSet SmallProfilesWith(params FeelRow[] rows) => new FeelProfileSet(
            SmallProfiles().Fields, new[] { SmallPreset() }.Concat(rows));

        // ------------------------------------------------------------------ 05 §10-4：临时状态精确恢复

        [Fact]
        public void AuraApplyThenRemove_RestoresExactValuesOfEveryField_NotReverseMultiplication()
        {
            var character = FeelRow.Overlay(FeelTables.Character, "c.base", new[] { Mul(AccMs, 1.0 / 3.0), Add(Reach, 0.1) });
            var state = new FakeState { Character = "c.base" };
            var resolver = Make(SmallProfiles(character), SmallCal(), state);
            var before = resolver.Resolve(Unit1);
            var beforeRaw = SmallFields().Fields.Select(f => before.GetRaw(f.Name)).ToArray();
            var beforeAbs = SmallFields().Fields.Select(f => before.GetAbsolute(f.Name)).ToArray();

            // 施加两个会产生浮点误差的光环：×0.1 与 ×3、+0.7。
            state.Temp.Add(new FeelTemporaryEntry("aura.slow", new[] { Mul(AccMs, 0.1), Add(Reach, 0.7) }));
            state.Temp.Add(new FeelTemporaryEntry("aura.fast", new[] { Mul(AccMs, 3.0) }));
            resolver.Invalidate(Unit1, "aura.applied");
            var during = resolver.Resolve(Unit1);
            Assert.NotEqual(before.GetRaw(AccMs), during.GetRaw(AccMs));
            Assert.NotEqual(before.GetRaw(Reach), during.GetRaw(Reach));

            // 先移除一个，再移除另一个（顺序与施加不同），每次都失效重算。
            state.Temp.RemoveAll(e => e.Key == "aura.slow");
            resolver.Invalidate(Unit1, "aura.removed");
            Assert.NotEqual(before.GetRaw(AccMs), resolver.Resolve(Unit1).GetRaw(AccMs));
            state.Temp.RemoveAll(e => e.Key == "aura.fast");
            resolver.Invalidate(Unit1, "aura.removed");
            var after = resolver.Resolve(Unit1);

            // 精确相等（位级），不是"近似相等"。
            for (var i = 0; i < beforeRaw.Length; i++)
            {
                var name = SmallFields()[i].Name;
                Assert.Equal(beforeRaw[i], after.GetRaw(name));
                Assert.Equal(beforeAbs[i], after.GetAbsolute(name));
            }
        }
    }
}
