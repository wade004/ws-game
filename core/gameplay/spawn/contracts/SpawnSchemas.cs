using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Spawn
{
    /// <summary>
    /// <c>spawn.table</c> 的 <see cref="TableSchema"/> 声明（见 05_对象模型与世界.md 第 5.1 节字段表）。
    /// 调用方需要 <c>RegisterSchema(SpawnSchemas.Table)</c> 后才能加载对应数据文件（惯例同
    /// <c>Core.Gameplay.AreaTrigger.AreaTriggerSchemas</c>）。
    /// </summary>
    public static class SpawnSchemas
    {
        public static readonly string[] RespawnPolicyValues = { "on_map_enter", "once", "never", "timer" };

        public static TableSchema Table { get; } = new TableSchema(
            name: "spawn.table",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "spawn.<name>"),
                // 判断记录同 AreaTriggerSchemas.TriggerDef.map_id：world.map 暂存于
                // core/foundation/scene_router，本模块不跨目录耦合其加载时机，只做 Id 格式校验。
                new FieldSchema("map_id", FieldKind.Id, required: true, description: "所属地图，指向 world.map"),
                // 判断记录：content_ref 可指向 creature.template 或 gobj.template 两张不同表，
                // FieldSchema.ReferenceTable/ReferenceDomain 只支持单一目标，无法同时表达"二选一"；
                // 域名合法性（creature|gobj）与目标行是否存在改由 SpawnContentRefRule（本模块自有
                // IValidationRule）负责，见 schema/SpawnValidationRules.cs。
                new FieldSchema("content_ref", FieldKind.Id, required: true,
                    description: "指向 creature.template 或 gobj.template（见 SpawnContentRefRule）"),
                new FieldSchema("position", FieldKind.Vec2, required: true, description: "刷新点坐标"),
                new FieldSchema("facing", FieldKind.Number, required: false, description: "初始朝向，缺省 0"),
                new FieldSchema("condition", FieldKind.Expr, required: false, description: "刷新条件（可空）"),
                new FieldSchema("respawn_policy", FieldKind.Enum, required: true, enumValues: RespawnPolicyValues),
                new FieldSchema("respawn_timer", FieldKind.Number, required: false,
                    description: "respawn_policy=timer 时必填，见 SpawnRespawnPolicyFieldGroupRule"),
            });
    }
}
