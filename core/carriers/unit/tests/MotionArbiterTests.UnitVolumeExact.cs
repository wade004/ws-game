// 单位间体积阻挡的"成对阻挡精确化"（M4-B，手感设计/02 第 3.5 节、ADR-0128 追加决定）的运行时冒烟：解除 1.95.0 留下的四条已知限制，
// 每条一个复现（量从 X 变到 Y，期望值由规则算出、不写死裸数）加一条不变量（任意处理顺序下成立）：
//   1 跟随者不再滞后一个 tick（同 tick 内按最终位置求解）  2 滑动/折线位移的成对撞停按实际折线逐段扫掠
//   3 位移到达/受阻事件延后到成对裁决之后、携带最终位置  4 追击与扑向目标读 tick 起点快照（与处理顺序无关）
using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Feel;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Unit
{
    public partial class MotionArbiterTests
    {
        private static long Bits(double v) => BitConverter.DoubleToInt64Bits(v);

        private static void SetSpeed(Fx fx, Id unit, double speed) =>
            fx.Stats.SetBase(unit, new MovementOptions().MoveSpeedStat, speed);

        // ================================================================== 1 跟随者不再滞后一个 tick

        /// <summary>英雄在原点、假人在 <paramref name="gap"/> 处，都朝 +x 走；<paramref name="heroSpeed"/> 是英雄的移动速度属性。</summary>
        private static (List<Vec2> Hero, List<Vec2> Dummy) FollowRun(double gap, double heroSpeed, int ticks, bool withNeighbour = true)
        {
            var fx = BuildVolumes(withNeighbour ? new Vec2(gap, 0) : new Vec2(100, 100));
            SetSpeed(fx, HeroId, heroSpeed);
            if (!withNeighbour) fx.Player.Position = new Vec2(0, 0);
            var hero = new List<Vec2>();
            var dummy = new List<Vec2>();
            for (var tick = 0; tick < ticks; tick++)
            {
                fx.Move(1, 0);
                if (withNeighbour) SubmitMove(fx, DummyId, 1, 0);
                fx.Tick();
                hero.Add(fx.Pos);
                dummy.Add(fx.Units.GetPosition(DummyId));
            }

            return (hero, dummy);
        }

        private static List<Vec2> LeaderAlone(double gap, int ticks)
        {
            var fx = BuildVolumes(new Vec2(gap, 0));
            fx.Player.Position = new Vec2(100, 100); // 英雄走开，假人一个人走
            var track = new List<Vec2>();
            for (var tick = 0; tick < ticks; tick++)
            {
                SubmitMove(fx, DummyId, 1, 0);
                fx.Tick();
                track.Add(fx.Units.GetPosition(DummyId));
            }

            return track;
        }

        [Fact]
        public void SameTickSolve_AFollowerAtTheSameSpeed_KeepsPaceWithTheLeader_NoOneTickLag()
        {
            // 复现：英雄紧跟在假人身后（中心距比半径之和多 0.1），同速同向。旧实现里英雄只看得到假人 tick 起点的位置，
            // 第一个 tick 就被挡在"起点位置 − 半径之和"，之后每 tick 都比假人落后；现在两个单位的位移相同，中心距恒定、英雄轨迹与独自行走逐位一致。
            const double gap = SumRadius + 0.1;
            var (hero, dummy) = FollowRun(gap, Speed, 30);
            var (alone, _) = FollowRun(gap, Speed, 30, withNeighbour: false);
            for (var tick = 0; tick < hero.Count; tick++)
            {
                Near(gap, dummy[tick].X - hero[tick].X, 1e-9);
                Assert.True(
                    Bits(alone[tick].X) == Bits(hero[tick].X) && Bits(alone[tick].Y) == Bits(hero[tick].Y),
                    $"tick {tick}：同速跟随者的轨迹应与独自行走逐位一致（没有被前面的单位拖慢）");
            }

            // 场景确实在走（防空判）。
            Assert.True(hero[hero.Count - 1].X > 5 * Speed * Dt);
        }

        [Fact]
        public void SameTickSolve_AFasterFollower_CatchesUpToTheLeadersFinalPosition_AndNeverThrottlesTheLeader()
        {
            // 复现：英雄速度属性 6 追假人（4）。追上后英雄贴在假人"本 tick 最终位置"的体积边界外一个到达容差处；
            // 旧实现贴的是假人 tick 起点位置，所以会多落后一个假人步长（Speed × Dt）。
            var (hero, dummy) = FollowRun(SumRadius + 0.5, 6.0, 60);
            var leaderAlone = LeaderAlone(SumRadius + 0.5, 60);
            for (var tick = 0; tick < hero.Count; tick++)
            {
                // 不变量 a：任意 tick 中心距不小于半径之和。
                Assert.True((dummy[tick] - hero[tick]).Length >= SumRadius - 1e-9, $"tick {tick}：英雄撞进了假人的体积");
                // 不变量 b：前面的单位不被后面追上来的单位拖慢——领头单位的轨迹与它独自行走逐位一致。
                Assert.True(
                    Bits(leaderAlone[tick].X) == Bits(dummy[tick].X) && Bits(leaderAlone[tick].Y) == Bits(dummy[tick].Y),
                    $"tick {tick}：领头单位被追上来的跟随者拖慢了");
            }

            for (var tick = 40; tick < hero.Count; tick++)
            {
                var g = (dummy[tick] - hero[tick]).Length;
                Assert.InRange(g, SumRadius - 1e-9, SumRadius + Pull + 1e-6); // 贴着最终位置，不是落后一个步长（Speed × Dt = 0.4）
            }
        }

        [Fact]
        public void SameTickSolve_TwoUnitsCrossingAtDifferentSpeeds_NeverOverlap_AndTheFastOneYields()
        {
            // 垂直交叉：英雄 +x、假人 +y，两条路线在 (4,0) 附近相交；跑得快的让、跑得慢的继续走，任意 tick 都不重叠。
            var fx = BuildVolumes(new Vec2(3.0, -3.0));
            SetSpeed(fx, HeroId, 5.0);
            SetSpeed(fx, DummyId, 3.0);
            var dummyAlone = BuildVolumes(new Vec2(3.0, -3.0));
            dummyAlone.Player.Position = new Vec2(100, 100);
            SetSpeed(dummyAlone, DummyId, 3.0);
            for (var tick = 0; tick < 30; tick++)
            {
                fx.Move(1, 0);
                SubmitMove(fx, DummyId, 0, 1);
                fx.Tick();
                SubmitMove(dummyAlone, DummyId, 0, 1);
                dummyAlone.Tick();
                AssertNeverOverlaps(fx, $"交叉 tick {tick}");
            }

            // 英雄从一开始就比假人更快到达交叉点：假人在英雄身后的路线上，是被追的一方？——不依赖谁让，只要求最终没有叠起来、
            // 且两个单位都往各自的方向走了（不是互相卡死）。
            Assert.True(fx.Pos.X > 1.0);
            Assert.True(fx.Units.GetPosition(DummyId).Y > -3.0 + 1.0);
        }

        // ================================================================== 2 折线逐段精确扫掠（成对阶段）

        private static Fx WallSlideRunMoving(Vec2 dummyAt, Vec2 dummyDirection, double dummySpeed, ActionMotionBlocking blocking = ActionMotionBlocking.Slide)
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(3, -50), new Vec2(4, 50)) });
            var fx = BuildVolumes(dummyAt, wallSlide: true, nav: nav);
            fx.Player.Position = new Vec2(2.0, 0.0);
            SetSpeed(fx, DummyId, dummySpeed);
            var c = 1.0 / Math.Sqrt(2.0);
            var motion = Lunge(4.0, 0, 1, new Vec2(c, c), blocking: blocking);
            for (var i = 0; i < 3; i++)
            {
                fx.Actions.State = i < motion.EndTick + 2 ? Act(i, motion) : (ActionState?)null;
                SubmitMove(fx, DummyId, dummyDirection.X, dummyDirection.Y);
                fx.Tick();
            }

            return fx;
        }

        [Fact]
        public void ExactPolylineSweep_AgainstAMovingUnit_FollowsTheActualTwoSegmentPath_NotTheChord()
        {
            var (via, end) = WallSlideGeometry();

            // 复现 1（不误拦）：假人在慢慢走开；它只和起终点的弦相交、折线两段都碰不到（本 tick 内也不会碰到）——
            // 旧实现用弦检测，把英雄拉回；现在放行，英雄走完规则算出的折线终点。
            var chordOnly = new Vec2(0.9, 1.9);
            Assert.True(DistanceToSegment(chordOnly, new Vec2(2.0, 0.0), end) < SumRadius, "夹具：弦确实穿过体积");
            var passed = WallSlideRunMoving(chordOnly, new Vec2(-1, 0), 1.0);
            Near(end.X, passed.Pos.X, 1e-9);
            Near(end.Y, passed.Pos.Y, 1e-9);
            AssertNeverOverlaps(passed, "折线放行");

            // 复现 2（不漏拦）：假人只和第二段（沿墙滑的那段）相交，弦碰不到——折线上第二段的接触必须拦住，英雄停在体积边界外。
            var secondLegOnly = new Vec2(4.95, 2.2);
            Assert.True(DistanceToSegment(secondLegOnly, new Vec2(2.0, 0.0), end) >= SumRadius, "夹具：弦碰不到体积");
            Assert.True(DistanceToSegment(secondLegOnly, via, end) < SumRadius, "夹具：第二段碰得到体积");
            var blocked = WallSlideRunMoving(secondLegOnly, new Vec2(0, 1), 1.0);
            Assert.True(blocked.Pos.Y < end.Y - 0.05, $"没有被拦住：停在 {blocked.Pos}");
            AssertNeverOverlaps(blocked, "折线拦截");
            // 英雄被拦住之后假人还在走开（3 个 tick × 速度 1 × 步长）：中心距最多比体积边界外一个容差再多这么一点。
            Assert.InRange(GapToDummy(blocked), SumRadius - 1e-9, SumRadius + Pull + 3 * 1.0 * Dt + 1e-6);
        }

        [Theory]
        [InlineData(0.9, 1.9, -1.0, 0.0)]
        [InlineData(4.95, 2.2, 0.0, 1.0)]
        [InlineData(4.95, 2.2, 0.0, -1.0)]
        [InlineData(1.0, 3.5, 1.0, 0.0)]
        [InlineData(5.5, 1.0, -1.0, 0.0)]
        [InlineData(2.0, 2.0, 0.5, 0.5)]
        [InlineData(3.5, 3.0, -1.0, -1.0)]
        public void ExactPolylineSweep_AgainstAMovingUnit_NeverEndsInsideTheVolume_ForAnyPlacementAndDirection(double x, double y, double dx, double dy)
        {
            foreach (var blocking in new[] { ActionMotionBlocking.Slide, ActionMotionBlocking.Stop })
            {
                var fx = WallSlideRunMoving(new Vec2(x, y), new Vec2(dx, dy), 1.0, blocking);
                var startGap = (new Vec2(2.0, 0.0) - new Vec2(x, y)).Length;
                Assert.True(
                    GapToDummy(fx) >= Math.Min(SumRadius, startGap) - 1e-9,
                    $"({x},{y}) 方向 ({dx},{dy}) {blocking}：终点中心距 {GapToDummy(fx):R}");
            }
        }

        // ================================================================== 3 位移事件延后到成对裁决之后

        private sealed class StopLog
        {
            public readonly List<(int Tick, Id Unit, MoveStopReason Reason, Vec2 Pos)> Events = new List<(int, Id, MoveStopReason, Vec2)>();
        }

        private static StopLog LogStops(Fx fx, Func<int> tick)
        {
            var log = new StopLog();
            fx.Host.OnMoveStopped += (id, pos, reason) => log.Events.Add((tick(), id, reason, pos));
            return log;
        }

        /// <summary>
        /// 英雄被击退走向假人（假人在 <paramref name="dummyX"/>，同时被击退走向英雄）：两个位移都会在同一 tick 的成对裁决里被拉回。
        /// 返回事件日志，以及每个 tick 末两个单位的位置。
        /// </summary>
        private static (StopLog Log, List<(Vec2 Hero, Vec2 Dummy)> Ends) KnockTowardEachOther(double dummyX, double distance)
        {
            var fx = BuildVolumes(new Vec2(dummyX, 0));
            foreach (var id in new[] { HeroId, DummyId }) SetUnitField(fx, id, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            var tick = 0;
            var log = LogStops(fx, () => tick);
            fx.Host.BeginKnockback(new KnockbackRequest(HeroId, new Vec2(1, 0), distance, 0.3));
            fx.Host.BeginKnockback(new KnockbackRequest(DummyId, new Vec2(-1, 0), distance, 0.3));
            var ends = new List<(Vec2, Vec2)>();
            for (tick = 0; tick < 12; tick++)
            {
                fx.Tick();
                ends.Add((fx.Pos, fx.Units.GetPosition(DummyId)));
            }

            return (log, ends);
        }

        [Fact]
        public void DeferredStopEvents_AreEmittedAfterThePairwiseAdjudication_WithTheFinalPosition()
        {
            // 复现：两个单位相向（中心距 3.5）被击退各 3，位移结束时两个提议终点（英雄 x = 3.0、假人 x = 0.5）互相穿过，
            // 成对裁决把它们拉回体积边界。事件里的位置必须是拉回之后的位置（旧实现是拉回之前的提议终点，两个单位叠在一起）。
            var (log, ends) = KnockTowardEachOther(3.5, 3.0);
            Assert.True(log.Events.Count >= 2, "两个位移都应当结束并各发一条事件");
            foreach (var e in log.Events)
            {
                var (hero, dummy) = ends[e.Tick];
                var finalPos = e.Unit.Equals(HeroId) ? hero : dummy;
                Assert.True(
                    Bits(finalPos.X) == Bits(e.Pos.X) && Bits(finalPos.Y) == Bits(e.Pos.Y),
                    $"tick {e.Tick} {e.Unit} {e.Reason}：事件位置 {e.Pos} 不是 tick 末的最终位置 {finalPos}");
                Assert.True((ends[e.Tick].Hero - ends[e.Tick].Dummy).Length >= SumRadius - 1e-9, "事件发出时两个单位不重叠");
            }

            // 拉回确实发生了：英雄的最终位置没有到提议的终点 4。
            var last = ends[ends.Count - 1];
            Assert.True(last.Hero.X < 3.0 - 0.5, $"英雄应被拉回：{last.Hero}");
            Assert.True(last.Dummy.X > 0.5 + 0.5, $"假人应被拉回：{last.Dummy}");
        }

        [Theory]
        [InlineData(6.0, 4.0)]
        [InlineData(7.0, 5.0)]
        [InlineData(9.0, 3.0)]
        [InlineData(5.0, 0.5)]
        [InlineData(3.5, 3.0)]
        [InlineData(4.0, 3.0)]
        [InlineData(4.5, 3.0)]
        [InlineData(5.0, 3.0)]
        [InlineData(5.5, 2.5)]
        [InlineData(6.0, 3.0)]
        [InlineData(8.0, 5.0)]
        public void DeferredStopEvents_EveryEventCarriesTheUnitsFinalPositionOfThatTick_InIdOrder(double dummyX, double distance)
        {
            var (log, ends) = KnockTowardEachOther(dummyX, distance);
            foreach (var group in log.Events.GroupBy(e => e.Tick))
            {
                var ids = group.Select(e => e.Unit).ToList();
                var sorted = ids.OrderBy(x => x).ToList();
                Assert.True(ids.SequenceEqual(sorted), $"tick {group.Key}：同 tick 的事件应按单位 id 排序");
                foreach (var e in group)
                {
                    var (hero, dummy) = ends[e.Tick];
                    var finalPos = e.Unit.Equals(HeroId) ? hero : dummy;
                    Assert.True(Bits(finalPos.X) == Bits(e.Pos.X) && Bits(finalPos.Y) == Bits(e.Pos.Y), $"tick {e.Tick} {e.Unit}");
                }
            }

            foreach (var (hero, dummy) in ends) Assert.True((hero - dummy).Length >= Math.Min(SumRadius, Math.Abs(dummyX)) - 1e-9);
        }

        [Fact]
        public void DeferredStopEvents_UnitsWithoutVolume_StillGetTheEventsImmediately_AtTheCurrentPosition()
        {
            // 没声明体积的世界：事件与既有实现一致（立即发出）。位置 = 位移结束时单位的位置 = tick 末位置。
            var fx = BuildVolumes(new Vec2(50, 50), volumes: false);
            var tick = 0;
            var log = LogStops(fx, () => tick);
            fx.Host.BeginKnockback(new KnockbackRequest(HeroId, new Vec2(1, 0), 2.0, 0.3));
            for (tick = 0; tick < 6; tick++) fx.Tick();
            var arrived = log.Events.Single();
            Assert.Equal(MoveStopReason.DisplacementArrived, arrived.Reason);
            Near(2.0, arrived.Pos.X, 1e-9);
        }

        // ================================================================== 4 追击与扑向目标读 tick 起点快照

        private static Crowd BuildChasePair(IReadOnlyList<string> creationOrder)
        {
            var start = new Dictionary<string, Vec2> { ["u.a"] = new Vec2(0, 0), ["u.b"] = new Vec2(10, 0) };
            return BuildCrowd(creationOrder, start);
        }

        private static List<Vec2> ChaseTrack(IReadOnlyList<string> creationOrder, IReadOnlyList<string> submitOrder, int ticks)
        {
            var crowd = BuildChasePair(creationOrder);
            var a = new Id("u.a");
            var b = new Id("u.b");
            var track = new List<Vec2>();
            for (var tick = 0; tick < ticks; tick++)
            {
                foreach (var name in submitOrder)
                {
                    if (name == "u.a" && tick == 0)
                    {
                        crowd.World.SubmitIntent(new Intent(a, "move_to_unit", new JsonObjectBuilder()
                            .Add("targetUnitId", new JsonString(b.ToString())).Add("stopRange", new JsonNumber(0.5))
                            .Add("mode", new JsonString("Run")).Build()));
                    }
                    else if (name == "u.b")
                    {
                        SubmitDirection(crowd, b, 0.0, 1.0);
                    }
                }

                crowd.World.Tick(SimStep.Continuous(Dt));
                track.Add(crowd.Units.GetPosition(a));
            }

            return track;
        }

        private static void SubmitDirection(Crowd crowd, Id unit, double dx, double dy) =>
            crowd.World.SubmitIntent(new Intent(unit, "move", new JsonObjectBuilder()
                .Add("dx", new JsonNumber(dx)).Add("dy", new JsonNumber(dy)).Add("mode", new JsonString("Run")).Build()));

        [Fact]
        public void StartSnapshotTargets_AChaserAimsAtTheTargetsTickStartPosition_WhateverTheProcessingOrder()
        {
            // 复现：追击者在 (0,0)，目标在 (10,0)、每 tick 朝 +y 走。追击者这个 tick 瞄的是目标 tick 起点的位置 (10,0)，
            // 所以第一个 tick 追击者没有任何 y 位移（旧实现里目标先处理的顺序下，追击者会瞄到已经走动了的位置、产生 y 位移）。
            var combos = new[]
            {
                (new[] { "u.a", "u.b" }, new[] { "u.a", "u.b" }),
                (new[] { "u.b", "u.a" }, new[] { "u.a", "u.b" }),
                (new[] { "u.a", "u.b" }, new[] { "u.b", "u.a" }),
                (new[] { "u.b", "u.a" }, new[] { "u.b", "u.a" }),
            };
            var tracks = combos.Select(c => ChaseTrack(c.Item1, c.Item2, 40)).ToList();
            foreach (var track in tracks)
            {
                Assert.Equal(0.0, track[0].Y);
                Assert.True(track[0].X > 0.0);
            }

            // 不变量：创建顺序与意图提交顺序的四种组合，追击者轨迹逐位一致。
            for (var k = 1; k < tracks.Count; k++)
            {
                for (var tick = 0; tick < tracks[0].Count; tick++)
                {
                    Assert.True(
                        Bits(tracks[0][tick].X) == Bits(tracks[k][tick].X) && Bits(tracks[0][tick].Y) == Bits(tracks[k][tick].Y),
                        $"组合 {k} 第 {tick} tick 的追击者位置与基准不是逐位一致");
                }
            }

            // 场景确实追了一段（防空判）：追击者一路朝目标走、y 方向逐步跟上。
            Assert.True(tracks[0][39].Y > 0.5);
        }

        [Fact]
        public void StartSnapshotTargets_ATowardTargetChargeAimsAtTheTargetsTickStartPosition_EvenIfTheTargetWasDisplacedFirst()
        {
            // 复现：英雄朝假人（在 (6,0)）冲锋；同一 tick 假人被击退向 +y（受控位移先于动作位移结算）。
            // 英雄瞄的是假人 tick 起点的位置 (6,0)，所以第一个 tick 没有 y 位移；之后每 tick 都瞄上一 tick 末的位置。
            var fx = BuildVolumes(new Vec2(6, 0));
            SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            fx.Host.BeginKnockback(new KnockbackRequest(DummyId, new Vec2(0, 1), 3.0, 0.3));
            var motion = Lunge(
                8.0, 0, 8, new Vec2(1, 0), kind: ActionMotionKind.Charge, target: DummyId, stopDistance: 0.0,
                dir: ActionMotionDirection.TowardTarget, maxTurnDeg: 90);
            fx.Actions.State = Act(0, motion);
            fx.Tick();
            Assert.Equal(0.0, fx.Pos.Y);
            Assert.True(fx.Pos.X > 0.0);
            Assert.True(fx.Units.GetPosition(DummyId).Y > 0.0, "夹具：假人这个 tick 确实被击退走动了");
            var heroAfterTick0 = fx.Pos;

            fx.Actions.State = Act(1, motion);
            fx.Tick();
            Assert.True(fx.Pos.Y > heroAfterTick0.Y, "第二个 tick 起英雄瞄向假人上一 tick 末的位置（y 已大于 0），开始朝 +y 偏转");
        }

        // ================================================================== 顺序无关：覆盖新路径的人群

        private static readonly string[] RichNames = { "u.a", "u.b", "u.c", "u.d", "u.e", "u.f" };

        /// <summary>
        /// 比 <c>RunCrowd</c> 更全的人群：有体积、墙滑动（折线）、追击、受控位移（击退）、朝目标冲锋；
        /// 返回每 tick 每个单位位置的逐位表示，以及按发出顺序的位移停止事件（含位置的逐位表示）。
        /// </summary>
        private static (List<long[]> Track, List<string> Events, int Chases, int Stops) RunRichCrowd(uint permSeed)
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(4.0, -50), new Vec2(4.6, 50)) });
            var order = permSeed == 0 ? RichNames.ToList() : Shuffled(RichNames, permSeed);
            var crowd = BuildCrowd(order, CrowdStart(), nav);
            var ids = RichNames.ToDictionary(n => n, n => new Id(n));
            var events = new List<string>();
            var tickNow = 0;
            crowd.Host.OnMoveStopped += (id, pos, reason) =>
                events.Add($"{tickNow}|{id}|{reason}|{Bits(pos.X)}|{Bits(pos.Y)}");
            var charge = Lunge(
                9.0, 0, 20, new Vec2(1, 0), kind: ActionMotionKind.Charge, target: ids["u.d"], stopDistance: 0.0,
                dir: ActionMotionDirection.TowardTarget, maxTurnDeg: 90);
            var track = new List<long[]>();
            var prevGaps = new Dictionary<(int, int), double>();
            for (var tick = 0; tick < 100; tick++)
            {
                tickNow = tick;
                var submitOrder = permSeed == 0 ? RichNames.ToList() : Shuffled(RichNames, permSeed * 31u + (uint)tick);
                foreach (var name in submitOrder)
                {
                    var idx = Array.IndexOf(RichNames, name);
                    var id = ids[name];
                    switch (idx)
                    {
                        case 0: // u.a 走向远点
                            if (tick == 0 || tick == 50)
                            {
                                var h = Hash(name, tick);
                                crowd.World.SubmitIntent(new Intent(id, "move", new JsonObjectBuilder()
                                    .Add("x", new JsonNumber(-2.0 + Unit01(h) * 10.0)).Add("y", new JsonNumber(-3.0 + Unit01(h * 7u + 1u) * 8.0))
                                    .Add("mode", new JsonString("Run")).Build()));
                            }

                            break;
                        case 1: // u.b 追 u.a
                        case 4: // u.e 追 u.c
                            if (tick == 0 || tick == 60)
                            {
                                var target = idx == 1 ? ids["u.a"] : ids["u.c"];
                                crowd.World.SubmitIntent(new Intent(id, "move_to_unit", new JsonObjectBuilder()
                                    .Add("targetUnitId", new JsonString(target.ToString())).Add("stopRange", new JsonNumber(0.5))
                                    .Add("mode", new JsonString("Run")).Build()));
                            }

                            break;
                        default: // u.c u.d u.f 随机转向
                        {
                            var h = Hash(name, tick / 5);
                            var angle = Unit01(h) * 2.0 * Math.PI;
                            SubmitDirection(crowd, id, Math.Cos(angle), Math.Sin(angle));
                            break;
                        }
                    }

                    if ((tick == 15 || tick == 70) && (name == "u.a" || name == "u.c"))
                    {
                        var toward = name == "u.a" ? new Vec2(1, 0.2) : new Vec2(0.4, -1);
                        crowd.Host.BeginKnockback(new KnockbackRequest(id, toward, 4.0, 0.3));
                    }
                }

                // u.f 在 10..29 tick 朝 u.d 冲锋（动作位移）；动作只属于 u.f。
                crowd.Actions.Owner = ids["u.f"];
                crowd.Actions.State = tick >= 10 && tick < 30 ? Act(tick - 10, charge) : (ActionState?)null;

                crowd.World.Tick(SimStep.Continuous(Dt));

                var row = new long[RichNames.Length * 2];
                for (var k = 0; k < RichNames.Length; k++)
                {
                    var p = crowd.Units.GetPosition(ids[RichNames[k]]);
                    row[2 * k] = Bits(p.X);
                    row[2 * k + 1] = Bits(p.Y);
                }

                track.Add(row);

                // 不变量：任意两个单位的中心距不比上一 tick 的 min(半径之和, 上一 tick 的距离) 更小。
                for (var x = 0; x < RichNames.Length; x++)
                {
                    for (var y = x + 1; y < RichNames.Length; y++)
                    {
                        var gap = (crowd.Units.GetPosition(ids[RichNames[x]]) - crowd.Units.GetPosition(ids[RichNames[y]])).Length;
                        if (prevGaps.TryGetValue((x, y), out var prev))
                        {
                            Assert.True(
                                gap >= Math.Min(SumRadius, prev) - 1e-9,
                                $"seed {permSeed} tick {tick} {RichNames[x]}/{RichNames[y]}：中心距 {gap:R} 比上一 tick（{prev:R}）更深地进入了体积");
                        }

                        prevGaps[(x, y)] = gap;
                    }
                }
            }

            return (track, events, ids.Count, events.Count);
        }

        [Fact]
        public void OrderIndependence_RichCrowdWithChasePolylineDisplacementAndCharge_IsBitIdentical_AndSoIsTheStopEventStream()
        {
            var baseline = RunRichCrowd(0);
            Assert.True(baseline.Stops >= 2, "场景应发出位移停止事件（击退结束），否则事件流不变量是空判");

            foreach (var seed in new uint[] { 1, 2, 3, 4, 5, 6, 7 })
            {
                var shuffled = RunRichCrowd(seed);
                for (var tick = 0; tick < baseline.Track.Count; tick++)
                {
                    Assert.True(
                        baseline.Track[tick].SequenceEqual(shuffled.Track[tick]),
                        $"打乱顺序（种子 {seed}）后第 {tick} tick 的位置与原顺序不是逐位一致");
                }

                Assert.Equal(baseline.Events, shuffled.Events);
            }
        }
    }
}
