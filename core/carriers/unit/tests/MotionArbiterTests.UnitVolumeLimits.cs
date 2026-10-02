// 单位间体积阻挡的"已知限制全部解除"（M3-A，手感设计/02 第 3.5 节、ADR-0128 追加决定）的运行时冒烟：每项一条复现（量从 X 变到 Y，
// 期望值由档案字段 × 标定参考身高 × 步长算出，不写死裸数）加一条不变量（任意 tick、任意处理顺序下成立）。
//   1 推开重叠单位  2 路径跟随与追击绕行  3 受控位移推人  4 顺序无关  5 穿过种类数据声明  6 先撞墙滑动又撞体积的折线精确扫掠
using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Unit
{
    public partial class MotionArbiterTests
    {
        // ------------------------------------------------------------------ 夹具

        private static Id AddExtraUnit(Fx fx, string name, Vec2 at)
        {
            var id = new Id(name);
            fx.World.AddEntity(new PlayerUnit(id, MapId, FactionId, ArchetypeId) { Position = at });
            fx.Stats.SetBase(id, new MovementOptions().MoveSpeedStat, Speed);
            return id;
        }

        private static void SubmitMove(Fx fx, Id unit, double dx, double dy) =>
            fx.World.SubmitIntent(new Intent(unit, "move", new JsonObjectBuilder()
                .Add("dx", new JsonNumber(dx)).Add("dy", new JsonNumber(dy)).Add("mode", new JsonString("Run")).Build()));

        private static double Gap(Fx fx, Id a, Id b) => (fx.Units.GetPosition(a) - fx.Units.GetPosition(b)).Length;

        /// <summary>每 tick 分离速率上限（世界单位）：缺省倍数 × 移动速度属性 × 步长。</summary>
        private static double SeparationCap(double ratio = MotionProfile.DefaultSeparationSpeedRatio) => ratio * Speed * Dt;

        // ================================================================== 1 推开重叠单位

        [Fact]
        public void Separation_BornOverlapping_AreSeparatedAtTheDeclaredRate_AndStopExactlyAtTheBoundary()
        {
            const double startGap = 1.0;
            var fx = BuildVolumes(new Vec2(startGap, 0));
            var cap = SeparationCap();
            var gaps = new List<double> { GapToDummy(fx) };
            for (var tick = 1; tick <= 8; tick++)
            {
                fx.Tick();
                gaps.Add(GapToDummy(fx));
                // 复现：两个单位同倍数、同移速，各分担一半深度，每个单位每 tick 最多走 cap；到边界为止、不越过。
                Near(Math.Min(SumRadius, startGap + 2.0 * cap * tick), gaps[tick]);
                // 沿连线对称：中点不动。
                Near(startGap / 2.0, (fx.Pos.X + fx.Units.GetPosition(DummyId).X) / 2.0);
                Near(0.0, fx.Pos.Y);
            }

            // 不变量：中心距单调不减、不超过半径之和（不过度分离）、到达后保持不动。
            for (var k = 1; k < gaps.Count; k++)
            {
                Assert.True(gaps[k] >= gaps[k - 1] - 1e-9);
                Assert.True(gaps[k] <= SumRadius + 1e-9);
            }

            Near(SumRadius, gaps[gaps.Count - 1]);
            var settled = fx.Pos;
            fx.Tick();
            Assert.Equal(settled, fx.Pos);
        }

        [Theory]
        [InlineData(0.0, 0.5)]
        [InlineData(0.5, 0.0)]
        [InlineData(1.0, 0.25)]
        [InlineData(2.0, 2.0)]
        public void Separation_SharesTheDepthByRate_AndAZeroRateUnitIsNeverPushed(double heroRatio, double dummyRatio)
        {
            var fx = BuildVolumes(new Vec2(0.4, 0));
            fx.Set(FeelFieldNames.UnitSeparationSpeedRatio, heroRatio);
            SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(dummyRatio));
            var hero0 = fx.Pos;
            var dummy0 = fx.Units.GetPosition(DummyId);
            var wHero = heroRatio * Speed;
            var wDummy = dummyRatio * Speed;

            fx.Tick();

            // 第一 tick：深度按速率分摊，各自再受 速率 × 步长 的上限；速率为 0 的单位不被推（对方承担全部）。
            var depth = SumRadius - 0.4;
            var expectHero = Math.Min(depth * wHero / (wHero + wDummy), wHero * Dt);
            var expectDummy = Math.Min(depth * wDummy / (wHero + wDummy), wDummy * Dt);
            Near(-expectHero, fx.Pos.X - hero0.X);
            Near(expectDummy, fx.Units.GetPosition(DummyId).X - dummy0.X);
            if (heroRatio == 0.0) Assert.Equal(hero0, fx.Pos);
            if (dummyRatio == 0.0) Assert.Equal(dummy0, fx.Units.GetPosition(DummyId));
        }

        [Fact]
        public void Separation_BothRatesZero_NobodyIsPushed_AndTheOldOverlapBehaviorStays()
        {
            var fx = BuildVolumes(new Vec2(1.0, 0));
            fx.Set(FeelFieldNames.UnitSeparationSpeedRatio, 0.0);
            for (var i = 0; i < 5; i++) fx.Tick();
            Assert.Equal(Vec2.Zero, fx.Pos);
            Assert.Equal(new Vec2(1.0, 0), fx.Units.GetPosition(DummyId));
        }

        [Fact]
        public void Separation_NeverPushesAUnitThroughAWall_AndTheOtherUnitSeparatesAlone()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(-50, -50), new Vec2(-0.25, 50)) }); // 英雄身后 0.25 处是墙
            var fx = BuildVolumes(new Vec2(0.6, 0), nav: nav);
            var gap0 = GapToDummy(fx);
            var prev = gap0;
            for (var tick = 0; tick < 40; tick++)
            {
                fx.Tick();
                Assert.True(nav.IsWalkable(MapId, fx.Pos), $"tick {tick}：英雄被推进了墙里 {fx.Pos}");
                var gap = GapToDummy(fx);
                Assert.True(gap >= prev - 1e-9, $"tick {tick}：中心距变小");
                prev = gap;
            }

            Assert.True(fx.Pos.X >= -0.25 - 1e-9);
            Near(SumRadius, prev, 1e-6); // 英雄被墙挡住后，靠假人那一边继续分离，最终还是分开了
        }

        [Fact]
        public void Separation_TheBodiesLeftByAnInterruptedPassThroughDash_ArePushedOutAfterTheWindow_NotDuring()
        {
            // 冲刺 6.5 分 4 个 tick（每 tick 1.625），假人在 6.0（半径之和 2）：第 3 个 tick 之后英雄在 4.875，落在假人体积里，深度 0.875。
            // 窗口被打断（第 4 个 tick 起没有动作）时落点没有经过"窗口最后一个 tick 的落点修正"，由重叠分离接管：窗口内不分离、窗口后才推出。
            var fx = BuildVolumes(new Vec2(6.0, 0));
            fx.Set(FeelFieldNames.DodgeThroughUnits, true);
            SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0)); // 假人不被推，英雄一个人出来
            var motion = Lunge(6.5, 0, 4, new Vec2(1, 0), kind: ActionMotionKind.Dash);
            var dummyAt = fx.Units.GetPosition(DummyId);
            var gaps = new List<double>();
            for (var i = 0; i < 20; i++)
            {
                fx.Actions.State = i < 3 ? Act(i, motion) : (ActionState?)null;
                fx.Tick();
                gaps.Add(GapToDummy(fx));
                Assert.Equal(dummyAt, fx.Units.GetPosition(DummyId));
            }

            // 窗口内（前三个 tick）幽灵穿过、没有被分离干扰：窗口中断时中心距就是穿过后的落点距离，之后才被推开。
            Near(6.0 - 6.5 * 3.0 / 4.0, gaps[2], 1e-9);
            var firstOutside = gaps.FindIndex(3, g => g >= SumRadius - 1e-9);
            Assert.True(firstOutside > 3, "窗口之后才开始被推出体积（落在体积里的那几个 tick 里中心距逐步变大）");
            Assert.True(gaps[3] > gaps[2] + 1e-9, "窗口结束后的第一个 tick 就开始分离");
            Assert.True(gaps[gaps.Count - 1] >= SumRadius - 1e-9, $"最终分开：{gaps[gaps.Count - 1]:R}");
            for (var k = 4; k < gaps.Count; k++) Assert.True(gaps[k] >= gaps[k - 1] - 1e-9);
            Assert.True(fx.Pos.X < 6.0, "英雄从假人这一侧被推出（沿连线方向，本来就在这一侧）");
        }

        // ================================================================== 2 路径跟随与追击绕行

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.3)]
        [InlineData(-0.3)]
        [InlineData(1.0)]
        public void Avoidance_PathFollowing_GoesAroundAUnitOnTheLine_AndArrives_WithoutEverOverlapping(double offsetY)
        {
            var fx = BuildVolumes(new Vec2(5.0, offsetY));
            SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0)); // 假人是木桩，不被推
            fx.MoveTo(10, 0);
            var passedDummy = false;
            var arrivedAt = -1;
            for (var tick = 0; tick < 120 && arrivedAt < 0; tick++)
            {
                fx.Tick();
                AssertNeverOverlaps(fx, $"绕行 tick {tick}");
                passedDummy |= fx.Pos.X > 5.0 + SumRadius;
                if (fx.Player.MovementState.CurrentPath == null) arrivedAt = tick;
            }

            Assert.True(arrivedAt >= 0, $"没有在 120 tick 内到达，停在 {fx.Pos}");
            Assert.True(passedDummy, "绕过了假人");
            Near(10.0, fx.Pos.X, 0.011);
            Near(0.0, fx.Pos.Y, 0.011);
        }

        [Fact]
        public void Avoidance_Chase_GoesAroundAThirdUnit_AndStopsAtTheTargetsVolumeBoundary()
        {
            var fx = BuildVolumes(new Vec2(6.0, 0)); // 假人 = 挡路单位
            var target = AddExtraUnit(fx, "unit.target", new Vec2(14, 0));
            SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            SetUnitField(fx, target, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            fx.Chase(target, 0.5);
            var passed = false;
            for (var tick = 0; tick < 150; tick++)
            {
                fx.Tick();
                AssertNeverOverlaps(fx, $"追击 tick {tick}");
                Assert.True(Gap(fx, HeroId, target) >= SumRadius - 1e-9, $"追击 tick {tick}：撞进了目标的体积");
                passed |= fx.Pos.X > 6.0 + SumRadius;
            }

            Assert.True(passed);
            Assert.InRange(Gap(fx, HeroId, target), SumRadius, SumRadius + 3 * Pull);
            Assert.Equal(MoveMode.Idle, fx.Player.MovementState.Mode);
        }

        [Fact]
        public void Avoidance_WhenTheGoalIsInsideTheBlockersVolume_StopsInFront_AndDoesNotOrbit()
        {
            var fx = BuildVolumes(new Vec2(5.0, 0));
            SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            fx.MoveTo(5.5, 0); // 目标点就在假人体积里：绕过去也到不了
            for (var tick = 0; tick < 60; tick++) fx.Tick();
            var stoppedAt = fx.Pos;
            Assert.InRange(stoppedAt.X, 5.0 - SumRadius - Pull - 1e-9, 5.0 - SumRadius + 1e-9);
            for (var tick = 0; tick < 20; tick++)
            {
                fx.Tick();
                AssertNeverOverlaps(fx, $"tick {tick}");
            }

            Assert.Equal(stoppedAt, fx.Pos); // 不绕圈、不蠕动
        }

        [Fact]
        public void Avoidance_InACorridorTooNarrowToPass_FallsBackToStopping_InsideTheWalls()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[]
            {
                new Rect(new Vec2(-50, 0.6), new Vec2(50, 50)),
                new Rect(new Vec2(-50, -50), new Vec2(50, -0.6)),
            });
            var fx = BuildVolumes(new Vec2(5.0, 0), nav: nav);
            SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            fx.MoveTo(10, 0);
            for (var tick = 0; tick < 80; tick++)
            {
                fx.Tick();
                Assert.True(nav.IsWalkable(MapId, fx.Pos), $"tick {tick}：走进了墙 {fx.Pos}");
                AssertNeverOverlaps(fx, $"窄道 tick {tick}");
            }

            // 贴着体积边界、没有越过假人（侧移最多到墙边）；之后不再动（不蠕动、不来回摆）。
            Assert.True(fx.Pos.X < 5.0, $"没有越过假人：{fx.Pos}");
            Assert.InRange(GapToDummy(fx), SumRadius - 1e-9, SumRadius + Pull + 1e-9);
            var stoppedAt = fx.Pos;
            for (var tick = 0; tick < 10; tick++) fx.Tick();
            Assert.Equal(stoppedAt, fx.Pos);
        }

        [Fact]
        public void Avoidance_TurnedOff_KeepsTheOldBehavior_StopsInFrontOfTheUnit()
        {
            var fx = BuildVolumes(new Vec2(5.0, 0));
            fx.Set(FeelFieldNames.PathAvoidUnits, false);
            fx.MoveTo(10, 0);
            for (var tick = 0; tick < 60; tick++) fx.Tick();
            Assert.InRange(fx.Pos.X, 5.0 - SumRadius - Pull - 1e-9, 5.0 - SumRadius + 1e-9);
            Assert.NotNull(fx.Player.MovementState.CurrentPath);
        }

        // ================================================================== 3 受控位移推人

        private static (Fx Fx, double Remaining) KnockIntoDummy(bool push, double? ratio, double resistance)
        {
            var fx = BuildVolumes(new Vec2(5.0, 0));
            SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0)); // 假人不会被分离"吃掉"推出的位移
            if (push) fx.Set(FeelFieldNames.ForcedPushUnits, true);
            if (ratio.HasValue) fx.Set(FeelFieldNames.ForcedPushRatio, ratio.Value);
            if (resistance > 0.0)
            {
                var stat = new Id("stat.kb_resist");
                fx.Stats.SetBase(DummyId, stat, resistance);
                SetUnitField(fx, DummyId, FeelFieldNames.KnockbackResistanceStat, FeelValue.Of(stat.ToString()));
            }

            fx.Host.BeginKnockback(new KnockbackRequest(HeroId, new Vec2(1, 0), 8.0, 0.4));
            for (var tick = 0; tick < 30; tick++) fx.Tick();
            return (fx, 8.0 - fx.Pos.X);
        }

        [Theory]
        [InlineData(null, 0.0)]
        [InlineData(0.25, 0.0)]
        [InlineData(1.0, 0.0)]
        [InlineData(0.5, 0.5)]
        [InlineData(1.0, 1.0)]
        public void ForcedPush_TransfersTheRemainingDisplacement_ByRatioAndResistance(double? ratio, double resistance)
        {
            var (fx, remaining) = KnockIntoDummy(true, ratio, resistance);
            var r = ratio ?? MotionProfile.DefaultForcedPushRatio;
            var shift = fx.Units.GetPosition(DummyId).X - 5.0;

            // 复现：击退者撞停（被阻挡），被撞单位沿连线方向获得 剩余位移 × 比例 ×（1 − 抗性）。
            Assert.Contains(fx.Stops, s => s.Reason == MoveStopReason.DisplacementBlocked);
            Assert.InRange(fx.Pos.X, 5.0 - SumRadius - Pull - 1e-9, 5.0 - SumRadius + 1e-9);
            Near(remaining * r * (1.0 - resistance), shift, 1e-6);
            Near(0.0, fx.Units.GetPosition(DummyId).Y, 1e-9);

            // 不变量：转移量不超过剩余位移；击退者与被推单位任何时候都不重叠（最终中心距 ≥ 半径之和）。
            Assert.True(shift <= remaining + 1e-9);
            AssertNeverOverlaps(fx, "推人之后");
        }

        [Fact]
        public void ForcedPush_IsOffByDefault_TheKnockbackJustStops()
        {
            var (fx, _) = KnockIntoDummy(false, null, 0.0);
            Assert.Equal(new Vec2(5.0, 0), fx.Units.GetPosition(DummyId));
            Assert.InRange(fx.Pos.X, 5.0 - SumRadius - Pull - 1e-9, 5.0 - SumRadius + 1e-9);
        }

        [Fact]
        public void ForcedPush_TwoKnockbacksIntoTheSameUnit_AreCombinedAsAVectorSum_IndependentOfIntentOrder()
        {
            Vec2 Run(bool heroFirst)
            {
                var fx = BuildVolumes(new Vec2(5.0, 0));
                var other = AddExtraUnit(fx, "unit.other", new Vec2(5.0, 8.0)); // 在假人正上方 8
                fx.Set(FeelFieldNames.ForcedPushUnits, true);
                foreach (var id in new[] { HeroId, DummyId, other })
                {
                    SetUnitField(fx, id, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
                }

                var requests = new[]
                {
                    new KnockbackRequest(HeroId, new Vec2(1, 0), 8.0, 0.4),
                    new KnockbackRequest(other, new Vec2(0, -1), 8.0, 0.4),
                };
                foreach (var r in heroFirst ? requests : requests.Reverse().ToArray()) fx.Host.BeginKnockback(r);
                for (var tick = 0; tick < 30; tick++) fx.Tick();
                return fx.Units.GetPosition(DummyId);
            }

            var a = Run(true);
            var b = Run(false);
            Assert.Equal(a, b);
            Assert.True(a.X > 5.0 && a.Y < 0.0 + 1e-9 + 8.0 && a != new Vec2(5.0, 0), $"两个击退同时撞上假人，结果是向量和：{a}");
        }

        // ================================================================== 4 顺序无关

        [Fact]
        public void OrderIndependence_TwoUnitsWalkingTowardEachOther_MeetSymmetrically_AndTheirMovedEventsCarryTheFinalPosition()
        {
            // 复现：两个单位相向走，各自只看得到对方 tick 起点的位置——旧实现里先处理的单位占位，另一个被挡得更早（不对称）。
            // 现在两个单位按同一比例互相撞停，镜像对称到逐位相等。
            var fx = BuildVolumes(new Vec2(1.5, 0));
            fx.Player.Position = new Vec2(-1.5, 0);
            fx.Stats.SetBase(HeroId, new MovementOptions().MoveSpeedStat, Speed);
            var movedSeen = 0;
            for (var tick = 0; tick < 12; tick++)
            {
                fx.Move(1, 0);
                SubmitMove(fx, DummyId, -1, 0);
                fx.Tick();
                var a = fx.Pos;
                var b = fx.Units.GetPosition(DummyId);
                Assert.Equal(BitConverter.DoubleToInt64Bits(-a.X), BitConverter.DoubleToInt64Bits(b.X));
                AssertNeverOverlaps(fx, $"tick {tick}");

                // unit.moved 事件：延迟到 tick 末、携带最终位置（不是撞停前的提议位置），按单位 id 顺序。
                var events = fx.Moved.Skip(movedSeen).ToList();
                movedSeen = fx.Moved.Count;
                if (events.Count == 0) continue;
                Assert.Equal(new[] { HeroId, DummyId }.OrderBy(x => x.ToString(), StringComparer.Ordinal).ToList(), events.Select(e => e.UnitId).ToList());
                Assert.Equal(a, events.Single(e => e.UnitId.Equals(HeroId)).Position);
                Assert.Equal(b, events.Single(e => e.UnitId.Equals(DummyId)).Position);
            }

            // 双方最终在边界处相遇：中心距 = 半径之和加（至多）一个到达容差量级。
            Assert.InRange(GapToDummy(fx), SumRadius, SumRadius + 3 * Pull);
        }

        private sealed class Crowd
        {
            public WorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public MovementHost Host = null!;
            public FakeActions Actions = null!;
            public List<string> Names = null!;
            public MovementTickHandler Handler = null!;
            public EventBus Bus = null!;
            public FeelSystem Feel = null!;
        }

        private static Crowd BuildCrowd(
            IReadOnlyList<string> creationOrder, IReadOnlyDictionary<string, Vec2> start, Core.Foundation.EngineAdapter.INavigation2D? nav = null,
            Action<FeelSystem>? configureFeel = null)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var stats = new FakeStatHost();
            var opts = new MovementOptions();
            foreach (var name in creationOrder)
            {
                var id = new Id(name);
                world.AddEntity(new PlayerUnit(id, MapId, FactionId, ArchetypeId) { Position = start[name] });
                stats.SetBase(id, opts.MoveSpeedStat, Speed);
            }

            var units = new WorldUnitAccess(world);
            var host = new MovementHost(world);
            var feel = AssembleFeel();
            feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.UnitBodyRadius, FeelOp.Set, FeelValue.Of(BodyRel)));
            feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.ForcedPushUnits, FeelOp.Set, FeelValue.Of(true)));
            if (nav != null)
            {
                feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.WallSlide, FeelOp.Set, FeelValue.Of(true)));
            }

            configureFeel?.Invoke(feel);
            var actions = new FakeActions();
            host.Motion = new MotionServices
            {
                Feel = feel.Resolver,
                Actions = actions,
                ActionClock = new FakeClock(),
                Stagger = new FakeStagger(),
                RootMotion = new FakeRootMotion(),
            };
            var handler = new MovementTickHandler(units, stats, new FakeAuraQuery(), host, bus, nav, opts);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, handler);
            return new Crowd
            {
                World = world, Units = units, Host = host, Actions = actions, Names = creationOrder.ToList(), Handler = handler, Bus = bus,
                Feel = feel,
            };
        }

        private static uint Hash(string name, int k)
        {
            var h = 2166136261u;
            foreach (var c in name)
            {
                h ^= c;
                h *= 16777619u;
            }

            h ^= (uint)k * 2654435761u;
            h *= 2246822519u;
            h ^= h >> 13;
            h *= 3266489917u;
            h ^= h >> 16;
            return h;
        }

        private static double Unit01(uint h) => (h >> 8) / (double)(1u << 24);

        private static List<T> Shuffled<T>(IEnumerable<T> items, uint seed)
        {
            var list = items.ToList();
            var state = seed * 2654435761u + 97u;
            for (var i = list.Count - 1; i > 0; i--)
            {
                state = state * 1664525u + 1013904223u;
                var j = (int)((state >> 8) % (uint)(i + 1));
                (list[i], list[j]) = (list[j], list[i]);
            }

            return list;
        }

        private static readonly string[] CrowdNames = { "u.a", "u.b", "u.c", "u.d", "u.e", "u.f" };

        private static Dictionary<string, Vec2> CrowdStart() => new Dictionary<string, Vec2>
        {
            ["u.a"] = new Vec2(0.0, 0.0),
            ["u.b"] = new Vec2(1.2, 0.3),
            ["u.c"] = new Vec2(2.4, -0.2),
            ["u.d"] = new Vec2(0.5, 1.5),
            ["u.e"] = new Vec2(1.9, 1.7),
            ["u.f"] = new Vec2(3.4, 1.0),
        };

        /// <summary>
        /// 跑一遍人群场景：六个单位聚在一起（出生重叠）、有的随机转向、有的走向远点、第 15 tick 两个单位被击退；
        /// <paramref name="permSeed"/> 决定"创建单位的顺序"与"每个 tick 提交意图的顺序"（0 = 原顺序）。返回每 tick 每个单位位置的逐位表示。
        /// </summary>
        private static (List<long[]> Track, double MinGapAfterFirstSeparation, int Contacts, List<string> Events, int MaxPasses) RunCrowd(
            uint permSeed, Action<Crowd>? configure = null)
        {
            var order = permSeed == 0 ? CrowdNames.ToList() : Shuffled(CrowdNames, permSeed);
            var crowd = BuildCrowd(order, CrowdStart());
            configure?.Invoke(crowd);
            var events = new List<string>();
            var maxPasses = 0;
            crowd.Bus.Subscribe(CarriersEventKeys.UnitMoved, ev =>
            {
                var m = (UnitMovedEvent)ev;
                events.Add($"moved {m.UnitId} {BitConverter.DoubleToInt64Bits(m.Position.X)} {BitConverter.DoubleToInt64Bits(m.Position.Y)}");
            });
            crowd.Bus.Subscribe(CarriersEventKeys.UnitStateChanged, ev =>
            {
                var m = (UnitStateChangedEvent)ev;
                events.Add($"state {m.UnitId} {m.OldState}>{m.NewState}");
            });
            crowd.Host.OnMoveStopped += (id, pos, reason) =>
                events.Add($"stop {id} {reason} {BitConverter.DoubleToInt64Bits(pos.X)} {BitConverter.DoubleToInt64Bits(pos.Y)}");
            var ids = CrowdNames.ToDictionary(n => n, n => new Id(n));
            var track = new List<long[]>();
            var prevGaps = new Dictionary<(string, string), double>();
            var minGap = double.MaxValue;
            var contacts = 0;
            for (var tick = 0; tick < 120; tick++)
            {
                var submitOrder = permSeed == 0 ? CrowdNames.ToList() : Shuffled(CrowdNames, permSeed * 31u + (uint)tick);
                foreach (var name in submitOrder)
                {
                    var idx = Array.IndexOf(CrowdNames, name);
                    if (idx % 3 == 0)
                    {
                        if (tick == 0 || tick == 50)
                        {
                            var h = Hash(name, tick);
                            crowd.World.SubmitIntent(new Intent(ids[name], "move", new JsonObjectBuilder()
                                .Add("x", new JsonNumber(-4.0 + Unit01(h) * 12.0)).Add("y", new JsonNumber(-3.0 + Unit01(h * 7u + 1u) * 8.0))
                                .Add("mode", new JsonString("Run")).Build()));
                        }
                    }
                    else
                    {
                        var h = Hash(name, tick / 5);
                        var angle = Unit01(h) * 2.0 * Math.PI;
                        crowd.World.SubmitIntent(new Intent(ids[name], "move", new JsonObjectBuilder()
                            .Add("dx", new JsonNumber(Math.Cos(angle))).Add("dy", new JsonNumber(Math.Sin(angle)))
                            .Add("mode", new JsonString("Run")).Build()));
                    }

                    if (tick == 15 && (name == "u.a" || name == "u.d"))
                    {
                        var toward = name == "u.a" ? new Vec2(1, 0.2) : new Vec2(0.4, -1);
                        crowd.Host.BeginKnockback(new KnockbackRequest(ids[name], toward, 5.0, 0.4));
                    }
                }

                crowd.World.Tick(SimStep.Continuous(Dt));
                maxPasses = Math.Max(maxPasses, crowd.Handler.LastVolumePassCount);
                events.Add($"-- tick {tick}");

                var row = new long[CrowdNames.Length * 2];
                for (var k = 0; k < CrowdNames.Length; k++)
                {
                    var p = crowd.Units.GetPosition(ids[CrowdNames[k]]);
                    row[2 * k] = BitConverter.DoubleToInt64Bits(p.X);
                    row[2 * k + 1] = BitConverter.DoubleToInt64Bits(p.Y);
                }

                track.Add(row);

                // 不变量：任意两个单位的中心距不会比上一 tick 的"min(半径之和, 上一 tick 的距离)"更小（只能不变或更远，分离只会拉开）。
                for (var x = 0; x < CrowdNames.Length; x++)
                {
                    for (var y = x + 1; y < CrowdNames.Length; y++)
                    {
                        var gap = (crowd.Units.GetPosition(ids[CrowdNames[x]]) - crowd.Units.GetPosition(ids[CrowdNames[y]])).Length;
                        if (prevGaps.TryGetValue((CrowdNames[x], CrowdNames[y]), out var prev))
                        {
                            Assert.True(
                                gap >= Math.Min(SumRadius, prev) - 1e-9,
                                $"seed {permSeed} tick {tick} {CrowdNames[x]}/{CrowdNames[y]}：中心距 {gap:R} 比上一 tick（{prev:R}）更深地进入了体积");
                            if (prev >= SumRadius && gap < SumRadius + 0.05) contacts++;
                        }

                        prevGaps[(CrowdNames[x], CrowdNames[y])] = gap;
                        if (tick > 40) minGap = Math.Min(minGap, gap);
                    }
                }
            }

            return (track, minGap, contacts, events, maxPasses);
        }

        [Fact]
        public void OrderIndependence_CrowdOfOverlappingUnits_GivesBitIdenticalTracks_WhateverTheCreationAndIntentOrder()
        {
            var baseline = RunCrowd(0);

            // 场景确实走过了出生重叠分离与互相贴着走（否则这条不变量是空判）。
            Assert.True(baseline.Contacts > 0, "人群里应当发生过单位贴着体积边界的接触");
            var first = baseline.Track[0];
            Assert.True(first.Length > 0);

            foreach (var seed in new uint[] { 1, 2, 3, 4, 5, 6, 7 })
            {
                var shuffled = RunCrowd(seed);
                Assert.Equal(baseline.Track.Count, shuffled.Track.Count);
                for (var tick = 0; tick < baseline.Track.Count; tick++)
                {
                    Assert.True(
                        baseline.Track[tick].SequenceEqual(shuffled.Track[tick]),
                        $"打乱顺序（种子 {seed}）后第 {tick} tick 的位置与原顺序不是逐位一致");
                }
            }
        }

        // ================================================================== 5 穿过种类由数据声明

        [Theory]
        [InlineData("lunge", true, false)]
        [InlineData("dash", false, true)]
        [InlineData("lunge,dash", true, true)]
        [InlineData("step_back, charge", false, false)]
        [InlineData("", false, false)]
        public void PassThroughKinds_AreDeclaredInTheProfile_AndOnlyListedKindsPass(string kinds, bool lungePasses, bool dashPasses)
        {
            double EndX(ActionMotionKind kind)
            {
                var fx = BuildVolumes(new Vec2(6.0, 0));
                SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
                fx.Set(FeelFieldNames.DodgeThroughUnits, true);
                fx.Set(FeelFieldNames.PassThroughMotionKinds, kinds);
                RunLunge(fx, Lunge(12.0, 0, 3, new Vec2(1, 0), kind: kind), 6);
                if (fx.Pos.X < 6.0 - SumRadius + 1e-9) AssertNeverOverlaps(fx, $"{kind} 被挡");
                return fx.Pos.X;
            }

            Assert.Equal(lungePasses, EndX(ActionMotionKind.Lunge) > 6.0);
            Assert.Equal(dashPasses, EndX(ActionMotionKind.Dash) > 6.0);
        }

        [Fact]
        public void PassThroughKinds_AbsentMeansDashAndStepBack_AndTheMasterSwitchStillGates_AndUnknownNamesAreRejected()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.UnitBodyRadius, BodyRel);
            var plain = MotionProfile.Read(fx.Feel.Resolver.ResolveJudging(HeroId));
            Assert.True(plain.PassesThroughKind(ActionMotionKind.Dash));
            Assert.True(plain.PassesThroughKind(ActionMotionKind.StepBack));
            Assert.False(plain.PassesThroughKind(ActionMotionKind.Lunge));
            Assert.False(plain.PassesThroughKind(ActionMotionKind.Charge));
            Assert.False(plain.DodgeThroughUnits); // 总开关缺省关：种类集合有值也不穿

            // 总开关关着：列了 lunge 也不穿。
            var gated = BuildVolumes(new Vec2(6.0, 0));
            gated.Set(FeelFieldNames.PassThroughMotionKinds, "lunge");
            RunLunge(gated, Lunge(12.0, 0, 3, new Vec2(1, 0)), 6);
            Assert.True(gated.Pos.X < 6.0 - SumRadius + 1e-9);

            fx.Set(FeelFieldNames.PassThroughMotionKinds, "dash,teleport");
            var ex = Assert.Throws<InvalidOperationException>(() => MotionProfile.Read(fx.Feel.Resolver.ResolveJudging(HeroId)));
            Assert.Contains(FeelFieldNames.PassThroughMotionKinds, ex.Message);
            Assert.Contains("teleport", ex.Message);
        }

        // ================================================================== 6 先撞墙滑动又撞单位体积：折线精确扫掠

        /// <summary>
        /// 英雄从 (2,0) 以对角方向、步长 4 的扑击撞上 x=3 的墙：先走到墙前（第一段），再沿墙面向 +y 滑一段（第二段）。
        /// 折线拐点与终点由规则算出（回退量 = 到达容差，滑动 = 剩余位移去掉法向分量）。
        /// </summary>
        private static (Vec2 Via, Vec2 End) WallSlideGeometry()
        {
            var c = 1.0 / Math.Sqrt(2.0);
            var dir = new Vec2(c, c);
            var hitDistance = (3.0 - 2.0) / c;
            var advance = hitDistance - Pull;
            var via = new Vec2(2.0, 0.0) + dir * advance;
            var rem = 4.0 - advance;
            return (via, new Vec2(via.X, via.Y + c * rem));
        }

        private static Fx WallSlideRun(Vec2 dummyAt, bool volumes, ActionMotionBlocking blocking = ActionMotionBlocking.Slide)
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(3, -50), new Vec2(4, 50)) });
            var fx = BuildVolumes(dummyAt, wallSlide: true, nav: nav, volumes: volumes);
            fx.Player.Position = new Vec2(2.0, 0.0);
            SetUnitField(fx, DummyId, FeelFieldNames.UnitSeparationSpeedRatio, FeelValue.Of(0.0));
            var c = 1.0 / Math.Sqrt(2.0);
            RunLunge(fx, Lunge(4.0, 0, 1, new Vec2(c, c), blocking: blocking), 3);
            return fx;
        }

        [Fact]
        public void WallSlideThenUnitVolume_TheSweepFollowsTheActualTwoSegmentPath_NotTheChord()
        {
            var (via, end) = WallSlideGeometry();

            // 没有体积时落在规则算出的折线终点（对照：证明几何期望本身对）。
            var off = WallSlideRun(new Vec2(100, 100), volumes: false);
            Near(via.X, off.Pos.X, 1e-9);
            Near(end.Y, off.Pos.Y, 1e-9);

            // 复现 1（不误拦）：假人只和起终点连成的弦相交、折线两段都碰不到——折线精确扫掠放行，英雄走完折线。
            var chordOnly = new Vec2(0.9, 1.9);
            Assert.True(DistanceToSegment(chordOnly, new Vec2(2.0, 0.0), end) < SumRadius, "夹具：弦确实穿过体积");
            Assert.True(DistanceToSegment(chordOnly, new Vec2(2.0, 0.0), via) >= SumRadius && DistanceToSegment(chordOnly, via, end) >= SumRadius, "夹具：折线两段都碰不到");
            var passed = WallSlideRun(chordOnly, volumes: true);
            Near(end.X, passed.Pos.X, 1e-9);
            Near(end.Y, passed.Pos.Y, 1e-9);
            AssertNeverOverlaps(passed, "折线放行");

            // 复现 2（不漏拦）：假人只和第二段（沿墙滑的那段）相交、弦碰不到——弦端点检测会隧穿过去，折线扫掠必须拦住。
            var secondLegOnly = new Vec2(4.95, 2.2);
            Assert.True(DistanceToSegment(secondLegOnly, new Vec2(2.0, 0.0), end) >= SumRadius, "夹具：弦碰不到体积");
            Assert.True(DistanceToSegment(secondLegOnly, via, end) < SumRadius, "夹具：第二段碰得到体积");
            var blocked = WallSlideRun(secondLegOnly, volumes: true);
            Assert.NotEqual(end, blocked.Pos);
            AssertNeverOverlaps(blocked, "折线拦截");
            Assert.True(blocked.Pos.Y < end.Y, $"没有被拦住：停在 {blocked.Pos}");
        }

        [Theory]
        [InlineData(0.9, 1.9)]
        [InlineData(4.95, 2.2)]
        [InlineData(1.0, 3.5)]
        [InlineData(5.5, 1.0)]
        [InlineData(2.0, 2.0)]
        [InlineData(3.5, 3.0)]
        public void WallSlideThenUnitVolume_NeverEndsInsideTheVolume_ForAnyDummyPlacement(double x, double y)
        {
            foreach (var blocking in new[] { ActionMotionBlocking.Slide, ActionMotionBlocking.Stop })
            {
                var fx = WallSlideRun(new Vec2(x, y), volumes: true, blocking);
                var gapAtEnd = GapToDummy(fx);
                // 起点 (2,0) 到假人的距离：起点本来就在体积里时只要求不更深。
                var startGap = (new Vec2(2.0, 0.0) - new Vec2(x, y)).Length;
                Assert.True(
                    gapAtEnd >= Math.Min(SumRadius, startGap) - 1e-9,
                    $"({x},{y}) {blocking}：终点中心距 {gapAtEnd:R}");
            }
        }

        private static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            var ab = b - a;
            var t = Math.Max(0.0, Math.Min(1.0, (p - a).Dot(ab) / ab.Dot(ab)));
            return (p - (a + ab * t)).Length;
        }
    }
}
