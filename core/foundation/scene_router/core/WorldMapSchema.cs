using System;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.SceneRouter
{
    /// <summary>
    /// <c>world.map</c> 的 <see cref="TableSchema"/> 登记（见 05_对象模型与世界.md 第 4.1 节）。
    /// <para>
    /// 判断记录：<c>world.map</c> 按 01_分层与依赖.md 归属 L4（对象模型与世界），不属于本模块
    /// （L0 <c>scene_router</c>）。任务书允许两种处理方式之一：登记完整字段但只做类型校验，
    /// 或只登记场景路由读取的字段、允许其余字段存在。经查
    /// <c>DataRegistry.RunFieldValidation</c> 实现（见 <c>core/foundation/data_registry/core/DataRegistry.cs</c>）：
    /// 字段校验只遍历 <see cref="TableSchema.Fields"/> 里登记的字段，从不检查记录里是否存在
    /// 未登记的多余字段——也就是说"只登记场景路由需要的字段"本来就不会让
    /// <c>regions</c>/<c>teleport_points</c>/<c>music_ref</c>/<c>allowed_difficulties</c> 这些
    /// 未登记字段的存在触发任何校验错误，两种处理方式在当前 <c>DataRegistry</c> 实现下效果
    /// 等价。因此选择更简单的"只登记本模块读取的字段"：<c>id</c>、<c>scene_ref</c>、
    /// <c>nav_ref</c>、<c>spawn_points</c>。本 schema 登记只是当前阶段暂存处，待 L4
    /// （对象模型与世界）模块落地时应整表迁移过去并补全其余字段（regions/teleport_points/
    /// music_ref/allowed_difficulties），与 data_registry 模块 <c>BuiltinSchemas</c> 类型注释
    /// 记录的"暂存处，届时应移除"同一惯例。
    /// </para>
    /// </summary>
    public static class WorldMapSchema
    {
        public static readonly TableSchema Table = new TableSchema(
            name: "world.map",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "world.<地图名>"),
                new FieldSchema("scene_ref", FieldKind.String, required: true, description: "场景资源引用（不含路径），由引擎适配层解析加载"),
                new FieldSchema("nav_ref", FieldKind.String, required: true, description: "导航资源引用（可行走区域、遮挡层数据），由引擎适配层加载后经 INavigation2D 暴露"),
                new FieldSchema("spawn_points", FieldKind.Array, required: true, description: "玩家出生点/复活点清单，List<{id, position, facing}>；本模块只读取第 0 个元素的 position 作为默认出生点"),
            },
            migrations: Array.Empty<TableMigration>());
    }
}
