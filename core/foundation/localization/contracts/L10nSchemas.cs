using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.Localization
{
    /// <summary>
    /// <c>l10n.locale</c>/<c>l10n.text</c> 两张表的 <see cref="TableSchema"/> 登记（04 第 7.2 节）。
    /// <para>
    /// 判断记录：本任务（T1-7b1）把这两张表的 schema 从 <c>data_registry/core/BuiltinSchemas.cs</c>
    /// 迁到本模块——<c>BuiltinSchemas.cs</c> 顶部注释早已预告"归各模块目录（如 l10n 归 T1-7
    /// 本地化模块）"，本次实施落实该预告，定义内容原样保留，只是搬家：字段、主键规则、
    /// <c>IsRegistryTable</c>/<c>HasLocaleCompositeKey</c> 取值均与原
    /// <c>BuiltinSchemas.L10nLocale</c>/<c>BuiltinSchemas.L10nText</c> 完全一致，
    /// 依赖方（如 <c>data_registry/tests/DataRegistryTests.cs</c>）改为引用
    /// <see cref="Locale"/>/<see cref="Text"/>。
    /// </para>
    /// </summary>
    public static class L10nSchemas
    {
        /// <summary>支持语言清单与回退链（04 第 7.2 节）。主键 <c>id</c>，内容表（domain 前缀
        /// 必须等于表名首段 <c>l10n</c>）。</summary>
        public static readonly TableSchema Locale = new TableSchema(
            name: "l10n.locale",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "语言 id，如 l10n.locale.zh_cn"),
                new FieldSchema("fallback", FieldKind.Id, required: false, description: "回退语言 id，可为空"),
                new FieldSchema("is_default", FieldKind.Bool, required: true,
                    description: "是否为默认语言；全表必须恰好一条为 true"),
            },
            migrations: Array.Empty<TableMigration>());

        /// <summary>本地化文本表（04 第 7.2 节）。主键 <c>key</c> + <c>locale</c> 复合
        /// （见 <see cref="TableSchema.HasLocaleCompositeKey"/>、<c>data/README.md</c>"记录主键"
        /// 的 l10n.text 例外）。</summary>
        public static readonly TableSchema Text = new TableSchema(
            name: "l10n.text",
            primaryKey: "key",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("key", FieldKind.String, required: true, description: "文本键，格式 l10n.<来源域>.<来源记录name>.<字段名>"),
                new FieldSchema("locale", FieldKind.Reference, required: true, referenceTable: "l10n.locale",
                    description: "引用 l10n.locale，本条文本所属语言"),
                new FieldSchema("text", FieldKind.String, required: true, description: "本地化文本内容"),
            },
            migrations: Array.Empty<TableMigration>(),
            isRegistryTable: true,
            hasLocaleCompositeKey: true);

        /// <summary>全部内置 schema，供 <see cref="IDataRegistry.RegisterSchema"/> 批量登记。</summary>
        public static IReadOnlyList<TableSchema> All { get; } = new[]
        {
            Locale,
            Text,
        };
    }
}
