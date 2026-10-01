// HitReactionMotionTests：手感设计/03（ADR-0117）受击裁决与运动层的接线冒烟——用真实 MovementTickHandler、真实运动仲裁器、
// 真实行动者动作时钟与受击裁决宿主（命中事件由测试里的替身发射器给出）。期望值全部由档案毫秒与标定 tick 率算出。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.SimLoop;
using Core.Rules.Assembly;
using Core.Rules.Combat;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Unit
{
    public class HitReactionMotionTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id FactionId = new Id("fac.player");
        private static readonly Id ArchetypeId = new Id("arch.class.sample");
        private static readonly Id HeroId = new Id("unit.hero");
        private static readonly Id AttackerId = new Id("unit.attacker");
        private static readonly Id SkillId = new Id("skill.hr_swing");

        private const double Dt = 1.0 / 60.0;
        private const double Speed = 4.0;

        private static int Ticks(double ms) => FeelCalibration.MillisecondsToTicks(ms, Dt);

        private sealed class Fx
        {
            public EventBus Bus = null!;
            public WorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public PlayerUnit Hero = null!;
            public PlayerUnit Attacker = null!;
            public MovementHost Movement = null!;
            public FeelSystem Feel = null!;
            public HitFeelSystem Sys = null!;
            public FakeStatHost Stats = null!;
            public long TickNo;
            public readonly List<(long Tick, MotionMode Mode, Vec2 Pos)> Trace = new List<(long, MotionMode, Vec2)>();

            public void Set(string field, double value) =>
                Feel.DebugOverrides!.SetGlobal(new FeelWrite(field, FeelOp.Set, FeelValue.Of(value)));

            public void Set(string field, string value) =>
                Feel.DebugOverrides!.SetGlobal(new FeelWrite(field, FeelOp.Set, FeelValue.Of(value)));

            public double Number(string field) => Feel.Resolver.ResolveJudging(HeroId).GetNumber(field);

            public void Tick(bool holdInput = false)
            {
                if (holdInput)
                {
                    World.SubmitIntent(new Intent(HeroId, "move", new JsonObjectBuilder()
                        .Add("dx", new JsonNumber(0)).Add("dy", new JsonNumber(1)).Add("mode", new JsonString("Run")).Build()));
                }

                World.Tick(SimStep.Continuous(Dt));
                Trace.Add((TickNo, Hero.MovementState.Motion.Mode, Units.GetPosition(HeroId)));
                TickNo++;
            }

            public void Run(int ticks, bool holdInput = false)
            {
                for (var i = 0; i < ticks; i++) Tick(holdInput);
            }

            public void Hit(bool kill = false)
            {
                var outcome = Sys.Host.Evaluate(new HitFeelInput(AttackerId, HeroId, HitResult.Hit, 10, kill));
                var from = Units.GetPosition(AttackerId);
                var to = Units.GetPosition(HeroId);
                var dir = to - from;
                dir = dir * (1.0 / dir.Length);
                Bus.Enqueue(new CombatHitConfirmedEvent(
                    new Id("attack.hr." + TickNo), 0, AttackerId, HeroId, SkillId, HitResult.Hit, 10, 0.01, false, kill,
                    to, -dir, dir, outcome.ImpactClass, outcome.AttackerHitStopTicks, outcome.TargetHitStopTicks, outcome.Reaction));
            }
        }

        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
            for (var i = 0; i < 4; i++) dir = dir.Parent!;
            return dir.FullName;
        }

        private static FeelSystem AssembleFeel()
        {
            var dir = Path.Combine(FindRepoRoot(), "data", "_feel", "feel");
            var files = Directory.GetFiles(dir, "*.json");
            Array.Sort(files, StringComparer.Ordinal);
            var source = new InMemoryDataSource();
            foreach (var f in files) source.Add(Path.GetFileNameWithoutExtension(f), File.ReadAllText(f));
            source.Add("feel.calibration",
                "{ \"table\": \"feel.calibration\", \"schema_version\": 1, \"rows\": [ { \"id\": \"feel.calibration.hr_test\", " +
                "\"base_preset\": \"feel.preset.rpg_classic\", \"reference_height\": 2.0, \"base_speed\": 4.0, \"animation_fps\": 30.0, " +
                "\"reference_camera_height\": 10.0, \"reference_zoom\": 1.0, \"pixels_per_unit\": 32.0, \"marker_tolerance_ms\": 50.0 } ] }");
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            FeelSchemas.RegisterAll(registry);
            Assert.Equal(0, registry.LoadAll().ErrorCount);
            var result = FeelAssembly.Assemble(registry, new FeelAssemblyOptions { StepSeconds = Dt, CalibrationId = "feel.calibration.hr_test" });
            Assert.True(result.IsAssembled);
            return result.System!;
        }

        /// <param name="profile">true 写入一组有顿帧/硬直的档案；false 保持框架缺省预设 rpg_classic。</param>
        /// <param name="wired">是否把受击裁决接进运动层（false 即没有手感系统的老行为）。</param>
        private static Fx Build(bool profile, bool wired = true)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var hero = new PlayerUnit(HeroId, MapId, FactionId, ArchetypeId) { Position = Vec2.Zero };
            var attacker = new PlayerUnit(AttackerId, MapId, FactionId, ArchetypeId) { Position = new Vec2(-1, 0) };
            world.AddEntity(hero);
            world.AddEntity(attacker);

            var units = new WorldUnitAccess(world);
            var stats = new FakeStatHost();
            var options = new MovementOptions();
            stats.SetBase(HeroId, options.MoveSpeedStat, Speed);
            stats.SetBase(AttackerId, options.MoveSpeedStat, Speed);
            var auras = new FakeAuraQuery();
            var movement = new MovementHost(world);
            var feel = AssembleFeel();
            var fx = new Fx
            {
                Bus = bus, World = world, Units = units, Hero = hero, Attacker = attacker, Movement = movement, Feel = feel, Stats = stats,
            };

            movement.Motion = new MotionServices { Feel = feel.Resolver };
            var handler = new MovementTickHandler(units, stats, auras, movement, bus, null, options);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, handler);

            if (wired)
            {
                fx.Sys = HitFeelAssembly.Attach(bus, units, feel.Resolver, stats, Dt, new HitFeelOptions());
                MotionHitFeelWiring.Connect(movement, fx.Sys.Clock, fx.Sys.Host);
            }

            if (profile)
            {
                fx.Set(FeelFieldNames.ImpactClass, "medium");
                fx.Set(FeelFieldNames.AttackerHitstopMs, 50);
                fx.Set(FeelFieldNames.TargetHitstopMs, 80);
                fx.Set(FeelFieldNames.HitstopCapMs, 200);
                fx.Set(FeelFieldNames.AttackerHitstopCapMs, 100);
                fx.Set(FeelFieldNames.HitStunMs, 230);
                fx.Set(FeelFieldNames.StaggerPower, 5);
                fx.Set(FeelFieldNames.ReactionCap, "knockdown");
                fx.Set(FeelFieldNames.KillHitstopScale, 1.5);
                fx.Set(FeelFieldNames.DownedMs, 300);
            }

            return fx;
        }

        [Fact]
        public void HitstopThenStagger_ArbiterSelectsFrozenThenStaggered_AndInputIsBlockedThroughBoth()
        {
            var fx = Build(profile: true);
            var hitstop = Ticks(fx.Number(FeelFieldNames.TargetHitstopMs));
            var stun = Ticks(fx.Number(FeelFieldNames.HitStunMs));
            Assert.True(hitstop > 0 && stun > 0 && hitstop != stun);

            fx.Run(3, holdInput: true); // 正常行走几个 tick
            var walked = fx.Trace.Last().Pos;
            Assert.True(walked.Y > 0);

            fx.Hit();
            fx.Tick(holdInput: true); // 命中 tick
            var hitIndex = fx.Trace.Count - 1;
            var posAtHit = fx.Trace[hitIndex].Pos;
            fx.Run(hitstop + stun + 5, holdInput: true);

            var after = fx.Trace.Skip(hitIndex + 1).ToList();
            // 顿帧：hitstop 个 tick 的 Frozen；随后硬直：stun 个 tick 的 Staggered；之后回到常规。
            Assert.All(after.Take(hitstop), t => Assert.Equal(MotionMode.Frozen, t.Mode));
            Assert.All(after.Skip(hitstop).Take(stun), t => Assert.Equal(MotionMode.Staggered, t.Mode));
            Assert.Equal(MotionMode.Grounded, after[hitstop + stun].Mode);
            // 冻结与硬直期间位置一动不动（输入被挡掉），之后恢复位移。
            Assert.All(after.Take(hitstop + stun), t => Assert.Equal(posAtHit, t.Pos));
            Assert.True(after[hitstop + stun].Pos.Y > posAtHit.Y);
        }

        [Fact]
        public void PoiseNotBroken_NeverEntersStaggeredMode_OnlyTheHitstopFreezes()
        {
            var fx = Build(profile: true);
            var hitstop = Ticks(fx.Number(FeelFieldNames.TargetHitstopMs));
            fx.Stats.SetBase(HeroId, new HitFeelOptions().PoiseStat, fx.Number(FeelFieldNames.StaggerPower)); // 韧性 = 强度 → 不破韧
            fx.Hit();
            fx.Run(hitstop + 40, holdInput: true);
            Assert.DoesNotContain(fx.Trace, t => t.Mode == MotionMode.Staggered);
            Assert.Equal(hitstop, fx.Trace.Count(t => t.Mode == MotionMode.Frozen));
        }

        [Fact]
        public void LethalHit_ArbiterShowsDead_NeverStaggeredOrFrozenAfterwards()
        {
            var fx = Build(profile: true);
            fx.Hero.Alive = false;
            fx.Bus.Enqueue(new UnitDiedEvent(HeroId, AttackerId));
            fx.Hit(kill: true);
            fx.Run(30);
            Assert.All(fx.Trace, t => Assert.Equal(MotionMode.Dead, t.Mode));
            Assert.Equal(0, fx.Sys.Clock.PauseHandleCount(HeroId));
        }

        [Fact]
        public void Knockback_HeavyImpact_MovesTheTargetByTheFormulaDistance_StartingAfterTheHitstop()
        {
            var fx = Build(profile: true);
            fx.Set(FeelFieldNames.ImpactClass, "heavy");
            fx.Set(FeelFieldNames.KnockbackDistance, 0.5);
            var options = new HitFeelOptions();
            var distance = fx.Number(FeelFieldNames.KnockbackDistance) * options.KnockbackImpactMultipliers["heavy"];
            var hitstop = Ticks(fx.Number(FeelFieldNames.TargetHitstopMs));
            var hitX = fx.Units.GetPosition(HeroId).X;

            fx.Hit();
            fx.Tick(); // 命中 tick
            var hitIndex = fx.Trace.Count - 1;
            fx.Run(hitstop + 120);

            var after = fx.Trace.Skip(hitIndex + 1).ToList();
            // 顿帧期间原地不动；之后沿"攻击方 → 目标"方向（+x）走完公式距离，期间为受控位移的 Forced 模式。
            Assert.All(after.Take(hitstop), t => Assert.Equal(hitX, t.Pos.X));
            Assert.Contains(after, t => t.Mode == MotionMode.Forced);
            var final = after.Last().Pos;
            Assert.Equal(hitX + distance, final.X, 6);
            Assert.Equal(0.0, final.Y, 9);
        }

        [Fact]
        public void DefaultProfile_WiringChangesNothing_PositionFacingAndModeAreBitIdentical()
        {
            List<long[]> Scenario(bool isWired)
            {
                var fx = Build(profile: false, wired: isWired);
                var rows = new List<long[]>();
                for (var tick = 0; tick < 60; tick++)
                {
                    if (isWired && tick % 7 == 3) fx.Hit();
                    fx.Tick(holdInput: tick < 40);
                    var p = fx.Units.GetPosition(HeroId);
                    rows.Add(new[]
                    {
                        BitConverter.DoubleToInt64Bits(p.X), BitConverter.DoubleToInt64Bits(p.Y),
                        BitConverter.DoubleToInt64Bits(fx.Hero.Facing), (long)fx.Hero.MovementState.Motion.Mode,
                    });
                }

                return rows;
            }

            var plain = Scenario(isWired: false);
            var wired = Scenario(isWired: true);
            Assert.Equal(plain.Count, wired.Count);
            for (var i = 0; i < plain.Count; i++) Assert.Equal(plain[i], wired[i]);
        }

        [Fact]
        public void Wiring_SetsActionClockAndStaggerQuery_AndMakesMovementTheKnockbackSink()
        {
            var fx = Build(profile: false);
            Assert.Same(fx.Sys.Clock, fx.Movement.Motion!.ActionClock);
            Assert.IsType<HitReactionStaggerQuery>(fx.Movement.Motion.Stagger);
            Assert.Same(fx.Movement, fx.Sys.Host.Knockback);
        }
    }
}
