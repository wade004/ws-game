using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.AppLifecycle
{
    /// <summary>
    /// <c>found.game_state</c> 表的 <see cref="TableSchema"/> 登记（见 04_数据与内容管线.md
    /// 第 1.1 节总索引"游戏状态机的状态与合法迁移定义"、本模块 <c>schema/found.game_state.md</c>
    /// 字段表）。
    /// <para>
    /// 判断记录（主键字段名为 <c>id</c> 而非 <c>key</c>，<see cref="TableSchema.IsRegistryTable"/>
    /// 取默认值 false）：本表虽登记在 <c>found</c> domain 之下（<c>data/_framework/found/</c>
    /// 目录），但每一行的 <c>id</c> 形如 <c>found.state.&lt;from&gt;_to_&lt;to&gt;</c>——domain 前缀
    /// 本身就是 <c>found</c>，与表名首段一致，不属于 <c>found.event_catalog</c>/
    /// <c>found.input_action</c> 那种"记录 domain 与表名 domain 不一致"的跨 domain 登记表情形
    /// （见 <c>data/README.md</c>"记录主键"一节），因此不需要 <see cref="TableSchema.IsRegistryTable"/>
    /// 豁免"id 的 domain 前缀应等于表名首段"检查，按普通内容表处理即可。
    /// </para>
    /// </summary>
    public static class FoundGameStateSchema
    {
        private static readonly string[] Kinds = { "main", "sub" };

        public static readonly TableSchema Table = new TableSchema(
            name: "found.game_state",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "found.state.<from>_to_<to>，见 schema/found.game_state.md"),
                new FieldSchema("from", FieldKind.String, required: true,
                    description: "转移起点状态名；kind=main 取 AppState 枚举名，kind=sub 取 SubStateId 名"),
                new FieldSchema("to", FieldKind.String, required: true,
                    description: "转移终点状态名，取值规则同 from"),
                new FieldSchema("kind", FieldKind.Enum, required: true, enumValues: Kinds,
                    description: "区分本行是主状态转移（main）还是 InWorld 子状态转移（sub）"),
                new FieldSchema("description", FieldKind.String, required: false),
            },
            migrations: Array.Empty<TableMigration>());

        /// <summary>全部内置 schema，供 <see cref="IDataRegistry.RegisterSchema"/> 批量登记
        /// （本模块目前只有一张表，保留该属性与其它模块的 <c>*Schemas.All</c>/<c>*Schema.All</c>
        /// 惯例一致）。</summary>
        public static IReadOnlyList<TableSchema> All { get; } = new[] { Table };
    }
}
