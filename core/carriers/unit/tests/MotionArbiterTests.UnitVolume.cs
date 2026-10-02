// 单位间体积阻挡（手感设计/02 第 9 节）的运行时冒烟：复现（相向移动/动作位移/击退撞上另一个单位，停在体积边界）+ 不变量
// （任意 tick 两单位中心距不小于半径之和；不声明体积时轨迹与没有那个单位时逐位一致）。
// 期望值全部由档案字段（unit_body_radius × 标定参考身高）与步长算出，不写死裸数。
using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Feel;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Unit
{
    public partial class MotionArbiterTests
    {
        /// <summary>档案里写的体积半径（身高倍数）；标定参考身高 2.0，所以世界半径 = 1.0，两个单位的半径之和 = 2.0。</summary>
        private const double BodyRel = 0.5;

        private const double RefHeight = 2.0;
        private const double BodyRadius = BodyRel * RefHeight;
        private const double SumRadius = 2.0 * BodyRadius;

        /// <summary>阻挡后的回退量（<see cref="MovementOptions.ArrivalEpsilon"/> 缺省值），同墙体阻挡的约定。</summary>
        private const double Pull = 0.01;

        private static void SetUnitField(Fx fx, Id unit, string field, FeelValue value) =>
            fx.Feel.DebugOverrides!.SetUnit(unit, new FeelWrite(field, FeelOp.Set, value));

        /// <summary>英雄在原点，假人在 <paramref name="dummyAt"/>；两者都声明体积（全局覆盖），<paramref name="wallSlide"/> 控制滑开。</summary>
        private static Fx BuildVolumes(
            Vec2 dummyAt, bool wallSlide = false, INavigation2D? nav = null, bool volumes = true, MovementOptions? options = null)
        {
            var fx = Build(nav: nav, options: options, withDummy: true);
            fx.Dummy.Position = dummyAt;
            fx.Actions.Owner = HeroId; // 动作只属于英雄，假人原地不动
            fx.Stats.SetBase(DummyId, (options ?? new MovementOptions()).MoveSpeedStat, Speed);
            if (volumes) fx.Set(FeelFieldNames.UnitBodyRadius, BodyRel);
            if (wallSlide) fx.Set(FeelFieldNames.WallSlide, true);
            return fx;
        }

        private static double GapToDummy(Fx fx) => (fx.Pos - fx.Units.GetPosition(DummyId)).Length;

        private static void AssertNeverOverlaps(Fx fx, string what) =>
            Assert.True(GapToDummy(fx) >= SumRadius - 1e-9, $"{what}：中心距 {GapToDummy(fx):R} 小于半径之和 {SumRadius}");

        // ------------------------------------------------------------------ 档案字段

        [Fact]
        public void UnitVolume_ProfileReadsTheRadiusInWorldUnits_AndAbsentMeansNoVolume()
        {
            var fx = Build();
            Assert.Equal(0.0, MotionProfile.Read(fx.Feel.Resolver.ResolveJudging(HeroId)).UnitBodyRadius);
            Assert.False(MotionProfile.Read(fx.Feel.Resolver.ResolveJudging(HeroId)).DodgeThroughUnits);
            Assert.Equal(0.0, MotionProfile.LegacyEquivalent.UnitBodyRadius);

            fx.Set(FeelFieldNames.UnitBodyRadius, BodyRel);
            fx.Set(FeelFieldNames.DodgeThroughUnits, true);
            var profile = MotionProfile.Read(fx.Feel.Resolver.ResolveJudging(HeroId));
            Near(BodyRel * fx.Feel.Calibration.ReferenceHeight, profile.UnitBodyRadius);
            Near(BodyRadius, profile.UnitBodyRadius);
            Assert.True(profile.DodgeThroughUnits);
        }

        // ------------------------------------------------------------------ 缺省：与既有行为逐位一致

        private static List<long[]> HeroTrack(Vec2 dummyAt, bool volumes, string? onlyUnit = null)
        {
            var fx = BuildVolumes(dummyAt, wallSlide: true, volumes: false);
            if (volumes)
            {
                if (onlyUnit == null) fx.Set(FeelFieldNames.UnitBodyRadius, BodyRel);
                else SetUnitField(fx, onlyUnit == "hero" ? HeroId : DummyId, FeelFieldNames.UnitBodyRadius, FeelValue.Of(BodyRel));
            }

            var rows = new List<long[]>();
            for (var tick = 0; tick < 50; tick++)
            {
                fx.Move(1, tick < 25 ? 0.0 : 0.3);
                fx.Tick();
                rows.Add(new[] { BitConverter.DoubleToInt64Bits(fx.Pos.X), BitConverter.DoubleToInt64Bits(fx.Pos.Y) });
            }

            return rows;
        }

        private static void AssertSameTrack(List<long[]> expected, List<long[]> actual, string what)
        {
            Assert.Equal(expected.Count, actual.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.True(expected[i][0] == actual[i][0] && expected[i][1] == actual[i][1], $"{what}：第 {i} tick 的轨迹与基准不是逐位一致");
            }
        }

        [Fact]
        public void UnitVolume_NotDeclared_HeroWalksThroughTheOtherUnit_BitIdenticalToHavingNoOtherUnitInTheWay()
        {
            var inTheWay = HeroTrack(new Vec2(6.3, 0.0), volumes: false);
            var elsewhere = HeroTrack(new Vec2(100, 100), volumes: false);
            AssertSameTrack(elsewhere, inTheWay, "未声明体积");
        }

        [Fact]
        public void UnitVolume_IsPairwise_AUnitWithoutAVolumeNeitherBlocksNorIsBlocked()
        {
            var baseline = HeroTrack(new Vec2(100, 100), volumes: false);
            AssertSameTrack(baseline, HeroTrack(new Vec2(6.3, 0.0), volumes: true, onlyUnit: "hero"), "只有英雄声明体积");
            AssertSameTrack(baseline, HeroTrack(new Vec2(6.3, 0.0), volumes: true, onlyUnit: "dummy"), "只有假人声明体积");
        }

        // ------------------------------------------------------------------ 输入方向位移（regular）

        [Fact]
        public void UnitVolume_Regular_WalkingIntoAnotherUnit_StopsAtTheBoundary_AndNeverCreepsOrOverlaps()
        {
            var dummyX = 6.3;
            var fx = BuildVolumes(new Vec2(dummyX, 0));
            var boundary = dummyX - SumRadius;
            for (var tick = 0; tick < 40; tick++)
            {
                fx.Move(1, 0);
                fx.Tick();
                AssertNeverOverlaps(fx, $"tick {tick}");
            }

            Assert.InRange(fx.Pos.X, boundary - Pull - 1e-9, boundary + 1e-9);
            var stoppedAt = fx.Pos;
            for (var tick = 0; tick < 10; tick++)
            {
                fx.Move(1, 0);
                fx.Tick();
            }

            Assert.Equal(stoppedAt, fx.Pos); // 贴着体积继续推：不蠕动、不抖动
            Assert.Equal(0.0, fx.Mo.Speed);
            Assert.Equal(0.0, fx.Pos.Y);
        }

        [Fact]
        public void UnitVolume_Regular_WallSlide_GlidesAroundTheOtherUnit_WithoutEverEnteringIt()
        {
            const double dummyX = 6.3;

            (Vec2 End, double MinGap) Run(bool wallSlide)
            {
                var fx = BuildVolumes(new Vec2(dummyX, 0.5), wallSlide);
                var minGap = double.MaxValue;
                for (var tick = 0; tick < 60; tick++)
                {
                    fx.Move(1, 0);
                    fx.Tick();
                    minGap = Math.Min(minGap, GapToDummy(fx));
                }

                return (fx.Pos, minGap);
            }

            var stop = Run(false);
            var slide = Run(true);
            Assert.True(stop.MinGap >= SumRadius - 1e-9 && slide.MinGap >= SumRadius - 1e-9, $"最小中心距 stop={stop.MinGap:R} slide={slide.MinGap:R}");
            Assert.True(stop.End.X < dummyX, "不滑开：停在体积前，没有越过假人");
            Assert.Equal(0.0, stop.End.Y);
            Assert.True(slide.End.X > dummyX + SumRadius, $"滑开：绕过假人继续向前，x={slide.End.X:R}");
            Assert.True(slide.End.Y < 0.0, $"假人在输入轴上方，英雄应从下方滑过，y={slide.End.Y:R}");
        }

        [Fact]
        public void UnitVolume_Regular_StartingInsideTheOverlap_OnlyBlocksMovingCloser()
        {
            var fx = BuildVolumes(new Vec2(1.0, 0), wallSlide: false);
            fx.Move(1, 0);
            fx.Tick();
            Assert.Equal(0.0, fx.Pos.X); // 朝圆心走被拒
            fx.Move(-1, 0);
            fx.Tick();
            Near(-Speed * Dt, fx.Pos.X); // 走开放行
            fx.Move(0, 1);
            fx.Tick();
            Assert.True(fx.Pos.Y > 0.0); // 切向放行（没有比起点更深）
        }

        [Fact]
        public void UnitVolume_DeadUnitsDoNotBlock()
        {
            var fx = BuildVolumes(new Vec2(6.3, 0));
            fx.Units.SetAlive(DummyId, false);
            for (var tick = 0; tick < 30; tick++)
            {
                fx.Move(1, 0);
                fx.Tick();
            }

            Assert.True(fx.Pos.X > 6.3);
        }

        // ------------------------------------------------------------------ 不变量：相向移动，任意 tick 不重叠

        [Theory]
        [InlineData(1u, false)]
        [InlineData(2u, true)]
        [InlineData(3u, false)]
        [InlineData(4u, true)]
        [InlineData(5u, true)]
        [InlineData(6u, false)]
        public void UnitVolume_TwoUnitsSteeringRandomlyTowardEachOther_NeverOverlapOnAnyTick(uint seed, bool wallSlide)
        {
            var fx = BuildVolumes(new Vec2(5, 0.6), wallSlide);
            fx.Player.Position = new Vec2(-5, -0.4);
            var state = seed * 2654435761u + 12345u;
            double Next()
            {
                state = state * 1664525u + 1013904223u;
                return (state >> 8) / (double)(1u << 24);
            }

            var minGap = double.MaxValue;
            for (var tick = 0; tick < 200; tick++)
            {
                foreach (var (id, other) in new[] { (HeroId, DummyId), (DummyId, HeroId) })
                {
                    var toward = fx.Units.GetPosition(other) - fx.Units.GetPosition(id);
                    var angle = Math.Atan2(toward.Y, toward.X) + (Next() < 0.7 ? 0.0 : (Next() - 0.5) * Math.PI);
                    fx.World.SubmitIntent(new Intent(id, "move", new JsonObjectBuilder()
                        .Add("dx", new JsonNumber(Math.Cos(angle))).Add("dy", new JsonNumber(Math.Sin(angle)))
                        .Add("mode", new JsonString("Run")).Build()));
                }

                fx.Tick();
                var gap = GapToDummy(fx);
                minGap = Math.Min(minGap, gap);
                Assert.True(gap >= SumRadius - 1e-9, $"seed {seed} tick {tick}：中心距 {gap:R} < {SumRadius}");
            }

            Assert.True(minGap < SumRadius + 0.1, $"seed {seed}：两个单位应当真的撞上过（最小中心距 {minGap:R}），否则这条不变量是空判");
        }

        // ------------------------------------------------------------------ 动作位移

        private static void RunLunge(Fx fx, ActionMotionState motion, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                fx.Actions.State = i < motion.EndTick + 2 ? Act(i, motion) : (ActionState?)null;
                fx.Tick();
            }
        }

        [Fact]
        public void UnitVolume_ActionLunge_StopsAtTheBoundary_AndAHighSpeedStepCannotTunnelThroughTheUnit()
        {
            // 慢速：8 个单位距离分 5 个 tick，每 tick 1.6，第 3 个 tick 起步即撞上。
            var fx = BuildVolumes(new Vec2(6.3, 0));
            RunLunge(fx, Lunge(8.0, 0, 5, new Vec2(1, 0)), 8);
            Assert.InRange(fx.Pos.X, 6.3 - SumRadius - Pull - 1e-9, 6.3 - SumRadius + 1e-9);
            AssertNeverOverlaps(fx, "慢速扑击");

            // 单 tick 位移 12 远大于半径之和 4：端点检测会隧穿，扫掠不会。
            var fast = BuildVolumes(new Vec2(6.0, 0));
            RunLunge(fast, Lunge(12.0, 0, 1, new Vec2(1, 0)), 4);
            Assert.InRange(fast.Pos.X, 6.0 - SumRadius - Pull - 1e-9, 6.0 - SumRadius + 1e-9);
            AssertNeverOverlaps(fast, "高速扑击");

            // 不声明体积：同一个扑击一路穿过去，净位移就是声明距离。
            var none = BuildVolumes(new Vec2(6.0, 0), volumes: false);
            RunLunge(none, Lunge(12.0, 0, 1, new Vec2(1, 0)), 4);
            Near(12.0, none.Pos.X);
        }

        [Fact]
        public void UnitVolume_ActionDash_PassesThroughOnlyWhenTheProfileSaysSo_AndLungeNeverDoes()
        {
            Fx Run(ActionMotionKind kind, bool through)
            {
                var fx = BuildVolumes(new Vec2(6.0, 0));
                if (through) fx.Set(FeelFieldNames.DodgeThroughUnits, true);
                RunLunge(fx, Lunge(12.0, 0, 3, new Vec2(1, 0), kind: kind), 6);
                return fx;
            }

            Near(12.0, Run(ActionMotionKind.Dash, through: true).Pos.X);
            Near(12.0, Run(ActionMotionKind.StepBack, through: true).Pos.X);
            var blockedDash = Run(ActionMotionKind.Dash, through: false);
            Assert.True(blockedDash.Pos.X < 6.0 - SumRadius + 1e-9);
            AssertNeverOverlaps(blockedDash, "声明前的闪避");
            var lunge = Run(ActionMotionKind.Lunge, through: true);
            Assert.True(lunge.Pos.X < 6.0 - SumRadius + 1e-9, "dodge_through_units 只管闪避类位移，扑击仍被挡");
            AssertNeverOverlaps(lunge, "声明 dodge_through_units 后的扑击");
        }

        [Fact]
        public void UnitVolume_ActionLunge_BlockingSlide_GlidesAroundTheUnit_StopDoesNot()
        {
            double RunY(ActionMotionBlocking blocking)
            {
                var fx = BuildVolumes(new Vec2(4.0, 0.5));
                var motion = Lunge(9.0, 0, 6, new Vec2(1, 0), blocking: blocking);
                for (var i = 0; i < 9; i++)
                {
                    fx.Actions.State = i < 6 ? Act(i, motion) : (ActionState?)null;
                    fx.Tick();
                    AssertNeverOverlaps(fx, $"{blocking} tick {i}");
                }

                return fx.Pos.Y;
            }

            var stopY = RunY(ActionMotionBlocking.Stop);
            var slideY = RunY(ActionMotionBlocking.Slide);
            Assert.True(Math.Abs(stopY) < 1e-9, $"stop 停在体积前，没有横向位移，y={stopY:R}");
            Assert.True(slideY < -0.3, $"slide 沿体积切向滑开，y={slideY:R}");
        }

        // ------------------------------------------------------------------ 强制位移（击退）

        [Fact]
        public void UnitVolume_Knockback_IsClippedAtTheOtherUnit_AndEndsAsBlocked()
        {
            var fx = BuildVolumes(new Vec2(5.0, 0));
            fx.Host.BeginKnockback(new KnockbackRequest(HeroId, new Vec2(1, 0), 8.0, 0.4));
            for (var tick = 0; tick < 8; tick++)
            {
                fx.Tick();
                AssertNeverOverlaps(fx, $"击退 tick {tick}");
            }

            Assert.InRange(fx.Pos.X, 5.0 - SumRadius - Pull - 1e-9, 5.0 - SumRadius + 1e-9);
            Assert.Contains(fx.Stops, s => s.Reason == MoveStopReason.DisplacementBlocked);
            Assert.DoesNotContain(fx.Stops, s => s.Reason == MoveStopReason.DisplacementArrived);

            // 不声明体积：同一个击退走满声明距离。
            var none = BuildVolumes(new Vec2(5.0, 0), volumes: false);
            none.Host.BeginKnockback(new KnockbackRequest(HeroId, new Vec2(1, 0), 8.0, 0.4));
            for (var tick = 0; tick < 8; tick++) none.Tick();
            Near(8.0, none.Pos.X);
        }

        // ------------------------------------------------------------------ 路径跟随与追击

        [Fact]
        public void UnitVolume_PathFollowing_StopsInFrontOfTheUnit_AndSlidesAroundItWhenWallSlideIsOn()
        {
            var stop = BuildVolumes(new Vec2(5.0, 0));
            stop.MoveTo(10, 0);
            for (var tick = 0; tick < 60; tick++)
            {
                stop.Tick();
                AssertNeverOverlaps(stop, $"停下 tick {tick}");
            }

            Assert.InRange(stop.Pos.X, 5.0 - SumRadius - Pull - 1e-9, 5.0 - SumRadius + 1e-9);
            Assert.NotNull(stop.Player.MovementState.CurrentPath); // 路径还在，没有被吞掉

            var slide = BuildVolumes(new Vec2(5.0, 0.5), wallSlide: true);
            slide.MoveTo(10, 0);
            for (var tick = 0; tick < 80; tick++)
            {
                slide.Tick();
                AssertNeverOverlaps(slide, $"滑开 tick {tick}");
            }

            Assert.True(slide.Pos.X > 5.0 + SumRadius * 0.5, $"沿路径绕过假人，x={slide.Pos.X:R}");
        }

        [Fact]
        public void UnitVolume_Chase_StopsAtTheVolumeBoundary_EvenWhenTheStopRangeIsSmaller()
        {
            var fx = BuildVolumes(new Vec2(12.0, 0));
            fx.Chase(DummyId, 0.5);
            for (var tick = 0; tick < 60; tick++)
            {
                fx.Tick();
                AssertNeverOverlaps(fx, $"追击 tick {tick}");
            }

            Assert.InRange(GapToDummy(fx), SumRadius, SumRadius + 3 * Pull);
            Assert.Equal(MoveMode.Idle, fx.Player.MovementState.Mode); // 到位后是"待命"，不是永远在靠近
            Assert.True(fx.Player.MovementState.Chase.HasValue);

            var none = BuildVolumes(new Vec2(12.0, 0), volumes: false);
            none.Chase(DummyId, 0.5);
            for (var tick = 0; tick < 60; tick++) none.Tick();
            Assert.InRange(GapToDummy(none), 0.0, 0.5 + 1e-6);
        }
    }
}
