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
    /// （<c>List&lt;{id, position}&gt;</c> 嵌套对象数组，与既有 <c>spawn_points</c> 同构）、
    /// <c>music_ref</c>（<c>String</c>）、<c>allowed_difficulties</c>（<c>List&lt;Id&gt;</c>，
    /// 见 08 难度档位，用 <see cref="FieldKind.IdList"/>）。四个字段均不声明
    /// <see cref="FieldSchema.ReferenceTable"/>/<see cref="FieldSchema.ReferenceDomain"/>——
    /// <c>regions</c> 引用的子区域划分与 <c>allowed_difficulties</c> 引用的难度档位当前均无独立
    /// 登记表可供引用完整性校验（08 难度档位只在运行期以 <see cref="Core.Foundation.Common.Id"/> 标识，未登记进
    /// <c>IDataRegistry</c>），登记完整性校验属于后续独立收口项。
    /// </para>
    /// <para>
    /// P3-07 勘误（外部审计 audit-c9ff301-20260909）：本节此前留下一句过期描述——
    /// "<c>teleport_points</c>……同样只做 <see cref="FieldKind.Array"/> 类型校验，不展开逐条元素
    /// 形状"，与下面 <see cref="PointItemSchema"/> 实际登记（<c>item: PointItemSchema</c>，展开到
    /// <c>id</c>/<c>position</c> 两个子字段）已经不一致——ADR-0019 之后的登记升级没有同步更新这句
    /// 注释，是纯文档漂移，不是运行期行为差异。<c>spawn_points</c>/<c>teleport_points</c> 两个点
    /// 数组共用同一个 <c>PointItemSchema</c>，但两者的实际"必填"语义按用途各不相同、不强迫所有点
    /// 统一字段（见该类型判断记录）：
    /// <list type="bullet">
    /// <item><c>spawn_points</c> 数组本身（<see cref="FieldKind.Array"/>）不表达最小长度/首元素
    /// 必填 <c>position</c> 这类跨元素约束，<see cref="SceneDescriptor.FromRecord"/> 只读第 0 个
    /// 元素的 <c>position</c> 作为默认出生点，缺失时构造期直接抛
    /// <see cref="Core.Foundation.DataRegistry.DataFieldException"/>——这是登记层表达不了的业务
    /// 约束，见下方 <see cref="WorldMapSpawnPointsValidationRule"/> 在 report 阶段补齐。数组内第
    /// 0 个之外的其余出生点、以及 <c>teleport_points</c> 的全部点，本身既可以是匿名点（只有
    /// <c>position</c>、供直接坐标引用）也可以是命名点（只有 <c>id</c>、被 <c>teleport_target_ref</c>
    /// 一类外部引用按 id 查找，实际坐标随后由被引用方决定或压根不需要），因此 <c>id</c>/
    /// <c>position</c> 均保持可选，不强制两者都填。</item>
    /// <item>"命名引用目标"（<c>teleport_points</c> 里带 <c>id</c> 的点，供
    /// <c>Core.Gameplay.Assembly.TeleportTargetResolver</c> 按 <c>id</c> 查找）与"匿名坐标点"
    /// （只有 <c>position</c>）是同一个 <see cref="PointItemSchema"/> 结构下两种不同的使用方式，
    /// 不是两种不同的 schema——<c>TeleportTargetResolver.TryReadPosition</c> 对缺失字段温和降级为
    /// "找不到"而非抛异常（见该方法判断记录），因此这里不新增引用完整性校验；跨地图/跨表按 id
    /// 引用这些命名点是否存在，属于内容管线的更高层职责（04/05 尚未给出具体规则），本次不越权
    /// 替 04/05 拍板。</item>
    /// </list>
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
                    new FieldSchema("x", FieldKind.Number, required: true, description: "世界坐标 x，与 scene_ref 场景资源的坐标系一致"),
                    new FieldSchema("y", FieldKind.Number, required: true, description: "世界坐标 y，与 scene_ref 场景资源的坐标系一致"),
                },
                    description: "提供时 x/y 均必填；spawn_points 第 0 个元素若缺失 position，" +
                        "SceneDescriptor.FromRecord 构造期直接抛异常（非登记层校验，登记层不重复表达）"),
            }, description: "出生点/传送点共用条目结构：{id?, position?}，供 spawn_points/teleport_points 复用");

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

    /// <summary>
    /// P3-07 根治（外部审计 audit-c9ff301-20260909）："<c>spawn_points</c> 至少一条、且第 0 条必须
    /// 携带合法 <c>position</c>"这条业务约束，<see cref="FieldSchema"/> 的
    /// <see cref="FieldKind.Array"/>/<see cref="WorldMapSchema.PointItemSchema"/>（<c>id</c>/
    /// <c>position</c> 均可选，见该字段判断记录）登记层表达不了，此前完全没有校验——违反的记录能
    /// 完整通过 <c>DataRegistry.LoadAll()</c>（0 error），只在真正切场景经
    /// <see cref="SceneDescriptor.FromRecord"/> 解析时才抛
    /// <see cref="Core.Foundation.DataRegistry.DataFieldException"/>。本规则在 report 阶段补齐同一
    /// 条判定（与 <see cref="SceneDescriptor.FromRecord"/> 的两个异常分支一一对应），使用真实
    /// <c>world.map</c> 数据的消费方在加载期而不是切场景那一刻发现内容缺陷。
    /// </summary>
    public sealed class WorldMapSpawnPointsValidationRule : IValidationRule
    {
        private const string CheckName = "world_map_spawn_points_first_position";

        public System.Collections.Generic.IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(WorldMapSchema.Table.Name))
            {
                if (!record.TryGetArray("spawn_points", out var spawnPoints))
                {
                    // 缺失/类型不符属于 required_field/field_type 职责，本规则不重复报错。
                    continue;
                }

                if (spawnPoints.Count == 0)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, WorldMapSchema.Table.Name, CheckName,
                        "spawn_points 至少需要一个出生点才能确定默认出生点", recordKey: record.Key, field: "spawn_points");
                    continue;
                }

                if (!HasValidPosition(spawnPoints[0]))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, WorldMapSchema.Table.Name, CheckName,
                        "spawn_points 第 0 个出生点缺少合法 position（{\"x\": Number, \"y\": Number}）",
                        recordKey: record.Key, field: "spawn_points");
                }
            }
        }

        private static bool HasValidPosition(Core.Foundation.Common.Json.JsonValue firstPoint) =>
            firstPoint is Core.Foundation.Common.Json.JsonObject first
            && first.TryGetValue("position", out var posVal)
            && posVal is Core.Foundation.Common.Json.JsonObject posObj
            && posObj.TryGetValue("x", out var xv) && xv is Core.Foundation.Common.Json.JsonNumber
            && posObj.TryGetValue("y", out var yv) && yv is Core.Foundation.Common.Json.JsonNumber;
    }
}
