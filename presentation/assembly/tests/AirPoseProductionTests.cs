// AirPoseProductionTests：空中姿势（ADR-0130 追加决定）经生产装配（HeadlessWorldBuilder -> GameplayAssembly -> PresentationAssembly）的运行时冒烟：
// 世界装配了竖直轴时，竖直运动的上升/下降/落地被喂进姿势选择器；没有竖直轴时选择器的空中阶段恒为 None（与改动前一致）。
// 期望值由抛体公式算出（落地 tick = ceil(2·v0/g/dt)），不写死裸数。
using System;
using System.Collections.Generic;
using System.IO;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;
using Core.Sim;
using Presentation.Assembly;
using Presentation.Render;
using Xunit;

namespace Tests.Presentation.Assembly
{
    public sealed class AirPoseProductionTests : IDisposable
    {
        private const double Dt = 1.0 / 60.0;
        private const string Calibration = "feel.calibration.framework_default";
        private static readonly Id PlayerId = new Id("unit.lab_player");
        private static readonly Id MapId = new Id("world.lab_arena");

        private HeadlessWorld _world = null!;
        private PresentationAssembly _presentation = null!;

        public void Dispose() => _presentation?.Dispose();

        private void Build(bool vertical, double gravity = 24.0)
        {
            var fs = new StubFileSystem();
            CopyDisk(fs, "data/_framework");
            CopyDisk(fs, "data/_feel");
            CopyDisk(fs, "data/_lab");
            _world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = new List<IDataSource>
                {
                    new FileSystemDataSource(fs, "data/_framework"),
                    new FileSystemDataSource(fs, "data/_feel"),
                    new FileSystemDataSource(fs, "data/_lab"),
                },
                FileSystem = fs,
                Seed = 20261003UL,
                MapId = MapId,
                PlayerId = PlayerId,
                PlayerFactionId = new Id("fac.player"),
                PlayerClassId = new Id("arch.class.lab_hero"),
                PlayerLevel = 1,
                GameId = new Id("game.air_pose"),
                StepSeconds = Dt,
                EnableDiscreteTimeModel = true,
                FeelOptions = new CarriersFeelOptions { CalibrationId = Calibration },
                MovementOptions = vertical ? new MovementOptions { Vertical = new VerticalAxisOptions { Gravity = gravity } } : null,
            });
            Assert.False(_world.LoadReport.IsBlocking, string.Join("\n", _world.LoadReport.Issues));

            var engine = new StubEngine();
            var sceneRouter = new SceneRouter(
                _world.Registry, engine.ResourceLoader, _world.Gameplay.AppState, _world.World, _world.Gameplay.Hooks, _world.Bus);
            _presentation = new PresentationAssembly(
                _world.Gameplay, _world.World, _world.Registry, _world.Bus, new RngHost(2),
                new Tests.PresentationViewBinding.FakeViewFactory(), engine.Renderer2D, engine.Camera, engine.Audio,
                engine.FileSystem, sceneRouter);
        }

        private AirPhase Phase() => _presentation.Pose!.GetContext(PlayerId).Air;

        [Fact]
        public void Launch_RunsThroughRiseThenFallThenLandWindowThenNone_OnTheComputedTicks()
        {
            const double v0 = 6.1;
            const double g = 24.0;
            Build(vertical: true, gravity: g);
            Assert.Equal(AirPhase.None, Phase());
            Assert.True(_world.Gameplay.Carriers.VerticalMotion!.Launch(PlayerId, v0));

            var landTick = (int)Math.Ceiling(2.0 * v0 / g / Dt - 1e-9);
            var apexTick = (int)Math.Ceiling(v0 / g / Dt - 1e-9); // 第一个速度 <= 0 的 tick
            var phases = new List<AirPhase>();
            for (var tick = 1; tick <= landTick + AirPoseFeeder.DefaultLandHoldTicks + 2; tick++)
            {
                _world.Gameplay.Advance(Dt);
                phases.Add(Phase());
            }

            for (var tick = 1; tick < landTick; tick++)
            {
                var speed = v0 - g * tick * Dt;
                Assert.Equal(speed > 0.0 ? AirPhase.Rise : AirPhase.Fall, phases[tick - 1]);
            }

            Assert.Equal(AirPhase.Rise, phases[0]);
            Assert.Equal(AirPhase.Rise, phases[apexTick - 2]);
            Assert.Equal(AirPhase.Fall, phases[apexTick - 1]);
            for (var i = 0; i < AirPoseFeeder.DefaultLandHoldTicks; i++)
            {
                Assert.Equal(AirPhase.Land, phases[landTick - 1 + i]);
            }

            Assert.Equal(AirPhase.None, phases[landTick - 1 + AirPoseFeeder.DefaultLandHoldTicks]);
        }

        [Fact]
        public void WithoutTheVerticalAxis_TheAirPhaseStaysNone_AndNoFeederIsAssembled()
        {
            Build(vertical: false);
            Assert.Null(_world.Gameplay.Carriers.VerticalMotion);
            for (var i = 0; i < 30; i++) _world.Gameplay.Advance(Dt);
            Assert.Equal(PoseContext.Empty, _presentation.Pose!.GetContext(PlayerId));
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

        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException());
            for (var i = 0; i < 3; i++) dir = dir.Parent ?? throw new InvalidOperationException();
            return dir.FullName;
        }
    }
}
