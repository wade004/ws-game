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

        // ------------------------------------------------------------------
        // T-M14（ADR-0125）：错误路径。直接用 DataRecord 构造函数喂绕过校验的坏行。
        // ------------------------------------------------------------------

        private static DataRecord Raw(string json) =>
            new DataRecord(CameraSchemas.Profile, "camera_profile.t", null,
                (Core.Foundation.Common.Json.JsonObject)Core.Foundation.Common.Json.JsonReader.Parse(json));

        private static string Row(string? drop = null, string? replaceKey = null, string? replaceValueJson = null, string extra = "")
        {
            var fields = new Dictionary<string, string>
            {
                ["id"] = "\"camera_profile.t\"",
                ["pitch_degrees"] = "55.0",
                ["yaw_degrees"] = "0.0",
                ["zoom_min"] = "5.0",
                ["zoom_max"] = "15.0",
                ["zoom_default"] = "10.0",
                ["follow_lerp"] = "0.15",
            };
            if (drop != null) fields.Remove(drop);
            if (replaceKey != null) fields[replaceKey] = replaceValueJson!;
            var parts = new List<string>();
            foreach (var kv in fields) parts.Add("\"" + kv.Key + "\":" + kv.Value);
            if (extra.Length > 0) parts.Add(extra);
            return "{" + string.Join(",", parts) + "}";
        }

        [Theory]
        [InlineData("id")]
        [InlineData("pitch_degrees")]
        [InlineData("yaw_degrees")]
        [InlineData("zoom_min")]
        [InlineData("zoom_max")]
        [InlineData("zoom_default")]
        [InlineData("follow_lerp")]
        public void FromRecord_MissingRequiredField_ThrowsDataFieldException_NamingTheField(string field)
        {
            var ex = Assert.Throws<DataFieldException>(() => CameraProfile.FromRecord(Raw(Row(drop: field))));

            Assert.Equal(field, ex.Field);
            Assert.Equal("camera_profile", ex.Table);
        }

        [Theory]
        [InlineData("id", "42")]
        [InlineData("id", "\"Bad Id\"")]
        [InlineData("pitch_degrees", "\"steep\"")]
        [InlineData("zoom_min", "true")]
        [InlineData("zoom_default", "null")]
        [InlineData("follow_lerp", "[]")]
        public void FromRecord_WrongTypeRequiredField_ThrowsDataFieldException_NamingTheField(string field, string valueJson)
        {
            var ex = Assert.Throws<DataFieldException>(() => CameraProfile.FromRecord(Raw(Row(replaceKey: field, replaceValueJson: valueJson))));

            Assert.Equal(field, ex.Field);
        }

        [Theory]
        [InlineData("[5]", "第 0 个元素不是对象")]
        [InlineData("[{\"amplitude\":1,\"duration\":1}]", "\"id\"")]
        [InlineData("[{\"id\":\"Bad Id\",\"amplitude\":1,\"duration\":1}]", "\"id\"")]
        [InlineData("[{\"id\":\"camera_profile.s\",\"duration\":1}]", "\"amplitude\"")]
        [InlineData("[{\"id\":\"camera_profile.s\",\"amplitude\":\"big\",\"duration\":1}]", "\"amplitude\"")]
        [InlineData("[{\"id\":\"camera_profile.s\",\"amplitude\":1}]", "\"duration\"")]
        public void FromRecord_BadShakePreset_ThrowsDataFieldException_OnShakePresetsField(string presetsJson, string expectedMessagePart)
        {
            var ex = Assert.Throws<DataFieldException>(() =>
                CameraProfile.FromRecord(Raw(Row(extra: "\"shake_presets\":" + presetsJson))));

            Assert.Equal("shake_presets", ex.Field);
            Assert.Contains(expectedMessagePart, ex.Message);
        }

        [Theory]
        [InlineData("{\"max\":{\"x\":1,\"y\":1}}", "\"min\"")]
        [InlineData("{\"min\":{\"x\":0,\"y\":0}}", "\"max\"")]
        [InlineData("{\"min\":5,\"max\":{\"x\":1,\"y\":1}}", "\"min\"")]
        [InlineData("{\"min\":{\"x\":0},\"max\":{\"x\":1,\"y\":1}}", "\"min\"")]
        [InlineData("{\"min\":{\"x\":0,\"y\":\"a\"},\"max\":{\"x\":1,\"y\":1}}", "\"min\"")]
        public void FromRecord_BadBounds_ThrowsDataFieldException_OnBoundsField(string boundsJson, string expectedMessagePart)
        {
            var ex = Assert.Throws<DataFieldException>(() =>
                CameraProfile.FromRecord(Raw(Row(extra: "\"bounds\":" + boundsJson))));

            Assert.Equal("bounds", ex.Field);
            Assert.Contains(expectedMessagePart, ex.Message);
        }

        /// <summary>通过字段层解析、但被值对象构造函数的不变量拒绝的行（min&gt;max、zoom 区间错乱）抛
        /// ArgumentException 家族，不是 DataFieldException（现状钉住；是否统一包装待设计层确认）。</summary>
        [Theory]
        [InlineData("zoom_min", "20.0")]      // zoomMin > zoomMax
        [InlineData("zoom_default", "99.0")]  // zoomDefault 落在区间外
        [InlineData("zoom_default", "1.0")]
        public void FromRecord_ZoomRangeInvariantViolation_ThrowsArgumentException_NotDataFieldException(string field, string valueJson)
        {
            var ex = Assert.ThrowsAny<System.ArgumentException>(() =>
                CameraProfile.FromRecord(Raw(Row(replaceKey: field, replaceValueJson: valueJson))));

            Assert.IsNotType<DataFieldException>(ex);
        }

        [Fact]
        public void FromRecord_BoundsMinGreaterThanMax_ThrowsArgumentException()
        {
            var bounds = "{\"min\":{\"x\":10,\"y\":0},\"max\":{\"x\":0,\"y\":1}}";

            Assert.Throws<System.ArgumentException>(() =>
                CameraProfile.FromRecord(Raw(Row(extra: "\"bounds\":" + bounds))));
        }

        // ---- 值对象：CameraBounds / CameraProfile 构造函数 ----

        [Theory]
        [InlineData(1.0, 0.0, 0.0, 0.0)]   // min.X > max.X
        [InlineData(0.0, 1.0, 0.0, 0.0)]   // min.Y > max.Y
        public void CameraBounds_MinGreaterThanMax_ThrowsArgumentException(double minX, double minY, double maxX, double maxY)
        {
            Assert.Throws<System.ArgumentException>(() => new CameraBounds(new Vec2(minX, minY), new Vec2(maxX, maxY)));
        }

        [Fact]
        public void CameraBounds_MinEqualsMax_IsValid_AndClampCollapsesToThePoint()
        {
            var point = new Vec2(3, 4);
            var bounds = new CameraBounds(point, point);

            Assert.Equal(point, bounds.Clamp(new Vec2(-100, 100)));
        }

        [Fact]
        public void CameraBounds_Clamp_ClampsEachAxisIndependently()
        {
            var bounds = new CameraBounds(new Vec2(-1, -2), new Vec2(3, 4));

            Assert.Equal(new Vec2(-1, 4), bounds.Clamp(new Vec2(-50, 50)));
            Assert.Equal(new Vec2(2, 0), bounds.Clamp(new Vec2(2, 0))); // 范围内原样返回
        }

        [Fact]
        public void CameraProfile_Constructor_ZoomMinGreaterThanMax_OrDefaultOutsideRange_ThrowsArgumentException()
        {
            var id = new Id("camera_profile.ctor");

            Assert.Throws<System.ArgumentException>(() => new CameraProfile(id, 0, 0, zoomMin: 2, zoomMax: 1, zoomDefault: 1.5, followLerp: 0.1));
            Assert.Throws<System.ArgumentException>(() => new CameraProfile(id, 0, 0, zoomMin: 1, zoomMax: 2, zoomDefault: 0.5, followLerp: 0.1));
            Assert.Throws<System.ArgumentException>(() => new CameraProfile(id, 0, 0, zoomMin: 1, zoomMax: 2, zoomDefault: 2.5, followLerp: 0.1));
        }

        [Fact]
        public void CameraProfile_Constructor_ZoomDefaultOnBoundary_IsValid_AndNullShakePresetsBecomeEmpty()
        {
            var profile = new CameraProfile(new Id("camera_profile.ctor"), 0, 0, zoomMin: 1, zoomMax: 2, zoomDefault: 2, followLerp: 0.1);

            Assert.Equal(2, profile.ZoomDefault);
            Assert.NotNull(profile.ShakePresets);
            Assert.Empty(profile.ShakePresets);
            Assert.Null(profile.Bounds);
        }
    }
}
