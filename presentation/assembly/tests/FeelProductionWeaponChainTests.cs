// FeelProductionWeaponChainTests：手感落地 M1 补缺——换装链与武器普攻映射经生产装配（HeadlessWorldBuilder → GameplayAssembly → PresentationAssembly）后的运行时冒烟。
// 同一个装配好的世界、真实输入映射（StubInput → PresentationAssembly.InputMap → 输入缓冲）、真实时间线动作；游戏侧不写任何换装代码：
// 装单手剑按普攻 → 分相 tick 等于单手剑普攻技能毫秒 × 倍率换算值，姿势族 1h；换双手巨剑 → 巨剑换算值，姿势族 2h；卸下 → 空手（槽位绑定）换算值，无姿势族。
// 期望值全部由 skill.def 的时间线毫秒与手感解析出的相位倍率、标定 tick 率算出，不写死裸数。
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
using Core.Foundation.Rng;
using Core.Foundation.SceneRouter;
using Core.Carriers.Item;
using Core.Rules.Common;
using Core.Rules.Skill;
using Core.Sim;
using Presentation.Assembly;
using Presentation.Common;
using Presentation.Render;
using Xunit;

namespace Tests.Presentation.Assembly
{
    public sealed class FeelProductionWeaponChainTests : IDisposable
    {
        private const double Dt = 1.0 / 60.0;
        private const string Calibration = "feel.calibration.framework_default";

        private static readonly Id PlayerId = new Id("unit.lab_player");
        private static readonly Id MapId = new Id("world.lab_arena");
        private static readonly Id SwordSkill = new Id("skill.lab_equip.attack_sword_1h");
        private static readonly Id GreatswordSkill = new Id("skill.lab_equip.attack_greatsword");
        private static readonly Id UnarmedSkill = new Id("skill.lab_equip.attack_unarmed");
        private static readonly Id AttackAction = new Id("input.action.m1fix_attack");
        private static readonly Id MainHandSlot = new Id("item.slot.m1fix_main_hand");

        // 一把单手剑（族 1h）与一把双手巨剑（族 2h）：feel.weapon 声明 auto_attack_timeline_ref，指向数据根 _lab 里已有的两条普攻时间线技能。
        private const string OverlayWeaponJson = @"{
  ""table"": ""feel.weapon"", ""schema_version"": 1,
  ""rows"": [
    { ""id"": ""feel.weapon.m1fix_sword"", ""maturity"": ""experimental"", ""profile_version"": 1, ""family"": ""1h"",
      ""auto_attack_timeline_ref"": ""skill.lab_equip.attack_sword_1h"",
      ""writes"": [ { ""field"": ""impact_class"", ""op"": ""set"", ""value"": ""light"" } ] },
    { ""id"": ""feel.weapon.m1fix_greatsword"", ""maturity"": ""experimental"", ""profile_version"": 1, ""family"": ""2h"",
      ""auto_attack_timeline_ref"": ""skill.lab_equip.attack_greatsword"",
      ""writes"": [ { ""field"": ""impact_class"", ""op"": ""set"", ""value"": ""heavy"" } ] }
  ]
}";

        private const string OverlayItemTables = @"{ ""table"": ""item.budget_curve"", ""schema_version"": 2, ""rows"": [
  { ""id"": ""item.budget.default"", ""entries"": [ { ""x"": 1, ""y"": 20 }, { ""x"": 10, ""y"": 200 } ], ""exponent"": 1.5 } ] }";

        private const string OverlayQualityJson = @"{ ""table"": ""item.quality_definition"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""item.quality.m1fix_common"", ""name_key"": ""l10n.item.quality.m1fix_common.name"", ""sort_weight"": 1, ""budget_multiplier"": 1.0,
    ""affix_count"": 0, ""grant_budget_share"": 0.0, ""price_multiplier"": 1.0 } ] }";

        private const string OverlaySlotJson = @"{ ""table"": ""item.slot_definition"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""item.slot.m1fix_main_hand"", ""name_key"": ""l10n.item.slot.m1fix_main_hand.name"", ""sort_weight"": 1, ""is_weapon"": true, ""is_equipment"": true,
    ""budget_coefficient"": 1.0, ""price_coefficient"": 1.0, ""has_armor"": false } ] }";

        private const string OverlayItemJson = @"{ ""table"": ""item.template"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""item.m1fix_sword"", ""slot"": ""item.slot.m1fix_main_hand"", ""quality"": ""item.quality.m1fix_common"", ""item_level"": 1,
    ""weapon_profile"": { ""damage_min"": 5, ""damage_max"": 8, ""speed"": 1.5 }, ""stack_size"": 1, ""name_key"": ""l10n.item.m1fix_sword.name"", ""display_ref"": ""display.map.m1fix_sword"",
    ""feel_weapon_ref"": ""feel.weapon.m1fix_sword"" },
  { ""id"": ""item.m1fix_greatsword"", ""slot"": ""item.slot.m1fix_main_hand"", ""quality"": ""item.quality.m1fix_common"", ""item_level"": 1,
    ""weapon_profile"": { ""damage_min"": 9, ""damage_max"": 16, ""speed"": 1.5 }, ""stack_size"": 1, ""name_key"": ""l10n.item.m1fix_greatsword.name"", ""display_ref"": ""display.map.m1fix_greatsword"",
    ""feel_weapon_ref"": ""feel.weapon.m1fix_greatsword"" } ] }";

        // 物品模板必填的外形引用与名称文本键。
        private const string OverlayDisplayJson = @"{ ""table"": ""display.map"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""display.map.m1fix_sword"", ""category"": ""item"", ""logical_id"": ""item.m1fix_sword"", ""kind"": ""sprite"", ""sprite_set_id"": ""sprite.lab_placeholder"", ""direction_count"": 4 },
  { ""id"": ""display.map.m1fix_greatsword"", ""category"": ""item"", ""logical_id"": ""item.m1fix_greatsword"", ""kind"": ""sprite"", ""sprite_set_id"": ""sprite.lab_placeholder"", ""direction_count"": 4 } ] }";

        private const string OverlayTextJson = @"{ ""table"": ""l10n.text"", ""schema_version"": 1, ""rows"": [
  { ""key"": ""l10n.item.quality.m1fix_common.name"", ""locale"": ""l10n.locale.zh_cn"", ""text"": ""common"" },
  { ""key"": ""l10n.item.slot.m1fix_main_hand.name"", ""locale"": ""l10n.locale.zh_cn"", ""text"": ""main hand"" },
  { ""key"": ""l10n.item.m1fix_sword.name"", ""locale"": ""l10n.locale.zh_cn"", ""text"": ""sword"" },
  { ""key"": ""l10n.item.m1fix_greatsword.name"", ""locale"": ""l10n.locale.zh_cn"", ""text"": ""greatsword"" } ] }";

        // 普攻输入动作：类别 attack，skill_slot 指向槽位 slot_atk（空手时回落到这个槽位绑定的空手普攻）。
        private const string OverlayActionJson = @"{ ""table"": ""found.input_action"", ""schema_version"": 1, ""rows"": [
  { ""key"": ""input.action.m1fix_attack"", ""kind"": ""button"", ""default_bindings"": [ ""key:j"" ], ""rebind_group"": ""default"",
    ""class"": ""attack"", ""buffer_ms"": 160, ""skill_slot"": ""slot_atk"" } ] }";

        private readonly HeadlessWorld _world;
        private readonly PresentationAssembly _presentation;
        private readonly StubInput _input = new StubInput();
        private readonly List<IEvent> _events = new List<IEvent>();
        private readonly List<(int Tick, IEvent Event)> _log = new List<(int, IEvent)>();
        private int _tick;
        private int _cursor;

        public FeelProductionWeaponChainTests()
        {
            (_world, _presentation) = BuildRig(feel: true, viewFactory: null);

            var skills = _world.Gameplay.Carriers.Rules.Skill;
            foreach (var skill in new[] { SwordSkill, GreatswordSkill, UnarmedSkill })
            {
                skills.LearnSkill(PlayerId, skill);
            }

            // 槽位绑定只给空手普攻：武器普攻不经槽位绑定，由武器手感引用决定。
            Assert.True(_world.Gameplay.Carriers.SkillBindings.Bind(PlayerId, "slot_atk", UnarmedSkill));
        }

        /// <summary>生产装配：无头世界（GameplayAssembly，可选手感）+ PresentationAssembly（桩引擎、真实输入映射），并声明本测试的输入动作集。</summary>
        private static (HeadlessWorld World, PresentationAssembly Presentation) BuildRig(bool feel, IViewFactory? viewFactory)
        {
            var fs = new StubFileSystem();
            CopyDisk(fs, "data/_framework");
            CopyDisk(fs, "data/_feel");
            CopyDisk(fs, "data/_lab");
            fs.WriteTextAtomic("test/_m1fix/feel/feel.weapon.json", OverlayWeaponJson);
            fs.WriteTextAtomic("test/_m1fix/item/item.budget_curve.json", OverlayItemTables);
            fs.WriteTextAtomic("test/_m1fix/item/item.quality_definition.json", OverlayQualityJson);
            fs.WriteTextAtomic("test/_m1fix/item/item.slot_definition.json", OverlaySlotJson);
            fs.WriteTextAtomic("test/_m1fix/item/item.template.json", OverlayItemJson);
            fs.WriteTextAtomic("test/_m1fix/found/found.input_action.json", OverlayActionJson);
            fs.WriteTextAtomic("test/_m1fix/display/display.map.json", OverlayDisplayJson);
            fs.WriteTextAtomic("test/_m1fix/l10n/l10n.text.json", OverlayTextJson);

            var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = new List<IDataSource>
                {
                    new FileSystemDataSource(fs, "data/_framework"),
                    new FileSystemDataSource(fs, "data/_feel"),
                    new FileSystemDataSource(fs, "data/_lab"),
                    new FileSystemDataSource(fs, "test/_m1fix"),
                },
                FileSystem = fs,
                Seed = 20261002UL,
                MapId = MapId,
                PlayerId = PlayerId,
                PlayerFactionId = new Id("fac.player"),
                PlayerClassId = new Id("arch.class.lab_hero"),
                PlayerLevel = 1,
                GameId = new Id("game.m1fix"),
                StepSeconds = Dt,
                EnableDiscreteTimeModel = true,
                FeelOptions = feel ? new CarriersFeelOptions { CalibrationId = Calibration } : null,
            });
            Assert.False(world.LoadReport.IsBlocking, string.Join("\n", world.LoadReport.Issues));

            var engine = new StubEngine();
            var sceneRouter = new SceneRouter(
                world.Registry, engine.ResourceLoader, world.Gameplay.AppState, world.World, world.Gameplay.Hooks, world.Bus);
            var presentation = new PresentationAssembly(
                world.Gameplay, world.World, world.Registry, world.Bus, new RngHost(2),
                viewFactory ?? new Tests.PresentationViewBinding.FakeViewFactory(), engine.Renderer2D, engine.Camera, engine.Audio,
                engine.FileSystem, sceneRouter);

            var definitions = new List<ActionDefinition>();
            foreach (var record in world.Registry.GetAll("found.input_action"))
            {
                definitions.Add(ActionDefinition.FromRecord(record));
            }

            presentation.InputMap.DeclareActionSet(new Id("actionset.m1fix"), definitions);
            return (world, presentation);
        }

        public void Dispose() => _presentation.Dispose();

        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException());
            for (var i = 0; i < 3; i++) dir = dir.Parent ?? throw new InvalidOperationException();
            return dir.FullName;
        }

        private static void CopyDisk(StubFileSystem fs, string relativeRoot)
        {
            var absoluteRoot = Path.Combine(FindRepoRoot(), relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            foreach (var file in Directory.GetFiles(absoluteRoot, "*.json", SearchOption.AllDirectories))
            {
                var rel = file.Substring(absoluteRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                fs.WriteTextAtomic(relativeRoot + "/" + rel, File.ReadAllText(file));
            }
        }

        // ------------------------------------------------------------------ 驱动

        private void Step()
        {
            _presentation.InputMap.Update(_input);
            _world.Gameplay.Advance(Dt);
            _world.Spatial.UpdatePosition(PlayerId, _world.Player.Position);
            while (_cursor < _world.Events.Count) _log.Add((_tick, _world.Events[_cursor++]));
            _tick++;
        }

        private void Run(int ticks)
        {
            for (var i = 0; i < ticks; i++) Step();
        }

        /// <summary>点按普攻（按下 + 下一 tick 抬起），随后跑到本次动作结束；返回这次动作的实测与期望。</summary>
        private ActionObservation Attack()
        {
            var firstLogIndex = _log.Count;
            _input.Press("j");
            var pressTick = _tick;
            Step();
            _input.Release("j");

            var started = default(ActionStartedEvent);
            var startTick = -1;
            var activeAt = -1;
            var recoveryAt = -1;
            var finishedAt = -1;
            for (var guard = 0; guard < 400 && finishedAt < 0; guard++)
            {
                for (var i = firstLogIndex; i < _log.Count; i++)
                {
                    var (tick, evt) = _log[i];
                    if (evt is ActionStartedEvent s && s.ActorId.Equals(PlayerId) && started == null) { started = s; startTick = tick; }
                    else if (started != null && evt is ActionPhaseChangedEvent p && p.ActorId.Equals(PlayerId))
                    {
                        if (p.Phase == ActionPhase.Active && activeAt < 0) activeAt = tick - startTick;
                        else if (p.Phase == ActionPhase.Recovery && recoveryAt < 0) recoveryAt = tick - startTick;
                    }
                    else if (started != null && evt is ActionFinishedEvent f && f.ActorId.Equals(PlayerId) && finishedAt < 0) finishedAt = tick - startTick;
                }

                firstLogIndex = _log.Count;
                if (finishedAt < 0) Step();
            }

            Assert.NotNull(started);
            Assert.True(finishedAt >= 0, "动作应在有限 tick 内结束");
            Run(2);
            Assert.True(startTick >= pressTick);
            return new ActionObservation(started!.SkillId, started.DurationTicks, activeAt, recoveryAt, finishedAt);
        }

        private readonly struct ActionObservation
        {
            public readonly Id Skill;
            public readonly int DurationTicks;
            public readonly int ActiveAt;
            public readonly int RecoveryAt;
            public readonly int FinishedAt;

            public ActionObservation(Id skill, int durationTicks, int activeAt, int recoveryAt, int finishedAt)
            {
                Skill = skill;
                DurationTicks = durationTicks;
                ActiveAt = activeAt;
                RecoveryAt = recoveryAt;
                FinishedAt = finishedAt;
            }
        }

        /// <summary>某技能时间线毫秒 × 当前玩家手感解析出的相位倍率，按标定步长换算的 tick 数：(前摇, 有效, 后摇)。</summary>
        private (int Startup, int Active, int Recovery) ExpectedTicks(Id skillId)
        {
            var def = _world.Registry.Get("skill.def", skillId) ?? throw new InvalidOperationException("缺技能 " + skillId);
            Assert.True(def.TryGetObject("timeline", out var timeline));
            double Ms(string name)
            {
                for (var i = 0; i < timeline.Count; i++)
                {
                    if (string.Equals(timeline[i].Key, name, StringComparison.Ordinal)
                        && timeline[i].Value is Core.Foundation.Common.Json.JsonNumber n)
                    {
                        return n.Value;
                    }
                }

                return 0;
            }

            var judging = _world.Gameplay.Feel!.Resolver.Resolve(PlayerId).Judging;
            int Ticks(string field, string scale) =>
                FeelCalibration.MillisecondsToTicks(Ms(field) * judging.GetNumber(scale), Dt);
            return (Ticks("startup_ms", FeelFieldNames.PhaseScaleStartup),
                Ticks("active_ms", FeelFieldNames.PhaseScaleActive),
                Ticks("recovery_ms", FeelFieldNames.PhaseScaleRecovery));
        }

        private void Equip(string itemTemplate)
        {
            var carriers = _world.Gameplay.Carriers;
            Assert.True(carriers.Inventory.AddItem(PlayerId, new Id(itemTemplate), 1));
            Id? instance = null;
            foreach (var item in carriers.Inventory.ListItems(PlayerId))
            {
                if (string.Equals(item.TemplateId.Value, itemTemplate, StringComparison.Ordinal)) instance = item.InstanceId;
            }

            Assert.True(instance.HasValue);
            Assert.True(carriers.Equipment.Equip(PlayerId, instance.Value, MainHandSlot).Success);
            // 装备事件经事件总线在固定步末尾派发：跑一个 tick，换装链对账与姿势族刷新在这一 tick 末尾完成。
            Run(1);
        }

        private void AssertActionMatchesRule(ActionObservation observed, Id expectedSkill)
        {
            Assert.Equal(expectedSkill, observed.Skill);
            var expected = ExpectedTicks(expectedSkill);
            Assert.Equal(expected.Startup, observed.ActiveAt);
            Assert.Equal(expected.Startup + expected.Active, observed.RecoveryAt);
            Assert.Equal(expected.Startup + expected.Active + expected.Recovery, observed.FinishedAt);
            Assert.Equal(observed.FinishedAt, observed.DurationTicks);
        }

        // ------------------------------------------------------------------ 冒烟

        private string? PoseFamily() => _presentation.Pose!.GetContext(PlayerId).Family;

        private Id WeaponAutoAttack(string weaponId) =>
            _world.Registry.Get("feel.weapon", weaponId)!.GetId("auto_attack_timeline_ref");

        private string WeaponFamily(string weaponId) =>
            _world.Registry.Get("feel.weapon", weaponId)!.GetString("family");

        [Fact]
        public void WeaponSwap_ChangesAutoAttackTimelineAndPoseFamily_WithoutGameCode()
        {
            // 空手（尚未装备任何武器）：回落到槽位绑定的空手普攻，没有武器族。
            AssertActionMatchesRule(Attack(), UnarmedSkill);
            Assert.Null(PoseFamily());

            // 武器族与普攻时间线的期望都由手感数据（feel.weapon.family / auto_attack_timeline_ref）读出，不写死。
            Equip("item.m1fix_sword");
            AssertActionMatchesRule(Attack(), WeaponAutoAttack("feel.weapon.m1fix_sword"));
            Assert.Equal(WeaponFamily("feel.weapon.m1fix_sword"), PoseFamily());

            Equip("item.m1fix_greatsword");
            AssertActionMatchesRule(Attack(), WeaponAutoAttack("feel.weapon.m1fix_greatsword"));
            Assert.Equal(WeaponFamily("feel.weapon.m1fix_greatsword"), PoseFamily());
            Assert.NotEqual(WeaponFamily("feel.weapon.m1fix_sword"), WeaponFamily("feel.weapon.m1fix_greatsword"));

            Assert.NotNull(_world.Gameplay.Carriers.Equipment.Unequip(PlayerId, MainHandSlot));
            Run(1);
            AssertActionMatchesRule(Attack(), UnarmedSkill);
            Assert.Null(PoseFamily());
        }

        private static BufferedIntent AttackIntent(Id action) =>
            new BufferedIntent(action, ActionClass.Attack, 0L, 10L, 0, null, BufferHoldState.Tap, 0, false);

        [Fact]
        public void AutoAttackActions_RestrictsWhichAttackActionsFollowTheWeapon()
        {
            // 游戏有第二个攻击类动作（自带 skill_slot）时，用 AutoAttackActions 点名普攻，其余攻击类动作始终走槽位绑定。
            Equip("item.m1fix_sword");
            var heavy = new Id("input.action.m1fix_heavy");

            // 缺省（不点名）= 所有攻击类动作都跟随武器普攻。
            Assert.True(_world.Gameplay.Feel!.ActionBinding.TryResolveSkill(PlayerId, AttackIntent(heavy), out var skill));
            Assert.Equal(WeaponAutoAttack("feel.weapon.m1fix_sword"), skill);

            var restricted = new WeaponPreferredActionBinding(
                new WeaponActionBinding(
                    new EquippedWeaponFeelProvider(_world.Gameplay.Carriers.Equipment, _world.Registry), new FeelWeaponCatalog(_world.Registry)),
                new FixedBinding(UnarmedSkill),
                new[] { AttackAction });
            Assert.True(restricted.TryResolveSkill(PlayerId, AttackIntent(heavy), out skill));
            Assert.Equal(UnarmedSkill, skill);
            Assert.True(restricted.TryResolveSkill(PlayerId, AttackIntent(AttackAction), out skill));
            Assert.Equal(WeaponAutoAttack("feel.weapon.m1fix_sword"), skill);
        }

        private sealed class FixedBinding : IActionSkillBinding
        {
            private readonly Id _skill;

            public FixedBinding(Id skill) => _skill = skill;

            public bool TryResolveSkill(Id actorId, BufferedIntent intent, out Id skillId)
            {
                skillId = _skill;
                return true;
            }
        }

        private sealed class ReceivingViewFactory : IViewFactory, IPoseContextReceiver
        {
            public IPoseContextSource? Received;
            public int Calls;

            public IView CreateView(ViewKind kind, Id displayId, Id entityId) =>
                new Tests.PresentationViewBinding.FakeView();

            public void SetPoseContextSource(IPoseContextSource source)
            {
                Received = source;
                Calls++;
            }
        }

        [Fact]
        public void PoseSource_IsHandedToReceivingViewFactory_OnlyWhenFeelEnabled()
        {
            var enabledFactory = new ReceivingViewFactory();
            var (enabledWorld, enabled) = BuildRig(feel: true, viewFactory: enabledFactory);
            using (enabled)
            {
                Assert.NotNull(enabledWorld.Gameplay.Feel);
                Assert.NotNull(enabled.Pose);
                Assert.Same(enabled.Pose, enabledFactory.Received);
                Assert.Equal(1, enabledFactory.Calls);
            }

            var disabledFactory = new ReceivingViewFactory();
            var (disabledWorld, disabled) = BuildRig(feel: false, viewFactory: disabledFactory);
            using (disabled)
            {
                Assert.Null(disabledWorld.Gameplay.Feel);
                Assert.Null(disabled.Pose);
                Assert.Null(disabledFactory.Received);
                Assert.Equal(0, disabledFactory.Calls);
            }
        }
    }
}
