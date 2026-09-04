using Core.Foundation.DataRegistry;

namespace Core.Gameplay.AreaTrigger
{
    /// <summary>
    /// <c>area.trigger_def</c> 的 <see cref="TableSchema"/> 声明（见 05_对象模型与世界.md 第 1.5、
    /// 3.5、7、7.1 节）。调用方需要 <c>RegisterSchema(AreaTriggerSchemas.TriggerDef)</c> 后才能加载
    /// 对应数据文件（惯例同 <c>core/carriers/gobj</c> 的 <c>GobjSchemas</c>：本模块不自动注册）。
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
                    description: "所属地图，指向 world.map（见 05 第 1.5 节）"),
                new FieldSchema("shape", FieldKind.Object, required: true,
                    description: "触发范围，{kind, radius, angle, length, width, center{x,y}, rotation}，见 05 第 3.5 节；字段组完整性见 AreaTriggerShapeKindRule"),
                new FieldSchema("trigger_type", FieldKind.Enum, required: true, enumValues: TriggerTypeValues,
                    description: "四种类型之一，见 05 第 7 节"),
                new FieldSchema("condition", FieldKind.Expr, required: false, description: "附加触发条件（可空）"),
                new FieldSchema("one_shot", FieldKind.Bool, required: false, description: "是否只触发一次，缺省 false"),
                new FieldSchema("params", FieldKind.Object, required: true,
                    description: "按 trigger_type 解释：map_transition {target_map, spawn_point}；encounter_start {encounter_ref}；script {hook_id}；quest_explore {}；字段组完整性见 AreaTriggerParamsFieldGroupRule"),
                new FieldSchema("name_key", FieldKind.TextKey, required: false, description: "显示名文本键（可选）"),
            });
    }
}
