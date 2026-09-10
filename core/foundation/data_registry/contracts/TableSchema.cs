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

        /// <summary>ADR-0022（04 第 3.4 节"表级归属元数据"）：本表所属架构分层；由各层 schema
        /// catalog 注册时经 <see cref="WithOwnership"/> 填入。未填入时为 <c>null</c>——只有
        /// <c>SchemaAudit</c> 门禁覆盖到的真实登记表要求非空（见该类型"table_ownership"检查），
        /// 测试用的临时 <see cref="TableSchema"/> 不受影响（向后兼容，同 <see cref="FieldSchema.Range"/>
        /// 的登记惯例）。</summary>
        public SchemaLayer? Layer { get; private set; }

        /// <summary>所属模块名，与 <c>architecture/04_数据与内容管线.md</c> 第 1.1 节"模块"列、
        /// 实现目录/装配名一致（如 <c>"archetype"</c>、<c>"skill"</c>）。</summary>
        public string? Module { get; private set; }

        private string? _domain;

        /// <summary>04 第 2.2 节域名：默认取表名第一段（<c>Name</c> 按 <c>'.'</c> 切分后的首段），
        /// 三张单段名表（<c>camera_profile</c>/<c>ui_layout_definition</c>/<c>shell_menu_definition</c>，
        /// 04 第 2.2 节勘误登记的命名例外）经 <see cref="WithDomain"/> 显式声明为 <c>camera</c>/
        /// <c>ui</c>/<c>shell</c>。工具应始终读取本属性而非自行解析表名首段（正是为了统一处理这三个
        /// 例外）。</summary>
        public string Domain => _domain ?? DefaultDomain;

        private string DefaultDomain
        {
            get
            {
                var dot = Name.IndexOf('.');
                return dot > 0 ? Name.Substring(0, dot) : Name;
            }
        }

        /// <summary>ADR-0022（04 第 3.4 节"时间字段单位与作用域"）：本表所属的时间模型作用域，默认
        /// <see cref="Core.Foundation.DataRegistry.TimeScope.None"/>（不含时间模型字段）。含
        /// <see cref="FieldSchema.Unit"/> 为 <see cref="FieldUnit.Time"/> 字段的表必须经
        /// <see cref="WithTimeScope"/> 显式声明非 <see cref="Core.Foundation.DataRegistry.TimeScope.None"/>
        /// 的取值（见 <c>SchemaAudit</c>"time_scope_declared"检查）。</summary>
        public TimeScope TimeScope { get; private set; } = TimeScope.None;

        /// <summary>登记 <see cref="Layer"/>/<see cref="Module"/>；只能整体设置一次（重复调用抛异常，
        /// 同 <see cref="FieldSchema.WithRange"/> 的"防止静默覆盖"惯例）。两者总是成对登记（04 第 1.1
        /// 节总索引表"层"与"模块"两列总是同时出现），故合并为一个方法而非两个独立的 With*，减少调用点
        /// 样板代码。返回 <c>this</c> 便于链式调用。</summary>
        public TableSchema WithOwnership(SchemaLayer layer, string module)
        {
            if (string.IsNullOrWhiteSpace(module)) throw new ArgumentException("module 不能为空", nameof(module));
            if (Layer != null) throw new InvalidOperationException($"表 \"{Name}\"：Layer/Module 已设置，不可重复设置");
            Layer = layer;
            Module = module;
            return this;
        }

        /// <summary>显式登记 <see cref="Domain"/>，仅用于 04 第 2.2 节勘误登记的单段名命名例外
        /// （<c>camera_profile</c>/<c>ui_layout_definition</c>/<c>shell_menu_definition</c>）；
        /// 只能设置一次。返回 <c>this</c> 便于链式调用。</summary>
        public TableSchema WithDomain(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) throw new ArgumentException("domain 不能为空", nameof(domain));
            if (_domain != null) throw new InvalidOperationException($"表 \"{Name}\"：Domain 已设置，不可重复设置");
            _domain = domain;
            return this;
        }

        /// <summary>登记 <see cref="TimeScope"/>；只能设置一次。返回 <c>this</c> 便于链式调用。</summary>
        public TableSchema WithTimeScope(TimeScope timeScope)
        {
            if (TimeScope != TimeScope.None) throw new InvalidOperationException($"表 \"{Name}\"：TimeScope 已设置，不可重复设置");
            TimeScope = timeScope;
            return this;
        }

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
