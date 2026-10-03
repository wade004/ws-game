// GuardSustainAssemblyTests：输入层的按住维持（ADR-0143，timeline.active_until_release）与受击裁决的格挡（ADR-0145）经生产装配接上的运行时冒烟。
// 带 skill.tag.guard 标签的技能按住维持期间算在格挡：正面命中伤害 = 普通伤害 × guard_damage_scale；松键后恢复普通伤害；没有标签的维持技能不算格挡。
// 期望值由同一单位未格挡时的实测伤害与档案字段算出，不写死裸数。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Numbers.PowerSet;
using Core.Rules.Common;
using Core.Rules.Skill;
using Core.Sim;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    public sealed class GuardSustainAssemblyTests
    {
        private const double Dt = 0.02;
        private const string FrameworkCalibration = "feel.calibration.framework_default";
        private const double GuardScale = 0.25;

        private static readonly Id MapId = new Id("world.lab_arena");
        private static readonly Id PlayerId = new Id("unit.lab_player");
        private static readonly Id GuardSkill = new Id("skill.m5_guard");
        private static readonly Id PlainSkill = new Id("skill.m5_hold_plain");
        private static readonly Id StakeTemplate = new Id("creature.lab_stake");
        private static readonly Id Physical = new Id("school.physical");

        private const string SkillJson = @"{
  ""table"": ""skill.def"", ""schema_version"": 1,
  ""rows"": [
    { ""id"": ""skill.m5_guard"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 0, ""cast_time"": 0.3,
      ""cooldown_duration"": 0, ""respects_gcd"": false, ""tags"": [ ""skill.tag.guard"" ], ""target_shape_ref"": ""target.chain.lab_equip_self"",
      ""timeline"": { ""startup_ms"": 100, ""active_ms"": 100, ""recovery_ms"": 100, ""markers"": [], ""active_until_release"": { ""max_ms"": 5000 } },
      ""effects"": [ { ""kind"": ""heal"", ""params"": { ""base_value"": 1, ""coefficient"": 0 } } ] },
    { ""id"": ""skill.m5_hold_plain"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 0, ""cast_time"": 0.3,
      ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_equip_self"",
      ""timeline"": { ""startup_ms"": 100, ""active_ms"": 100, ""recovery_ms"": 100, ""markers"": [], ""active_until_release"": { ""max_ms"": 5000 } },
      ""effects"": [ { ""kind"": ""heal"", ""params"": { ""base_value"": 1, ""coefficient"": 0 } } ] }
  ]
}";

        private const string ActionJson = @"{
  ""table"": ""found.input_action"", ""schema_version"": 1,
  ""rows"": [
    { ""key"": ""input.action.m5_guard"", ""kind"": ""button"", ""default_bindings"": [ ""key:g"" ], ""rebind_group"": ""default"",
      ""class"": ""skill"", ""buffer_ms"": 200, ""skill_slot"": ""slot_guard"" },
    { ""key"": ""input.action.m5_plain"", ""kind"": ""button"", ""default_bindings"": [ ""key:h"" ], ""rebind_group"": ""default"",
      ""class"": ""skill"", ""buffer_ms"": 200, ""skill_slot"": ""slot_plain"" }
  ]
}";

        private sealed class Rig
        {
            public HeadlessWorld World = null!;
            public StubInput Input = new StubInput();
            public InputMapHost Map = null!;

            public void Step()
            {
                Map.Update(Input);
                World.Gameplay.Advance(Dt);
                World.Spatial.UpdatePosition(PlayerId, World.Player.Position);
            }

            public void Run(int ticks)
            {
                for (var i = 0; i < ticks; i++) Step();
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

        private static (Rig Rig, Id Attacker) Build()
        {
            var fs = new StubFileSystem();
            var repoRoot = FindRepoRoot();
            CopyDisk(fs, repoRoot, "data/_framework");
            CopyDisk(fs, repoRoot, "data/_feel");
            CopyDisk(fs, repoRoot, "data/_lab");
            fs.WriteTextAtomic("test/_m5g/skill/skill.def.json", SkillJson);
            fs.WriteTextAtomic("test/_m5g/found/found.input_action.json", ActionJson);

            var worldOptions = new HeadlessWorldOptions
            {
                DataSources = new List<IDataSource>
                {
                    new FileSystemDataSource(fs, "data/_framework"),
                    new FileSystemDataSource(fs, "data/_feel"),
                    new FileSystemDataSource(fs, "data/_lab"),
                    new FileSystemDataSource(fs, "test/_m5g"),
                },
                FileSystem = fs,
                Seed = 20261004UL,
                MapId = MapId,
                PlayerId = PlayerId,
                PlayerFactionId = new Id("fac.player"),
                PlayerClassId = new Id("arch.class.lab_hero"),
                PlayerLevel = 1,
                GameId = new Id("game.lab"),
                StepSeconds = Dt,
                EnableDiscreteTimeModel = true,
                FeelOptions = new CarriersFeelOptions { CalibrationId = FrameworkCalibration },
            };
            var world = HeadlessWorldBuilder.Build(worldOptions);
            Assert.False(world.LoadReport.IsBlocking, string.Join("\n", world.LoadReport.Issues));
            Assert.Equal(0, world.LoadReport.ErrorCount);

            var rig = new Rig { World = world };
            rig.Map = new InputMapHost(world.Bus);
            var definitions = new List<ActionDefinition>();
            foreach (var record in world.Registry.GetAll("found.input_action")) definitions.Add(ActionDefinition.FromRecord(record));
            rig.Map.DeclareActionSet(new Id("actionset.m5g"), definitions);
            world.Gameplay.Feel!.InputBuffer.BindLocalInput(rig.Map, PlayerId, null);

            var skills = world.Gameplay.Carriers.Rules.Skill;
            skills.LearnSkill(PlayerId, GuardSkill);
            skills.LearnSkill(PlayerId, PlainSkill);
            var bindings = world.Gameplay.Carriers.SkillBindings;
            Assert.True(bindings.Bind(PlayerId, "slot_guard", GuardSkill));
            Assert.True(bindings.Bind(PlayerId, "slot_plain", PlainSkill));

            // 玩家朝 +x，攻击方站在正面；玩家声明格挡减伤倍率（调试覆盖层单位覆盖，同实验室的调参入口）。
            world.Gameplay.Carriers.Units.SetFacing(PlayerId, 0.0);
            var pos = new Vec2(1.5, 0);
            var attacker = world.Gameplay.Carriers.Creatures.Spawn(StakeTemplate, MapId, pos, Math.PI, null, 1);
            world.Spatial.Register(attacker, pos, 0.5);
            var ai = world.Gameplay.Carriers.Rules.Ai;
            foreach (var registered in new List<Id>(ai.RegisteredUnitIds))
            {
                if (registered.Equals(attacker)) { ai.UnregisterUnit(attacker); break; }
            }

            world.Gameplay.Feel!.Feel.DebugOverrides!.SetUnit(
                PlayerId, new FeelWrite(FeelFieldNames.GuardDamageScale, FeelOp.Set, FeelValue.Of(GuardScale)));
            return (rig, attacker);
        }

        /// <summary>攻击方对玩家打一击（可未命中的伤害结算），返回玩家掉的血。</summary>
        private static double Hit(Rig rig, Id attacker)
        {
            var powers = rig.World.Gameplay.Carriers.Rules.Powers;
            var before = powers.GetPower(PlayerId, WellKnownPowers.Health);
            var context = new EffectContext(
                attacker, PlayerId, GuardSkill, EffectKind.SchoolDamage, Physical, 10, coefficient: 1.0, isPeriodic: false, canCrit: false, canMiss: true);
            rig.World.Gameplay.Carriers.Rules.Combat.ResolveEffect(context);
            rig.Step();
            return before - powers.GetPower(PlayerId, WellKnownPowers.Health);
        }

        private static int Ticks(double ms) => FeelCalibration.MillisecondsToTicks(ms, Dt);

        /// <summary>按住键直到动作进入按住维持（前摇 + 判定相走完后停在判定相末尾）。</summary>
        private static void HoldUntilSustained(Rig rig, string key, Id skill)
        {
            rig.Input.Press(key);
            var skillHost = rig.World.Gameplay.Carriers.Rules.Skill;
            for (var i = 0; i < Ticks(100) + Ticks(100) + 10; i++) rig.Step();
            Assert.True(skillHost.ActionStateQuery.IsSustained(PlayerId), "按住后动作应停在判定相末尾按住维持");
            Assert.Equal(skill, skillHost.ActionStateQuery.Current(PlayerId)!.Value.SkillId);
        }

        [Fact]
        public void HoldingAGuardTaggedSustainedAction_BlocksFrontalHitsWithGuardDamageScale_ReleaseRestoresNormalDamage()
        {
            var (rig, attacker) = Build();
            var baseline = Hit(rig, attacker);
            Assert.True(baseline > 0, "未格挡的命中应造成伤害");

            HoldUntilSustained(rig, "g", GuardSkill);
            var guarded = Hit(rig, attacker);
            Assert.Equal(baseline * GuardScale, guarded, 6);

            rig.Input.Release("g");
            rig.Run(Ticks(100) + Ticks(100) + 10); // 松键后动作走完后摇、不再维持
            Assert.False(rig.World.Gameplay.Carriers.Rules.Skill.ActionStateQuery.IsSustained(PlayerId));
            Assert.Equal(baseline, Hit(rig, attacker), 6);
        }

        [Fact]
        public void SustainedActionWithoutTheGuardTag_DoesNotCountAsGuarding()
        {
            var (rig, attacker) = Build();
            var baseline = Hit(rig, attacker);
            HoldUntilSustained(rig, "h", PlainSkill);
            Assert.Equal(baseline, Hit(rig, attacker), 6);
        }
    }
}
