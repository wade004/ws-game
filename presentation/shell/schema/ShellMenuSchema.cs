using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Presentation.Shell
{
    /// <summary>菜单项动作类型（见任务书"entries[]{id, text_key, action: new_game|load_game|
    /// settings|quit|resume|back, target_panel?}"，09_表现层.md 第 9 节 Shell 模块清单：主菜单/
    /// 存档槽/新游戏与难度/加载画面/设置）。新增取值走 [12_扩展与变更流程.md](../../../architecture/12_扩展与变更流程.md)
    /// 审批（惯例同 <c>Core.Gameplay.Dialog.DialogActionKind</c>）。</summary>
    public enum ShellMenuAction
    {
        NewGame,
        LoadGame,
        Settings,
        Quit,
        Resume,
        Back,
    }

    public static class ShellMenuActionWireNames
    {
        public static readonly string[] EnumValues = { "new_game", "load_game", "settings", "quit", "resume", "back" };

        public static bool TryParse(string text, out ShellMenuAction value)
        {
            switch (text)
            {
                case "new_game": value = ShellMenuAction.NewGame; return true;
                case "load_game": value = ShellMenuAction.LoadGame; return true;
                case "settings": value = ShellMenuAction.Settings; return true;
                case "quit": value = ShellMenuAction.Quit; return true;
                case "resume": value = ShellMenuAction.Resume; return true;
                case "back": value = ShellMenuAction.Back; return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>一条菜单项（见任务书 entries[] 结构）。<see cref="TargetPanel"/> 供
    /// <c>settings</c>/<c>back</c> 一类需要跳到某个具体 UI 面板（见
    /// <c>Presentation.Ui.UiLayoutDefinition.Id</c>）的动作使用，其余动作通常为 null。</summary>
    public sealed class ShellMenuEntry
    {
        public Id Id { get; }

        public Id TextKey { get; }

        public ShellMenuAction Action { get; }

        public Id? TargetPanel { get; }

        public ShellMenuEntry(Id id, Id textKey, ShellMenuAction action, Id? targetPanel)
        {
            Id = id;
            TextKey = textKey;
            Action = action;
            TargetPanel = targetPanel;
        }
    }

    /// <summary>一条 <c>shell_menu_definition</c> 记录的强类型视图（见 01_分层与依赖.md L5 模块表
    /// <c>shell</c> 行"主要数据表：shell_menu_definition"）。</summary>
    public sealed class ShellMenuDefinition
    {
        public Id Id { get; }

        public IReadOnlyList<ShellMenuEntry> Entries { get; }

        public ShellMenuDefinition(Id id, IReadOnlyList<ShellMenuEntry> entries)
        {
            Id = id;
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        public static ShellMenuDefinition FromRecord(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            var id = record.GetId("id");
            var entriesArray = record.GetArray("entries");
            var entries = new List<ShellMenuEntry>(entriesArray.Count);
            foreach (var item in entriesArray)
            {
                if (!(item is JsonObject o))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "entries", "元素必须是对象");
                }
                entries.Add(ParseEntry(record, o));
            }

            return new ShellMenuDefinition(id, entries);
        }

        private static ShellMenuEntry ParseEntry(DataRecord record, JsonObject o)
        {
            if (!o.TryGetValue("id", out var idVal) || !(idVal is JsonString idStr) || !Id.TryParse(idStr.Value, out var entryId))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "entries[].id", "缺失或不是合法 Id");
            }

            if (!o.TryGetValue("text_key", out var tkVal) || !(tkVal is JsonString tkStr) || !Id.TryParse(tkStr.Value, out var textKey))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "entries[].text_key", "缺失或不是合法 Id");
            }

            if (!o.TryGetValue("action", out var actionVal) || !(actionVal is JsonString actionStr) ||
                !ShellMenuActionWireNames.TryParse(actionStr.Value, out var action))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "entries[].action", "缺失或取值非法");
            }

            Id? targetPanel = null;
            if (o.TryGetValue("target_panel", out var tp) && tp is JsonString tpStr && Id.TryParse(tpStr.Value, out var tpId))
            {
                targetPanel = tpId;
            }

            return new ShellMenuEntry(entryId, textKey, action, targetPanel);
        }
    }

    /// <summary><c>shell_menu_definition</c> 的静态结构声明。内容表，主键 <c>id</c>。</summary>
    public static class ShellSchemas
    {
        /// <summary><c>entries[]</c>：<c>ShellMenuEntry.ParseEntry</c> 权威解析，id/text_key/action
        /// 缺失或类型不符均抛异常，三者必填；<c>target_panel</c> 缺省 null。<c>target_panel</c>
        /// 登记为 <c>Reference(ui_layout_definition)</c>：<c>Presentation.Shell</c>/
        /// <c>Presentation.Ui</c> 同属 <c>Presentation.Common</c> 程序集（同层），登记为 Reference
        /// 不违反分层。<c>text_key</c> 登记为 <see cref="FieldKind.TextKey"/>（本次补齐文本键存在性
        /// 校验，原运行时只做 <c>Id.TryParse</c> 格式检查，不检查 <c>l10n.text</c> 是否真的有这个
        /// 键——同 <c>gobj.template.type_data.sign.text_key</c> 判断记录）。</summary>
        public static readonly FieldSchema EntriesItemSchema = new FieldSchema(
            "<menu_entry>", FieldKind.Object, required: true, fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "菜单项逻辑 id"),
                new FieldSchema("text_key", FieldKind.TextKey, required: true, description: "菜单项显示文案的文本键，指向 l10n.text"),
                new FieldSchema("action", FieldKind.Enum, required: true, enumValues: ShellMenuActionWireNames.EnumValues,
                    description: "菜单项触发的动作（new_game/load_game/settings/quit/resume/back）"),
                new FieldSchema("target_panel", FieldKind.Reference, required: false, referenceTable: "ui_layout_definition",
                    description: "供 settings/back 一类需要跳到某个具体面板的动作使用，其余动作通常省略"),
            }, description: "一条菜单项：{id, text_key, action, target_panel?}");

        public static readonly TableSchema ShellMenuDefinitionTable = new TableSchema(
            name: "shell_menu_definition",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "菜单逻辑 id"),
                new FieldSchema("entries", FieldKind.Array, required: true, item: EntriesItemSchema,
                    description: "菜单项列表，见 ShellMenuEntry"),
            });
    }
}
