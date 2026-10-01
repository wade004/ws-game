// FeelWiringEndToEndTests：手感落地 S10——手感机制接进生产装配（HeadlessWorldBuilder → GameplayAssembly → Carriers/Rules → Presentation 无关部分）后的运行时冒烟。
// 一个装配好的世界、真实输入映射（StubInput → InputMapHost → 输入缓冲）、真实时间线动作、真实受击裁决与运动层；期望值全部由档案毫秒与标定 tick 率算出，不写死裸数。
// 数据来源：磁盘的 data/_framework + data/_feel + data/_lab，叠加本文件内联的覆盖层（时间线技能、带类别的输入动作、一行 rpg_classic 标定）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Foundation.SimLoop;
using Core.Carriers.Assembly;
using Core.Numbers.PowerSet;
using Core.Rules.Common;
using Core.Sim;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    public sealed class FeelWiringEndToEndTests
    {
        private const double Dt = 0.02;
        private const string FrameworkCalibration = "feel.calibration.framework_default";
        private const string RpgCalibration = "feel.calibration.fw_rpg";

        private static readonly Id MapId = new Id("world.lab_arena");
        private static readonly Id PlayerId = new Id("unit.lab_player");
        private static readonly Id SwingSkill = new Id("skill.fw_swing");
        private static readonly Id ConeSwingSkill = new Id("skill.fw_cone_swing");
        private static readonly Id FlatSwingSkill = new Id("skill.fw_flat_swing");
        private static readonly Id AssistSwingSkill = new Id("skill.fw_assist_swing");
        private static readonly Id InstantSkill = new Id("skill.lab_slash");
        private static readonly Id AttackAction = new Id("input.action.fw_attack");
        private static readonly Id StakeTemplate = new Id("creature.lab_stake");
        private static readonly Id MobTemplate = new Id("creature.lab_mob");

        private static int Ticks(double ms) => FeelCalibration.MillisecondsToTicks(ms, Dt);

        // ------------------------------------------------------------------ 数据

        // 时间线技能：前摇 100 ms、有效 60 ms、后摇 240 ms，hit 标记落在有效帧起点；cast_time 必须等于三段之和（秒）。
        // 同一套时间线数据挂不同的目标选择链：fw_swing 用实验室圆形（半径 30）链 -> 空间命中；fw_cone_swing 用本文件的扇形链 -> 空间命中但只命中扇形内；
        // fw_flat_swing 用没有 shape 的 current_target 链 -> 链无 shape 的时间线 instant 结算（目标取施法请求的显式目标）；
        // fw_assist_swing 声明目标辅助（face_only，候选取实验室圆形链）。
        private static string TimelineSkill(string id, string chain, string extra = "") => @"{
    ""id"": """ + id + @""", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 3, ""cast_time"": 0.4,
    ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": """ + chain + @""",
    ""timeline"": { ""startup_ms"": 100, ""active_ms"": 60, ""recovery_ms"": 240, ""markers"": [ { ""name"": ""hit"", ""at_ms"": 100 } ]" + extra + @" },
    ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 10, ""scaling"": [ { ""stat"": ""stat.attack_power"", ""coefficient"": 1.0 } ], ""school"": ""school.physical"" } } ]
  }";

        // 投射物时间线技能（S7b 修复 S11 缺口）：命中伤害写在 projectile 的 on_hit_effects 里。
        // releaseMarker 为真时投射物在 release 标记处发射（hit 标记只结算其余效果）；为假时随 hit 标记发射。
        private static string ProjectileTimelineSkill(string id, bool releaseMarker, bool withDirectDamage)
        {
            var marker = releaseMarker ? "release" : "hit";
            var direct = withDirectDamage
                ? @", { ""kind"": ""school_damage"", ""params"": { ""base_value"": 10, ""coefficient"": 0, ""school"": ""school.physical"" } }"
                : "";
            return @"{
    ""id"": """ + id + @""", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 3, ""cast_time"": 0.4,
    ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_nearest_enemy"",
    ""timeline"": { ""startup_ms"": 100, ""active_ms"": 60, ""recovery_ms"": 240, ""markers"": [ { ""name"": """ + marker + @""", ""at_ms"": 100 } ] },
    ""effects"": [ { ""kind"": ""projectile"", ""params"": { ""travel_mode"": ""straight"", ""hit_behavior"": ""impact_on_first"", ""speed"": 20, ""max_range"": 30,
        ""on_hit_effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 10, ""coefficient"": 0, ""school"": ""school.physical"" } } ] } }" + direct + @" ]
  }";
        }

        private static readonly string SwingSkillJson = @"{
  ""table"": ""skill.def"", ""schema_version"": 1,
  ""rows"": [ " + TimelineSkill("skill.fw_swing", "target.chain.lab_nearest_enemy") + ",\n  "
            + ProjectileTimelineSkill("skill.fw_bolt", releaseMarker: false, withDirectDamage: false) + ",\n  "
            + ProjectileTimelineSkill("skill.fw_bolt_release", releaseMarker: true, withDirectDamage: false) + ",\n  "
            + ProjectileTimelineSkill("skill.fw_mixed", releaseMarker: false, withDirectDamage: true) + ",\n  "
            + TimelineSkill("skill.fw_cone_swing", "target.chain.fw_cone_enemies") + ",\n  "
            + TimelineSkill("skill.fw_flat_swing", "target.chain.fw_current_enemy") + ",\n  "
            + TimelineSkill(
                "skill.fw_assist_swing", "target.chain.lab_nearest_enemy",
                @", ""target_assist"": { ""chain_ref"": ""target.chain.lab_nearest_enemy"", ""max_distance"": 10, ""max_angle_deg"": 90, ""mode"": ""face_only"" }")
            + @" ]
}";

        // 目标选择链：扇形（张角 90 度 = pi/2 弧度，半径 3，全部敌对存活者）与没有 shape 的当前目标链。
        private const string ChainJson = @"{
  ""table"": ""target.chain_def"", ""schema_version"": 1,
  ""rows"": [
    { ""id"": ""target.chain.fw_cone_enemies"", ""source"": ""all_in_shape"", ""shape"": { ""kind"": ""cone"", ""angle"": 1.5707963267948966, ""radius"": 3 },
      ""filters"": [""relation:hostile"", ""alive""], ""sort_by"": { ""key"": ""distance"", ""direction"": ""asc"" }, ""max_targets"": 8 },
    { ""id"": ""target.chain.fw_current_enemy"", ""source"": ""current_target"", ""filters"": [""relation:hostile"", ""alive""], ""max_targets"": 1 }
  ]
}";

        // 带类别与缓冲窗口的输入动作（class + buffer_ms + skill_slot，S10 加法字段）。
        private const string AttackActionJson = @"{
  ""table"": ""found.input_action"", ""schema_version"": 1,
  ""rows"": [ { ""key"": ""input.action.fw_attack"", ""kind"": ""button"", ""default_bindings"": [ ""key:j"" ], ""rebind_group"": ""default"",
                ""class"": ""attack"", ""buffer_ms"": 160, ""skill_slot"": ""slot_0"" } ]
}";

        // 一行 rpg_classic 标定：与框架缺省标定并存，装配时必须指定 CalibrationId。
        private const string RpgCalibrationJson = @"{
  ""table"": ""feel.calibration"", ""schema_version"": 1,
  ""rows"": [ { ""id"": ""feel.calibration.fw_rpg"", ""base_preset"": ""feel.preset.rpg_classic"", ""reference_height"": 1.0, ""base_speed"": 4.0,
                ""animation_fps"": 30.0, ""reference_camera_height"": 10.0, ""reference_zoom"": 1.0, ""pixels_per_unit"": 32.0, ""marker_tolerance_ms"": 50.0 } ]
}";

        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException());
            for (var i = 0; i < 4; i++) dir = dir.Parent ?? throw new InvalidOperationException();
            return dir.FullName;
        }

        private static void CopyDisk(StubFileSystem fs, string repoRoot, string relativeRoot)
        {
            var absoluteRoot = Path.Combine(repoRoot, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            foreach (var file in Directory.GetFiles(absoluteRoot, "*.json", SearchOption.AllDirectories))
            {
                var rel = file.Substring(absoluteRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                fs.WriteTextAtomic(relativeRoot + "/" + rel, File.ReadAllText(file));
            }
        }

        private static IReadOnlyList<IDataSource> Sources(StubFileSystem fs, bool withFeelOverlay)
        {
            var repoRoot = FindRepoRoot();
            CopyDisk(fs, repoRoot, "data/_framework");
            CopyDisk(fs, repoRoot, "data/_feel");
            CopyDisk(fs, repoRoot, "data/_lab");
            fs.WriteTextAtomic("test/_fw_base/feel/feel.calibration.json", RpgCalibrationJson);
            var sources = new List<IDataSource>
            {
                new FileSystemDataSource(fs, "data/_framework"),
                new FileSystemDataSource(fs, "data/_feel"),
                new FileSystemDataSource(fs, "data/_lab"),
                new FileSystemDataSource(fs, "test/_fw_base"),
            };
            if (withFeelOverlay)
            {
                fs.WriteTextAtomic("test/_fw_feel/skill/skill.def.json", SwingSkillJson);
                fs.WriteTextAtomic("test/_fw_feel/target/target.chain_def.json", ChainJson);
                fs.WriteTextAtomic("test/_fw_feel/found/found.input_action.json", AttackActionJson);
                sources.Add(new FileSystemDataSource(fs, "test/_fw_feel"));
            }

            return sources;
        }

        // ------------------------------------------------------------------ 驱动

        private sealed class Rig
        {
            public HeadlessWorld World = null!;
            public StubInput Input = new StubInput();
            public InputMapHost Map = null!;
            public int Tick;
            public readonly List<(int Tick, IEvent Event)> Log = new List<(int, IEvent)>();
            public readonly List<(int Tick, Vec2 Pos, MotionMode Mode)> Trace = new List<(int, Vec2, MotionMode)>();
            private int _cursor;

            public Unit Player => World.Player;
            public CarriersFeelSystem Feel => World.Gameplay.Feel!;

            /// <summary>同实验室宿主的固定步顺序：输入映射 → 移动请求 → Advance → 空间索引同步 → 采集事件。</summary>
            public void Step(Vec2? move = null)
            {
                Map.Update(Input);
                if (move.HasValue) World.Gameplay.Carriers.Movement.Request(MoveRequest.InDirection(PlayerId, move.Value));
                World.Gameplay.Advance(Dt);
                World.Spatial.UpdatePosition(PlayerId, World.Player.Position);
                while (_cursor < World.Events.Count) Log.Add((Tick, World.Events[_cursor++]));
                Trace.Add((Tick, World.Player.Position, World.Player.MovementState.Motion.Mode));
                Tick++;
            }

            public void Run(int ticks, Vec2? move = null)
            {
                for (var i = 0; i < ticks; i++) Step(move);
            }

            /// <summary>按下并在下一个 tick 抬起（一次点按）；返回按下所在的 tick。</summary>
            public int Tap()
            {
                Input.Press("j");
                var at = Tick;
                Step();
                Input.Release("j");
                return at;
            }

            public IEnumerable<(int Tick, T Event)> Of<T>() where T : IEvent =>
                Log.Where(l => l.Event is T).Select(l => (l.Tick, (T)l.Event));
        }

        private static Rig Build(bool feel, string calibration = FrameworkCalibration, bool feelOverlay = true)
        {
            var fs = new StubFileSystem();
            var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = Sources(fs, feelOverlay),
                FileSystem = fs,
                Seed = 20261002UL,
                MapId = MapId,
                PlayerId = PlayerId,
                PlayerFactionId = new Id("fac.player"),
                PlayerClassId = new Id("arch.class.lab_hero"),
                PlayerLevel = 1,
                GameId = new Id("game.lab"),
                StepSeconds = Dt,
                EnableDiscreteTimeModel = true,
                FeelOptions = feel ? new CarriersFeelOptions { CalibrationId = calibration } : null,
            });
            Assert.False(world.LoadReport.IsBlocking, string.Join("\n", world.LoadReport.Issues));

            var rig = new Rig { World = world };
            rig.Map = new InputMapHost(world.Bus);
            var definitions = new List<ActionDefinition>();
            foreach (var record in world.Registry.GetAll("found.input_action")) definitions.Add(ActionDefinition.FromRecord(record));
            rig.Map.DeclareActionSet(new Id("actionset.fw"), definitions);
            // 与 PresentationAssembly 的接线同一个调用：本地输入的按钮边沿接给输入缓冲。
            world.Gameplay.Feel?.InputBuffer.BindLocalInput(rig.Map, PlayerId, null);

            var skills = world.Gameplay.Carriers.Rules.Skill;
            if (feelOverlay)
            {
                skills.LearnSkill(PlayerId, SwingSkill);
                Assert.True(world.Gameplay.Carriers.SkillBindings.Bind(PlayerId, "slot_0", SwingSkill));
            }

            return rig;
        }

        private static Id SpawnDummy(Rig rig, Id template, Vec2 pos)
        {
            var id = rig.World.Gameplay.Carriers.Creatures.Spawn(template, MapId, pos, Math.PI, null, 1);
            rig.World.Spatial.Register(id, pos, 0.5);
            var ai = rig.World.Gameplay.Carriers.Rules.Ai;
            foreach (var registered in new List<Id>(ai.RegisteredUnitIds))
            {
                if (registered.Equals(id)) { ai.UnregisterUnit(id); break; }
            }

            return id;
        }

        // ------------------------------------------------------------------ 冒烟

        [Fact]
        public void BufferedPress_BeforeRecoveryEnds_StartsNextActionOnFirstAcceptableTick()
        {
            var rig = Build(feel: true);
            SpawnDummy(rig, StakeTemplate, new Vec2(1.5, 0));
            Assert.True(rig.Feel.Resolver.ResolveJudging(PlayerId).GetNumber(FeelFieldNames.BufferMs) > 0);

            rig.Tap();
            var started = rig.Of<ActionStartedEvent>().ToList();
            Assert.Single(started);
            var firstStart = started[0].Tick;
            var duration = started[0].Event.DurationTicks;
            Assert.Equal(Ticks(100) + Ticks(60) + Ticks(240), duration);

            // 在后摇结束前 earlyMs 毫秒按下（小于动作 buffer_ms=160）。
            const double earlyMs = 80;
            var pressAt = firstStart + duration - Ticks(earlyMs);
            rig.Run(pressAt - rig.Tick);
            var tapped = rig.Tap();
            Assert.Equal(pressAt, tapped);
            rig.Run(duration + 6);

            var finished = rig.Of<ActionFinishedEvent>().ToList();
            var starts = rig.Of<ActionStartedEvent>().ToList();
            Assert.Equal(2, starts.Count);
            Assert.NotEmpty(finished);
            // 第一个可接受的 tick = 第一个动作结束后的第一个步骤 1 → 同一 tick 的步骤 3 起手。
            Assert.Equal(finished[0].Tick + 1, starts[1].Tick);
        }

        [Fact]
        public void PressTooEarly_BeyondBufferWindow_IsDropped()
        {
            var rig = Build(feel: true);
            SpawnDummy(rig, StakeTemplate, new Vec2(1.5, 0));

            rig.Tap();
            var firstStart = rig.Of<ActionStartedEvent>().Single().Tick;
            var duration = rig.Of<ActionStartedEvent>().Single().Event.DurationTicks;
            var windowTicks = Ticks(160);

            // 比缓冲窗口早两个 tick 按下：到动作结束时已经过期。
            var pressAt = firstStart + duration - windowTicks - 2;
            rig.Run(pressAt - rig.Tick);
            rig.Tap();
            rig.Run(duration + 6);

            Assert.Single(rig.Of<ActionStartedEvent>());
        }

        [Fact]
        public void Hit_FreezesAttackerAndTargetActionClocksByProfileTicks()
        {
            var rig = Build(feel: true);
            var target = SpawnDummy(rig, StakeTemplate, new Vec2(1.5, 0));
            var clock = rig.Feel.Clock;

            // 把攻击方与目标的顿帧毫秒调成明显不同的两档（经手感系统的调试覆盖口，生产装配里同一个提供者），避免两边相等时测不出谁冻了多久。
            rig.Feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.AttackerHitstopMs, FeelOp.Set, FeelValue.Of(50)));
            rig.Feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.TargetHitstopMs, FeelOp.Set, FeelValue.Of(110)));
            rig.Tap();
            var judging = rig.Feel.Resolver.ResolveJudging(PlayerId);
            var attackerTicks = Ticks(judging.GetNumber(FeelFieldNames.AttackerHitstopMs));
            var targetTicks = Ticks(judging.GetNumber(FeelFieldNames.TargetHitstopMs));
            Assert.True(attackerTicks > 0 && targetTicks > 0 && attackerTicks != targetTicks);

            var attackerAdvance = new List<(int Tick, long Delta)>();
            var targetAdvance = new List<(int Tick, long Delta)>();
            var run = rig.Of<ActionStartedEvent>().Single().Event.DurationTicks + 10;
            for (var i = 0; i < run; i++)
            {
                var a0 = clock.ActionTicks(PlayerId);
                var t0 = clock.ActionTicks(target);
                rig.Step();
                attackerAdvance.Add((rig.Tick - 1, clock.ActionTicks(PlayerId) - a0));
                targetAdvance.Add((rig.Tick - 1, clock.ActionTicks(target) - t0));
            }

            var hit = rig.Of<CombatHitConfirmedEvent>().Single();
            Assert.Equal(attackerTicks, hit.Event.AttackerHitStopTicks);
            Assert.Equal(targetTicks, hit.Event.TargetHitStopTicks);
            Assert.Equal(attackerTicks, attackerAdvance.Count(x => x.Delta == 0));
            Assert.Equal(targetTicks, targetAdvance.Count(x => x.Delta == 0));
        }

        [Fact]
        public void PoiseBreak_TargetMotionGoesFrozenThenStaggeredThenGrounded()
        {
            var rig = Build(feel: true);
            var target = SpawnDummy(rig, StakeTemplate, new Vec2(1.5, 0));
            var targetUnit = (Unit)rig.World.World.GetEntity(target)!;

            rig.Tap();
            var judging = rig.Feel.Resolver.ResolveJudging(PlayerId);
            var targetFreeze = Ticks(judging.GetNumber(FeelFieldNames.TargetHitstopMs));
            var stun = Ticks(judging.GetNumber(FeelFieldNames.HitStunMs));
            Assert.True(targetFreeze > 0 && stun > 0);

            var modes = new List<MotionMode>();
            var hitTick = -1;
            for (var i = 0; i < 60; i++)
            {
                rig.Step();
                modes.Add(targetUnit.MovementState.Motion.Mode);
                if (hitTick < 0 && rig.Of<CombatHitConfirmedEvent>().Any()) hitTick = rig.Tick - 1;
            }

            var hit = rig.Of<CombatHitConfirmedEvent>().Single();
            Assert.Equal(HitReaction.Stagger, hit.Event.Reaction);
            // 命中的那个 tick 起：顿帧 targetFreeze 个 tick 的 Frozen，随后 stun 个 tick 的 Staggered，再回到 Grounded。
            var from = hitTick - (rig.Tick - 60) + 1;
            var after = modes.Skip(from).ToList();
            Assert.All(after.Take(targetFreeze), m => Assert.Equal(MotionMode.Frozen, m));
            Assert.All(after.Skip(targetFreeze).Take(stun), m => Assert.Equal(MotionMode.Staggered, m));
            Assert.Equal(MotionMode.Grounded, after[targetFreeze + stun]);
        }

        [Fact]
        public void HeldMoveInput_ReachesFullSpeedAfterTheTicksImpliedByAccelMs()
        {
            var rig = Build(feel: true);
            var accelTicks = Ticks(rig.Feel.Resolver.ResolveJudging(PlayerId).GetNumber(FeelFieldNames.AccelMs));
            Assert.True(accelTicks > 1);

            var axis = new Vec2(1, 0);
            var speeds = new List<double>();
            var last = rig.Player.Position;
            for (var i = 0; i < accelTicks + 6; i++)
            {
                rig.Step(axis);
                var p = rig.Player.Position;
                speeds.Add((p - last).Length / Dt);
                last = p;
            }

            var full = speeds.Max();
            Assert.True(full > 0);
            var firstFull = speeds.FindIndex(s => s >= full - 1e-9) + 1; // 1 基的 tick 序号
            Assert.Equal(accelTicks, firstFull);
            // 没到之前严格小于满速，且单调不减。
            for (var i = 0; i + 1 < accelTicks; i++) Assert.True(speeds[i] < full - 1e-9);
            for (var i = 1; i < speeds.Count; i++) Assert.True(speeds[i] >= speeds[i - 1] - 1e-9);
        }

        [Fact]
        public void StaggeredActor_ActionIsCancelledWithStaggerReason()
        {
            var rig = Build(feel: true);
            var mob = SpawnDummy(rig, MobTemplate, new Vec2(1.5, 0));
            var skills = rig.World.Gameplay.Carriers.Rules.Skill;
            skills.LearnSkill(mob, SwingSkill);

            // 怪物先对玩家施放同一技能（早玩家一个 tick 起手，它的命中标记先到）；玩家随后起手，处在前摇中时被怪物命中打进硬直，
            // 玩家的时间线动作被 Stagger 打断。
            var cast = skills.CastSkill(mob, SwingSkill, new[] { PlayerId });
            Assert.True(cast.Success, cast.Reason.ToString());
            rig.Step();
            rig.Tap();
            rig.Run(30);

            var cancelled = rig.Of<ActionCancelledEvent>().Where(c => c.Event.ActorId == PlayerId).ToList();
            Assert.Single(cancelled);
            Assert.Equal(ActionCancelReason.Stagger, cancelled[0].Event.Reason);
        }

        // ------------------------------------------------------------------ S11：空间命中、目标辅助与单次确认（经生产装配）

        /// <summary>三种命中路径：每种路径下一次命中恰好一条确认、一次顿帧评估。</summary>
        public enum HitPath
        {
            /// <summary>时间线技能 + 带 shape 的链：空间命中（marker 策略）。</summary>
            TimelineSpatial,

            /// <summary>时间线技能 + 没有 shape 的链：instant 式结算，由时间线路径自己发确认。</summary>
            TimelineChainWithoutShape,

            /// <summary>没有时间线的 instant 技能：由受击裁决的 instant 适配器合成确认。</summary>
            Instant,
        }

        private static void CastPath(Rig rig, HitPath path, Id target)
        {
            var skills = rig.World.Gameplay.Carriers.Rules.Skill;
            Id skill;
            Id[] explicitTargets;
            switch (path)
            {
                case HitPath.TimelineSpatial: skill = SwingSkill; explicitTargets = new Id[0]; break;
                case HitPath.TimelineChainWithoutShape: skill = FlatSwingSkill; explicitTargets = new[] { target }; break;
                default: skill = InstantSkill; explicitTargets = new Id[0]; break;
            }

            skills.LearnSkill(PlayerId, skill);
            var cast = skills.CastSkill(PlayerId, skill, explicitTargets);
            Assert.True(cast.Success, cast.Reason.ToString());
        }

        [Theory]
        [InlineData(HitPath.TimelineSpatial)]
        [InlineData(HitPath.TimelineChainWithoutShape)]
        [InlineData(HitPath.Instant)]
        public void EachHit_ProducesExactlyOneConfirmation_AndOneHitstopEvaluation(HitPath path)
        {
            var rig = Build(feel: true);
            var target = SpawnDummy(rig, StakeTemplate, new Vec2(1.5, 0));
            rig.Feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.AttackerHitstopMs, FeelOp.Set, FeelValue.Of(50)));
            rig.Feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.TargetHitstopMs, FeelOp.Set, FeelValue.Of(110)));
            var judging = rig.Feel.Resolver.ResolveJudging(PlayerId);
            var attackerTicks = Ticks(judging.GetNumber(FeelFieldNames.AttackerHitstopMs));
            var targetTicks = Ticks(judging.GetNumber(FeelFieldNames.TargetHitstopMs));
            Assert.True(attackerTicks > 0 && targetTicks > 0 && attackerTicks != targetTicks);

            CastPath(rig, path, target);
            rig.Run(60 + attackerTicks + targetTicks);

            // 复现（S10 装配的缺陷）：时间线技能的命中曾既由时间线路径确认、又被 instant 适配器按 combat.damage_dealt 再合成一条。
            Assert.Single(rig.Of<CombatDamageDealtEvent>());
            var hit = Assert.Single(rig.Of<CombatHitConfirmedEvent>()).Event;
            Assert.Equal(target, hit.TargetId);
            Assert.Equal(attackerTicks, hit.AttackerHitStopTicks);
            Assert.Equal(targetTicks, hit.TargetHitStopTicks);

            // 顿帧评估一次：两侧时长不同 -> 起始事件按时长分成两组，各一条；一次评估落地的冻结不会叠加第二轮。
            var started = rig.Of<FeelHitstopStartedEvent>().Select(x => x.Event).ToList();
            Assert.Equal(2, started.Count);
            Assert.Equal(attackerTicks, started.Single(e => e.UnitIds.Contains(PlayerId)).Ticks);
            Assert.Equal(targetTicks, started.Single(e => e.UnitIds.Contains(target)).Ticks);
            Assert.Single(rig.Of<CombatReactionAppliedEvent>());
        }

        /// <summary>
        /// S7b 复现（S11 缺口）：时间线技能的投射物效果随 hit 标记发射（没有 release 标记）时，其命中曾既不被时间线路径确认、
        /// 又被即时适配器按"时间线技能"跳过——没有 hit_confirmed、没有顿帧、没有受击反应。
        /// 不变量：每一次造成伤害的命中恰好一条 combat.hit_confirmed，且与伤害事件一一对应（攻击实例互不重复）。
        /// </summary>
        [Theory]
        [InlineData("skill.fw_bolt", 1)]
        [InlineData("skill.fw_bolt_release", 1)]
        [InlineData("skill.fw_mixed", 2)]
        public void TimelineProjectileHit_IsConfirmedExactlyOnce_PerDamageHit(string skillId, int expectedHits)
        {
            var rig = Build(feel: true);
            var target = SpawnDummy(rig, StakeTemplate, new Vec2(1.5, 0));
            rig.Feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.AttackerHitstopMs, FeelOp.Set, FeelValue.Of(50)));
            rig.Feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.TargetHitstopMs, FeelOp.Set, FeelValue.Of(110)));
            var skill = new Id(skillId);
            var skills = rig.World.Gameplay.Carriers.Rules.Skill;
            skills.LearnSkill(PlayerId, skill);
            var cast = skills.CastSkill(PlayerId, skill, new Id[0]);
            Assert.True(cast.Success, cast.Reason.ToString());
            rig.Run(90);

            var damages = rig.Of<CombatDamageDealtEvent>().Select(x => x.Event).ToList();
            var hits = rig.Of<CombatHitConfirmedEvent>().Select(x => x.Event).ToList();
            Assert.Equal(expectedHits, damages.Count);
            Assert.Equal(damages.Count, hits.Count);
            Assert.Equal(hits.Count, hits.Select(h => h.AttackInstanceId).Distinct().Count());
            Assert.All(hits, h => Assert.Equal(target, h.TargetId));
            Assert.True(rig.Of<FeelHitstopStartedEvent>().Any(), "确认之后应有顿帧");
        }

        [Fact]
        public void ShapedTimelineSkill_ThroughProductionAssembly_HitsOnlyTargetsInsideTheCone()
        {
            var rig = Build(feel: true);
            rig.World.Gameplay.Carriers.Units.SetFacing(PlayerId, 0.0);
            var inside = SpawnDummy(rig, StakeTemplate, new Vec2(1.5, 0));
            var behind = SpawnDummy(rig, StakeTemplate, new Vec2(-1.5, 0));
            var side = SpawnDummy(rig, StakeTemplate, new Vec2(0, 2.5));
            var powers = rig.World.Gameplay.Carriers.Rules.Powers;
            var before = new Dictionary<Id, double>
            {
                [inside] = powers.GetPower(inside, WellKnownPowers.Health),
                [behind] = powers.GetPower(behind, WellKnownPowers.Health),
                [side] = powers.GetPower(side, WellKnownPowers.Health),
            };

            var skills = rig.World.Gameplay.Carriers.Rules.Skill;
            skills.LearnSkill(PlayerId, ConeSwingSkill);
            var cast = skills.CastSkill(PlayerId, ConeSwingSkill, new Id[0]);
            Assert.True(cast.Success, cast.Reason.ToString());
            rig.Run(40);

            // 扇形外的目标（身后、侧面 90 度，都在半径 3 之内）不被命中，也没有伤害。
            var hit = Assert.Single(rig.Of<CombatHitConfirmedEvent>()).Event;
            Assert.Equal(inside, hit.TargetId);
            Assert.True(powers.GetPower(inside, WellKnownPowers.Health) < before[inside]);
            Assert.Equal(before[behind], powers.GetPower(behind, WellKnownPowers.Health));
            Assert.Equal(before[side], powers.GetPower(side, WellKnownPowers.Health));

            // 对照：同一份时间线数据、同样站位，链换成圆形（半径 30，max_targets 1）则最近的一个（侧面那个）被命中——扇形限制来自链的 shape，不是别处。
            var control = Build(feel: true);
            control.World.Gameplay.Carriers.Units.SetFacing(PlayerId, 0.0);
            var near = SpawnDummy(control, StakeTemplate, new Vec2(0, 1.0));
            SpawnDummy(control, StakeTemplate, new Vec2(1.5, 0));
            control.World.Gameplay.Carriers.Rules.Skill.LearnSkill(PlayerId, SwingSkill);
            Assert.True(control.World.Gameplay.Carriers.Rules.Skill.CastSkill(PlayerId, SwingSkill, new Id[0]).Success);
            control.Run(40);
            Assert.Equal(near, Assert.Single(control.Of<CombatHitConfirmedEvent>()).Event.TargetId);
        }

        [Fact]
        public void TargetAssist_DeclaredBySkill_TurnsTheActorByTheProfileCap_ThroughProductionAssembly()
        {
            var rig = Build(feel: true);
            var units = rig.World.Gameplay.Carriers.Units;
            units.SetFacing(PlayerId, 0.0);
            // 目标在 60 度方向、距离 2（身高倍数标定后的 10 世界单位以内）：朝向修正取 min(60, 档案 turn_assist_deg)。
            var target = SpawnDummy(rig, StakeTemplate, new Vec2(2.0 * Math.Cos(Math.PI / 3), 2.0 * Math.Sin(Math.PI / 3)));
            var cap = rig.Feel.Resolver.ResolveJudging(PlayerId).GetNumber(FeelFieldNames.TurnAssistDeg);
            Assert.True(cap > 0 && cap < 60);

            var skills = rig.World.Gameplay.Carriers.Rules.Skill;
            skills.LearnSkill(PlayerId, AssistSwingSkill);
            var cast = skills.CastSkill(PlayerId, AssistSwingSkill, new Id[0]);
            Assert.True(cast.Success, cast.Reason.ToString());
            rig.Step();

            var assisted = Assert.Single(rig.Of<ActionTargetAssistedEvent>()).Event;
            Assert.Equal(target, assisted.TargetId);
            Assert.Equal(cap, assisted.FacingDelta, 9);
            // 朝向真的被写入：生产的 IUnitAccess（WorldUnitAccess）同时是 IUnitFacingWriter。
            Assert.Equal(cap * Math.PI / 180.0, units.GetFacing(PlayerId), 9);
        }

        [Fact]
        public void TargetAssist_NotDeclaredBySkill_LeavesFacingAlone()
        {
            var rig = Build(feel: true);
            var units = rig.World.Gameplay.Carriers.Units;
            units.SetFacing(PlayerId, 0.0);
            SpawnDummy(rig, StakeTemplate, new Vec2(2.0 * Math.Cos(Math.PI / 3), 2.0 * Math.Sin(Math.PI / 3)));
            var skills = rig.World.Gameplay.Carriers.Rules.Skill;
            skills.LearnSkill(PlayerId, SwingSkill);
            Assert.True(skills.CastSkill(PlayerId, SwingSkill, new Id[0]).Success);
            rig.Step();

            Assert.Empty(rig.Of<ActionTargetAssistedEvent>());
            Assert.Equal(0.0, units.GetFacing(PlayerId), 9);
        }

        // ------------------------------------------------------------------ 逐位不变

        private static string Fingerprint(Rig rig)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var t in rig.Trace)
            {
                sb.Append(t.Tick).Append(':')
                  .Append(t.Pos.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                  .Append(t.Pos.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                  .Append(t.Mode).Append('\n');
            }

            foreach (var e in rig.Log)
            {
                // 手感系统启用时额外发布的观测事件（命中确认、顿帧起止、受击反应）不属于"行为"，单独在用例里断言其取值。
                if (e.Event is CombatHitConfirmedEvent || e.Event is FeelHitstopStartedEvent || e.Event is FeelHitstopEndedEvent
                    || e.Event is CombatReactionAppliedEvent) continue;
                sb.Append(e.Tick).Append(':').Append(e.Event.Key.Value);
                switch (e.Event)
                {
                    case SkillCastSuccessEvent s: sb.Append(' ').Append(s.SkillId.Value).Append(' ').Append(s.Targets.Count); break;
                    case SkillCastFailedEvent f: sb.Append(' ').Append(f.SkillId.Value).Append(' ').Append(f.ReasonCode); break;
                }

                sb.Append('\n');
            }

            return sb.ToString();
        }

        private static string RunBaselineScript(Rig rig)
        {
            var stake = SpawnDummy(rig, StakeTemplate, new Vec2(1.5, 0));
            var slash = new Id("skill.lab_slash");
            rig.Run(4, new Vec2(1, 0));
            for (var round = 0; round < 3; round++)
            {
                var args = new JsonObjectBuilder().Add("skill_id", new JsonString(slash.Value)).Build();
                rig.World.World.SubmitIntent(new Intent(PlayerId, "cast", args));
                rig.Run(3, new Vec2(0, 1));
                rig.Run(25);
            }

            rig.Run(10, new Vec2(-1, 0));
            Assert.True(rig.Of<SkillCastSuccessEvent>().Any());
            return Fingerprint(rig);
        }

        [Fact]
        public void FeelDisabled_VersusEnabledWithRpgClassicAndNoTimelineOrClassAction_AreBitIdentical()
        {
            var off = RunBaselineScript(Build(feel: false, feelOverlay: false));
            var enabled = Build(feel: true, calibration: RpgCalibration, feelOverlay: false);
            var on = RunBaselineScript(enabled);
            Assert.Equal(off, on);

            // 额外的观测事件在 rpg_classic 下是中性值：没有顿帧、没有受击反应。
            var hits = enabled.Of<CombatHitConfirmedEvent>().ToList();
            Assert.NotEmpty(hits);
            Assert.All(hits, h =>
            {
                Assert.Equal(0, h.Event.AttackerHitStopTicks);
                Assert.Equal(0, h.Event.TargetHitStopTicks);
                Assert.Equal(HitReaction.None, h.Event.Reaction);
            });
            Assert.Empty(enabled.Of<FeelHitstopStartedEvent>());
        }

        [Fact]
        public void FeelDisabled_LeavesTheFeelSystemAbsent()
        {
            var rig = Build(feel: false, feelOverlay: false);
            Assert.Null(rig.World.Gameplay.Feel);
        }
    }
}
