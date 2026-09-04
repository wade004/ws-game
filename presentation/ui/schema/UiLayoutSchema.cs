using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Presentation.Ui
{
    /// <summary>
    /// 面板类别（见 01_分层与依赖.md L5 模块表 <c>ui</c> 行"主要数据表：ui_layout_definition"、
    /// 09_表现层.md 第 7.1 节 UI 组成清单）。任务书拍板"panel（enum 十个）"，本模块把十个值定为
    /// 与 <c>core/ViewModels</c> 下十个视图模型一一对应（09 §7.1 清单里的"状态栏"并入 Hud、
    /// "目标框"同样并入 Hud——两者都由 <see cref="HudViewModel"/> 一并承载；"商店"未单列专属视图
    /// 模型，复用 <see cref="UiIntents.Buy"/>/<see cref="UiIntents.Sell"/> 意图 + 数据来源与
    /// <see cref="InventoryViewModel"/> 相同，不需要单独面板类别；"按键绑定面板"并入 Settings，
    /// 因为 <see cref="SettingsViewModel"/> 已经承载绑定列表——见本目录 README 判断记录），
    /// 保证"面板类别"与"落地计划要求的十个视图模型"严格对齐，不产生"有面板无视图模型"或反之
    /// 的孤儿。
    /// </summary>
    public enum UiPanel
    {
        Hud,
        ActionBar,
        Inventory,
        QuestLog,
        Dialog,
        SkillBook,
        CharacterStats,
        Settings,
        SaveSlots,
        PauseMenu,
    }

    public static class UiPanelWireNames
    {
        public static readonly string[] EnumValues =
        {
            "hud", "action_bar", "inventory", "quest_log", "dialog",
            "skill_book", "character_stats", "settings", "save_slots", "pause_menu",
        };

        public static bool TryParse(string text, out UiPanel value)
        {
            switch (text)
            {
                case "hud": value = UiPanel.Hud; return true;
                case "action_bar": value = UiPanel.ActionBar; return true;
                case "inventory": value = UiPanel.Inventory; return true;
                case "quest_log": value = UiPanel.QuestLog; return true;
                case "dialog": value = UiPanel.Dialog; return true;
                case "skill_book": value = UiPanel.SkillBook; return true;
                case "character_stats": value = UiPanel.CharacterStats; return true;
                case "settings": value = UiPanel.Settings; return true;
                case "save_slots": value = UiPanel.SaveSlots; return true;
                case "pause_menu": value = UiPanel.PauseMenu; return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>一条 <c>ui_layout_definition</c> 记录的强类型视图（见任务书"ui_layout_definition
    /// 表 schema（id、panel（enum 十个）、slots?、fields: Object——布局参数留给引擎侧，本模块只
    /// 登记与读取动作条槽数等逻辑相关项）"）。<see cref="Fields"/> 保留原始 JSON，本模块不解释其
    /// 内容（具体布局参数是引擎适配层/表现资源的事）；<see cref="Slots"/> 是唯一一个本模块自己会
    /// 读取的"逻辑相关项"——<see cref="ActionBarViewModel"/> 用它决定动作条槽位数量（见 09 第 7.1
    /// 节"动作条"、任务书"槽数由 UiLayoutDefinition 数据决定"）。</summary>
    public sealed class UiLayoutDefinition
    {
        public Id Id { get; }

        public UiPanel Panel { get; }

        /// <summary>动作条槽位数量；仅 <see cref="Panel"/> 为 <see cref="UiPanel.ActionBar"/> 时有
        /// 意义，其余面板为 null。</summary>
        public int? Slots { get; }

        /// <summary>布局参数，结构由引擎适配层/具体游戏约定，本模块只透传（见类型注释）。</summary>
        public JsonObject Fields { get; }

        public UiLayoutDefinition(Id id, UiPanel panel, int? slots, JsonObject fields)
        {
            Id = id;
            Panel = panel;
            Slots = slots;
            Fields = fields;
        }

        public static UiLayoutDefinition FromRecord(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            var id = record.GetId("id");
            var panelText = record.GetString("panel");
            if (!UiPanelWireNames.TryParse(panelText, out var panel))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "panel", $"取值 \"{panelText}\" 不是合法枚举");
            }

            var slots = record.TryGetInt("slots", out var slotsVal) ? (int?)slotsVal : null;
            var fields = record.TryGetObject("fields", out var fieldsVal) ? fieldsVal : new JsonObjectBuilder().Build();

            return new UiLayoutDefinition(id, panel, slots, fields);
        }
    }

    /// <summary><c>ui_layout_definition</c> 的静态结构声明（见 01_分层与依赖.md L5 模块表
    /// <c>ui</c> 行）。内容表，主键 <c>id</c>（不是跨 domain 登记表）。</summary>
    public static class UiSchemas
    {
        public static readonly TableSchema UiLayoutDefinition = new TableSchema(
            name: "ui_layout_definition",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "面板逻辑 id"),
                new FieldSchema("panel", FieldKind.Enum, required: true, enumValues: UiPanelWireNames.EnumValues, description: "面板类别"),
                new FieldSchema("slots", FieldKind.Int, required: false, description: "动作条槽位数量，仅 panel=action_bar 时有意义"),
                new FieldSchema("fields", FieldKind.Object, required: true, description: "布局参数，结构留给引擎适配层解释"),
            });
    }
}
