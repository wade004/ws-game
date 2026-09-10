using System;
using System.Collections.Generic;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// L0 自有表的 <see cref="TableSchema"/> 登记（见任务书"登记 L0 拥有的表"）：目前只剩
    /// <c>found.event_catalog</c>。<c>stat.definition</c> 属 L1，不在本类登记，由调用方
    /// （如测试）临时构造 <see cref="TableSchema"/> 并 <see cref="IDataRegistry.RegisterSchema"/>。
    /// <para>
    /// 判断记录（T1-7b1）：<c>l10n.locale</c>/<c>l10n.text</c> 两张表的 <see cref="TableSchema"/>
    /// 原登记在本类（本类原本的类型注释已预告"届时应从本类移除对应登记...归各模块目录（如
    /// l10n 归 T1-7 本地化模块）"），本次实施把它们原样搬到
    /// <c>core/foundation/localization/contracts/L10nSchemas.cs</c>
    /// （<c>Core.Foundation.Localization.L10nSchemas.Locale</c>/<c>.Text</c>），字段定义、
    /// 主键规则、<c>IsRegistryTable</c>/<c>HasLocaleCompositeKey</c> 取值均未改变，只是搬家；
    /// 依赖方（如 <c>data_registry/tests/DataRegistryTests.cs</c>）已改为引用
    /// <c>L10nSchemas.Locale</c>/<c>L10nSchemas.Text</c>。本类现在只剩 <c>found.*</c> 表
    /// （真正属于 L0 <c>data_registry</c> 自身职责边界内的表），与本类原判断记录的目标状态一致。
    /// </para>
    /// </summary>
    public static class BuiltinSchemas
    {
        /// <summary>
        /// 事件词汇登记表（04 第 1.1 节）。主键 <c>key</c>，跨 domain 登记表（见
        /// <c>data/README.md</c>"记录主键"、<c>event_bus/schema/found.event_catalog.md</c>）。
        /// </summary>
        public static readonly TableSchema FoundEventCatalog = new TableSchema(
            name: "found.event_catalog",
            primaryKey: "key",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("key", FieldKind.String, required: true, description: "事件 key，须为合法 Id 格式（^[a-z][a-z0-9_]*(\\.[a-z0-9_]+)+$）"),
                new FieldSchema("domain", FieldKind.String, required: true, description: "事件所属 domain，不保证等于 key 的第一段"),
                // ADR-0024 第二批登记：元素结构本身就是"只登记名字不登记类型"的裸字符串数组
                // （EventDefinition.Fields 类型为 IReadOnlyList<string>，data/_framework/found/
                // found.event_catalog.json 全部 90 行实测核对一致），Array.Item = String 精确表达
                // 这条约束本身，不需要 Map（此前 allowlist 条目的"Array.Item 机制无法表达"是误判——
                // 该条约束与"是否存在消费固定子键的解析代码"无关，纯粹是数组元素的类型声明）。
                new FieldSchema("fields", FieldKind.Array, required: true,
                    item: new FieldSchema("<field_name>", FieldKind.String, required: true, description: "事件携带的字段名"),
                    description: "事件携带的字段名列表，只登记名字不登记类型"),
                new FieldSchema("description", FieldKind.String, required: false,
                    description: "该事件的说明文本，供编辑器/文档展示，可为空"),
            },
            migrations: Array.Empty<TableMigration>(),
            isRegistryTable: true)
            .WithOwnership(SchemaLayer.Foundation, "foundation");

        /// <summary>全部内置 schema，供 <see cref="IDataRegistry.RegisterSchema"/> 批量登记。</summary>
        public static IReadOnlyList<TableSchema> All { get; } = new[]
        {
            FoundEventCatalog,
        };
    }
}
