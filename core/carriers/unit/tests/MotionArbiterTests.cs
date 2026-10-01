// MotionArbiterTests：手感设计/02（ADR-0116）运动档案与运动仲裁器的运行时冒烟。
// 期望值全部由档案字段与步长算出（例如 accel_ms = A 的达速 tick 数 = ceil(A / 步长)），不写死裸数；
// 每组用例是"复现 + 不变量"：复现给出具体数值，不变量（单调、不超速、逐位等价、总距离守恒）覆盖同一类的其它取值。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Unit
{
    public class MotionArbiterTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id FactionId = new Id("fac.player");
        private static readonly Id ArchetypeId = new Id("arch.class.sample");
        private static readonly Id HeroId = new Id("unit.hero");
        private static readonly Id DummyId = new Id("unit.dummy");

        /// <summary>模拟步长（秒）：与手感解析器的 StepSeconds 取同一值，毫秒折 tick 的依据。</summary>
        private const double Dt = 0.1;

        /// <summary>移动速度属性（世界单位/秒）；标定基础移速也取 4，目标速度 = 属性速度 × 倍率。</summary>
        private const double Speed = 4.0;

        private const double Eps = 1e-9;

        // ------------------------------------------------------------------ 夹具

        private sealed class FakeActions : IActionStateQuery
        {
            public ActionState? State;

            public ActionState? Current(Id unitId) => State;

            public bool IsCancelOpen(Id unitId, ActionClass actionClass) => false;

            public bool IsInvulnerable(Id unitId) => false;

            public bool IsActionClockPaused(Id unitId) => false;
        }

        private sealed class FakeClock : IActorActionClockQuery
        {
            public bool Paused;

            public bool IsPaused(Id actorId) => Paused;

            public int RemainingPausedTicks(Id actorId) => Paused ? 1 : 0;

            public long ActionTicks(Id actorId) => 0;

            public int PauseHandleCount(Id actorId) => Paused ? 1 : 0;

            public int TotalPauseHandleCount => Paused ? 1 : 0;
        }

        private sealed class FakeStagger : IStaggerStateQuery
        {
            public bool Staggered;

            public bool IsStaggered(Id unitId) => Staggered;
        }

        private sealed class FakeRootMotion : IRootMotionSource
        {
            public bool Supported;
            public Vec2 Delta;

            public bool SupportsRootMotion => Supported;

            public Vec2 ConsumeRootMotionDelta(Id unitId) => Delta;
        }

        private sealed class Fx
        {
            public WorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public PlayerUnit Player = null!;
            public PlayerUnit Dummy = null!;
            public MovementHost Host = null!;
            public FakeAuraQuery Auras = null!;
            public FakeStatHost Stats = null!;
            public FeelSystem Feel = null!;
            public MotionServices? Motion;
            public FakeActions Actions = new FakeActions();
            public FakeClock Clock = new FakeClock();
            public FakeStagger Stagger = new FakeStagger();
            public FakeRootMotion Root = new FakeRootMotion();
            public List<(MoveStopReason Reason, Vec2 Pos)> Stops = new List<(MoveStopReason, Vec2)>();

            public Vec2 Pos => Units.GetPosition(HeroId);

            public MotionKinematics Mo => Player.MovementState.Motion;

            public void Set(string field, double value) =>
                Feel.DebugOverrides!.SetGlobal(new FeelWrite(field, FeelOp.Set, FeelValue.Of(value)));

            public void Set(string field, string value) =>
                Feel.DebugOverrides!.SetGlobal(new FeelWrite(field, FeelOp.Set, FeelValue.Of(value)));

            public void Set(string field, bool value) =>
                Feel.DebugOverrides!.SetGlobal(new FeelWrite(field, FeelOp.Set, FeelValue.Of(value)));

            public void Tick() => World.Tick(SimStep.Continuous(Dt));

            public void Move(double dx, double dy, string mode = "Run") =>
                World.SubmitIntent(new Intent(HeroId, "move", new JsonObjectBuilder()
                    .Add("dx", new JsonNumber(dx)).Add("dy", new JsonNumber(dy)).Add("mode", new JsonString(mode)).Build()));

            public void MoveTo(double x, double y) =>
                World.SubmitIntent(new Intent(HeroId, "move", new JsonObjectBuilder()
                    .Add("x", new JsonNumber(x)).Add("y", new JsonNumber(y)).Add("mode", new JsonString("Run")).Build()));

            public void Chase(Id target, double stopRange) =>
                World.SubmitIntent(new Intent(HeroId, "move_to_unit", new JsonObjectBuilder()
                    .Add("targetUnitId", new JsonString(target.ToString())).Add("stopRange", new JsonNumber(stopRange))
                    .Add("mode", new JsonString("Run")).Build()));

            /// <summary>连续 n 个 tick 持续输入方向，返回每 tick 之后的速度标量。</summary>
            public List<double> RunSpeeds(int n, double dx, double dy)
            {
                var speeds = new List<double>();
                for (var i = 0; i < n; i++)
                {
                    Move(dx, dy);
                    Tick();
                    speeds.Add(Mo.Speed);
                }

                return speeds;
            }
        }

        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
            for (var i = 0; i < 4; i++) dir = dir.Parent!;
            return dir.FullName;
        }

        private static List<(string Table, string Json)> FrameworkTables(string basePreset)
        {
            var dir = Path.Combine(FindRepoRoot(), "data", "_feel", "feel");
            var files = Directory.GetFiles(dir, "*.json");
            Array.Sort(files, StringComparer.Ordinal);
            var tables = files.Select(f => (Path.GetFileNameWithoutExtension(f), File.ReadAllText(f))).ToList();
            tables.Add(("feel.calibration",
                "{ \"table\": \"feel.calibration\", \"schema_version\": 1, \"rows\": [ { \"id\": \"feel.calibration.motion_test\", " +
                "\"base_preset\": \"" + basePreset + "\", \"reference_height\": 2.0, \"base_speed\": 4.0, \"animation_fps\": 30.0, " +
                "\"reference_camera_height\": 10.0, \"reference_zoom\": 1.0, \"pixels_per_unit\": 32.0, \"marker_tolerance_ms\": 50.0 } ] }"));
            return tables;
        }

        private static FeelSystem AssembleFeel(string basePreset = "feel.preset.rpg_classic")
        {
            var source = new InMemoryDataSource();
            foreach (var (table, json) in FrameworkTables(basePreset)) source.Add(table, json);
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            FeelSchemas.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.Equal(0, report.ErrorCount);
            var result = FeelAssembly.Assemble(registry, new FeelAssemblyOptions { StepSeconds = Dt, CalibrationId = "feel.calibration.motion_test" });
            Assert.True(result.IsAssembled);
            return result.System!;
        }

        private static Fx Build(
            bool motion = true, StubNavigation2D? nav = null, MovementOptions? options = null,
            string basePreset = "feel.preset.rpg_classic", bool withDummy = false)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var player = new PlayerUnit(HeroId, MapId, FactionId, ArchetypeId) { Position = Vec2.Zero };
            world.AddEntity(player);
            var dummy = new PlayerUnit(DummyId, MapId, FactionId, ArchetypeId) { Position = new Vec2(100, 100) };
            if (withDummy) world.AddEntity(dummy);

            var units = new WorldUnitAccess(world);
            var stats = new FakeStatHost();
            var opts = options ?? new MovementOptions();
            stats.SetBase(HeroId, opts.MoveSpeedStat, Speed);
            var auras = new FakeAuraQuery();
            var host = new MovementHost(world);
            var fx = new Fx { World = world, Units = units, Player = player, Dummy = dummy, Host = host, Auras = auras, Stats = stats };
            host.OnMoveStopped += (id, pos, reason) =>
            {
                if (id.Equals(HeroId)) fx.Stops.Add((reason, pos));
            };

            fx.Feel = AssembleFeel(basePreset);
            if (motion)
            {
                fx.Motion = new MotionServices
                {
                    Feel = fx.Feel.Resolver,
                    Actions = fx.Actions,
                    ActionClock = fx.Clock,
                    Stagger = fx.Stagger,
                    RootMotion = fx.Root,
                };
                host.Motion = fx.Motion;
            }

            var handler = new MovementTickHandler(units, stats, auras, host, bus, nav, opts);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, handler);
            return fx;
        }

        private static void Near(double expected, double actual, double tol = 1e-7) =>
            Assert.True(Math.Abs(expected - actual) <= tol, $"期望 {expected:R}，实测 {actual:R}（容差 {tol}）");

        // ------------------------------------------------------------------ 缺省档案逐位等价

        private static List<long[]> RunLegacyScenario(bool motion)
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(3, -50), new Vec2(4, 50)) });
            var fx = Build(motion, nav, withDummy: true);
            fx.Dummy.Position = new Vec2(-3, 6);
            var rows = new List<long[]>();
            for (var tick = 0; tick < 90; tick++)
            {
                if (tick < 8) fx.World.SubmitIntent(new Intent(HeroId, "move", new JsonObjectBuilder()
                    .Add("dx", new JsonNumber(1)).Add("dy", new JsonNumber(0.4)).Build()));
                else if (tick == 20) fx.MoveTo(-2, 3);
                else if (tick == 45) fx.Chase(DummyId, 1.5);
                else if (tick >= 60 && tick < 90) fx.Move(1, 0.7, tick % 2 == 0 ? "Run" : "Walk");
                fx.Tick();
                var p = fx.Pos;
                rows.Add(new[]
                {
                    BitConverter.DoubleToInt64Bits(p.X), BitConverter.DoubleToInt64Bits(p.Y),
                    BitConverter.DoubleToInt64Bits(fx.Player.Facing), (long)fx.Player.MovementState.Mode,
                });
            }

            return rows;
        }

        [Fact]
        public void DefaultProfile_PositionFacingAndMode_AreBitIdenticalToLegacyMovement()
        {
            var legacy = RunLegacyScenario(motion: false);
            var motion = RunLegacyScenario(motion: true);

            Assert.Equal(legacy.Count, motion.Count);
            for (var i = 0; i < legacy.Count; i++)
            {
                Assert.True(legacy[i].SequenceEqual(motion[i]), $"第 {i} tick 的位置/朝向/模式与既有实现不是逐位一致");
            }

            // 场景确实走过了方向、路径、追击、撞墙四类移动（防止用例空转）。
            Assert.Contains(legacy, r => r[3] == (long)MoveMode.Walk);
            Assert.Contains(legacy, r => r[3] == (long)MoveMode.Run);
        }

        [Fact]
        public void NoMotionServices_StateKeepsRestKinematics_AndNothingChanges()
        {
            var fx = Build(motion: false);
            for (var i = 0; i < 5; i++)
            {
                fx.Move(1, 0);
                fx.Tick();
            }

            Assert.Equal(Speed * Dt * 5, fx.Pos.X, 9);
            Assert.Equal(MotionMode.Grounded, fx.Mo.Mode);
            Assert.Equal(0.0, fx.Mo.Speed);
        }

        // ------------------------------------------------------------------ 加速 / 减速

        [Theory]
        [InlineData(100.0)]
        [InlineData(200.0)]
        [InlineData(250.0)]
        [InlineData(300.0)]
        [InlineData(350.0)]
        public void Accel_LinearRamp_ReachesFullSpeedInCeilTicks_AndNeverExceedsTarget(double accelMs)
        {
            var fx = Build();
            fx.Set(FeelFieldNames.AccelMs, accelMs);

            var ticksReal = accelMs / (Dt * 1000.0);
            var expectedTicks = (int)Math.Ceiling(ticksReal - 1e-9);
            var speeds = fx.RunSpeeds(expectedTicks + 3, 1, 0);

            for (var k = 1; k <= speeds.Count; k++)
            {
                var expected = k < expectedTicks ? Speed * k / ticksReal : Speed;
                Near(expected, speeds[k - 1]);
            }

            // 不变量：单调不减、不超过目标速度、首次达到目标恰在第 ceil(A/步长) 个 tick。
            for (var k = 1; k < speeds.Count; k++) Assert.True(speeds[k] >= speeds[k - 1] - Eps);
            Assert.All(speeds, s => Assert.True(s <= Speed + Eps));
            Assert.Equal(expectedTicks, speeds.FindIndex(s => Math.Abs(s - Speed) <= Eps) + 1);

            // 位移 = 各 tick 速率 × dt 之和（速率取 tick 末值）。
            Near(speeds.Sum() * Dt, fx.Pos.X);
        }

        [Fact]
        public void Accel_EaseInCurve_IsSlowerThanLinearAtTheSameTick_AndStillSnapsToFullSpeed()
        {
            var linear = Build();
            linear.Set(FeelFieldNames.AccelMs, 400.0);
            var eased = Build();
            eased.Set(FeelFieldNames.AccelMs, 400.0);
            eased.Set(FeelFieldNames.AccelCurve, "ease_in");

            var a = linear.RunSpeeds(6, 1, 0);
            var b = eased.RunSpeeds(6, 1, 0);

            Assert.True(b[0] < a[0]);
            Assert.True(b[1] < a[1]);
            Near(Speed, b[3]);
            Near(Speed, b[5]);
            // ease_in：第 k 个 tick 的速率 = 目标 × (k/N)^2。
            Near(Speed * Math.Pow(1.0 / 4.0, 2), b[0]);
            Near(Speed * Math.Pow(2.0 / 4.0, 2), b[1]);
        }

        [Fact]
        public void Decel_ReleasingInput_BrakesToZeroInCeilTicks_StoppingDistanceMatchesDiscreteFormula()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.DecelMs, 200.0);
            fx.RunSpeeds(3, 1, 0); // 瞬时达速（accel_ms = 0），满速 4
            Near(Speed, fx.Mo.Speed);
            var xAtRelease = fx.Pos.X;

            var speeds = new List<double>();
            for (var i = 0; i < 4; i++)
            {
                fx.Tick(); // 松开输入
                speeds.Add(fx.Mo.Speed);
            }

            var n = 200.0 / (Dt * 1000.0); // 2 个 tick
            Near(Speed * (1 - 1 / n), speeds[0]);
            Near(0.0, speeds[1]);
            Near(0.0, speeds[2]);

            var stopDistance = fx.Pos.X - xAtRelease;
            // 离散公式：Σ v·(1 − k/N)·dt（k = 1..N−1）；与连续公式 v·decel/2 相差不超过半个 tick 的位移。
            Near(Speed * Dt * (n - 1) / 2.0, stopDistance);
            var continuous = Speed * (200.0 / 1000.0) / 2.0;
            Assert.True(Math.Abs(stopDistance - continuous) <= Speed * Dt / 2.0 + Eps);
            Assert.Equal(MotionSource.None, fx.Mo.Source);
        }

        [Fact]
        public void Decel_NonIntegerTicks_BrakesWithoutOvershootingZero()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.DecelMs, 250.0);
            fx.RunSpeeds(2, 1, 0);
            var speeds = new List<double>();
            for (var i = 0; i < 4; i++)
            {
                fx.Tick();
                speeds.Add(fx.Mo.Speed);
            }

            Near(2.4, speeds[0]);
            Near(0.8, speeds[1]);
            Near(0.0, speeds[2]);
            Assert.All(speeds, s => Assert.True(s >= 0.0));
        }

        [Fact]
        public void DefaultProfile_ReleasingInput_StopsImmediately()
        {
            var fx = Build();
            fx.RunSpeeds(2, 1, 0);
            var x = fx.Pos.X;
            fx.Tick();
            Assert.Equal(x, fx.Pos.X);
            Assert.Equal(0.0, fx.Mo.Speed);
        }

        // ------------------------------------------------------------------ 反向策略

        [Fact]
        public void Reverse_Instant_FlipsDirectionWithoutLosingSpeed()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.AccelMs, 200.0);
            fx.Set(FeelFieldNames.DecelMs, 200.0);
            fx.RunSpeeds(3, 1, 0);
            Near(Speed, fx.Mo.Speed);
            var x = fx.Pos.X;

            fx.Move(-1, 0);
            fx.Tick();

            Near(-Speed, fx.Mo.Velocity.X);
            Near(x - Speed * Dt, fx.Pos.X);
        }

        [Fact]
        public void Reverse_ThroughZero_BrakesAlongOldDirectionThenAcceleratesAlongNew()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.AccelMs, 200.0);
            fx.Set(FeelFieldNames.DecelMs, 200.0);
            fx.Set(FeelFieldNames.ReversePolicy, "through_zero");
            fx.RunSpeeds(3, 1, 0);
            Near(Speed, fx.Mo.Speed);

            var vx = new List<double>();
            for (var i = 0; i < 4; i++)
            {
                fx.Move(-1, 0);
                fx.Tick();
                vx.Add(fx.Mo.Velocity.X);
            }

            Near(Speed / 2, vx[0]); // 仍沿 +x，已减到一半
            Near(0.0, vx[1]);
            Near(-Speed / 2, vx[2]); // 过零后沿 −x 加速
            Near(-Speed, vx[3]);
        }

        // ------------------------------------------------------------------ 转向速率

        [Theory]
        [InlineData(90.0)]
        [InlineData(300.0)]
        [InlineData(720.0)]
        public void TurnRate_FacingChangesByRateTimesDtPerTick_AndEndsOnTarget(double rateDegS)
        {
            var fx = Build();
            fx.Set(FeelFieldNames.TurnRateDegS, rateDegS);
            var perTick = rateDegS * Dt * Math.PI / 180.0;
            var target = Math.PI / 2;
            var previous = fx.Player.Facing;

            for (var tick = 1; tick <= 40; tick++)
            {
                fx.Move(0, 1);
                fx.Tick();
                var expected = Math.Min(perTick * tick, target);
                Near(expected, fx.Player.Facing, 1e-9);
                var delta = fx.Player.Facing - previous;
                Assert.True(delta >= -Eps && delta <= perTick + Eps);
                previous = fx.Player.Facing;
            }

            Near(target, fx.Player.Facing, 1e-12);
        }

        [Fact]
        public void TurnRate_TakesTheShortestArcAcrossTheWrapPoint()
        {
            // 朝向 170°，目标 −170°（= 190°）：应当顺时针（+）转 20°，而不是反向转 340°。
            var next = MotionMath.StepFacing(170.0 * Math.PI / 180, -170.0 * Math.PI / 180, 90.0, 0.1);
            Near(179.0 * Math.PI / 180, next, 1e-9);
            var done = MotionMath.StepFacing(next, -170.0 * Math.PI / 180, 90.0, 0.1);
            Near(-172.0 * Math.PI / 180, MotionMath.WrapAngle(done), 1e-9);
            Assert.Equal(1.234, MotionMath.StepFacing(0.0, 1.234, 0.0, 0.1)); // 速率 0 = 瞬时到位
        }

        // ------------------------------------------------------------------ 模式仲裁

        [Fact]
        public void Rooted_InputProducesNoDisplacement_ButStillTurns()
        {
            var fx = Build();
            fx.Auras.SetControlFlags(HeroId, ControlFlags.NoMove);
            for (var i = 0; i < 4; i++)
            {
                fx.Move(0, 1);
                fx.Tick();
            }

            Assert.Equal(Vec2.Zero, fx.Pos);
            Assert.Equal(MotionMode.Rooted, fx.Mo.Mode);
            Assert.Equal(0.0, fx.Mo.Speed);
            Near(Math.PI / 2, fx.Player.Facing, 1e-12);
        }

        [Fact]
        public void Knockback_OverridesRootedAndInput_FollowsEaseOutCurve_AndDoesNotTurn()
        {
            var fx = Build();
            fx.Auras.SetControlFlags(HeroId, ControlFlags.NoMove);
            fx.Host.BeginKnockback(new KnockbackRequest(HeroId, new Vec2(-1, 0), 1.0, 0.2));

            fx.Move(0, 1); // 同 tick 的输入既不位移也不转向
            fx.Tick();
            var f = 1.0 - Math.Pow(1.0 - 0.5, 2); // ease_out(0.5)
            Near(-f, fx.Pos.X);
            Near(0.0, fx.Pos.Y);
            Assert.Equal(MotionMode.Forced, fx.Mo.Mode);
            Assert.Equal(MotionSource.Forced, fx.Mo.Source);
            Assert.Equal(0.0, fx.Player.Facing);

            fx.Move(0, 1);
            fx.Tick();
            Near(-1.0, fx.Pos.X);
            Assert.False(fx.Player.MovementState.IsControlledDisplacementActive);
            Assert.Contains(fx.Stops, s => s.Reason == MoveStopReason.DisplacementArrived);
        }

        [Fact]
        public void Knockback_PreemptsHeldInput_ThenInputResumesFromRestWithoutResidualMomentum()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.AccelMs, 200.0);
            fx.RunSpeeds(3, 1, 0);
            var x0 = fx.Pos.X;
            fx.Host.BeginKnockback(new KnockbackRequest(HeroId, new Vec2(-1, 0), 1.0, 0.2));

            fx.Move(1, 0);
            fx.Tick();
            fx.Move(1, 0);
            fx.Tick();
            Near(x0 - 1.0, fx.Pos.X); // 位移只由击退给出，输入被 forced 压制

            fx.Move(1, 0);
            fx.Tick();
            Near(Speed / 2, fx.Mo.Speed); // forced 退出清零动量（规则表 keeps_momentum_on_exit = no），重新加速
            Assert.Equal(MotionMode.Grounded, fx.Mo.Mode);
        }

        [Fact]
        public void Knockback_StackPolicy_ReplaceTakesTheNewOne_IgnoreKeepsTheFirst()
        {
            foreach (var policy in new[] { KnockbackStackPolicy.Replace, KnockbackStackPolicy.Ignore })
            {
                var fx = Build(options: new MovementOptions { KnockbackStack = policy });
                fx.Host.BeginKnockback(new KnockbackRequest(HeroId, new Vec2(-1, 0), 1.0, 0.4));
                fx.Tick(); // p = 0.25 -> f = 0.4375
                var f1 = 1.0 - Math.Pow(0.75, 2);
                Near(-f1, fx.Pos.X);

                fx.Host.BeginKnockback(new KnockbackRequest(HeroId, new Vec2(0, 1), 2.0, 0.4));
                for (var i = 0; i < 6; i++) fx.Tick();

                if (policy == KnockbackStackPolicy.Replace)
                {
                    Near(-f1, fx.Pos.X); // 第二次替换后不再沿 −x 走
                    Near(2.0, fx.Pos.Y);
                    Assert.Contains(fx.Stops, s => s.Reason == MoveStopReason.Replaced);
                }
                else
                {
                    Near(-1.0, fx.Pos.X);
                    Near(0.0, fx.Pos.Y);
                    Assert.DoesNotContain(fx.Stops, s => s.Reason == MoveStopReason.Replaced);
                }
            }
        }

        [Fact]
        public void Knockback_ResumePathPolicy_ContinuesToOriginalTarget_DropPolicyStaysPut()
        {
            foreach (var policy in new[] { ResumePathAfterForcedPolicy.Resume, ResumePathAfterForcedPolicy.Drop })
            {
                var fx = Build(options: new MovementOptions { ResumePathAfterForced = policy });
                fx.MoveTo(10, 0);
                fx.Tick();
                fx.Tick();
                fx.Host.BeginKnockback(new KnockbackRequest(HeroId, new Vec2(0, 1), 1.0, 0.2));
                for (var i = 0; i < 40; i++) fx.Tick();

                if (policy == ResumePathAfterForcedPolicy.Resume)
                {
                    Near(10.0, fx.Pos.X);
                    Near(0.0, fx.Pos.Y); // 回到原目标点（路径重规划为直线到最终目标）
                }
                else
                {
                    Assert.True(fx.Pos.X < 1.0);
                    Near(1.0, fx.Pos.Y);
                }
            }
        }

        [Fact]
        public void Frozen_SuppressesDisplacement_KeepsVelocity_AndRestoresPreviousMode()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.AccelMs, 200.0);
            fx.RunSpeeds(3, 1, 0);
            var x = fx.Pos.X;
            var v = fx.Mo.Velocity;

            fx.Clock.Paused = true;
            for (var i = 0; i < 3; i++)
            {
                fx.Move(1, 0);
                fx.Tick();
                Assert.Equal(x, fx.Pos.X);
                Assert.Equal(MotionMode.Frozen, fx.Mo.Mode);
                Assert.Equal(MotionMode.Grounded, fx.Mo.BaseMode);
                Assert.Equal(v, fx.Mo.Velocity);
            }

            fx.Clock.Paused = false;
            fx.Move(1, 0);
            fx.Tick();
            Near(x + Speed * Dt, fx.Pos.X); // 速度保留，解冻当 tick 即以满速前进
            Assert.Equal(MotionMode.Grounded, fx.Mo.Mode);
        }

        [Fact]
        public void Frozen_HoldsAControlledDisplacementInPlace_UntilThawed()
        {
            var fx = Build();
            fx.Clock.Paused = true;
            fx.Host.BeginKnockback(new KnockbackRequest(HeroId, new Vec2(1, 0), 1.0, 0.2));
            fx.Tick();
            fx.Tick();
            Assert.Equal(0.0, fx.Pos.X);
            Assert.True(fx.Player.MovementState.IsControlledDisplacementActive);

            fx.Clock.Paused = false;
            fx.Tick();
            fx.Tick();
            Near(1.0, fx.Pos.X);
        }

        [Fact]
        public void Dead_SuppressesEverything()
        {
            var fx = Build();
            fx.Player.Alive = false;
            fx.Move(1, 0);
            fx.Tick();
            Assert.Equal(Vec2.Zero, fx.Pos);
            Assert.Equal(MotionMode.Dead, fx.Mo.Mode);
            Assert.Equal(0.0, fx.Mo.Speed);
        }

        [Fact]
        public void Staggered_BlocksInputAndTurning()
        {
            var fx = Build();
            fx.Stagger.Staggered = true;
            fx.Move(0, 1);
            fx.Tick();
            Assert.Equal(Vec2.Zero, fx.Pos);
            Assert.Equal(MotionMode.Staggered, fx.Mo.Mode);
            Assert.Equal(0.0, fx.Player.Facing);
        }

        [Fact]
        public void Priority_DeadFrozenForcedStaggeredRootedActionRegular_EachHigherStateWins()
        {
            var fx = Build();
            var motion = new ActionMotionState(
                new ActionMotion(ActionMotionDriver.Code, ActionMotionKind.Lunge, 1, "linear", ActionMotionDirection.Facing, 0, ActionMotionBlocking.Stop),
                10.0, 0, 100, new Vec2(1, 0));
            fx.Move(1, 0);
            fx.Tick();
            Assert.Equal(MotionMode.Grounded, fx.Mo.Mode);
            Assert.Equal(MotionSource.Regular, fx.Mo.Source);

            fx.Actions.State = new ActionState(new Id("skill.t"), new Id("cast.1"), ActionPhase.Active, 1, 0, motion);
            fx.Move(1, 0);
            fx.Tick();
            Assert.Equal(MotionMode.Action, fx.Mo.Mode);
            Assert.Equal(MotionSource.Action, fx.Mo.Source);

            fx.Auras.SetControlFlags(HeroId, ControlFlags.NoMove);
            var xRooted = fx.Pos.X;
            fx.Move(1, 0);
            fx.Tick();
            Assert.Equal(MotionMode.Rooted, fx.Mo.Mode);
            Assert.Equal(xRooted, fx.Pos.X); // rooted 压制 action 位移

            fx.Stagger.Staggered = true;
            fx.Tick();
            Assert.Equal(MotionMode.Staggered, fx.Mo.Mode);

            fx.Host.BeginKnockback(new KnockbackRequest(HeroId, new Vec2(0, 1), 5.0, 1.0));
            fx.Tick();
            Assert.Equal(MotionMode.Forced, fx.Mo.Mode);
            Assert.Equal(MotionSource.Forced, fx.Mo.Source);
            Assert.True(fx.Pos.Y > 0.0);

            fx.Clock.Paused = true;
            var y = fx.Pos.Y;
            fx.Tick();
            Assert.Equal(MotionMode.Frozen, fx.Mo.Mode);
            Assert.Equal(MotionMode.Forced, fx.Mo.BaseMode);
            Assert.Equal(y, fx.Pos.Y);

            fx.Player.Alive = false;
            fx.Tick();
            Assert.Equal(MotionMode.Dead, fx.Mo.Mode);
            Assert.Equal(MotionSource.None, fx.Mo.Source);
        }

        // ------------------------------------------------------------------ 滑墙

        private static (double X, double Y, List<double> Xs, List<double> Ys) RunAlongWall(bool wallSlide, int ticks = 20)
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(2, -50), new Vec2(3, 50)) });
            var fx = Build(nav: nav);
            fx.Set(FeelFieldNames.WallSlide, wallSlide);
            var xs = new List<double>();
            var ys = new List<double>();
            for (var i = 0; i < ticks; i++)
            {
                fx.Move(1, 1);
                fx.Tick();
                xs.Add(fx.Pos.X);
                ys.Add(fx.Pos.Y);
            }

            return (fx.Pos.X, fx.Pos.Y, xs, ys);
        }

        [Fact]
        public void WallSlide_OnVersusOff_DiagonalInputKeepsSlidingAlongTheWall()
        {
            var off = RunAlongWall(false);
            var on = RunAlongWall(true);
            var wallX = 2.0;

            // 关：撞墙后整体停下（与既有行为一致）。开：沿墙切向继续。
            Assert.True(off.X < wallX && on.X < wallX);
            var diagStep = Speed * Dt / Math.Sqrt(2.0);
            var impactIndex = off.Xs.FindIndex(x => x > wallX - 0.05);
            Assert.True(impactIndex >= 0);
            for (var i = impactIndex + 2; i < off.Ys.Count; i++)
            {
                Assert.Equal(off.Ys[impactIndex + 1], off.Ys[i]); // 关闭滑墙：撞墙后 y 方向也停止推进
                Assert.Equal(off.Xs[impactIndex + 1], off.Xs[i]);
            }

            // 开：撞墙后 x 不再变化（法向分量为零、无抖动），y 每 tick 推进 速度 × dt × 切向分量。
            for (var i = impactIndex + 2; i < on.Xs.Count; i++)
            {
                Assert.Equal(on.Xs[impactIndex + 1], on.Xs[i]);
                Near(on.Ys[i - 1] + diagStep, on.Ys[i], 1e-9);
            }

            Assert.True(on.Y > off.Y + diagStep * 5);
            // 单调不减（无回退抖动）。
            for (var i = 1; i < on.Ys.Count; i++) Assert.True(on.Ys[i] >= on.Ys[i - 1] - Eps);
            for (var i = 1; i < on.Xs.Count; i++) Assert.True(on.Xs[i] >= on.Xs[i - 1] - Eps);
        }

        [Fact]
        public void WallSlide_AtACorner_StopsInsteadOfSlidingThrough()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[]
            {
                new Rect(new Vec2(2, -50), new Vec2(3, 50)), new Rect(new Vec2(-50, 2), new Vec2(50, 3)),
            });
            var fx = Build(nav: nav);
            fx.Set(FeelFieldNames.WallSlide, true);
            for (var i = 0; i < 30; i++)
            {
                fx.Move(1, 1);
                fx.Tick();
            }

            Assert.True(fx.Pos.X < 2.0 && fx.Pos.Y < 2.0);
        }

        // ------------------------------------------------------------------ 路径跟随 / 到达减速

        [Fact]
        public void PathFollowing_WithProfile_RampsUpBrakesIntoTheTarget_AndNeverOvershoots()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.ApplyToPathFollowing, true);
            fx.Set(FeelFieldNames.AccelMs, 200.0);
            fx.Set(FeelFieldNames.DecelMs, 400.0);
            fx.Set(FeelFieldNames.ArrivalDecel, true);

            fx.MoveTo(10, 0);
            var speeds = new List<double>();
            var xs = new List<double>();
            for (var i = 0; i < 80; i++)
            {
                fx.Tick();
                speeds.Add(fx.Mo.Speed);
                xs.Add(fx.Pos.X);
                if (fx.Player.MovementState.CurrentPath == null) break;
            }

            Near(Speed / 2, speeds[0]); // 第 1 个 tick：accel_ms = 200 -> 2 tick 达速
            Assert.All(xs, x => Assert.True(x <= 10.0 + Eps));
            Assert.Null(fx.Player.MovementState.CurrentPath);
            Near(10.0, fx.Pos.X, 1e-9);
            Assert.Equal(0.0, fx.Mo.Speed);
            Assert.Equal(Speed, speeds.Max(), 6);
            // 到达前速度单调下降（减速段）。
            var peak = speeds.IndexOf(speeds.Max());
            for (var i = speeds.Count - 1; i > peak + 1; i--) Assert.True(speeds[i] <= speeds[i - 1] + Eps);
            Assert.True(xs.Count > 10.0 / (Speed * Dt)); // 比瞬时达速的 25 tick 慢
        }

        [Fact]
        public void PathFollowing_DefaultProfile_IgnoresProfileFields_LikeLegacy()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.AccelMs, 500.0); // apply_to_path_following 缺省 false：路径跟随不受影响
            fx.MoveTo(10, 0);
            fx.Tick();
            Near(Speed * Dt, fx.Pos.X); // 路径跟随不受 accel_ms 影响：第 1 个 tick 即满速
            fx.Tick();
            Near(Speed * Dt * 2, fx.Pos.X);
        }

        // ------------------------------------------------------------------ 动作位移

        private static ActionMotionState Lunge(
            double distance, int start, int end, Vec2 direction, string curve = "linear",
            ActionMotionBlocking blocking = ActionMotionBlocking.Stop, ActionMotionKind kind = ActionMotionKind.Lunge,
            ActionMotionDriver driver = ActionMotionDriver.Code, Id? target = null, double stopDistance = 0.0,
            ActionMotionDirection dir = ActionMotionDirection.Facing, double maxTurnDeg = 0.0) =>
            new ActionMotionState(
                new ActionMotion(driver, kind, 1.0, curve, dir, maxTurnDeg, blocking), distance, start, end, direction, target, stopDistance);

        private static ActionState Act(int elapsed, ActionMotionState motion) =>
            new ActionState(new Id("skill.t"), new Id("cast.1"), ActionPhase.Active, elapsed, 0, motion);

        [Theory]
        [InlineData("linear")]
        [InlineData("ease_out")]
        [InlineData("ease_in_out")]
        public void ActionLunge_MovesExactlyTheDeclaredDistanceInsideTheWindow_AndSuppressesInput(string curve)
        {
            var fx = Build();
            var motion = Lunge(2.0, 2, 5, new Vec2(1, 0), curve);
            var xs = new List<double>();
            var yDuringAction = 0.0;
            for (var i = 0; i < 8; i++)
            {
                fx.Actions.State = i <= 6 ? Act(i, motion) : (ActionState?)null;
                fx.Move(0, 1); // 动作期间输入位移被压制（action_move_speed_ratio = 0）
                fx.Tick();
                xs.Add(fx.Pos.X);
                if (i == 3) Assert.Equal(MotionSource.Action, fx.Mo.Source);
                if (i == 6) yDuringAction = fx.Pos.Y;
            }

            // 窗口 tick 2、3、4：逐 tick 位移 = D × (f(p1) − f(p0))。
            double F(double p) => MotionMath.EvalCurve(curve, p, null);
            Near(0.0, xs[1]);
            Near(2.0 * F(1.0 / 3), xs[2]);
            Near(2.0 * F(2.0 / 3), xs[3]);
            Near(2.0, xs[4]);
            Near(2.0, xs[7]); // 窗口之后不再移动
            // 动作期间（tick 0..6）没有 y 位移，动作结束后（tick 7）输入才恢复。
            Assert.Equal(0.0, yDuringAction);
            Near(Speed * Dt, fx.Pos.Y);
        }

        [Fact]
        public void ActionLunge_AfterTheAction_InputResumes()
        {
            var fx = Build();
            fx.Actions.State = Act(0, Lunge(1.0, 0, 2, new Vec2(1, 0)));
            fx.Move(0, 1);
            fx.Tick();
            fx.Actions.State = null;
            fx.Move(0, 1);
            fx.Tick();
            Near(Speed * Dt, fx.Pos.Y);
        }

        [Fact]
        public void ActionLunge_StopsBeforeAWall_AndSlideKeepsTheTangentialComponent()
        {
            double Run(ActionMotionBlocking blocking)
            {
                var nav = new StubNavigation2D();
                nav.SetBlocking(MapId, new[] { new Rect(new Vec2(1.2, -50), new Vec2(2, 50)) });
                var fx = Build(nav: nav);
                var dir = new Vec2(Math.Sqrt(0.5), Math.Sqrt(0.5));
                var motion = Lunge(3.0, 0, 3, dir, blocking: blocking);
                for (var i = 0; i < 6; i++)
                {
                    fx.Actions.State = i < 3 ? Act(i, motion) : (ActionState?)null;
                    fx.Tick();
                }

                Assert.True(fx.Pos.X < 1.2);
                return fx.Pos.Y;
            }

            var stopY = Run(ActionMotionBlocking.Stop);
            var slideY = Run(ActionMotionBlocking.Slide);
            Assert.True(slideY > stopY + 0.3, $"Slide 的 y {slideY} 应明显大于 Stop 的 y {stopY}");
            Assert.True(slideY <= 3.0 * Math.Sqrt(0.5) + 1e-9);
        }

        [Fact]
        public void ActionCharge_StopsAtTheStopDistanceInFrontOfTheTarget_NeverBeyondTheDeclaredDistance()
        {
            var fx = Build(withDummy: true);
            fx.Dummy.Position = new Vec2(5, 0);
            var motion = Lunge(
                10.0, 0, 10, new Vec2(1, 0), kind: ActionMotionKind.Charge, target: DummyId, stopDistance: 1.0,
                dir: ActionMotionDirection.TowardTarget, maxTurnDeg: 90);
            for (var i = 0; i < 10; i++)
            {
                fx.Actions.State = Act(i, motion);
                fx.Tick();
                Assert.True(fx.Pos.X <= 4.0 + Eps);
            }

            Near(4.0, fx.Pos.X);

            // 声明距离更短时受距离上限约束：目标在 5，最大距离 2 -> 只走 2。
            var fx2 = Build(withDummy: true);
            fx2.Dummy.Position = new Vec2(5, 0);
            var shortMotion = Lunge(
                2.0, 0, 10, new Vec2(1, 0), kind: ActionMotionKind.Charge, target: DummyId, stopDistance: 1.0,
                dir: ActionMotionDirection.TowardTarget, maxTurnDeg: 90);
            for (var i = 0; i < 10; i++)
            {
                fx2.Actions.State = Act(i, shortMotion);
                fx2.Tick();
            }

            Near(2.0, fx2.Pos.X);
        }

        [Fact]
        public void ActionRootMotion_UsesTheAdapterDelta_AndUnsupportedAdapterIsAnErrorNotAFallback()
        {
            var motion = Lunge(2.0, 0, 5, new Vec2(1, 0), driver: ActionMotionDriver.RootMotion);

            var supported = Build();
            supported.Root.Supported = true;
            supported.Root.Delta = new Vec2(0.3, 0.1);
            for (var i = 0; i < 3; i++)
            {
                supported.Actions.State = Act(i, motion);
                supported.Tick();
            }

            Near(0.9, supported.Pos.X);
            Near(0.3, supported.Pos.Y);
            Assert.Equal(MotionSource.RootMotion, supported.Mo.Source);

            var unsupported = Build();
            unsupported.Root.Supported = false;
            unsupported.Actions.State = Act(0, motion);
            var ex = Assert.ThrowsAny<Exception>(() => unsupported.Tick());
            Assert.Contains("root_motion", ex.ToString());
            Assert.Equal(Vec2.Zero, unsupported.Pos); // 不降级为代码驱动
        }

        [Fact]
        public void ActionMode_ByProfileRatioAndTurnLock_ControlInputDuringTheAction()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.ActionMoveSpeedRatio, 0.5);
            fx.Set(FeelFieldNames.ActionTurnLock, false);
            fx.Actions.State = Act(0, Lunge(0.0, 50, 60, new Vec2(1, 0))); // 动作进行中但不在位移窗口
            fx.Move(0, 1);
            fx.Tick();
            Near(Speed * 0.5 * Dt, fx.Pos.Y);
            Near(Math.PI / 2, fx.Player.Facing, 1e-12);
            Assert.Equal(MotionMode.Action, fx.Mo.Mode);

            var locked = Build();
            locked.Set(FeelFieldNames.ActionMoveSpeedRatio, 0.5);
            locked.Actions.State = Act(0, Lunge(0.0, 50, 60, new Vec2(1, 0)));
            locked.Move(0, 1);
            locked.Tick();
            Assert.Equal(0.0, locked.Player.Facing); // action_turn_lock 缺省 true
        }

        [Fact]
        public void ActionEnd_MomentumKeptOrDroppedByKeepMomentumOnActionEnd()
        {
            foreach (var keep in new[] { true, false })
            {
                var fx = Build();
                fx.Set(FeelFieldNames.AccelMs, 200.0);
                fx.Set(FeelFieldNames.DecelMs, 200.0);
                fx.Set(FeelFieldNames.KeepMomentumOnActionEnd, keep);
                fx.RunSpeeds(3, 1, 0);
                fx.Actions.State = Act(0, Lunge(0.0, 50, 60, new Vec2(1, 0)));
                fx.Tick();
                fx.Actions.State = null;
                fx.Move(1, 0);
                fx.Tick();
                // 保留动量：从动作期间衰减后的速度继续加速；不保留：从零重新加速（accel 2 tick -> 2.0）。
                if (keep) Assert.True(fx.Mo.Speed > Speed / 2 + 1e-6);
                else Near(Speed / 2, fx.Mo.Speed);
            }
        }

        // ------------------------------------------------------------------ 规则表 / 档案读取 / 纯函数

        [Fact]
        public void ModeRuleSet_Default_EqualsTheFrameworkDataFile_ForEveryModeAndJudgment()
        {
            var source = new InMemoryDataSource();
            foreach (var (table, json) in FrameworkTables("feel.preset.rpg_classic")) source.Add(table, json);
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            FeelSchemas.RegisterAll(registry);
            registry.LoadAll();
            var profiles = FeelProfileSet.FromRegistry(registry, FeelFields.Default);
            var fromData = MotionModeRuleSet.FromProfiles(profiles);
            var def = MotionModeRuleSet.Default;
            Assert.Equal(7, profiles.MotionModeRules.Count);

            foreach (var profile in new[] { MotionProfile.LegacyEquivalent, ActionProfile(0.5, false, true) })
            {
                for (var mode = MotionMode.Grounded; mode <= MotionMode.Dead; mode++)
                {
                    Assert.Equal(def.AcceptsInput(mode, profile), fromData.AcceptsInput(mode, profile));
                    Assert.Equal(def.AllowsTurn(mode, profile), fromData.AllowsTurn(mode, profile));
                    Assert.Equal(def.KeepsMomentumOnExit(mode, profile), fromData.KeepsMomentumOnExit(mode, profile));
                    Assert.Equal(def.ZeroesMomentumOnExit(mode, profile), fromData.ZeroesMomentumOnExit(mode, profile));
                }
            }
        }

        private static MotionProfile ActionProfile(double ratio, bool turnLock, bool keep) =>
            new MotionProfile(1, 0, 0, "linear", "linear", ReversePolicy.Instant, 0, 1.0, ratio, turnLock, keep, false, false, false, null);

        [Fact]
        public void ModeRuleSet_DesignTable_MatchesTheDocument()
        {
            var r = MotionModeRuleSet.Default;
            var p = ActionProfile(0.5, true, false);
            Assert.True(r.AcceptsInput(MotionMode.Grounded, p));
            Assert.True(r.AcceptsInput(MotionMode.Action, p));
            foreach (var m in new[] { MotionMode.Forced, MotionMode.Staggered, MotionMode.Rooted, MotionMode.Frozen, MotionMode.Dead })
            {
                Assert.False(r.AcceptsInput(m, p));
            }

            Assert.True(r.AllowsTurn(MotionMode.Rooted, p));
            Assert.False(r.AllowsTurn(MotionMode.Action, p)); // action_turn_lock = true
            Assert.False(r.AllowsTurn(MotionMode.Forced, p));
            Assert.False(r.AcceptsInput(MotionMode.Action, ActionProfile(0.0, true, false)));
        }

        [Fact]
        public void ModeRuleSet_RejectsByProfileOutsideActionAndRestoreOutsideFrozen()
        {
            Assert.Throws<ArgumentException>(() => MotionModeRuleSet.FromRules(new[]
            {
                new FeelMotionModeRule("grounded", "by_profile", "yes", "none"),
            }));
            Assert.Throws<ArgumentException>(() => MotionModeRuleSet.FromRules(new[]
            {
                new FeelMotionModeRule("rooted", "no", "yes", "restore_previous_mode"),
            }));
            // 游戏可以改表：硬直中允许缓慢转向。
            var custom = MotionModeRuleSet.FromRules(new[]
            {
                new FeelMotionModeRule("staggered", "no", "yes", "no"),
            });
            Assert.True(custom.AllowsTurn(MotionMode.Staggered, MotionProfile.LegacyEquivalent));
        }

        [Fact]
        public void MotionProfile_ReadsEveryMotionField_FromTheResolvedJudgingView()
        {
            var fx = Build(basePreset: "feel.preset.arpg_responsive");
            var p = MotionProfile.Read(fx.Feel.Resolver.ResolveJudging(HeroId));
            Assert.Equal(90.0, p.AccelMs);
            Assert.Equal(70.0, p.DecelMs);
            Assert.Equal(ReversePolicy.Instant, p.Reverse);
            Assert.Equal(720.0, p.TurnRateDegS);
            Assert.Equal(0.5, p.WalkSpeedRatio);
            Assert.True(p.WallSlide && p.ArrivalDecel && p.ApplyToPathFollowing);

            var legacy = MotionProfile.Read(Build().Feel.Resolver.ResolveJudging(HeroId));
            Assert.Equal(0.0, legacy.AccelMs);
            Assert.Equal(0.0, legacy.DecelMs);
            Assert.Equal(0.0, legacy.TurnRateDegS);
            Assert.Equal(1.0, legacy.WalkSpeedRatio);
            Assert.False(legacy.WallSlide || legacy.ArrivalDecel || legacy.ApplyToPathFollowing);
        }

        [Fact]
        public void ArpgResponsivePreset_WalkRatioAndProfileChangesTakeEffectNextTick()
        {
            var fx = Build(basePreset: "feel.preset.arpg_responsive");
            fx.Set(FeelFieldNames.AccelMs, 0.0);
            fx.Set(FeelFieldNames.TurnRateDegS, 0.0);
            fx.Move(1, 0, "Walk");
            fx.Tick();
            Near(Speed * 0.5 * Dt, fx.Pos.X); // walk_speed_ratio = 0.5

            fx.Set(FeelFieldNames.WalkSpeedRatio, 1.0); // 热改：下一 tick 生效
            fx.Move(1, 0, "Walk");
            fx.Tick();
            Near(Speed * 0.5 * Dt + Speed * Dt, fx.Pos.X);
        }

        [Fact]
        public void MotionMath_CurveInversionRoundTrips_AndTicksIgnoreFloatingPointNoise()
        {
            foreach (var curve in new[] { "linear", "ease_in", "ease_out", "ease_in_out" })
            {
                for (var i = 0; i <= 20; i++)
                {
                    var y = i / 20.0;
                    var t = MotionMath.InvertCurve(curve, y, null);
                    Near(y, MotionMath.EvalCurve(curve, t, null), 1e-9);
                }

                // 单调不减。
                var last = 0.0;
                for (var i = 0; i <= 100; i++)
                {
                    var v = MotionMath.EvalCurve(curve, i / 100.0, null);
                    Assert.True(v >= last - Eps);
                    last = v;
                }
            }

            Assert.Equal(6.0, MotionMath.ToTicks(100.0, 1.0 / 60.0));
            Assert.Equal(3.0, MotionMath.ToTicks(300.0, 0.1));
            Assert.Equal(1.0, MotionMath.ToTicks(1.0, 1.0 / 60.0)); // 非零至少 1 tick
            Assert.Equal(0.0, MotionMath.ToTicks(0.0, 0.1));
            Assert.Throws<InvalidOperationException>(() => MotionMath.EvalCurve("custom:missing", 0.5, null));
            Assert.Throws<InvalidOperationException>(() => MotionMath.EvalCurve("bogus", 0.5, null));
        }

        [Fact]
        public void MotionMath_ArrivalLimitedSpeed_NeverAllowsOvershootingTheStopPoint()
        {
            var a = Speed / 0.4; // base 4，decel 400ms
            for (var l = 0.0; l <= 3.0; l += 0.25)
            {
                var v = MotionMath.ArrivalLimitedSpeed(Speed, Speed, 400.0, l);
                Assert.True(v * v / (2 * a) <= l + 1e-9);
                Assert.True(v <= Speed + Eps);
            }

            Assert.Equal(Speed, MotionMath.ArrivalLimitedSpeed(Speed, Speed, 0.0, 0.0));
        }

        [Fact]
        public void TargetAssist_FacingCorrectionIsCappedAndOutOfRangeCandidatesAreSilent()
        {
            var request = new TargetAssistRequest(HeroId, "chain.nearest", 5.0, 45.0, TargetAssistMode.FaceOnly);
            var at30 = new TargetAssistCandidate(DummyId, new Vec2(3 * Math.Cos(30 * Math.PI / 180), 3 * Math.Sin(30 * Math.PI / 180)));
            Assert.True(TargetAssistEvaluator.TryEvaluate(Vec2.Zero, 0.0, at30, request, 20.0, 2.0, 1.0, out var capped));
            Near(20.0, capped.FacingDeltaDeg, 1e-9);
            Assert.True(TargetAssistEvaluator.TryEvaluate(Vec2.Zero, 0.0, at30, request, 90.0, 2.0, 1.0, out var full));
            Near(30.0, full.FacingDeltaDeg, 1e-9);
            Assert.Equal(0.0, full.DistanceAdjust);

            var at60 = new TargetAssistCandidate(DummyId, new Vec2(3 * Math.Cos(60 * Math.PI / 180), 3 * Math.Sin(60 * Math.PI / 180)));
            Assert.False(TargetAssistEvaluator.TryEvaluate(Vec2.Zero, 0.0, at60, request, 90.0, 2.0, 1.0, out _));
            var far = new TargetAssistCandidate(DummyId, new Vec2(6, 0));
            Assert.False(TargetAssistEvaluator.TryEvaluate(Vec2.Zero, 0.0, far, request, 90.0, 2.0, 1.0, out _));

            var close = new TargetAssistRequest(HeroId, "chain.nearest", 5.0, 45.0, TargetAssistMode.CloseDistance);
            var at25 = new TargetAssistCandidate(DummyId, new Vec2(2.5, 0));
            Assert.True(TargetAssistEvaluator.TryEvaluate(Vec2.Zero, 0.0, at25, close, 90.0, 2.0, 1.0, out var shrunk));
            Near(-0.5, shrunk.DistanceAdjust, 1e-9); // 只需 2.5 − 1.0 = 1.5，比声明的 2.0 短 0.5
            var at4 = new TargetAssistCandidate(DummyId, new Vec2(4, 0));
            Assert.True(TargetAssistEvaluator.TryEvaluate(Vec2.Zero, 0.0, at4, close, 90.0, 2.0, 1.0, out var capped2));
            Near(0.0, capped2.DistanceAdjust, 1e-9); // 需要 3.0，但不超过声明距离 2.0
        }

        [Fact]
        public void Knockback_DistanceUsesCalibratedBodyHeightsAndResistance()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.KnockbackDistance, 0.5); // 身高倍数；标定参考身高 2 -> 1.0 世界单位
            fx.Set(FeelFieldNames.KnockbackResistanceStat, "stat.kb_res");
            var stat = new Id("stat.kb_res");
            fx.Stats.SetBase(HeroId, stat, 0.25);
            var view = fx.Feel.Resolver.ResolveJudging(HeroId);

            var resistance = MotionKnockback.ReadResistance(view, fx.Stats, HeroId);
            Near(0.25, resistance);
            Near(1.0 * 0.75 * 1.5, MotionKnockback.ComputeDistanceWorld(view, resistance, 1.5));
            Near(1.0, MotionKnockback.ComputeDistanceWorld(view, 0.0));
            Near(0.0, MotionKnockback.ComputeDistanceWorld(view, 1.0)); // 抗性满 = 不被击退
            fx.Stats.SetBase(HeroId, stat, 7.0);
            Near(1.0, MotionKnockback.ReadResistance(view, fx.Stats, HeroId)); // 越界夹取
        }

        [Fact]
        public void Gait_ExportsCurrentSpeedRatioAndThePresentingThresholds()
        {
            var fx = Build(basePreset: "feel.preset.arpg_responsive");
            fx.Set(FeelFieldNames.AccelMs, 0.0);
            fx.Move(1, 0);
            fx.Tick();
            var view = fx.Feel.Resolver.ResolvePresenting(HeroId);
            var gait = GaitInputs.From(fx.Mo, view);

            Near(1.0, gait.SpeedRatio);
            Near(Speed, gait.Velocity.X);
            Near(view.GetRaw(FeelFieldNames.IdleMaxRatio).AsNumber(), gait.IdleMaxRatio);
            Near(view.GetRaw(FeelFieldNames.WalkMaxRatio).AsNumber(), gait.WalkMaxRatio);
            Near(view.GetRaw(FeelFieldNames.GaitHysteresisRatio).AsNumber(), gait.HysteresisRatio);
            Assert.Equal(0.0, MotionKinematics.Rest.SpeedRatio);
        }

        [Fact]
        public void MovementState_ExistingConstructorsKeepRestKinematics_AndWithMotionRoundTrips()
        {
            var state = new MovementState(null, MoveMode.Run, false, 0);
            Assert.Equal(MotionMode.Grounded, state.MotionMode);
            Assert.Equal(Vec2.Zero, state.Velocity);
            var kin = new MotionKinematics(new Vec2(1, 2), new Vec2(0, 1), MotionMode.Action, MotionMode.Action, MotionSource.Action, 4.0);
            var withMotion = state.WithMotion(kin);
            Assert.Equal(new Vec2(1, 2), withMotion.Velocity);
            Assert.Equal(MotionSource.Action, withMotion.MotionSource);
            Assert.Equal(new Vec2(0, 1), withMotion.DesiredDirection);
            Assert.Equal(MoveMode.Run, withMotion.Mode);
            Assert.Equal(new Vec2(1, 2), withMotion.WithLocked(true).Velocity); // WithLocked 保留运动学
        }
    }
}
