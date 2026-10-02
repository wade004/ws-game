// FeelGraceCompleteTests：手感落地 M3-B——宽限窗口与手感热重载的已知限制全部解除后，经生产装配（HeadlessWorldBuilder → GameplayAssembly → Carriers/Rules）的运行时冒烟。
// 覆盖：地面施法携带宽限条件、排队中的施法保留宽限快照（动作时钟计窗）、框架缺省的 Expr 宽限求值（游戏不写代码）、非本地行动者从登记起采样、标定热更换。
// 期望值全部由档案毫秒、标定 tick 率与数据规则算出，不写死裸数；每个用例带"量从 X 变到 Y"的复现或不变量。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Core.Sim;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    public sealed class FeelGraceCompleteTests
    {
        private const double Dt = 0.02;
        private const string FrameworkCalibration = "feel.calibration.framework_default";
        private const string CalibrationPath = "data/_feel/feel/feel.calibration.json";
        private const string ConditionPath = "test/_m3b/found/found.grace_condition.json";

        private static readonly Id MapId = new Id("world.lab_arena");
        private static readonly Id PlayerId = new Id("unit.lab_player");
        private static readonly Id StakeTemplate = new Id("creature.lab_stake");
        private static readonly Id SlashSkill = new Id("skill.m3b_slash");
        private static readonly Id GroundSkill = new Id("skill.m3b_ground");
        private static readonly Id WindupSkill = new Id("skill.m3b_windup");
        private static readonly Id LungeSkill = new Id("skill.m3b_lunge");
        private static readonly Id LongWindupSkill = new Id("skill.m3b_long_windup");
        private static readonly Id InRangeCondition = new Id("input.grace.m3b_in_range");
        private static readonly Id AlwaysCondition = new Id("input.grace.m3b_always");
        private static readonly Id[] GraceByRange = { InRangeCondition };

        private static int Ticks(double ms) => FeelCalibration.MillisecondsToTicks(ms, Dt);

        // ------------------------------------------------------------------ 数据

        // 四个技能（目标链是实验室圆形，找得到远处目标，所以目标离开射程后步骤 7 以 OUT_OF_RANGE 拒绝）：
        // slash 瞬发、射程 3；ground 瞬发地面施法、射程 3；windup 读条 2 tick、long_windup 读条 10 tick（都无射程限制，用来占住施法状态）；lunge 读条 1 tick、射程 3（排队用）。
        private const string SkillJson = @"{
  ""table"": ""skill.def"", ""schema_version"": 1,
  ""rows"": [
    { ""id"": ""skill.m3b_slash"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 3, ""cast_time"": 0,
      ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_nearest_enemy"",
      ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 10, ""coefficient"": 0, ""school"": ""school.physical"" } } ] },
    { ""id"": ""skill.m3b_ground"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 3, ""cast_time"": 0, ""ground_target"": true,
      ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_nearest_enemy"",
      ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 10, ""coefficient"": 0, ""school"": ""school.physical"" } } ] },
    { ""id"": ""skill.m3b_windup"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 0, ""cast_time"": 0.04,
      ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_nearest_enemy"",
      ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 1, ""coefficient"": 0, ""school"": ""school.physical"" } } ] },
    { ""id"": ""skill.m3b_long_windup"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 0, ""cast_time"": 0.2,
      ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_nearest_enemy"",
      ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 1, ""coefficient"": 0, ""school"": ""school.physical"" } } ] },
    { ""id"": ""skill.m3b_lunge"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 3, ""cast_time"": 0.02,
      ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_nearest_enemy"",
      ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 10, ""coefficient"": 0, ""school"": ""school.physical"" } } ] }
  ]
}";

        // 缺省 Expr 求值的条件声明：in_range 用"最近敌人距离"，不依赖目标解析；always 恒真。
        private const string RangeExpr = "enemies.nearest_distance <= 3";

        private static string ConditionJson(string rangeExpr) => @"{
  ""table"": ""found.grace_condition"", ""schema_version"": 1,
  ""rows"": [
    { ""key"": ""input.grace.m3b_in_range"", ""expr"": """ + rangeExpr + @""", ""description"": ""nearest enemy in range"" },
    { ""key"": ""input.grace.m3b_always"", ""expr"": ""true"", ""description"": ""always true"" }
  ]
}";

        // 攻击动作：grace 声明宽限条件，plain 不声明（对照）；probe 只为让 always 条件进入采样。
        private const string ActionJson = @"{
  ""table"": ""found.input_action"", ""schema_version"": 1,
  ""rows"": [
    { ""key"": ""input.action.m3b_attack_grace"", ""kind"": ""button"", ""default_bindings"": [ ""key:j"" ], ""rebind_group"": ""default"",
      ""class"": ""attack"", ""buffer_ms"": 160, ""skill_slot"": ""slot_m3b"", ""grace_conditions"": [ ""input.grace.m3b_in_range"" ] },
    { ""key"": ""input.action.m3b_attack_plain"", ""kind"": ""button"", ""default_bindings"": [ ""key:k"" ], ""rebind_group"": ""default"",
      ""class"": ""attack"", ""buffer_ms"": 160, ""skill_slot"": ""slot_m3b"" },
    { ""key"": ""input.action.m3b_probe"", ""kind"": ""button"", ""default_bindings"": [ ""key:l"" ], ""rebind_group"": ""default"",
      ""class"": ""attack"", ""buffer_ms"": 160, ""skill_slot"": ""slot_m3b"", ""grace_conditions"": [ ""input.grace.m3b_always"" ] }
  ]
}";

        // ------------------------------------------------------------------ 装配

        private sealed class DistanceEvaluator : IGraceConditionEvaluator
        {
            public Func<Id, Vec2>? Position;
            public Id? Target;

            public bool Evaluate(Id actorId, Id conditionId) =>
                Target.HasValue && Position != null && Vec2.Distance(Position(actorId), Position(Target.Value)) <= 3.0;
        }

        private sealed class Rig
        {
            public HeadlessWorld World = null!;
            public StubFileSystem Fs = null!;
            public StubInput Input = new StubInput();
            public InputMapHost Map = null!;
            public int Tick;
            public readonly List<(int Tick, IEvent Event)> Log = new List<(int, IEvent)>();
            private int _cursor;

            public CarriersFeelSystem Feel => World.Gameplay.Feel!;
            public IFeelResolver Resolver => Feel.Resolver;
            public Core.Rules.Skill.SkillHost Skills => World.Gameplay.Carriers.Rules.Skill;
            public Vec2 PlayerPosition => World.Player.Position;

            public void Step()
            {
                Map.Update(Input);
                World.Gameplay.Advance(Dt);
                World.Spatial.UpdatePosition(PlayerId, World.Player.Position);
                while (_cursor < World.Events.Count) Log.Add((Tick, World.Events[_cursor++]));
                Tick++;
            }

            public void Run(int ticks)
            {
                for (var i = 0; i < ticks; i++) Step();
            }

            public int Tap(string key)
            {
                Input.Press(key);
                var at = Tick;
                Step();
                Input.Release(key);
                return at;
            }

            public ValidationReport ReloadTable(string table, string path, Func<string, string> edit)
            {
                var text = Fs.ReadText(path) ?? throw new InvalidOperationException(path);
                var edited = edit(text);
                Assert.NotEqual(text, edited);
                Fs.WriteTextAtomic(path, edited);
                var report = World.Registry.Reload(table);
                World.Bus.PublishImmediate(new DataLoadCompletedEvent(World.Registry.Tables.Count, 0, report.ErrorCount, report.WarningCount));
                Step();
                return report;
            }

            public IEnumerable<(int Tick, T Event)> Of<T>() where T : IEvent =>
                Log.Where(l => l.Event is T).Select(l => (l.Tick, (T)l.Event));
        }

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

        /// <param name="options">手感装配选项；缺省使用框架缺省的 Expr 宽限求值（不设置 GraceEvaluator）。</param>
        private static Rig Build(CarriersFeelOptions? options = null)
        {
            var fs = new StubFileSystem();
            var repoRoot = FindRepoRoot();
            CopyDisk(fs, repoRoot, "data/_framework");
            CopyDisk(fs, repoRoot, "data/_feel");
            CopyDisk(fs, repoRoot, "data/_lab");
            fs.WriteTextAtomic("test/_m3b/skill/skill.def.json", SkillJson);
            fs.WriteTextAtomic(ConditionPath, ConditionJson(RangeExpr));
            fs.WriteTextAtomic("test/_m3b/found/found.input_action.json", ActionJson);

            options ??= new CarriersFeelOptions();
            options.CalibrationId = FrameworkCalibration;
            var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = new List<IDataSource>
                {
                    new FileSystemDataSource(fs, "data/_framework"),
                    new FileSystemDataSource(fs, "data/_feel"),
                    new FileSystemDataSource(fs, "data/_lab"),
                    new FileSystemDataSource(fs, "test/_m3b"),
                },
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
                FeelOptions = options,
            });
            Assert.False(world.LoadReport.IsBlocking, string.Join("\n", world.LoadReport.Issues));

            var rig = new Rig { World = world, Fs = fs };
            rig.Map = new InputMapHost(world.Bus);
            var definitions = new List<ActionDefinition>();
            foreach (var record in world.Registry.GetAll("found.input_action")) definitions.Add(ActionDefinition.FromRecord(record));
            rig.Map.DeclareActionSet(new Id("actionset.m3b"), definitions);
            world.Gameplay.Feel!.InputBuffer.BindLocalInput(rig.Map, PlayerId, null);

            var skills = world.Gameplay.Carriers.Rules.Skill;
            foreach (var skill in new[] { SlashSkill, GroundSkill, WindupSkill, LongWindupSkill, LungeSkill }) skills.LearnSkill(PlayerId, skill);
            Assert.True(world.Gameplay.Carriers.SkillBindings.Bind(PlayerId, "slot_m3b", SlashSkill));
            return rig;
        }

        private static (Rig Rig, Id Target, Unit TargetUnit) RigWithTarget(CarriersFeelOptions? options = null)
        {
            var rig = Build(options);
            var target = rig.World.Gameplay.Carriers.Creatures.Spawn(StakeTemplate, MapId, new Vec2(1.5, 0), Math.PI, null, 1);
            rig.World.Spatial.Register(target, new Vec2(1.5, 0), 0.5);
            var ai = rig.World.Gameplay.Carriers.Rules.Ai;
            foreach (var registered in new List<Id>(ai.RegisteredUnitIds))
            {
                if (registered.Equals(target)) { ai.UnregisterUnit(target); break; }
            }

            return (rig, target, (Unit)rig.World.World.GetEntity(target)!);
        }

        private static void MoveTargetOut(Rig rig, Id target, Unit unit)
        {
            unit.Position = new Vec2(10, 0);
            rig.World.Spatial.UpdatePosition(target, unit.Position);
        }

        private static int GraceTicks(Rig rig) =>
            Ticks(rig.Resolver.ResolveJudging(PlayerId).GetNumber(FeelFieldNames.GraceMs));

        private static GroundCastRequest GroundRequest(Rig rig, bool withGrace)
        {
            // 落点离施法者 6 个世界单位，超出射程 3：没有宽限时步骤 7' 以 OUT_OF_RANGE 拒绝。
            var request = new GroundCastRequest(new Vec2(rig.PlayerPosition.X + 6, rig.PlayerPosition.Y));
            return withGrace ? request.WithGraceConditions(GraceByRange) : request;
        }

        // ------------------------------------------------------------------ 1. 地面施法携带宽限条件

        /// <summary>
        /// 复现用例：目标在射程内（条件为真）→ 离开射程 → 之后 k 个 tick 对射程外落点发起地面施法。
        /// 不带宽限条件：一律 OutOfRange（施放 0 次）；带宽限条件：条件最近一次为真之后 <c>grace_ms</c> 换算的 tick 数以内被接受（施放 0 → 1），之后与不带时一样被拒。
        /// </summary>
        [Fact]
        public void GroundCast_WithGraceConditions_IsAcceptedWithinTheWindow_ThenRejectedAsOutOfRange_AndWithoutConditionsAlwaysRejected()
        {
            var graceTicks = GraceTicks(RigWithTarget().Rig);
            Assert.True(graceTicks >= 2, "标定下 grace_ms 应换算出至少 2 个 tick，用例才有意义");
            var accepted = 0;
            var rejected = 0;

            for (var wait = 0; wait <= graceTicks + 3; wait++)
            {
                var (rig, target, unit) = RigWithTarget();
                rig.Run(3);
                MoveTargetOut(rig, target, unit);
                rig.Run(wait);

                var tracker = rig.Feel.Grace!;
                Assert.True(tracker.LastTrueTick(PlayerId, InRangeCondition) >= 0);
                var delta = tracker.CurrentTick - tracker.LastTrueTick(PlayerId, InRangeCondition);

                var plain = rig.Skills.CastSkillAtGround(PlayerId, GroundSkill, GroundRequest(rig, withGrace: false));
                Assert.False(plain.Success);
                Assert.Equal(CastFailureReason.OutOfRange, plain.Reason);

                var graced = rig.Skills.CastSkillAtGround(PlayerId, GroundSkill, GroundRequest(rig, withGrace: true));
                // 判断记录：条件"当前仍为真"（delta=0，刚采样过）时宽限不参与——条件为真意味着游戏声明"现在可达"，射程外的落点按落点自己的几何被拒；
                // 宽限只在条件"刚刚失效、仍在窗口内"（1 <= delta <= grace_ticks）时放宽射程/视线。
                if (delta >= 1 && delta <= graceTicks)
                {
                    Assert.True(graced.Success, $"wait={wait} delta={delta} grace={graceTicks}：宽限内应被接受");
                    accepted++;
                    rig.Run(2);
                    var success = rig.Of<SkillCastSuccessEvent>().Last().Event;
                    Assert.Equal(GroundSkill, success.SkillId);
                    Assert.Equal(GroundRequest(rig, false).Point, success.GroundPoint);
                }
                else
                {
                    Assert.False(graced.Success, $"wait={wait} delta={delta} grace={graceTicks}：条件仍为真或宽限外应被拒绝");
                    Assert.Equal(CastFailureReason.OutOfRange, graced.Reason);
                    rejected++;
                }
            }

            Assert.True(accepted > 0 && rejected > 0, "扫描范围应同时覆盖宽限内与宽限外");
        }

        /// <summary>不变量：没有任何条件满足过（目标一直在射程外）时带宽限条件的地面请求与不带一样被拒；条件一直为真且落点在射程内时两者一致。</summary>
        [Fact]
        public void GroundCast_GraceNeverCoversWhenTheConditionWasNeverTrue_AndPlainRequestsAreUnchanged()
        {
            var (rig, target, unit) = RigWithTarget();
            MoveTargetOut(rig, target, unit);
            rig.Run(4);
            var neverTrue = rig.Skills.CastSkillAtGround(PlayerId, GroundSkill, GroundRequest(rig, withGrace: true));
            Assert.False(neverTrue.Success);
            Assert.Equal(CastFailureReason.OutOfRange, neverTrue.Reason);

            var (rig2, _, _) = RigWithTarget();
            rig2.Run(4);
            var inRangePoint = new GroundCastRequest(new Vec2(rig2.PlayerPosition.X + 2, rig2.PlayerPosition.Y));
            Assert.True(rig2.Skills.CastSkillAtGround(PlayerId, GroundSkill, inRangePoint).Success);
            Assert.True(rig2.Skills.CastSkillAtGround(PlayerId, GroundSkill, inRangePoint.WithGraceConditions(GraceByRange)).Success);
            Assert.Empty(inRangePoint.GraceConditions);
            Assert.Equal(GraceByRange, inRangePoint.WithGraceConditions(GraceByRange).GraceConditions.ToArray());
        }

        // ------------------------------------------------------------------ 2. 排队中的施法保留宽限上下文

        private sealed class QueueOutcome
        {
            public bool LungeSucceeded;
            public CastFailureReason? LungeFailure;
            public int FinishTick;
            public long LastTrueTick;
            public int TapTick;
        }

        /// <summary>
        /// 让 windup 先占住施法状态（读条 2 tick），再按键（槽位绑定 lunge、射程 3）经真实输入缓冲 → 施法意图在步骤内发起排队请求；返回 lunge 在 windup 结束出队时的结局。
        /// 目标先在射程内 3 tick，然后离开，等 <paramref name="wait"/> 个 tick 后按键；<paramref name="withGrace"/> 选带宽限条件的动作（j）或不带的对照动作（k）；
        /// <paramref name="pauseAfterTapTicks"/> 大于 0 时在排队之后（按键 tick 末尾）让行动者顿帧（动作时钟暂停）。
        /// （排队之前的顿帧会把整个施法意图推迟到顿帧结束，不会入队，所以顿帧只能落在排队之后。）
        /// </summary>
        private static QueueOutcome RunQueued(int wait, bool withGrace, int pauseAfterTapTicks = 0, Id? windup = null)
        {
            var (rig, target, unit) = RigWithTarget();
            Assert.True(rig.World.Gameplay.Carriers.SkillBindings.Bind(PlayerId, "slot_m3b", LungeSkill));
            rig.Run(3);
            MoveTargetOut(rig, target, unit);
            rig.Run(wait);

            var windupSkill = windup ?? WindupSkill;
            var started = rig.Skills.CastSkill(PlayerId, windupSkill, Array.Empty<Id>());
            Assert.True(started.Success, started.Reason.ToString());
            var outcome = new QueueOutcome { LastTrueTick = rig.Feel.Grace!.LastTrueTick(PlayerId, InRangeCondition) };

            outcome.TapTick = rig.Tap(withGrace ? "j" : "k");
            if (pauseAfterTapTicks > 0) rig.Feel.Rules.Clock.Pause(PlayerId, pauseAfterTapTicks);
            rig.Run(14);

            var windupDone = rig.Of<SkillCastSuccessEvent>().First(e => e.Event.SkillId.Equals(windupSkill));
            outcome.FinishTick = windupDone.Tick;
            outcome.LungeSucceeded = rig.Of<SkillCastSuccessEvent>().Any(e => e.Event.SkillId.Equals(LungeSkill));
            var failure = rig.Of<SkillCastFailedEvent>().Where(e => e.Event.SkillId.Equals(LungeSkill)).ToList();
            outcome.LungeFailure = failure.Count == 0 ? (CastFailureReason?)null : failure[0].Event.ReasonCode;
            return outcome;
        }

        /// <summary>
        /// 复现用例：条件刚失效、仍在宽限内时发起排队请求，windup 结束出队执行。此前出队按原规则判定（排队请求不带宽限上下文），一律 OutOfRange（lunge 施放 0 次）；
        /// 现在带宽限条件的请求按进入队列时的快照判定：窗口内出队被接受（0 → 1），窗口外与此前一样被拒。边界由规则算出：出队 tick 距条件最近一次为真不超过 grace_ms 换算的 tick 数。
        /// </summary>
        [Fact]
        public void QueuedCast_KeepsTheGraceSnapshot_AcceptedInsideTheWindow_RejectedAfterItExpires()
        {
            var graceTicks = GraceTicks(RigWithTarget().Rig);
            var accepted = 0;
            var rejected = 0;
            for (var wait = 0; wait <= graceTicks + 3; wait++)
            {
                var plain = RunQueued(wait, withGrace: false);
                Assert.False(plain.LungeSucceeded, $"wait={wait}：没有宽限条件的排队请求出队时目标已在射程外，应被拒");
                Assert.Equal(CastFailureReason.OutOfRange, plain.LungeFailure);

                var graced = RunQueued(wait, withGrace: true);
                var dequeueDelta = graced.FinishTick - graced.LastTrueTick;
                if (dequeueDelta <= graceTicks)
                {
                    Assert.True(graced.LungeSucceeded, $"wait={wait} dequeueDelta={dequeueDelta} grace={graceTicks}：窗口内出队应被接受");
                    accepted++;
                }
                else
                {
                    Assert.False(graced.LungeSucceeded, $"wait={wait} dequeueDelta={dequeueDelta} grace={graceTicks}：窗口外出队应被拒");
                    Assert.Equal(CastFailureReason.OutOfRange, graced.LungeFailure);
                    rejected++;
                }
            }

            Assert.True(accepted > 0 && rejected > 0, "扫描范围应同时覆盖窗口内与窗口外");
        }

        /// <summary>
        /// 复现用例：条件刚失效、按键排队时仍在窗口内，但长读条的 windup 要 10 个 tick 才结束、按模拟 tick 计窗时出队那一刻早已过期（lunge 被拒）；
        /// 排队之后行动者顿帧（动作时钟暂停）则窗口按动作时钟计，暂停期间不流逝，出队时仍在窗口内（lunge 施放 0 → 1）。
        /// 扫描所有等待长度：凡是"排队时窗口还剩至少 1 个 tick"的情形，不顿帧被拒、顿帧被接受；至少存在一个这样的情形。
        /// </summary>
        [Fact]
        public void QueuedCast_WindowCountsOnTheActionClock_SoHitstopKeepsTheSnapshotAlive()
        {
            var graceTicks = GraceTicks(RigWithTarget().Rig);
            var witnessed = 0;
            for (var wait = 0; wait <= graceTicks + 3; wait++)
            {
                var withoutPause = RunQueued(wait, withGrace: true, windup: LongWindupSkill);
                var withPause = RunQueued(wait, withGrace: true, pauseAfterTapTicks: 12, windup: LongWindupSkill);
                var queuedDelta = withoutPause.TapTick - withoutPause.LastTrueTick;
                if (queuedDelta >= 1 && queuedDelta <= graceTicks - 1)
                {
                    Assert.False(withoutPause.LungeSucceeded, $"wait={wait}：按模拟 tick 计窗，出队时已过期");
                    Assert.Equal(CastFailureReason.OutOfRange, withoutPause.LungeFailure);
                    Assert.True(withPause.LungeSucceeded, $"wait={wait}：顿帧期间动作时钟不走，按快照判定窗口尚未过期");
                    witnessed++;
                }
            }

            Assert.True(witnessed > 0, "扫描范围内应至少出现一次排队时窗口还剩至少 1 个 tick 的情形");
        }

        /// <summary>不变量：目标一直在射程内时，带宽限条件与不带的排队请求出队结果一致（宽限不参与）。</summary>
        [Fact]
        public void QueuedCast_WhenTheConditionStaysTrue_BehavesIdenticallyWithAndWithoutGraceConditions()
        {
            foreach (var withGrace in new[] { false, true })
            {
                var (rig, _, _) = RigWithTarget();
                rig.Run(3);
                Assert.True(rig.World.Gameplay.Carriers.SkillBindings.Bind(PlayerId, "slot_m3b", LungeSkill));
                Assert.True(rig.Skills.CastSkill(PlayerId, WindupSkill, Array.Empty<Id>()).Success);
                rig.Tap(withGrace ? "j" : "k");
                rig.Run(8);

                Assert.Contains(rig.Of<SkillCastSuccessEvent>(), e => e.Event.SkillId.Equals(LungeSkill));
                Assert.Empty(rig.Of<SkillCastFailedEvent>());
            }
        }

        // ------------------------------------------------------------------ 3. 框架缺省的 Expr 宽限求值

        /// <summary>
        /// 复现用例：不提供任何求值器（游戏不写代码），经真实按键与 <c>found.grace_condition</c> 数据声明的 Expr 判定。
        /// 此前缺省不装配宽限（Grace 为 null，步骤 7 照常拒绝，动作声明的 grace_conditions 无人求值）；现在缺省装配 Expr 求值器，
        /// 条件最近一次为真之后 grace_ms 换算的 tick 数以内被接受，之后被拒。
        /// </summary>
        [Fact]
        public void DefaultExprEvaluator_DrivesGraceFromDataOnly_WithNoGameCode()
        {
            var probe = RigWithTarget();
            Assert.IsType<ExprGraceConditionEvaluator>(probe.Rig.Feel.GraceEvaluator);
            Assert.NotNull(probe.Rig.Feel.Grace);
            var graceTicks = GraceTicks(probe.Rig);
            Assert.True(graceTicks >= 2);
            var accepted = 0;
            var rejected = 0;

            for (var wait = 0; wait <= graceTicks + 3; wait++)
            {
                var (rig, target, unit) = RigWithTarget();
                rig.Run(3);
                MoveTargetOut(rig, target, unit);
                rig.Run(wait);

                var pressAt = rig.Tick;
                rig.Tap("j");
                rig.Run(3);

                var lastTrue = rig.Feel.Grace!.LastTrueTick(PlayerId, InRangeCondition);
                Assert.True(lastTrue >= 0);
                var casts = rig.Of<SkillCastSuccessEvent>().Count();
                var failures = rig.Of<SkillCastFailedEvent>().Select(f => f.Event.ReasonCode).ToList();
                if (pressAt - lastTrue <= graceTicks)
                {
                    Assert.True(casts == 1, $"wait={wait}：宽限内应被接受");
                    Assert.Empty(failures);
                    accepted++;
                }
                else
                {
                    Assert.True(casts == 0, $"wait={wait}：宽限外应被拒绝");
                    Assert.Contains(CastFailureReason.OutOfRange, failures);
                    rejected++;
                }
            }

            Assert.True(accepted > 0 && rejected > 0);
        }

        /// <summary>不变量：不声明宽限条件的动作与目标一直在射程内的情形，缺省 Expr 求值器接入后与此前一致（目标刚离开时 plain 动作仍被拒）。</summary>
        [Fact]
        public void DefaultExprEvaluator_PlainActionStaysStrict_AndAnInRangeTargetNeedsNoGrace()
        {
            var (rig, target, unit) = RigWithTarget();
            rig.Run(3);
            rig.Tap("j");
            rig.Run(4);
            Assert.Single(rig.Of<SkillCastSuccessEvent>());

            MoveTargetOut(rig, target, unit);
            rig.Run(1);
            rig.Tap("k");
            rig.Run(3);
            Assert.Single(rig.Of<SkillCastSuccessEvent>());
            Assert.Contains(CastFailureReason.OutOfRange, rig.Of<SkillCastFailedEvent>().Select(f => f.Event.ReasonCode));
        }

        /// <summary>
        /// <c>CarriersFeelOptions.GraceEvaluator</c> 仍可覆盖：传入自定义求值器时 <c>Feel.GraceEvaluator</c> 就是它，数据里的 Expr 不参与。
        /// </summary>
        [Fact]
        public void GraceEvaluatorOption_StillOverridesTheDefault()
        {
            var custom = new DistanceEvaluator();
            var (rig, target, unit) = RigWithTarget(new CarriersFeelOptions { GraceEvaluator = custom });
            custom.Position = id => rig.World.World.GetEntity(id)!.Position;
            custom.Target = target;

            Assert.Same(custom, rig.Feel.GraceEvaluator);
            rig.Run(3);
            MoveTargetOut(rig, target, unit);
            rig.Run(1);
            rig.Tap("j");
            rig.Run(3);

            Assert.Single(rig.Of<SkillCastSuccessEvent>()); // 宽限内被接受，与 M2-B 一致
        }

        /// <summary>
        /// 条件表热加载：把 <c>enemies.nearest_distance &lt;= 3</c> 改成 <c>&lt;= 30</c> 后，距离 10 的目标从"不在射程内"（条件 false）变为"在射程内"（条件 true）；
        /// 改成无法解析的表达式时旧条件原样保留。
        /// </summary>
        [Fact]
        public void DefaultExprEvaluator_ConditionTableHotReload_SwapsTheExpr_AndKeepsTheOldOneWhenBroken()
        {
            var (rig, target, unit) = RigWithTarget();
            MoveTargetOut(rig, target, unit);
            rig.Run(2);
            var evaluator = (ExprGraceConditionEvaluator)rig.Feel.GraceEvaluator!;
            Assert.False(evaluator.Evaluate(PlayerId, InRangeCondition));

            rig.ReloadTable("found.grace_condition", ConditionPath, t => t.Replace(RangeExpr, "enemies.nearest_distance <= 30"));
            Assert.True(evaluator.Evaluate(PlayerId, InRangeCondition));
            Assert.Null(evaluator.LastReloadError);

            // 新表达式写坏：数据注册表校验不过（该次热加载不可读），求值器拒绝并保持旧条件、记下原因；这里不推进模拟（注册表此刻整体不可读），改好后恢复。
            var good = rig.Fs.ReadText(ConditionPath)!;
            rig.Fs.WriteTextAtomic(ConditionPath, good.Replace("enemies.nearest_distance <= 30", "enemies.nearest_distance <= <="));
            var report = rig.World.Registry.Reload("found.grace_condition");
            Assert.True(report.ErrorCount > 0);
            rig.World.Bus.PublishImmediate(new DataLoadCompletedEvent(rig.World.Registry.Tables.Count, 0, report.ErrorCount, report.WarningCount));
            Assert.NotNull(evaluator.LastReloadError);
            Assert.True(evaluator.Evaluate(PlayerId, InRangeCondition)); // 旧条件原样保留

            rig.Fs.WriteTextAtomic(ConditionPath, good);
            var fixedReport = rig.World.Registry.Reload("found.grace_condition");
            rig.World.Bus.PublishImmediate(new DataLoadCompletedEvent(rig.World.Registry.Tables.Count, 0, fixedReport.ErrorCount, fixedReport.WarningCount));
            Assert.Null(evaluator.LastReloadError);
            Assert.True(evaluator.Evaluate(PlayerId, InRangeCondition));
        }

        /// <summary>
        /// 目标解析委托：表达式用 <c>self.distance_to_target</c>，行动者当前目标由 <c>GraceTargetResolver</c> 给出（示例用"距离大于 1"以避开缺省值 0 的干扰）。
        /// 没有目标时条件恒为假（并记宿主诊断）；有目标时按目标距离求值。
        /// </summary>
        [Fact]
        public void DefaultExprEvaluator_UsesTheTargetResolver_ForTargetRelativeConditions()
        {
            Id? targetId = null;
            var (rig, target, _) = RigWithTarget(new CarriersFeelOptions { GraceTargetResolver = _ => targetId });
            rig.ReloadTable("found.grace_condition", ConditionPath, t => t.Replace(RangeExpr, "self.distance_to_target > 1"));
            var evaluator = (ExprGraceConditionEvaluator)rig.Feel.GraceEvaluator!;

            Assert.False(evaluator.Evaluate(PlayerId, InRangeCondition)); // 没有目标：distance_to_target 取缺省值 0，条件不成立
            targetId = target;
            Assert.True(evaluator.Evaluate(PlayerId, InRangeCondition));  // 目标在 1.5 处，距离 1.5 > 1
        }

        // ------------------------------------------------------------------ 4. 非本地行动者从登记起采样

        /// <summary>
        /// 复现用例：非本地行动者（生物）从未按过键。此前缓冲在首次按键时才建立，宽限追踪对它没有历史（最近一次为真 = -1）；
        /// 现在生产装配对世界里的单位自动登记，装配之后出生的生物同样从出生那一刻起采样（最近一次为真 = -1 → 当前 tick）。
        /// 关闭 <c>AutoRegisterGraceActors</c> 时保持此前行为（-1）；显式 <c>RegisterActor</c> 与自动登记等价。
        /// </summary>
        [Fact]
        public void NonLocalActors_AreSampledFromRegistration_BeforeTheirFirstKeyPress()
        {
            var (rig, target, _) = RigWithTarget();
            rig.Run(3);
            var tracker = rig.Feel.Grace!;
            Assert.True(rig.Feel.InputBuffer.IsActorRegistered(target));
            Assert.Equal(tracker.CurrentTick, tracker.LastTrueTick(target, AlwaysCondition));

            // 对照：关闭自动登记，没人按过键时没有历史。
            var (off, offTarget, _) = RigWithTarget(new CarriersFeelOptions { AutoRegisterGraceActors = false });
            off.Run(3);
            Assert.False(off.Feel.InputBuffer.IsActorRegistered(offTarget));
            Assert.Equal(-1, off.Feel.Grace!.LastTrueTick(offTarget, AlwaysCondition));

            off.Feel.InputBuffer.RegisterActor(offTarget);
            off.Run(2);
            Assert.Equal(off.Feel.Grace.CurrentTick, off.Feel.Grace.LastTrueTick(offTarget, AlwaysCondition));
        }

        /// <summary>不变量：装配之后才出生的生物，出生后第一个被采样的 tick 起就有历史（最近一次为真不晚于当前 tick）。</summary>
        [Fact]
        public void NonLocalActor_BornAfterAssembly_HasHistoryBeforeItsFirstPress()
        {
            var (rig, _, _) = RigWithTarget();
            var tracker = rig.Feel.Grace!;
            var late = rig.World.Gameplay.Carriers.Creatures.Spawn(StakeTemplate, MapId, new Vec2(-2, 0), 0, null, 1);
            Assert.Equal(-1, tracker.LastTrueTick(late, AlwaysCondition));
            rig.Run(4);
            var last = tracker.LastTrueTick(late, AlwaysCondition);
            Assert.True(last >= 0 && last <= tracker.CurrentTick);
            Assert.True(rig.Feel.InputBuffer.IsActorRegistered(late));
        }

        // ------------------------------------------------------------------ 5. 标定热更换

        /// <summary>
        /// 复现用例：改 <c>feel.calibration</c> 里的参考身高 1 → 2，经"DataRegistry.Reload + data.load_completed"热加载后，运行中的解析器标定从 1 变为 2、
        /// 身高倍数字段的绝对值随之从"相对值 × 1"变为"相对值 × 2"，热加载结果标 <c>CalibrationChanged</c> 但已换入（Applied），不再要求重启；
        /// 毫秒 → tick 的换算（只取决于模拟步长）不变。
        /// </summary>
        [Fact]
        public void CalibrationHotSwap_ReferenceHeightEdit_TakesEffectWithoutRestart()
        {
            var rig = Build();
            var before = rig.Resolver.Calibration;
            const string field = "knockback_distance";
            var raw = rig.Resolver.ResolvePresenting(PlayerId).Contains(field)
                ? rig.Resolver.ResolvePresenting(PlayerId).GetRaw(field).AsNumber()
                : rig.Resolver.ResolveJudging(PlayerId).GetRaw(field).AsNumber();
            double Absolute() => rig.Resolver.ResolvePresenting(PlayerId).Contains(field)
                ? rig.Resolver.ResolvePresenting(PlayerId).GetNumber(field)
                : rig.Resolver.ResolveJudging(PlayerId).GetNumber(field);
            var graceTicksBefore = GraceTicks(rig);

            Assert.Equal(raw * before.ReferenceHeight, Absolute(), 9);

            var newHeight = before.ReferenceHeight * 2;
            rig.ReloadTable("feel.calibration", CalibrationPath, t => t.Replace("\"reference_height\": 1.0,", "\"reference_height\": 2.0,"));

            var result = rig.Feel.LastHotReload!;
            Assert.True(result.Applied, result.Reason);
            Assert.True(result.CalibrationChanged);
            Assert.Equal(newHeight, rig.Resolver.Calibration.ReferenceHeight);
            Assert.Equal(newHeight, rig.Feel.Feel.Calibration.ReferenceHeight);
            Assert.Equal(raw * newHeight, Absolute(), 9);
            Assert.Equal(graceTicksBefore, GraceTicks(rig)); // 毫秒 → tick 只取决于步长
        }

        /// <summary>不变量：内容不变的热加载不报告标定变化，标定取值不变。</summary>
        [Fact]
        public void CalibrationHotSwap_WithNoCalibrationChange_ReportsNoChange_AndKeepsTheSameValues()
        {
            var rig = Build();
            var before = rig.Resolver.Calibration;
            var report = rig.World.Registry.Reload("feel.calibration");
            rig.World.Bus.PublishImmediate(new DataLoadCompletedEvent(rig.World.Registry.Tables.Count, 0, report.ErrorCount, report.WarningCount));
            rig.Run(1);

            var result = rig.Feel.LastHotReload!;
            Assert.True(result.Applied);
            Assert.False(result.CalibrationChanged);
            Assert.Equal(before.ReferenceHeight, rig.Resolver.Calibration.ReferenceHeight);
            Assert.Equal(before.BaseSpeed, rig.Resolver.Calibration.BaseSpeed);
            Assert.Equal(before.ReferenceCameraHeight, rig.Resolver.Calibration.ReferenceCameraHeight);
        }
    }
}
