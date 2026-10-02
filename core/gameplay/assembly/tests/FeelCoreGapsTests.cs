// FeelCoreGapsTests：手感落地 M2-B——手感核心侧缺口经生产装配（HeadlessWorldBuilder → GameplayAssembly → Carriers/Rules）后的运行时冒烟。
// 同一个装配好的世界、真实输入映射（StubInput → InputMapHost → 输入缓冲）、真实施法管线；期望值全部由档案毫秒、标定 tick 率与数据规则算出，不写死裸数。
// 覆盖：宽限窗口被施法管线消费（01 第 2.4 节）、数据热加载接线（05 第 8 节）、武器 phase_scale 作用于时间线各相（05 第 3.4/8 节）、
// 光环 feel_modifiers 按实例层数叠加（05 第 6 节）。
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
    public sealed class FeelCoreGapsTests
    {
        private const double Dt = 0.02;
        private const string FrameworkCalibration = "feel.calibration.framework_default";

        private static readonly Id MapId = new Id("world.lab_arena");
        private static readonly Id PlayerId = new Id("unit.lab_player");
        private static readonly Id StakeTemplate = new Id("creature.lab_stake");
        private static readonly Id SlashSkill = new Id("skill.m2b_slash");
        private static readonly Id SwordSkill = new Id("skill.lab_equip.attack_sword_1h");
        private static readonly Id InRangeCondition = new Id("input.grace.m2b_in_range");
        private static readonly Id MainHandSlot = new Id("item.slot.m2b_main_hand");
        private static readonly Id HasteAura = new Id("skill.aura_def.m2b_feel_stack");

        private static int Ticks(double ms) => FeelCalibration.MillisecondsToTicks(ms, Dt);

        // ------------------------------------------------------------------ 数据

        // 瞬发、射程 3 的非时间线技能；目标链是实验室圆形（半径 30，找得到远处的目标），所以目标离开射程后由步骤 7 以 OUT_OF_RANGE 拒绝。
        private const string SlashSkillJson = @"{
  ""table"": ""skill.def"", ""schema_version"": 1,
  ""rows"": [ {
    ""id"": ""skill.m2b_slash"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 3, ""cast_time"": 0,
    ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_nearest_enemy"",
    ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 10, ""coefficient"": 0, ""school"": ""school.physical"" } } ]
  } ]
}";

        private const string GraceConditionJson = @"{
  ""table"": ""found.grace_condition"", ""schema_version"": 1,
  ""rows"": [ { ""key"": ""input.grace.m2b_in_range"", ""expr"": ""true"", ""description"": ""target in range"" } ]
}";

        // 攻击动作：m2b_attack_grace 声明宽限条件，m2b_attack_plain 不声明（对照）；都映射到槽位 slot_m2b。
        private const string ActionJson = @"{
  ""table"": ""found.input_action"", ""schema_version"": 1,
  ""rows"": [
    { ""key"": ""input.action.m2b_attack_grace"", ""kind"": ""button"", ""default_bindings"": [ ""key:j"" ], ""rebind_group"": ""default"",
      ""class"": ""attack"", ""buffer_ms"": 160, ""skill_slot"": ""slot_m2b"", ""grace_conditions"": [ ""input.grace.m2b_in_range"" ] },
    { ""key"": ""input.action.m2b_attack_plain"", ""kind"": ""button"", ""default_bindings"": [ ""key:k"" ], ""rebind_group"": ""default"",
      ""class"": ""attack"", ""buffer_ms"": 160, ""skill_slot"": ""slot_m2b"" }
  ]
}";

        // 两把武器：quick 带三相倍率（前摇 ×0.5、有效 ×2、后摇 ×1.5），plain 不写倍率（对照）；普攻时间线都指向实验室单手剑普攻。
        private const double QuickStartup = 0.5;
        private const double QuickActive = 2.0;
        private const double QuickRecovery = 1.5;

        private const string WeaponJson = @"{
  ""table"": ""feel.weapon"", ""schema_version"": 1,
  ""rows"": [
    { ""id"": ""feel.weapon.m2b_quick"", ""maturity"": ""experimental"", ""profile_version"": 1, ""family"": ""1h"",
      ""auto_attack_timeline_ref"": ""skill.lab_equip.attack_sword_1h"",
      ""writes"": [
        { ""field"": ""impact_class"", ""op"": ""set"", ""value"": ""light"" },
        { ""field"": ""phase_scale.startup"", ""op"": ""multiply"", ""value"": 0.5 },
        { ""field"": ""phase_scale.active"", ""op"": ""multiply"", ""value"": 2.0 },
        { ""field"": ""phase_scale.recovery"", ""op"": ""multiply"", ""value"": 1.5 } ] },
    { ""id"": ""feel.weapon.m2b_plain"", ""maturity"": ""experimental"", ""profile_version"": 1, ""family"": ""1h"",
      ""auto_attack_timeline_ref"": ""skill.lab_equip.attack_sword_1h"",
      ""writes"": [ { ""field"": ""impact_class"", ""op"": ""set"", ""value"": ""light"" } ] }
  ]
}";

        private const string ItemBudgetJson = @"{ ""table"": ""item.budget_curve"", ""schema_version"": 2, ""rows"": [
  { ""id"": ""item.budget.default"", ""entries"": [ { ""x"": 1, ""y"": 20 }, { ""x"": 10, ""y"": 200 } ], ""exponent"": 1.5 } ] }";

        private const string ItemQualityJson = @"{ ""table"": ""item.quality_definition"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""item.quality.m2b_common"", ""name_key"": ""l10n.item.quality.m2b_common.name"", ""sort_weight"": 1, ""budget_multiplier"": 1.0,
    ""affix_count"": 0, ""grant_budget_share"": 0.0, ""price_multiplier"": 1.0 } ] }";

        private const string ItemSlotJson = @"{ ""table"": ""item.slot_definition"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""item.slot.m2b_main_hand"", ""name_key"": ""l10n.item.slot.m2b_main_hand.name"", ""sort_weight"": 1, ""is_weapon"": true, ""is_equipment"": true,
    ""budget_coefficient"": 1.0, ""price_coefficient"": 1.0, ""has_armor"": false } ] }";

        private const string ItemTemplateJson = @"{ ""table"": ""item.template"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""item.m2b_quick"", ""slot"": ""item.slot.m2b_main_hand"", ""quality"": ""item.quality.m2b_common"", ""item_level"": 1,
    ""weapon_profile"": { ""damage_min"": 5, ""damage_max"": 8, ""speed"": 1.5 }, ""stack_size"": 1, ""name_key"": ""l10n.item.m2b_quick.name"", ""display_ref"": ""display.map.m2b_quick"",
    ""feel_weapon_ref"": ""feel.weapon.m2b_quick"" },
  { ""id"": ""item.m2b_plain"", ""slot"": ""item.slot.m2b_main_hand"", ""quality"": ""item.quality.m2b_common"", ""item_level"": 1,
    ""weapon_profile"": { ""damage_min"": 5, ""damage_max"": 8, ""speed"": 1.5 }, ""stack_size"": 1, ""name_key"": ""l10n.item.m2b_plain.name"", ""display_ref"": ""display.map.m2b_plain"",
    ""feel_weapon_ref"": ""feel.weapon.m2b_plain"" } ] }";

        private const string DisplayJson = @"{ ""table"": ""display.map"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""display.map.m2b_quick"", ""category"": ""item"", ""logical_id"": ""item.m2b_quick"", ""kind"": ""sprite"", ""sprite_set_id"": ""sprite.lab_placeholder"", ""direction_count"": 4 },
  { ""id"": ""display.map.m2b_plain"", ""category"": ""item"", ""logical_id"": ""item.m2b_plain"", ""kind"": ""sprite"", ""sprite_set_id"": ""sprite.lab_placeholder"", ""direction_count"": 4 } ] }";

        private const string TextJson = @"{ ""table"": ""l10n.text"", ""schema_version"": 1, ""rows"": [
  { ""key"": ""l10n.item.quality.m2b_common.name"", ""locale"": ""l10n.locale.zh_cn"", ""text"": ""common"" },
  { ""key"": ""l10n.item.slot.m2b_main_hand.name"", ""locale"": ""l10n.locale.zh_cn"", ""text"": ""main hand"" },
  { ""key"": ""l10n.item.m2b_quick.name"", ""locale"": ""l10n.locale.zh_cn"", ""text"": ""quick"" },
  { ""key"": ""l10n.item.m2b_plain.name"", ""locale"": ""l10n.locale.zh_cn"", ""text"": ""plain"" } ] }";

        // 可叠 3 层的光环：accel_ms 乘 0.8、decel_ms 加 25（均为判定型、非属性已承载字段）。
        private const double AuraAccelMultiplier = 0.8;
        private const double AuraDecelAdd = 25;

        private const string AuraJson = @"{
  ""table"": ""skill.aura_def"", ""schema_version"": 1,
  ""rows"": [ {
    ""id"": ""skill.aura_def.m2b_feel_stack"", ""duration"": 60, ""max_stacks"": 3,
    ""effects"": [ { ""kind"": ""mod_stat"", ""params"": { ""stat"": ""stat.attack_power"", ""op"": ""flat"", ""value"": 1 } } ],
    ""feel_modifiers"": [
      { ""field"": ""accel_ms"", ""op"": ""multiply"", ""value"": 0.8 },
      { ""field"": ""decel_ms"", ""op"": ""add"", ""value"": 25 } ]
  } ]
}";

        // ------------------------------------------------------------------ 装配

        private sealed class DistanceEvaluator : IGraceConditionEvaluator
        {
            public Func<Id, Vec2>? Position;
            public Id? Target;
            public double Radius = 3.0;

            public bool Evaluate(Id actorId, Id conditionId)
            {
                if (!Target.HasValue || Position == null) return false;
                return Vec2.Distance(Position(actorId), Position(Target.Value)) <= Radius;
            }
        }

        private sealed class Rig
        {
            public HeadlessWorld World = null!;
            public StubFileSystem Fs = null!;
            public StubInput Input = new StubInput();
            public InputMapHost Map = null!;
            public DistanceEvaluator Evaluator = null!;
            public int Tick;
            public readonly List<(int Tick, IEvent Event)> Log = new List<(int, IEvent)>();
            private int _cursor;

            public Unit Player => World.Player;
            public CarriersFeelSystem Feel => World.Gameplay.Feel!;
            public IFeelResolver Resolver => Feel.Resolver;
            public JudgingFeelView Judging => Resolver.ResolveJudging(PlayerId);

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

            /// <summary>改数据文件文本后按宿主的开发期惯例热加载：DataRegistry.Reload(表) 之后补发 data.load_completed。</summary>
            public ValidationReport ReloadTable(string table, string path, Func<string, string> edit)
            {
                var text = Fs.ReadText(path) ?? throw new InvalidOperationException(path);
                var edited = edit(text);
                Assert.NotEqual(text, edited);
                Fs.WriteTextAtomic(path, edited);
                var report = World.Registry.Reload(table);
                World.Bus.PublishImmediate(new DataLoadCompletedEvent(World.Registry.Tables.Count, 0, report.ErrorCount, report.WarningCount));
                Step(); // 让事件落进日志
                return report;
            }

            public IEnumerable<(int Tick, T Event)> Of<T>() where T : IEvent =>
                Log.Where(l => l.Event is T).Select(l => (l.Tick, (T)l.Event));

            /// <summary>点按攻击并跑到本次动作结束；返回（起手 tick 起算）各相位切换的 tick 偏移与总时长。</summary>
            public (int Duration, int ActiveAt, int RecoveryAt, int FinishedAt) Attack(string key)
            {
                var from = Log.Count;
                Tap(key);
                for (var guard = 0; guard < 400; guard++)
                {
                    if (Log.Skip(from).Any(l => l.Event is ActionFinishedEvent)) break;
                    Step();
                }

                var slice = Log.Skip(from).ToList();
                var started = slice.Single(l => l.Event is ActionStartedEvent);
                var duration = ((ActionStartedEvent)started.Event).DurationTicks;
                int At(ActionPhase phase) => slice.First(l => l.Event is ActionPhaseChangedEvent p && p.Phase == phase).Tick - started.Tick;
                var finished = slice.Single(l => l.Event is ActionFinishedEvent).Tick - started.Tick;
                Run(2);
                return (duration, At(ActionPhase.Active), At(ActionPhase.Recovery), finished);
            }
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

        private static Rig Build()
        {
            var fs = new StubFileSystem();
            var repoRoot = FindRepoRoot();
            CopyDisk(fs, repoRoot, "data/_framework");
            CopyDisk(fs, repoRoot, "data/_feel");
            CopyDisk(fs, repoRoot, "data/_lab");
            fs.WriteTextAtomic("test/_m2b/skill/skill.def.json", SlashSkillJson);
            fs.WriteTextAtomic("test/_m2b/skill/skill.aura_def.json", AuraJson);
            fs.WriteTextAtomic("test/_m2b/found/found.grace_condition.json", GraceConditionJson);
            fs.WriteTextAtomic("test/_m2b/found/found.input_action.json", ActionJson);
            fs.WriteTextAtomic("test/_m2b/feel/feel.weapon.json", WeaponJson);
            fs.WriteTextAtomic("test/_m2b/item/item.budget_curve.json", ItemBudgetJson);
            fs.WriteTextAtomic("test/_m2b/item/item.quality_definition.json", ItemQualityJson);
            fs.WriteTextAtomic("test/_m2b/item/item.slot_definition.json", ItemSlotJson);
            fs.WriteTextAtomic("test/_m2b/item/item.template.json", ItemTemplateJson);
            fs.WriteTextAtomic("test/_m2b/display/display.map.json", DisplayJson);
            fs.WriteTextAtomic("test/_m2b/l10n/l10n.text.json", TextJson);

            var evaluator = new DistanceEvaluator();
            var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = new List<IDataSource>
                {
                    new FileSystemDataSource(fs, "data/_framework"),
                    new FileSystemDataSource(fs, "data/_feel"),
                    new FileSystemDataSource(fs, "data/_lab"),
                    new FileSystemDataSource(fs, "test/_m2b"),
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
                FeelOptions = new CarriersFeelOptions { CalibrationId = FrameworkCalibration, GraceEvaluator = evaluator },
            });
            Assert.False(world.LoadReport.IsBlocking, string.Join("\n", world.LoadReport.Issues));

            var rig = new Rig { World = world, Fs = fs, Evaluator = evaluator };
            rig.Map = new InputMapHost(world.Bus);
            var definitions = new List<ActionDefinition>();
            foreach (var record in world.Registry.GetAll("found.input_action")) definitions.Add(ActionDefinition.FromRecord(record));
            rig.Map.DeclareActionSet(new Id("actionset.m2b"), definitions);
            world.Gameplay.Feel!.InputBuffer.BindLocalInput(rig.Map, PlayerId, null);

            var skills = world.Gameplay.Carriers.Rules.Skill;
            skills.LearnSkill(PlayerId, SlashSkill);
            skills.LearnSkill(PlayerId, SwordSkill);
            Assert.True(world.Gameplay.Carriers.SkillBindings.Bind(PlayerId, "slot_m2b", SlashSkill));
            evaluator.Position = id => world.World.GetEntity(id)!.Position;
            return rig;
        }

        private static (Rig Rig, Id Target, Unit TargetUnit) GraceRigWithTarget()
        {
            var rig = Build();
            var target = rig.World.Gameplay.Carriers.Creatures.Spawn(StakeTemplate, MapId, new Vec2(1.5, 0), Math.PI, null, 1);
            rig.World.Spatial.Register(target, new Vec2(1.5, 0), 0.5);
            var ai = rig.World.Gameplay.Carriers.Rules.Ai;
            foreach (var registered in new List<Id>(ai.RegisteredUnitIds))
            {
                if (registered.Equals(target)) { ai.UnregisterUnit(target); break; }
            }

            rig.Evaluator.Target = target;
            return (rig, target, (Unit)rig.World.World.GetEntity(target)!);
        }

        private static void MoveTargetOut(Rig rig, Id target, Unit unit)
        {
            unit.Position = new Vec2(10, 0);
            rig.World.Spatial.UpdatePosition(target, unit.Position);
        }

        private static void BindSwordAsAttack(Rig rig) =>
            Assert.True(rig.World.Gameplay.Carriers.SkillBindings.Bind(PlayerId, "slot_m2b", SwordSkill));

        private static void Equip(Rig rig, string itemTemplate)
        {
            var carriers = rig.World.Gameplay.Carriers;
            Assert.True(carriers.Inventory.AddItem(PlayerId, new Id(itemTemplate), 1));
            Id? instance = null;
            foreach (var item in carriers.Inventory.ListItems(PlayerId))
            {
                if (string.Equals(item.TemplateId.Value, itemTemplate, StringComparison.Ordinal)) instance = item.InstanceId;
            }

            Assert.True(instance.HasValue);
            Assert.True(carriers.Equipment.Equip(PlayerId, instance.Value, MainHandSlot).Success);
            rig.Run(1); // 装备事件在固定步末尾派发：换装链对账与解析器失效在这一 tick 末尾完成。
        }

        private static double SwordMs(Rig rig, string name)
        {
            var def = rig.World.Registry.Get("skill.def", SwordSkill)!;
            Assert.True(def.TryGetObject("timeline", out var timeline));
            for (var i = 0; i < timeline.Count; i++)
            {
                if (string.Equals(timeline[i].Key, name, StringComparison.Ordinal)
                    && timeline[i].Value is Core.Foundation.Common.Json.JsonNumber n) return n.Value;
            }

            throw new InvalidOperationException(name);
        }

        // ------------------------------------------------------------------ 1. 宽限窗口被施法管线消费

        /// <summary>
        /// 复现用例：目标在射程内 → 离开射程 → 之后 k 个 tick 点按攻击。无宽限条件的动作一律以 OUT_OF_RANGE 被拒（施放 0 次）；
        /// 声明了宽限条件的动作在"最近一次条件为真"之后 <c>grace_ms</c> 换算的 tick 数以内被接受（施放 0 → 1），之后与无宽限时一样被拒。边界由规则算出。
        /// </summary>
        [Fact]
        public void Grace_ActionWithGraceCondition_IsAcceptedWithinGraceWindowAfterTargetLeavesRange_ThenRejectedAsOutOfRange()
        {
            var probe = GraceRigWithTarget();
            var graceTicks = Ticks(probe.Rig.Judging.GetNumber(FeelFieldNames.GraceMs));
            Assert.True(graceTicks >= 2, "标定下 grace_ms 应换算出至少 2 个 tick，用例才有意义");

            for (var wait = 0; wait <= graceTicks + 3; wait++)
            {
                var (rig, target, unit) = GraceRigWithTarget();
                rig.Run(3); // 目标在射程内，宽限追踪记下"最近一次为真"
                MoveTargetOut(rig, target, unit);
                rig.Run(wait);

                var pressAt = rig.Tick;
                rig.Tap("j");
                rig.Run(3);

                var lastTrue = rig.Feel.Grace!.LastTrueTick(PlayerId, InRangeCondition);
                Assert.True(lastTrue >= 0);
                // 追踪的"当前 tick"与测试的 tick 序号同一基准：按下 tick 的步骤 1 先采样再取用，步骤 3 施法。
                var delta = pressAt - lastTrue;
                var shouldAccept = delta <= graceTicks;
                var casts = rig.Of<SkillCastSuccessEvent>().Count();
                var failures = rig.Of<SkillCastFailedEvent>().Select(f => f.Event.ReasonCode).ToList();
                if (shouldAccept)
                {
                    Assert.True(casts == 1, $"wait={wait} delta={delta} grace={graceTicks}：宽限内应被接受");
                    Assert.Empty(failures);
                }
                else
                {
                    Assert.True(casts == 0, $"wait={wait} delta={delta} grace={graceTicks}：宽限外应被拒绝");
                    Assert.Contains(CastFailureReason.OutOfRange, failures);
                }
            }
        }

        [Fact]
        public void Grace_ActionWithoutGraceCondition_IsRejectedAsOutOfRangeEvenRightAfterTheTargetLeaves()
        {
            var (rig, target, unit) = GraceRigWithTarget();
            rig.Run(3);
            MoveTargetOut(rig, target, unit);
            rig.Run(1);

            rig.Tap("k"); // 不声明宽限条件的对照动作
            rig.Run(3);

            Assert.Empty(rig.Of<SkillCastSuccessEvent>());
            Assert.Contains(CastFailureReason.OutOfRange, rig.Of<SkillCastFailedEvent>().Select(f => f.Event.ReasonCode));
        }

        [Fact]
        public void Grace_DoesNotChangeWhatTheAcceptedCastResolves_EffectsStillApplyToTheResolvedTarget()
        {
            // 不变量：宽限只放宽"接受"，不改变随后的结算——被接受的施法仍按目标选择链结算到同一个目标（非时间线技能不另做几何判定）。
            var (rig, target, unit) = GraceRigWithTarget();
            rig.Run(3);
            MoveTargetOut(rig, target, unit);
            rig.Run(1);

            rig.Tap("j");
            rig.Run(3);

            var success = rig.Of<SkillCastSuccessEvent>().Single().Event;
            Assert.Equal(new[] { target }, success.Targets.ToArray());
        }

        [Fact]
        public void Grace_WhenTargetStaysInRange_PlainAndGraceActionsBehaveIdentically()
        {
            // 不变量：条件一直为真时宽限不参与（IsInGrace 恒假），两个动作的施放结果一致。
            var (rig, _, _) = GraceRigWithTarget();
            rig.Run(2);
            rig.Tap("j");
            rig.Run(4);
            rig.Tap("k");
            rig.Run(4);

            Assert.Equal(2, rig.Of<SkillCastSuccessEvent>().Count());
            Assert.Empty(rig.Of<SkillCastFailedEvent>());
        }

        // ------------------------------------------------------------------ 2. 数据热加载

        private const string PresetPath = "data/_feel/feel/feel.preset.json";

        /// <summary>
        /// 复现用例：改 <c>feel.preset</c> 里的 accel_ms，经"DataRegistry.Reload + data.load_completed"后，运行中单位的 accel_ms 从旧档案值变为新档案值，
        /// 解析器版本号递增；进行中的动作手感快照不变，下一次动作才看到新值。
        /// </summary>
        [Fact]
        public void HotReload_PresetEdit_IsPickedUpByTheLiveResolver_AndBumpsTheVersion()
        {
            var rig = Build();
            var before = rig.Judging.GetNumber(FeelFieldNames.AccelMs);
            var versionBefore = rig.Resolver.GetVersion(PlayerId);
            var edited = before + 111;

            rig.ReloadTable("feel.preset", PresetPath, t => t.Replace("\"accel_ms\": " + (long)before + ",", "\"accel_ms\": " + (long)edited + ","));

            Assert.NotNull(rig.Feel.LastHotReload);
            Assert.True(rig.Feel.LastHotReload!.Applied, rig.Feel.LastHotReload.Reason);
            Assert.Equal(edited, rig.Judging.GetNumber(FeelFieldNames.AccelMs));
            Assert.True(rig.Resolver.GetVersion(PlayerId) > versionBefore);
            Assert.Equal(1, rig.Feel.LastHotReload.Generation);
        }

        [Fact]
        public void HotReload_InFlightActionKeepsItsSnapshot_NextActionSeesTheNewData()
        {
            var rig = Build();
            BindSwordAsAttack(rig);
            var startupMs = SwordMs(rig, "startup_ms");
            var oldStartup = Ticks(startupMs);
            var newStartup = Ticks(startupMs * 2);
            Assert.NotEqual(oldStartup, newStartup);

            rig.Tap("j");
            rig.Run(1); // 动作已起手
            rig.ReloadTable("feel.preset", PresetPath, t => t.Replace("\"phase_scale.startup\": 1,", "\"phase_scale.startup\": 2,"));
            rig.Run(120);
            var first = rig.Of<ActionPhaseChangedEvent>().First(e => e.Event.Phase == ActionPhase.Active).Tick
                        - rig.Of<ActionStartedEvent>().First().Tick;
            Assert.Equal(oldStartup, first); // 进行中的动作按起手时的快照走完

            var second = rig.Attack("j");
            Assert.Equal(newStartup, second.ActiveAt); // 下一次动作按新数据
        }

        /// <summary>
        /// 不变量：新数据校验不过（缺必填字段）时被拒绝，当前档案原样保留、版本号不动、原因写在 LastHotReload 上；之后改好再热加载恢复生效。
        /// </summary>
        [Fact]
        public void HotReload_InvalidData_IsRejectedAndKeepsTheCurrentProfiles_ThenValidDataRecovers()
        {
            var rig = Build();
            var accel = rig.Judging.GetNumber(FeelFieldNames.AccelMs);
            var versionBefore = rig.Resolver.GetVersion(PlayerId);

            var original = rig.Fs.ReadText(PresetPath)!;
            rig.ReloadTable("feel.preset", PresetPath, t => t.Replace("\"buffer_ms\": 120,", string.Empty));

            Assert.False(rig.Feel.LastHotReload!.Applied);
            Assert.NotEmpty(rig.Feel.LastHotReload.Issues);
            Assert.Equal(0, rig.Feel.LastHotReload.Generation);
            Assert.Equal(accel, rig.Judging.GetNumber(FeelFieldNames.AccelMs));
            Assert.Equal(versionBefore, rig.Resolver.GetVersion(PlayerId));

            // 改回合法数据（同时改 accel_ms）：恢复生效。
            rig.ReloadTable("feel.preset", PresetPath, _ => original.Replace("\"accel_ms\": " + (long)accel + ",", "\"accel_ms\": " + ((long)accel + 7) + ","));
            Assert.True(rig.Feel.LastHotReload!.Applied, rig.Feel.LastHotReload.Reason);
            Assert.Equal(accel + 7, rig.Judging.GetNumber(FeelFieldNames.AccelMs));
        }

        /// <summary>
        /// 复现用例：已装备武器的 family 从 1h 改成 2h 后热加载：武器族 1h 变为 2h，换装链重新对账并发布 feel.weapon_changed（上一武器族 1h、当前 2h）；
        /// 武器目录同时换新，普攻绑定读到新行。
        /// </summary>
        [Fact]
        public void HotReload_WeaponFamilyEdit_ReconcilesTheEquipmentChainAndRepublishesTheWeaponChanged()
        {
            var rig = Build();
            Equip(rig, "item.m2b_plain");
            var before = rig.Of<FeelWeaponChangedEvent>().Last().Event;
            Assert.Equal("1h", before.Family);

            const string path = "test/_m2b/feel/feel.weapon.json";
            rig.ReloadTable("feel.weapon", path, t => t.Replace("\"family\": \"1h\"", "\"family\": \"2h\""));

            Assert.True(rig.Feel.LastHotReload!.Applied, rig.Feel.LastHotReload.Reason);
            var after = rig.Of<FeelWeaponChangedEvent>().Last().Event;
            Assert.Equal(("1h", "2h"), (after.PreviousFamily, after.Family));
            Assert.Equal(PlayerId, after.UnitId);
        }

        [Fact]
        public void HotReload_WithNoDataChange_ReappliesTheSameValues_AndPublishesNoWeaponChange()
        {
            // 不变量：内容不变的热加载不改变任何解析结果，也不重复发布武器变化事件（对账只看差异）。
            var rig = Build();
            Equip(rig, "item.m2b_quick");
            var events = rig.Of<FeelWeaponChangedEvent>().Count();
            var startup = rig.Judging.GetNumber(FeelFieldNames.PhaseScaleStartup);

            var report = rig.World.Registry.Reload("feel.preset");
            rig.World.Bus.PublishImmediate(new DataLoadCompletedEvent(rig.World.Registry.Tables.Count, 0, report.ErrorCount, report.WarningCount));

            rig.Run(1);
            Assert.True(rig.Feel.LastHotReload!.Applied);
            Assert.Equal(events, rig.Of<FeelWeaponChangedEvent>().Count());
            Assert.Equal(startup, rig.Judging.GetNumber(FeelFieldNames.PhaseScaleStartup));
        }

        // ------------------------------------------------------------------ 5. 武器 phase_scale

        /// <summary>
        /// 复现用例：同一条单手剑普攻时间线，空手（槽位绑定）→ 装备带三相倍率的武器 → 卸下。三相 tick 数从"毫秒 × 1"变为"毫秒 × 武器倍率"再变回；
        /// 期望值 = 时间线毫秒 × （基础预设倍率 × 武器 multiply）按标定步长换算，倍率来源是数据里的武器行常量，不读解析结果。
        /// </summary>
        [Fact]
        public void WeaponPhaseScale_ScalesTheTimelinePhases_ThenWithdrawsOnUnequip()
        {
            var rig = Build();
            BindSwordAsAttack(rig);
            var baseScale = rig.Judging.GetNumber(FeelFieldNames.PhaseScaleStartup); // 基础预设的相位倍率（空手）
            Assert.Equal(1.0, baseScale);

            (int Startup, int Active, int Recovery) Expected(double s, double a, double r) => (
                Ticks(SwordMs(rig, "startup_ms") * s), Ticks(SwordMs(rig, "active_ms") * a), Ticks(SwordMs(rig, "recovery_ms") * r));

            var unarmed = rig.Attack("j");
            var plain = Expected(baseScale, baseScale, baseScale);
            Assert.Equal((plain.Startup, plain.Startup + plain.Active, plain.Startup + plain.Active + plain.Recovery),
                (unarmed.ActiveAt, unarmed.RecoveryAt, unarmed.FinishedAt));

            Equip(rig, "item.m2b_quick");
            var quick = rig.Attack("j");
            var scaled = Expected(baseScale * QuickStartup, baseScale * QuickActive, baseScale * QuickRecovery);
            Assert.Equal((scaled.Startup, scaled.Startup + scaled.Active, scaled.Startup + scaled.Active + scaled.Recovery),
                (quick.ActiveAt, quick.RecoveryAt, quick.FinishedAt));
            Assert.NotEqual(unarmed.Duration, quick.Duration);
            Assert.Equal(quick.FinishedAt, quick.Duration);

            // 不变量：不写倍率的武器与空手逐 tick 一致（占位武器数据不带 phase_scale，既有基线不变）。
            Equip(rig, "item.m2b_plain");
            var plainWeapon = rig.Attack("j");
            Assert.Equal((unarmed.ActiveAt, unarmed.RecoveryAt, unarmed.FinishedAt),
                (plainWeapon.ActiveAt, plainWeapon.RecoveryAt, plainWeapon.FinishedAt));
        }

        [Fact]
        public void WeaponPhaseScale_ProvenanceNamesTheWeaponLayer_AndOnlyThePhasesTheWeaponWritesChange()
        {
            var rig = Build();
            Equip(rig, "item.m2b_quick");

            var judging = rig.Judging;
            Assert.Equal(QuickStartup, judging.GetNumber(FeelFieldNames.PhaseScaleStartup));
            Assert.Equal(QuickActive, judging.GetNumber(FeelFieldNames.PhaseScaleActive));
            Assert.Equal(QuickRecovery, judging.GetNumber(FeelFieldNames.PhaseScaleRecovery));

            var trail = rig.Resolver.GetProvenance(PlayerId, FeelFieldNames.PhaseScaleStartup);
            Assert.Contains(trail, e => e.Layer == (int)FeelLayer.Weapon && e.SourceId == "feel.weapon.m2b_quick" && e.Op == FeelProvenanceOps.Multiply);
            Assert.Equal(judging.GetNumber(FeelFieldNames.PhaseScaleStartup), trail[trail.Count - 1].ValueAfter.AsNumber());
            // 武器没写的字段不受影响。
            Assert.Equal(1.0, judging.GetNumber(FeelFieldNames.CancelWindowScale));
        }

        // ------------------------------------------------------------------ 6. 光环层数叠乘

        /// <summary>
        /// 复现用例：同一光环 1、2、3 层：accel_ms 从"基础值 × 0.8"变为"× 0.8 的 n 次方"，decel_ms 从"基础 + 25"变为"基础 + 25n"；
        /// 移除光环后与施加前逐位相等（第 7 层每次从基础层重算，不做反向乘除）。条目键是光环实例 id。
        /// </summary>
        [Fact]
        public void AuraFeelModifiers_StackByInstanceStacks_AndRestoreExactlyOnRemoval()
        {
            var rig = Build();
            var skills = rig.World.Gameplay.Carriers.Rules.Skill;
            var baseAccel = rig.Judging.GetNumber(FeelFieldNames.AccelMs);
            var baseDecel = rig.Judging.GetNumber(FeelFieldNames.DecelMs);
            var versionBefore = rig.Resolver.GetVersion(PlayerId);

            for (var n = 1; n <= 3; n++)
            {
                skills.EffectSink.ApplyAura(PlayerId, HasteAura, PlayerId);
                rig.Step(); // 光环事件在步骤 7 派发后失效缓存
                Assert.Equal(n, skills.AuraQuery.GetStacks(PlayerId, HasteAura));

                var expectedAccel = baseAccel;
                var expectedDecel = baseDecel;
                for (var k = 0; k < n; k++)
                {
                    expectedAccel *= AuraAccelMultiplier;
                    expectedDecel += AuraDecelAdd;
                }

                Assert.Equal(expectedAccel, rig.Judging.GetNumber(FeelFieldNames.AccelMs), 9);
                Assert.Equal(expectedDecel, rig.Judging.GetNumber(FeelFieldNames.DecelMs), 9);
            }

            Assert.True(rig.Resolver.GetVersion(PlayerId) > versionBefore);
            var instance = skills.AuraQuery.TryGetInstanceRef(PlayerId, HasteAura)!.Value.AuraInstanceId.Value;
            var trail = rig.Resolver.GetProvenance(PlayerId, FeelFieldNames.AccelMs);
            Assert.Contains(trail, e => e.Layer == (int)FeelLayer.Temporary && e.SourceId == instance);

            skills.EffectSink.RemoveAura(PlayerId, new AuraInstanceRef(new Id(instance)));
            rig.Step();
            Assert.False(skills.AuraQuery.HasAura(PlayerId, HasteAura));
            Assert.Equal(baseAccel, rig.Judging.GetNumber(FeelFieldNames.AccelMs));
            Assert.Equal(baseDecel, rig.Judging.GetNumber(FeelFieldNames.DecelMs));
        }

        [Fact]
        public void AuraFeelModifiers_SingleStack_IsIdenticalToThePreStackBehaviour()
        {
            // 不变量：1 层时与此前（层数不放大）逐位一致：乘一次、加一次。
            var rig = Build();
            var skills = rig.World.Gameplay.Carriers.Rules.Skill;
            var baseAccel = rig.Judging.GetNumber(FeelFieldNames.AccelMs);
            var baseDecel = rig.Judging.GetNumber(FeelFieldNames.DecelMs);

            skills.EffectSink.ApplyAura(PlayerId, HasteAura, PlayerId);
            rig.Step();

            Assert.Equal(baseAccel * AuraAccelMultiplier, rig.Judging.GetNumber(FeelFieldNames.AccelMs));
            Assert.Equal(baseDecel + AuraDecelAdd, rig.Judging.GetNumber(FeelFieldNames.DecelMs));
        }
    }
}
