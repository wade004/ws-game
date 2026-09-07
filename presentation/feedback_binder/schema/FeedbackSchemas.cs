using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Presentation.FeedbackBinder.Schema
{
    /// <summary>
    /// 本模块拥有的两张表的 <see cref="TableSchema"/> 登记（见 09_表现层.md 第 6.1、6.2 节）：
    /// <c>feedback.binding</c>、<c>feedback.floating_text_style</c>（同
    /// <c>Presentation.VfxSfx.Schema.VfxSfxSchemas</c> 惯例：只登记 schema，不接入
    /// <c>data/_sample/</c>）。
    /// </summary>
    public static class FeedbackSchemas
    {
        /// <summary><c>feedback.binding</c>（09 第 6.1 节）。<c>actions</c> 的嵌套结构（每项
        /// <c>{kind, params}</c>）由 <see cref="Presentation.FeedbackBinder.Contracts.FeedbackRule.FromRecord"/>
        /// 自行解析，schema 层只声明为 <see cref="FieldKind.Array"/>（结构未知/上层解释，同
        /// <c>Core.Rules.Ai.AiSchemas.Rotation.entries</c> 惯例）。</summary>
        public static readonly TableSchema Binding = new TableSchema(
            name: "feedback.binding",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "feedback.<name>"),
                new FieldSchema("event", FieldKind.Id, required: true, description: "订阅的事件 key（如 combat.damage_dealt），指向 found.event_catalog，见判断记录（跨 domain 登记表不适合用 FieldKind.Reference，本模块的 FeedbackRuleValidator 另行按 EventKeys.All 校验）"),
                new FieldSchema("condition", FieldKind.Expr, required: false, description: "Expr 条件文本，见 04 第 6 节；宿主分组含 event（触发事件字段）"),
                new FieldSchema("actions", FieldKind.Array, required: true, description: "有序 FeedbackAction 列表：[{kind: floating_text|play_vfx|play_sfx|freeze|shake_camera|flash, params: {...}}]，见 09 第 6.1 节"),
                new FieldSchema("sync", FieldKind.Enum, required: false, enumValues: new[] { "hit_frame" }, description: "ADR-0017 决策 d：命中帧同步声明，未提供时按 event 是否为 combat.damage_dealt 决定默认值（见 FeedbackRule.Sync 判断记录）"),
            },
            migrations: Array.Empty<TableMigration>());

        /// <summary><c>feedback.floating_text_style</c>（09 第 6.2 节）。</summary>
        public static readonly TableSchema FloatingTextStyle = new TableSchema(
            name: "feedback.floating_text_style",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "feedback.floating_text_style.<name>"),
                new FieldSchema("color_ref", FieldKind.Id, required: true, description: "颜色引用（正常伤害/暴击/治疗/闪避文案各自配色）"),
                new FieldSchema("size_scale", FieldKind.Number, required: false, description: "相对基础字号的缩放"),
                new FieldSchema("motion_profile", FieldKind.Id, required: false, description: "飘字运动曲线引用（上浮/抖动/聚合等）"),
            },
            migrations: Array.Empty<TableMigration>());

        public static IReadOnlyList<TableSchema> All { get; } = new[] { Binding, FloatingTextStyle };
    }
}
