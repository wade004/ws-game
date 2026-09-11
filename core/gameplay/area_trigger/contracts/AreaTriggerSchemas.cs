using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Gameplay.AreaTrigger
{
    /// <summary>
    /// <c>area.trigger_def</c> 的 <see cref="TableSchema"/> 声明（见 05_对象模型与世界.md 第 1.5、
    /// 3.5、7、7.1 节）。调用方需要 <c>RegisterSchema(AreaTriggerSchemas.TriggerDef)</c> 后才能加载
    /// 对应数据文件（惯例同 <c>core/carriers/gobj</c> 的 <c>GobjSchemas</c>：本模块不自动注册）。
    /// <para>
    /// ADR-0019 首批登记（F1b，04 第 3.2 节"复合字段子结构登记"）：<c>shape</c> 登记为按判别字段
    /// <c>kind</c> 分派的 <see cref="VariantSchema"/>（见 <see cref="BuildShapeVariants"/>），与
    /// <see cref="AreaTriggerShapeJson.Parse"/> 唯一权威对照；原手写的 <c>AreaTriggerShapeKindRule</c>
    /// （只检查 <c>shape.kind</c> 合法性）已被 <c>DataRegistry</c> 内置的 <c>variant_discriminator</c>
    /// 检查完全覆盖，**整条退役删除**。<c>params</c> 因判别字段 <c>trigger_type</c> 与 <c>params</c>
    /// 平级（不在 <c>params</c> 对象内部）而无法使用 <see cref="VariantSchema"/>（见
    /// <see cref="ParamsSchema"/> 判断记录），改登记为 <see cref="FieldSchema.Fields"/>（四种
    /// <c>trigger_type</c> 用到的子字段并集，全部非必填）；<c>AreaTriggerParamsFieldGroupRule</c>
    /// （"按 trigger_type 决定哪些 params 子字段必填"）表达的是跨字段业务判断，登记层无法覆盖，
    /// **原样保留**。完整对照与判断记录见 <c>schema/README.md</c>"子结构登记表"/"退役规则"两节。
    /// </para>
    /// </summary>
    public static class AreaTriggerSchemas
    {
        /// <summary><c>shape.kind</c> 合法取值（见 05 第 3.5 节 Shape 联合类型四种）。</summary>
        public static readonly string[] ShapeKindValues = { "circle", "cone", "line", "rect" };

        /// <summary><c>trigger_type</c> 合法取值（见 05 第 7 节四种类型）。</summary>
        public static readonly string[] TriggerTypeValues =
        {
            "map_transition", "quest_explore", "encounter_start", "script",
        };

        // -----------------------------------------------------------------
        // shape：AreaTriggerShapeJson.Parse 唯一权威，按 kind 分派四种形状（05 第 3.5 节）。
        // 判断记录：GetNumber/ParseCenter 对缺失或类型不符的字段一律静默兜底为 0/Vec2.Zero，不抛
        // 异常（与 EncounterSchemas.BoundsShapeSchema 依赖的 EncounterShapeJson.RequireNumber"缺失
        // 即抛"形成对照，见该类型判断记录）——因此四个分支下的全部子字段（含公共的 center、cone/
        // line/rect 共用的 rotation）均登记为非必填，只有判别字段 kind 仍强制"存在且合法"（由
        // VariantSchema 机制内置的 variant_discriminator 检查覆盖）。
        // -----------------------------------------------------------------
        private static VariantSchema BuildShapeVariants()
        {
            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
            {
                ["circle"] = new[]
                {
                    new FieldSchema("radius", FieldKind.Number, required: false,
                        description: "Shape.Circle 半径；AreaTriggerShapeJson.GetNumber 对缺失/非 Number 兜底为 0"),
                },
                ["cone"] = new[]
                {
                    new FieldSchema("rotation", FieldKind.Number, required: false,
                        description: "Shape.Cone 的 Direction；缺失/非 Number 兜底为 0"),
                    new FieldSchema("angle", FieldKind.Number, required: false, description: "缺失/非 Number 兜底为 0"),
                    new FieldSchema("radius", FieldKind.Number, required: false, description: "缺失/非 Number 兜底为 0"),
                },
                ["line"] = new[]
                {
                    new FieldSchema("rotation", FieldKind.Number, required: false,
                        description: "Shape.Line 的 Direction；缺失/非 Number 兜底为 0"),
                    new FieldSchema("length", FieldKind.Number, required: false, description: "缺失/非 Number 兜底为 0"),
                    new FieldSchema("width", FieldKind.Number, required: false, description: "缺失/非 Number 兜底为 0"),
                },
                ["rect"] = new[]
                {
                    new FieldSchema("rotation", FieldKind.Number, required: false, description: "缺失/非 Number 兜底为 0"),
                    new FieldSchema("length", FieldKind.Number, required: false,
                        description: "换算为 Shape.HalfExtents.X 时除 2；缺失/非 Number 兜底为 0"),
                    new FieldSchema("width", FieldKind.Number, required: false,
                        description: "换算为 Shape.HalfExtents.Y 时除 2；缺失/非 Number 兜底为 0"),
                },
            };

            var commonFields = new[]
            {
                new FieldSchema("center", FieldKind.Vec2, required: false,
                    description: "circle 的圆心/cone·line·rect 的原点；ParseCenter 对缺失或非 {x,y} 兜底为 Vec2.Zero"),
            };

            return new VariantSchema("kind", cases, commonFields);
        }

        public static readonly FieldSchema ShapeSchema = new FieldSchema(
            "shape", FieldKind.Object, required: true, variants: BuildShapeVariants(),
            description: "{kind, radius?, angle?, length?, width?, center{x,y}?, rotation?}，见 05 第 3.5 节、" +
                "AreaTriggerShapeJson.Parse");

        // -----------------------------------------------------------------
        // params：AreaTriggerDef.FromRecord 唯一权威，按 trigger_type 分派必填字段（05 第 7 节）。
        // 判断记录（Variants 不适用于本字段）：判别字段 trigger_type 是 area.trigger_def 行内与
        // params 平级的字段，不在 params 对象内部；VariantSchema.Discriminator 要求判别字段与被
        // 判别的子字段处在同一个 JsonObject 里（DataRegistry.ValidateVariantObject 在 params 自身
        // 的 JsonObject 里找 Discriminator），若把 Discriminator 设为 "trigger_type"，每条记录的
        // params 对象内都不会有这个键，会恒报 variant_discriminator 缺失——这与 SpawnSchemas.Table
        // 的 respawn_policy/respawn_timer 是一对平级字段（判别字段与被判别字段不同级）同属一类结构
        // 边界，不是 ADR-0019 Fields/Item/Variants 机制当前设计要覆盖的场景。改用 Fields 登记四种
        // trigger_type 分别用到的子字段并集，全部标记非必填——真正"哪些字段必填视 trigger_type 而定"
        // 这条业务判断，登记层表达不了，继续保留在 AreaTriggerParamsFieldGroupRule（不退役，见
        // schema/README.md"退役规则"一节）；本次登记新增的是此前完全没有的"存在时类型必须是合法
        // Id"校验，与 AreaTriggerParamsFieldGroupRule 的必填性检查互不重叠，不会对同一缺陷双报。
        // -----------------------------------------------------------------
        public static readonly FieldSchema ParamsSchema = new FieldSchema(
            "params", FieldKind.Object, required: true, fields: new[]
            {
                new FieldSchema("target_map", FieldKind.Id, required: false,
                    description: "trigger_type=map_transition 必填（AreaTriggerParamsFieldGroupRule 校验），本登记只管类型；" +
                        "world.map 未随本模块登记加载，判断记录同 map_id，退回 Id"),
                new FieldSchema("spawn_point", FieldKind.Id, required: false,
                    description: "trigger_type=map_transition 可选；缺省由 ISceneRouter 落在目标地图默认出生点"),
                new FieldSchema("encounter_ref", FieldKind.Id, required: false,
                    description: "trigger_type=encounter_start 必填（同上，本登记只管类型）；经 " +
                        "AreaTriggerOptions.EncounterStartRequested 委托分发，本模块不直接依赖 core/gameplay/encounter" +
                        "（判断记录同 EncounterSchemas.UnitItemSchema.spawn_ref 的决耦惯例），退回 Id" +
                        "（消费方反馈第 29 条：按字段名与触发语义推定指向 encounter.def，登记为软引用，仅供内容工具补全/跳转）")
                    .WithSoftReference(table: "encounter.def"),
                new FieldSchema("hook_id", FieldKind.Id, required: false,
                    description: "trigger_type=script 必填（同上，本登记只管类型）；found.hook 当前无实现级 schema 登记，" +
                        "判断记录同 EncounterSchemas.PhaseItemSchema.on_enter_hook，退回 Id"),
            },
            description: "按 trigger_type 分派：map_transition {target_map, spawn_point?}；" +
                "encounter_start {encounter_ref}；script {hook_id}；quest_explore {}；" +
                "字段组必填性见 AreaTriggerParamsFieldGroupRule（Variants 不适用，见本字段判断记录）");

        public static TableSchema TriggerDef { get; } = new TableSchema(
            name: "area.trigger_def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "area.<name>"),
                // 判断记录：05 第 1.5 节 map_id 概念上指向 world.map，但 world.map 目前暂存于
                // core/foundation/scene_router（见该模块 WorldMapSchema.cs 顶部判断记录，属 L0，
                // 待 L4 对象模型与世界模块落地时整表迁移）；本模块不跨目录依赖 scene_router 的具体
                // 数据表登记时机（测试/内容管线可能先加载本表再加载 world.map），改用 FieldKind.Id
                // 只做格式校验，不做跨表引用完整性检查（同 SpawnSchemas.Table.map_id、
                // SpawnSchemas.Table.content_ref 的处理惯例，见该文件判断记录）。
                new FieldSchema("map_id", FieldKind.Id, required: true,
                    description: "所属地图，指向 world.map（见 05 第 1.5 节；消费方反馈第 30 条：登记为软引用，仅供内容工具补全/跳转）")
                    .WithSoftReference(table: "world.map"),
                ShapeSchema,
                new FieldSchema("trigger_type", FieldKind.Enum, required: true, enumValues: TriggerTypeValues,
                    description: "四种类型之一，见 05 第 7 节"),
                new FieldSchema("condition", FieldKind.Expr, required: false, description: "附加触发条件（可空）"),
                new FieldSchema("one_shot", FieldKind.Bool, required: false, description: "是否只触发一次，缺省 false"),
                ParamsSchema,
                new FieldSchema("name_key", FieldKind.TextKey, required: false, description: "显示名文本键（可选）"),
            }).WithOwnership(SchemaLayer.Gameplay, "area");
    }
}
