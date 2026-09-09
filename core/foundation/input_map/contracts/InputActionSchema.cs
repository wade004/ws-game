using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// <c>found.input_action</c> 表的 <see cref="TableSchema"/> 登记（见 04_数据与内容管线.md
    /// 第 1.1 节、01_分层与依赖.md L0 模块表 <c>input_map</c> 行、本模块
    /// <c>schema/found.input_action.md</c>）。
    /// <para>
    /// 判断记录：表名 <c>found.input_action</c> 的 domain 前缀是 <c>found</c>，但记录本身的
    /// id 域名是 <c>input</c>（见 04 第 2.2 节域名清单 <c>input</c> 行"输入动作"、示例
    /// <c>input.action.primary_attack</c>）——与 <c>found.event_catalog</c> 同理，属于
    /// "found 表登记 input 域的记录"。任务书拍板：按登记表处理，主键字段名为 <c>key</c>
    /// （而不是一般内容表的 <c>id</c>），<see cref="TableSchema.IsRegistryTable"/> = true，
    /// 与 <c>data/README.md</c>"记录主键"一节、<c>core/foundation/data_registry/README.md</c>
    /// "主键规则"一节的登记表约定完全一致。
    /// </para>
    /// </summary>
    public static class InputActionSchema
    {
        public static readonly TableSchema Table = new TableSchema(
            name: "found.input_action",
            primaryKey: "key",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("key", FieldKind.Id, required: true,
                    description: "动作 id，如 input.action.move；登记表主键字段名为 key（见类型级判断记录）"),
                new FieldSchema("kind", FieldKind.Enum, required: true,
                    enumValues: new[] { "button", "axis1d", "axis2d" },
                    description: "button：按下/抬起；axis1d：一维轴；axis2d：二维轴（如摇杆/方向输入）"),
                new FieldSchema("default_bindings", FieldKind.Array, required: true,
                    item: new FieldSchema("<binding>", FieldKind.String, required: true,
                        description: "单条默认绑定字符串，语法见 core/foundation/input_map/README.md"),
                    description: "默认绑定字符串数组（ActionDefinition.FromRecord 对非字符串元素抛异常），" +
                        "语法见 core/foundation/input_map/README.md；至少一项这条业务判断留在 ActionDefinition 构造函数（登记层不表达非空数组）"),
                new FieldSchema("rebind_group", FieldKind.String, required: false,
                    description: "重绑分组，缺省视为 \"default\""),
                new FieldSchema("description", FieldKind.String, required: false,
                    description: "该输入动作的说明文本，供编辑器/文档展示，可为空"),
            },
            migrations: Array.Empty<TableMigration>(),
            isRegistryTable: true);

        /// <summary>全部内置 schema，供 <see cref="IDataRegistry.RegisterSchema"/> 批量登记
        /// （本模块目前只有一张表，保留该属性与其它模块的 <c>*Schemas.All</c> 惯例一致）。</summary>
        public static IReadOnlyList<TableSchema> All { get; } = new[] { Table };
    }
}
