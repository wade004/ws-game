using System;
using System.Collections.Generic;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// L0 自有表的 <see cref="TableSchema"/> 登记（见任务书"登记 L0 拥有的表"）：
    /// <c>found.event_catalog</c>、<c>l10n.locale</c>、<c>l10n.text</c>。<c>stat.definition</c>
    /// 属 L1，不在本类登记，由调用方（如测试）临时构造 <see cref="TableSchema"/> 并
    /// <see cref="IDataRegistry.RegisterSchema"/>。
    /// <para>
    /// 判断记录：本类只是"当前阶段暂存处"——按 <c>README.md</c>"不负责什么"一节，模块自有表
    /// 的 schema 将随各模块实现迁移到各自模块目录（如 <c>l10n</c> 归 T1-7 本地化模块），届时
    /// 应从本类移除对应登记，本类最终应该只剩 <c>found.*</c> 表（真正属于 L0 <c>data_registry</c>
    /// 自身职责边界内的表）。
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
                new FieldSchema("fields", FieldKind.Array, required: true, description: "事件携带的字段名列表，只登记名字不登记类型"),
                new FieldSchema("description", FieldKind.String, required: false),
            },
            migrations: Array.Empty<TableMigration>(),
            isRegistryTable: true);

        /// <summary>支持语言清单与回退链（04 第 7.2 节）。主键 <c>id</c>，内容表（domain 前缀
        /// 必须等于表名首段 <c>l10n</c>）。</summary>
        public static readonly TableSchema L10nLocale = new TableSchema(
            name: "l10n.locale",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("fallback", FieldKind.Id, required: false, description: "回退语言 id，可为空"),
                new FieldSchema("is_default", FieldKind.Bool, required: true),
            },
            migrations: Array.Empty<TableMigration>());

        /// <summary>本地化文本表（04 第 7.2 节）。主键 <c>key</c> + <c>locale</c> 复合
        /// （见 <see cref="TableSchema.HasLocaleCompositeKey"/>、<c>data/README.md</c>"记录主键"
        /// 的 l10n.text 例外）。</summary>
        public static readonly TableSchema L10nText = new TableSchema(
            name: "l10n.text",
            primaryKey: "key",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("key", FieldKind.String, required: true, description: "文本键，格式 l10n.<来源域>.<来源记录name>.<字段名>"),
                new FieldSchema("locale", FieldKind.Reference, required: true, referenceTable: "l10n.locale"),
                new FieldSchema("text", FieldKind.String, required: true),
            },
            migrations: Array.Empty<TableMigration>(),
            isRegistryTable: true,
            hasLocaleCompositeKey: true);

        /// <summary>全部内置 schema，供 <see cref="IDataRegistry.RegisterSchema"/> 批量登记。</summary>
        public static IReadOnlyList<TableSchema> All { get; } = new[]
        {
            FoundEventCatalog,
            L10nLocale,
            L10nText,
        };
    }
}
