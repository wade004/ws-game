using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Presentation.Shell;
using Xunit;

namespace Tests.Presentation.Shell
{
    /// <summary>
    /// T-M14（ADR-0125）：<see cref="ShellMenuDefinition.FromRecord"/>/<see cref="ShellMenuActionWireNames.TryParse"/>
    /// 的正常与错误路径。错误路径直接用 <see cref="DataRecord"/> 构造函数喂绕过校验的行（schema 校验层已拦截的
    /// 形状，只读分析入口仍可能走到运行期解析）。
    /// </summary>
    public sealed class ShellMenuFromRecordTests
    {
        private static DataRecord Raw(string json) =>
            new DataRecord(ShellSchemas.ShellMenuDefinitionTable, "shell_menu_definition.t", null, (JsonObject)JsonReader.Parse(json));

        private const string GoodEntry =
            "{\"id\":\"shell.entry.a\",\"text_key\":\"l10n.shell.a\",\"action\":\"new_game\"}";

        [Fact]
        public void FromRecord_WellFormedRow_ParsesEntriesInOrder_WithOptionalTargetPanel()
        {
            var def = ShellMenuDefinition.FromRecord(Raw(
                "{\"id\":\"shell_menu_definition.t\",\"entries\":[" + GoodEntry + "," +
                "{\"id\":\"shell.entry.b\",\"text_key\":\"l10n.shell.b\",\"action\":\"settings\",\"target_panel\":\"ui_layout_definition.s\"}]}"));

            Assert.Equal(new Id("shell_menu_definition.t"), def.Id);
            Assert.Equal(2, def.Entries.Count);
            Assert.Equal(ShellMenuAction.NewGame, def.Entries[0].Action);
            Assert.Null(def.Entries[0].TargetPanel);
            Assert.Equal(ShellMenuAction.Settings, def.Entries[1].Action);
            Assert.Equal(new Id("ui_layout_definition.s"), def.Entries[1].TargetPanel);
        }

        [Fact]
        public void FromRecord_EmptyEntries_IsValid()
        {
            var def = ShellMenuDefinition.FromRecord(Raw("{\"id\":\"shell_menu_definition.t\",\"entries\":[]}"));

            Assert.Empty(def.Entries);
        }

        [Theory]
        [InlineData("{\"entries\":[]}", "id")]
        [InlineData("{\"id\":7,\"entries\":[]}", "id")]
        [InlineData("{\"id\":\"Bad Id\",\"entries\":[]}", "id")]
        [InlineData("{\"id\":\"shell_menu_definition.t\"}", "entries")]
        [InlineData("{\"id\":\"shell_menu_definition.t\",\"entries\":\"x\"}", "entries")]
        [InlineData("{\"id\":\"shell_menu_definition.t\",\"entries\":[5]}", "entries")]
        public void FromRecord_TopLevelFieldProblems_ThrowDataFieldException_NamingTheField(string rowJson, string expectedField)
        {
            var ex = Assert.Throws<DataFieldException>(() => ShellMenuDefinition.FromRecord(Raw(rowJson)));

            Assert.Equal(expectedField, ex.Field);
            Assert.Equal("shell_menu_definition", ex.Table);
        }

        [Theory]
        // 缺字段
        [InlineData("{\"text_key\":\"l10n.shell.a\",\"action\":\"quit\"}", "entries[].id")]
        [InlineData("{\"id\":\"shell.entry.a\",\"action\":\"quit\"}", "entries[].text_key")]
        [InlineData("{\"id\":\"shell.entry.a\",\"text_key\":\"l10n.shell.a\"}", "entries[].action")]
        // 类型不符
        [InlineData("{\"id\":1,\"text_key\":\"l10n.shell.a\",\"action\":\"quit\"}", "entries[].id")]
        [InlineData("{\"id\":\"shell.entry.a\",\"text_key\":true,\"action\":\"quit\"}", "entries[].text_key")]
        [InlineData("{\"id\":\"shell.entry.a\",\"text_key\":\"l10n.shell.a\",\"action\":3}", "entries[].action")]
        // 非法 Id / 非法枚举
        [InlineData("{\"id\":\"Bad Id\",\"text_key\":\"l10n.shell.a\",\"action\":\"quit\"}", "entries[].id")]
        [InlineData("{\"id\":\"shell.entry.a\",\"text_key\":\"Bad Key\",\"action\":\"quit\"}", "entries[].text_key")]
        [InlineData("{\"id\":\"shell.entry.a\",\"text_key\":\"l10n.shell.a\",\"action\":\"explode\"}", "entries[].action")]
        [InlineData("{\"id\":\"shell.entry.a\",\"text_key\":\"l10n.shell.a\",\"action\":\"NEW_GAME\"}", "entries[].action")] // 枚举值区分大小写
        public void FromRecord_BadEntry_ThrowsDataFieldException_NamingTheEntryField(string entryJson, string expectedField)
        {
            var ex = Assert.Throws<DataFieldException>(() => ShellMenuDefinition.FromRecord(Raw(
                "{\"id\":\"shell_menu_definition.t\",\"entries\":[" + GoodEntry + "," + entryJson + "]}")));

            Assert.Equal(expectedField, ex.Field);
        }

        [Fact]
        public void Constructor_NullEntries_ThrowsArgumentNull() =>
            Assert.Equal("entries", Assert.Throws<System.ArgumentNullException>(
                () => new ShellMenuDefinition(new Id("shell_menu_definition.t"), null!)).ParamName);

        [Theory]
        [InlineData("new_game", ShellMenuAction.NewGame)]
        [InlineData("load_game", ShellMenuAction.LoadGame)]
        [InlineData("settings", ShellMenuAction.Settings)]
        [InlineData("quit", ShellMenuAction.Quit)]
        [InlineData("resume", ShellMenuAction.Resume)]
        [InlineData("back", ShellMenuAction.Back)]
        public void ActionWireNames_TryParse_KnownNames_RoundTripWithEnumValues(string wire, ShellMenuAction expected)
        {
            Assert.True(ShellMenuActionWireNames.TryParse(wire, out var value));
            Assert.Equal(expected, value);
            Assert.Contains(wire, ShellMenuActionWireNames.EnumValues);
        }

        [Theory]
        [InlineData("")]
        [InlineData("NewGame")]
        [InlineData("quit ")]
        [InlineData("nonsense")]
        public void ActionWireNames_TryParse_UnknownNames_ReturnFalse(string wire)
        {
            Assert.False(ShellMenuActionWireNames.TryParse(wire, out _));
        }

        [Fact]
        public void ActionWireNames_TryParse_Null_ReturnsFalse_AndEveryEnumValueIsListedOnce()
        {
            Assert.False(ShellMenuActionWireNames.TryParse(null!, out _));

            // 不变量：线名清单与枚举成员一一对应（数量由枚举本身算出，不写裸数）。
            Assert.Equal(System.Enum.GetValues(typeof(ShellMenuAction)).Length, ShellMenuActionWireNames.EnumValues.Length);
            Assert.All(ShellMenuActionWireNames.EnumValues, name => Assert.True(ShellMenuActionWireNames.TryParse(name, out _)));
        }
    }
}
