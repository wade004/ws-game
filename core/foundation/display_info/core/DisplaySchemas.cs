using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// 本模块拥有的三张表的 <see cref="TableSchema"/> 登记（见 04_数据与内容管线.md 第 7.1、
    /// 7.1.1、7.1.2 节）：<c>display.map</c>、<c>display.anim_set</c>、<c>display.equip_visual</c>。
    /// <para>
    /// 判断记录（部分字段用 <see cref="FieldKind.Id"/> 而非 <see cref="FieldKind.Reference"/>）：
    /// 任务书显式拍板 <c>logical_id</c>（指向技能/光环/物品/生物/物件，具体指向哪张表由
    /// <c>category</c> 决定，不是单一目标表）、<c>vfx_id</c>/<c>sfx_id</c>（指向 <c>vfx.def</c>/
    /// <c>sfx.def</c>，两张表本任务未定义、可能不在当前数据集里加载）、<c>weapon_style_ref</c>
    /// （指向 <c>display.weapon_style</c>，04 原文点名该表但本任务不定义它）一律用
    /// <see cref="FieldKind.Id"/>，只做格式校验，不做跨表存在性检查——避免 display_info 模块
    /// 单独跑校验时，因为这些表未加载而报虚假的引用完整性错误。<c>display.equip_visual.item_id</c>
    /// （指向 <c>item.template</c>，同样不在本任务数据集里）按同一理由处理为
    /// <see cref="FieldKind.Id"/>。<c>anim_set_ref</c> 指向的 <c>display.anim_set</c> 是本模块
    /// 自己拥有并登记的表，因此按文档原意用 <see cref="FieldKind.Reference"/>。
    /// </para>
    /// </summary>
    public static class DisplaySchemas
    {
        private static readonly string[] Categories = { "skill", "aura", "item", "creature", "gobj", "projectile" };
        private static readonly string[] Kinds = { "sprite", "model" };
        private static readonly string[] Shadows = { "none", "blob", "projected" };
        private static readonly string[] EquipVisualModes = { "slot_mesh", "socket_attach" };

        /// <summary><c>display.map</c>（04 第 7.1 节）：公共字段 + sprite/model 两组专属字段
        /// （专属字段在 schema 层一律登记为非必填——"kind 决定哪组必填"是
        /// <see cref="DisplayKindFieldGroupRule"/> 的职责，不是简单的 required 声明能表达的
        /// 条件必填，见该规则类型注释）。</summary>
        public static readonly TableSchema Map = new TableSchema(
            name: "display.map",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("category", FieldKind.Enum, required: true, enumValues: Categories),
                new FieldSchema("logical_id", FieldKind.Id, required: true, description: "指向的逻辑记录 id（技能/光环/物品/生物/物件），具体目标表由 category 决定，见判断记录"),
                new FieldSchema("kind", FieldKind.Enum, required: true, enumValues: Kinds),
                new FieldSchema("icon_id", FieldKind.String, required: false),
                new FieldSchema("vfx_id", FieldKind.Id, required: false, description: "指向 vfx.def，见判断记录"),
                new FieldSchema("sfx_id", FieldKind.Id, required: false, description: "指向 sfx.def，见判断记录"),
                new FieldSchema("scale", FieldKind.Number, required: false, description: "默认缩放，缺省 1.0"),
                new FieldSchema("shadow", FieldKind.Enum, required: false, enumValues: Shadows, description: "缺省 blob"),
                new FieldSchema("sort_offset", FieldKind.Number, required: false, description: "缺省 0"),
                new FieldSchema("weapon_style_ref", FieldKind.Id, required: false, description: "指向 display.weapon_style（本任务未定义该表），见判断记录"),

                // sprite 型专属字段（schema 层非必填，见类型注释）
                new FieldSchema("sprite_set_id", FieldKind.String, required: false),
                new FieldSchema("direction_count", FieldKind.Int, required: false, description: "取值 4/8/16"),
                new FieldSchema("mirror_pairs", FieldKind.Array, required: false),
                new FieldSchema("paperdoll_layers", FieldKind.Array, required: false),
                new FieldSchema("anchor_points", FieldKind.Object, required: false),

                // model 型专属字段（schema 层非必填，见类型注释）
                new FieldSchema("model_ref", FieldKind.Id, required: false),
                new FieldSchema("anim_set_ref", FieldKind.Reference, required: false, referenceTable: "display.anim_set"),
                new FieldSchema("sockets", FieldKind.IdList, required: false),
                new FieldSchema("slots", FieldKind.IdList, required: false),
                new FieldSchema("default_slot_meshes", FieldKind.Object, required: false),
                new FieldSchema("material_params", FieldKind.Object, required: false),
            },
            migrations: Array.Empty<TableMigration>());

        /// <summary><c>display.anim_set</c>（04 第 7.1.1 节）。</summary>
        public static readonly TableSchema AnimSet = new TableSchema(
            name: "display.anim_set",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("clips", FieldKind.Object, required: true, description: "剪辑 id 到 {resource_ref, events} 的映射，见 04 第 7.1.1 节"),
            },
            migrations: Array.Empty<TableMigration>());

        /// <summary><c>display.equip_visual</c>（04 第 7.1.2 节）。<c>slot_id</c>/<c>mesh_ref</c>
        /// 在 <c>mode: slot_mesh</c> 时必填、<c>socket_id</c>/<c>model_ref</c> 在
        /// <c>mode: socket_attach</c> 时必填的条件必填规则，本任务未要求实现对应
        /// <see cref="IValidationRule"/>（任务书只点名 <see cref="DisplayKindFieldGroupRule"/>、
        /// <see cref="DisplayMapCoverageRule"/> 两条规则），这里只登记字段类型，条件必填留给
        /// 后续任务按同一模式扩展（见本模块 README"不负责什么"）。</summary>
        public static readonly TableSchema EquipVisual = new TableSchema(
            name: "display.equip_visual",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("item_id", FieldKind.Id, required: true, description: "指向 item.template（本任务未定义该表），见判断记录"),
                new FieldSchema("mode", FieldKind.Enum, required: true, enumValues: EquipVisualModes),
                new FieldSchema("slot_id", FieldKind.Id, required: false, description: "mode: slot_mesh 时必填，对应 display.map 的 slots"),
                new FieldSchema("mesh_ref", FieldKind.Id, required: false, description: "mode: slot_mesh 时必填"),
                new FieldSchema("socket_id", FieldKind.Id, required: false, description: "mode: socket_attach 时必填，对应 display.map 的 sockets"),
                new FieldSchema("model_ref", FieldKind.Id, required: false, description: "mode: socket_attach 时必填"),
            },
            migrations: Array.Empty<TableMigration>());

        /// <summary>全部三张表，供 <see cref="IDataRegistry.RegisterSchema"/> 批量登记。</summary>
        public static IReadOnlyList<TableSchema> All { get; } = new[] { Map, AnimSet, EquipVisual };
    }
}
