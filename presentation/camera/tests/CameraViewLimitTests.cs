using System;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Presentation.Camera;
using Presentation.Camera.Schema;
using Presentation.VfxSfx.Contracts;
using Xunit;

namespace Tests.PresentationCamera
{
    /// <summary>
    /// 镜头可视范围限制（ADR-0181，<c>camera_profile.view_limit</c>）：声明后镜头宿主把跟随点收在"画面可视的地面区域不越出限制矩形"的区间内；
    /// 缺省关闭（行为与没有这个字段时逐位一致）；适配层不支持时不生效并记一条诊断。期望位置由限制矩形与桩相机的可视半宽半高（缩放 × 宽高比）算出。
    /// </summary>
    public class CameraViewLimitTests
    {
        private static readonly Id ProfileId = new Id("camera_profile.view_limit");

        private static readonly Vec2 MapMin = new Vec2(0, 0);
        private static readonly Vec2 MapMax = new Vec2(60, 40);
        private const double Aspect = 16.0 / 9.0;
        private const double Zoom = 8.0;

        private static CameraProfile Profile(CameraBounds? viewLimit, CameraBounds? bounds = null) =>
            new CameraProfile(ProfileId, 55, 0, zoomMin: 4, zoomMax: 30, zoomDefault: Zoom, followLerp: 0.2, bounds, shakePresets: null, viewLimit);

        private static CameraBounds Map => new CameraBounds(MapMin, MapMax);

        private static (CameraHost Host, StubCamera Camera, FakeFollowTarget Target, PresentationDiagnosticsRecorder Diagnostics) Build(
            CameraBounds? viewLimit, double aspect = Aspect, CameraBounds? bounds = null)
        {
            var camera = new StubCamera { ViewAspect = aspect };
            var target = new FakeFollowTarget();
            var diagnostics = new PresentationDiagnosticsRecorder();
            var host = new CameraHost(camera, target, bus: null, options: null, diagnostics);
            host.Configure(Profile(viewLimit, bounds));
            host.Follow(new Id("unit.player_1"));
            return (host, camera, target, diagnostics);
        }

        private static (Vec2 Min, Vec2 Max) Visible(StubCamera camera)
        {
            Assert.True(camera.TryGetGroundFootprint(out var lo, out var hi));
            return (camera.FollowTarget + lo, camera.FollowTarget + hi);
        }

        [Fact]
        public void Update_TargetNearEachCorner_VisibleRectangleStaysInsideTheMap()
        {
            var (host, camera, target, _) = Build(Map);
            var halfW = Zoom * Aspect;
            var halfH = Zoom;

            // 目标站在地图四个角（含越出地图一截），可视范围的四条边都不得越出地图矩形。
            foreach (var corner in new[]
            {
                new Vec2(MapMin.X, MapMin.Y), new Vec2(MapMax.X, MapMin.Y), new Vec2(MapMin.X, MapMax.Y), new Vec2(MapMax.X, MapMax.Y),
                new Vec2(MapMin.X - 30, MapMin.Y - 30), new Vec2(MapMax.X + 30, MapMax.Y + 30),
            })
            {
                target.NextPosition = corner;
                host.Update(0.0);
                var (lo, hi) = Visible(camera);
                Assert.True(lo.X >= MapMin.X - 1e-9 && lo.Y >= MapMin.Y - 1e-9, $"{corner}: {lo}");
                Assert.True(hi.X <= MapMax.X + 1e-9 && hi.Y <= MapMax.Y + 1e-9, $"{corner}: {hi}");
            }

            // 贴角时跟随点恰在 "地图角 ± 可视半宽半高"。
            target.NextPosition = new Vec2(MapMax.X + 5, MapMax.Y + 5);
            host.Update(0.0);
            Assert.Equal(new Vec2(MapMax.X - halfW, MapMax.Y - halfH), camera.FollowTarget);
            target.NextPosition = new Vec2(MapMin.X - 5, MapMin.Y - 5);
            host.Update(0.0);
            Assert.Equal(new Vec2(MapMin.X + halfW, MapMin.Y + halfH), camera.FollowTarget);
        }

        [Fact]
        public void Update_TargetWellInsideTheMap_IsNotMoved()
        {
            var (host, camera, target, _) = Build(Map);
            target.NextPosition = new Vec2((MapMin.X + MapMax.X) / 2, (MapMin.Y + MapMax.Y) / 2);

            host.Update(0.0);

            Assert.Equal(target.NextPosition, camera.FollowTarget);
        }

        [Fact]
        public void Update_AspectOrZoomChanges_LimitFollowsWithoutRecomputingAnyData()
        {
            var (host, camera, target, _) = Build(Map);
            target.NextPosition = new Vec2(MapMax.X, MapMax.Y);

            foreach (var aspect in new[] { 4.0 / 3.0, 16.0 / 9.0, 21.0 / 9.0 })
            {
                camera.ViewAspect = aspect;
                foreach (var zoom in new[] { 5.0, 8.0, 12.0 })
                {
                    host.SetZoom(zoom);
                    host.Update(0.0);
                    var (lo, hi) = Visible(camera);
                    Assert.Equal(MapMax.X, hi.X, 9); // 可视范围贴着右上边界，不越出也不留空
                    Assert.Equal(MapMax.Y, hi.Y, 9);
                    Assert.True(lo.X >= MapMin.X - 1e-9 && lo.Y >= MapMin.Y - 1e-9);
                }
            }
        }

        [Fact]
        public void Update_ViewLargerThanTheMapOnAnAxis_CentersTheMapOnThatAxis()
        {
            var (host, camera, target, _) = Build(Map);
            host.SetZoom(30); // 半高 30 → 画面高 60 > 地图高 40；半宽 30 × 16/9 ≈ 53 → 画面宽 ≈ 107 > 地图宽 60
            target.NextPosition = new Vec2(MapMax.X, MapMax.Y);

            host.Update(0.0);

            Assert.Equal((MapMin.X + MapMax.X) / 2, camera.FollowTarget.X, 9);
            Assert.Equal((MapMin.Y + MapMax.Y) / 2, camera.FollowTarget.Y, 9);
        }

        [Fact]
        public void Update_WithoutViewLimit_BehavesExactlyAsBefore_EvenIfTheAdapterCouldAnswer()
        {
            var (host, camera, target, diagnostics) = Build(viewLimit: null);
            target.NextPosition = new Vec2(MapMax.X + 100, MapMin.Y - 100);

            host.Update(0.0);

            Assert.Equal(target.NextPosition, camera.FollowTarget);
            Assert.Empty(diagnostics.Warnings);
        }

        [Fact]
        public void Update_AdapterWithoutFootprintSupport_LeavesPositionAndWarnsOnlyOnce()
        {
            var (host, camera, target, diagnostics) = Build(Map, aspect: 0);
            target.NextPosition = new Vec2(MapMax.X + 100, MapMax.Y + 100);

            host.Update(0.0);
            host.Update(0.0);
            host.Update(0.0);

            Assert.Equal(target.NextPosition, camera.FollowTarget);
            var warning = Assert.Single(diagnostics.Warnings);
            Assert.Contains("view_limit", warning);
        }

        [Fact]
        public void Update_BoundsAndViewLimitBoth_ViewLimitWinsAfterBoundsClamp()
        {
            // bounds 比地图宽松（越出地图 100），view_limit 仍保证画面不出地图。
            var bounds = new CameraBounds(new Vec2(MapMin.X - 100, MapMin.Y - 100), new Vec2(MapMax.X + 100, MapMax.Y + 100));
            var (host, camera, target, _) = Build(Map, bounds: bounds);
            target.NextPosition = new Vec2(MapMax.X + 50, MapMax.Y + 50);

            host.Update(0.0);

            var (_, hi) = Visible(camera);
            Assert.Equal(MapMax.X, hi.X, 9);
            Assert.Equal(MapMax.Y, hi.Y, 9);
        }

        [Fact]
        public void ConstrainView_Invariant_ResultAlwaysKeepsTheVisibleRectangleInsideOrCentered()
        {
            var limit = Map;
            var rng = new Random(20261010);
            for (var i = 0; i < 500; i++)
            {
                var halfW = rng.NextDouble() * 50;
                var halfH = rng.NextDouble() * 50;
                var focus = new Vec2(rng.NextDouble() * 200 - 70, rng.NextDouble() * 200 - 70);
                var result = limit.ConstrainView(focus, new Vec2(-halfW, -halfH), new Vec2(halfW, halfH));

                foreach (var axis in new[] { 0, 1 })
                {
                    var min = axis == 0 ? MapMin.X : MapMin.Y;
                    var max = axis == 0 ? MapMax.X : MapMax.Y;
                    var half = axis == 0 ? halfW : halfH;
                    var c = axis == 0 ? result.X : result.Y;
                    if (half * 2 <= max - min)
                    {
                        Assert.True(c - half >= min - 1e-9 && c + half <= max + 1e-9, $"axis {axis}: {c} half {half}");
                    }
                    else
                    {
                        Assert.Equal((min + max) / 2, c, 9);
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // 数据：view_limit 字段解析与校验
        // ------------------------------------------------------------------

        private static CameraProfile ParseRow(string extra)
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
            });
            var source = new InMemoryDataSource();
            source.Add("camera_profile",
                "{\"table\":\"camera_profile\",\"schema_version\":1,\"rows\":[{\"id\":\"camera_profile.vl\",\"pitch_degrees\":55,\"yaw_degrees\":0," +
                "\"zoom_min\":5,\"zoom_max\":15,\"zoom_default\":10,\"follow_lerp\":0.15" + extra + "}]}");
            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, new EventBus(catalog, new EventBusOptions { StrictCatalog = false }));
            registry.RegisterSchema(CameraSchemas.Profile);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking);
            return CameraProfile.FromRecord(registry.Get("camera_profile", "camera_profile.vl")!);
        }

        [Fact]
        public void FromRecord_ViewLimit_ParsedWhenPresent_NullWhenAbsent()
        {
            Assert.Null(ParseRow(string.Empty).ViewLimit);

            var profile = ParseRow(",\"view_limit\":{\"min\":{\"x\":-1,\"y\":-2},\"max\":{\"x\":30,\"y\":40}}");
            Assert.NotNull(profile.ViewLimit);
            Assert.Equal(new Vec2(-1, -2), profile.ViewLimit!.Value.Min);
            Assert.Equal(new Vec2(30, 40), profile.ViewLimit.Value.Max);
        }

        [Fact]
        public void FromRecord_ViewLimitMinGreaterThanMax_Throws()
        {
            Assert.Throws<DataFieldException>(() => ParseRow(",\"view_limit\":{\"min\":{\"x\":10,\"y\":0},\"max\":{\"x\":5,\"y\":9}}"));
        }
    }
}
