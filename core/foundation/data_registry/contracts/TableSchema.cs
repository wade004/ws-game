using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;

namespace Core.Foundation.DataRegistry
{
    /// <summary>迁移函数：把一条旧结构的记录（JSON 对象）转换为下一版本结构的记录
    /// （见 04 第 3 节"迁移函数只做结构转换...不做业务判断"）。</summary>
    public delegate JsonObject MigrateDelegate(JsonObject row);

    /// <summary>
    /// 一条迁移链环节：<see cref="FromVersion"/> → <see cref="ToVersion"/>（见 04 第 3 节）。
    /// <see cref="TableSchema.Migrations"/> 里的多个环节按版本号顺序串成完整迁移链；
    /// 链中若缺少衔接某个历史版本的环节，加载期报 <c>schema_version</c> 错误（见 04 第 3 节
    /// "迁移链只增不减：任何历史版本永远可迁移到最新版本"）。
    /// </summary>
    public sealed class TableMigration
    {
        public int FromVersion { get; }

        public int ToVersion { get; }

        public MigrateDelegate Migrate { get; }

        public TableMigration(int fromVersion, int toVersion, MigrateDelegate migrate)
        {
            if (toVersion <= fromVersion)
            {
                throw new ArgumentException("ToVersion 必须大于 FromVersion", nameof(toVersion));
            }
            FromVersion = fromVersion;
            ToVersion = toVersion;
            Migrate = migrate ?? throw new ArgumentNullException(nameof(migrate));
        }
    }

    /// <summary>
    /// 一张数据表的静态结构声明：主键字段名、当前 schema 版本、字段清单、迁移链、是否为跨
    /// domain 登记表（见 04 第 1、2、3 节，<c>data/README.md</c>"记录主键"一节）。
    /// </summary>
    public sealed class TableSchema
    {
        /// <summary>表名，如 <c>"stat.definition"</c>、<c>"found.event_catalog"</c>。</summary>
        public string Name { get; }

        /// <summary>主键字段名：内容表固定为 <c>"id"</c>，登记表（跨 domain，见
        /// <c>data/README.md</c>"记录主键"）固定为 <c>"key"</c>。</summary>
        public string PrimaryKey { get; }

        public int CurrentSchemaVersion { get; }

        public IReadOnlyList<FieldSchema> Fields { get; }

        public IReadOnlyList<TableMigration> Migrations { get; }

        /// <summary>主键为 <c>key</c> 的登记表：不做"id 的 domain 前缀必须等于表名首段"检查
        /// （见 04 第 2.1 节、<c>data/README.md</c>"记录主键"一节的 <c>found.event_catalog</c>、
        /// <c>l10n.text</c> 例外）。</summary>
        public bool IsRegistryTable { get; }

        /// <summary>
        /// 是否为 <c>key</c> + <c>locale</c> 复合主键的登记表（本模块目前只有
        /// <c>l10n.text</c> 一张，见 04 第 7.2 节"没有 id 字段，主键是 key 与 locale 的复合键"）：
        /// 为 true 时，<see cref="DataRecord.Key"/> 取 <c>"{key字段值}@{locale字段值}"</c>。
        /// </summary>
        public bool HasLocaleCompositeKey { get; }

        /// <summary>true 表示这是 <see cref="Unschematized"/> 构造出的占位 schema
        /// （<see cref="DataRegistryOptions.FailOnUnknownTable"/> 为 false 时，未登记 schema
        /// 的表仍需要一个占位 <see cref="TableSchema"/> 才能构造 <see cref="DataRecord"/>）：
        /// 不做 <c>schema_version</c>/<c>required_field</c>/<c>field_type</c> 等依赖已知字段
        /// 结构的检查，只做信封与主键格式检查。</summary>
        public bool IsUnschematized { get; private set; }

        private readonly Dictionary<string, FieldSchema> _fieldsByName;

        public TableSchema(
            string name,
            string primaryKey,
            int currentSchemaVersion,
            IReadOnlyList<FieldSchema> fields,
            IReadOnlyList<TableMigration>? migrations = null,
            bool isRegistryTable = false,
            bool hasLocaleCompositeKey = false)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("表名不能为空", nameof(name));
            if (primaryKey != "id" && primaryKey != "key")
            {
                throw new ArgumentException("PrimaryKey 必须是 \"id\" 或 \"key\"", nameof(primaryKey));
            }
            if (currentSchemaVersion < 1)
            {
                throw new ArgumentException("CurrentSchemaVersion 必须从 1 起", nameof(currentSchemaVersion));
            }
            if (hasLocaleCompositeKey && primaryKey != "key")
            {
                throw new ArgumentException("复合主键（key+locale）要求 PrimaryKey 为 \"key\"", nameof(hasLocaleCompositeKey));
            }

            Name = name;
            PrimaryKey = primaryKey;
            CurrentSchemaVersion = currentSchemaVersion;
            Fields = fields ?? throw new ArgumentNullException(nameof(fields));
            Migrations = migrations ?? Array.Empty<TableMigration>();
            IsRegistryTable = isRegistryTable;
            HasLocaleCompositeKey = hasLocaleCompositeKey;

            _fieldsByName = new Dictionary<string, FieldSchema>(StringComparer.Ordinal);
            foreach (var f in Fields)
            {
                if (_fieldsByName.ContainsKey(f.Name))
                {
                    throw new ArgumentException($"字段 \"{f.Name}\" 在表 \"{name}\" 中重复声明", nameof(fields));
                }
                _fieldsByName.Add(f.Name, f);
            }
        }

        public FieldSchema? GetField(string name) => _fieldsByName.TryGetValue(name, out var f) ? f : null;

        /// <summary>供"未登记 schema 但 FailOnUnknownTable=false"场景构造的最小占位 schema：
        /// 只做信封检查，不声明任何字段；主键字段名按调用方传入的猜测值（见
        /// <see cref="Core.Foundation.DataRegistry.DataRegistry"/> 的处理逻辑）。</summary>
        public static TableSchema Unschematized(string tableName, string primaryKey)
        {
            var schema = new TableSchema(tableName, primaryKey, currentSchemaVersion: 1, fields: Array.Empty<FieldSchema>(), isRegistryTable: true)
            {
                IsUnschematized = true,
            };
            return schema;
        }
    }
}
