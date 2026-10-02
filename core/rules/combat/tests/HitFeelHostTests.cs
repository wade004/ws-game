// HitFeelHostTests：手感设计/03（ADR-0117）局部顿帧与受击裁决的运行时冒烟。
// 期望值全部由档案毫秒与标定 tick 率（FeelCalibration.MillisecondsToTicks）算出，不写死裸数；
// 每组是"复现 + 不变量"：复现给出具体 tick 数，不变量（嵌套取大不累加、霸体恒不硬直、死亡恒无硬直、句柄归零）覆盖同一类的其它取值。
// 空间命中（combat.hit_confirmed 的发出方）属于后续切片：这里用测试里的替身发射器——用 HitFeelHost.Evaluate 的结论填事件，
// 与后续切片的用法一致；instant 适配（接 combat.damage_dealt）用真实 CombatHost。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Numbers.Faction;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Assembly;
using Core.Rules.Combat;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    public class HitFeelHostTests
    {
        private static readonly Id Attacker = new Id("unit.hf_attacker");
        private static readonly Id Target = new Id("unit.hf_target");
        private static readonly Id Target2 = new Id("unit.hf_target2");
        private static readonly Id Target3 = new Id("unit.hf_target3");
        private static readonly Id Bystander = new Id("unit.hf_bystander");
        private static readonly Id Skill = new Id("skill.hf_swing");

        /// <summary>模拟步长（秒）：与手感解析器的 StepSeconds 取同一值，毫秒折 tick 的依据。</summary>
        private const double Dt = 1.0 / 60.0;

        private static int Ticks(double ms) => FeelCalibration.MillisecondsToTicks(ms, Dt);

        // ------------------------------------------------------------------ 夹具

        private sealed class FakeActions : IActionStateQuery
        {
            public bool SuperArmor;

            public ActionState? Current(Id unitId) => null;

            public bool IsCancelOpen(Id unitId, ActionClass actionClass) => false;

            public bool IsInvulnerable(Id unitId) => false;

            public bool IsActionClockPaused(Id unitId) => false;

            public bool IsSuperArmor(Id unitId) => SuperArmor;
        }

        private sealed class RecordingKnockback : IKnockbackSink
        {
            public readonly List<(long Tick, Id Unit, Vec2 Direction, double Distance, double Duration)> Calls =
                new List<(long, Id, Vec2, double, double)>();

            public Func<long> Now = () => 0;

            public void BeginKnockback(Id unitId, Vec2 direction, double distanceWorld, double durationSeconds) =>
                Calls.Add((Now(), unitId, direction, distanceWorld, durationSeconds));
        }

        private sealed class RecordingLaunch : ILaunchSink
        {
            public readonly List<(long Tick, Id Unit, double Apex)> Calls = new List<(long, Id, double)>();

            /// <summary>与 <see cref="Calls"/> 一一对应的叠加方式与上限（二参数重载记为 Restart/0）。</summary>
            public readonly List<(LaunchStackMode Stack, double Cap)> Stacks = new List<(LaunchStackMode, double)>();

            public Func<long> Now = () => 0;

            public void BeginLaunch(Id unitId, double apexHeightWorld)
            {
                Calls.Add((Now(), unitId, apexHeightWorld));
                Stacks.Add((LaunchStackMode.Restart, 0.0));
                HeightCaps.Add(0.0);
            }

            public void BeginLaunch(Id unitId, double apexHeightWorld, LaunchStackMode stack, double stackCapApexWorld)
            {
                Calls.Add((Now(), unitId, apexHeightWorld));
                Stacks.Add((stack, stackCapApexWorld));
                HeightCaps.Add(0.0);
            }

            /// <summary>与 <see cref="Calls"/> 一一对应的绝对高度上限（没带上限的重载记为 0）。</summary>
            public readonly List<double> HeightCaps = new List<double>();

            public void BeginLaunch(Id unitId, double apexHeightWorld, LaunchStackMode stack, double stackCapApexWorld, double heightCapWorld)
            {
                Calls.Add((Now(), unitId, apexHeightWorld));
                Stacks.Add((stack, stackCapApexWorld));
                HeightCaps.Add(heightCapWorld);
            }
        }

        private sealed class FakeAirborne : IAirborneQuery
        {
            public readonly HashSet<Id> Air = new HashSet<Id>();

            public bool IsAirborne(Id unitId) => Air.Contains(unitId);
        }

        private sealed class RecordingInterrupt : IStaggerInterruptSink
        {
            public readonly List<(Id Unit, Id Source)> Calls = new List<(Id, Id)>();

            public void InterruptByStagger(Id unitId, Id sourceId) => Calls.Add((unitId, sourceId));
        }

        private sealed class Fx
        {
            public EventBus Bus = null!;
            public WorldSim World = null!;
            public FeelSystem Feel = null!;
            public HitFeelSystem Sys = null!;
            public CombatTestSupport.Fixture C = null!;
            public CombatHost Combat = null!;
            public FakeActions Actions = new FakeActions();
            public RecordingKnockback Knock = new RecordingKnockback();
            public RecordingLaunch Launch = new RecordingLaunch();
            public FakeAirborne Airborne = new FakeAirborne();
            public RecordingInterrupt Interrupts = new RecordingInterrupt();
            public HitFeelOptions Options = new HitFeelOptions();
            public long TickNo;

            public readonly List<(long Tick, FeelHitstopStartedEvent E)> Started = new List<(long, FeelHitstopStartedEvent)>();
            public readonly List<(long Tick, FeelHitstopEndedEvent E)> Ended = new List<(long, FeelHitstopEndedEvent)>();
            public readonly List<(long Tick, CombatReactionAppliedEvent E)> Reactions = new List<(long, CombatReactionAppliedEvent)>();
            public readonly List<string> Trace = new List<string>();

            /// <summary>每个 tick 结束后各单位的"硬直中"采样：Staggered[unit][i] 是第 i 个 tick 结束时的值。</summary>
            public readonly Dictionary<Id, List<bool>> Staggered = new Dictionary<Id, List<bool>>();

            public IActorActionClockQuery Clock => Sys.Clock;

            public HitFeelHost Host => Sys.Host;

            public void Set(string field, double value) =>
                Feel.DebugOverrides!.SetGlobal(new FeelWrite(field, FeelOp.Set, FeelValue.Of(value)));

            public void Set(string field, string value) =>
                Feel.DebugOverrides!.SetGlobal(new FeelWrite(field, FeelOp.Set, FeelValue.Of(value)));

            public void Tick()
            {
                World.Tick(SimStep.Continuous(Dt));
                foreach (var unit in new[] { Attacker, Target, Target2, Target3, Bystander })
                {
                    if (!Staggered.TryGetValue(unit, out var list)) Staggered[unit] = list = new List<bool>();
                    list.Add(Host.IsStaggered(unit));
                }

                TickNo++;
            }

            public void Run(int ticks)
            {
                for (var i = 0; i < ticks; i++) Tick();
            }

            /// <summary>替身发射器：用受击裁决的结论填 combat.hit_confirmed，与空间命中切片的用法一致。</summary>
            public CombatHitConfirmedEvent Confirmed(Id attacker, Id target, bool kill = false, HitResult result = HitResult.Hit, string? attackId = null)
            {
                var outcome = Host.Evaluate(new HitFeelInput(attacker, target, result, kill ? 1000.0 : 10.0, kill));
                var from = C.Units.GetPosition(attacker);
                var to = C.Units.GetPosition(target);
                var direction = to - from;
                if (direction.Length > 1e-9) direction = direction * (1.0 / direction.Length); else direction = new Vec2(1, 0);
                return new CombatHitConfirmedEvent(
                    new Id(attackId ?? "attack.hf." + TickNo + "." + target.Value), 0, attacker, target, Skill, result,
                    kill ? 1000.0 : 10.0, 0.01, false, kill, to, -direction, direction,
                    outcome.ImpactClass, outcome.AttackerHitStopTicks, outcome.TargetHitStopTicks, outcome.Reaction);
            }

            public void Hit(Id attacker, Id target, bool kill = false, HitResult result = HitResult.Hit)
            {
                if (kill) C.Units.SetAlive(target, false);
                Bus.Enqueue(Confirmed(attacker, target, kill, result));
            }

            /// <summary>跑 n 个 tick，返回该单位在其中被冻结的 tick 数（n − 动作时钟前进量）。</summary>
            public int FrozenOver(Id unit, int n)
            {
                var before = Clock.ActionTicks(unit);
                Run(n);
                return n - (int)(Clock.ActionTicks(unit) - before);
            }

            public int StaggeredCount(Id unit) => Staggered[unit].Count(x => x);

            public int FirstStaggeredTick(Id unit) => Staggered[unit].IndexOf(true);
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
                "{ \"table\": \"feel.calibration\", \"schema_version\": 1, \"rows\": [ { \"id\": \"feel.calibration.hf_test\", " +
                "\"base_preset\": \"feel.preset.rpg_classic\", \"reference_height\": 2.0, \"base_speed\": 4.0, \"animation_fps\": 30.0, " +
                "\"reference_camera_height\": 10.0, \"reference_zoom\": 1.0, \"pixels_per_unit\": 32.0, \"marker_tolerance_ms\": 50.0 } ] }");
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            FeelSchemas.RegisterAll(registry);
            Assert.Equal(0, registry.LoadAll().ErrorCount);
            var result = FeelAssembly.Assemble(registry, new FeelAssemblyOptions { StepSeconds = Dt, CalibrationId = "feel.calibration.hf_test" });
            Assert.True(result.IsAssembled);
            return result.System!;
        }

        /// <summary>
        /// 搭一套完整夹具。<paramref name="profile"/> 为 true 时写入一组"有手感"的档案（顿帧/硬直非零）；为 false 保持
        /// 框架缺省预设 rpg_classic（顿帧 0、受击上限 none）——用来验证缺省档案下裁决不改变既有结果。
        /// </summary>
        private static Fx Build(bool profile = true, Action<HitFeelOptions>? configure = null, bool useActions = false, IActionStateQuery? actionState = null)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var registry = CombatTestSupport.MakeRegistry(bus);
            var stats = new StatHost(registry, bus);
            var powers = CombatTestSupport.MakePowerHost(bus);
            var factions = new FactionMatrix(registry, bus);
            var rng = new RngHost(12345);
            var units = new FakeUnitAccess();
            var auras = new FakeAuraQuery();
            var staticImmunity = new FakeStaticImmunityProvider();
            var gearOffset = new FakeGearLevelOffsetProvider();
            var diagnostics = new InMemoryCombatDiagnostics();
            var combatOptions = new CombatOptions
            {
                HitTableConfigId = new Id("combat.hit_table.default"),
                ArmorStat = CombatTestSupport.StatArmor,
                DamageDonePctStat = CombatTestSupport.StatDamageDonePct,
                DamageTakenPctStat = CombatTestSupport.StatDamageTakenPct,
                HealingDonePctStat = CombatTestSupport.StatHealingDonePct,
                PhysicalSchool = CombatTestSupport.SchoolPhysical,
            };
            var combat = new CombatHost(stats, powers, units, auras, factions, rng, bus, registry, combatOptions, diagnostics, staticImmunity, gearOffset);
            var fixture = new CombatTestSupport.Fixture
            {
                Bus = bus, Registry = registry, Stats = stats, Powers = powers, Factions = factions, Rng = rng, Units = units,
                Auras = auras, StaticImmunity = staticImmunity, GearLevelOffset = gearOffset, Diagnostics = diagnostics, Host = combat,
                Events = new List<IEvent>(),
            };
            CombatTestSupport.RegisterUnit(fixture, Attacker, CombatTestSupport.FactionParty);
            foreach (var t in new[] { Target, Target2, Target3, Bystander })
            {
                CombatTestSupport.RegisterUnit(fixture, t, CombatTestSupport.FactionHorde);
            }

            // 单位摆位：攻击方在原点，目标沿 +x 排开（击退方向 = 单位化的 目标 − 攻击方）。
            units.SetPosition(Attacker, new Vec2(0, 0));
            units.SetPosition(Target, new Vec2(1, 0));
            units.SetPosition(Target2, new Vec2(1, 1));
            units.SetPosition(Target3, new Vec2(1, -1));
            units.SetPosition(Bystander, new Vec2(5, 5));

            var feel = AssembleFeel();
            var fx = new Fx { Bus = bus, World = world, Feel = feel, C = fixture, Combat = combat };
            var options = new HitFeelOptions { PoiseStat = CombatTestSupport.StatArmor };
            configure?.Invoke(options);
            fx.Options = options;
            fx.Sys = HitFeelAssembly.Attach(
                bus, units, feel.Resolver, stats, Dt, options, powers, actionState ?? (useActions ? fx.Actions : null), auras);
            fx.Sys.Host.AddInterruptSink(fx.Interrupts);
            fx.Knock.Now = () => fx.TickNo;
            fx.Sys.Host.Knockback = fx.Knock;
            fx.Launch.Now = () => fx.TickNo;
            fx.Sys.Host.Launch = fx.Launch;
            fx.Sys.Host.Airborne = fx.Airborne;

            bus.Subscribe<FeelHitstopStartedEvent>(RulesEventKeys.FeelHitstopStarted, e => { fx.Started.Add((fx.TickNo, e)); fx.Trace.Add($"{fx.TickNo}:started:{string.Join("+", e.UnitIds)}:{e.Ticks}"); });
            bus.Subscribe<FeelHitstopEndedEvent>(RulesEventKeys.FeelHitstopEnded, e => { fx.Ended.Add((fx.TickNo, e)); fx.Trace.Add($"{fx.TickNo}:ended:{string.Join("+", e.UnitIds)}"); });
            bus.Subscribe<CombatReactionAppliedEvent>(RulesEventKeys.CombatReactionApplied, e => { fx.Reactions.Add((fx.TickNo, e)); fx.Trace.Add($"{fx.TickNo}:reaction:{e.TargetId}:{e.Reaction}:{e.DurationTicks}"); });

            if (profile)
            {
                // 一组便于手算的档案：每个量都不同，换算后 tick 数互不相同（避免"碰巧相等"掩盖口径错误）。
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

        private static double Ms(Fx fx, Id unit, string field) => fx.Feel.Resolver.ResolveJudging(unit).GetNumber(field);

        // ------------------------------------------------------------------ 局部顿帧

        [Fact]
        public void Hit_PausesAttackerAndTarget_ForTheirOwnTickCounts_AndNobodyElse()
        {
            var fx = Build();
            var attackerTicks = Ticks(Ms(fx, Attacker, FeelFieldNames.AttackerHitstopMs));
            var targetTicks = Ticks(Ms(fx, Target, FeelFieldNames.TargetHitstopMs));
            Assert.NotEqual(attackerTicks, targetTicks);

            fx.Hit(Attacker, Target);
            fx.Tick(); // 命中 tick：派发与落地发生在本 tick 内，冻结从下一个 tick 起算。
            var hitTick = fx.TickNo - 1;
            var simBefore = fx.World.TickIndex;

            const int observe = 30;
            var frozenA = fx.FrozenOver(Attacker, observe);
            // 同一段观察里目标的冻结量要再测一遍起点：FrozenOver 会推进 tick，故各单位用同一份快照重算。
            Assert.Equal(attackerTicks, frozenA);
            Assert.Equal(observe, fx.World.TickIndex - simBefore); // 模拟时钟一刻没停
            Assert.Equal(0, fx.Clock.TotalPauseHandleCount);

            // 事件：攻击方与受击方各自一条 started（时长不同分两组），ended 分别落在 命中 tick + 各自时长。
            var startedUnits = fx.Started.ToDictionary(s => s.E.UnitIds.Single(), s => s.E.Ticks);
            Assert.Equal(attackerTicks, startedUnits[Attacker]);
            Assert.Equal(targetTicks, startedUnits[Target]);
            Assert.All(fx.Started, s => Assert.Equal(hitTick, s.Tick));
            Assert.Equal(hitTick + attackerTicks, fx.Ended.Single(e => e.E.UnitIds.Contains(Attacker)).Tick);
            Assert.Equal(hitTick + targetTicks, fx.Ended.Single(e => e.E.UnitIds.Contains(Target)).Tick);
            Assert.DoesNotContain(fx.Started, s => s.E.UnitIds.Contains(Bystander));
        }

        [Fact]
        public void Hit_TargetFrozenTicks_EqualProfileMillisecondsConvertedAtTheCalibratedRate()
        {
            var fx = Build();
            var targetTicks = Ticks(Ms(fx, Target, FeelFieldNames.TargetHitstopMs));
            var bystanderTicks = 0;
            fx.Hit(Attacker, Target);
            fx.Tick();
            var before = fx.Clock.ActionTicks(Target);
            var beforeBystander = fx.Clock.ActionTicks(Bystander);
            const int observe = 30;
            fx.Run(observe);
            Assert.Equal(targetTicks, observe - (int)(fx.Clock.ActionTicks(Target) - before));
            Assert.Equal(bystanderTicks, observe - (int)(fx.Clock.ActionTicks(Bystander) - beforeBystander));
        }

        [Theory]
        [InlineData(50, 80, 140, 100)] // 最大值 140ms 超过上限 100ms：取上限
        [InlineData(50, 80, 140, 300)] // 上限宽松：取最大值
        [InlineData(140, 80, 50, 100)] // 顺序无关
        public void SameTickThreeTargets_AttackerHitstop_IsMinOfMaxAndCap(double ms1, double ms2, double ms3, double capMs)
        {
            var fx = Build();
            fx.Set(FeelFieldNames.AttackerHitstopCapMs, capMs);
            var raw = new[] { ms1, ms2, ms3 }.Select(Ticks).ToArray();
            var expected = Math.Min(raw.Max(), Ticks(capMs));

            // 发射器替身直接给"未限幅"的攻击方 tick 数（Evaluate 自己会限幅；这里验证宿主落地时仍限幅）。
            var targets = new[] { Target, Target2, Target3 };
            for (var i = 0; i < 3; i++)
            {
                fx.Bus.Enqueue(new CombatHitConfirmedEvent(
                    new Id("attack.hf.group"), 0, Attacker, targets[i], Skill, HitResult.Hit, 10, 0.01, false, false,
                    Vec2.Zero, new Vec2(-1, 0), new Vec2(1, 0), "medium", raw[i], 0, HitReaction.None));
            }

            fx.Tick();
            Assert.Equal(expected, fx.FrozenOver(Attacker, 30));
            Assert.Equal(0, fx.Clock.TotalPauseHandleCount);
            // 同一批的攻击方只发一条 started（多目标不重复宣告）。
            Assert.Single(fx.Started, s => s.E.UnitIds.Contains(Attacker));
            Assert.Equal(expected, fx.Started.Single(s => s.E.UnitIds.Contains(Attacker)).E.Ticks);
        }

        [Fact]
        public void SameTickThreeTargets_ViaEvaluate_EachTargetIsFrozenItsOwnTargetTicks()
        {
            var fx = Build();
            var targetTicks = Ticks(Ms(fx, Target, FeelFieldNames.TargetHitstopMs));
            var attackerExpected = Math.Min(
                Ticks(Ms(fx, Attacker, FeelFieldNames.AttackerHitstopMs)),
                Ticks(Ms(fx, Attacker, FeelFieldNames.AttackerHitstopCapMs)));
            fx.Hit(Attacker, Target);
            fx.Hit(Attacker, Target2);
            fx.Hit(Attacker, Target3);
            fx.Tick();
            var b2 = fx.Clock.ActionTicks(Target2);
            var b3 = fx.Clock.ActionTicks(Target3);
            Assert.Equal(targetTicks, fx.FrozenOver(Target, 30));
            Assert.Equal(targetTicks, 30 - (int)(fx.Clock.ActionTicks(Target2) - b2));
            Assert.Equal(targetTicks, 30 - (int)(fx.Clock.ActionTicks(Target3) - b3));
            // 攻击方：三次命中同值，取大即该值，不累加成三倍。
            Assert.Equal(attackerExpected, Ticks(Ms(fx, Attacker, FeelFieldNames.AttackerHitstopMs)));
            Assert.Equal(0, fx.Clock.TotalPauseHandleCount);
        }

        [Theory]
        [InlineData(200, 60)]  // 第二次更短：剩余更大，保持
        [InlineData(200, 400)] // 第二次更长：取新值
        public void SecondHitDuringHitstop_RemainingTakesTheLarger_NeverTheSum(double firstMs, double secondMs)
        {
            var fx = Build();
            fx.Set(FeelFieldNames.TargetHitstopMs, firstMs);
            fx.Set(FeelFieldNames.HitstopCapMs, 1000);
            var t1 = Ticks(firstMs);
            fx.Hit(Attacker, Target);
            fx.Tick(); // tick 0：第一次命中
            const int k = 2;
            fx.Run(k - 1);
            fx.Set(FeelFieldNames.TargetHitstopMs, secondMs);
            var t2 = Ticks(secondMs);
            Assert.True(fx.Clock.IsPaused(Target));
            fx.Hit(Attacker, Target);
            fx.Tick(); // tick k：冻结期间再次命中

            var remainingAfterSecond = fx.Clock.RemainingPausedTicks(Target);
            var remainingBefore = t1 - k; // 第一次的窗口在 tick k 结束时还剩 t1 - k 个 tick
            Assert.Equal(Math.Max(remainingBefore, t2), remainingAfterSecond);
            Assert.NotEqual(t1 + t2 - k, remainingAfterSecond);

            fx.Run(60);
            // 总冻结 tick 数 = max(t1, k + t2)（第一次命中的 tick 序号为 0）。
            Assert.Equal(Math.Max(t1, k + t2), fx.Ended.Last(e => e.E.UnitIds.Contains(Target)).Tick);
            Assert.Equal(1, fx.Ended.Count(e => e.E.UnitIds.Contains(Target)));
            Assert.Equal(0, fx.Clock.TotalPauseHandleCount);
            // started：第二次只在冻结真的变长时才再宣告一次，且宣告值是更大的剩余时长。
            var startedForTarget = fx.Started.Where(s => s.E.UnitIds.Contains(Target)).ToList();
            if (t2 > remainingBefore)
            {
                Assert.Equal(2, startedForTarget.Count);
                Assert.Equal(t2, startedForTarget[1].E.Ticks);
            }
            else
            {
                Assert.Single(startedForTarget);
            }

        }

        // ------------------------------------------------------------------ 受击裁决

        [Fact]
        public void PoiseNotBroken_NoStun_OnlyAFlinch()
        {
            var fx = Build();
            var power = Ms(fx, Attacker, FeelFieldNames.StaggerPower);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, power); // 韧性 = 强度：不高于韧性 → 不打断
            fx.Hit(Attacker, Target);
            fx.Run(60);
            Assert.Equal(0, fx.StaggeredCount(Target));
            Assert.Equal(0, fx.Host.RemainingStaggerTicks(Target));
            var reaction = fx.Reactions.Single().E;
            Assert.Equal(HitReaction.Flinch, reaction.Reaction);
            Assert.Equal(0, reaction.DurationTicks);
            Assert.Empty(fx.Interrupts.Calls);
            Assert.Empty(fx.Knock.Calls);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void PoiseBroken_StunTicksMatchProfile_AndCountFromTheEndOfHitstop(bool withTargetHitstop)
        {
            var fx = Build();
            if (!withTargetHitstop) fx.Set(FeelFieldNames.TargetHitstopMs, 0);
            var power = Ms(fx, Attacker, FeelFieldNames.StaggerPower);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, power - 1); // 强度高于韧性 → 破韧
            var stunTicks = Ticks(Ms(fx, Target, FeelFieldNames.HitStunMs));
            var hitstopTicks = withTargetHitstop ? Ticks(Ms(fx, Target, FeelFieldNames.TargetHitstopMs)) : 0;

            fx.Hit(Attacker, Target);
            fx.Run(1 + hitstopTicks + stunTicks + 20);

            Assert.Equal(stunTicks, fx.StaggeredCount(Target));
            // 顿帧期间不处于硬直；硬直恰好在顿帧结束后的下一个 tick 起算（命中 tick = 0）。
            Assert.Equal(hitstopTicks + 1, fx.FirstStaggeredTick(Target));
            // 连续一段，不间断。
            var trace = fx.Staggered[Target];
            var first = trace.IndexOf(true);
            Assert.All(trace.Skip(first).Take(stunTicks), x => Assert.True(x));
            Assert.False(trace[first + stunTicks]);

            var reaction = fx.Reactions.Single().E;
            Assert.Equal(HitReaction.Stagger, reaction.Reaction);
            Assert.Equal(stunTicks, reaction.DurationTicks);
            Assert.Equal(Target, fx.Interrupts.Calls.Single().Unit);
            Assert.Equal(Attacker, fx.Interrupts.Calls.Single().Source);
            Assert.Equal(0, fx.Host.RemainingStaggerTicks(Target));
        }

        [Fact]
        public void Stagger_DoesNotAdvanceWhileTargetIsHitstopFrozenAgain()
        {
            var fx = Build();
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            var stunTicks = Ticks(Ms(fx, Target, FeelFieldNames.HitStunMs));
            var hitstopTicks = Ticks(Ms(fx, Target, FeelFieldNames.TargetHitstopMs));
            fx.Hit(Attacker, Target);
            fx.Run(1 + hitstopTicks + 2); // 已进入硬直两个 tick
            Assert.True(fx.Host.IsStaggered(Target));
            fx.Hit(Attacker, Target2); // 别的目标命中不影响
            fx.Run(1);
            Assert.True(fx.Host.IsStaggered(Target));
            Assert.Equal(stunTicks, fx.Host.RemainingStaggerTicks(Target) + fx.StaggeredCount(Target));
        }

        [Fact]
        public void SuperArmor_NeverEntersStagger_ButStillTakesTheTargetHitstop()
        {
            var fx = Build(useActions: true);
            fx.Actions.SuperArmor = true;
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0); // 韧性远低于强度：若不是霸体必破韧
            var targetTicks = Ticks(Ms(fx, Target, FeelFieldNames.TargetHitstopMs));
            fx.Hit(Attacker, Target);
            fx.Tick();
            Assert.Equal(targetTicks, fx.FrozenOver(Target, 40));
            Assert.Equal(0, fx.StaggeredCount(Target));
            Assert.Empty(fx.Reactions);
            Assert.Empty(fx.Interrupts.Calls);
            Assert.Empty(fx.Knock.Calls);

            // 霸体窗口结束后同样的命中恢复正常裁决。
            fx.Actions.SuperArmor = false;
            fx.Hit(Attacker, Target);
            fx.Run(1 + targetTicks + 3);
            Assert.True(fx.StaggeredCount(Target) > 0);
        }

        [Fact]
        public void SuperArmor_FromRealTimelineWindow_InsideNoStaggerButTargetHitstop_OutsideStaggers()
        {
            // 联动不变量：真实 CastPipeline（armor_start/armor_end 时间线标记）作为 IActionStateQuery 接给受击裁决。
            var actor = Tests.Rules.Skill.TimelineHarness.Actor;
            var h = Tests.Rules.Skill.TimelineHarness.Create(
                new[]
                {
                    Tests.Rules.Skill.TimelineHarness.TlSkill(
                        "skill.sample_armor_swing", 50, 150, 100,
                        markers: new[]
                        {
                            Tests.Rules.Skill.TimelineHarness.Marker("armor_start", 50), Tests.Rules.Skill.TimelineHarness.Marker("armor_end", 150),
                        }),
                },
                b => b.Options.GcdEnabled = false);
            var fx = Build(actionState: h.Query);
            CombatTestSupport.RegisterUnit(fx.C, actor, CombatTestSupport.FactionHorde);
            fx.C.Units.SetPosition(actor, new Vec2(1, 0));
            fx.C.Stats.SetBase(actor, CombatTestSupport.StatArmor, 0); // 韧性远低于强度：不是霸体必破韧

            var targetTicks = Ticks(Ms(fx, actor, FeelFieldNames.TargetHitstopMs));
            var stunTicks = Ticks(Ms(fx, actor, FeelFieldNames.HitStunMs));
            var windowStart = Ticks(50);
            var windowEnd = Ticks(150);

            h.CastInTick("skill.sample_armor_swing");
            while (h.ElapsedTicks < windowStart) h.Tick();
            Assert.True(h.Query.IsSuperArmor(actor));

            // 窗口内：不进硬直、无受击反应、不打断动作，但仍有受击方顿帧。
            fx.Hit(Attacker, actor);
            fx.Tick();
            var before = fx.Clock.ActionTicks(actor);
            var staggeredInside = 0;
            for (var i = 0; i < 40; i++)
            {
                fx.Tick();
                if (fx.Host.IsStaggered(actor)) staggeredInside++;
            }

            Assert.Equal(targetTicks, 40 - (int)(fx.Clock.ActionTicks(actor) - before));
            Assert.Equal(0, staggeredInside);
            Assert.Empty(fx.Reactions);
            Assert.Empty(fx.Interrupts.Calls);

            // 窗口外：同样的命中进入硬直，时长 = 档案毫秒换算。
            while (h.ElapsedTicks < windowEnd) h.Tick();
            Assert.False(h.Query.IsSuperArmor(actor));
            fx.Hit(Attacker, actor);
            fx.Tick();
            var staggeredOutside = 0;
            for (var i = 0; i < targetTicks + stunTicks + 10; i++)
            {
                fx.Tick();
                if (fx.Host.IsStaggered(actor)) staggeredOutside++;
            }

            Assert.Equal(stunTicks, staggeredOutside);
            Assert.Equal(HitReaction.Stagger, fx.Reactions.Single().E.Reaction);
            Assert.Equal(actor, fx.Interrupts.Calls.Single().Unit);
        }

        [Fact]
        public void SuperArmor_PolicyCanDropTheTargetHitstop()
        {
            var fx = Build(configure: o => o.SuperArmorTargetHitstop = false, useActions: true);
            fx.Actions.SuperArmor = true;
            fx.Hit(Attacker, Target);
            fx.Tick();
            Assert.Equal(0, fx.FrozenOver(Target, 20));
        }

        [Fact]
        public void LethalHit_IsDeath_NoStagger_NoTargetFreeze_AndAttackerGetsTheKillHitstop()
        {
            var fx = Build();
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            var scaled = Ms(fx, Attacker, FeelFieldNames.AttackerHitstopMs) * Ms(fx, Attacker, FeelFieldNames.KillHitstopScale);
            var expectedAttacker = Math.Min(Ticks(scaled), Ticks(Ms(fx, Attacker, FeelFieldNames.AttackerHitstopCapMs)));
            Assert.True(expectedAttacker > Ticks(Ms(fx, Attacker, FeelFieldNames.AttackerHitstopMs)));

            fx.Hit(Attacker, Target, kill: true);
            fx.Tick();
            var outcomeReaction = fx.Reactions.Single().E;
            Assert.Equal(HitReaction.Death, outcomeReaction.Reaction);
            Assert.Equal(0, outcomeReaction.DurationTicks);
            Assert.Equal(expectedAttacker, fx.FrozenOver(Attacker, 40));
            Assert.Equal(0, fx.StaggeredCount(Target));
            Assert.Equal(0, fx.Clock.PauseHandleCount(Target));
            Assert.Equal(0, fx.Clock.TotalPauseHandleCount);
            Assert.Empty(fx.Interrupts.Calls);
            Assert.Empty(fx.Knock.Calls);
            Assert.DoesNotContain(fx.Started, s => s.E.UnitIds.Contains(Target));
        }

        [Fact]
        public void DeathWhileFrozenAndStaggered_ReleasesEverythingImmediately()
        {
            var fx = Build();
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            fx.Hit(Attacker, Target);
            fx.Run(1 + Ticks(Ms(fx, Target, FeelFieldNames.TargetHitstopMs)) + 2);
            Assert.True(fx.Host.IsStaggered(Target));

            fx.C.Units.SetAlive(Target, false);
            fx.Bus.Enqueue(new UnitDiedEvent(Target, Attacker));
            fx.Tick();
            Assert.False(fx.Host.IsStaggered(Target));
            Assert.Equal(0, fx.Host.RemainingStaggerTicks(Target));
            Assert.Equal(0, fx.Clock.PauseHandleCount(Target));
        }

        [Fact]
        public void DeathDuringHitstop_EmitsEnded_AndLeavesZeroHandles()
        {
            var fx = Build();
            fx.Hit(Attacker, Target);
            fx.Tick();
            Assert.True(fx.Clock.IsPaused(Target));
            fx.C.Units.SetAlive(Target, false);
            fx.Bus.Enqueue(new UnitDiedEvent(Target, Attacker));
            fx.Tick();
            Assert.False(fx.Clock.IsPaused(Target));
            Assert.Contains(fx.Ended, e => e.E.UnitIds.Contains(Target));
        }

        [Theory]
        [InlineData("light", "knockdown", HitReaction.StaggerLight)]
        [InlineData("medium", "knockdown", HitReaction.Stagger)]
        [InlineData("heavy", "knockdown", HitReaction.Knockback)]
        [InlineData("massive", "knockdown", HitReaction.Knockdown)]
        [InlineData("massive", "knockback", HitReaction.Knockback)]
        [InlineData("heavy", "stagger_light", HitReaction.StaggerLight)]
        [InlineData("medium", "flinch", HitReaction.Flinch)]
        [InlineData("massive", "none", HitReaction.None)]
        [InlineData("light", "stagger", HitReaction.StaggerLight)] // 上限高于映射：不抬高
        public void ReactionCap_ClampsTheImpactClassReaction(string impact, string cap, HitReaction expected)
        {
            var fx = Build();
            fx.Set(FeelFieldNames.ImpactClass, impact);
            fx.Set(FeelFieldNames.ReactionCap, cap);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            var outcome = fx.Host.Evaluate(new HitFeelInput(Attacker, Target, HitResult.Hit, 10, false));
            Assert.Equal(expected, outcome.Reaction);
            Assert.Equal(impact, outcome.ImpactClass);
        }

        [Fact]
        public void ReactionDuration_KnockdownAddsDownedTime_AndIsDownedTracksTheSecondSegment()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.ImpactClass, "massive");
            fx.Set(FeelFieldNames.TargetHitstopMs, 0);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            var stun = Ticks(Ms(fx, Target, FeelFieldNames.HitStunMs));
            var downed = Ticks(Ms(fx, Target, FeelFieldNames.DownedMs));
            fx.Hit(Attacker, Target);
            fx.Run(1 + stun + downed + 3);
            Assert.Equal(stun + downed, fx.StaggeredCount(Target));
            Assert.Equal(stun + downed, fx.Reactions.Single().E.DurationTicks);
            Assert.Equal(HitReaction.Knockdown, fx.Reactions.Single().E.Reaction);
        }

        [Theory]
        [InlineData(HitResult.Miss)]
        [InlineData(HitResult.Dodge)]
        [InlineData(HitResult.Parry)]
        [InlineData(HitResult.Immune)]
        [InlineData(HitResult.Invulnerable)]
        public void AvoidedResults_NoHitstop_NoReaction(HitResult result)
        {
            var fx = Build();
            var outcome = fx.Host.Evaluate(new HitFeelInput(Attacker, Target, result, 0, false));
            Assert.Equal(0, outcome.AttackerHitStopTicks);
            Assert.Equal(0, outcome.TargetHitStopTicks);
            Assert.Equal(HitReaction.None, outcome.Reaction);

            fx.Hit(Attacker, Target, result: result);
            fx.Run(5);
            Assert.Equal(0, fx.Clock.TotalPauseHandleCount);
            Assert.Empty(fx.Started);
            Assert.Empty(fx.Reactions);
        }

        // ------------------------------------------------------------------ 击退

        [Fact]
        public void Knockback_DistanceFollowsResistanceAndImpactMultiplier_AndIsSubmittedOnceBeforeTheStaggerStarts()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.ImpactClass, "heavy");
            fx.Set(FeelFieldNames.KnockbackDistance, 0.5);
            fx.Set(FeelFieldNames.KnockbackResistanceStat, CombatTestSupport.StatBlockValue.Value);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatBlockValue, 0.25);
            var baseDistance = Ms(fx, Attacker, FeelFieldNames.KnockbackDistance);
            var expectedDistance = baseDistance * (1.0 - 0.25) * fx.Options.KnockbackImpactMultipliers["heavy"];
            var hitstop = Ticks(Ms(fx, Target, FeelFieldNames.TargetHitstopMs));

            fx.Hit(Attacker, Target);
            fx.Run(1 + hitstop + 3);

            var call = fx.Knock.Calls.Single();
            Assert.Equal(Target, call.Unit);
            Assert.Equal(expectedDistance, call.Distance, 9);
            Assert.Equal(1.0, call.Direction.X, 9);
            Assert.Equal(0.0, call.Direction.Y, 9);
            // 目标顿帧的最后一个 tick 起提交（意图下一 tick 生效 = 硬直第一个 tick）。
            Assert.Equal(hitstop, call.Tick);
            Assert.Equal(HitReaction.Knockback, fx.Reactions.Single().E.Reaction);
        }

        [Fact]
        public void Knockback_WithoutHitstop_IsSubmittedInTheHitTick()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.ImpactClass, "heavy");
            fx.Set(FeelFieldNames.TargetHitstopMs, 0);
            fx.Set(FeelFieldNames.KnockbackDistance, 0.5);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            fx.Hit(Attacker, Target);
            fx.Run(3);
            Assert.Equal(0, fx.Knock.Calls.Single().Tick);
        }

        [Fact]
        public void Knockback_NotSubmittedWhenTargetDiesBeforeItStarts()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.ImpactClass, "heavy");
            fx.Set(FeelFieldNames.KnockbackDistance, 0.5);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            fx.Hit(Attacker, Target);
            fx.Tick();
            fx.C.Units.SetAlive(Target, false);
            fx.Bus.Enqueue(new UnitDiedEvent(Target, Attacker));
            fx.Run(20);
            Assert.Empty(fx.Knock.Calls);
        }

        // ------------------------------------------------------------------ 击飞（竖直轴能力包，手感设计/06 第 10 节勘误 9）

        [Fact]
        public void Launch_ApexFollowsLaunchHeightResistanceAndImpactMultiplier_AndIsSubmittedWithTheKnockback()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.ImpactClass, "heavy");
            fx.Set(FeelFieldNames.KnockbackDistance, 0.5);
            fx.Set(FeelFieldNames.LaunchHeight, 1.2);
            fx.Set(FeelFieldNames.KnockbackResistanceStat, CombatTestSupport.StatBlockValue.Value);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatBlockValue, 0.25);
            Assert.True(fx.Feel.Resolver.ResolveJudging(Attacker).TryGetNumber(FeelFieldNames.LaunchHeight, out var baseHeight));
            var expectedApex = baseHeight * (1.0 - 0.25) * fx.Options.KnockbackImpactMultipliers["heavy"];
            var hitstop = Ticks(Ms(fx, Target, FeelFieldNames.TargetHitstopMs));

            fx.Hit(Attacker, Target);
            fx.Run(1 + hitstop + 3);

            var call = fx.Launch.Calls.Single();
            Assert.Equal(Target, call.Unit);
            Assert.Equal(expectedApex, call.Apex, 9);
            // 与击退同一提交时机（目标顿帧结束那个 tick）。
            Assert.Equal(hitstop, call.Tick);
            Assert.Equal(fx.Knock.Calls.Single().Tick, call.Tick);
        }

        [Fact]
        public void Launch_DefaultsToNoLaunch_AndLowReactionsNeverLaunch_KnockbackUnchanged()
        {
            // 不变量：缺省（档案没有 launch_height）不击飞，击退行为与引入击飞前一致；档案声明了击飞但反应没到击退（medium → stagger）也不击飞。
            var heavy = Build();
            heavy.Set(FeelFieldNames.ImpactClass, "heavy");
            heavy.Set(FeelFieldNames.KnockbackDistance, 0.5);
            heavy.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            heavy.Hit(Attacker, Target);
            heavy.Run(20);
            Assert.Empty(heavy.Launch.Calls);
            Assert.Single(heavy.Knock.Calls);

            var medium = Build();
            medium.Set(FeelFieldNames.ImpactClass, "medium");
            medium.Set(FeelFieldNames.LaunchHeight, 1.2);
            medium.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            medium.Hit(Attacker, Target);
            medium.Run(20);
            Assert.Empty(medium.Launch.Calls);
        }

        [Fact]
        public void Launch_IsIndependentOfKnockbackDistance()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.ImpactClass, "heavy");
            fx.Set(FeelFieldNames.KnockbackDistance, 0);
            fx.Set(FeelFieldNames.LaunchHeight, 1.0);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            fx.Hit(Attacker, Target);
            fx.Run(20);
            Assert.Empty(fx.Knock.Calls);
            Assert.True(fx.Feel.Resolver.ResolveJudging(Attacker).TryGetNumber(FeelFieldNames.LaunchHeight, out var baseHeight));
            Assert.Equal(baseHeight * fx.Options.KnockbackImpactMultipliers["heavy"], fx.Launch.Calls.Single().Apex, 9);
        }

        // ------------------------------------------------------------------ 腾空受击反应 air_hit_reaction（ADR-0130 追加决定）

        private static HitReaction ReactionOf(Fx fx) => fx.Reactions.Last().E.Reaction;

        [Fact]
        public void AirHitReaction_ReplacesTheReactionOnlyWhileTheTargetIsAirborne()
        {
            // 复现：同一击（heavy → knockback）——地面仍是 knockback，空中按 air_hit_reaction 变成 flinch。
            var ground = Build();
            ground.Set(FeelFieldNames.ImpactClass, "heavy");
            ground.Set(FeelFieldNames.AirHitReaction, "flinch");
            ground.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            ground.Hit(Attacker, Target);
            ground.Run(3);
            Assert.Equal(HitReaction.Knockback, ReactionOf(ground));

            var air = Build();
            air.Set(FeelFieldNames.ImpactClass, "heavy");
            air.Set(FeelFieldNames.AirHitReaction, "flinch");
            air.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            air.Airborne.Air.Add(Target);
            air.Hit(Attacker, Target);
            air.Run(3);
            Assert.Equal(HitReaction.Flinch, ReactionOf(air));
        }

        [Fact]
        public void AirHitReaction_AbsentOrSame_IsIdenticalToTheGroundReaction_EvenInTheAir()
        {
            // 不变量：缺省（档案没声明）与 same 都与地面受击一致；没有腾空查询（平面世界）时声明了也不生效。
            foreach (var declared in new string?[] { null, "same" })
            {
                var fx = Build();
                fx.Set(FeelFieldNames.ImpactClass, "heavy");
                if (declared != null) fx.Set(FeelFieldNames.AirHitReaction, declared);
                fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
                fx.Airborne.Air.Add(Target);
                fx.Hit(Attacker, Target);
                fx.Run(3);
                Assert.Equal(HitReaction.Knockback, ReactionOf(fx));
            }

            var flat = Build();
            flat.Sys.Host.Airborne = null;
            flat.Set(FeelFieldNames.ImpactClass, "heavy");
            flat.Set(FeelFieldNames.AirHitReaction, "none");
            flat.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            flat.Hit(Attacker, Target);
            flat.Run(3);
            Assert.Equal(HitReaction.Knockback, ReactionOf(flat));
        }

        [Fact]
        public void AirHitReaction_CanRaiseTheReaction_AndReactionCapStillLimitsIt()
        {
            // medium → stagger；空中声明 knockdown → 升到 knockdown；再叠 reaction_cap=stagger → 被限回 stagger。
            var raised = Build();
            raised.Set(FeelFieldNames.ImpactClass, "medium");
            raised.Set(FeelFieldNames.AirHitReaction, "knockdown");
            raised.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            raised.Airborne.Air.Add(Target);
            raised.Hit(Attacker, Target);
            raised.Run(3);
            Assert.Equal(HitReaction.Knockdown, ReactionOf(raised));

            var capped = Build();
            capped.Set(FeelFieldNames.ImpactClass, "medium");
            capped.Set(FeelFieldNames.AirHitReaction, "knockdown");
            capped.Set(FeelFieldNames.ReactionCap, "stagger");
            capped.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            capped.Airborne.Air.Add(Target);
            capped.Hit(Attacker, Target);
            capped.Run(3);
            Assert.Equal(HitReaction.Stagger, ReactionOf(capped));
        }

        [Fact]
        public void AirHitReaction_DeathIsNeverReplaced()
        {
            var kill = Build();
            kill.Set(FeelFieldNames.AirHitReaction, "none");
            kill.Airborne.Air.Add(Target);
            kill.Hit(Attacker, Target, kill: true);
            kill.Run(3);
            Assert.Equal(HitReaction.Death, ReactionOf(kill));
        }

        [Fact]
        public void AirHitReaction_AirKnockback_RelaunchesWhenTheAttackerDeclaresLaunchHeight()
        {
            // 受击方空中声明 knockback → 与地面 knockback 同路径：声明了 launch_height 的攻击方击飞该目标。
            var fx = Build();
            fx.Set(FeelFieldNames.ImpactClass, "medium");
            fx.Set(FeelFieldNames.AirHitReaction, "knockback");
            fx.Set(FeelFieldNames.LaunchHeight, 0.8);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            fx.Airborne.Air.Add(Target);
            fx.Hit(Attacker, Target);
            fx.Run(30);
            Assert.Single(fx.Launch.Calls);
        }

        // ------------------------------------------------------------------ 击飞叠加 launch_stack（ADR-0130 追加决定）

        [Fact]
        public void LaunchStack_AddAndCapAreForwardedToTheSink_CalibratedToWorldUnits()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.ImpactClass, "heavy");
            fx.Set(FeelFieldNames.LaunchHeight, 1.0);
            fx.Set(FeelFieldNames.LaunchStack, "add");
            fx.Set(FeelFieldNames.LaunchStackCap, 2.5);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            Assert.True(fx.Feel.Resolver.ResolveJudging(Attacker).TryGetNumber(FeelFieldNames.LaunchStackCap, out var capWorld));
            fx.Hit(Attacker, Target);
            fx.Run(30);

            Assert.Single(fx.Launch.Calls);
            var (stack, cap) = fx.Launch.Stacks.Single();
            Assert.Equal(LaunchStackMode.Add, stack);
            Assert.Equal(capWorld, cap, 9);
            Assert.NotEqual(2.5, cap); // 标定后的世界单位，不是档案里的体高倍数
        }

        [Fact]
        public void LaunchStack_AddWithoutCap_ForwardsZeroCap()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.ImpactClass, "heavy");
            fx.Set(FeelFieldNames.LaunchHeight, 1.0);
            fx.Set(FeelFieldNames.LaunchStack, "add");
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            fx.Hit(Attacker, Target);
            fx.Run(30);
            var (stack, cap) = fx.Launch.Stacks.Single();
            Assert.Equal(LaunchStackMode.Add, stack);
            Assert.Equal(0.0, cap);
        }

        [Fact]
        public void LaunchStack_DefaultsToRestartWithNoCap_LikeNineteen95()
        {
            foreach (var declared in new string?[] { null, "restart" })
            {
                var fx = Build();
                fx.Set(FeelFieldNames.ImpactClass, "heavy");
                fx.Set(FeelFieldNames.LaunchHeight, 1.0);
                if (declared != null) fx.Set(FeelFieldNames.LaunchStack, declared);
                fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
                fx.Hit(Attacker, Target);
                fx.Run(30);
                var (stack, cap) = fx.Launch.Stacks.Single();
                Assert.Equal(LaunchStackMode.Restart, stack);
                Assert.Equal(0.0, cap);
            }
        }

        // ------------------------------------------------------------------ 手感落地 M4-W1b：目标侧空中反应、高度上限、体型缩放、硬直持续到落地

        private static Fx AirFx(string impact = "heavy")
        {
            var fx = Build();
            fx.Set(FeelFieldNames.ImpactClass, impact);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            fx.Airborne.Air.Add(Target);
            return fx;
        }

        private static void SetUnitField(Fx fx, Id unit, string field, string value) =>
            fx.Feel.DebugOverrides!.SetUnit(unit, new FeelWrite(field, FeelOp.Set, FeelValue.Of(value)));

        private static void SetUnitField(Fx fx, Id unit, string field, double value) =>
            fx.Feel.DebugOverrides!.SetUnit(unit, new FeelWrite(field, FeelOp.Set, FeelValue.Of(value)));

        [Fact]
        public void AirHitReaction_TargetSideDeclaration_AppliesWhenTheAttackerDeclaresNothing()
        {
            // 复现：只有受击方档案声明 air_hit_reaction=flinch（攻击方没有）——空中受击也被替换；地面不受影响。
            var air = AirFx();
            SetUnitField(air, Target, FeelFieldNames.AirHitReaction, "flinch");
            air.Hit(Attacker, Target);
            air.Run(3);
            Assert.Equal(HitReaction.Flinch, ReactionOf(air));

            var ground = Build();
            ground.Set(FeelFieldNames.ImpactClass, "heavy");
            ground.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            SetUnitField(ground, Target, FeelFieldNames.AirHitReaction, "flinch");
            ground.Hit(Attacker, Target);
            ground.Run(3);
            Assert.Equal(HitReaction.Knockback, ReactionOf(ground));
        }

        [Fact]
        public void AirHitReaction_AttackerDeclarationWinsOverTheTargets_SameFallsThroughToTheTarget()
        {
            // 不变量：攻击方声明了非 same 的值就以它为准；攻击方 same/缺省才看受击方。
            var win = AirFx();
            SetUnitField(win, Attacker, FeelFieldNames.AirHitReaction, "none");
            SetUnitField(win, Target, FeelFieldNames.AirHitReaction, "flinch");
            win.Hit(Attacker, Target);
            win.Run(3);
            Assert.True(win.Reactions.Count == 0 || ReactionOf(win) == HitReaction.None); // 攻击方的 none 赢过受击方的 flinch

            var fall = AirFx();
            SetUnitField(fall, Attacker, FeelFieldNames.AirHitReaction, "same");
            SetUnitField(fall, Target, FeelFieldNames.AirHitReaction, "flinch");
            fall.Hit(Attacker, Target);
            fall.Run(3);
            Assert.Equal(HitReaction.Flinch, ReactionOf(fall));
        }

        [Fact]
        public void AirReactionCap_LimitsOnlyAirborneHits_AndComposesWithReactionCap()
        {
            // 复现：reaction_cap=knockdown（不封顶）、air_reaction_cap=stagger：空中 heavy 被限成 stagger；地面仍是 knockback。
            var air = AirFx();
            air.Set(FeelFieldNames.AirReactionCap, "stagger");
            air.Hit(Attacker, Target);
            air.Run(3);
            Assert.Equal(HitReaction.Stagger, ReactionOf(air));

            var ground = Build();
            ground.Set(FeelFieldNames.ImpactClass, "heavy");
            ground.Set(FeelFieldNames.AirReactionCap, "stagger");
            ground.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            ground.Hit(Attacker, Target);
            ground.Run(3);
            Assert.Equal(HitReaction.Knockback, ReactionOf(ground));

            // 不变量：与 reaction_cap 叠加取较低者（reaction_cap=flinch 更低时以它为准）。
            var both = AirFx();
            both.Set(FeelFieldNames.ReactionCap, "flinch");
            both.Set(FeelFieldNames.AirReactionCap, "stagger");
            both.Hit(Attacker, Target);
            both.Run(3);
            Assert.Equal(HitReaction.Flinch, ReactionOf(both));

            // 不变量：死亡不受它影响。
            var kill = AirFx();
            kill.Set(FeelFieldNames.AirReactionCap, "none");
            kill.Hit(Attacker, Target, kill: true);
            kill.Run(3);
            Assert.Equal(HitReaction.Death, ReactionOf(kill));
        }

        [Fact]
        public void LaunchHeightCap_IsForwardedCalibrated_MinOfAttackerAndTarget_AbsentMeansZero()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.ImpactClass, "heavy");
            fx.Set(FeelFieldNames.LaunchHeight, 1.0);
            fx.Set(FeelFieldNames.LaunchHeightCap, 2.0);
            SetUnitField(fx, Target, FeelFieldNames.LaunchHeightCap, 1.5);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            Assert.True(fx.Feel.Resolver.ResolveJudging(Target).TryGetNumber(FeelFieldNames.LaunchHeightCap, out var targetWorld));
            Assert.True(fx.Feel.Resolver.ResolveJudging(Attacker).TryGetNumber(FeelFieldNames.LaunchHeightCap, out var attackerWorld));
            Assert.True(targetWorld < attackerWorld);
            fx.Hit(Attacker, Target);
            fx.Run(30);
            Assert.Equal(targetWorld, fx.Launch.HeightCaps.Single(), 9); // 两侧都声明取较小者，且是标定后的世界单位

            var none = Build();
            none.Set(FeelFieldNames.ImpactClass, "heavy");
            none.Set(FeelFieldNames.LaunchHeight, 1.0);
            none.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            none.Hit(Attacker, Target);
            none.Run(30);
            Assert.Equal(0.0, none.Launch.HeightCaps.Single()); // 不变量：没声明 = 走 4 参数重载（与引入之前一致）
        }

        [Fact]
        public void LaunchBodyScale_ScalesTheApexOnlyWhenDeclared()
        {
            var plain = Build();
            plain.Set(FeelFieldNames.ImpactClass, "heavy");
            plain.Set(FeelFieldNames.LaunchHeight, 1.0);
            plain.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            plain.Hit(Attacker, Target);
            plain.Run(30);
            var baseApex = plain.Launch.Calls.Single().Apex;

            var scaled = Build();
            scaled.Set(FeelFieldNames.ImpactClass, "heavy");
            scaled.Set(FeelFieldNames.LaunchHeight, 1.0);
            SetUnitField(scaled, Target, FeelFieldNames.LaunchBodyScale, 0.5);
            scaled.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            scaled.Hit(Attacker, Target);
            scaled.Run(30);
            Assert.Equal(baseApex * 0.5, scaled.Launch.Calls.Single().Apex, 9);
        }

        [Fact]
        public void AirStunUntilLand_HoldsTheStaggerPastItsDuration_UntilTheTargetLands()
        {
            // 复现：空中受击 stagger（230ms 档）；声明 air_stun_until_land 后，时长到点仍在空中 → 一直硬直；落地后下一个 tick 结束。
            var hold = Build();
            hold.Set(FeelFieldNames.ImpactClass, "medium");
            hold.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.AirStunUntilLand, FeelOp.Set, FeelValue.Of(true)));
            hold.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            hold.Airborne.Air.Add(Target);
            hold.Hit(Attacker, Target);
            hold.Run(60);
            Assert.True(hold.Host.IsStaggered(Target));
            hold.Airborne.Air.Remove(Target);
            hold.Run(2);
            Assert.False(hold.Host.IsStaggered(Target));

            // 不变量：缺省不声明时按时长结束，与是否在空中无关（与引入之前一致）。
            var plain = Build();
            plain.Set(FeelFieldNames.ImpactClass, "medium");
            plain.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            plain.Airborne.Air.Add(Target);
            plain.Hit(Attacker, Target);
            plain.Run(60);
            Assert.False(plain.Host.IsStaggered(Target));
        }

        // ------------------------------------------------------------------ instant（目标选择式）适配：真实 CombatHost

        private static void CastHit(Fx fx, double baseValue = 10)
        {
            var context = new EffectContext(Attacker, Target, Skill, EffectKind.SchoolDamage, CombatTestSupport.SchoolPhysical,
                baseValue, coefficient: 1.0, isPeriodic: false, canCrit: false, canMiss: false);
            fx.Combat.ResolveEffect(context);
        }

        [Fact]
        public void InstantMode_RealCombatHost_HitGetsHitstopAndReaction()
        {
            var fx = Build();
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            var attackerExpected = Math.Min(
                Ticks(Ms(fx, Attacker, FeelFieldNames.AttackerHitstopMs)), Ticks(Ms(fx, Attacker, FeelFieldNames.AttackerHitstopCapMs)));
            var targetExpected = Ticks(Ms(fx, Target, FeelFieldNames.TargetHitstopMs));
            var stunExpected = Ticks(Ms(fx, Target, FeelFieldNames.HitStunMs));
            var healthBefore = fx.C.Powers.GetPower(Target, WellKnownPowers.Health);

            CastHit(fx);
            fx.Tick();
            Assert.True(fx.C.Powers.GetPower(Target, WellKnownPowers.Health) < healthBefore);
            Assert.Equal(attackerExpected, fx.FrozenOver(Attacker, 40));
            Assert.Equal(stunExpected, fx.StaggeredCount(Target));
            Assert.Equal(targetExpected + 1, fx.FirstStaggeredTick(Target));
            Assert.Equal(HitReaction.Stagger, fx.Reactions.Single().E.Reaction);
            Assert.Equal(0, fx.Clock.TotalPauseHandleCount);
        }

        [Fact]
        public void InstantMode_RealCombatHost_LethalHitIsDeath_AndNoStagger()
        {
            var fx = Build();
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            CastHit(fx, baseValue: 100000);
            fx.Tick();
            Assert.False(fx.C.Units.IsAlive(Target));
            fx.Run(40);
            Assert.Equal(HitReaction.Death, fx.Reactions.Single().E.Reaction);
            Assert.Equal(0, fx.StaggeredCount(Target));
            Assert.Equal(0, fx.Clock.PauseHandleCount(Target));
            Assert.Equal(0, fx.Clock.TotalPauseHandleCount);
        }

        [Fact]
        public void InstantMode_PeriodicAuraDamage_IsNotAHit()
        {
            var fx = Build();
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            var context = new EffectContext(Attacker, Target, Skill, EffectKind.SchoolDamage, CombatTestSupport.SchoolPhysical,
                10, coefficient: 1.0, isPeriodic: true, canCrit: false, canMiss: false);
            fx.Combat.ResolveEffect(context);
            fx.Run(10);
            Assert.Empty(fx.Started);
            Assert.Empty(fx.Reactions);
            Assert.Equal(0, fx.Clock.TotalPauseHandleCount);
        }

        [Fact]
        public void InstantMode_TimelineSkillsAreSkipped_SoSpatialHitsAreNotCountedTwice()
        {
            var fx = Build(configure: o => o.IsTimelineSkill = id => id == Skill);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            CastHit(fx);
            fx.Run(10);
            Assert.Empty(fx.Started);
            Assert.Empty(fx.Reactions);
        }

        [Fact]
        public void InstantMode_KillMarkLeftByATimelineKill_IsConsumed_SoALaterInstantHitInTheSameTickIsNotAKill()
        {
            // 复现：时间线技能击杀 → unit.died 先于致死那一击的 combat.damage_dealt 入队，instant 适配对时间线技能直接返回，
            // 旧实现不消费击杀标记，标记残留到下一个 tick 起点，同一 tick 内（如复活后）对同一单位的 instant 命中被误判为击杀（顿帧放大、反应 Death）。
            var other = new Id("skill.hf_other_instant");
            var fx = Build(configure: o => o.IsTimelineSkill = id => id == Skill);
            var confirmed = new List<CombatHitConfirmedEvent>();
            fx.Bus.Subscribe<CombatHitConfirmedEvent>(RulesEventKeys.CombatHitConfirmed, e => confirmed.Add(e));

            fx.Bus.Enqueue(new UnitDiedEvent(Target, Attacker));
            fx.Bus.Enqueue(new CombatDamageDealtEvent(Attacker, Target, CombatTestSupport.SchoolPhysical, 10, false, HitResult.Hit, 0, null, Skill));
            fx.Bus.Enqueue(new CombatDamageDealtEvent(Attacker, Target, CombatTestSupport.SchoolPhysical, 10, false, HitResult.Hit, 0, null, other));
            fx.Tick();

            // 时间线技能那一条被适配器跳过（hit_confirmed 由时间线路径发），只剩 instant 技能的一条，且不是击杀。
            var only = Assert.Single(confirmed);
            Assert.Equal(other, only.SkillId);
            Assert.False(only.IsKill);
        }

        [Fact]
        public void DefaultProfile_HitstopIsZeroAndArbitrationChangesNothing()
        {
            var fx = Build(profile: false);
            var healthBefore = fx.C.Powers.GetPower(Target, WellKnownPowers.Health);
            var outcome = fx.Host.Evaluate(new HitFeelInput(Attacker, Target, HitResult.Hit, 10, false));
            Assert.Equal(0, outcome.AttackerHitStopTicks);
            Assert.Equal(0, outcome.TargetHitStopTicks);
            Assert.Equal(HitReaction.None, outcome.Reaction);
            Assert.Equal(0, outcome.ReactionDurationTicks);

            CastHit(fx);
            CastHit(fx, baseValue: 100000);
            fx.Run(40);
            Assert.Empty(fx.Started);
            Assert.Empty(fx.Ended);
            Assert.Equal(0, fx.Clock.TotalPauseHandleCount);
            Assert.All(fx.Staggered.Values, list => Assert.DoesNotContain(true, list));
            Assert.Empty(fx.Interrupts.Calls);
            Assert.Empty(fx.Knock.Calls);
            // 致死命中是唯一的反应事件（死亡），且不改变伤害结算本身。
            Assert.True(fx.Reactions.Count == 1, string.Join("|", fx.Trace));
            Assert.Equal(HitReaction.Death, fx.Reactions.Single().E.Reaction);
            Assert.True(fx.C.Powers.GetPower(Target, WellKnownPowers.Health) < healthBefore);
        }

        // ------------------------------------------------------------------ 开关、释放、确定性

        [Fact]
        public void DiscreteMode_DisablesEverything()
        {
            var discrete = false;
            var fx = Build(configure: o => o.IsDiscreteMode = () => discrete);
            discrete = true;
            var outcome = fx.Host.Evaluate(new HitFeelInput(Attacker, Target, HitResult.Hit, 10, false));
            Assert.Equal(HitReaction.None, outcome.Reaction);
            Assert.Equal(0, outcome.AttackerHitStopTicks);
            fx.Bus.Enqueue(new CombatHitConfirmedEvent(
                new Id("attack.hf.discrete"), 0, Attacker, Target, Skill, HitResult.Hit, 10, 0.01, false, false,
                Vec2.Zero, new Vec2(-1, 0), new Vec2(1, 0), "medium", 5, 5, HitReaction.Stagger));
            fx.Run(10);
            Assert.Equal(0, fx.Clock.TotalPauseHandleCount);
            Assert.Empty(fx.Reactions);
        }

        [Fact]
        public void TimeModelRescaled_ReleasesAllFreezesAndStaggers()
        {
            var fx = Build();
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
            fx.Hit(Attacker, Target);
            fx.Run(1 + Ticks(Ms(fx, Target, FeelFieldNames.TargetHitstopMs)) + 2);
            Assert.True(fx.Host.IsStaggered(Target));
            fx.Bus.Enqueue(new TimeModelRescaledEvent(2.0));
            fx.Tick();
            Assert.False(fx.Host.IsStaggered(Target));
            Assert.Equal(0, fx.Clock.TotalPauseHandleCount);
        }

        [Fact]
        public void EntityDestroyed_ReleasesTheUnit()
        {
            var fx = Build();
            fx.Hit(Attacker, Target);
            fx.Tick();
            Assert.True(fx.Clock.IsPaused(Target));
            fx.Bus.Enqueue(new EntityDestroyedEvent(Target));
            fx.Tick();
            Assert.False(fx.Clock.IsPaused(Target));
            Assert.Equal(0, fx.Clock.PauseHandleCount(Target));
        }

        [Fact]
        public void Disposed_StopsReactingToEvents()
        {
            var fx = Build();
            fx.Sys.Dispose();
            fx.Hit(Attacker, Target);
            fx.Run(5);
            Assert.Equal(0, fx.Clock.TotalPauseHandleCount);
            Assert.Empty(fx.Started);
        }

        [Fact]
        public void SameScenarioTwice_ProducesTheSameEventTrace()
        {
            List<string> Run()
            {
                var fx = Build();
                fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 0);
                CastHit(fx);
                fx.Hit(Attacker, Target2);
                fx.Hit(Attacker, Target3, kill: true);
                fx.Run(80);
                return fx.Trace;
            }

            var first = Run();
            Assert.NotEmpty(first);
            Assert.Equal(first, Run());
        }

        // ------------------------------------------------------------------ 动态韧性（手感落地 M4-L）

        private sealed class PoiseProbe
        {
            public readonly List<(long Tick, CombatPoiseChangedEvent E)> Changed = new List<(long, CombatPoiseChangedEvent)>();
            public readonly List<(long Tick, CombatPoiseRecoveredEvent E)> Recovered = new List<(long, CombatPoiseRecoveredEvent)>();
        }

        private static PoiseProbe WatchPoise(Fx fx)
        {
            var probe = new PoiseProbe();
            fx.Bus.Subscribe<CombatPoiseChangedEvent>(RulesEventKeys.CombatPoiseChanged, e => probe.Changed.Add((fx.TickNo, e)));
            fx.Bus.Subscribe<CombatPoiseRecoveredEvent>(RulesEventKeys.CombatPoiseRecovered, e => probe.Recovered.Add((fx.TickNo, e)));
            return probe;
        }

        /// <summary>
        /// 夹具：韧性容量 = 3 × 每击韧性伤害（至少够挡两击），强度不高于任何击前韧性（强度取档案值，每击伤害取强度与 1 之大者）。
        /// 对照靶 Target2 的韧性属性为 0——动态韧性对没有韧性的目标不生效，走静态规则，用它拿"完整反应"的期望值。
        /// </summary>
        private static (Fx Fx, double Damage, double Max) BuildPoise()
        {
            var fx = Build();
            var power = Ms(fx, Attacker, FeelFieldNames.StaggerPower);
            var damage = Math.Max(power, 1.0);
            var max = 3.0 * damage;
            fx.Set(FeelFieldNames.PoiseDamage, damage);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, max);
            fx.C.Stats.SetBase(Target2, CombatTestSupport.StatArmor, 0);
            return (fx, damage, max);
        }

        private static HitReaction FullReaction(Fx fx) => fx.Host.Evaluate(new HitFeelInput(Attacker, Target2, HitResult.Hit, 10.0, false)).Reaction;

        [Fact]
        public void DynamicPoise_EachHitDrainsThePool_TheShelterEndsExactlyWhenThePoolReachesZero()
        {
            var (fx, damage, max) = BuildPoise();
            var probe = WatchPoise(fx);
            var full = FullReaction(fx);
            Assert.NotEqual(HitReaction.Flinch, full);

            var reactions = new List<HitReaction>();
            var expectedPool = max;
            for (var hit = 0; hit < 5; hit++)
            {
                var before = expectedPool;
                expectedPool = Math.Max(0.0, expectedPool - damage);
                fx.Hit(Attacker, Target);
                fx.Run(1);
                reactions.Add(fx.Reactions[fx.Reactions.Count - 1].E.Reaction);
                Assert.Equal(expectedPool, fx.Host.CurrentPoise(Target), 9);
                var changed = probe.Changed[probe.Changed.Count - 1].E;
                Assert.Equal(before, changed.Before, 9);
                Assert.Equal(expectedPool, changed.After, 9);
                Assert.Equal(max, changed.Max, 9);
                Assert.Equal(before > 0.0 && expectedPool <= 0.0, changed.Broken);
            }

            // 容量 = 3 次伤害：前两击被挡成 Flinch，第三击打穿（破韧）给出完整反应，之后池子是 0 的命中同样不被挡。
            Assert.Equal(new[] { HitReaction.Flinch, HitReaction.Flinch, full, full, full }, reactions);
            Assert.Equal(new[] { false, false, true, false, false }, probe.Changed.Select(c => c.E.Broken).ToArray());
        }

        [Fact]
        public void DynamicPoise_AHitThatDoesNotDeclarePoiseDamage_IsStaticAndNeverTouchesThePool()
        {
            var fx = Build();
            var power = Ms(fx, Attacker, FeelFieldNames.StaggerPower);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, power); // 静态韧性 = 强度：不高于韧性 → Flinch
            var probe = WatchPoise(fx);

            for (var i = 0; i < 4; i++)
            {
                fx.Hit(Attacker, Target);
                fx.Run(1);
            }

            Assert.All(fx.Reactions, r => Assert.Equal(HitReaction.Flinch, r.E.Reaction));
            Assert.Empty(probe.Changed);
            Assert.Empty(probe.Recovered);
            Assert.Equal(power, fx.Host.CurrentPoise(Target), 9);
        }

        [Fact]
        public void DynamicPoise_NoPoiseAttributeMeansNoPool_TheStaticRuleApplies()
        {
            var (fx, _, _) = BuildPoise();
            var probe = WatchPoise(fx);
            fx.Hit(Attacker, Target2);
            fx.Run(1);
            Assert.Empty(probe.Changed);
            Assert.Equal(FullReaction(fx), fx.Reactions.Single().E.Reaction);
        }

        [Fact]
        public void DynamicPoise_RecoversAfterTheDelayAtTheDeclaredRate_AndAnnouncesTheRefillOnce()
        {
            var (fx, damage, max) = BuildPoise();
            var probe = WatchPoise(fx);
            const double delayMs = 300.0;
            var perSecond = max; // 回满需要 1 秒
            fx.Set(FeelFieldNames.PoiseRecoverDelayMs, delayMs);
            fx.Set(FeelFieldNames.PoiseRecoverPerS, perSecond);
            var delayTicks = Ticks(delayMs);
            var perTick = perSecond * Dt;

            long breakTick = 0;
            for (var i = 0; i < 3; i++) // 打穿
            {
                breakTick = fx.TickNo; // 裁决发生在这一击之后的第一个 tick 开始之前
                fx.Hit(Attacker, Target);
                fx.Run(1);
            }

            // 裁决之后经过的 tick 数 e：e 从 1 起（刚跑完的这个 tick 就是第 1 个）；延迟耗尽后每 tick 回复 perTick。
            var refillAt = delayTicks + (int)Math.Ceiling(max / perTick - 1e-9);
            for (var e = 1; e <= refillAt + 5; e++)
            {
                if (e > 1) fx.Run(1);
                var expected = Math.Min(max, perTick * Math.Max(0, e - delayTicks));
                Assert.Equal(expected, fx.Host.CurrentPoise(Target), 6);
            }

            // 回满那一 tick 发一次 poise_recovered，之后没有重复。
            var recovered = probe.Recovered.Single();
            Assert.Equal(breakTick + refillAt - 1, recovered.Tick);
            Assert.Equal(max, recovered.E.Poise, 9);
            // 回满之后重新能挡：下一击又是 Flinch。
            fx.Hit(Attacker, Target);
            fx.Run(1);
            Assert.Equal(HitReaction.Flinch, fx.Reactions[fx.Reactions.Count - 1].E.Reaction);
            Assert.Equal(max - damage, fx.Host.CurrentPoise(Target), 9);
        }

        [Fact]
        public void DynamicPoise_AHitInsideTheDelayRestartsTheDelay_AndWithoutARateThePoolNeverRecovers()
        {
            var (fx, _, max) = BuildPoise();
            const double delayMs = 300.0;
            fx.Set(FeelFieldNames.PoiseRecoverDelayMs, delayMs);
            fx.Set(FeelFieldNames.PoiseRecoverPerS, max);
            var delayTicks = Ticks(delayMs);

            fx.Hit(Attacker, Target);
            fx.Run(delayTicks - 2);
            fx.Hit(Attacker, Target); // 延迟结束前再挨一击：延迟重新计
            fx.Run(1);
            var afterSecond = fx.Host.CurrentPoise(Target);
            fx.Run(delayTicks - 1);
            Assert.Equal(afterSecond, fx.Host.CurrentPoise(Target), 9); // 重新计的延迟里没有回复
            fx.Run(1);
            Assert.True(fx.Host.CurrentPoise(Target) > afterSecond);

            // 没声明回复速率的目标：永不回复。
            var (fx2, damage, max2) = BuildPoise();
            fx2.Hit(Attacker, Target);
            fx2.Run(600);
            Assert.Equal(max2 - damage, fx2.Host.CurrentPoise(Target), 9);
        }

        [Fact]
        public void DynamicPoise_PoolStaysInsideZeroAndMax_AcrossArbitraryHitAndRecoverySequences()
        {
            foreach (var pattern in new[] { new[] { 1, 0, 0, 5, 40 }, new[] { 1, 1, 1, 1, 1, 1, 30 }, new[] { 3, 17, 2, 90, 1, 1 } })
            {
                var (fx, damage, max) = BuildPoise();
                fx.Set(FeelFieldNames.PoiseRecoverDelayMs, 100);
                fx.Set(FeelFieldNames.PoiseRecoverPerS, damage);
                var probe = WatchPoise(fx);
                foreach (var gap in pattern)
                {
                    fx.Hit(Attacker, Target);
                    fx.Run(gap);
                    var pool = fx.Host.CurrentPoise(Target);
                    Assert.InRange(pool, 0.0, max + 1e-9);
                }

                // 每条 changed 记录自洽：after = max(0, before − 伤害)；破韧恰好是 before > 0 且 after = 0。
                foreach (var c in probe.Changed)
                {
                    Assert.Equal(Math.Max(0.0, c.E.Before - damage), c.E.After, 9);
                    Assert.Equal(c.E.Before > 0.0 && c.E.After <= 0.0, c.E.Broken);
                    Assert.InRange(c.E.Before, 0.0, max + 1e-9);
                }
            }
        }

        [Fact]
        public void DynamicPoise_SuperArmorAndKillsNeverTouchThePool_AndReleasingTheUnitResetsIt()
        {
            var fx = Build(useActions: true);
            var power = Ms(fx, Attacker, FeelFieldNames.StaggerPower);
            var damage = Math.Max(power, 1.0);
            fx.Set(FeelFieldNames.PoiseDamage, damage);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, 3.0 * damage);
            var probe = WatchPoise(fx);

            fx.Actions.SuperArmor = true; // 霸体：不走韧性（不扣池、不发事件）
            fx.Hit(Attacker, Target);
            fx.Run(1);
            fx.Actions.SuperArmor = false;
            Assert.Empty(probe.Changed);
            Assert.Equal(3.0 * damage, fx.Host.CurrentPoise(Target), 9);

            fx.Hit(Attacker, Target);
            fx.Run(1);
            Assert.Single(probe.Changed);
            Assert.Equal(2.0 * damage, fx.Host.CurrentPoise(Target), 9);

            fx.Hit(Attacker, Target, kill: true); // 击杀：Death 优先，不扣池
            fx.Run(1);
            Assert.Single(probe.Changed);
            Assert.Equal(3.0 * damage, fx.Host.CurrentPoise(Target), 9); // 死亡释放了该单位的池
        }

        [Fact]
        public void DynamicPoise_FieldsAreOptionalAndDefaultToTheOldBehavior()
        {
            var fx = Build();
            Assert.False(fx.Feel.Resolver.ResolveJudging(Attacker).TryGetNumber(FeelFieldNames.PoiseDamage, out _));
            Assert.False(fx.Feel.Resolver.ResolveJudging(Target).TryGetNumber(FeelFieldNames.PoiseRecoverPerS, out _));
            Assert.False(fx.Feel.Resolver.ResolveJudging(Target).TryGetNumber(FeelFieldNames.PoiseRecoverDelayMs, out _));
            Assert.True(FeelFields.Default.Get(FeelFieldNames.PoiseDamage).Optional);
            Assert.True(FeelFields.Default.Get(FeelFieldNames.PoiseRecoverPerS).Optional);
            Assert.True(FeelFields.Default.Get(FeelFieldNames.PoiseRecoverDelayMs).Optional);
        }

        // ------------------------------------------------------------------ 动态韧性的三项可选补充（手感落地 M4-W3）

        private static (Fx Fx, double Damage, double Max) BuildPoise(Action<HitFeelOptions> configure)
        {
            var fx = Build(configure: configure);
            var power = Ms(fx, Attacker, FeelFieldNames.StaggerPower);
            var damage = Math.Max(power, 1.0);
            var max = 3.0 * damage;
            fx.Set(FeelFieldNames.PoiseDamage, damage);
            fx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, max);
            fx.C.Stats.SetBase(Target2, CombatTestSupport.StatArmor, 0);
            return (fx, damage, max);
        }

        /// <summary>把目标打进"战斗中"：返回可变的战斗中单位集合，宿主据此判断脱战（装配根接的是 CombatHost.IsInCombat，这里用集合精确控制进出）。</summary>
        private static HashSet<Id> WireCombatState(Fx fx)
        {
            var inCombat = new HashSet<Id>();
            fx.Host.InCombat = id => inCombat.Contains(id);
            return inCombat;
        }

        /// <summary>
        /// 复现用例（脱战回复）：目标 <c>poise_recover_mode = out_of_combat</c>，被削掉一击后一直处于战斗中——此前口径（只有"延迟 + 速率"）下池子在延迟过后照常回复
        /// （战斗中的有效韧性 X → X + 回复量）；现在战斗中延迟计时与回复都暂停（X → X，不论过去多久）；脱战之后才开始计延迟：脱战后第 n 个 tick 的有效韧性
        /// = min(容量, X + 每 tick 回复量 × max(0, n − 延迟 tick 数))，逐 tick 由规则算出；回满那一 tick 恰好发一次 <c>combat.poise_recovered</c>。
        /// </summary>
        [Fact]
        public void PoiseRecoverMode_OutOfCombat_SuspendsDelayAndRegenWhileInCombat_ThenCountsTheDelayFromTheLeaveTick()
        {
            var (fx, damage, max) = BuildPoise();
            var inCombat = WireCombatState(fx);
            const double delayMs = 200.0;
            var perSecond = max; // 脱战后 1 秒回满
            fx.Set(FeelFieldNames.PoiseRecoverDelayMs, delayMs);
            fx.Set(FeelFieldNames.PoiseRecoverPerS, perSecond);
            fx.Set(FeelFieldNames.PoiseRecoverMode, "out_of_combat");
            var probe = WatchPoise(fx);
            var delayTicks = Ticks(delayMs);
            var perTick = perSecond * Dt;

            inCombat.Add(Target);
            fx.Hit(Attacker, Target);
            var afterHit = max - damage;
            Assert.Equal(afterHit, fx.Host.CurrentPoise(Target), 9);

            // 战斗中：过去远超延迟的时间，池子纹丝不动。
            fx.Run(delayTicks * 5 + 10);
            Assert.Equal(afterHit, fx.Host.CurrentPoise(Target), 9);
            Assert.Empty(probe.Recovered);

            // 脱战：延迟从脱战那一刻起算（战斗中没有消耗过任何延迟）。
            inCombat.Remove(Target);
            long leaveTick = fx.TickNo;
            var refillAfter = delayTicks + (int)Math.Ceiling((max - afterHit) / perTick - 1e-9);
            for (var n = 1; n <= refillAfter + 3; n++)
            {
                fx.Run(1);
                var expected = Math.Min(max, afterHit + perTick * Math.Max(0, n - delayTicks));
                Assert.Equal(expected, fx.Host.CurrentPoise(Target), 6);
            }

            var recovered = probe.Recovered.Single();
            Assert.Equal(leaveTick + refillAfter - 1, recovered.Tick);
            Assert.Equal(max, recovered.E.Poise, 9);
        }

        /// <summary>
        /// 不变量：回复模式缺省（delay）或显式 <c>delay</c> 时，战斗状态不参与——处于战斗中的目标照常在延迟后回复（与此前逐位一致）；
        /// 声明了 <c>out_of_combat</c> 但没有接战斗状态查询时视为一直脱战（不会卡死）；战斗中又挨一击，脱战后重新计延迟（每次动态命中重新计）。
        /// </summary>
        [Fact]
        public void PoiseRecoverMode_DelayAndNoCombatQuery_IgnoreCombatState_AndAHitRestartsTheDelayAfterLeaving()
        {
            foreach (var mode in new[] { string.Empty, "delay" })
            {
                var (fx, damage, max) = BuildPoise();
                var inCombat = WireCombatState(fx);
                fx.Set(FeelFieldNames.PoiseRecoverDelayMs, 100);
                fx.Set(FeelFieldNames.PoiseRecoverPerS, max);
                if (mode.Length > 0) fx.Set(FeelFieldNames.PoiseRecoverMode, mode);
                inCombat.Add(Target);
                fx.Hit(Attacker, Target);
                fx.Run(Ticks(100) + 3);
                Assert.True(fx.Host.CurrentPoise(Target) > max - damage, $"mode='{mode}'：战斗中也应在延迟后回复");
            }

            var (fx2, damage2, max2) = BuildPoise();
            fx2.Set(FeelFieldNames.PoiseRecoverDelayMs, 100);
            fx2.Set(FeelFieldNames.PoiseRecoverPerS, max2);
            fx2.Set(FeelFieldNames.PoiseRecoverMode, "out_of_combat");
            Assert.Null(fx2.Host.InCombat);
            fx2.Hit(Attacker, Target);
            fx2.Run(Ticks(100) + 3);
            Assert.True(fx2.Host.CurrentPoise(Target) > max2 - damage2, "没有战斗状态查询：视为一直脱战，口径同 delay");

            // 脱战回复期间再挨一击：延迟重新计，已回复的部分保留（不是回到命中前）。
            var (fx3, damage3, max3) = BuildPoise();
            var combat3 = WireCombatState(fx3);
            const double delayMs = 200.0;
            fx3.Set(FeelFieldNames.PoiseRecoverDelayMs, delayMs);
            fx3.Set(FeelFieldNames.PoiseRecoverPerS, max3);
            fx3.Set(FeelFieldNames.PoiseRecoverMode, "out_of_combat");
            var delayTicks = Ticks(delayMs);
            fx3.Hit(Attacker, Target);
            fx3.Run(delayTicks + 2); // 脱战（集合里没有它）：延迟过后已回复一点
            var partial = fx3.Host.CurrentPoise(Target);
            Assert.True(partial > max3 - damage3);
            combat3.Add(Target);
            fx3.Hit(Attacker, Target);
            var afterSecond = fx3.Host.CurrentPoise(Target);
            Assert.Equal(partial - damage3, afterSecond, 6);
            combat3.Remove(Target);
            fx3.Run(delayTicks);
            Assert.Equal(afterSecond, fx3.Host.CurrentPoise(Target), 9); // 重新计的延迟里没有回复
            fx3.Run(1);
            Assert.True(fx3.Host.CurrentPoise(Target) > afterSecond);
        }

        /// <summary>
        /// 复现用例（破韧后自动回满）：只有 <c>poise_break_reset_ms</c>、没有回复速率。此前破韧的池子永远是 0（有效韧性 0 → 0，后续命中同样不被挡）；
        /// 现在破韧之后经过 <c>ceil(poise_break_reset_ms / 步长)</c> 个 tick 一次回满（0 → 容量），恰好发一次 <c>combat.poise_recovered</c>，回满前一直是 0；
        /// 回满之后重新能挡（Flinch）。
        /// </summary>
        [Fact]
        public void PoiseBreakReset_RefillsTheWholePoolOnceAfterTheDelay_AndTheShelterReturns()
        {
            var (fx, damage, max) = BuildPoise();
            const double resetMs = 500.0;
            fx.Set(FeelFieldNames.PoiseBreakResetMs, resetMs);
            var probe = WatchPoise(fx);
            var resetTicks = Ticks(resetMs);

            long breakTick = 0;
            for (var i = 0; i < 3; i++)
            {
                breakTick = fx.TickNo;
                fx.Hit(Attacker, Target);
                fx.Run(1);
            }

            Assert.True(probe.Changed.Last().E.Broken);
            // 破韧后的第 e 个 tick（e 从 1 起）：e 未到 resetTicks 时池子是 0；到点的那个 tick 回满。
            for (var e = 1; e <= resetTicks + 5; e++)
            {
                if (e > 1) fx.Run(1);
                var expected = e >= resetTicks ? max : 0.0;
                Assert.Equal(expected, fx.Host.CurrentPoise(Target), 9);
            }

            var recovered = probe.Recovered.Single();
            Assert.Equal(breakTick + resetTicks - 1, recovered.Tick);
            Assert.Equal(max, recovered.E.Poise, 9);

            fx.Hit(Attacker, Target);
            fx.Run(1);
            Assert.Equal(HitReaction.Flinch, fx.Reactions[fx.Reactions.Count - 1].E.Reaction);
            Assert.Equal(max - damage, fx.Host.CurrentPoise(Target), 9);
        }

        /// <summary>
        /// 不变量：回满计时从"破韧"那一击起算，池子已空时的后续命中不顺延它；不受回复模式/战斗状态影响（战斗中照样回满）；
        /// 回满后再次破韧重新起算；没声明 <c>poise_break_reset_ms</c> 时不自动回满（既有行为）。
        /// </summary>
        [Fact]
        public void PoiseBreakReset_IsNotExtendedByLaterHits_IgnoresCombatState_AndRearmsOnTheNextBreak()
        {
            var (fx, _, max) = BuildPoise();
            var inCombat = WireCombatState(fx);
            const double resetMs = 400.0;
            fx.Set(FeelFieldNames.PoiseBreakResetMs, resetMs);
            fx.Set(FeelFieldNames.PoiseRecoverMode, "out_of_combat");
            fx.Set(FeelFieldNames.PoiseRecoverPerS, 1.0); // 有速率但战斗中不回复：回满只能来自破韧回满
            fx.Set(FeelFieldNames.PoiseRecoverDelayMs, 0);
            var probe = WatchPoise(fx);
            var resetTicks = Ticks(resetMs);
            inCombat.Add(Target);

            long breakTick = 0;
            for (var i = 0; i < 3; i++)
            {
                breakTick = fx.TickNo;
                fx.Hit(Attacker, Target);
                fx.Run(1);
            }

            // 池子已空，计时进行到一半时再挨一击：不顺延。
            fx.Run(resetTicks / 2 - 3);
            fx.Hit(Attacker, Target);
            fx.Run(resetTicks - (int)(fx.TickNo - breakTick) + 1);
            var first = probe.Recovered.Single();
            Assert.Equal(breakTick + resetTicks - 1, first.Tick);
            Assert.Equal(max, fx.Host.CurrentPoise(Target), 9);

            // 回满后再次打穿：重新起算。
            long secondBreak = 0;
            for (var i = 0; i < 3; i++)
            {
                secondBreak = fx.TickNo;
                fx.Hit(Attacker, Target);
                fx.Run(1);
            }

            Assert.Equal(0.0, fx.Host.CurrentPoise(Target), 9);
            fx.Run(resetTicks);
            Assert.Equal(2, probe.Recovered.Count);
            Assert.Equal(secondBreak + resetTicks - 1, probe.Recovered[1].Tick);

            // 没声明：不自动回满。
            var (fx2, _, _) = BuildPoise();
            fx2.Hit(Attacker, Target);
            fx2.Hit(Attacker, Target);
            fx2.Hit(Attacker, Target);
            fx2.Run(1);
            fx2.Run(600);
            Assert.Equal(0.0, fx2.Host.CurrentPoise(Target), 9);
        }

        /// <summary>
        /// 复现用例（冲击等级倍率）：攻击方 <c>impact_class = medium</c>、声明 <c>poise_damage = D</c>，游戏填了 <c>PoiseDamageImpactMultipliers[medium] = m</c>。
        /// 此前（没有这张表）每击扣 D（池子 3D → 2D）；现在每击扣 D × m（3D → 3D − D·m），<c>combat.poise_changed</c> 的伤害字段报告乘后的有效值，
        /// m = 1.5 时第二击就打穿（容量 3D：3D → 1.5D → 0）。表里没有的等级取 1；空表（缺省）与此前逐位一致；静态韧性（命中不声明 <c>poise_damage</c>）不读这张表。
        /// </summary>
        [Fact]
        public void PoiseDamageImpactMultipliers_ScaleThePoolDrain_ByTheAttackersImpactClass()
        {
            const double multiplier = 1.5;
            var (fx, damage, max) = BuildPoise(o => o.PoiseDamageImpactMultipliers = new Dictionary<string, double> { ["medium"] = multiplier });
            var probe = WatchPoise(fx);
            var full = FullReaction(fx);

            fx.Hit(Attacker, Target);
            fx.Run(1);
            Assert.Equal(max - damage * multiplier, fx.Host.CurrentPoise(Target), 9);
            Assert.Equal(damage * multiplier, probe.Changed.Single().E.Damage, 9);
            fx.Hit(Attacker, Target);
            fx.Run(1);
            Assert.Equal(0.0, fx.Host.CurrentPoise(Target), 9); // 3D → 1.5D → 0：第二击破韧
            Assert.True(probe.Changed[1].E.Broken);
            Assert.Equal(new[] { HitReaction.Flinch, full }, fx.Reactions.Select(r => r.E.Reaction).ToArray());

            // 对照：空表（缺省）每击扣 D，容量 3D 要三击才破。
            var (plain, _, _) = BuildPoise();
            var plainProbe = WatchPoise(plain);
            plain.Hit(Attacker, Target);
            plain.Run(1);
            Assert.Equal(max - damage, plain.Host.CurrentPoise(Target), 9);
            Assert.Equal(damage, plainProbe.Changed.Single().E.Damage, 9);

            // 表里没有该等级：取 1。
            var (other, _, _) = BuildPoise(o => o.PoiseDamageImpactMultipliers = new Dictionary<string, double> { ["massive"] = 4.0 });
            other.Hit(Attacker, Target);
            other.Run(1);
            Assert.Equal(max - damage, other.Host.CurrentPoise(Target), 9);

            // 静态韧性：命中不声明 poise_damage，表不起作用（池不被碰、没有 poise_changed）。
            var staticFx = Build(configure: o => o.PoiseDamageImpactMultipliers = new Dictionary<string, double> { ["medium"] = 9.0 });
            var power = Ms(staticFx, Attacker, FeelFieldNames.StaggerPower);
            staticFx.C.Stats.SetBase(Target, CombatTestSupport.StatArmor, power);
            var staticProbe = WatchPoise(staticFx);
            staticFx.Hit(Attacker, Target);
            staticFx.Run(1);
            Assert.Empty(staticProbe.Changed);
            Assert.Equal(power, staticFx.Host.CurrentPoise(Target), 9);
            Assert.Equal(HitReaction.Flinch, staticFx.Reactions.Single().E.Reaction);
        }

        /// <summary>不变量：每击伤害乘倍率后池子仍恒在 [0, 容量]，每条 <c>poise_changed</c> 自洽（<c>after = max(0, before − 伤害)</c>，伤害即乘后的有效值）。</summary>
        [Fact]
        public void PoiseDamageImpactMultipliers_KeepTheAccountingConsistent_AcrossHitSequences()
        {
            foreach (var m in new[] { 0.5, 1.0, 2.5 })
            {
                var (fx, damage, max) = BuildPoise(o => o.PoiseDamageImpactMultipliers = new Dictionary<string, double> { ["medium"] = m });
                fx.Set(FeelFieldNames.PoiseRecoverDelayMs, 50);
                fx.Set(FeelFieldNames.PoiseRecoverPerS, damage);
                var probe = WatchPoise(fx);
                foreach (var gap in new[] { 1, 0, 4, 20, 1, 1, 1 })
                {
                    fx.Hit(Attacker, Target);
                    fx.Run(gap);
                    Assert.InRange(fx.Host.CurrentPoise(Target), 0.0, max + 1e-9);
                }

                foreach (var c in probe.Changed)
                {
                    Assert.Equal(damage * m, c.E.Damage, 9);
                    Assert.Equal(Math.Max(0.0, c.E.Before - c.E.Damage), c.E.After, 9);
                    Assert.Equal(c.E.Before > 0.0 && c.E.After <= 0.0, c.E.Broken);
                }
            }
        }

        [Fact]
        public void PoiseDynamicsFields_AreOptional_AndTheModeVocabularyMatches()
        {
            var fx = Build();
            Assert.True(fx.Feel.Resolver.ResolveJudging(Target).GetAbsolute(FeelFieldNames.PoiseRecoverMode).IsNone);
            Assert.False(fx.Feel.Resolver.ResolveJudging(Target).TryGetNumber(FeelFieldNames.PoiseBreakResetMs, out _));
            Assert.True(FeelFields.Default.Get(FeelFieldNames.PoiseRecoverMode).Optional);
            Assert.True(FeelFields.Default.Get(FeelFieldNames.PoiseBreakResetMs).Optional);
            Assert.Equal(new[] { "delay", "out_of_combat" }, FeelFields.Default.Get(FeelFieldNames.PoiseRecoverMode).EnumValues!.ToArray());
            Assert.Empty(new HitFeelOptions().PoiseDamageImpactMultipliers); // 缺省空表：不缩放
        }

        [Fact]
        public void EventKeys_AreRegisteredInTheFrameworkCatalog()
        {
            var catalog = File.ReadAllText(Path.Combine(FindRepoRoot(), "data", "_framework", "found", "found.event_catalog.json"));
            foreach (var key in new[]
            {
                RulesEventKeys.CombatHitConfirmed, RulesEventKeys.CombatReactionApplied,
                RulesEventKeys.FeelHitstopStarted, RulesEventKeys.FeelHitstopEnded,
                RulesEventKeys.CombatPoiseChanged, RulesEventKeys.CombatPoiseRecovered,
            })
            {
                Assert.Contains("\"" + key.Value + "\"", catalog);
            }
        }
    }
}
