using System;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.SceneRouter
{
    /// <summary>
    /// <c>world.map</c> 的 <see cref="TableSchema"/> 登记（见 05_对象模型与世界.md 第 4.1 节）。
    /// <para>
    /// 判断记录：<c>world.map</c> 按 01_分层与依赖.md 归属 L4（对象模型与世界），不属于本模块
    /// （L0 <c>scene_router</c>）；本 schema 登记只是当前阶段暂存处，待 L4（对象模型与世界）
    /// 模块落地时应整表迁移过去，与 data_registry 模块 <c>BuiltinSchemas</c> 类型注释记录的
    /// "暂存处，届时应移除"同一惯例。
    /// </para>
    /// <para>
    /// DATA-DOC-01 根治（architecture/落地计划/audit-ac3b622-20260909，文档/代码待补，已收口）：
    /// 此前只登记场景路由自己读取的 <c>id</c>/<c>scene_ref</c>/<c>nav_ref</c>/<c>spawn_points</c>
    /// 四个字段，05 第 4.1 节"字段"表里另外四个字段——<c>regions</c>/<c>teleport_points</c>/
    /// <c>music_ref</c>/<c>allowed_difficulties</c>——只有文字结构声明，从未接入
    /// <see cref="TableSchema"/> 校验（代码待补）。现按 05 原文类型/必填性逐一补登记（均为可选
    /// 字段，05 原文"必填"列均为"否"）：<c>regions</c>（<c>List&lt;Id&gt;</c>，供 Expr 中
    /// <c>world</c> 分组按区域读取标志，用 <see cref="FieldKind.IdList"/>）、<c>teleport_points</c>
    /// （<c>List&lt;{id, position}&gt;</c> 嵌套对象数组，与既有 <c>spawn_points</c> 同构，同样只做
    /// <see cref="FieldKind.Array"/> 类型校验，不展开逐条元素形状——<c>Core.Gameplay.Assembly.
    /// TeleportTargetResolver</c> 已按此形状真实消费该字段，见该类型 <c>TeleportPointsField</c>
    /// 判断记录）、<c>music_ref</c>（<c>String</c>）、<c>allowed_difficulties</c>（<c>List&lt;Id&gt;</c>，
    /// 见 08 难度档位，用 <see cref="FieldKind.IdList"/>）。四个字段均不声明
    /// <see cref="FieldSchema.ReferenceTable"/>/<see cref="FieldSchema.ReferenceDomain"/>——
    /// <c>regions</c> 引用的子区域划分与 <c>allowed_difficulties</c> 引用的难度档位当前均无独立
    /// 登记表可供引用完整性校验（08 难度档位只在运行期以 <see cref="Id"/> 标识，未登记进
    /// <c>IDataRegistry</c>），登记完整性校验属于后续独立收口项，本次只补齐类型校验，与既有
    /// <c>spawn_points</c>/<c>scene_ref</c>/<c>nav_ref</c> 同样"只做类型校验、不做引用完整性"的
    /// 处理口径一致。
    /// </para>
    /// </summary>
    public static class WorldMapSchema
    {
        /// <summary><c>spawn_points</c>/<c>teleport_points</c> 元素共用结构：以运行时实际读取为准
        /// （<c>SceneDescriptor.FromRecord</c> 只读 <c>position</c>；
        /// <c>Core.Gameplay.Assembly.TeleportTargetResolver.TryReadPosition</c> 对
        /// <c>spawn_points</c>/<c>teleport_points</c> 两个字段共用同一套 <c>id</c>/<c>position</c>
        /// 读取逻辑，缺失时温和降级为"找不到"而非抛异常，故 <c>id</c>/<c>position</c> 均登记为
        /// 非必填）。判断记录：本类型注释文档提到的 <c>facing</c> 子字段全仓库搜索找不到任何运行时
        /// 读取代码（<c>spawn.table.facing</c> 是另一张不同的表），按 ADR-0019 通用规则 1 不登记。</summary>
        private static readonly FieldSchema PointItemSchema = new FieldSchema(
            "<point>", FieldKind.Object, required: true, fields: new[]
            {
                new FieldSchema("id", FieldKind.String, required: false,
                    description: "按 id 查找的命名传送/出生点键；缺失时 TeleportTargetResolver 温和降级为找不到"),
                new FieldSchema("position", FieldKind.Object, required: false, fields: new[]
                {
                    new FieldSchema("x", FieldKind.Number, required: true),
                    new FieldSchema("y", FieldKind.Number, required: true),
                },
                    description: "提供时 x/y 均必填；spawn_points 第 0 个元素若缺失 position，" +
                        "SceneDescriptor.FromRecord 构造期直接抛异常（非登记层校验，登记层不重复表达）"),
            });

        public static readonly TableSchema Table = new TableSchema(
            name: "world.map",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "world.<地图名>"),
                new FieldSchema("scene_ref", FieldKind.String, required: true, description: "场景资源引用（不含路径），由引擎适配层解析加载"),
                new FieldSchema("regions", FieldKind.IdList, required: false, description: "该地图内的子区域划分，供 Expr 中 world 分组按区域读取标志"),
                new FieldSchema("nav_ref", FieldKind.String, required: true, description: "导航资源引用（可行走区域、遮挡层数据），由引擎适配层加载后经 INavigation2D 暴露"),
                new FieldSchema("spawn_points", FieldKind.Array, required: true, item: PointItemSchema,
                    description: "玩家出生点/复活点清单，List<{id?, position?}>；本模块只读取第 0 个元素的 position 作为默认出生点"),
                new FieldSchema("teleport_points", FieldKind.Array, required: false, item: PointItemSchema,
                    description: "供传送类效果/AreaTrigger 引用的命名传送目标，List<{id?, position?}>"),
                new FieldSchema("music_ref", FieldKind.String, required: false, description: "背景音乐资源引用"),
                new FieldSchema("allowed_difficulties", FieldKind.IdList, required: false, description: "该地图允许应用的难度档位（见 08）"),
            },
            migrations: Array.Empty<TableMigration>());
    }
}
