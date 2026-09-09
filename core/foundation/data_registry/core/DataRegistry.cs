using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using CommonId = Core.Foundation.Common.Id;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// <see cref="IDataRegistry"/> 的默认实现（见 04 第 4 节、01_分层与依赖.md L0 模块表
    /// <c>data_registry</c> 行）。加载流程（<see cref="LoadAll()"/>）：读取每张表文本 → JSON
    /// 解析 → 信封检查（<c>table</c>/<c>schema_version</c>/<c>rows</c>，<c>table</c> 必须等于
    /// 文件名）→ 版本迁移（低于当前版本依次跑迁移链，缺环节报错；高于当前版本报错）→ 逐行建
    /// <see cref="DataRecord"/>（主键重复报错）→ 全部表进内存后跑第 5 节校验项 + 已注册
    /// <see cref="IValidationRule"/> → 发 <c>data.load_completed</c>；报告为阻断态时再发
    /// <c>data.validation_failed</c>，且此后 <see cref="Get(string, string)"/>/
    /// <see cref="GetAll"/>/<see cref="Query(string, ExprNode)"/> 一律抛
    /// <see cref="InvalidOperationException"/>（见 11 第 4 节"不做静默降级"）。
    /// 无反射、无 LINQ 热路径、无线程。
    /// <para>
    /// 判断记录（数据目录框架/游戏分层任务，多根加载）：新增 <see cref="LoadAll(IReadOnlyList{IDataSource})"/>
    /// 重载支持"同一张表可同时出现在多个数据根"（见 <c>data/README.md</c>"框架级数据表与游戏数据
    /// 目录并列加载"一节）。选型：任务书给出两个等价选项——"<c>DataRegistryOptions.DataRoots:
    /// IReadOnlyList&lt;string&gt;</c>"或"<c>LoadAll(IReadOnlyList&lt;string&gt; roots)</c>重载"，
    /// 本实现采用第二种的变体：参数类型是 <see cref="IDataSource"/> 而不是原始路径字符串——
    /// 本类本来就通过 <see cref="IDataSource"/> 与具体存储介质解耦（见构造函数），一个"根"在磁盘
    /// 场景下就是一个 <c>new FileSystemDataSource(fs, rootPath)</c>；把参数类型定成
    /// <c>IReadOnlyList&lt;string&gt;</c> 反而要求本类知道怎么把路径字符串转成 <see cref="IDataSource"/>
    /// （需要引用 <see cref="Core.Foundation.EngineAdapter.IFileSystem"/>），与"本类不关心存储介质"
    /// 的既有边界矛盾；调用方（如 toolchain/validator、Unity 适配层引导代码）自己按根路径逐个构造
    /// <see cref="FileSystemDataSource"/> 传入即可，同时天然支持"内存根 + 磁盘根混合"等既有单根版本
    /// 做不到的场景。<see cref="LoadAll()"/>（无参，构造函数传入的单一 <c>_source</c>）保持完全不变的
    /// 行为与语义，等价于 <c>LoadAll(new[] { _source })</c>，向后兼容全部既有单根调用方。
    /// </para>
    /// <para>
    /// 合并规则（同一表名出现在多个根时）：先各自独立做信封解析（读文本、JSON 解析、
    /// <c>table</c>/<c>schema_version</c>/<c>rows</c> 检查）；若信封 <c>schema_version</c>
    /// 在多个根之间不一致，判定为阻断错误（<c>schema_version</c> 检查项，消息里点出两个根各自的
    /// 位置），不再合并该表（该表本次加载视为失败，不出现在 <see cref="Tables"/> 中，语义与其它
    /// envelope 级错误一致）；否则各根独立完成版本迁移与"根内"逐行建 <see cref="DataRecord"/>
    /// （根内主键重复仍按原规则报 <c>primary_key</c> 错误），再按 <paramref name="sources"/> 声明
    /// 顺序把各根产出的记录集合依次合并（顺序即"层"：先声明的根是前层，后声明的根是后层——典型
    /// 用法是框架根在前、具体游戏根在后）——合并阶段若同一主键在两个不同根间重复，见下"覆盖语义"。
    /// </para>
    /// <para>
    /// 覆盖语义（数据行覆盖语义任务新增，见 <c>data/README.md</c>"多根加载与合并规则"、
    /// <see cref="DataRegistryOptions.AllowOverride"/>）：跨根同主键重复默认仍是阻断错误（防止
    /// 无意撞键）；<see cref="DataRegistryOptions.AllowOverride"/> 为 <c>true</c>（默认）时，
    /// 该行为可用行级字段显式改写——
    /// <list type="bullet">
    /// <item>后层行（合并处理顺序更靠后的根）声明 <c>"override": true</c>：整行替换已合入结果的
    /// 前层同主键行（<see cref="DataRecord.Raw"/> 整体换成后层这一行，不做字段级合并），并记录一条
    /// <see cref="OverrideDiagnostic"/>（见 <see cref="GetOverrideDiagnostics"/>）——覆盖成功不产生
    /// 任何 <see cref="ValidationIssue"/>（既不是 Warning 也不是 Error，见该方法判断记录"为什么不进
    /// ValidationReport.Issues"）。</item>
    /// <item>已合入结果的前层行声明 <c>"final": true</c>：拒绝被后续任何后层行覆盖（无论后层行是否
    /// 声明 <c>override</c>），判定为阻断错误（<c>primary_key</c> 检查项，消息点出两个根各自的
    /// 位置与 <c>final</c> 语义），该条后层行不计入合并结果，其余不冲突记录不受影响。</item>
    /// <item>都未声明（或 <see cref="DataRegistryOptions.AllowOverride"/> 为 <c>false</c>）：
    /// 行为与改动前完全一致——跨根同主键重复即阻断错误。</item>
    /// <item><c>override</c>/<c>final</c> 两个字段仅在多根合并且确实发生同主键跨根重复时才有意义；
    /// 单根加载（该表本次加载只来自一个根，见 <see cref="LoadOneTable"/>）里出现值为 <c>true</c> 的
    /// <c>override</c>/<c>final</c> 字段，判定为 Warning（检查项沿用 <c>envelope</c>），字段本身
    /// 被忽略、不影响加载结果——两者"仅在多根合并时有意义"是同一条判断记录，行为对齐。</item>
    /// </list>
    /// <c>schema_version</c> 跨根不一致仍无条件阻断，不受覆盖语义影响（覆盖只发生在"两个根各自都
    /// 已成功解析出内容"之后，见上"合并规则"）。
    /// </para>
    /// </summary>
    public sealed class DataRegistry : IDataRegistry
    {
        private readonly IDataSource _source;
        private readonly IEventBus _bus;
        private readonly DataRegistryOptions _options;

        private readonly Dictionary<string, TableSchema> _schemas = new Dictionary<string, TableSchema>(StringComparer.Ordinal);
        private readonly List<(string FromTable, string Field, string ToTable)> _declaredReferences =
            new List<(string FromTable, string Field, string ToTable)>();
        private readonly List<IValidationRule> _rules = new List<IValidationRule>();

        private Dictionary<string, LoadedTable> _tables = new Dictionary<string, LoadedTable>(StringComparer.Ordinal);

        /// <summary>最近一次加载/重载实际发生的全部行覆盖（见 <see cref="GetOverrideDiagnostics"/>、
        /// 类型级判断记录"覆盖语义"）；<see cref="LoadAllCore"/> 整体重建，<see cref="Reload(string)"/>
        /// 只替换对应表的条目。</summary>
        private readonly List<OverrideDiagnostic> _overrideDiagnostics = new List<OverrideDiagnostic>();

        /// <summary>FND-03 收口：按表持久保留"加载阶段"（envelope/schema_version/primary_key，即
        /// <see cref="LoadOneTablePartial"/>/<see cref="LoadMergedTable"/> 产出、发生在字段校验与
        /// <see cref="IValidationRule"/> 之前的那部分）诊断，跨越无参 <see cref="Validate()"/> 调用与
        /// 不相关表的 <see cref="Reload(string)"/> 持续存在——一张表加载失败（例如坏 JSON）意味着它
        /// 根本没有进入 <see cref="_tables"/>，字段级校验循环天然看不到它，如果不额外持久化这份诊断，
        /// 任何后续无参 <see cref="Validate()"/>（哪怕只是想重新跑一遍别的表的规则）都会拿一个全新的
        /// 空 issues 列表起步，凭空"忘掉"这张坏表曾经报过错，<see cref="_blocked"/> 可能被错误解除。
        /// <see cref="LoadAllCore"/> 整体重建（覆盖全部表），<see cref="Reload(string)"/> 只替换
        /// 参数指定的那张表的条目——这正是"按表保留，直到该表成功重载"的语义：其它表的历史诊断不受
        /// 影响，只有显式重载了的那张表才可能被清空（重载成功、不再报错时）或替换（重载后错误变了）。
        /// <see cref="Validate()"/> 以这份列表的快照作为起点，而不是从空列表开始。</summary>
        private readonly List<ValidationIssue> _loadDiagnostics = new List<ValidationIssue>();

        /// <summary>最近一次 <see cref="LoadAll()"/>/<see cref="LoadAll(IReadOnlyList{IDataSource})"/>
        /// 使用的完整根集合；<see cref="Reload(string)"/> 据此在全部根里重新定位待重载的表（多根注册表
        /// 下，某张表可能同时来自多个根，<see cref="Reload(string)"/> 按同样的合并规则重新计算该表）。
        /// 构造期默认只含构造函数传入的单一 <c>_source</c>。</summary>
        private IReadOnlyList<IDataSource> _sources;

        /// <summary>true 时 <see cref="Get(string, string)"/>/<see cref="GetAll"/>/
        /// <see cref="Query(string, ExprNode)"/> 拒绝读取；初始为 true（尚未 <see cref="LoadAll()"/>
        /// 时不允许读取）。</summary>
        private bool _blocked = true;

        public DataRegistry(IDataSource source, IEventBus bus, DataRegistryOptions? options = null)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _options = options ?? new DataRegistryOptions();
            _sources = new[] { _source };
        }

        private sealed class LoadedTable
        {
            public TableSchema Schema = null!;
            public List<DataRecord> Records = null!;
            public Dictionary<string, DataRecord> ByKey = null!;

            /// <summary>该表本次加载实际读取到的全部来源位置（诊断用，见
            /// <see cref="IDataRegistryView.GetTableSourceLocations"/>）：单根加载时只有一条，
            /// 多根合并时按参与合并的根顺序排列。</summary>
            public List<string> Locations = null!;
        }

        /// <summary>单个数据根对一张表的独立解析结果（信封检查、版本迁移、根内建记录均已完成，
        /// 尚未与其它根合并）——见类型级判断记录"合并规则"。</summary>
        private sealed class PartialTable
        {
            public TableSchema Schema = null!;

            /// <summary>该根这张表文件信封里的原始 <c>schema_version</c>（迁移前），供跨根一致性
            /// 检查使用；未登记 schema（<see cref="TableSchema.IsUnschematized"/>）时无迁移，
            /// 该值与迁移前后一致。</summary>
            public int RawSchemaVersion;

            public List<DataRecord> RecordsInOrder = null!;
            public Dictionary<string, DataRecord> ByKey = null!;
            public string Location = null!;
        }

        // ---------------------------------------------------------------
        // 注册
        // ---------------------------------------------------------------

        public void RegisterSchema(TableSchema schema)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            _schemas[schema.Name] = schema;
        }

        public void DeclareReference(string fromTable, string field, string toTable)
        {
            if (string.IsNullOrEmpty(fromTable)) throw new ArgumentException("fromTable 不能为空", nameof(fromTable));
            if (string.IsNullOrEmpty(field)) throw new ArgumentException("field 不能为空", nameof(field));
            if (string.IsNullOrEmpty(toTable)) throw new ArgumentException("toTable 不能为空", nameof(toTable));
            _declaredReferences.Add((fromTable, field, toTable));
        }

        public void RegisterValidationRule(IValidationRule rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            _rules.Add(rule);
        }

        // ---------------------------------------------------------------
        // 加载 / 校验 / 重载
        // ---------------------------------------------------------------

        public ValidationReport LoadAll()
        {
            _sources = new[] { _source };
            return LoadAllCore(_sources);
        }

        /// <summary>多根加载：见类型级判断记录"合并规则"。<paramref name="sources"/> 内的顺序
        /// 只影响诊断信息（<see cref="LoadedTable.Locations"/> 的排列顺序、冲突消息里"先出现的根"
        /// 是谁），不影响合并结果本身是否报错——两个根之间同一主键冲突/同一表 schema_version
        /// 不一致，无论顺序都会报错。</summary>
        public ValidationReport LoadAll(IReadOnlyList<IDataSource> sources)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            if (sources.Count == 0) throw new ArgumentException("sources 不能为空", nameof(sources));
            _sources = sources;
            return LoadAllCore(sources);
        }

        private ValidationReport LoadAllCore(IReadOnlyList<IDataSource> sources)
        {
            var issues = new List<ValidationIssue>();
            var loaded = new Dictionary<string, LoadedTable>(StringComparer.Ordinal);
            int recordCount = 0;
            _overrideDiagnostics.Clear();

            // 按表名分组：同一表名出现在多个根时才走合并路径，出现在单个根时走原有单根路径
            // （保持与改动前逐字节相同的行为，见类型级判断记录）。
            var byTable = new Dictionary<string, List<DataTableSource>>(StringComparer.Ordinal);
            for (int s = 0; s < sources.Count; s++)
            {
                var tableSources = sources[s].ListTables();
                for (int i = 0; i < tableSources.Count; i++)
                {
                    var ts = tableSources[i];
                    if (!byTable.TryGetValue(ts.TableName, out var list))
                    {
                        list = new List<DataTableSource>();
                        byTable[ts.TableName] = list;
                    }
                    list.Add(ts);
                }
            }

            // 确定性：按表名排序后再处理，保证多次运行、issues 顺序一致（呼应 Directory.Build.props
            // "确定性"、本类既有"无 LINQ 热路径"风格——用手写排序代替 LINQ OrderBy）。
            var tableNames = new List<string>(byTable.Keys);
            tableNames.Sort(StringComparer.Ordinal);

            for (int t = 0; t < tableNames.Count; t++)
            {
                var tableName = tableNames[t];
                var group = byTable[tableName];
                if (group.Count == 1)
                {
                    LoadOneTable(group[0], issues, loaded, ref recordCount);
                }
                else
                {
                    LoadMergedTable(tableName, group, issues, loaded, ref recordCount, _overrideDiagnostics);
                }
            }

            _tables = loaded;

            // 快照"加载阶段"诊断（此时 issues 里只有 envelope/schema_version/primary_key 一类
            // 错误，字段校验与规则尚未运行）——RunValidationAndBuildReport 会继续往同一个 issues
            // 列表追加字段级诊断，但 List<T> 的 AddRange 是值拷贝，不会让 _loadDiagnostics 跟着后续
            // 追加联动，因此必须在调用 RunValidationAndBuildReport 之前拍这一份快照。
            _loadDiagnostics.Clear();
            _loadDiagnostics.AddRange(issues);

            var report = RunValidationAndBuildReport(issues);

            _bus.PublishImmediate(new DataLoadCompletedEvent(_tables.Count, recordCount, report.ErrorCount, report.WarningCount));
            if (report.IsBlocking)
            {
                _bus.PublishImmediate(new DataValidationFailedEvent(report.ErrorCount, report.WarningCount));
            }

            return report;
        }

        public ValidationReport Validate() => RunValidationAndBuildReport(new List<ValidationIssue>(_loadDiagnostics));

        /// <summary>仅限开发期使用的单表热重载：在 <see cref="_sources"/> 全部根中重新定位
        /// <paramref name="table"/>，按与初次加载相同的合并规则重建该表，替换内存态记录，随后
        /// 重跑一次全量 <see cref="Validate"/>。多根注册表下，若该表来自多个根，重载会重新读取
        /// 全部涉及的根（不只是"最初命中的那一个"），语义与首次 <see cref="LoadAll()"/> 一致。</summary>
        public ValidationReport Reload(string table)
        {
            if (string.IsNullOrEmpty(table)) throw new ArgumentException("table 不能为空", nameof(table));

            var matches = new List<DataTableSource>();
            for (int s = 0; s < _sources.Count; s++)
            {
                var tableSources = _sources[s].ListTables();
                for (int i = 0; i < tableSources.Count; i++)
                {
                    if (tableSources[i].TableName == table) matches.Add(tableSources[i]);
                }
            }

            var localIssues = new List<ValidationIssue>();
            if (matches.Count == 0)
            {
                localIssues.Add(new ValidationIssue(ValidationSeverity.Error, table, "envelope", $"数据源中不存在表 \"{table}\""));
            }
            else if (matches.Count == 1)
            {
                var loaded = new Dictionary<string, LoadedTable>(StringComparer.Ordinal);
                int recordCount = 0;
                LoadOneTable(matches[0], localIssues, loaded, ref recordCount);
                if (loaded.TryGetValue(table, out var loadedTable))
                {
                    _tables[table] = loadedTable;
                }
                // 该表本次重载只来自单根（可能此前是多根合并表，某个根被移除/该表改名腾空）：
                // 不再有合并、不可能再有覆盖关系，清掉它遗留的旧覆盖诊断。
                _overrideDiagnostics.RemoveAll(d => d.Table == table);
            }
            else
            {
                var loaded = new Dictionary<string, LoadedTable>(StringComparer.Ordinal);
                int recordCount = 0;
                // 该表本次重载新产出的覆盖诊断先收集到本地列表，再整体替换 _overrideDiagnostics
                // 里属于这张表的旧条目——不能直接就地追加（旧条目可能已不再成立，例如某个根改动后
                // 覆盖关系反转），也不能像 LoadAllCore 那样整体清空（会丢掉其它表的诊断）。
                var freshDiagnostics = new List<OverrideDiagnostic>();
                LoadMergedTable(table, matches, localIssues, loaded, ref recordCount, freshDiagnostics);
                if (loaded.TryGetValue(table, out var loadedTable))
                {
                    _tables[table] = loadedTable;
                }
                _overrideDiagnostics.RemoveAll(d => d.Table == table);
                _overrideDiagnostics.AddRange(freshDiagnostics);
            }

            // FND-03 收口：按表持久化本次重载得到的"加载阶段"诊断（此时 localIssues 只含
            // envelope/schema_version/primary_key 一类错误，字段校验尚未运行），替换掉
            // _loadDiagnostics 里属于这张表的旧条目——重载成功（localIssues 为空）则该表的历史
            // 加载错误随之清空，解除对应阻断；重载仍失败则替换成这一次的错误消息；其它表的诊断
            // 完全不受影响（呼应上面 _overrideDiagnostics 的替换惯例）。
            _loadDiagnostics.RemoveAll(d => d.Table == table);
            _loadDiagnostics.AddRange(localIssues);

            // 关键一步（不是只把 localIssues 传给 RunValidationAndBuildReport）：本次 Reload 返回
            // 的报告、以及它据此更新的 _blocked 状态，必须看到全部表的持久化加载诊断，不能只看
            // "这次重载的这一张表"——否则 Reload 一张完全无关的好表也会把 _blocked
            // 重新算成"不阻断"（用的是只含这张表信息的局部报告），等价于绕过了上面
            // Validate()/_loadDiagnostics 好不容易做到的"按表持久保留，直到该表成功重载"。
            var issuesForReport = new List<ValidationIssue>(_loadDiagnostics);
            return RunValidationAndBuildReport(issuesForReport);
        }

        private ValidationReport RunValidationAndBuildReport(List<ValidationIssue> issues)
        {
            // 校验期间允许读取（RunFieldValidation 与 IValidationRule 都要经 this 只读查询），
            // 最终是否阻断由本次算出的报告决定。
            _blocked = false;

            RunFieldValidation(issues);

            foreach (var rule in _rules)
            {
                foreach (var issue in rule.Validate(this))
                {
                    issues.Add(issue);
                }
            }

            var report = new ValidationReport(issues, _options.Strictness);
            _blocked = report.IsBlocking;
            return report;
        }

        // ---------------------------------------------------------------
        // 只读查询（IDataRegistryView）
        // ---------------------------------------------------------------

        public DataRecord? Get(string table, string key)
        {
            EnsureReadable();
            return _tables.TryGetValue(table, out var t) && t.ByKey.TryGetValue(key, out var record) ? record : null;
        }

        public DataRecord? Get(string table, CommonId id) => Get(table, id.Value);

        public IReadOnlyList<DataRecord> GetAll(string table)
        {
            EnsureReadable();
            return _tables.TryGetValue(table, out var t) ? t.Records : Array.Empty<DataRecord>();
        }

        public IReadOnlyList<DataRecord> Query(string table, ExprNode predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            var records = GetAll(table);
            var result = new List<DataRecord>();
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                var diagnostics = new ExprDiagnosticsRecorder();
                var host = new RecordExprHost(record, diagnostics);
                var value = ExprEvaluator.Evaluate(predicate, host, diagnostics);
                if (value.Kind == ExprValueKind.Bool && value.AsBool)
                {
                    result.Add(record);
                }
            }
            return result;
        }

        /// <summary>便捷重载：解析 <paramref name="predicateText"/> 用 <see cref="RecordExprSchema.For"/>
        /// 把 <paramref name="table"/> 的字段登记为 <c>self.&lt;field&gt;</c>（见 04 第 4 节）。
        /// 要求该表已经 <see cref="RegisterSchema"/>。</summary>
        public IReadOnlyList<DataRecord> Query(string table, string predicateText)
        {
            if (predicateText == null) throw new ArgumentNullException(nameof(predicateText));

            var schema = GetSchema(table) ?? throw new InvalidOperationException($"表 \"{table}\" 未注册 schema，无法解析谓词文本");
            var exprSchema = RecordExprSchema.For(schema);
            var node = ExprParser.Parse(predicateText, exprSchema);
            return Query(table, node);
        }

        public IReadOnlyList<string> Tables
        {
            get
            {
                var names = new List<string>(_tables.Count);
                foreach (var name in _tables.Keys) names.Add(name);
                return names;
            }
        }

        public TableSchema? GetSchema(string table) => _schemas.TryGetValue(table, out var s) ? s : null;

        /// <summary>只读诊断（F3 元数据门禁新增，见 <see cref="Presentation.Assembly.SchemaAudit"/>）：
        /// 本次已 <see cref="RegisterSchema"/> 登记的全部 <see cref="TableSchema"/>（不要求已
        /// <see cref="LoadAll()"/>——只反映"代码里声明了哪些表结构"，与是否已加载任何实际数据行
        /// 无关）。<see cref="IDataRegistry"/> 接口本身不暴露这份列表（不改接口签名，见任务书约束），
        /// 只加在具体类上；调用方（<c>SchemaAudit.EnumerateRegisteredSchemas</c>）需要持有具体的
        /// <see cref="DataRegistry"/> 实例才能读取。返回一份快照数组，不随后续 <see cref="RegisterSchema"/>
        /// 调用联动。</summary>
        public IReadOnlyList<TableSchema> RegisteredSchemas
        {
            get
            {
                var result = new TableSchema[_schemas.Count];
                _schemas.Values.CopyTo(result, 0);
                return result;
            }
        }

        /// <summary>只读诊断：<paramref name="table"/> 本次加载实际来自哪些根（<see cref="DataTableSource.Location"/>
        /// 列表，单根加载时只有一条）；表未加载时返回空列表。不参与任何校验判定，纯粹供上层
        /// （如 Unity 适配层引导日志、构建产物核对）观察"这张表到底是从哪个数据根读上来的"。</summary>
        public IReadOnlyList<string> GetTableSourceLocations(string table) =>
            _tables.TryGetValue(table, out var t) ? t.Locations : Array.Empty<string>();

        /// <summary>只读诊断：见 <see cref="IDataRegistry.GetOverrideDiagnostics"/>、类型级判断记录
        /// "覆盖语义"。返回一份快照（调用方后续 <see cref="Reload(string)"/> 不会影响已返回的列表）。</summary>
        public IReadOnlyList<OverrideDiagnostic> GetOverrideDiagnostics() => _overrideDiagnostics.ToArray();

        private void EnsureReadable()
        {
            if (_blocked)
            {
                throw new InvalidOperationException("数据校验未通过，禁止读取");
            }
        }

        // ---------------------------------------------------------------
        // 加载单表（单根路径，行为与改动前完全一致）
        // ---------------------------------------------------------------

        private void LoadOneTable(DataTableSource tableSource, List<ValidationIssue> issues, Dictionary<string, LoadedTable> loaded, ref int recordCount)
        {
            var partial = LoadOneTablePartial(tableSource, issues);
            if (partial == null) return;

            WarnStrayOverrideMetaFields(tableSource.TableName, partial.RecordsInOrder, issues);

            recordCount += partial.RecordsInOrder.Count;
            loaded[tableSource.TableName] = new LoadedTable
            {
                Schema = partial.Schema,
                Records = partial.RecordsInOrder,
                ByKey = partial.ByKey,
                Locations = new List<string> { partial.Location },
            };
        }

        /// <summary>覆盖语义判断记录"override/final 仅在多根合并时有意义"：<paramref name="records"/>
        /// 本次加载并未经历任何跨根合并（该表这次只来自一个根），若某一行仍显式声明
        /// <c>"override": true</c> 或 <c>"final": true</c>，判定为 Warning——两个字段在这种场景下
        /// 完全不生效（没有"前层"可覆盖、也没有后续合并会尝试覆盖它），提醒作者大概率是误留的模板
        /// 残留或误解了字段语义；字段值本身被忽略，不影响加载结果。</summary>
        private static void WarnStrayOverrideMetaFields(string tableName, List<DataRecord> records, List<ValidationIssue> issues)
        {
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (record.TryGetBool("override", out var isOverride) && isOverride)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, tableName, "envelope",
                        "字段 \"override\" 仅在多根合并加载且发生同主键跨根重复时有意义；本次该表只来自单个数据根，已忽略",
                        recordKey: record.Key, field: "override"));
                }
                if (record.TryGetBool("final", out var isFinal) && isFinal)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, tableName, "envelope",
                        "字段 \"final\" 仅在多根合并加载且发生同主键跨根重复时有意义；本次该表只来自单个数据根，已忽略",
                        recordKey: record.Key, field: "final"));
                }
            }
        }

        // ---------------------------------------------------------------
        // 加载并合并多根同名表（见类型级判断记录"合并规则"/"覆盖语义"）
        // ---------------------------------------------------------------

        private void LoadMergedTable(string tableName, List<DataTableSource> tableSources, List<ValidationIssue> issues, Dictionary<string, LoadedTable> loaded, ref int recordCount, List<OverrideDiagnostic> diagnostics)
        {
            var partials = new List<PartialTable>();
            for (int i = 0; i < tableSources.Count; i++)
            {
                var partial = LoadOneTablePartial(tableSources[i], issues);
                if (partial != null) partials.Add(partial);
            }

            if (partials.Count == 0) return; // 各根本身已各自报过 envelope 级错误。
            if (partials.Count == 1)
            {
                // 只有一个根真正解析成功（其余根本身报了 envelope 级错误提前退出），按单根方式落表——
                // 没有第二个根参与合并，override/final 同样不生效。
                var only = partials[0];
                WarnStrayOverrideMetaFields(tableName, only.RecordsInOrder, issues);
                recordCount += only.RecordsInOrder.Count;
                loaded[tableName] = new LoadedTable
                {
                    Schema = only.Schema,
                    Records = only.RecordsInOrder,
                    ByKey = only.ByKey,
                    Locations = new List<string> { only.Location },
                };
                return;
            }

            // 1) 跨根 schema_version 一致性：不一致即阻断，不再合并该表（见类型级判断记录）。
            var firstVersion = partials[0].RawSchemaVersion;
            var versionMismatch = false;
            for (int i = 1; i < partials.Count; i++)
            {
                if (partials[i].RawSchemaVersion != firstVersion)
                {
                    versionMismatch = true;
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "schema_version",
                        $"表 \"{tableName}\" 在多个数据根之间的 schema_version 不一致：\"{partials[0].Location}\" 为 {firstVersion}，\"{partials[i].Location}\" 为 {partials[i].RawSchemaVersion}"));
                }
            }
            if (versionMismatch) return;

            // 2) 合并记录：跨根主键冲突默认仍阻断；AllowOverride 打开时按行级 override/final 字段
            //    改写（见类型级判断记录"覆盖语义"）。
            var allowOverride = _options.AllowOverride;
            var mergedByKey = new Dictionary<string, DataRecord>(StringComparer.Ordinal);
            var mergedRecords = new List<DataRecord>();
            var mergedIndexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            var mergedLocationByKey = new Dictionary<string, string>(StringComparer.Ordinal);
            var locations = new List<string>(partials.Count);

            for (int p = 0; p < partials.Count; p++)
            {
                var partial = partials[p];
                locations.Add(partial.Location);

                for (int i = 0; i < partial.RecordsInOrder.Count; i++)
                {
                    var record = partial.RecordsInOrder[i];

                    if (mergedByKey.TryGetValue(record.Key, out var existing))
                    {
                        var existingLocation = mergedLocationByKey[record.Key];
                        var existingIsFinal = allowOverride && existing.TryGetBool("final", out var fin) && fin;
                        if (existingIsFinal)
                        {
                            issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "primary_key",
                                $"主键重复：\"{record.Key}\"——数据根 \"{existingLocation}\" 的行声明 final=true，拒绝被数据根 \"{partial.Location}\" 覆盖",
                                recordKey: record.Key));
                            continue;
                        }

                        var rowIsOverride = allowOverride && record.TryGetBool("override", out var ov) && ov;
                        if (rowIsOverride)
                        {
                            var idx = mergedIndexByKey[record.Key];
                            mergedRecords[idx] = record;
                            mergedByKey[record.Key] = record;
                            mergedLocationByKey[record.Key] = partial.Location;
                            diagnostics.Add(new OverrideDiagnostic(tableName, record.Key, partial.Location, existingLocation));
                            continue;
                        }

                        issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "primary_key",
                            $"主键重复：\"{record.Key}\"（分别来自数据根 \"{existingLocation}\" 与 \"{partial.Location}\"）",
                            recordKey: record.Key));
                        continue;
                    }

                    mergedIndexByKey[record.Key] = mergedRecords.Count;
                    mergedByKey.Add(record.Key, record);
                    mergedRecords.Add(record);
                    mergedLocationByKey[record.Key] = partial.Location;
                    recordCount++;
                }
            }

            loaded[tableName] = new LoadedTable
            {
                Schema = partials[0].Schema,
                Records = mergedRecords,
                ByKey = mergedByKey,
                Locations = locations,
            };
        }

        /// <summary>单个根对一张表的独立解析：信封检查 → schema 匹配/未知表策略 → 版本迁移 →
        /// 根内逐行建 <see cref="DataRecord"/>（根内主键重复报错）。任何早退错误已写入
        /// <paramref name="issues"/>，返回 null；成功则返回 <see cref="PartialTable"/>，不直接
        /// 写入任何"已加载表"字典——是否直接落表（单根）还是与同名表的其它根合并
        /// （<see cref="LoadMergedTable"/>）由调用方决定。逻辑与改动前的 <c>LoadOneTable</c>
        /// 完全一致，只是把"结果"从直接赋值给 <c>loaded[tableName]</c> 改成返回值。</summary>
        private PartialTable? LoadOneTablePartial(DataTableSource tableSource, List<ValidationIssue> issues)
        {
            var tableName = tableSource.TableName;

            string text;
            try
            {
                text = tableSource.ReadText();
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope", $"读取表文本失败：{ex.Message}"));
                return null;
            }

            JsonValue root;
            try
            {
                root = JsonReader.Parse(text);
            }
            catch (JsonParseException ex)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope", $"JSON 解析失败：{ex.Message}"));
                return null;
            }

            if (!(root is JsonObject rootObj))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope", "顶层结构必须是 JSON 对象"));
                return null;
            }

            if (!rootObj.TryGetValue("table", out var tableVal) || !(tableVal is JsonString tableStr))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope", "缺少或非法的顶层字段 \"table\""));
                return null;
            }
            if (!string.Equals(tableStr.Value, tableName, StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope",
                    $"table 字段 \"{tableStr.Value}\" 与文件名 \"{tableName}\" 不一致"));
                return null;
            }

            if (!rootObj.TryGetValue("schema_version", out var svVal) || !(svVal is JsonNumber svNum)
                || !svNum.TryGetInt64(out var svLong) || svLong < 1)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope",
                    "缺少或非法的顶层字段 \"schema_version\"（须为 >=1 的整数）"));
                return null;
            }
            var schemaVersion = (int)svLong;

            if (!rootObj.TryGetValue("rows", out var rowsVal) || !(rowsVal is JsonArray rowsArr))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope", "缺少或非法的顶层字段 \"rows\"（须为数组）"));
                return null;
            }

            TableSchema schema;
            if (_schemas.TryGetValue(tableName, out var registered))
            {
                schema = registered;
            }
            else if (_options.FailOnUnknownTable)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope", $"表 \"{tableName}\" 未通过 RegisterSchema 登记"));
                return null;
            }
            else
            {
                var guessedKey = "id";
                if (rowsArr.Count > 0 && rowsArr[0] is JsonObject firstRow && !firstRow.ContainsKey("id") && firstRow.ContainsKey("key"))
                {
                    guessedKey = "key";
                }
                schema = TableSchema.Unschematized(tableName, guessedKey);
            }

            IReadOnlyList<JsonValue> effectiveRows = rowsArr;
            if (!schema.IsUnschematized)
            {
                if (schemaVersion > schema.CurrentSchemaVersion)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "schema_version",
                        $"schema_version {schemaVersion} 超过当前代码期望的版本 {schema.CurrentSchemaVersion}"));
                    return null;
                }

                if (schemaVersion < schema.CurrentSchemaVersion)
                {
                    var chain = BuildMigrationChain(schema, schemaVersion, schema.CurrentSchemaVersion);
                    if (chain == null)
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "schema_version",
                            $"从版本 {schemaVersion} 到 {schema.CurrentSchemaVersion} 缺少迁移环节"));
                        return null;
                    }

                    var migrated = new List<JsonValue>(rowsArr.Count);
                    for (int i = 0; i < rowsArr.Count; i++)
                    {
                        if (!(rowsArr[i] is JsonObject rowObj))
                        {
                            issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope", $"rows[{i}] 不是对象，无法迁移"));
                            continue;
                        }

                        var current = rowObj;
                        for (int m = 0; m < chain.Count; m++)
                        {
                            current = chain[m].Migrate(current);
                        }
                        migrated.Add(current);
                    }
                    effectiveRows = migrated;
                }
            }

            var recordsByKey = new Dictionary<string, DataRecord>(StringComparer.Ordinal);
            var recordsInOrder = new List<DataRecord>();
            var keyField = schema.PrimaryKey;
            var expectedDomain = tableName.Split('.')[0];

            for (int i = 0; i < effectiveRows.Count; i++)
            {
                if (!(effectiveRows[i] is JsonObject rowObj))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "envelope", $"rows[{i}] 不是对象"));
                    continue;
                }

                if (!rowObj.TryGetValue(keyField, out var keyVal) || !(keyVal is JsonString keyStr) || keyStr.Value.Length == 0)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "primary_key",
                        $"rows[{i}] 缺少主键字段 \"{keyField}\" 或不是非空字符串"));
                    continue;
                }
                var keyRaw = keyStr.Value;

                CommonId? idValue = null;
                string recordKey;

                if (keyField == "id")
                {
                    if (!CommonId.TryParse(keyRaw, out var parsedId))
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "primary_key",
                            $"id \"{keyRaw}\" 不符合 id 格式", recordKey: keyRaw));
                        continue;
                    }
                    if (!schema.IsRegistryTable && !string.Equals(parsedId.Domain, expectedDomain, StringComparison.Ordinal))
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "primary_key",
                            $"id 的 domain 前缀 \"{parsedId.Domain}\" 应等于表名首段 \"{expectedDomain}\"", recordKey: keyRaw));
                        continue;
                    }
                    idValue = parsedId;
                    recordKey = keyRaw;
                }
                else
                {
                    if (!CommonId.IsValidFormat(keyRaw))
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "primary_key",
                            $"key \"{keyRaw}\" 不符合 id 格式", recordKey: keyRaw));
                        continue;
                    }
                    recordKey = keyRaw;

                    if (schema.HasLocaleCompositeKey)
                    {
                        if (!rowObj.TryGetValue("locale", out var localeVal) || !(localeVal is JsonString localeStr) || localeStr.Value.Length == 0)
                        {
                            issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "primary_key",
                                "复合主键表缺少 \"locale\" 字段", recordKey: keyRaw));
                            continue;
                        }
                        recordKey = keyRaw + "@" + localeStr.Value;
                    }
                }

                if (recordsByKey.ContainsKey(recordKey))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, tableName, "primary_key",
                        $"主键重复：\"{recordKey}\"", recordKey: recordKey));
                    continue;
                }

                var record = new DataRecord(schema, recordKey, idValue, rowObj);
                recordsByKey.Add(recordKey, record);
                recordsInOrder.Add(record);
            }

            return new PartialTable
            {
                Schema = schema,
                RawSchemaVersion = schemaVersion,
                RecordsInOrder = recordsInOrder,
                ByKey = recordsByKey,
                Location = tableSource.Location,
            };
        }

        private static List<TableMigration>? BuildMigrationChain(TableSchema schema, int fromVersion, int toVersion)
        {
            var chain = new List<TableMigration>();
            var current = fromVersion;
            while (current < toVersion)
            {
                TableMigration? step = null;
                foreach (var m in schema.Migrations)
                {
                    if (m.FromVersion == current)
                    {
                        step = m;
                        break;
                    }
                }
                if (step == null) return null;
                chain.Add(step);
                current = step.ToVersion;
            }
            return current == toVersion ? chain : null;
        }

        // ---------------------------------------------------------------
        // 字段级校验（04 第 5 节：required_field / field_type / reference_integrity /
        // text_key_exists / expr_parsable，以及 ADR-0019 复合字段子结构校验新增的
        // variant_discriminator / substructure_depth / unknown_subfield；envelope /
        // schema_version / primary_key 已在 LoadOneTablePartial 中检查）
        //
        // 判断记录（ADR-0019 落地，子结构递归）：Object.Fields/Variants、Array.Item 的递归校验与
        // 顶层字段共用同一套 required_field/field_type/reference_integrity/text_key_exists/
        // expr_parsable 检查名与实现——所有既有校验方法（ValidateIdList/ValidateReferenceField/
        // ValidateTextKeyField/ValidateExprField/ValidateEnumField）改成接受
        // (table, recordKey, fieldPath, ...) 而不是 DataRecord，field.Name 换成完整路径
        // fieldPath（如 "effects[2].params.base_value"），不新增平行的检查名/实现（见任务书
        // "子层 Reference/TextKey/Expr 的校验逻辑必须与顶层共用同一份实现"）。递归深度上限
        // MaxSubstructureDepth，防御登记错误（如误将 itemFactory 指向自身导致的无限递归）。
        // ---------------------------------------------------------------

        /// <summary>子结构递归校验的最大深度（见 04 第 3.2 节、ADR-0019）：超过判定为
        /// <c>substructure_depth</c> 错误并停止对该子树继续递归，其余字段/记录不受影响。</summary>
        private const int MaxSubstructureDepth = 32;

        private void RunFieldValidation(List<ValidationIssue> issues)
        {
            foreach (var kvp in _tables)
            {
                var schema = kvp.Value.Schema;
                if (schema.IsUnschematized) continue;

                var records = kvp.Value.Records;
                for (int i = 0; i < records.Count; i++)
                {
                    var record = records[i];
                    var fields = schema.Fields;
                    for (int f = 0; f < fields.Count; f++)
                    {
                        ValidateRecordField(record, fields[f], issues);
                    }
                }
            }

            foreach (var decl in _declaredReferences)
            {
                if (!_tables.TryGetValue(decl.FromTable, out var fromLoaded)) continue;

                foreach (var record in fromLoaded.Records)
                {
                    if (!record.Has(decl.Field)) continue;
                    if (!record.TryGetString(decl.Field, out var refValue)) continue;

                    if (!ReferenceExists(decl.ToTable, refValue))
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, decl.FromTable, "reference_integrity",
                            $"字段 \"{decl.Field}\" 的值 \"{refValue}\" 在表 \"{decl.ToTable}\" 中不存在",
                            recordKey: record.Key, field: decl.Field));
                    }
                }
            }
        }

        private void ValidateRecordField(DataRecord record, FieldSchema field, List<ValidationIssue> issues)
        {
            if (!record.Has(field.Name))
            {
                if (field.Required)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, record.Table.Name, "required_field",
                        $"必填字段 \"{field.Name}\" 缺失", recordKey: record.Key, field: field.Name));
                }
                return;
            }

            ValidateFieldValue(record.Table.Name, record.Key, field.Name, field, record.Raw[field.Name], issues, depth: 0);
        }

        /// <summary>校验一个已确认存在的字段值：<paramref name="fieldPath"/> 是完整路径（顶层调用时
        /// 等于字段名，递归调用时形如 <c>"effects[2].params.base_value"</c>），<paramref name="depth"/>
        /// 是当前递归深度（顶层为 0，每进入一层 Object.Fields/Variants 子字段或 Array.Item 元素 +1，
        /// 见 <see cref="MaxSubstructureDepth"/>）。</summary>
        private void ValidateFieldValue(string table, string recordKey, string fieldPath, FieldSchema field, JsonValue raw, List<ValidationIssue> issues, int depth)
        {
            switch (field.Kind)
            {
                case FieldKind.Bool:
                    if (!(raw is JsonBool)) AddFieldTypeError(issues, table, recordKey, fieldPath, raw, "Bool");
                    break;

                case FieldKind.Int:
                    if (!(raw is JsonNumber ni && ni.TryGetInt64(out _))) AddFieldTypeError(issues, table, recordKey, fieldPath, raw, "Int");
                    break;

                case FieldKind.Number:
                    if (!(raw is JsonNumber)) AddFieldTypeError(issues, table, recordKey, fieldPath, raw, "Number");
                    break;

                case FieldKind.String:
                    if (!(raw is JsonString)) AddFieldTypeError(issues, table, recordKey, fieldPath, raw, "String");
                    break;

                case FieldKind.Id:
                    if (!(raw is JsonString sid && CommonId.IsValidFormat(sid.Value))) AddFieldTypeError(issues, table, recordKey, fieldPath, raw, "Id");
                    break;

                case FieldKind.IdList:
                    ValidateIdList(table, recordKey, fieldPath, raw, issues);
                    break;

                case FieldKind.Reference:
                    ValidateReferenceField(table, recordKey, fieldPath, field, raw, issues);
                    break;

                case FieldKind.TextKey:
                    ValidateTextKeyField(table, recordKey, fieldPath, raw, issues);
                    break;

                case FieldKind.Expr:
                    ValidateExprField(table, recordKey, fieldPath, raw, issues);
                    break;

                case FieldKind.Enum:
                    ValidateEnumField(table, recordKey, fieldPath, field, raw, issues);
                    break;

                case FieldKind.Vec2:
                    if (!(raw is JsonObject vo && vo.TryGetValue("x", out var xv) && xv is JsonNumber
                                              && vo.TryGetValue("y", out var yv) && yv is JsonNumber))
                    {
                        AddFieldTypeError(issues, table, recordKey, fieldPath, raw, "Vec2 ({\"x\": Number, \"y\": Number})");
                    }
                    break;

                case FieldKind.Object:
                    ValidateObjectField(table, recordKey, fieldPath, field, raw, issues, depth);
                    break;

                case FieldKind.Array:
                    ValidateArrayField(table, recordKey, fieldPath, field, raw, issues, depth);
                    break;
            }
        }

        // -----------------------------------------------------------------
        // ADR-0019：Object.Fields / Object.Variants / Array.Item 递归
        // -----------------------------------------------------------------

        private void ValidateObjectField(string table, string recordKey, string fieldPath, FieldSchema field, JsonValue raw, List<ValidationIssue> issues, int depth)
        {
            if (!(raw is JsonObject obj))
            {
                AddFieldTypeError(issues, table, recordKey, fieldPath, raw, "Object");
                return;
            }

            var variants = field.Variants;
            var fields = field.Fields;
            if (variants == null && fields == null)
            {
                return; // 未登记子结构：维持"存在且是对象"（向后兼容）。
            }

            if (depth >= MaxSubstructureDepth)
            {
                AddSubstructureDepthError(issues, table, recordKey, fieldPath);
                return;
            }

            if (variants != null)
            {
                ValidateVariantObject(table, recordKey, fieldPath, variants, obj, issues, depth);
            }
            else
            {
                var known = new HashSet<string>(StringComparer.Ordinal);
                ValidateFieldList(table, recordKey, fieldPath, fields, obj, issues, depth, known);
                ReportUnknownSubfields(table, recordKey, fieldPath, obj, known, issues);
            }
        }

        private void ValidateVariantObject(string table, string recordKey, string fieldPath, VariantSchema variants, JsonObject obj, List<ValidationIssue> issues, int depth)
        {
            if (!TryGetPresent(obj, variants.Discriminator, out var discRaw))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, table, "variant_discriminator",
                    $"字段 \"{fieldPath}\" 缺少判别字段 \"{variants.Discriminator}\"",
                    recordKey: recordKey, field: fieldPath + "." + variants.Discriminator));
                return;
            }

            if (!(discRaw is JsonString discStr) || !variants.Cases.TryGetValue(discStr.Value, out var caseFields))
            {
                var legalValues = new List<string>(variants.Cases.Keys);
                legalValues.Sort(StringComparer.Ordinal);
                issues.Add(new ValidationIssue(ValidationSeverity.Error, table, "variant_discriminator",
                    $"字段 \"{fieldPath}\" 的判别字段 \"{variants.Discriminator}\" 取值非法，合法取值：{string.Join("|", legalValues)}",
                    recordKey: recordKey, field: fieldPath + "." + variants.Discriminator));
                return;
            }

            var known = new HashSet<string>(StringComparer.Ordinal) { variants.Discriminator };
            ValidateFieldList(table, recordKey, fieldPath, variants.CommonFields, obj, issues, depth, known);
            ValidateFieldList(table, recordKey, fieldPath, caseFields, obj, issues, depth, known);
            ReportUnknownSubfields(table, recordKey, fieldPath, obj, known, issues);
        }

        /// <summary>对一份子字段清单逐个做"必填检查 + 递归校验"，并把子字段名收进
        /// <paramref name="known"/>（供 <see cref="ReportUnknownSubfields"/> 判断多余子字段）。
        /// <paramref name="fields"/> 为 null 时空操作（<see cref="VariantSchema.CommonFields"/> 可选）。</summary>
        private void ValidateFieldList(string table, string recordKey, string parentPath, IReadOnlyList<FieldSchema>? fields, JsonObject obj, List<ValidationIssue> issues, int depth, HashSet<string> known)
        {
            if (fields == null) return;

            for (int i = 0; i < fields.Count; i++)
            {
                var sub = fields[i];
                known.Add(sub.Name);
                var childPath = parentPath + "." + sub.Name;

                if (!TryGetPresent(obj, sub.Name, out var subRaw))
                {
                    if (sub.Required)
                    {
                        issues.Add(new ValidationIssue(ValidationSeverity.Error, table, "required_field",
                            $"必填字段 \"{sub.Name}\" 缺失", recordKey: recordKey, field: childPath));
                    }
                    continue;
                }

                ValidateFieldValue(table, recordKey, childPath, sub, subRaw, issues, depth + 1);
            }
        }

        private void ReportUnknownSubfields(string table, string recordKey, string fieldPath, JsonObject obj, HashSet<string> known, List<ValidationIssue> issues)
        {
            if (_options.UnknownSubfieldSeverity != UnknownSubfieldPolicy.Warning) return;

            foreach (var entry in obj)
            {
                if (known.Contains(entry.Key)) continue;

                issues.Add(new ValidationIssue(ValidationSeverity.Warning, table, "unknown_subfield",
                    $"字段 \"{fieldPath}\" 出现未登记的子字段 \"{entry.Key}\"（未登记子结构默认允许扩展，不影响加载）",
                    recordKey: recordKey, field: fieldPath + "." + entry.Key));
            }
        }

        private void ValidateArrayField(string table, string recordKey, string fieldPath, FieldSchema field, JsonValue raw, List<ValidationIssue> issues, int depth)
        {
            if (!(raw is JsonArray arr))
            {
                AddFieldTypeError(issues, table, recordKey, fieldPath, raw, "Array");
                return;
            }

            var item = field.Item;
            if (item == null)
            {
                return; // 未登记元素结构：维持"存在且是数组"（向后兼容）。
            }

            if (depth >= MaxSubstructureDepth)
            {
                AddSubstructureDepthError(issues, table, recordKey, fieldPath);
                return;
            }

            for (int i = 0; i < arr.Count; i++)
            {
                ValidateFieldValue(table, recordKey, $"{fieldPath}[{i}]", item, arr[i], issues, depth + 1);
            }
        }

        private static void AddSubstructureDepthError(List<ValidationIssue> issues, string table, string recordKey, string fieldPath)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, table, "substructure_depth",
                $"字段 \"{fieldPath}\" 的子结构递归深度超过上限 {MaxSubstructureDepth}，已停止对该子树继续校验" +
                "（防御登记错误导致的无限递归，见 FieldSchema.Item/Variants 惰性求值判断记录）",
                recordKey: recordKey, field: fieldPath));
        }

        private static bool TryGetPresent(JsonObject obj, string key, out JsonValue raw)
        {
            if (obj.TryGetValue(key, out var v) && v.Kind != JsonKind.Null)
            {
                raw = v;
                return true;
            }

            raw = null!;
            return false;
        }

        // -----------------------------------------------------------------
        // 标量/引用/文本键/表达式/枚举校验（顶层字段与 ADR-0019 子结构递归共用）
        // -----------------------------------------------------------------

        private static void AddFieldTypeError(List<ValidationIssue> issues, string table, string recordKey, string fieldPath, JsonValue raw, string expected)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, table, "field_type",
                $"字段 \"{fieldPath}\" 期望 {expected}，实际 JSON 类型 {raw.Kind}", recordKey: recordKey, field: fieldPath));
        }

        private static void ValidateIdList(string table, string recordKey, string fieldPath, JsonValue raw, List<ValidationIssue> issues)
        {
            if (!(raw is JsonArray arr))
            {
                AddFieldTypeError(issues, table, recordKey, fieldPath, raw, "IdList");
                return;
            }

            for (int i = 0; i < arr.Count; i++)
            {
                if (!(arr[i] is JsonString s) || !CommonId.IsValidFormat(s.Value))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, table, "field_type",
                        $"字段 \"{fieldPath}\" 第 {i} 个元素不是合法 Id", recordKey: recordKey, field: fieldPath));
                }
            }
        }

        private void ValidateReferenceField(string table, string recordKey, string fieldPath, FieldSchema field, JsonValue raw, List<ValidationIssue> issues)
        {
            if (!(raw is JsonString s) || !CommonId.IsValidFormat(s.Value))
            {
                AddFieldTypeError(issues, table, recordKey, fieldPath, raw, "Reference(Id)");
                return;
            }

            var value = s.Value;

            if (field.ReferenceTable != null)
            {
                if (!ReferenceExists(field.ReferenceTable, value))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, table, "reference_integrity",
                        $"字段 \"{fieldPath}\" 的值 \"{value}\" 在表 \"{field.ReferenceTable}\" 中不存在", recordKey: recordKey, field: fieldPath));
                }
            }
            else if (field.ReferenceDomain != null)
            {
                if (!CommonId.TryParse(value, out var idVal) || !string.Equals(idVal.Domain, field.ReferenceDomain, StringComparison.Ordinal))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, table, "reference_integrity",
                        $"字段 \"{fieldPath}\" 的值 \"{value}\" 的 domain 应为 \"{field.ReferenceDomain}\"", recordKey: recordKey, field: fieldPath));
                }
                else if (!ReferenceExistsInDomain(field.ReferenceDomain, value))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, table, "reference_integrity",
                        $"字段 \"{fieldPath}\" 的值 \"{value}\" 在 domain \"{field.ReferenceDomain}\" 下的任何已加载表中都不存在",
                        recordKey: recordKey, field: fieldPath));
                }
            }
        }

        private void ValidateTextKeyField(string table, string recordKey, string fieldPath, JsonValue raw, List<ValidationIssue> issues)
        {
            if (!(raw is JsonString s) || !CommonId.IsValidFormat(s.Value))
            {
                AddFieldTypeError(issues, table, recordKey, fieldPath, raw, "TextKey(Id)");
                return;
            }

            var textKey = s.Value;

            if (!_tables.TryGetValue("l10n.text", out var l10nText))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, table, "text_key_exists",
                    "l10n.text 表未加载，跳过文本键存在性检查", recordKey: recordKey, field: fieldPath));
                return;
            }

            var defaultLocale = _options.DefaultLocale.Value;
            var composite = textKey + "@" + defaultLocale;
            if (!l10nText.ByKey.ContainsKey(composite))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, table, "text_key_exists",
                    $"文本键 \"{textKey}\" 在语言 \"{defaultLocale}\" 下不存在", recordKey: recordKey, field: fieldPath));
            }
        }

        private void ValidateExprField(string table, string recordKey, string fieldPath, JsonValue raw, List<ValidationIssue> issues)
        {
            if (!(raw is JsonString s))
            {
                AddFieldTypeError(issues, table, recordKey, fieldPath, raw, "Expr(String)");
                return;
            }

            if (_options.ExprSchema == null)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, table, "expr_parsable",
                    "ExprSchema 未配置，跳过表达式解析与静态校验", recordKey: recordKey, field: fieldPath));
                return;
            }

            ExprNode node;
            try
            {
                node = ExprParser.Parse(s.Value, _options.ExprSchema);
            }
            catch (ExprParseException ex)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, table, "expr_parsable",
                    $"表达式解析失败：{ex.Message}", recordKey: recordKey, field: fieldPath));
                return;
            }

            var exprIssues = ExprValidator.Validate(node, _options.ExprSchema);
            for (int i = 0; i < exprIssues.Count; i++)
            {
                var exprIssue = exprIssues[i];
                var severity = exprIssue.Severity == ExprIssueSeverity.Error ? ValidationSeverity.Error : ValidationSeverity.Warning;
                issues.Add(new ValidationIssue(severity, table, "expr_parsable", exprIssue.Message,
                    recordKey: recordKey, field: fieldPath));
            }
        }

        private static void ValidateEnumField(string table, string recordKey, string fieldPath, FieldSchema field, JsonValue raw, List<ValidationIssue> issues)
        {
            if (!(raw is JsonString s))
            {
                AddFieldTypeError(issues, table, recordKey, fieldPath, raw, "Enum(String)");
                return;
            }

            var values = field.EnumValues;
            var found = false;
            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    if (string.Equals(values[i], s.Value, StringComparison.Ordinal)) { found = true; break; }
                }
            }

            if (!found)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, table, "field_type",
                    $"字段 \"{fieldPath}\" 取值 \"{s.Value}\" 不在枚举合法集合内", recordKey: recordKey, field: fieldPath));
            }
        }

        private bool ReferenceExists(string tableName, string value) =>
            _tables.TryGetValue(tableName, out var t) && t.ByKey.ContainsKey(value);

        private bool ReferenceExistsInDomain(string domain, string value)
        {
            foreach (var kv in _tables)
            {
                var firstSeg = kv.Key.Split('.')[0];
                if (string.Equals(firstSeg, domain, StringComparison.Ordinal) && kv.Value.ByKey.ContainsKey(value))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
