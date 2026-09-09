using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Presentation.Camera.Schema;
using Xunit;

namespace Tests.Presentation.Camera
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="CameraSchemas.Profile"/> 的 <c>bounds</c>/<c>shake_presets</c>
    /// 子结构登记（<c>Fields</c>/<c>Item</c>），覆盖范围：子结构命中/坏形状各一例。
    /// </summary>
    public sealed class CameraSchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        private static string BaseRow(string id, string extra) =>
            "[{\"id\":\"" + id + "\",\"pitch_degrees\":45,\"yaw_degrees\":0,\"zoom_min\":5,\"zoom_max\":20," +
            "\"zoom_default\":10,\"follow_lerp\":0.2" + (string.IsNullOrEmpty(extra) ? "" : "," + extra) + "}]";

        [Fact]
        public void BoundsAndShakePresets_WellFormed_LoadsWithoutErrors()
        {
            var rows = BaseRow("camera_profile.cov_sample",
                "\"bounds\":{\"min\":{\"x\":-10,\"y\":-10},\"max\":{\"x\":10,\"y\":10}}," +
                "\"shake_presets\":[{\"id\":\"shake.cov\",\"amplitude\":1,\"duration\":0.3}]");

            var source = new InMemoryDataSource().Add(CameraSchemas.Profile.Name, Envelope(CameraSchemas.Profile.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(CameraSchemas.Profile);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Bounds_MissingMax_ReportsRequiredField()
        {
            var rows = BaseRow("camera_profile.cov_bad", "\"bounds\":{\"min\":{\"x\":0,\"y\":0}}");

            var source = new InMemoryDataSource().Add(CameraSchemas.Profile.Name, Envelope(CameraSchemas.Profile.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(CameraSchemas.Profile);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "bounds.max");
        }

        [Fact]
        public void ShakePresets_MissingDuration_ReportsRequiredField()
        {
            var rows = BaseRow("camera_profile.cov_bad_shake",
                "\"shake_presets\":[{\"id\":\"shake.cov_bad\",\"amplitude\":1}]");

            var source = new InMemoryDataSource().Add(CameraSchemas.Profile.Name, Envelope(CameraSchemas.Profile.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(CameraSchemas.Profile);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "shake_presets[0].duration");
        }
    }
}
