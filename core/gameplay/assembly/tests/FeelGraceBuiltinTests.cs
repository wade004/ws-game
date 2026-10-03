// FeelGraceBuiltinTests：手感落地 M4-G——宽限窗口的三条已知限制解除后，经生产装配（HeadlessWorldBuilder → GameplayAssembly → Carriers/Rules）的运行时冒烟。
// 覆盖：框架内置宽限条件（found.grace_condition 框架数据行 + Expr 的 event.aim_* 上下文变量，游戏只引用不写代码）；缺省 Expr 求值器的目标来源
// （游戏覆盖 → 本次施法请求携带的目标/落点 → 自动攻击目标）；宽限采样的惰性分配（没有任何动作声明宽限条件时不为单位建缓冲）。
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
using Core.Rules.Skill;
using Core.Sim;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    public sealed class FeelGraceBuiltinTests
    {
        private const double Dt = 0.02;
        private const string FrameworkCalibration = "feel.calibration.framework_default";

        private static readonly Id MapId = new Id("world.lab_arena");
        private static readonly Id PlayerId = new Id("unit.lab_player");
        private static readonly Id StakeTemplate = new Id("creature.lab_stake");
        private static readonly Id SlashSkill = new Id("skill.m4g_slash");
        private static readonly Id GroundSkill = new Id("skill.m4g_ground");

        // 框架内置条件（data/_framework/found/found.grace_condition.json）；测试自己的条件在 test/_m4g 里。
        private static readonly Id BuiltinInRange = new Id("input.grace.builtin_aim_in_range");
        private static readonly Id BuiltinLineOfSight = new Id("input.grace.builtin_aim_line_of_sight");
        private static readonly Id BuiltinReachable = new Id("input.grace.builtin_aim_reachable");
        private static readonly Id TargetNear = new Id("input.grace.m4g_target_near");
        private static readonly Id RangeIsThree = new Id("input.grace.m4g_range_is_3");
        private static readonly Id Always = new Id("input.grace.m4g_always");
        private static readonly Id[] InRangeConditions = { BuiltinInRange };

        private static int Ticks(double ms) => FeelCalibration.MillisecondsToTicks(ms, Dt);

        // ------------------------------------------------------------------ 数据

        // slash：瞬发、射程 3；ground：瞬发地面施法、射程 3（目标链是实验室圆形，找得到远处目标，所以离开射程后步骤 7 以 OUT_OF_RANGE 拒绝）。
        private const string SkillJson = @"{
  ""table"": ""skill.def"", ""schema_version"": 1,
  ""rows"": [
    { ""id"": ""skill.m4g_slash"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 3, ""cast_time"": 0,
      ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_nearest_enemy"",
      ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 10, ""coefficient"": 0, ""school"": ""school.physical"" } } ] },
    { ""id"": ""skill.m4g_ground"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 3, ""cast_time"": 0, ""ground_target"": true,
      ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_nearest_enemy"",
      ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 10, ""coefficient"": 0, ""school"": ""school.physical"" } } ] }
  ]
}";

        // 测试自己的三条条件：依赖目标的、读射程上下文的、恒真的（只为让单位进入采样）。框架内置条件不在这里（来自框架数据根）。
        private const string ConditionJson = @"{
  ""table"": ""found.grace_condition"", ""schema_version"": 1,
  ""rows"": [
    { ""key"": ""input.grace.m4g_target_near"", ""expr"": ""target.is_alive and self.distance_to_target <= 3"", ""description"": ""target group bound to the aim"" },
    { ""key"": ""input.grace.m4g_range_is_3"", ""expr"": ""event.aim_range >= 3 and event.aim_range <= 3"", ""description"": ""range context probe"" },
    { ""key"": ""input.grace.m4g_always"", ""expr"": ""true"", ""description"": ""always true"" }
  ]
}";

        // builtin：引用框架内置条件（j）；plain 不声明（k，对照）；probe 只为让 always 条件进入采样（l）。
        private const string ActionJsonWithGrace = @"{
  ""table"": ""found.input_action"", ""schema_version"": 1,
  ""rows"": [
    { ""key"": ""input.action.m4g_attack_builtin"", ""kind"": ""button"", ""default_bindings"": [ ""key:j"" ], ""rebind_group"": ""default"",
      ""class"": ""attack"", ""buffer_ms"": 160, ""skill_slot"": ""slot_m4g"", ""grace_conditions"": [ ""input.grace.builtin_aim_in_range"" ] },
    { ""key"": ""input.action.m4g_attack_plain"", ""kind"": ""button"", ""default_bindings"": [ ""key:k"" ], ""rebind_group"": ""default"",
      ""class"": ""attack"", ""buffer_ms"": 160, ""skill_slot"": ""slot_m4g"" },
    { ""key"": ""input.action.m4g_probe"", ""kind"": ""button"", ""default_bindings"": [ ""key:l"" ], ""rebind_group"": ""default"",
      ""class"": ""attack"", ""buffer_ms"": 160, ""skill_slot"": ""slot_m4g"", ""grace_conditions"": [ ""input.grace.m4g_always"" ] }
  ]
}";

        // 没有任何动作声明宽限条件的动作集（惰性分配用例）。
        private const string ActionJsonNoGrace = @"{
  ""table"": ""found.input_action"", ""schema_version"": 1,
  ""rows"": [
    { ""key"": ""input.action.m4g_attack_plain"", ""kind"": ""button"", ""default_bindings"": [ ""key:k"" ], ""rebind_group"": ""default"",
      ""class"": ""attack"", ""buffer_ms"": 160, ""skill_slot"": ""slot_m4g"" }
  ]
}";

        // ------------------------------------------------------------------ 装配

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
            public SkillHost Skills => World.Gameplay.Carriers.Rules.Skill;
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

            public void MovePlayerTo(Vec2 position)
            {
                World.Player.Position = position;
                World.Spatial.UpdatePosition(PlayerId, position);
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

        /// <param name="options">手感装配选项；缺省使用框架缺省的 Expr 宽限求值（不设置 GraceEvaluator、GraceTargetResolver）。</param>
        /// <param name="withGraceActions">动作集是否声明宽限条件（false = 没有任何动作声明宽限条件）。</param>
        /// <param name="configure">在装配前改无头世界选项（例如打开导航视线）；缺省不改。</param>
        private static Rig Build(CarriersFeelOptions? options = null, bool withGraceActions = true, Action<HeadlessWorldOptions>? configure = null)
        {
            var fs = new StubFileSystem();
            var repoRoot = FindRepoRoot();
            CopyDisk(fs, repoRoot, "data/_framework");
            CopyDisk(fs, repoRoot, "data/_feel");
            CopyDisk(fs, repoRoot, "data/_lab");
            fs.WriteTextAtomic("test/_m4g/skill/skill.def.json", SkillJson);
            fs.WriteTextAtomic("test/_m4g/found/found.grace_condition.json", ConditionJson);
            fs.WriteTextAtomic("test/_m4g/found/found.input_action.json", withGraceActions ? ActionJsonWithGrace : ActionJsonNoGrace);

            options ??= new CarriersFeelOptions();
            options.CalibrationId = FrameworkCalibration;
            var worldOptions = new HeadlessWorldOptions
            {
                DataSources = new List<IDataSource>
                {
                    new FileSystemDataSource(fs, "data/_framework"),
                    new FileSystemDataSource(fs, "data/_feel"),
                    new FileSystemDataSource(fs, "data/_lab"),
                    new FileSystemDataSource(fs, "test/_m4g"),
                },
                FileSystem = fs,
                Seed = 20261003UL,
                MapId = MapId,
                PlayerId = PlayerId,
                PlayerFactionId = new Id("fac.player"),
                PlayerClassId = new Id("arch.class.lab_hero"),
                PlayerLevel = 1,
                GameId = new Id("game.lab"),
                StepSeconds = Dt,
                EnableDiscreteTimeModel = true,
                FeelOptions = options,
            };
            configure?.Invoke(worldOptions);
            var world = HeadlessWorldBuilder.Build(worldOptions);
            Assert.False(world.LoadReport.IsBlocking, string.Join("\n", world.LoadReport.Issues));
            Assert.Equal(0, world.LoadReport.ErrorCount);

            var rig = new Rig { World = world, Fs = fs };
            rig.Map = new InputMapHost(world.Bus);
            var definitions = new List<ActionDefinition>();
            foreach (var record in world.Registry.GetAll("found.input_action")) definitions.Add(ActionDefinition.FromRecord(record));
            rig.Map.DeclareActionSet(new Id("actionset.m4g"), definitions);
            world.Gameplay.Feel!.InputBuffer.BindLocalInput(rig.Map, PlayerId, null);

            var skills = world.Gameplay.Carriers.Rules.Skill;
            foreach (var skill in new[] { SlashSkill, GroundSkill }) skills.LearnSkill(PlayerId, skill);
            Assert.True(world.Gameplay.Carriers.SkillBindings.Bind(PlayerId, "slot_m4g", SlashSkill));
            return rig;
        }

        private static (Id Id, Unit Unit) Spawn(Rig rig, Vec2 position)
        {
            var target = rig.World.Gameplay.Carriers.Creatures.Spawn(StakeTemplate, MapId, position, Math.PI, null, 1);
            rig.World.Spatial.Register(target, position, 0.5);
            var ai = rig.World.Gameplay.Carriers.Rules.Ai;
            foreach (var registered in new List<Id>(ai.RegisteredUnitIds))
            {
                if (registered.Equals(target)) { ai.UnregisterUnit(target); break; }
            }

            return (target, (Unit)rig.World.World.GetEntity(target)!);
        }

        private static void MoveOut(Rig rig, Id target, Unit unit)
        {
            unit.Position = new Vec2(10, 0);
            rig.World.Spatial.UpdatePosition(target, unit.Position);
        }

        private static int GraceTicks(Rig rig) =>
            Ticks(rig.Resolver.ResolveJudging(PlayerId).GetNumber(FeelFieldNames.GraceMs));

        private static ExprGraceConditionEvaluator Evaluator(Rig rig) => (ExprGraceConditionEvaluator)rig.Feel.GraceEvaluator!;

        private static CastResult CastAt(Rig rig, Id target, Id[] conditions) =>
            rig.Skills.CastSkillWithContext(PlayerId, SlashSkill, new[] { target }, new ActionCastContext(null, 0), conditions);

        // ------------------------------------------------------------------ 1. 框架内置宽限条件

        /// <summary>
        /// 复现用例（输入路径，游戏只引用不写代码）：动作 j 声明 <c>grace_conditions: [input.grace.builtin_aim_in_range]</c>，条件来自框架数据根，射程取动作绑定技能的射程；
        /// 自动攻击目标在射程内 3 个 tick 后离开，等 w 个 tick 后按键。不引用内置条件的对照动作 k 一律 OutOfRange（施放 0 次）；
        /// 引用内置条件的动作 j：最近一次为真之后 <c>grace_ms</c> 换算的 tick 数以内被接受（施放 0 → 1），之后与对照一样被拒。
        /// </summary>
        [Fact]
        public void BuiltinInRange_ReferencedFromAnActionByData_GivesTheWindowOnTheInputPath()
        {
            var probe = Build();
            var graceTicks = GraceTicks(probe);
            Assert.True(graceTicks >= 2);
            Assert.True(Evaluator(probe).HasCondition(BuiltinInRange), "框架数据根自带内置条件行");
            Assert.Equal(3.0, probe.Skills.GetSkillRange(SlashSkill));
            var accepted = 0;
            var rejected = 0;

            for (var wait = 0; wait <= graceTicks + 3; wait++)
            {
                foreach (var key in new[] { "k", "j" })
                {
                    var rig = Build();
                    var (target, unit) = Spawn(rig, new Vec2(1.5, 0));
                    rig.World.Gameplay.Carriers.Rules.AutoAttack.SetTarget(PlayerId, target); // 缺省目标来源：自动攻击目标，游戏不提供任何覆盖
                    rig.Run(3);
                    MoveOut(rig, target, unit);
                    rig.Run(wait);

                    var pressAt = rig.Tick;
                    rig.Tap(key);
                    rig.Run(3);
                    var lastTrue = rig.Feel.Grace!.LastTrueTick(PlayerId, BuiltinInRange);
                    var casts = rig.Of<SkillCastSuccessEvent>().Count();
                    var failures = rig.Of<SkillCastFailedEvent>().Select(f => f.Event.ReasonCode).ToList();

                    if (key == "k")
                    {
                        Assert.Equal(0, casts);
                        Assert.Contains(CastFailureReason.OutOfRange, failures);
                    }
                    else if (pressAt - lastTrue <= graceTicks)
                    {
                        Assert.True(casts == 1, $"wait={wait}：宽限内应被接受（pressAt={pressAt} lastTrue={lastTrue} grace={graceTicks}）");
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
            }

            Assert.True(accepted > 0 && rejected > 0, "扫描范围应同时覆盖窗口内与窗口外");
        }

        /// <summary>
        /// 复现用例（地面落点）：对落点 P 以引用内置条件的请求施法（在射程内，被接受，瞄点 = P），施法者随后离开使 P 超出射程，窗口内再对同一落点施法：
        /// 不带条件的请求一律被拒（OutOfRange），带内置条件的被接受（0 → 1）；换一个落点（另一个瞄点，同样够不着）被拒——历史属于那个落点；窗口过后同样被拒。
        /// 边界由规则算出：1 &lt;= 距最近一次为真的 tick 数 &lt;= grace_ticks（条件当前仍为真时宽限不参与）。
        /// </summary>
        [Fact]
        public void BuiltinInRange_GroundPoint_TheSamePointIsCoveredInTheWindow_AnotherPointIsNot()
        {
            var graceTicks = GraceTicks(Build());
            var covered = 0;
            var notCovered = 0;
            var point = new Vec2(2, 0);

            for (var wait = 0; wait <= graceTicks + 3; wait++)
            {
                var rig = Build();
                rig.Run(2);
                var first = rig.Skills.CastSkillAtGround(PlayerId, GroundSkill, new GroundCastRequest(point).WithGraceConditions(InRangeConditions));
                Assert.True(first.Success, "落点在射程内，首次施法被接受");
                rig.Run(2);
                var noted = rig.Feel.Grace!.LastTrueTick(PlayerId, BuiltinInRange);

                rig.MovePlayerTo(new Vec2(-4, 0)); // 离开：落点距离 6 > 射程 3
                rig.Run(wait);
                var last = rig.Feel.Grace.LastTrueTick(PlayerId, BuiltinInRange);
                var delta = rig.Feel.Grace.CurrentTick - last;
                var inWindow = last >= 0 && delta >= 1 && delta <= graceTicks; // 窗口过后瞄点作废、历史一并清掉（-1）

                var plain = rig.Skills.CastSkillAtGround(PlayerId, GroundSkill, new GroundCastRequest(point));
                Assert.False(plain.Success);
                Assert.Equal(CastFailureReason.OutOfRange, plain.Reason);

                var other = rig.Skills.CastSkillAtGround(
                    PlayerId, GroundSkill, new GroundCastRequest(new Vec2(2.5, 0)).WithGraceConditions(InRangeConditions));
                Assert.False(other.Success, "另一个落点：不拿旧落点刚才够得着的记录放行");
                Assert.Equal(CastFailureReason.OutOfRange, other.Reason);

                // 上面换落点的尝试改变了瞄点；回到原落点时历史已作废。为了单独验证"同一落点"，对另一台重新搭起的现场再做一遍。
                var rig2 = Build();
                rig2.Run(2);
                Assert.True(rig2.Skills.CastSkillAtGround(PlayerId, GroundSkill, new GroundCastRequest(point).WithGraceConditions(InRangeConditions)).Success);
                rig2.Run(2);
                rig2.MovePlayerTo(new Vec2(-4, 0));
                rig2.Run(wait);
                var last2 = rig2.Feel.Grace!.LastTrueTick(PlayerId, BuiltinInRange);
                var delta2 = rig2.Feel.Grace.CurrentTick - last2;
                var same = rig2.Skills.CastSkillAtGround(PlayerId, GroundSkill, new GroundCastRequest(point).WithGraceConditions(InRangeConditions));

                Assert.Equal(last, last2);
                Assert.True(noted >= 0);
                if (inWindow)
                {
                    Assert.True(same.Success, $"wait={wait} delta={delta2} grace={graceTicks}：同一落点在窗口内应被接受");
                    covered++;
                }
                else
                {
                    Assert.False(same.Success, $"wait={wait} delta={delta2} grace={graceTicks}：条件仍为真或窗口外应被拒");
                    Assert.Equal(CastFailureReason.OutOfRange, same.Reason);
                    notCovered++;
                }
            }

            Assert.True(covered > 0 && notCovered > 0);
        }

        /// <summary>
        /// 不变量（缺省不变）：没有动作引用内置条件时，内置行存在但从不被采样、不产生诊断，宽限追踪里没有任何条件的历史；
        /// 不带条件的施法与此前一致（离开射程立刻 OutOfRange）。
        /// </summary>
        [Fact]
        public void BuiltinConditions_NotReferenced_AreNeverSampled_AndPlainCastsAreUnchanged()
        {
            var rig = Build(withGraceActions: false);
            var (target, unit) = Spawn(rig, new Vec2(1.5, 0));
            rig.World.Gameplay.Carriers.Rules.AutoAttack.SetTarget(PlayerId, target);
            rig.Run(3);

            var evaluator = Evaluator(rig);
            Assert.True(evaluator.HasCondition(BuiltinInRange));
            Assert.True(evaluator.HasCondition(BuiltinLineOfSight));
            Assert.True(evaluator.HasCondition(BuiltinReachable));
            Assert.Empty(rig.Feel.InputBuffer.GraceConditionNames);
            Assert.Equal(-1, rig.Feel.Grace!.LastTrueTick(PlayerId, BuiltinInRange));
            Assert.Empty(evaluator.Diagnostics);

            MoveOut(rig, target, unit);
            rig.Run(1);
            rig.Tap("k");
            rig.Run(3);
            Assert.Empty(rig.Of<SkillCastSuccessEvent>());
            Assert.Contains(CastFailureReason.OutOfRange, rig.Of<SkillCastFailedEvent>().Select(f => f.Event.ReasonCode));
        }

        /// <summary>
        /// 内置条件的上下文变量：瞄点在射程内/视线畅通。三条内置行分别求值：没有瞄点时都为假；瞄点（单位或落点）在射程内且视线畅通时为真；
        /// 视线被挡（自带的视线查询返回 false）时 line_of_sight 与 reachable 为假而 in_range 仍为真；射程外时 in_range 与 reachable 为假而 line_of_sight 仍为真。
        /// </summary>
        [Fact]
        public void BuiltinConditions_ReadTheAimContext_RangeAndLineOfSight()
        {
            var rig = Build(withGraceActions: false);
            var (target, _) = Spawn(rig, new Vec2(1.5, 0));
            var losClear = true;
            var services = new GraceAimServices
            {
                Position = id => rig.World.World.GetEntity(id)?.Position,
                LineOfSight = (from, to) => losClear,
                ConditionRange = (actor, condition) => 3,
            };
            var carriers = rig.World.Gameplay.Carriers;
            var evaluator = new ExprGraceConditionEvaluator(
                rig.World.Registry, carriers.Rules.ExprHostFactory, carriers.Rules.ExprSchema, null, null, services);

            bool Eval(Id condition, GraceAim aim) => evaluator.Evaluate(PlayerId, condition, aim);

            // 没有瞄点（也没有缺省目标）：全部为假。
            foreach (var condition in new[] { BuiltinInRange, BuiltinLineOfSight, BuiltinReachable }) Assert.False(Eval(condition, GraceAim.None));

            var near = GraceAim.OfTarget(target, 3);
            Assert.True(Eval(BuiltinInRange, near));
            Assert.True(Eval(BuiltinLineOfSight, near));
            Assert.True(Eval(BuiltinReachable, near));

            var pointNear = GraceAim.OfPoint(new Vec2(2, 0), 3);
            Assert.True(Eval(BuiltinReachable, pointNear));

            var pointFar = GraceAim.OfPoint(new Vec2(9, 0), 3);
            Assert.False(Eval(BuiltinInRange, pointFar));
            Assert.True(Eval(BuiltinLineOfSight, pointFar));
            Assert.False(Eval(BuiltinReachable, pointFar));

            losClear = false;
            Assert.True(Eval(BuiltinInRange, near));
            Assert.False(Eval(BuiltinLineOfSight, near));
            Assert.False(Eval(BuiltinReachable, near));

            // 瞄点自己没带射程时取"引用条件的动作绑定技能的射程"（这里由服务给出 3）；瞄点带射程时以瞄点的为准。
            losClear = true;
            Assert.True(Eval(RangeIsThree, GraceAim.OfPoint(new Vec2(9, 0), 0)));
            Assert.False(Eval(RangeIsThree, GraceAim.OfPoint(new Vec2(9, 0), 5)));
            Assert.True(evaluator.UsesAim(BuiltinInRange));
            Assert.True(evaluator.UsesAim(TargetNear));
            Assert.False(evaluator.UsesAim(Always));
        }

        /// <summary>生产装配的射程来源：引用内置条件的动作绑定技能的射程（槽位换了技能，射程随之换）；技能射程 0 的动作不参与。</summary>
        [Fact]
        public void TheProductionRangeSource_FollowsTheSkillBoundToTheActionsSlot()
        {
            var rig = Build();
            var (target, unit) = Spawn(rig, new Vec2(5, 0)); // 距离 5：射程 3 外
            rig.World.Gameplay.Carriers.Rules.AutoAttack.SetTarget(PlayerId, target);
            var evaluator = Evaluator(rig);

            Assert.False(evaluator.Evaluate(PlayerId, BuiltinInRange)); // slash 射程 3，距离 5
            unit.Position = new Vec2(2, 0);
            rig.World.Spatial.UpdatePosition(target, unit.Position);
            Assert.True(evaluator.Evaluate(PlayerId, BuiltinInRange)); // 距离 2 <= 3
            unit.Position = new Vec2(5, 0);
            rig.World.Spatial.UpdatePosition(target, unit.Position);
            Assert.False(evaluator.Evaluate(PlayerId, BuiltinInRange));
        }

        // ------------------------------------------------------------------ 2. 目标来源：施法请求携带的目标优先

        /// <summary>
        /// 复现用例：自动攻击目标 B 在射程外、施法请求携带的目标 A 在射程内。此前（缺省求值器只看自动攻击目标）"目标在射程内"类条件对 A 为假；
        /// 现在带瞄点的求值以 A 为准（假 → 真），不带瞄点仍回落到自动攻击目标 B（逐位不变）。
        /// </summary>
        [Fact]
        public void TheIntentCarriedTarget_WinsOverTheAutoAttackTarget_AndWithoutAnAimTheFallbackIsUnchanged()
        {
            var rig = Build();
            var (a, _) = Spawn(rig, new Vec2(1.5, 0));
            var (b, unitB) = Spawn(rig, new Vec2(1.5, 1.5));
            MoveOut(rig, b, unitB);
            rig.World.Gameplay.Carriers.Rules.AutoAttack.SetTarget(PlayerId, b);
            rig.Run(2);
            var evaluator = Evaluator(rig);

            Assert.False(evaluator.Evaluate(PlayerId, TargetNear), "没有瞄点：回落到自动攻击目标 B（射程外）");
            Assert.False(evaluator.Evaluate(PlayerId, TargetNear, GraceAim.None));
            Assert.True(evaluator.Evaluate(PlayerId, TargetNear, GraceAim.OfTarget(a, 3)), "施法请求携带的目标 A 优先");
            Assert.Equal(b, evaluator.DefaultAimTarget(PlayerId));

            // 瞄点是落点时没有单位目标：target 分组仍绑定缺省目标 B。
            Assert.False(evaluator.Evaluate(PlayerId, TargetNear, GraceAim.OfPoint(new Vec2(1, 0), 3)));
        }

        /// <summary>
        /// 复现用例（端到端，单位目标）：对目标 A 以携带内置条件的请求施法（A 在射程内被接受），A 离开射程，窗口内再对 A 施法：被接受（0 → 1）；
        /// 自动攻击目标 B 始终在射程外。此前宽限条件只看自动攻击目标 B，对 A 的这次施法没有历史可言、一律 OutOfRange。不带条件的对照一律被拒。
        /// 边界：距最近一次为真 1..grace_ticks。
        /// </summary>
        [Fact]
        public void ACastOnAnExplicitTarget_UsesThatTargetsHistory_NotTheAutoAttackTargets()
        {
            var graceTicks = GraceTicks(Build());
            var accepted = 0;
            var rejected = 0;
            for (var wait = 0; wait <= graceTicks + 3; wait++)
            {
                var rig = Build();
                var (a, unitA) = Spawn(rig, new Vec2(1.5, 0));
                var (b, unitB) = Spawn(rig, new Vec2(1.5, 1.5));
                MoveOut(rig, b, unitB);
                rig.World.Gameplay.Carriers.Rules.AutoAttack.SetTarget(PlayerId, b);
                rig.Run(2);

                Assert.True(CastAt(rig, a, InRangeConditions).Success, "A 在射程内，首次施法被接受");
                rig.Run(2);
                MoveOut(rig, a, unitA);
                rig.Run(wait);

                var lastTrue = rig.Feel.Grace!.LastTrueTick(PlayerId, BuiltinInRange);
                var delta = rig.Feel.Grace.CurrentTick - lastTrue;
                var inWindow = lastTrue >= 0 && delta >= 1 && delta <= graceTicks; // 窗口过后瞄点作废、历史一并清掉（-1）

                var plain = rig.Skills.CastSkillWithContext(PlayerId, SlashSkill, new[] { a }, new ActionCastContext(null, 0));
                Assert.False(plain.Success);
                Assert.Equal(CastFailureReason.OutOfRange, plain.Reason);

                var graced = CastAt(rig, a, InRangeConditions);
                if (inWindow)
                {
                    Assert.True(graced.Success, $"wait={wait} delta={delta} grace={graceTicks}：窗口内应被接受");
                    accepted++;
                }
                else
                {
                    Assert.False(graced.Success, $"wait={wait} delta={delta} grace={graceTicks}：窗口外或条件仍为真应被拒");
                    Assert.Equal(CastFailureReason.OutOfRange, graced.Reason);
                    rejected++;
                }
            }

            Assert.True(accepted > 0 && rejected > 0);
        }

        /// <summary>
        /// 不变量（覆盖照旧优先）：游戏提供了 <c>GraceTargetResolver</c> 时，它给出非空目标就用它（即使施法请求携带了别的目标）；它给出 null 时用施法请求携带的目标，
        /// 不回落到自动攻击目标（覆盖说了"没有"就是没有）。
        /// </summary>
        [Fact]
        public void TheGraceTargetResolver_StillWins_AndItsNullFallsToTheAimButNotToTheAutoAttackTarget()
        {
            Id? resolved = null;
            var rig = Build(new CarriersFeelOptions { GraceTargetResolver = id => resolved });
            var (a, _) = Spawn(rig, new Vec2(1.5, 0));
            var (b, unitB) = Spawn(rig, new Vec2(1.5, 1.5));
            MoveOut(rig, b, unitB);
            rig.World.Gameplay.Carriers.Rules.AutoAttack.SetTarget(PlayerId, a); // 自动攻击目标 A 在射程内
            rig.Run(2);
            var evaluator = Evaluator(rig);

            resolved = b; // 覆盖指向射程外的 B：压过施法请求携带的 A
            Assert.False(evaluator.Evaluate(PlayerId, TargetNear, GraceAim.OfTarget(a, 3)));
            Assert.False(evaluator.Evaluate(PlayerId, TargetNear));

            resolved = null; // 覆盖没有目标：没有瞄点时没有目标（不回落到在射程内的自动攻击目标 A）；有瞄点时用瞄点
            Assert.False(evaluator.Evaluate(PlayerId, TargetNear));
            Assert.True(evaluator.Evaluate(PlayerId, TargetNear, GraceAim.OfTarget(a, 3)));
            Assert.Null(evaluator.DefaultAimTarget(PlayerId));
        }

        /// <summary>不变量：不依赖瞄点的数据条件（如 true）求值不受瞄点影响，其历史在瞄点变化时保留；依赖目标的条件在换瞄点时历史作废。</summary>
        [Fact]
        public void ConditionsThatDoNotUseTheAim_KeepTheirHistory_WhileTargetConditionsDropTheirs()
        {
            var rig = Build();
            var (a, _) = Spawn(rig, new Vec2(1.5, 0));
            var (b, _) = Spawn(rig, new Vec2(1.5, 1.5));
            rig.World.Gameplay.Carriers.Rules.AutoAttack.SetTarget(PlayerId, a);
            rig.Run(3);
            var tracker = rig.Feel.Grace!;
            Assert.Equal(tracker.CurrentTick, tracker.LastTrueTick(PlayerId, Always));

            tracker.NoteAim(PlayerId, GraceAim.OfTarget(b, 3));
            Assert.Equal(tracker.CurrentTick, tracker.LastTrueTick(PlayerId, Always));
            Assert.False(Evaluator(rig).UsesAim(Always));
            Assert.True(Evaluator(rig).UsesAim(TargetNear));
        }

        // ------------------------------------------------------------------ 2b. 链解析出来的目标同样是瞄点（M4-W3）

        private static CastResult CastByChain(Rig rig, Id[] conditions) =>
            rig.Skills.CastSkillWithContext(PlayerId, SlashSkill, Array.Empty<Id>(), new ActionCastContext(null, 0), conditions);

        /// <summary>
        /// 复现用例：请求不携带目标，由技能自己的目标链解析出 A（在射程内，被接受）。此前链解析出的目标不记为瞄点（当前瞄点 None），
        /// A 离开射程后窗口内再施法一律 OutOfRange（历史只来自"缺省目标"，这里没有）；现在链解析出的首个目标按同一归属规则记为瞄点（None → A），窗口内被接受（0 → 1）。
        /// 不带条件的对照一律被拒。边界由规则算出：1 &lt;= 距最近一次为真的 tick 数 &lt;= grace_ticks（窗口过后瞄点作废、历史清零）。
        /// </summary>
        [Fact]
        public void AChainResolvedTarget_IsRecordedAsTheAim_AndGivesTheWindowForThatTarget()
        {
            var graceTicks = GraceTicks(Build());
            var accepted = 0;
            var rejected = 0;
            for (var wait = 0; wait <= graceTicks + 3; wait++)
            {
                var rig = Build();
                var (a, unitA) = Spawn(rig, new Vec2(1.5, 0));
                rig.Run(2);
                Assert.Equal(GraceAim.None, rig.Feel.Grace!.CurrentAim(PlayerId));

                Assert.True(CastByChain(rig, InRangeConditions).Success, "A 在射程内，目标链解析出 A，首次施法被接受");
                Assert.Equal(GraceAim.OfTarget(a, 3), rig.Feel.Grace.CurrentAim(PlayerId));
                rig.Run(2);
                MoveOut(rig, a, unitA);
                rig.Run(wait);

                var lastTrue = rig.Feel.Grace.LastTrueTick(PlayerId, BuiltinInRange);
                var delta = rig.Feel.Grace.CurrentTick - lastTrue;
                var inWindow = lastTrue >= 0 && delta >= 1 && delta <= graceTicks;

                var plain = rig.Skills.CastSkillWithContext(PlayerId, SlashSkill, Array.Empty<Id>(), new ActionCastContext(null, 0));
                Assert.False(plain.Success);
                Assert.Equal(CastFailureReason.OutOfRange, plain.Reason);

                var graced = CastByChain(rig, InRangeConditions);
                if (inWindow)
                {
                    Assert.True(graced.Success, $"wait={wait} delta={delta} grace={graceTicks}：窗口内应被接受");
                    accepted++;
                }
                else
                {
                    Assert.False(graced.Success, $"wait={wait} delta={delta} grace={graceTicks}：窗口外应被拒");
                    Assert.Equal(CastFailureReason.OutOfRange, graced.Reason);
                    rejected++;
                }
            }

            Assert.True(accepted > 0 && rejected > 0);
        }

        /// <summary>
        /// 不变量（瞄点属于携带宽限条件的请求，M4-W3 设计决定）：不携带宽限条件的施法（普通施法、别的单位目标）既不改瞄点、也不动依赖目标的条件的历史——
        /// 对 A 的带条件施法之后，对 B 的无条件施法前后：当前瞄点仍是 A（值与寿命都不变）、内置条件的最近为真 tick 不变；
        /// 此前口径与现在一致（无条件请求从不上报瞄点），本用例把"没有宽限条件的行为与引入宽限前逐位一致"钉成可观测量。
        /// </summary>
        [Fact]
        public void ACastWithoutGraceConditions_NeverTouchesTheAimOrTheHistory()
        {
            var rig = Build();
            var (a, _) = Spawn(rig, new Vec2(1.5, 0));
            var (b, _) = Spawn(rig, new Vec2(1.5, 1.0));
            rig.Run(2);
            Assert.True(CastAt(rig, a, InRangeConditions).Success);
            var aimBefore = rig.Feel.Grace!.CurrentAim(PlayerId);
            var historyBefore = rig.Feel.Grace.LastTrueTick(PlayerId, BuiltinInRange);
            Assert.Equal(GraceAim.OfTarget(a, 3), aimBefore);
            Assert.True(historyBefore >= 0);

            var plain = rig.Skills.CastSkillWithContext(PlayerId, SlashSkill, new[] { b }, new ActionCastContext(null, 0));
            Assert.True(plain.Success);
            Assert.Equal(aimBefore, rig.Feel.Grace.CurrentAim(PlayerId));
            Assert.Equal(historyBefore, rig.Feel.Grace.LastTrueTick(PlayerId, BuiltinInRange));
        }

        /// <summary>
        /// 不变量（历史属于瞄点）：链解析出的目标换成另一个单位（B）时，依赖目标的条件历史作废——不拿 A 刚才够得着的记录放行对 B 的施法：
        /// A 够得着后离开，B 出现在射程外但比 A 更近（目标链改选 B），窗口内带条件的链施法被拒（OutOfRange）；对照（没有 B）窗口内被接受。
        /// </summary>
        [Fact]
        public void AChainResolvedTargetThatChanges_DropsTheOldTargetsHistory()
        {
            bool RunCase(bool withOtherTarget)
            {
                var rig = Build();
                var (a, unitA) = Spawn(rig, new Vec2(1.5, 0));
                rig.Run(2);
                Assert.True(CastByChain(rig, InRangeConditions).Success);
                rig.Run(1);
                MoveOut(rig, a, unitA); // A 在 (10,0)
                if (withOtherTarget)
                {
                    Spawn(rig, new Vec2(9, 0)); // 比 A 近但仍在射程 3 外：目标链改选它
                }

                rig.Run(1);
                var result = CastByChain(rig, InRangeConditions);
                if (!result.Success) Assert.Equal(CastFailureReason.OutOfRange, result.Reason);
                return result.Success;
            }

            Assert.True(RunCase(false), "对照：目标链仍解析出 A，窗口内被接受");
            Assert.False(RunCase(true), "目标链改选了 B：A 的历史不属于 B，被拒");
        }

        // ------------------------------------------------------------------ 2c. 无头世界的导航视线（M4-W3）

        private static readonly Rect Wall = new Rect(new Vec2(0.9, -1.0), new Vec2(1.1, 1.0));
        private static readonly Id[] LineOfSightConditions = { BuiltinLineOfSight };

        private static Rig BuildWithNavigation(bool navigationLineOfSight, out StubNavigation2D nav)
        {
            var navigation = new StubNavigation2D();
            nav = navigation;
            return Build(configure: o =>
            {
                o.Navigation = navigation;
                o.NavigationLineOfSight = navigationLineOfSight;
            });
        }

        /// <summary>
        /// 复现用例：目标在射程内，但与施法者之间立着一堵墙（导航静态阻挡）。缺省装配（不开 <c>NavigationLineOfSight</c>）视线恒畅通——隔墙施法成功（与此前逐位一致）；
        /// 打开后视线取自导航阻挡：同一次施法以 LineOfSight 被拒（成功 → 失败），内置视线条件的求值由真变假；墙被移除（动态阻挡）后视线恢复、施法成功。
        /// </summary>
        [Fact]
        public void NavigationLineOfSight_SeesTheWall_AndTheDefaultStaysUnblocked()
        {
            var open = BuildWithNavigation(false, out var openNav);
            openNav.SetBlocking(MapId, new[] { Wall });
            var (openTarget, _) = Spawn(open, new Vec2(2, 0));
            open.Run(2);
            Assert.True(open.World.Spatial.HasLineOfSight(new Vec2(0, 0), new Vec2(2, 0)), "缺省：视线恒畅通");
            Assert.True(CastAt(open, openTarget, Array.Empty<Id>()).Success, "缺省：隔墙施法成功");

            var rig = BuildWithNavigation(true, out var nav);
            nav.SetBlocking(MapId, new[] { Wall });
            var (target, _) = Spawn(rig, new Vec2(2, 0));
            rig.Run(2);
            Assert.False(rig.World.Spatial.HasLineOfSight(rig.PlayerPosition, new Vec2(2, 0)), "打开后：被墙挡住");
            Assert.True(rig.World.Spatial.HasLineOfSight(rig.PlayerPosition, new Vec2(0, 2)), "没有穿墙的连线仍畅通");

            var blocked = CastAt(rig, target, Array.Empty<Id>());
            Assert.False(blocked.Success);
            Assert.Equal(CastFailureReason.LineOfSight, blocked.Reason);
            Assert.False(Evaluator(rig).Evaluate(PlayerId, BuiltinLineOfSight, GraceAim.OfTarget(target, 3)));

            nav.SetBlocking(MapId, Array.Empty<Rect>()); // 墙被拆掉（运行期动态阻挡）
            Assert.True(Evaluator(rig).Evaluate(PlayerId, BuiltinLineOfSight, GraceAim.OfTarget(target, 3)));
            Assert.True(CastAt(rig, target, Array.Empty<Id>()).Success);

            nav.AddBlocking(MapId, Wall); // 增量登记同样立刻生效
            Assert.False(rig.World.Spatial.HasLineOfSight(rig.PlayerPosition, new Vec2(2, 0)));
        }

        /// <summary>
        /// 复现用例（宽限的视线条件在无头下验证真实遮挡）：视线畅通时对 A 施法（内置视线条件为真，瞄点 = A），随后墙立起来挡住视线；窗口内再对 A 施法：
        /// 不带条件的一律以 LineOfSight 被拒，带内置视线条件的被接受（0 → 1）；窗口过后被拒。边界：1 &lt;= 距最近一次为真的 tick 数 &lt;= grace_ticks。
        /// </summary>
        [Fact]
        public void BuiltinLineOfSight_GraceCoversAWallThatJustAppeared_OnlyInsideTheWindow()
        {
            var graceTicks = GraceTicks(Build());
            var covered = 0;
            var notCovered = 0;
            for (var wait = 0; wait <= graceTicks + 3; wait++)
            {
                var rig = BuildWithNavigation(true, out var nav);
                var (target, _) = Spawn(rig, new Vec2(2, 0));
                rig.Feel.Grace!.Register(PlayerId, new[] { BuiltinLineOfSight });
                rig.Run(2);

                Assert.True(CastAt(rig, target, LineOfSightConditions).Success, "视线畅通，首次施法被接受");
                rig.Run(1);
                nav.AddBlocking(MapId, Wall);
                rig.Run(wait);

                var last = rig.Feel.Grace.LastTrueTick(PlayerId, BuiltinLineOfSight);
                var delta = rig.Feel.Grace.CurrentTick - last;
                var inWindow = last >= 0 && delta >= 1 && delta <= graceTicks;

                var plain = CastAt(rig, target, Array.Empty<Id>());
                Assert.False(plain.Success);
                Assert.Equal(CastFailureReason.LineOfSight, plain.Reason);

                var graced = CastAt(rig, target, LineOfSightConditions);
                if (inWindow)
                {
                    Assert.True(graced.Success, $"wait={wait} delta={delta} grace={graceTicks}：窗口内应被接受");
                    covered++;
                }
                else
                {
                    Assert.False(graced.Success, $"wait={wait} delta={delta} grace={graceTicks}：窗口外应被拒");
                    Assert.Equal(CastFailureReason.LineOfSight, graced.Reason);
                    notCovered++;
                }
            }

            Assert.True(covered > 0 && notCovered > 0);
        }

        /// <summary>不变量：打开导航视线却没有提供导航时，装配直接报错（没有阻挡数据可依据，不静默当作畅通）。</summary>
        [Fact]
        public void NavigationLineOfSight_WithoutANavigation_FailsLoudly()
        {
            var ex = Assert.Throws<ArgumentException>(() => Build(configure: o => o.NavigationLineOfSight = true));
            Assert.Contains("Navigation", ex.Message);
        }

        // ------------------------------------------------------------------ 3. 惰性分配

        private const int CreatureCount = 24;

        private static Rig RigWithCreatures(int count, bool withGraceActions, CarriersFeelOptions? options = null)
        {
            var rig = Build(options, withGraceActions);
            for (var i = 0; i < count; i++) Spawn(rig, new Vec2(-2 - (i % 6), -2 - (i / 6)));
            rig.Run(1); // entity.created 在步骤 7 派发，登记发生在下一个 tick 的派发里
            return rig;
        }

        /// <summary>
        /// 复现用例：没有任何动作声明宽限条件时，世界里 N 个生物自动登记。1.95.0 的自动登记给每个单位建一份空缓冲（等价于对每个生物调用 <c>RegisterActor</c>：缓冲分配数 N）；
        /// 现在惰性分配：没有宽限条件可采样就不登记（单位未登记、分配数 N → 0）。M4-W3 起<b>显式</b> <c>RegisterActor</c> 同样惰性：登记只记 id，缓冲等第一次真有边沿才建
        /// （对同样 N 个生物显式登记后，分配数仍是 0，而不是 N）；"显式登记即可采样"的语义不变（已登记、出现在 <c>ActorIds</c>）。
        /// </summary>
        [Fact]
        public void NoDeclaredGraceConditions_AllocatesNoBuffersForTheWorldsUnits()
        {
            var rig = RigWithCreatures(CreatureCount, withGraceActions: false);
            rig.Run(5);
            Assert.Equal(0, rig.Feel.InputBuffer.ActorBuffersAllocated);
            Assert.Empty(rig.Feel.InputBuffer.ActorIds);

            // 对照：1.95.0 的自动登记等价于对每个生物显式登记——当时每个生物一份缓冲；现在显式登记也只记 id。
            var legacy = RigWithCreatures(CreatureCount, withGraceActions: false);
            var creatures = legacy.World.World.QueryEntities(default).Where(e => e.Kind == EntityKinds.Creature).Select(e => e.EntityId).ToList();
            Assert.Equal(CreatureCount, creatures.Count);
            foreach (var id in creatures) legacy.Feel.InputBuffer.RegisterActor(id);
            Assert.Equal(0, legacy.Feel.InputBuffer.ActorBuffersAllocated);
            Assert.Equal(CreatureCount, legacy.Feel.InputBuffer.ActorIds.Count);
            legacy.Run(3); // 已登记但没有缓冲的行动者经过逐 tick 的缓冲维护不出错

            Assert.False(rig.Feel.InputBuffer.IsActorRegistered(creatures[0]));
            Assert.True(legacy.Feel.InputBuffer.IsActorRegistered(creatures[0]));
        }

        /// <summary>
        /// 不变量（需要采样时才登记、有边沿时才建缓冲）：有动作声明宽限条件时，每个单位（生物与玩家）在装配/出生时登记，采样从登记起；
        /// 缓冲对象一个都不建（分配数 0），玩家按下一次键才建自己的一份（0 → 1）；之后出生的生物在出生时登记。
        /// </summary>
        [Fact]
        public void DeclaredGraceConditions_RegisterEveryUnit_AndSamplingStartsAtRegistration_ButBuffersWaitForAnEdge()
        {
            var rig = RigWithCreatures(CreatureCount, withGraceActions: true);
            var eligible = rig.World.World.QueryEntities(default)
                .Count(e => e.Kind == EntityKinds.Creature || e.Kind == EntityKinds.Player);
            Assert.Equal(CreatureCount + 1, eligible);
            Assert.Equal(eligible, rig.Feel.InputBuffer.ActorIds.Count);
            Assert.Equal(0, rig.Feel.InputBuffer.ActorBuffersAllocated);

            rig.Run(3);
            var tracker = rig.Feel.Grace!;
            foreach (var entity in rig.World.World.QueryEntities(default).Where(e => e.Kind == EntityKinds.Creature))
            {
                Assert.Equal(tracker.CurrentTick, tracker.LastTrueTick(entity.EntityId, Always));
            }

            Assert.Equal(0, rig.Feel.InputBuffer.ActorBuffersAllocated);
            rig.Tap("l"); // 玩家第一次有边沿：建自己的缓冲
            rig.Run(2);
            Assert.Equal(1, rig.Feel.InputBuffer.ActorBuffersAllocated);

            var late = Spawn(rig, new Vec2(-9, -9)).Id;
            rig.Run(1);
            Assert.True(rig.Feel.InputBuffer.IsActorRegistered(late));
            Assert.Equal(1, rig.Feel.InputBuffer.ActorBuffersAllocated);
        }

        /// <summary>
        /// 不变量（条件声明出现的那一刻补登记）：开始没有宽限条件（分配 0），之后游戏经 <c>DeclareActions</c> 声明了带宽限条件的动作——
        /// 已有单位此刻才登记（已登记 0 → N+1，缓冲分配数仍为 0）并开始采样；此前出生的生物没有被漏掉；之后出生的生物出生时即登记。
        /// </summary>
        [Fact]
        public void GraceConditionsDeclaredLater_RegisterTheExistingUnitsThen()
        {
            var rig = RigWithCreatures(CreatureCount, withGraceActions: false);
            rig.Run(3);
            Assert.Equal(0, rig.Feel.InputBuffer.ActorBuffersAllocated);
            var creatures = rig.World.World.QueryEntities(default).Where(e => e.Kind == EntityKinds.Creature).Select(e => e.EntityId).ToList();
            var unlogged = Spawn(rig, new Vec2(-8, -8)).Id; // 没有条件时出生：不登记
            rig.Run(1);
            Assert.False(rig.Feel.InputBuffer.IsActorRegistered(unlogged));

            var withCondition = new ActionDefinition(
                new Id("input.action.m4g_late"), ActionKind.Button, new[] { "key:u" }, "default", null,
                ActionClass.Attack, null, null, null, InputRepeatPolicy.Refresh, null, new[] { Always }, "slot_m4g");
            rig.Feel.InputBuffer.DeclareActions(new[] { withCondition });
            Assert.Contains(Always, rig.Feel.InputBuffer.GraceConditionNames);

            var expected = creatures.Count + 1 + 1; // 24 个生物 + 之后出生的 1 个 + 玩家
            Assert.Equal(expected, rig.Feel.InputBuffer.ActorIds.Count);
            Assert.Equal(0, rig.Feel.InputBuffer.ActorBuffersAllocated); // 登记只记 id，缓冲等边沿
            Assert.True(rig.Feel.InputBuffer.IsActorRegistered(unlogged));
            rig.Run(2);
            Assert.Equal(rig.Feel.Grace!.CurrentTick, rig.Feel.Grace.LastTrueTick(creatures[0], Always));

            var late = Spawn(rig, new Vec2(-9, -8)).Id; // 条件已声明：出生时登记
            rig.Run(1);
            Assert.True(rig.Feel.InputBuffer.IsActorRegistered(late));
        }

        /// <summary>不变量：<c>AutoRegisterGraceActors = false</c> 时无论有无宽限条件都不自动建缓冲（行为与此前一致），显式 <c>RegisterActor</c> 仍然有效。</summary>
        [Fact]
        public void AutoRegistrationOff_NeverAllocates_ExplicitRegistrationStillWorks()
        {
            var rig = RigWithCreatures(4, withGraceActions: true, new CarriersFeelOptions { AutoRegisterGraceActors = false });
            rig.Run(2);
            Assert.Equal(0, rig.Feel.InputBuffer.ActorBuffersAllocated);

            var creature = rig.World.World.QueryEntities(default).First(e => e.Kind == EntityKinds.Creature).EntityId;
            rig.Feel.InputBuffer.RegisterActor(creature);
            Assert.True(rig.Feel.InputBuffer.IsActorRegistered(creature));
            Assert.Equal(0, rig.Feel.InputBuffer.ActorBuffersAllocated); // 显式登记也惰性：只记 id，不建缓冲
            rig.Run(2);
            Assert.Equal(rig.Feel.Grace!.CurrentTick, rig.Feel.Grace.LastTrueTick(creature, Always));
        }

        // ------------------------------------------------------------------ 上下文变量本身

        /// <summary>
        /// 瞄点上下文的字段：由位置与射程算出；射程 0 = 没有射程限制（有瞄点即在射程内）；没有瞄点时距离为大数、其余为假；视线与射程查询惰性（没有引用就不查询）。
        /// </summary>
        [Fact]
        public void TheAimContext_ComputesFromPositionsAndRange_AndQueriesLazily()
        {
            var losCalls = 0;
            var rangeCalls = 0;
            var context = new GraceAimContext(
                new Vec2(0, 0), new Vec2(4, 0), isGround: true, () => { rangeCalls++; return 3; }, (from, to) => { losCalls++; return true; });

            Assert.Equal(0, losCalls);
            Assert.Equal(0, rangeCalls);
            Assert.True(context.TryGetField("has_aim", out var hasAim) && hasAim.AsBool);
            Assert.True(context.TryGetField("aimDistance", out var distance) && distance.AsNumber == 4);
            Assert.Equal(0, losCalls); // 只读了位置相关字段，不触发视线与射程
            Assert.True(context.TryGetField("aim_in_range", out var inRange) && !inRange.AsBool); // 距离 4 > 射程 3
            Assert.Equal(1, rangeCalls);
            Assert.True(context.TryGetField("aim_is_ground", out var ground) && ground.AsBool);
            Assert.True(context.TryGetField("aim_line_of_sight", out var los) && los.AsBool);
            Assert.Equal(1, losCalls);
            Assert.False(context.TryGetField("not_a_field", out _));

            var unlimited = new GraceAimContext(new Vec2(0, 0), new Vec2(40, 0), isGround: false, () => 0, null);
            Assert.True(unlimited.AimInRange); // 射程 0：没有射程限制
            Assert.True(unlimited.AimLineOfSight); // 没有视线查询：视为畅通

            var none = new GraceAimContext(new Vec2(0, 0), null, isGround: false, () => 3, null);
            Assert.False(none.HasAim);
            Assert.Equal(GraceAimContext.NoAimDistance, none.AimDistance);
            Assert.False(none.AimInRange);
            Assert.False(none.AimLineOfSight);
        }
    }
}
