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
        private static readonly Id AttackAction = new Id("input.action.fw_attack");
        private static readonly Id StakeTemplate = new Id("creature.lab_stake");
        private static readonly Id MobTemplate = new Id("creature.lab_mob");

        private static int Ticks(double ms) => FeelCalibration.MillisecondsToTicks(ms, Dt);

        // ------------------------------------------------------------------ 数据

        // 时间线技能：前摇 100 ms、有效 60 ms、后摇 240 ms，hit 标记落在有效帧起点；cast_time 必须等于三段之和（秒）。
        private const string SwingSkillJson = @"{
  ""table"": ""skill.def"", ""schema_version"": 1,
  ""rows"": [ {
    ""id"": ""skill.fw_swing"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 3, ""cast_time"": 0.4,
    ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_nearest_enemy"",
    ""timeline"": { ""startup_ms"": 100, ""active_ms"": 60, ""recovery_ms"": 240, ""markers"": [ { ""name"": ""hit"", ""at_ms"": 100 } ] },
    ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 10, ""scaling"": [ { ""stat"": ""stat.attack_power"", ""coefficient"": 1.0 } ], ""school"": ""school.physical"" } } ]
  } ]
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
