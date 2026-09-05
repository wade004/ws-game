using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Presentation.Camera;
using Presentation.Camera.Schema;
using Xunit;

namespace Tests.PresentationCamera
{
    /// <summary>
    /// <see cref="CameraProfile.FromRecord"/>/<see cref="CameraSchemas"/> 的单元测试（P4-2 契约缺口
    /// 最小修补：`camera_profile` 表此前只有 schema 登记，没有 `DataRegistry`/`FromRecord` 数据行
    /// 解析器，见 <c>presentation/camera/schema/README.md</c>"本模块不做什么"一节）。测试夹具风格同
    /// <c>Core.Foundation.DisplayInfo</c> 的 <c>DisplayInfoTestSupport</c>/
    /// <c>DisplayInfoFromRecordTests</c> 惯例。
    /// </summary>
    public class CameraProfileFromRecordTests
    {
        private static IEventBus CreateBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
            });
            return new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
        }

        private static (IDataRegistry Registry, ValidationReport Report) BuildRegistry(string rowsJson)
        {
            var source = new InMemoryDataSource();
            source.Add("camera_profile", "{\"table\": \"camera_profile\", \"schema_version\": 1, \"rows\": " + rowsJson + "}");

            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, CreateBus());
            registry.RegisterSchema(CameraSchemas.Profile);

            var report = registry.LoadAll();
            return (registry, report);
        }

        private const string FullProfileRow = @"
        {
          ""id"": ""camera_profile.default_overworld"",
          ""pitch_degrees"": 55.0,
          ""yaw_degrees"": 0.0,
          ""zoom_min"": 5.0,
          ""zoom_max"": 15.0,
          ""zoom_default"": 10.0,
          ""follow_lerp"": 0.15,
          ""bounds"": {""min"": {""x"": -50.0, ""y"": -50.0}, ""max"": {""x"": 50.0, ""y"": 50.0}},
          ""shake_presets"": [
            {""id"": ""camera_profile.shake_hit"", ""amplitude"": 0.3, ""duration"": 0.2, ""frequency"": 20.0},
            {""id"": ""camera_profile.shake_explosion"", ""amplitude"": 1.0, ""duration"": 0.5}
          ]
        }";

        private const string MinimalProfileRow = @"
        {
          ""id"": ""camera_profile.minimal"",
          ""pitch_degrees"": 45.0,
          ""yaw_degrees"": 0.0,
          ""zoom_min"": 1.0,
          ""zoom_max"": 2.0,
          ""zoom_default"": 1.5,
          ""follow_lerp"": 0.1
        }";

        [Fact]
        public void FromRecord_FullRow_ParsesAllFieldsIncludingBoundsAndShakePresets()
        {
            var (registry, report) = BuildRegistry("[" + FullProfileRow + "]");
            Assert.False(report.IsBlocking);

            var record = registry.Get("camera_profile", "camera_profile.default_overworld")!;
            var profile = CameraProfile.FromRecord(record);

            Assert.Equal(new Id("camera_profile.default_overworld"), profile.Id);
            Assert.Equal(55.0, profile.PitchDegrees);
            Assert.Equal(0.0, profile.YawDegrees);
            Assert.Equal(5.0, profile.ZoomMin);
            Assert.Equal(15.0, profile.ZoomMax);
            Assert.Equal(10.0, profile.ZoomDefault);
            Assert.Equal(0.15, profile.FollowLerp);

            Assert.NotNull(profile.Bounds);
            Assert.Equal(new Vec2(-50.0, -50.0), profile.Bounds!.Value.Min);
            Assert.Equal(new Vec2(50.0, 50.0), profile.Bounds.Value.Max);

            Assert.Equal(2, profile.ShakePresets.Count);
            Assert.Equal(new Id("camera_profile.shake_hit"), profile.ShakePresets[0].Id);
            Assert.Equal(0.3, profile.ShakePresets[0].Amplitude);
            Assert.Equal(0.2, profile.ShakePresets[0].Duration);
            Assert.Equal(20.0, profile.ShakePresets[0].Frequency);

            // 判断记录：第二条未声明 frequency，按 0.0 兜底（09 第 3.5 节字段未规定缺省值，见
            // CameraProfile.FromRecord 判断记录）。
            Assert.Equal(new Id("camera_profile.shake_explosion"), profile.ShakePresets[1].Id);
            Assert.Equal(0.0, profile.ShakePresets[1].Frequency);
        }

        [Fact]
        public void FromRecord_MinimalRow_BoundsNullAndShakePresetsEmpty()
        {
            var (registry, report) = BuildRegistry("[" + MinimalProfileRow + "]");
            Assert.False(report.IsBlocking);

            var record = registry.Get("camera_profile", "camera_profile.minimal")!;
            var profile = CameraProfile.FromRecord(record);

            Assert.Equal(new Id("camera_profile.minimal"), profile.Id);
            Assert.Null(profile.Bounds);
            Assert.Empty(profile.ShakePresets);
        }
    }
}
