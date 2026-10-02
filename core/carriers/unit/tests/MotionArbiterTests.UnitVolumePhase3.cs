// 单位间体积阻挡三期（M4-W2，手感设计/02 第 3.5 节、ADR-0128 追加决定）的运行时冒烟：每项一条复现（量从 X 变到 Y，期望值由档案字段 × 标定参考身高 × 步长
// 与规则算出，不写死裸数）加一条不变量（任意 tick、任意顺序/任意起步分类下成立）。
//   1 分类由求解结果决定、与预判无关   2 按实际速度剖面求接触时刻   3 穿过式位移（幽灵）落点修正与别人看得到幽灵终点
//   4 被拉回保留切向速度、路径进度不丢   5 同 tick 推人   6 宽相网格与暴力逐位一致、n=200 耗时对比
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Carriers.Unit
{
    public partial class MotionArbiterTests
    {
        private readonly ITestOutputHelper _output;

        public MotionArbiterTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // ================================================================== 1 分类由求解结果决定、与预判无关

        private static (List<long[]> Track, List<string> Events, int MaxPasses) CrowdWithClassifier(Func<Id, bool>? classifier)
        {
            var run = RunCrowd(0, crowd => crowd.Handler.VolumeStartClassifier = classifier);
            return (run.Track, run.Events, run.MaxPasses);
        }

        [Fact]
        public void Classification_CrowdTracksAndEventStreams_AreBitIdentical_WhateverTheStartClassifierSays()
        {
            var baseline = CrowdWithClassifier(null);
            Assert.True(baseline.Events.Any(e => e.StartsWith("moved", StringComparison.Ordinal)), "场景发生过移动事件（防空判）");

            var variants = new List<(string Name, Func<Id, bool> Classifier)>
            {
                ("全部会动", _ => true),
                ("全部静止", _ => false),
                ("与预判相反", id => id.ToString().GetHashCode() % 2 == 0), // 仅用于下面的"遍数"断言，不参与结果比较的口径
            };
            for (var seed = 1u; seed <= 5u; seed++)
            {
                var s = seed;
                variants.Add(($"随机 {seed}", id => Hash(id.ToString(), (int)s) % 2u == 0u));
            }

            var maxPasses = baseline.MaxPasses;
            foreach (var (name, classifier) in variants)
            {
                var run = CrowdWithClassifier(classifier);
                maxPasses = Math.Max(maxPasses, run.MaxPasses);
                Assert.Equal(baseline.Track.Count, run.Track.Count);
                for (var tick = 0; tick < baseline.Track.Count; tick++)
                {
                    Assert.True(baseline.Track[tick].SequenceEqual(run.Track[tick]), $"起步分类「{name}」下第 {tick} tick 的位置与缺省不是逐位一致");
                }

                Assert.Equal(baseline.Events, run.Events);
            }

            // 场景确实用到了多遍求解（起步分类被纠正过），否则"与分类无关"是空判。
            Assert.True(maxPasses >= 2, $"最多求解遍数 {maxPasses}：没有任何 tick 需要纠正起步分类");
        }

        private static (List<long[]> Hero, int Passes) WalkPastAWalledInUnit(bool withMoveIntent, Func<Id, bool>? classifier)
        {
            // 假人被墙贴着（想动动不了）：有"移动意图"时预判会动（预判错），没有意图时预判不动（预判对）。
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(5.005, -50), new Vec2(9, 50)) });
            var fx = BuildVolumes(new Vec2(5.0, 0.6), wallSlide: true, nav: nav);
            fx.Handler.VolumeStartClassifier = classifier;
            SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            var rows = new List<long[]>();
            var maxPasses = 0;
            for (var tick = 0; tick < 40; tick++)
            {
                fx.Move(1, 0);
                if (withMoveIntent) SubmitMove(fx, DummyId, 1, 0);
                fx.Tick();
                maxPasses = Math.Max(maxPasses, fx.Handler.LastVolumePassCount);
                rows.Add(new[]
                {
                    BitConverter.DoubleToInt64Bits(fx.Pos.X), BitConverter.DoubleToInt64Bits(fx.Pos.Y),
                    BitConverter.DoubleToInt64Bits(fx.Units.GetPosition(DummyId).X), BitConverter.DoubleToInt64Bits(fx.Units.GetPosition(DummyId).Y),
                });
            }

            return (rows, maxPasses);
        }

        [Fact]
        public void Classification_AMispredictedMover_DoesNotChangeWhatTheNeighbourDoes()
        {
            var honest = WalkPastAWalledInUnit(withMoveIntent: false, classifier: null);
            var mispredicted = WalkPastAWalledInUnit(withMoveIntent: true, classifier: null);

            // 复现：假人有移动意图但被墙贴着动不了（预判"会动"是错的）——英雄的轨迹与"假人本来就没有意图"逐位一致（沿假人体积滑开，不是对称撞停）。
            Assert.Equal(honest.Hero.Count, mispredicted.Hero.Count);
            for (var i = 0; i < honest.Hero.Count; i++)
            {
                Assert.True(honest.Hero[i].SequenceEqual(mispredicted.Hero[i]), $"第 {i} tick：预判错与预判对的轨迹不一致");
            }

            var last = honest.Hero[honest.Hero.Count - 1];
            var heroY = BitConverter.Int64BitsToDouble(last[1]);
            Assert.True(heroY < -1e-6, $"英雄沿假人体积滑到了一侧：y = {heroY:R}");
            Assert.Equal(1, honest.Passes);
            Assert.True(mispredicted.Passes >= 2, "预判错的 tick 重新求解了至少一遍");

            // 不变量：起步分类取任何值，结果不变。
            foreach (var classifier in new Func<Id, bool>?[] { _ => true, _ => false, id => id.Equals(DummyId) })
            {
                var run = WalkPastAWalledInUnit(withMoveIntent: true, classifier);
                for (var i = 0; i < honest.Hero.Count; i++)
                {
                    Assert.True(honest.Hero[i].SequenceEqual(run.Hero[i]), $"第 {i} tick：起步分类改变了结果");
                }
            }
        }

        // ================================================================== 2 按实际速度剖面求接触时刻

        /// <summary>首次让中心距降到 <paramref name="threshold"/> 的时刻（二分到 1e-15；间隙随时间单调不增，起点间隙为正）。</summary>
        private static double FirstTimeBelow(Func<double, double> gap, double threshold)
        {
            double lo = 0.0, hi = 1.0;
            Assert.True(gap(0.0) > threshold && gap(1.0) <= threshold, "夹具：这个 tick 内必须发生接触");
            for (var i = 0; i < 80; i++)
            {
                var mid = 0.5 * (lo + hi);
                if (gap(mid) <= threshold) hi = mid; else lo = mid;
            }

            return hi;
        }

        /// <summary>
        /// 英雄从静止加速（<paramref name="heroAccelMs"/>，曲线 <paramref name="curve"/>）迎面走向在 <c>dummyX</c> 的假人（假人瞬时达速、反向走）。
        /// 返回第一个 tick 之后两者的位置与按规则算出的期望位置（接触时刻由英雄本 tick 的速度剖面决定，再退一个到达容差对应的时间）。
        /// </summary>
        private static (double Hero, double Dummy, double ExpectedHero, double ExpectedDummy, double UniformHero) HeadOnWithRamp(
            double dummyX, double heroAccelMs, string curve)
        {
            var fx = BuildVolumes(new Vec2(dummyX, 0));
            SetUnitField(fx, HeroId, FeelFieldNames.AccelMs, FeelValue.Of(heroAccelMs));
            if (curve != "linear") SetUnitField(fx, HeroId, FeelFieldNames.AccelCurve, FeelValue.Of(curve));
            fx.Move(1, 0);
            SubmitMove(fx, DummyId, -1, 0);
            fx.Tick();

            // 规则：加速 accel_ms 折成 tick 数，每 tick 沿曲线前进 1/ticks 的进度；本 tick 起速 0，进度 0 → 1/ticks。
            var ticks = heroAccelMs / (Dt * 1000.0);
            double Curve(double t) => curve == "linear" ? t : (curve == "ease_in" ? t * t : 1.0 - (1.0 - t) * (1.0 - t));
            double V(double f) => Speed * Curve(f / ticks); // tick 内 f 处的速率（线性插值进度）
            var v1 = V(1.0);
            var heroStep = v1 * Dt;
            var dummyStep = Speed * Dt;

            // 辛普森积分得到 u(t)（弧长占比随时间）。
            double IntegralTo(double t)
            {
                const int n = 4000;
                var h = t / n;
                var sum = V(0.0) + V(t);
                for (var k = 1; k < n; k++) sum += V(k * h) * (k % 2 == 1 ? 4.0 : 2.0);
                return sum * h / 3.0;
            }

            var total = IntegralTo(1.0);
            double U(double t) => IntegralTo(t) / total;
            double Gap(double t) => dummyX - dummyStep * t - heroStep * U(t);
            var tau = FirstTimeBelow(Gap, SumRadius - 1e-9);
            var du = (U(tau + 1e-6) - U(tau - 1e-6)) / 2e-6;
            var relSpeed = heroStep * du + dummyStep;
            var retreated = tau - Pull / relSpeed;
            var expectedHero = heroStep * U(retreated);
            var expectedDummy = dummyX - dummyStep * retreated;

            // 对照：假设沿折线匀速（旧模型）的接触点。
            var tauUniform = (dummyX - SumRadius) / (heroStep + dummyStep);
            var uniformHero = heroStep * (tauUniform - Pull / (heroStep + dummyStep));
            return (fx.Pos.X, fx.Units.GetPosition(DummyId).X, expectedHero, expectedDummy, uniformHero);
        }

        [Theory]
        [InlineData(2.3, 300.0, "linear")]
        [InlineData(2.4, 200.0, "linear")]
        [InlineData(2.3, 300.0, "ease_in")]
        [InlineData(2.3, 500.0, "ease_out")]
        public void SpeedProfile_HeadOnContactTime_FollowsTheActualAccelerationProfile_NotUniformSpeed(double dummyX, double accelMs, string curve)
        {
            var r = HeadOnWithRamp(dummyX, accelMs, curve);
            var tol = curve == "linear" ? 1e-6 : 2e-4; // 非线性曲线的剖面是 64 段表格近似
            Near(r.ExpectedHero, r.Hero, tol);
            Near(r.ExpectedDummy, r.Dummy, tol);

            // 复现：从静止加速的英雄这个 tick 前半段几乎没动，接触发生得更晚、英雄走得更少；匀速模型会让英雄停得更靠前（X → Y）。
            Assert.True(Math.Abs(r.Hero - r.UniformHero) > 0.005, $"夹具要能区分剖面与匀速：{r.Hero:R} vs {r.UniformHero:R}");
            Assert.True(r.Hero < r.UniformHero, "从静止加速：接触时刻更晚，英雄这个 tick 走得比匀速模型少");
        }

        [Fact]
        public void SpeedProfile_NoAccelerationDeclared_KeepsTheUniformResultBitForBit()
        {
            // 瞬时达速（缺省）：剖面为空，仍是匀速解析式——两个相向走的单位镜像对称到逐位相等，接触点就是匀速模型的闭式解。
            var fx = BuildVolumes(new Vec2(1.15, 0));
            fx.Player.Position = new Vec2(-1.15, 0);
            fx.Move(1, 0);
            SubmitMove(fx, DummyId, -1, 0);
            fx.Tick();
            var hero = fx.Pos.X;
            var dummy = fx.Units.GetPosition(DummyId).X;
            Assert.Equal(BitConverter.DoubleToInt64Bits(-hero), BitConverter.DoubleToInt64Bits(dummy));
            var tau = (2.3 - SumRadius) / (2.0 * Speed * Dt);
            Near(-1.15 + Speed * Dt * (tau - Pull / (2.0 * Speed * Dt)), hero, 1e-9);
        }

        [Fact]
        public void SpeedProfile_Invariant_NeverEntersTheVolumeDeeperThanBefore_ForAnyAccelerationCombination()
        {
            var cases = 0;
            foreach (var heroAccel in new[] { 0.0, 100.0, 300.0, 700.0, 1500.0 })
            {
                foreach (var dummyAccel in new[] { 0.0, 200.0, 900.0 })
                {
                    foreach (var curve in new[] { "linear", "ease_in", "ease_out" })
                    {
                        foreach (var gap0 in new[] { 2.05, 2.6, 3.3, 4.4 })
                        {
                            var fx = BuildVolumes(new Vec2(gap0, 0));
                            SetUnitField(fx, HeroId, FeelFieldNames.AccelMs, FeelValue.Of(heroAccel));
                            SetUnitField(fx, DummyId, FeelFieldNames.AccelMs, FeelValue.Of(dummyAccel));
                            SetUnitField(fx, HeroId, FeelFieldNames.AccelCurve, FeelValue.Of(curve));
                            SetUnitField(fx, DummyId, FeelFieldNames.AccelCurve, FeelValue.Of(curve));
                            SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
                            var prev = GapToDummy(fx);
                            for (var tick = 0; tick < 14; tick++)
                            {
                                fx.Move(1, 0);
                                SubmitMove(fx, DummyId, -1, 0);
                                fx.Tick();
                                var gap = GapToDummy(fx);
                                Assert.True(
                                    gap >= Math.Min(SumRadius, prev) - 1e-9,
                                    $"英雄加速 {heroAccel} / 假人加速 {dummyAccel} / {curve} / 初始间距 {gap0} / tick {tick}：中心距 {gap:R} 比上一 tick（{prev:R}）更深地进入体积");
                                prev = gap;
                            }

                            cases++;
                        }
                    }
                }
            }

            Assert.Equal(5 * 3 * 3 * 4, cases);
        }

        // ================================================================== 3 穿过式位移（幽灵）：落点修正，别人看得到幽灵的最终位置

        private static Fx GhostFixture(Vec2 dummyAt, INavigation2D? nav = null)
        {
            var fx = BuildVolumes(dummyAt, nav: nav);
            fx.Set(FeelFieldNames.DodgeThroughUnits, true);
            SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            return fx;
        }

        /// <summary>窗口 2 个 tick 的冲刺（距离 <paramref name="distance"/>，沿 +x）；返回每个 tick 之后的英雄位置，窗口后再空跑 5 个 tick。</summary>
        private static List<Vec2> RunGhostDash(Fx fx, double distance)
        {
            var motion = Lunge(distance, 0, 2, new Vec2(1, 0), kind: ActionMotionKind.Dash);
            var track = new List<Vec2>();
            for (var i = 0; i < 7; i++)
            {
                fx.Actions.State = i < 2 ? Act(i, motion) : (ActionState?)null;
                fx.Tick();
                track.Add(fx.Pos);
            }

            return track;
        }

        [Fact]
        public void Ghost_PassesThroughDuringTheWindow_ThenLandsOutsideTheVolumeAtTheNearestFeasiblePoint()
        {
            // 冲刺 6.5（2 个 tick，每 tick 3.25），假人在 6.0：窗口内第一个 tick 英雄在 3.25（不受修正），最后一个 tick 的提议终点 6.5 落在假人体积里（深度 1.5）。
            var fx = GhostFixture(new Vec2(6.0, 0));
            var track = RunGhostDash(fx, 6.5);

            Near(3.25, track[0].X, 1e-9);
            Near(0.0, track[0].Y, 1e-9);

            // 就近可行位置：假人体积圆（半径之和 + 一个到达容差）上离提议终点最近的点——提议终点在假人 +x 一侧，所以落在 +x 一侧。
            var expected = new Vec2(6.0 + SumRadius + Pull, 0.0);
            Near(expected.X, track[1].X, 1e-9);
            Near(expected.Y, track[1].Y, 1e-9);
            Assert.True(GapToDummy(fx) >= SumRadius - 1e-9);

            // 落点之后不再被分离推动（本来就在体积外），也不会再动。
            for (var k = 2; k < track.Count; k++) Assert.Equal(track[1], track[k]);

            // unit.moved 事件带的是修正后的最终位置。
            Assert.Equal(expected, fx.Moved.Last(e => e.UnitId.Equals(HeroId)).Position);
            Assert.Equal(new Vec2(6.0, 0), fx.Units.GetPosition(DummyId));
        }

        [Fact]
        public void Ghost_LandingOnTheNearSide_IsCorrectedBackwards_AndAWallBehindMakesItPickTheNearestWalkablePoint()
        {
            // 提议终点 5.5 在假人（6.0）的近侧，深度 1.5：就近可行位置在近侧 6.0 − (2 + 容差)。
            var near = GhostFixture(new Vec2(6.0, 0));
            var nearTrack = RunGhostDash(near, 5.5);
            Near(6.0 - (SumRadius + Pull), nearTrack[1].X, 1e-9);

            // 同一冲刺落在远侧（6.5），但假人身后 x ≥ 8.0 是墙：远侧的径向候选（8.01）不可走，选离终点最近的可走候选——
            // 在假人体积圆（半径 2.01）上、离 (6.5, 0) 最近且 x < 8 的采样点，比绕到近侧（3.99）近得多。
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(8.0, -50), new Vec2(60, 50)) });
            var walled = GhostFixture(new Vec2(6.0, 0), nav);
            var wallTrack = RunGhostDash(walled, 6.5);
            Assert.True(nav.IsWalkable(MapId, wallTrack[1]));
            Near(SumRadius + Pull, GapToDummy(walled), 1e-9); // 恰在体积圆上（贴着体积边界、没有多退）
            var toEnd = (wallTrack[1] - new Vec2(6.5, 0)).Length;
            Assert.True(toEnd < 6.5 - (6.0 - (SumRadius + Pull)) - 1e-6, $"比绕到近侧更近：{toEnd:R}");
            Assert.Equal(wallTrack[1], wallTrack[wallTrack.Count - 1]); // 落点之后不再被推动
        }

        [Fact]
        public void Ghost_AnotherUnitWalkingIntoTheLandingSpot_SeesTheGhostsFinalPosition_AndNobodyOverlaps()
        {
            // 英雄冲刺落在假人身后；第三个单位从更远处迎面走来（走到落点附近）。
            var fx = GhostFixture(new Vec2(6.0, 0));
            var walker = AddExtraUnit(fx, "unit.walker", new Vec2(10.3, 0));
            SetUnitField(fx, walker, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            SetUnitField(fx, HeroId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            var motion = Lunge(6.5, 0, 2, new Vec2(1, 0), kind: ActionMotionKind.Dash);
            var walkerProposal = 10.3;
            for (var i = 0; i < 6; i++)
            {
                fx.Actions.State = i < 2 ? Act(i, motion) : (ActionState?)null;
                SubmitMove(fx, walker, -1, 0);
                if (i < 2) walkerProposal -= Speed * Dt;
                fx.Tick();
                if (i >= 1)
                {
                    // 窗口最后一个 tick 起：三个单位两两不重叠（幽灵落点修正考虑了别人的最终位置，别人也不撞进幽灵的落点）。
                    Assert.True(GapToDummy(fx) >= SumRadius - 1e-9, $"tick {i}：英雄与假人重叠");
                    Assert.True(Gap(fx, HeroId, walker) >= SumRadius - 1e-9, $"tick {i}：英雄与走来的单位重叠 {fx.Pos} / {fx.Units.GetPosition(walker)}");
                    Assert.True((fx.Units.GetPosition(walker) - fx.Units.GetPosition(DummyId)).Length >= SumRadius - 1e-9, $"tick {i}：走来的单位与假人重叠");
                }
            }

            // 复现：走来的单位在窗口最后一个 tick 的提议位置是 10.3 − 0.4 × 2 = 9.5，离默认的远侧落点（假人身后 8.01）只有 1.49，会重叠；
            // 现在幽灵落点避开了它（落到两个体积圆的交点上），走来的单位也没有被拦得更早。
            Assert.True(walkerProposal < 8.0 + SumRadius, "夹具：走来的单位在不被拦的情况下会走进幽灵的远侧落点");
            Assert.True(Gap(fx, HeroId, walker) >= SumRadius - 1e-9);
        }

        [Fact]
        public void Ghost_ThreeGhostsLandingOnEachOther_AreSeparatedDeterministically_WhateverTheCreationOrder()
        {
            List<long[]> Run(IReadOnlyList<string> order)
            {
                var starts = new Dictionary<string, Vec2> { ["u.a"] = new Vec2(0, 0), ["u.b"] = new Vec2(0, 1.5), ["u.c"] = new Vec2(0.5, 3.0) };
                var crowd = BuildCrowd(order, starts, configureFeel: feel =>
                {
                    feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.DodgeThroughUnits, FeelOp.Set, FeelValue.Of(true)));
                    feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.UnitSeparationSpeedRatio, FeelOp.Set, FeelValue.Of(0.0)));
                });
                var motion = Lunge(6.5, 0, 2, new Vec2(1, 0), kind: ActionMotionKind.Dash);
                var rows = new List<long[]>();
                for (var i = 0; i < 4; i++)
                {
                    crowd.Actions.State = i < 2 ? Act(i, motion) : (ActionState?)null;
                    crowd.World.Tick(SimStep.Continuous(Dt));
                    var row = new long[6];
                    for (var k = 0; k < 3; k++)
                    {
                        var p = crowd.Units.GetPosition(new Id(CrowdNames[k]));
                        row[2 * k] = BitConverter.DoubleToInt64Bits(p.X);
                        row[2 * k + 1] = BitConverter.DoubleToInt64Bits(p.Y);
                    }

                    rows.Add(row);
                    if (i >= 1)
                    {
                        for (var x = 0; x < 3; x++)
                        {
                            for (var y = x + 1; y < 3; y++)
                            {
                                var gap = (crowd.Units.GetPosition(new Id(CrowdNames[x])) - crowd.Units.GetPosition(new Id(CrowdNames[y]))).Length;
                                Assert.True(gap >= SumRadius - 1e-9, $"tick {i}：{CrowdNames[x]}/{CrowdNames[y]} 中心距 {gap:R}");
                            }
                        }
                    }
                }

                return rows;
            }

            var baseline = Run(new[] { "u.a", "u.b", "u.c" });
            foreach (var order in new[] { new[] { "u.c", "u.b", "u.a" }, new[] { "u.b", "u.c", "u.a" }, new[] { "u.c", "u.a", "u.b" } })
            {
                var other = Run(order);
                for (var t = 0; t < baseline.Count; t++) Assert.True(baseline[t].SequenceEqual(other[t]), $"创建顺序 {string.Join(",", order)}：第 {t} tick 不一致");
            }
        }

        [Fact]
        public void Ghost_Invariant_TheLandingPointIsNeverInsideAVolume_ForAnyDummyPlacement()
        {
            var cases = 0;
            foreach (var dx in new[] { 3.5, 4.5, 5.5, 6.0, 6.5, 7.0, 8.0, 9.5 })
            {
                foreach (var dy in new[] { -1.5, -0.7, 0.0, 0.4, 1.2, 1.9 })
                {
                    var fx = GhostFixture(new Vec2(dx, dy));
                    var track = RunGhostDash(fx, 6.5);
                    Assert.True(GapToDummy(fx) >= SumRadius - 1e-9, $"假人 ({dx},{dy})：落点 {track[1]} 在体积里（中心距 {GapToDummy(fx):R}）");
                    Assert.Equal(track[1], track[track.Count - 1]); // 落点之后不再被推动
                    Near(0.0, track[0].Y, 1e-9);                      // 窗口内的第一个 tick 不被偏转
                    cases++;
                }
            }

            Assert.Equal(48, cases);
        }

        // ================================================================== 4 被拉回：保留切向速度，路径进度不丢

        [Fact]
        public void PullBack_KeepsTheTangentialVelocity_AndZeroesOnlyTheNormalComponent()
        {
            // 英雄沿 (1, 0.5) 方向走，假人迎面（-x）走来、略偏下：成对接触把两个单位同时拉回；英雄的速度去掉朝向对方的分量，保留沿接触面的切向分量。
            var fx = BuildVolumes(new Vec2(2.4, 0.3));
            SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            SetUnitField(fx, HeroId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            var dir = new Vec2(1.0, 0.5);
            var unitDir = new Vec2(dir.X / dir.Length, dir.Y / dir.Length);
            fx.Move(dir.X, dir.Y);
            SubmitMove(fx, DummyId, -1, 0);
            fx.Tick();

            // 夹具：这个 tick 确实被拉回了（英雄没走满 速率 × 步长）。
            Assert.True(fx.Pos.Length < Speed * Dt - 1e-6, $"夹具：应当被成对拉回：{fx.Pos}");

            // 规则：接触法线 n = 从对方指向本单位；速度 = v − min(0, v·n) n（朝向对方的分量清零，其余保留）。
            var v = new Vec2(unitDir.X * Speed, unitDir.Y * Speed);
            var line = fx.Pos - fx.Units.GetPosition(DummyId);
            var n = new Vec2(line.X / line.Length, line.Y / line.Length);
            var into = v.Dot(n);
            Assert.True(into < 0.0, "夹具：英雄确实在朝对方走");
            var expected = new Vec2(v.X - n.X * into, v.Y - n.Y * into);
            var actual = fx.Mo.Velocity;
            Near(expected.X, actual.X, 0.05);
            Near(expected.Y, actual.Y, 0.05);
            Assert.True(actual.Length > 0.5, $"切向分量被保留（旧实现整体归零）：{actual}");
            Assert.True(Math.Abs(actual.Dot(n)) < 0.05 * Speed, "法向分量已清零");
        }

        [Fact]
        public void PullBack_AnArrivalThatIsPulledBack_IsUndone_ThePathContinues_AndNoArrivalEventIsEmitted()
        {
            // 英雄的新路径（本 tick 才建立）走完只有 0.35、本 tick 就会到达；假人迎面走来：接触把英雄拉回，到达作废。
            var fx = BuildVolumes(new Vec2(2.3, 0));
            SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            SetUnitField(fx, HeroId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            fx.MoveTo(0.35, 0);
            SubmitMove(fx, DummyId, -1, 0);
            fx.Tick();

            Assert.True(fx.Pos.X < 0.35 - 1e-6, $"夹具：英雄被拉回，没走到路径终点：{fx.Pos}");
            var state = fx.Player.MovementState;
            Assert.NotNull(state.CurrentPath); // 路径没丢
            Assert.Equal(MoveMode.Run, state.Mode); // 没有到达：状态不是 Idle
            Assert.DoesNotContain(fx.StateChanged, e => e.UnitId.Equals(HeroId) && e.NewState == MoveMode.Idle.ToString());

            // 假人不再走（意图只提交一次）：路径继续——英雄在下一 tick 继续向终点走，而不是停在拉回的位置。
            var before = fx.Pos.X;
            fx.Tick();
            Assert.True(fx.Pos.X > before - 1e-9);
        }

        [Fact]
        public void PullBack_PathProgressNeverRunsAheadOfThePosition()
        {
            // 手动设定一条三点路径（0,0）→（0.2,0）→（5,0），英雄本 tick 起点在第一个点之后（下标 1）；假人迎面走来，英雄在到达 0.2 之前就被拉回——
            // 路径下标必须仍是 1（0.2 这个路点还没走过），路径对象不变。
            var fx = BuildVolumes(new Vec2(2.15, 0));
            SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            SetUnitField(fx, HeroId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            IReadOnlyList<Vec2> path = new List<Vec2> { new Vec2(0, 0), new Vec2(0.2, 0), new Vec2(5, 0) };
            fx.Player.MovementState = new MovementState(path, MoveMode.Run, false, 1);
            SubmitMove(fx, DummyId, -1, 0);
            fx.Tick();

            Assert.True(fx.Pos.X < 0.2, $"夹具：英雄在 0.2 之前被拉回：{fx.Pos}");
            Assert.Same(path, fx.Player.MovementState.CurrentPath);
            Assert.Equal(1, fx.Player.MovementState.PathIndex);
        }

        // ================================================================== 5 受控位移推人同 tick 生效

        private static Crowd PushCrowd(IReadOnlyList<string> order, IReadOnlyDictionary<string, Vec2> starts) =>
            BuildCrowd(order, starts, configureFeel: feel =>
                feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.UnitSeparationSpeedRatio, FeelOp.Set, FeelValue.Of(0.0))));

        [Fact]
        public void ForcedPush_TheStruckUnitMovesInTheSameTickTheKnockbackIsBlocked_ByTheRuleDerivedAmount()
        {
            var fx = BuildVolumes(new Vec2(5.0, 0));
            SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            fx.Set(FeelFieldNames.ForcedPushUnits, true);
            const double distance = 8.0;
            const double duration = 0.4;
            fx.Host.BeginKnockback(new KnockbackRequest(HeroId, new Vec2(1, 0), distance, duration));
            var blockedTick = -1;
            var dummyAtBlock = 0.0;
            for (var tick = 0; tick < 10 && blockedTick < 0; tick++)
            {
                fx.Tick();
                if (fx.Stops.Any(s => s.Reason == MoveStopReason.DisplacementBlocked))
                {
                    blockedTick = tick;
                    dummyAtBlock = fx.Units.GetPosition(DummyId).X;
                }
            }

            Assert.True(blockedTick >= 0, "夹具：击退撞上了假人");

            // 复现：旧实现里假人要到下一 tick 才开始动（此刻仍在 5.0）；现在同一 tick 就得到位移。
            // 规则：转移量 = 撞停时剩余位移 × forced_push_ratio（无抗性）；同 tick 内被推单位走 min(转移量, 速度 × dt)。
            // 击退是 ease_out 曲线位移（总时长 0.4 秒走 8）：推人沿用同一条曲线，时长按转移距离占总距离的比例缩短；第一个 tick 走 曲线(dt / 时长) × 转移量。
            var remaining = distance - fx.Pos.X;
            var transfer = remaining * MotionProfile.DefaultForcedPushRatio;
            var pushDuration = duration * (transfer / distance);
            var progress = Math.Min(1.0, Dt / pushDuration);
            Near(transfer * (1.0 - (1.0 - progress) * (1.0 - progress)), dummyAtBlock - 5.0, 1e-9);
            Assert.True(dummyAtBlock > 5.0 + 1e-9);

            // 事件：这个 tick 的 unit.moved 里已经有假人的新位置。
            Assert.Contains(fx.Moved, e => e.UnitId.Equals(DummyId) && e.Position.Equals(fx.Units.GetPosition(DummyId)));
            AssertNeverOverlaps(fx, "同 tick 推人之后");

            // 不变量：之后继续推进到总转移量，且从不重叠。
            for (var tick = 0; tick < 20; tick++)
            {
                fx.Tick();
                AssertNeverOverlaps(fx, $"推人之后 tick {tick}");
            }

            Near(5.0 + transfer, fx.Units.GetPosition(DummyId).X, 1e-6);
        }

        [Fact]
        public void ForcedPush_AChainOfUnits_IsPushedInOneTick_AndTheResultDoesNotDependOnCreationOrder()
        {
            // 英雄击退撞上 B，B 被推出的位移（同 tick）又撞上紧跟其后的 C：整条链在同一个 tick 里生效。
            var starts = new Dictionary<string, Vec2> { ["u.a"] = new Vec2(0, 0), ["u.b"] = new Vec2(5.0, 0), ["u.c"] = new Vec2(7.1, 0) };
            (List<Vec2[]> Rows, int ChainTick) Run(IReadOnlyList<string> order)
            {
                var crowd = PushCrowd(order, starts);
                var a = new Id("u.a");
                var b = new Id("u.b");
                var c = new Id("u.c");
                crowd.Host.BeginKnockback(new KnockbackRequest(a, new Vec2(1, 0), 8.0, 0.4));
                var rows = new List<Vec2[]>();
                var chainTick = -1;
                for (var tick = 0; tick < 12; tick++)
                {
                    crowd.World.Tick(SimStep.Continuous(Dt));
                    var p = new[] { crowd.Units.GetPosition(a), crowd.Units.GetPosition(b), crowd.Units.GetPosition(c) };
                    rows.Add(p);
                    if (chainTick < 0 && p[2].X > 7.1 + 1e-9) chainTick = tick;
                    for (var x = 0; x < 3; x++)
                    {
                        for (var y = x + 1; y < 3; y++)
                        {
                            Assert.True((p[x] - p[y]).Length >= SumRadius - 1e-9, $"tick {tick}：{x}/{y} 重叠");
                        }
                    }
                }

                return (rows, chainTick);
            }

            var baseline = Run(new[] { "u.a", "u.b", "u.c" });
            // 撞停发生在英雄第一次碰到 B 的那个 tick：B、C 在同一 tick 动起来（旧实现 B 下一 tick 才动，C 还要再晚一 tick）。
            var firstBMove = baseline.Rows.FindIndex(r => r[1].X > 5.0 + 1e-9);
            Assert.True(firstBMove >= 0);
            Assert.Equal(firstBMove, baseline.ChainTick);

            foreach (var order in new[] { new[] { "u.c", "u.b", "u.a" }, new[] { "u.b", "u.a", "u.c" }, new[] { "u.c", "u.a", "u.b" } })
            {
                var other = Run(order);
                Assert.Equal(baseline.Rows.Count, other.Rows.Count);
                for (var t = 0; t < baseline.Rows.Count; t++)
                {
                    for (var k = 0; k < 3; k++)
                    {
                        Assert.Equal(baseline.Rows[t][k], other.Rows[t][k]);
                    }
                }
            }
        }

        // ================================================================== 6 宽相网格：与暴力两两比较逐位一致，n=200 耗时对比

        private static (List<long[]> Track, List<string> Events, double Seconds) RunBigCrowd(int n, int ticks, bool brute, int knockbacks = 6)
        {
            var names = Enumerable.Range(0, n).Select(i => $"u.{i:D3}").ToList();
            var cols = (int)Math.Ceiling(Math.Sqrt(n));
            var starts = new Dictionary<string, Vec2>();
            for (var i = 0; i < n; i++)
            {
                var h = Hash(names[i], 1);
                // 间距 1.6 的格点加抖动：相邻单位的体积（半径之和 2）互相重叠，到处有接触与分离。
                starts[names[i]] = new Vec2((i % cols) * 1.6 + Unit01(h) * 0.5, (i / cols) * 1.6 + Unit01(h * 7u + 1u) * 0.5);
            }

            var crowd = BuildCrowd(names, starts);
            crowd.Handler.VolumeBroadPhaseBruteForce = brute;
            var events = new List<string>();
            crowd.Bus.Subscribe(CarriersEventKeys.UnitMoved, ev =>
            {
                var m = (UnitMovedEvent)ev;
                events.Add($"moved {m.UnitId} {BitConverter.DoubleToInt64Bits(m.Position.X)} {BitConverter.DoubleToInt64Bits(m.Position.Y)}");
            });
            var ids = names.Select(x => new Id(x)).ToList();
            var track = new List<long[]>();
            var watch = new Stopwatch();
            for (var tick = 0; tick < ticks; tick++)
            {
                for (var i = 0; i < n; i++)
                {
                    var h = Hash(names[i], tick / 4);
                    if (i % 4 == 3) continue; // 四分之一的单位原地不动（静止障碍）
                    var angle = Unit01(h) * 2.0 * Math.PI;
                    crowd.World.SubmitIntent(new Intent(ids[i], "move", new JsonObjectBuilder()
                        .Add("dx", new JsonNumber(Math.Cos(angle))).Add("dy", new JsonNumber(Math.Sin(angle))).Add("mode", new JsonString("Run")).Build()));
                }

                if (tick == 5)
                {
                    for (var k = 0; k < knockbacks; k++)
                    {
                        var h = Hash(names[(k * 17) % n], 99);
                        var angle = Unit01(h) * 2.0 * Math.PI;
                        crowd.Host.BeginKnockback(new KnockbackRequest(ids[(k * 17) % n], new Vec2(Math.Cos(angle), Math.Sin(angle)), 4.0, 0.4));
                    }
                }

                watch.Start();
                crowd.World.Tick(SimStep.Continuous(Dt));
                watch.Stop();
                var row = new long[n * 2];
                for (var i = 0; i < n; i++)
                {
                    var p = crowd.Units.GetPosition(ids[i]);
                    row[2 * i] = BitConverter.DoubleToInt64Bits(p.X);
                    row[2 * i + 1] = BitConverter.DoubleToInt64Bits(p.Y);
                }

                track.Add(row);
                events.Add($"-- tick {tick}");
            }

            return (track, events, watch.Elapsed.TotalSeconds);
        }

        [Theory]
        [InlineData(40, 60)]
        [InlineData(120, 30)]
        public void BroadPhase_GridResultIsBitIdenticalToBruteForcePairs_TracksAndEvents(int n, int ticks)
        {
            var grid = RunBigCrowd(n, ticks, brute: false);
            var brute = RunBigCrowd(n, ticks, brute: true);
            Assert.Equal(brute.Track.Count, grid.Track.Count);
            for (var t = 0; t < brute.Track.Count; t++)
            {
                Assert.True(brute.Track[t].SequenceEqual(grid.Track[t]), $"第 {t} tick 网格宽相与暴力两两比较的位置不是逐位一致");
            }

            Assert.Equal(brute.Events, grid.Events);
            Assert.True(grid.Events.Count(e => e.StartsWith("moved", StringComparison.Ordinal)) > n, "场景发生过大量移动事件（防空判）");
        }

        [Fact]
        public void BroadPhase_TwoHundredUnits_TimingIsReported_AndResultsAreIdentical()
        {
            // 耗时只报告、不断言：墙钟断言在并行负载下不稳（整套门禁并行跑时会出现假红）；逐位一致才是断言。
            // 预热（JIT）后交替各测三轮取最小值，降低噪声。
            const int n = 200;
            const int ticks = 20;
            RunBigCrowd(n, 3, brute: false);
            RunBigCrowd(n, 3, brute: true);
            var grid = RunBigCrowd(n, ticks, brute: false);
            var brute = RunBigCrowd(n, ticks, brute: true);
            var bestGrid = double.MaxValue;
            var bestBrute = double.MaxValue;
            for (var round = 0; round < 3; round++)
            {
                grid = RunBigCrowd(n, ticks, brute: false);
                brute = RunBigCrowd(n, ticks, brute: true);
                bestGrid = Math.Min(bestGrid, grid.Seconds);
                bestBrute = Math.Min(bestBrute, brute.Seconds);
            }

            _output.WriteLine($"n={n}, {ticks} ticks（三轮最小值）：网格宽相 {bestGrid * 1000.0:F1} ms，暴力两两 {bestBrute * 1000.0:F1} ms，比值 {bestBrute / bestGrid:F2}");
            Assert.Equal(brute.Track.Count, grid.Track.Count);
            for (var t = 0; t < brute.Track.Count; t++) Assert.True(brute.Track[t].SequenceEqual(grid.Track[t]), $"n=200 第 {t} tick 不一致");
            Assert.Equal(brute.Events, grid.Events);

            // 随规模的趋势（只报告，不断言）：暴力是 O(n²)，网格近似线性。
            foreach (var bigger in new[] { 100, 400 })
            {
                var g = RunBigCrowd(bigger, 10, brute: false);
                var b = RunBigCrowd(bigger, 10, brute: true);
                _output.WriteLine($"n={bigger}, 10 ticks：网格宽相 {g.Seconds * 1000.0:F1} ms，暴力两两 {b.Seconds * 1000.0:F1} ms，比值 {b.Seconds / g.Seconds:F2}");
            }
        }
    }
}
