using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Presentation.Ui;
using Xunit;

namespace Tests.PresentationUi
{
    /// <summary>
    /// T-M31（ADR-0125）：<see cref="UiSchemas.UiLayoutDefinition"/> 的 schema 级校验（正例 + 反例）与
    /// <see cref="UiLayoutDefinition.FromRecord"/> / <see cref="UiPanelWireNames"/> 的运行期错误路径
    ///（其它模块都有 *SchemaCoverageTests，本模块此前缺）。
    /// </summary>
    public sealed class UiLayoutSchemaTests
    {
        private static string Envelope(string rowsJson) =>
            "{\"table\":\"ui_layout_definition\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        private static (IDataRegistry Registry, ValidationReport Report) Load(string rowsJson)
        {
            var source = new InMemoryDataSource().Add(UiSchemas.UiLayoutDefinition.Name, Envelope(rowsJson));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(UiSchemas.UiLayoutDefinition);
            return (registry, registry.LoadAll());
        }

        private static DataRecord Raw(string json) =>
            new DataRecord(UiSchemas.UiLayoutDefinition, "ui_layout_definition.t", null, (JsonObject)JsonReader.Parse(json));

        // ---- schema 正例 ----

        /// <summary>每个 wire 名都能作为 panel 取值通过 schema 校验并被 FromRecord 解析回同名枚举。</summary>
        [Fact]
        public void Schema_EveryPanelWireName_LoadsAndParsesBack()
        {
            var rows = "[" + string.Join(",", UiPanelWireNames.EnumValues.Select((name, i) =>
                "{\"id\":\"ui_layout_definition.p" + i + "\",\"panel\":\"" + name + "\",\"fields\":{}}")) + "]";

            var (registry, report) = Load(rows);

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            for (var i = 0; i < UiPanelWireNames.EnumValues.Length; i++)
            {
                var def = UiLayoutDefinition.FromRecord(registry.Get(UiSchemas.UiLayoutDefinition.Name, "ui_layout_definition.p" + i)!);
                Assert.True(UiPanelWireNames.TryParse(UiPanelWireNames.EnumValues[i], out var expected));
                Assert.Equal(expected, def.Panel);
            }
        }

        [Fact]
        public void Schema_ActionBarRowWithSlots_ParsesSlotsAndFields()
        {
            var (registry, report) = Load(
                "[{\"id\":\"ui_layout_definition.bar\",\"panel\":\"action_bar\",\"slots\":10,\"fields\":{\"anchor\":\"bottom\"}}]");
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var def = UiLayoutDefinition.FromRecord(registry.Get(UiSchemas.UiLayoutDefinition.Name, "ui_layout_definition.bar")!);

            Assert.Equal(UiPanel.ActionBar, def.Panel);
            Assert.Equal(10, def.Slots);
            Assert.Equal("bottom", ((JsonString)def.Fields["anchor"]).Value);
        }

        // ---- skin_ref（手感设计/08 第 3/4 节、ADR-0123）：可选加法字段 ----

        [Fact]
        public void Schema_SkinRef_OptionalAndParsed_AbsentMeansPlaceholder_AndSchemaVersionUnchanged()
        {
            var (registry, report) = Load(
                "[{\"id\":\"ui_layout_definition.skinned\",\"panel\":\"inventory\",\"skin_ref\":\"skin.my_game\",\"fields\":{}}," +
                "{\"id\":\"ui_layout_definition.plain\",\"panel\":\"hud\",\"fields\":{}}]");
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var skinned = UiLayoutDefinition.FromRecord(registry.Get(UiSchemas.UiLayoutDefinition.Name, "ui_layout_definition.skinned")!);
            var plain = UiLayoutDefinition.FromRecord(registry.Get(UiSchemas.UiLayoutDefinition.Name, "ui_layout_definition.plain")!);

            Assert.Equal(new Id("skin.my_game"), skinned.SkinRef);
            Assert.Null(plain.SkinRef);
            Assert.Equal(1, UiSchemas.UiLayoutDefinition.CurrentSchemaVersion);
        }

        [Fact]
        public void Schema_SkinRefNotAnId_IsBlocking()
        {
            var (_, report) = Load("[{\"id\":\"ui_layout_definition.bad\",\"panel\":\"hud\",\"skin_ref\":3,\"fields\":{}}]");

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Field == "skin_ref");
        }

        [Fact]
        public void LegacyFourArgConstructor_StillWorks_WithNullSkinRef()
        {
            var def = new UiLayoutDefinition(new Id("ui_layout_definition.legacy"), UiPanel.Hud, null,
                (JsonObject)JsonReader.Parse("{}"));

            Assert.Null(def.SkinRef);
        }

        // ---- schema 反例 ----

        [Theory]
        [InlineData("{\"id\":\"ui_layout_definition.bad\",\"fields\":{}}", "panel")]                          // 缺 panel
        [InlineData("{\"id\":\"ui_layout_definition.bad\",\"panel\":\"hud\"}", "fields")]                      // 缺 fields
        [InlineData("{\"id\":\"ui_layout_definition.bad\",\"panel\":\"not_a_panel\",\"fields\":{}}", "panel")] // 未知枚举值
        [InlineData("{\"id\":\"ui_layout_definition.bad\",\"panel\":\"hud\",\"slots\":\"ten\",\"fields\":{}}", "slots")]  // 类型不符
        [InlineData("{\"id\":\"ui_layout_definition.bad\",\"panel\":\"hud\",\"fields\":[]}", "fields")]      // 类型不符
        public void Schema_BadRow_IsBlocking_AndReportsTheOffendingField(string rowJson, string expectedField)
        {
            var (_, report) = Load("[" + rowJson + "]");

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Field == expectedField);
        }

        [Fact]
        public void Schema_RowWithoutPrimaryKey_IsBlocking_AsPrimaryKeyIssue()
        {
            var (_, report) = Load("[{\"panel\":\"hud\",\"fields\":{}}]");

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "primary_key");
        }

        // ---- FromRecord 运行期错误路径 ----

        [Theory]
        [InlineData("{\"panel\":\"hud\",\"fields\":{}}", "id")]
        [InlineData("{\"id\":\"Bad Id\",\"panel\":\"hud\",\"fields\":{}}", "id")]
        [InlineData("{\"id\":\"ui_layout_definition.t\",\"fields\":{}}", "panel")]
        [InlineData("{\"id\":\"ui_layout_definition.t\",\"panel\":9,\"fields\":{}}", "panel")]
        [InlineData("{\"id\":\"ui_layout_definition.t\",\"panel\":\"not_a_panel\",\"fields\":{}}", "panel")]
        [InlineData("{\"id\":\"ui_layout_definition.t\",\"panel\":\"HUD\",\"fields\":{}}", "panel")] // 区分大小写
        public void FromRecord_BadIdOrPanel_ThrowsDataFieldException_NamingTheField(string rowJson, string expectedField)
        {
            var ex = Assert.Throws<DataFieldException>(() => UiLayoutDefinition.FromRecord(Raw(rowJson)));

            Assert.Equal(expectedField, ex.Field);
            Assert.Equal("ui_layout_definition", ex.Table);
        }

        [Fact]
        public void FromRecord_AbsentOptionalSlotsAndFields_DefaultToNullSlotsAndEmptyFields()
        {
            var def = UiLayoutDefinition.FromRecord(Raw("{\"id\":\"ui_layout_definition.t\",\"panel\":\"hud\"}"));

            Assert.Null(def.Slots);
            Assert.Empty(def.Fields);
        }

        [Fact]
        public void PanelWireNames_TryParse_UnknownOrNull_ReturnFalse_AndEnumValuesMatchEnumMembers()
        {
            Assert.False(UiPanelWireNames.TryParse("nonsense", out _));
            Assert.False(UiPanelWireNames.TryParse("", out _));
            Assert.False(UiPanelWireNames.TryParse(null!, out _));

            // 不变量：线名清单与 UiPanel 枚举成员一一对应，且每个线名可解析、互不重复。
            Assert.Equal(System.Enum.GetValues(typeof(UiPanel)).Length, UiPanelWireNames.EnumValues.Length);
            Assert.Equal(UiPanelWireNames.EnumValues.Length, UiPanelWireNames.EnumValues.Distinct().Count());
            var parsed = UiPanelWireNames.EnumValues.Select(n => { Assert.True(UiPanelWireNames.TryParse(n, out var v)); return v; });
            Assert.Equal(UiPanelWireNames.EnumValues.Length, parsed.Distinct().Count());
        }
    }
}
