// FeelPoseGaitProductionTests：手感落地 M2-B——步态经生产装配（HeadlessWorldBuilder → GameplayAssembly → PresentationAssembly）喂进姿势选择器后的运行时冒烟，
// 以及单位销毁时姿势选择器武器族清理与换装链对账的配合（手感设计/04 第 2 节、08 第 1 节）。
// 期望值全部由规则算出：步态由同一份呈现型手感视图的阈值对"运动状态速度比序列"逐 tick 推演（独立的 GaitDeriver 实例），不写死裸数。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Core.Sim;
using Presentation.Assembly;
using Presentation.Render;
using Xunit;

namespace Tests.Presentation.Assembly
{
    public sealed class FeelPoseGaitProductionTests : IDisposable
    {
        private const double Dt = 1.0 / 60.0;
        private const string Calibration = "feel.calibration.framework_default";
        private const string PresetPath = "data/_feel/feel/feel.preset.json";

        private static readonly Id PlayerId = new Id("unit.lab_player");
        private static readonly Id MapId = new Id("world.lab_arena");
        private static readonly Id MainHandSlot = new Id("item.slot.m2b_main_hand");

        private const string WeaponJson = @"{
  ""table"": ""feel.weapon"", ""schema_version"": 1,
  ""rows"": [
    { ""id"": ""feel.weapon.m2b_sword"", ""maturity"": ""experimental"", ""profile_version"": 1, ""family"": ""1h"",
      ""auto_attack_timeline_ref"": ""skill.lab_equip.attack_sword_1h"",
      ""writes"": [ { ""field"": ""impact_class"", ""op"": ""set"", ""value"": ""light"" } ] } ] }";

        private const string BudgetJson = @"{ ""table"": ""item.budget_curve"", ""schema_version"": 2, ""rows"": [
  { ""id"": ""item.budget.default"", ""entries"": [ { ""x"": 1, ""y"": 20 }, { ""x"": 10, ""y"": 200 } ], ""exponent"": 1.5 } ] }";

        private const string QualityJson = @"{ ""table"": ""item.quality_definition"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""item.quality.m2b_common"", ""name_key"": ""l10n.item.quality.m2b_common.name"", ""sort_weight"": 1, ""budget_multiplier"": 1.0,
    ""affix_count"": 0, ""grant_budget_share"": 0.0, ""price_multiplier"": 1.0 } ] }";

        private const string SlotJson = @"{ ""table"": ""item.slot_definition"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""item.slot.m2b_main_hand"", ""name_key"": ""l10n.item.slot.m2b_main_hand.name"", ""sort_weight"": 1, ""is_weapon"": true, ""is_equipment"": true,
    ""budget_coefficient"": 1.0, ""price_coefficient"": 1.0, ""has_armor"": false } ] }";

        private const string ItemJson = @"{ ""table"": ""item.template"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""item.m2b_sword"", ""slot"": ""item.slot.m2b_main_hand"", ""quality"": ""item.quality.m2b_common"", ""item_level"": 1,
    ""weapon_profile"": { ""damage_min"": 5, ""damage_max"": 8, ""speed"": 1.5 }, ""stack_size"": 1, ""name_key"": ""l10n.item.m2b_sword.name"", ""display_ref"": ""display.map.m2b_sword"",
    ""feel_weapon_ref"": ""feel.weapon.m2b_sword"" } ] }";

        private const string DisplayJson = @"{ ""table"": ""display.map"", ""schema_version"": 1, ""rows"": [
  { ""id"": ""display.map.m2b_sword"", ""category"": ""item"", ""logical_id"": ""item.m2b_sword"", ""kind"": ""sprite"", ""sprite_set_id"": ""sprite.lab_placeholder"", ""direction_count"": 4 } ] }";

        private const string TextJson = @"{ ""table"": ""l10n.text"", ""schema_version"": 1, ""rows"": [
  { ""key"": ""l10n.item.quality.m2b_common.name"", ""locale"": ""l10n.locale.zh_cn"", ""text"": ""common"" },
  { ""key"": ""l10n.item.slot.m2b_main_hand.name"", ""locale"": ""l10n.locale.zh_cn"", ""text"": ""main hand"" },
  { ""key"": ""l10n.item.m2b_sword.name"", ""locale"": ""l10n.locale.zh_cn"", ""text"": ""sword"" } ] }";

        private readonly HeadlessWorld _world;
        private readonly PresentationAssembly _presentation;

        public FeelPoseGaitProductionTests()
        {
            (_world, _presentation) = BuildRig(sprintMinRatio: null);
        }

        public void Dispose() => _presentation.Dispose();

        private static (HeadlessWorld World, PresentationAssembly Presentation) BuildRig(double? sprintMinRatio)
        {
            var fs = new StubFileSystem();
            CopyDisk(fs, "data/_framework");
            CopyDisk(fs, "data/_feel");
            CopyDisk(fs, "data/_lab");
            if (sprintMinRatio.HasValue)
            {
                var text = fs.ReadText(PresetPath)!;
                var edited = text.Replace("\"walk_max_ratio\": 0.6,", "\"walk_max_ratio\": 0.6,\n        \"sprint_min_ratio\": " + sprintMinRatio.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",");
                Assert.NotEqual(text, edited);
                fs.WriteTextAtomic(PresetPath, edited);
            }

            fs.WriteTextAtomic("test/_m2b/feel/feel.weapon.json", WeaponJson);
            fs.WriteTextAtomic("test/_m2b/item/item.budget_curve.json", BudgetJson);
            fs.WriteTextAtomic("test/_m2b/item/item.quality_definition.json", QualityJson);
            fs.WriteTextAtomic("test/_m2b/item/item.slot_definition.json", SlotJson);
            fs.WriteTextAtomic("test/_m2b/item/item.template.json", ItemJson);
            fs.WriteTextAtomic("test/_m2b/display/display.map.json", DisplayJson);
            fs.WriteTextAtomic("test/_m2b/l10n/l10n.text.json", TextJson);

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
                GameId = new Id("game.m2b"),
                StepSeconds = Dt,
                EnableDiscreteTimeModel = true,
                FeelOptions = new CarriersFeelOptions { CalibrationId = Calibration },
            });
            Assert.False(world.LoadReport.IsBlocking, string.Join("\n", world.LoadReport.Issues));

            var engine = new StubEngine();
            var sceneRouter = new SceneRouter(
                world.Registry, engine.ResourceLoader, world.Gameplay.AppState, world.World, world.Gameplay.Hooks, world.Bus);
            var presentation = new PresentationAssembly(
                world.Gameplay, world.World, world.Registry, world.Bus, new RngHost(2),
                new Tests.PresentationViewBinding.FakeViewFactory(), engine.Renderer2D, engine.Camera, engine.Audio,
                engine.FileSystem, sceneRouter);
            return (world, presentation);
        }

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

        private Unit Player => _world.Player;

        private LocomotionGait Gait() => _presentation.Pose!.GetContext(PlayerId).Gait;

        private void Tick(Vec2? move, MoveMode mode)
        {
            if (move.HasValue) _world.Gameplay.Carriers.Movement.Request(MoveRequest.InDirection(PlayerId, move.Value, mode));
            _world.Gameplay.Advance(Dt);
            _world.Spatial.UpdatePosition(PlayerId, Player.Position);
        }

        /// <summary>跑 n 个 tick，逐 tick 记录（运动状态速度比, 选择器步态）。</summary>
        private List<(double Ratio, LocomotionGait Gait)> Run(int ticks, Vec2? move, MoveMode mode)
        {
            var trace = new List<(double, LocomotionGait)>(ticks);
            for (var i = 0; i < ticks; i++)
            {
                Tick(move, mode);
                trace.Add((Player.MovementState.Motion.SpeedRatio, Gait()));
            }

            return trace;
        }

        /// <summary>独立推演：用同一份呈现型视图的阈值，对速度比序列逐项求步态（首项无滞回定档，其后带滞回）。</summary>
        private List<LocomotionGait> Expected(IReadOnlyList<(double Ratio, LocomotionGait Gait)> trace)
        {
            var deriver = new GaitDeriver(GaitThresholds.FromView(_world.Gameplay.Feel!.Resolver.ResolvePresenting(PlayerId)));
            var result = new List<LocomotionGait>();
            for (var i = 0; i < trace.Count; i++)
            {
                result.Add(i == 0 ? deriver.Seed(trace[i].Ratio) : deriver.Update(trace[i].Ratio));
            }

            return result;
        }

        // ------------------------------------------------------------------ 步态喂入

        /// <summary>
        /// 复现用例：玩家按住移动（Run 模式）——速度比从 0 加速到稳态，选择器步态从未观测（缺省）经 Walk 升到 Run，松开后经 Walk 降回 Idle。
        /// 逐 tick 与独立推演一致；稳态步态由数据（阈值）与稳态速度比决定。
        /// </summary>
        [Fact]
        public void Gait_FollowsTheMovementSpeedRatio_UpThroughWalkToRun_AndBackToIdleAfterRelease()
        {
            Assert.Equal(LocomotionGait.Idle, Gait()); // 未动过的单位：选择器没有记账，缺省
            Assert.Equal(PoseContext.Empty, _presentation.Pose!.GetContext(PlayerId));

            var accel = Run(120, new Vec2(1, 0), MoveMode.Run);
            var gaits = accel.Select(t => t.Gait).Distinct().ToList();
            Assert.Contains(LocomotionGait.Walk, gaits);
            Assert.Equal(LocomotionGait.Run, accel[accel.Count - 1].Gait);

            var decel = Run(180, null, MoveMode.Run);
            Assert.Equal(0, Player.MovementState.Motion.SpeedRatio);
            Assert.Equal(LocomotionGait.Idle, decel[decel.Count - 1].Gait);
            Assert.Contains(LocomotionGait.Walk, decel.Select(t => t.Gait));

            // 独立推演：整段（加速 + 松开减速）速度比序列经同一份阈值逐项求步态，与选择器逐 tick 一致。
            // 注意减速到 0 之后选择器不再被观测（移出活跃集合），推演只比到首次降到 0 的那一 tick 为止。
            var all = accel.Concat(decel).ToList();
            var firstZero = all.FindIndex(t => t.Ratio <= 0 && t.Gait == LocomotionGait.Idle);
            var upToZero = all.Take(firstZero + 1).ToList();
            AssertEqualSequence(Expected(upToZero), upToZero);
            Assert.Equal(0, ((PoseGaitFeeder)GetFeeder()).ActiveCount); // 停稳后移出活跃集合
        }

        private object GetFeeder() => typeof(PresentationAssembly)
            .GetField("_gaitFeeder", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(_presentation)!;

        private static void AssertEqualSequence(IReadOnlyList<LocomotionGait> expected, IReadOnlyList<(double Ratio, LocomotionGait Gait)> actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.True(expected[i] == actual[i].Gait, $"tick {i} ratio={actual[i].Ratio}：期望 {expected[i]}，实际 {actual[i].Gait}");
            }
        }

        [Fact]
        public void Gait_WalkMode_SettlesAtWalkGait_BecauseTheSteadyRatioIsBelowTheWalkMax()
        {
            Run(120, new Vec2(1, 0), MoveMode.Walk);
            var view = _world.Gameplay.Feel!.Resolver.ResolvePresenting(PlayerId);
            var thresholds = GaitThresholds.FromView(view);
            var ratio = Player.MovementState.Motion.SpeedRatio;
            Assert.InRange(ratio, thresholds.IdleMaxRatio, thresholds.WalkMaxRatio);
            Assert.Equal(LocomotionGait.Walk, Gait());
        }

        [Fact]
        public void Gait_WhenTheProfileDeclaresSprint_FullSpeedResolvesToSprint_AndFallsBackToRunThenBaseWhenTheClipSetLacksTheKey()
        {
            _presentation.Dispose();
            var (world, presentation) = BuildRig(sprintMinRatio: 1.0);
            try
            {
                Gait(world, presentation); // 缺省
                for (var i = 0; i < 240; i++)
                {
                    world.Gameplay.Carriers.Movement.Request(MoveRequest.InDirection(PlayerId, new Vec2(1, 0), MoveMode.Run));
                    world.Gameplay.Advance(Dt);
                }

                var ratio = world.Player.MovementState.Motion.SpeedRatio;
                var context = presentation.Pose!.GetContext(PlayerId);
                var thresholds = GaitThresholds.FromView(world.Gameplay.Feel!.Resolver.ResolvePresenting(PlayerId));
                var expected = ratio >= thresholds.SprintMinRatio!.Value ? LocomotionGait.Sprint : LocomotionGait.Run;
                Assert.Equal(expected, context.Gait);
                Assert.Equal(LocomotionGait.Sprint, context.Gait);

                // 回落链：表里有 move.sprint 命中；只有 move.run 回落到 run；只有基础键 move 回落到基础键。
                var request = context.ToRequest(PoseKeys.GaitState, inCombat: false);
                Assert.Equal("move.sprint", PoseResolver.Resolve(request, k => k == "move.sprint" || k == "move.run" || k == "move").TableKey);
                Assert.Equal("move.run", PoseResolver.Resolve(request, k => k == "move.run" || k == "move").TableKey);
                Assert.Equal("move", PoseResolver.Resolve(request, k => k == "move").TableKey);
            }
            finally
            {
                presentation.Dispose();
            }
        }

        /// <summary>
        /// M5-S4（ADR-0147）复现用例：玩家按住移动，生产装配的移动呈现参数逐 tick 与规则公式一致——播放速率 = <c>stride_scale</c> × 速度比 ÷ 该步态的参考速度比
        /// （夹取、取整），松开停稳后复位为 1；开启 <c>lean_deg_per_accel</c> 后加速段前倾为正、减速段后仰为负，稳态趋近 0。
        /// </summary>
        [Fact]
        public void Locomotion_StrideRateAndLean_FollowTheRulesFormulas_AndResetWhenStopped()
        {
            var loco = _presentation.Locomotion!;
            _world.Gameplay.Feel!.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.LeanDegPerAccel, FeelOp.Set, FeelValue.Of(3.0)));
            _world.Gameplay.Feel.Resolver.InvalidateAll("test");
            var view = _world.Gameplay.Feel.Resolver.ResolvePresenting(PlayerId);
            var thresholds = GaitThresholds.FromView(view);
            var scale = view.GetRaw(FeelFieldNames.StrideScale).AsNumber();

            var maxLean = double.MinValue;
            var minLean = double.MaxValue;
            for (var i = 0; i < 120; i++)
            {
                Tick(new Vec2(1, 0), MoveMode.Run);
                var ratio = Player.MovementState.Motion.SpeedRatio;
                var gait = Gait();
                var expected = LocomotionPresentationMath.StrideRate(
                    ratio, LocomotionPresentationMath.ReferenceRatio(gait, thresholds.WalkMaxRatio, thresholds.SprintMinRatio), scale);
                Assert.Equal(expected, loco.GetStrideRate(PlayerId), 9);
                maxLean = Math.Max(maxLean, loco.GetLeanDeg(PlayerId));
            }

            Assert.True(maxLean > 0, "加速段应前倾");
            Assert.InRange(loco.GetLeanDeg(PlayerId), -LocomotionPresentationMath.LeanQuantum, 1.0); // 稳态：加速度 0，前倾回落

            for (var i = 0; i < 180; i++)
            {
                Tick(null, MoveMode.Run);
                minLean = Math.Min(minLean, loco.GetLeanDeg(PlayerId));
            }

            Assert.True(minLean < 0, "减速段应后仰");
            Assert.Equal(0, Player.MovementState.Motion.SpeedRatio);
            Assert.Equal(1.0, loco.GetStrideRate(PlayerId));
            Assert.Equal(0.0, loco.GetLeanDeg(PlayerId));
        }

        /// <summary>
        /// ADR-0147：起步/急停混合时长取自呈现型字段 <c>start_blend_ms</c>/<c>stop_blend_ms</c>（毫秒 → 秒）：Idle→Move 取起步值、Move→Idle 取急停值，
        /// 其它切换不给提示；改写字段后重算立即反映。期望值由同一份视图的字段值算出。
        /// </summary>
        [Fact]
        public void LocomotionBlends_FollowTheStartAndStopBlendFields()
        {
            var blends = new LocomotionBlends(_world.Gameplay.Feel!.Resolver);
            var overrides = _world.Gameplay.Feel.Feel.DebugOverrides!;
            overrides.SetGlobal(new FeelWrite(FeelFieldNames.StartBlendMs, FeelOp.Set, FeelValue.Of(120.0)));
            overrides.SetGlobal(new FeelWrite(FeelFieldNames.StopBlendMs, FeelOp.Set, FeelValue.Of(40.0)));
            _world.Gameplay.Feel.Resolver.InvalidateAll("test");
            var view = _world.Gameplay.Feel.Resolver.ResolvePresenting(PlayerId);

            Assert.True(blends.TryGetBlendSeconds(PlayerId, AnimState.Idle, AnimState.Move, out var start));
            Assert.Equal(view.GetRaw(FeelFieldNames.StartBlendMs).AsNumber() / 1000.0, start, 9);
            Assert.True(blends.TryGetBlendSeconds(PlayerId, AnimState.Move, AnimState.Idle, out var stop));
            Assert.Equal(view.GetRaw(FeelFieldNames.StopBlendMs).AsNumber() / 1000.0, stop, 9);
            Assert.Equal(0.12, start, 9);
            Assert.Equal(0.04, stop, 9);

            Assert.False(blends.TryGetBlendSeconds(PlayerId, AnimState.Idle, AnimState.Attack, out _));
            Assert.False(blends.TryGetBlendSeconds(PlayerId, AnimState.Attack, AnimState.Idle, out _));
            Assert.False(blends.TryGetBlendSeconds(PlayerId, AnimState.Move, AnimState.Move, out _));

            overrides.SetGlobal(new FeelWrite(FeelFieldNames.StartBlendMs, FeelOp.Set, FeelValue.Of(0.0)));
            _world.Gameplay.Feel.Resolver.InvalidateAll("test");
            Assert.True(blends.TryGetBlendSeconds(PlayerId, AnimState.Idle, AnimState.Move, out var hardCut));
            Assert.Equal(0.0, hardCut); // 0 = 硬切
        }

        private static void Gait(HeadlessWorld world, PresentationAssembly presentation) =>
            Assert.Equal(PoseContext.Empty, presentation.Pose!.GetContext(PlayerId));

        [Fact]
        public void Gait_FeederIsInertForUnitsThatNeverMoved_AndTheDefaultContextStaysEmpty()
        {
            // 不变量：静止单位不进选择器；武器族等其它维度不受步态喂入影响。
            for (var i = 0; i < 30; i++) Tick(null, MoveMode.Walk);
            Assert.Equal(PoseContext.Empty, _presentation.Pose!.GetContext(PlayerId));
            Assert.Equal(0, ((PoseGaitFeeder)GetFeeder()).ActiveCount);
        }

        // ------------------------------------------------------------------ 单位销毁时的武器族清理

        private void EquipSword()
        {
            var carriers = _world.Gameplay.Carriers;
            Assert.True(carriers.Inventory.AddItem(PlayerId, new Id("item.m2b_sword"), 1));
            Id? instance = null;
            foreach (var item in carriers.Inventory.ListItems(PlayerId))
            {
                if (item.TemplateId.Value == "item.m2b_sword") instance = item.InstanceId;
            }

            Assert.True(carriers.Equipment.Equip(PlayerId, instance!.Value, MainHandSlot).Success);
            Tick(null, MoveMode.Walk);
        }

        /// <summary>
        /// 复现用例：装备单手剑 → 姿势族 1h；单位销毁事件之后选择器的族清空（1h → 无）；同 id 重建（实体创建事件）后换装链重新对账，族补回 1h。
        /// 此前销毁后选择器残留旧族、链也认为"未变"而不重发，同 id 重建的单位表现侧族与装备不一致。
        /// </summary>
        [Fact]
        public void PoseFamily_IsClearedOnUnitDestroy_AndRestoredWhenTheSameIdIsRecreated()
        {
            EquipSword();
            var family = _world.Registry.Get("feel.weapon", "feel.weapon.m2b_sword")!.GetString("family");
            Assert.Equal(family, _presentation.Pose!.GetContext(PlayerId).Family);

            _world.Bus.PublishImmediate(new EntityDestroyedEvent(PlayerId));
            Assert.Null(_presentation.Pose.GetContext(PlayerId).Family);

            _world.Bus.PublishImmediate(new EntityCreatedEvent(PlayerId, "unit", new Id("display.lab_player")));
            Tick(null, MoveMode.Walk); // 链用 Enqueue 发布 feel.weapon_changed，在固定步内派发
            Assert.Equal(family, _presentation.Pose.GetContext(PlayerId).Family);
        }

        /// <summary>不变量：读档（save.loaded）对账后族与装备一致——选择器此前被清掉时由对账补回，没清时不重复发事件。</summary>
        [Fact]
        public void PoseFamily_SaveLoadedReconciliation_StaysCorrectAfterTheDestroyCleanup()
        {
            EquipSword();
            var family = _world.Registry.Get("feel.weapon", "feel.weapon.m2b_sword")!.GetString("family");

            _world.Bus.PublishImmediate(new EntityDestroyedEvent(PlayerId));
            Assert.Null(_presentation.Pose!.GetContext(PlayerId).Family);

            _world.Bus.PublishImmediate(new SaveLoadedEvent(new Id("save.slot_m2b")));
            Tick(null, MoveMode.Walk);
            Assert.Equal(family, _presentation.Pose.GetContext(PlayerId).Family);

            var changed = 0;
            using var sub = new Subscription(_world.Bus, () => changed++);
            _world.Bus.PublishImmediate(new SaveLoadedEvent(new Id("save.slot_m2b")));
            Tick(null, MoveMode.Walk);
            Assert.Equal(0, changed); // 没有变化不重发
            Assert.Equal(family, _presentation.Pose.GetContext(PlayerId).Family);
        }

        private sealed class Subscription : IDisposable
        {
            private readonly SubscriptionHandle _handle;

            public Subscription(IEventBus bus, Action onChanged) =>
                _handle = bus.Subscribe<FeelWeaponChangedEvent>(RulesEventKeys.FeelWeaponChanged, _ => onChanged());

            public void Dispose() => _handle.Dispose();
        }
    }
}
