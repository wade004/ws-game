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
                new FieldSchema("id", FieldKind.Id, required: true, description: "display.map.<name>"),
                new FieldSchema("category", FieldKind.Enum, required: true, enumValues: Categories, description: "所属类别（skill/aura/item/creature/gobj/projectile），决定 logical_id 指向哪张表，见判断记录"),
                new FieldSchema("logical_id", FieldKind.Id, required: true, description: "指向的逻辑记录 id（技能/光环/物品/生物/物件），具体目标表由 category 决定，见判断记录"),
                new FieldSchema("kind", FieldKind.Enum, required: true, enumValues: Kinds, description: "显示类型（sprite/model），决定下方哪组专属字段生效，见 DisplayKindFieldGroupRule"),
                new FieldSchema("icon_id", FieldKind.String, required: false, description: "UI 图标资源引用"),
                new FieldSchema("vfx_id", FieldKind.Id, required: false, description: "指向 vfx.def，见判断记录"),
                new FieldSchema("sfx_id", FieldKind.Id, required: false, description: "指向 sfx.def，见判断记录"),
                new FieldSchema("scale", FieldKind.Number, required: false, description: "默认缩放，缺省 1.0"),
                new FieldSchema("shadow", FieldKind.Enum, required: false, enumValues: Shadows, description: "缺省 blob"),
                new FieldSchema("sort_offset", FieldKind.Number, required: false, description: "缺省 0"),
                new FieldSchema("weapon_style_ref", FieldKind.Id, required: false, description: "指向 display.weapon_style（本任务未定义该表），见判断记录"),

                // sprite 型专属字段（schema 层非必填，见类型注释）
                new FieldSchema("sprite_set_id", FieldKind.String, required: false, description: "精灵集资源引用（不含路径），sprite 型必填"),
                new FieldSchema("direction_count", FieldKind.Int, required: false, description: "取值 4/8/16"),
                new FieldSchema("mirror_pairs", FieldKind.Array, required: false,
                    item: new FieldSchema("<mirror_pair>", FieldKind.Object, required: true, fields: new[]
                    {
                        new FieldSchema("direction_slot", FieldKind.Id, required: true, description: "该镜像规则对应的方向槽位 id"),
                        new FieldSchema("mirror_of", FieldKind.Id, required: true, description: "美术资源实际复用的源方向槽位 id，渲染 direction_slot 时按 flip_x 翻转该方向的素材"),
                        new FieldSchema("flip_x", FieldKind.Bool, required: false, description: "缺省 false"),
                    }, description: "单条方向镜像规则：{direction_slot, mirror_of, flip_x?}"),
                    description: "[{direction_slot:Id, mirror_of:Id, flip_x:Bool}]（DisplayInfo.ParseSpriteInfo 对 " +
                        "direction_slot/mirror_of 缺失即抛异常，均必填）"),
                new FieldSchema("paperdoll_layers", FieldKind.Array, required: false,
                    item: new FieldSchema("<layer>", FieldKind.String, required: true, description: "纸娃娃分层引用名"),
                    description: "纸娃娃分层引用列表，仅 item/creature 使用"),
                new FieldSchema("anchor_points", FieldKind.Object, required: false,
                    description: "Map<anchor_name, AnchorDef>，键为锚点名（动态，内容作者自行声明，不指向任何已登记表）")
                    .WithMap(MapSchema.FreeKeyed(
                        "锚点名是内容作者在本条记录内自行声明的局部名字，供渲染层按字符串键回查，不指向任何已登记表的既有记录（同 sockets/slots 字段的 FreeIds 判断记录）",
                        new FieldSchema("<anchor_def>", FieldKind.Object, required: true, fields: new[]
                        {
                            // DisplayInfo.ParseAnchorDef：parent_layer/offset 均必填，非法即抛
                            // DataFieldException；parent_layer 引用 paperdoll_layers 列表元素（本身是
                            // 裸字符串，见 AnchorDef.ParentLayer 判断记录），不收紧为 Id 格式。
                            new FieldSchema("parent_layer", FieldKind.String, required: true,
                                description: "所属纸娃娃层名，引用 paperdoll_layers 的一个元素（裸字符串，不要求 Id 格式）"),
                            new FieldSchema("offset", FieldKind.Vec2, required: true,
                                description: "相对父层原点的默认偏移"),
                            new FieldSchema("offset_by_direction", FieldKind.Object, required: false,
                                description: "按方向档位覆盖偏移，缺省用 offset；键为方向槽位 id（与 mirror_pairs.direction_slot 同一套局部 id 空间，不指向已登记表）")
                                .WithMap(MapSchema.FreeKeyed(
                                    "方向槽位 id 是 IRenderConventionHost.ResolveDirectionSlot 返回的局部约定值，与 mirror_pairs.direction_slot 同一套 id 空间，不指向任何已登记表",
                                    new FieldSchema("<offset>", FieldKind.Vec2, required: true, description: "该方向槽位覆盖的偏移"))),
                        }, description: "{parent_layer: String, offset: Vec2, offset_by_direction?: Map<Id,Vec2>}，见 DisplayInfo.ParseAnchorDef"))),

                // model 型专属字段（schema 层非必填，见类型注释）
                new FieldSchema("model_ref", FieldKind.Id, required: false, description: "model 型专属，模型资源的间接引用，由引擎适配层解析加载"),
                new FieldSchema("anim_set_ref", FieldKind.Reference, required: false, referenceTable: "display.anim_set", description: "model 型专属，指向本模块的 display.anim_set 动画剪辑集合"),
                new FieldSchema("sockets", FieldKind.IdList, required: false, description: "model 型专属，该模型声明的挂点 id 列表，供 display.equip_visual 的 socket_attach 模式引用")
                    .WithFreeIds("挂点 id 由本条记录自行声明的局部名字，不指向任何已登记表的既有记录；display.equip_visual.socket_id 按字符串比较，不在本字段做引用完整性检查"),
                new FieldSchema("slots", FieldKind.IdList, required: false, description: "model 型专属，该模型声明的可换装槽位 id 列表，供 display.equip_visual 的 slot_mesh 模式引用")
                    .WithFreeIds("槽位 id 由本条记录自行声明的局部名字，不指向任何已登记表的既有记录；display.equip_visual.slot_id 按字符串比较，不在本字段做引用完整性检查"),
                new FieldSchema("default_slot_meshes", FieldKind.Object, required: false,
                    description: "Map<slot_id, mesh_ref:Id>，键为 slots 字段自行声明的局部槽位 id（不指向已登记表），值是引擎适配层解析的网格资源引用（同 model_ref，不做引用完整性检查）")
                    .WithMap(MapSchema.FreeKeyed(
                        "槽位 id 由本条记录自行声明的局部名字（slots 字段），不指向任何已登记表的既有记录，同 slots/sockets 字段的 FreeIds 判断记录",
                        new FieldSchema("<mesh_ref>", FieldKind.Id, required: true,
                            description: "该槽位默认换装的网格资源引用，由引擎适配层解析加载，不做引用完整性检查"))),
                new FieldSchema("material_params", FieldKind.Object, required: false,
                    description: "Map<param_name, Number>，键为材质参数名（自由字符串，非 Id 格式）")
                    .WithMap(MapSchema.FreeKeyed(
                        "材质参数名是引擎材质系统的参数名，自由字符串（不要求 Id 格式），不指向任何已登记表",
                        new FieldSchema("<param_value>", FieldKind.Number, required: true,
                            description: "该材质参数的取值"))),
            },
            migrations: Array.Empty<TableMigration>())
            .WithOwnership(SchemaLayer.Foundation, "display");

        /// <summary><c>display.anim_set</c>（04 第 7.1.1 节）。</summary>
        public static readonly TableSchema AnimSet = new TableSchema(
            name: "display.anim_set",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "display.anim_set.<name>"),
                // ADR-0024 第二批登记：键为剪辑名（自由字符串，AnimSetDef.Clips 类型注释"不要求点分
                // Id 格式"），值结构与 AnimSetDef.ParseClip 逐字段核对一致——resource_ref 必填 Id，
                // events 可选数组，元素 {name: 非空 String, time_pct: Number} 均必填（对照
                // AnimSetEventsShapeRule.ValidateEvent 的同款结构性检查，Error 级）。time_pct 越界
                // [0,1] 刻意不在此登记 Range：AnimSetEventsShapeRule 已把这条判定为 Warning（语义
                // 合理性问题，FromRecord 仍能解析成功），登记 Range 会把同一违规提升为 Error，与既有
                // 规则的严重级别产生分歧，见该规则类型判断记录"只登记警告，不阻断加载"。
                new FieldSchema("clips", FieldKind.Object, required: true, description: "剪辑 id 到 {resource_ref, events} 的映射，见 04 第 7.1.1 节")
                    .WithMap(MapSchema.FreeKeyed(
                        "剪辑名是内容作者自行命名的剪辑标识，不要求点分 Id 格式，不指向任何已登记表（见 AnimSetDef.Clips 类型注释）",
                        new FieldSchema("<clip>", FieldKind.Object, required: true, fields: new[]
                        {
                            new FieldSchema("resource_ref", FieldKind.Id, required: true,
                                description: "指向具体动画剪辑资产的资源引用，由引擎适配层解析"),
                            new FieldSchema("events", FieldKind.Array, required: false,
                                item: new FieldSchema("<event>", FieldKind.Object, required: true, fields: new[]
                                {
                                    new FieldSchema("name", FieldKind.String, required: true,
                                        description: "事件标记名，如 hit_frame"),
                                    new FieldSchema("time_pct", FieldKind.Number, required: true,
                                        description: "事件在剪辑时间轴上的相对位置，建议 [0,1]；越界由 AnimSetEventsShapeRule 报 Warning，不在此登记 Range（见判断记录）"),
                                }, description: "单条关键帧事件"),
                                description: "缺省空列表；未声明 events 字段时 AnimSetDef.FromRecord 视为无关键帧事件"),
                        }, description: "{resource_ref: Id, events?: [{name, time_pct}]}，见 AnimSetDef.ParseClip"))),
            },
            migrations: Array.Empty<TableMigration>())
            .WithOwnership(SchemaLayer.Foundation, "display");

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
                new FieldSchema("id", FieldKind.Id, required: true, description: "display.equip_visual.<name>"),
                new FieldSchema("item_id", FieldKind.Id, required: true, description: "指向 item.template（本任务未定义该表），见判断记录"),
                new FieldSchema("mode", FieldKind.Enum, required: true, enumValues: EquipVisualModes, description: "呈现模式（slot_mesh/socket_attach），决定 slot_id+mesh_ref 与 socket_id+model_ref 哪组必填"),
                new FieldSchema("slot_id", FieldKind.Id, required: false, description: "mode: slot_mesh 时必填，对应 display.map 的 slots"),
                new FieldSchema("mesh_ref", FieldKind.Id, required: false, description: "mode: slot_mesh 时必填"),
                new FieldSchema("socket_id", FieldKind.Id, required: false, description: "mode: socket_attach 时必填，对应 display.map 的 sockets"),
                new FieldSchema("model_ref", FieldKind.Id, required: false, description: "mode: socket_attach 时必填"),
            },
            migrations: Array.Empty<TableMigration>())
            .WithOwnership(SchemaLayer.Foundation, "display");

        /// <summary>全部三张表，供 <see cref="IDataRegistry.RegisterSchema"/> 批量登记。</summary>
        public static IReadOnlyList<TableSchema> All { get; } = new[] { Map, AnimSet, EquipVisual };
    }
}
