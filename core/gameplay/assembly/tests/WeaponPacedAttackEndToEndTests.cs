// WeaponPacedAttackEndToEndTests：攻速绑定挥击节奏（weapon_paced 时间线）经生产装配后的运行时冒烟。
// 真实输入映射（StubInput → InputMapHost → 输入缓冲）+ 真实装备/武器查询 + 真实施法管线；期望值全部由技能毫秒、武器速度与基准秒数算出，不写死裸数。
// 覆盖：连点上限由数据推出；换不同速度的武器节奏按比例变；未标记的时间线不受影响；缓冲不会打断当前挥击。
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
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Core.Sim;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    public sealed class WeaponPacedAttackEndToEndTests
    {
        private const double Dt = 0.02;
        private const double ReferenceSeconds = 1.0; // SkillOptions.WeaponPaceReferenceSeconds 默认值

        private static readonly Id MapId = new Id("world.lab_arena");
        private static readonly Id PlayerId = new Id("unit.lab_player");
        private static readonly Id PacedSkill = new Id("skill.wp_swing_paced");
        private static readonly Id PlainSkill = new Id("skill.wp_swing_plain");
        private static readonly Id MainHandSlot = new Id("item.slot.wp_main_hand");

        // 两条同节奏时间线（前摇 100 / 有效 100 / 后摇 200 ms），只有 paced 带 weapon_paced。
        private const int StartupMs = 100;
        private const int ActiveMs = 100;
        private const int RecoveryMs = 200;
        private const string SkillJson = @"{ ""table"": ""skill.def"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""skill.wp_swing_paced"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 0, ""cast_time"": 0.4, ""respects_gcd"": false,
    ""cooldown_duration"": 0, ""target_shape_ref"": ""target.chain.lab_equip_self"", ""effects"": [],
    ""timeline"": { ""startup_ms"": 100, ""active_ms"": 100, ""recovery_ms"": 200, ""weapon_paced"": true } },
  { ""id"": ""skill.wp_swing_plain"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 0, ""cast_time"": 0.4, ""respects_gcd"": false,
    ""cooldown_duration"": 0, ""target_shape_ref"": ""target.chain.lab_equip_self"", ""effects"": [],
    ""timeline"": { ""startup_ms"": 100, ""active_ms"": 100, ""recovery_ms"": 200 } } ] }";

        private const string ActionJson = @"{ ""table"": ""found.input_action"", ""schema_version"": 1, ""rows"": [
  { ""key"": ""input.action.wp_attack"", ""kind"": ""button"", ""default_bindings"": [ ""key:j"" ], ""rebind_group"": ""default"",
    ""class"": ""attack"", ""buffer_ms"": 160, ""skill_slot"": ""slot_wp"" } ] }";

        // 速度是挥击间隔（秒）：fast 0.5、slow 2.0；基准 1.0 时时间线分别拉伸 x0.5 / x2.0。
        private const double FastSpeed = 0.5;
        private const double SlowSpeed = 2.0;
        private const string ItemSlotJson = @"{ ""table"": ""item.slot_definition"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""item.slot.wp_main_hand"", ""name_key"": ""l10n.item.slot.wp_main_hand.name"", ""sort_weight"": 1, ""is_weapon"": true, ""is_equipment"": true,
    ""budget_coefficient"": 1.0, ""price_coefficient"": 1.0, ""has_armor"": false } ] }";
        private const string ItemTemplateJson = @"{ ""table"": ""item.template"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""item.wp_fast"", ""slot"": ""item.slot.wp_main_hand"", ""quality"": ""item.quality.wp_common"", ""item_level"": 1,
    ""weapon_profile"": { ""damage_min"": 5, ""damage_max"": 8, ""speed"": 0.5 }, ""stack_size"": 1, ""name_key"": ""l10n.item.wp_fast.name"", ""display_ref"": ""display.map.wp_fast"" },
  { ""id"": ""item.wp_slow"", ""slot"": ""item.slot.wp_main_hand"", ""quality"": ""item.quality.wp_common"", ""item_level"": 1,
    ""weapon_profile"": { ""damage_min"": 5, ""damage_max"": 8, ""speed"": 2.0 }, ""stack_size"": 1, ""name_key"": ""l10n.item.wp_slow.name"", ""display_ref"": ""display.map.wp_slow"" } ] }";
        private const string ItemBudgetJson = @"{ ""table"": ""item.budget_curve"", ""schema_version"": 2, ""rows"": [
  { ""id"": ""item.budget.default"", ""entries"": [ { ""x"": 1, ""y"": 20 }, { ""x"": 10, ""y"": 200 } ], ""exponent"": 1.5 } ] }";
        private const string ItemQualityJson = @"{ ""table"": ""item.quality_definition"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""item.quality.wp_common"", ""name_key"": ""l10n.item.quality.wp_common.name"", ""sort_weight"": 1, ""budget_multiplier"": 1.0,
    ""affix_count"": 0, ""grant_budget_share"": 0.0, ""price_multiplier"": 1.0 } ] }";
        private const string DisplayJson = @"{ ""table"": ""display.map"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""display.map.wp_fast"", ""category"": ""item"", ""logical_id"": ""item.wp_fast"", ""kind"": ""sprite"", ""sprite_set_id"": ""sprite.lab_placeholder"", ""direction_count"": 4 },
  { ""id"": ""display.map.wp_slow"", ""category"": ""item"", ""logical_id"": ""item.wp_slow"", ""kind"": ""sprite"", ""sprite_set_id"": ""sprite.lab_placeholder"", ""direction_count"": 4 } ] }";
        private const string TextJson = @"{ ""table"": ""l10n.text"", ""schema_version"": 1, ""rows"": [
  { ""key"": ""l10n.item.quality.wp_common.name"", ""locale"": ""l10n.locale.zh_cn"", ""text"": ""common"" },
  { ""key"": ""l10n.item.slot.wp_main_hand.name"", ""locale"": ""l10n.locale.zh_cn"", ""text"": ""main hand"" },
  { ""key"": ""l10n.item.wp_fast.name"", ""locale"": ""l10n.locale.zh_cn"", ""text"": ""fast"" },
  { ""key"": ""l10n.item.wp_slow.name"", ""locale"": ""l10n.locale.zh_cn"", ""text"": ""slow"" } ] }";

        private sealed class Rig
        {
            public HeadlessWorld World = null!;
            public StubInput Input = new StubInput();
            public InputMapHost Map = null!;
            public int Tick;
            public readonly List<(int Tick, IEvent Event)> Log = new List<(int, IEvent)>();
            private int _cursor;

            public void Step()
            {
                Map.Update(Input);
                World.Gameplay.Advance(Dt);
                while (_cursor < World.Events.Count) Log.Add((Tick, World.Events[_cursor++]));
                Tick++;
            }

            public void Run(int ticks)
            {
                for (var i = 0; i < ticks; i++) Step();
            }

            /// <summary>每 periodTicks 个 tick 点按一次，持续 seconds 秒；返回点按次数。</summary>
            public int Spam(int periodTicks, double seconds)
            {
                var steps = (int)Math.Round(seconds / Dt);
                var presses = 0;
                for (var i = 0; i < steps; i++)
                {
                    if (i % periodTicks == 0) { Input.Press("j"); presses++; }
                    Step();
                    Input.Release("j");
                }

                return presses;
            }

            public List<int> StartTicks(Id skill) =>
                Log.Where(l => l.Event is ActionStartedEvent s && s.SkillId.Equals(skill)).Select(l => l.Tick).ToList();

            public int Starts(Id skill) => StartTicks(skill).Count;
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

        private static Rig Build(Id skill, string? weaponTemplate)
        {
            var fs = new StubFileSystem();
            var repoRoot = FindRepoRoot();
            CopyDisk(fs, repoRoot, "data/_framework");
            CopyDisk(fs, repoRoot, "data/_feel");
            CopyDisk(fs, repoRoot, "data/_lab");
            fs.WriteTextAtomic("test/_wp/skill/skill.def.json", SkillJson);
            fs.WriteTextAtomic("test/_wp/found/found.input_action.json", ActionJson);
            fs.WriteTextAtomic("test/_wp/item/item.budget_curve.json", ItemBudgetJson);
            fs.WriteTextAtomic("test/_wp/item/item.quality_definition.json", ItemQualityJson);
            fs.WriteTextAtomic("test/_wp/item/item.slot_definition.json", ItemSlotJson);
            fs.WriteTextAtomic("test/_wp/item/item.template.json", ItemTemplateJson);
            fs.WriteTextAtomic("test/_wp/display/display.map.json", DisplayJson);
            fs.WriteTextAtomic("test/_wp/l10n/l10n.text.json", TextJson);

            var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = new List<IDataSource>
                {
                    new FileSystemDataSource(fs, "data/_framework"),
                    new FileSystemDataSource(fs, "data/_feel"),
                    new FileSystemDataSource(fs, "data/_lab"),
                    new FileSystemDataSource(fs, "test/_wp"),
                },
                FileSystem = fs,
                Seed = 20261009UL,
                MapId = MapId,
                PlayerId = PlayerId,
                PlayerFactionId = new Id("fac.player"),
                PlayerClassId = new Id("arch.class.lab_hero"),
                PlayerLevel = 1,
                GameId = new Id("game.lab"),
                StepSeconds = Dt,
                EnableDiscreteTimeModel = true,
                FeelOptions = new CarriersFeelOptions { CalibrationId = "feel.calibration.framework_default" },
            });
            Assert.False(world.LoadReport.IsBlocking, string.Join("\n", world.LoadReport.Issues));

            var rig = new Rig { World = world };
            rig.Map = new InputMapHost(world.Bus);
            var definitions = new List<ActionDefinition>();
            foreach (var record in world.Registry.GetAll("found.input_action")) definitions.Add(ActionDefinition.FromRecord(record));
            rig.Map.DeclareActionSet(new Id("actionset.wp"), definitions);
            world.Gameplay.Feel!.InputBuffer.BindLocalInput(rig.Map, PlayerId, null);

            var carriers = world.Gameplay.Carriers;
            carriers.Rules.Skill.LearnSkill(PlayerId, skill);
            Assert.True(carriers.SkillBindings.Bind(PlayerId, "slot_wp", skill));
            if (weaponTemplate != null) Equip(rig, weaponTemplate);
            return rig;
        }

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
            rig.Run(1);
        }

        private static int Ticks(double seconds) => (int)Math.Round(seconds / Dt);

        // 一挥完整时长（tick），由技能毫秒、武器速度、基准秒数算出。
        private static int ExpectedSwingTicks(double weaponSpeed) =>
            Ticks((StartupMs + ActiveMs + RecoveryMs) / 1000.0 * (weaponSpeed / ReferenceSeconds));

        [Theory]
        [InlineData(FastSpeed)]
        [InlineData(SlowSpeed)]
        public void SpamIsBoundedByWeaponSpeed(double speed)
        {
            var rig = Build(PacedSkill, speed == FastSpeed ? "item.wp_fast" : "item.wp_slow");
            const double seconds = 6.0;
            var presses = rig.Spam(2, seconds); // 每 0.04 秒点一次，远快于任何一挥
            var swing = ExpectedSwingTicks(speed);
            var upper = (int)Math.Ceiling(seconds / (swing * Dt)) + 1;
            var starts = rig.StartTicks(PacedSkill);

            Assert.True(starts.Count <= upper, $"started={starts.Count} upper={upper} presses={presses}");
            Assert.True(starts.Count >= upper * 0.7, $"started={starts.Count} upper={upper}"); // 下限：缓冲衔接损耗不超过三成，证明确实在按节奏连续挥击而非被卡死
            Assert.True(presses > starts.Count * 3);

            // 相邻两次起手的间隔不小于一挥完整时长（输入不得打断当前挥击）。
            for (var i = 1; i < starts.Count; i++) Assert.True(starts[i] - starts[i - 1] >= swing, $"gap {starts[i] - starts[i - 1]} < {swing}");
        }

        [Fact]
        public void SlowerWeaponSwingsProportionallyLess()
        {
            var fast = Build(PacedSkill, "item.wp_fast");
            var slow = Build(PacedSkill, "item.wp_slow");
            const double seconds = 8.0;
            fast.Spam(2, seconds);
            slow.Spam(2, seconds);
            var ratio = (double)fast.Starts(PacedSkill) / slow.Starts(PacedSkill);
            var expected = SlowSpeed / FastSpeed;
            Assert.InRange(ratio, expected * 0.8, expected * 1.25);
        }

        [Fact]
        public void UnflaggedTimelineIgnoresWeaponSpeed()
        {
            var fast = Build(PlainSkill, "item.wp_fast");
            var slow = Build(PlainSkill, "item.wp_slow");
            fast.Spam(2, 4.0);
            slow.Spam(2, 4.0);
            Assert.Equal(fast.Starts(PlainSkill), slow.Starts(PlainSkill));
        }

        [Fact]
        public void BufferedPressDoesNotRestartCurrentSwing()
        {
            var rig = Build(PacedSkill, "item.wp_slow");
            rig.Spam(2, 3.0);
            // 一次施法只对应一次起手：没有同一次挥击被打断后重开（起手数 - 完成数 为 0 或 1）。
            var started = rig.Starts(PacedSkill);
            var finished = rig.Log.Count(l => l.Event is ActionFinishedEvent);
            Assert.InRange(started - finished, 0, 1);
        }
    }
}
