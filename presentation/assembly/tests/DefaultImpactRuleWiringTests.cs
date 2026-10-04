// DefaultImpactRuleWiringTests：镜头与音画反馈（ADR-0148）经生产装配（HeadlessWorldBuilder → GameplayAssembly → PresentationAssembly）后的运行时冒烟。
// 同一个装配好的世界、真实输入映射、真实时间线动作：游戏数据里没有任何 play_impact 规则时，装配根内置的缺省规则
// （combat.hit_confirmed → PlayImpact(from_feel)）让手感反馈包照常出镜头冲击；游戏自己写了规则就只有游戏的一条（不叠加）；
// DefaultImpactRule=false 完全关闭。玩家强度系数在装配出口生效。期望值全部由手感解析出的字段与规则公式算出，不写死裸数。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Foundation.Rng;
using Core.Foundation.SceneRouter;
using Core.Sim;
using Presentation.Assembly;
using Presentation.Camera;
using Presentation.FeedbackBinder.Core;
using Xunit;

namespace Tests.Presentation.Assembly
{
    public sealed class DefaultImpactRuleWiringTests
    {
        private const double Dt = 0.02;
        private const string Calibration = "feel.calibration.framework_default";
        private static readonly Id PlayerId = new Id("unit.lab_player");
        private static readonly Id MapId = new Id("world.lab_arena");
        private static readonly Id StakeTemplate = new Id("creature.lab_stake");
        private static readonly Id SwingSkill = new Id("skill.av_swing");
        private const string ProfileId = "feedback.impact_profile.av";
        private const double ProfileImpulseGain = 0.5;

        private const string SkillJson = @"{
  ""table"": ""skill.def"", ""schema_version"": 1,
  ""rows"": [ {
    ""id"": ""skill.av_swing"", ""school"": ""school.physical"", ""kind"": ""active"", ""range"": 3, ""cast_time"": 0.4,
    ""cooldown_duration"": 0, ""respects_gcd"": false, ""target_shape_ref"": ""target.chain.lab_nearest_enemy"",
    ""timeline"": { ""startup_ms"": 100, ""active_ms"": 60, ""recovery_ms"": 240, ""markers"": [ { ""name"": ""hit"", ""at_ms"": 100 } ] },
    ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 10, ""scaling"": [ { ""stat"": ""stat.attack_power"", ""coefficient"": 1.0 } ], ""school"": ""school.physical"" } } ]
  } ]
}";

        private const string ActionJson = @"{
  ""table"": ""found.input_action"", ""schema_version"": 1,
  ""rows"": [ { ""key"": ""input.action.av_attack"", ""kind"": ""button"", ""default_bindings"": [ ""key:j"" ], ""rebind_group"": ""default"",
                ""class"": ""attack"", ""buffer_ms"": 160, ""skill_slot"": ""slot_0"" } ]
}";

        private const string ImpactProfileJson = @"{
  ""table"": ""feedback.impact_profile"", ""schema_version"": 1,
  ""rows"": [
    { ""id"": ""feedback.impact_profile.av"", ""variants"": [
      { ""class"": ""medium"", ""outcome"": ""hit"", ""camera"": { ""impulse_gain"": 0.5, ""decay_ms"": 120 } } ] } ]
}";

        private const string GameBindingJson = @"{
  ""table"": ""feedback.binding"", ""schema_version"": 1,
  ""rows"": [ { ""id"": ""feedback.av_game_impact"", ""event"": ""combat.hit_confirmed"", ""actions"": [ { ""kind"": ""play_impact"", ""params"": {} } ] } ]
}";

        private sealed class ImpulseCamera : ICamera, ICameraImpulse
        {
            private readonly StubCamera _inner = new StubCamera();
            public readonly List<(Vec2 Direction, double Magnitude, double DecayMs)> Impulses = new List<(Vec2, double, double)>();
            public bool SupportsCameraImpulse => true;
            public void Impulse(Vec2 direction, double magnitude, double decayMs) => Impulses.Add((direction, magnitude, decayMs));
            public void Configure(double pitchDegrees, double yawDegrees, ZoomRange zoomRange) => _inner.Configure(pitchDegrees, yawDegrees, zoomRange);
            public void Follow(Vec2 planePos, double smoothing) => _inner.Follow(planePos, smoothing);
            public void SetZoom(double zoom) => _inner.SetZoom(zoom);
            public Vec2 WorldToScreen(Vec2 planePos, double height) => _inner.WorldToScreen(planePos, height);
            public Vec2? ScreenToWorld(Vec2 screen) => _inner.ScreenToWorld(screen);
            public void Shake(double intensity, double durationSeconds, double frequency) => _inner.Shake(intensity, durationSeconds, frequency);
        }

        private sealed class Rig : IDisposable
        {
            public HeadlessWorld World = null!;
            public PresentationAssembly Presentation = null!;
            public ImpulseCamera Camera = new ImpulseCamera();
            public StubInput Input = new StubInput();

            public void Dispose() => Presentation.Dispose();

            public void Step()
            {
                Presentation.InputMap.Update(Input);
                World.Gameplay.Advance(Dt);
                World.Spatial.UpdatePosition(PlayerId, World.Player.Position);
            }

            /// <summary>敌人在 1.5 外，点按普攻并跑完整个动作。</summary>
            public void SwingOnce()
            {
                var pos = new Vec2(1.5, 0);
                var id = World.Gameplay.Carriers.Creatures.Spawn(StakeTemplate, MapId, pos, Math.PI, null, 1);
                World.Spatial.Register(id, pos, 0.5);
                var ai = World.Gameplay.Carriers.Rules.Ai;
                foreach (var registered in new List<Id>(ai.RegisteredUnitIds))
                {
                    if (registered.Equals(id)) { ai.UnregisterUnit(id); break; }
                }
                Step();
                var feel = World.Gameplay.Feel!;
                feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.ImpactProfileRef, FeelOp.Set, FeelValue.Of(ProfileId)));
                Input.Press("j");
                Step();
                Input.Release("j");
                var ticks = FeelCalibration.MillisecondsToTicks(100 + 60 + 240, Dt) + 20;
                for (var i = 0; i < ticks; i++) Step();
            }
        }

        private static Rig Build(bool gameBinding, bool? defaultRule = null)
        {
            var fs = new StubFileSystem();
            CopyDisk(fs, "data/_framework");
            CopyDisk(fs, "data/_feel");
            CopyDisk(fs, "data/_lab");
            fs.WriteTextAtomic("test/_av/skill/skill.def.json", SkillJson);
            fs.WriteTextAtomic("test/_av/found/found.input_action.json", ActionJson);
            fs.WriteTextAtomic("test/_av/feedback/feedback.impact_profile.json", ImpactProfileJson);
            if (gameBinding)
            {
                fs.WriteTextAtomic("test/_av/feedback/feedback.binding.json", GameBindingJson);
            }

            var rig = new Rig();
            rig.World = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = new List<IDataSource>
                {
                    new FileSystemDataSource(fs, "data/_framework"),
                    new FileSystemDataSource(fs, "data/_feel"),
                    new FileSystemDataSource(fs, "data/_lab"),
                    new FileSystemDataSource(fs, "test/_av"),
                },
                FileSystem = fs,
                Seed = 20261004UL,
                MapId = MapId,
                PlayerId = PlayerId,
                PlayerFactionId = new Id("fac.player"),
                PlayerClassId = new Id("arch.class.lab_hero"),
                PlayerLevel = 1,
                GameId = new Id("game.av"),
                StepSeconds = Dt,
                EnableDiscreteTimeModel = true,
                FeelOptions = new CarriersFeelOptions { CalibrationId = Calibration },
            });
            Assert.False(rig.World.LoadReport.IsBlocking, string.Join("\n", rig.World.LoadReport.Issues));

            var engine = new StubEngine();
            var sceneRouter = new SceneRouter(
                rig.World.Registry, engine.ResourceLoader, rig.World.Gameplay.AppState, rig.World.World, rig.World.Gameplay.Hooks, rig.World.Bus);
            var options = new PresentationAssemblyOptions();
            if (defaultRule.HasValue)
            {
                options.DefaultImpactRule = defaultRule.Value;
            }
            rig.Presentation = new PresentationAssembly(
                rig.World.Gameplay, rig.World.World, rig.World.Registry, rig.World.Bus, new RngHost(2),
                new Tests.PresentationViewBinding.FakeViewFactory(), engine.Renderer2D, rig.Camera, engine.Audio, engine.FileSystem, sceneRouter,
                options, hitFrameSource: new CharacterRigHitFrameSource());

            var definitions = new List<ActionDefinition>();
            foreach (var record in rig.World.Registry.GetAll("found.input_action")) definitions.Add(ActionDefinition.FromRecord(record));
            rig.Presentation.InputMap.DeclareActionSet(new Id("actionset.av"), definitions);
            rig.World.Gameplay.Carriers.Rules.Skill.LearnSkill(PlayerId, SwingSkill);
            Assert.True(rig.World.Gameplay.Carriers.SkillBindings.Bind(PlayerId, "slot_0", SwingSkill));
            return rig;
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

        private static double ExpectedImpulse(Rig rig)
        {
            var presenting = rig.World.Gameplay.Feel!.Resolver.ResolvePresenting(PlayerId);
            var baseGain = presenting.GetRaw(FeelFieldNames.CameraImpulseGain).AsNumber();
            var cap = presenting.GetRaw(FeelFieldNames.CameraShakeCap).AsNumber();
            return Math.Min(baseGain * ProfileImpulseGain, cap);
        }

        // ------------------------------------------------------------------

        [Fact]
        public void NoGameRule_TheBuiltInDefaultRuleStillPlaysTheFeelProfile()
        {
            // 复现：游戏数据里没有 play_impact 规则、手感又启用时，旧实现里整个反馈包体系静默不工作（镜头冲击恒为 0）。
            using var rig = Build(gameBinding: false);
            rig.SwingOnce();

            var expected = ExpectedImpulse(rig);
            Assert.True(expected > 0);
            var impulse = Assert.Single(rig.Camera.Impulses);
            Assert.Equal(expected, impulse.Magnitude, 9);
        }

        [Fact]
        public void DefaultRuleOff_NoGameRule_PlaysNothing()
        {
            using var rig = Build(gameBinding: false, defaultRule: false);
            rig.SwingOnce();

            Assert.Empty(rig.Camera.Impulses);
        }

        [Fact]
        public void GameOwnRule_Wins_AndTheBuiltInRuleIsNotStackedOnTop()
        {
            using var rig = Build(gameBinding: true);
            rig.SwingOnce();

            // 只有一次冲击：内置规则没有叠加（否则同一命中会播两次）。
            var impulse = Assert.Single(rig.Camera.Impulses);
            Assert.Equal(ExpectedImpulse(rig), impulse.Magnitude, 9);
        }

        [Theory]
        [InlineData(0.5)]
        [InlineData(0.0)]
        public void UserImpulseIntensity_IsAppliedAtTheProductionCameraExit(double coefficient)
        {
            using var rig = Build(gameBinding: false);
            rig.Presentation.FeelIntensity.Set(FeelIntensityKind.Impulse, coefficient);
            rig.SwingOnce();

            if (coefficient <= 0)
            {
                Assert.Empty(rig.Camera.Impulses);
                return;
            }

            // 帧内合成上限与冲击合并都不影响单次冲击：幅度 = 规则幅度 × 玩家强度系数。
            var impulse = Assert.Single(rig.Camera.Impulses);
            Assert.Equal(ExpectedImpulse(rig) * coefficient, impulse.Magnitude, 9);
        }

        [Fact]
        public void MarkerDirector_IsAssembledOnlyWhenFeelIsOnAndARigMarkerSourceIsProvided()
        {
            using var withSource = Build(gameBinding: false);
            Assert.NotNull(withSource.Presentation.AnimMarkers);
            Assert.NotNull(withSource.Presentation.Feedback.MarkerGate);
            Assert.NotNull(withSource.Presentation.Stride.Suppress);
        }
    }
}
