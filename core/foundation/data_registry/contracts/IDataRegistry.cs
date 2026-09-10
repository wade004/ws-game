using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Expr;
using CommonId = Core.Foundation.Common.Id;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 只读查询面（见 04 第 4 节 DataRegistry 接口"get/getAll/query：只读访问"）。数据未通过
    /// 校验（<see cref="ValidationReport.IsBlocking"/>）时，实现方的 <see cref="Get(string, string)"/>
    /// 等方法一律抛 <see cref="System.InvalidOperationException"/>（见 11 第 4 节"运行时不做
    /// 静默降级"），本接口只声明契约，具体拒绝行为由 <c>DataRegistry</c> 实现。
    /// </summary>
    public interface IDataRegistryView
    {
        DataRecord? Get(string table, string key);

        DataRecord? Get(string table, CommonId id);

        IReadOnlyList<DataRecord> GetAll(string table);

        /// <summary><paramref name="predicate"/> 的宿主上下文只暴露记录自身字段
        /// （<c>self.&lt;field&gt;</c>，见 04 第 4 节"宿主上下文只暴露记录自身字段"）。</summary>
        IReadOnlyList<DataRecord> Query(string table, ExprNode predicate);

        /// <summary>便捷重载：解析 <paramref name="predicateText"/> 用
        /// <see cref="RecordExprSchema.For"/> 把 <paramref name="table"/> 的字段登记为
        /// <c>self.&lt;field&gt;</c> 后再解析（见 04 第 4 节）。要求该表已 <see cref="IDataRegistry.RegisterSchema"/>。</summary>
        IReadOnlyList<DataRecord> Query(string table, string predicateText);

        IReadOnlyList<string> Tables { get; }

        TableSchema? GetSchema(string table);

        /// <summary>
        /// 判断记录（消费方反馈 E10 根治，2026-09-10，见
        /// architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E10）：全部已加载表的记录总数。
        /// <see cref="Presentation.Assembly.ContentValidationAssembly.Run"/> 走一条独立的事件订阅
        /// 路径拿到精确记录数（见该方法判断记录——即便加载结果阻断也能取到，<see cref="GetAll"/>
        /// 阻断时会抛异常，两条路径语义不同、不能互相替代）；但 <see cref="Presentation.Assembly.ContentValidationAssembly.CreateRegistry"/>
        /// 场景下调用方自己持有 registry、自行决定何时调用 <see cref="IDataRegistry.LoadAll()"/>/
        /// <see cref="IDataRegistry.Reload"/>（例如编辑器需要在用户操作间隙重复单表 Reload），
        /// 此前完全没有拿到记录总数的办法。本成员按 <see cref="Tables"/>/<see cref="GetAll"/> 求和
        /// 给出默认实现——用带默认实现的接口成员（C# 8+/netstandard2.1 支持，不要求任何已有实现类
        /// 显式实现它）新增，不是要求实现方必须提供的抽象成员，因此不构成"公开 API 表面"意义上的
        /// 破坏性变更（不会让任何现有 <see cref="IDataRegistryView"/> 实现类编译失败）。与
        /// <see cref="Get(string, string)"/> 同样的前提：数据未通过校验时调用方需自行处理
        /// <see cref="GetAll"/> 可能抛出的异常（本成员不做额外的静默降级，见 11 第 4 节"运行时不做
        /// 静默降级"）。
        /// <para>
        /// 判断记录（消费方反馈第 17 条根治，2026-09-10，见
        /// architecture/落地计划/消费方反馈-2026-09-10-编辑器-第二批.md 第 17 条）：本默认实现按
        /// <see cref="Tables"/>/<see cref="GetAll"/> 求和这一点保持不变（上一段判断记录仍然成立，
        /// 是"未持有具体 <c>DataRegistry</c> 实现、只有本接口"的第三方/测试替身唯一通用的兜底算法），
        /// 因此阻断态下仍可能抛出 <see cref="GetAll"/> 抛出的异常——调用方明确知道自己拿到的是具体
        /// <see cref="Core.Foundation.DataRegistry.DataRegistry"/> 实例时，改用该类型显式覆盖的
        /// <c>RecordCount</c>（阻断态不抛，直接读内部按表合并去重后的记录快照，见该类型同名成员
        /// 判断记录）；只持有 <see cref="IDataRegistryView"/>/<see cref="IDataRegistry"/> 接口、
        /// 不确定当前是否阻断时，改用 <see cref="TryGetRecordCount"/>（阻断态返回 <c>false</c> 而不
        /// 抛异常）。
        /// </para>
        /// </summary>
        int RecordCount => Tables.Sum(table => GetAll(table).Count);

        /// <summary>
        /// 判断记录（消费方反馈第 17 条根治，2026-09-10，见
        /// architecture/落地计划/消费方反馈-2026-09-10-编辑器-第二批.md 第 17 条）：尝试获取
        /// <see cref="RecordCount"/>，阻断态（或任何导致 <see cref="RecordCount"/> 默认实现内部
        /// <see cref="GetAll"/> 抛出"数据校验未通过，禁止读取"的场景）返回 <c>false</c> 而不抛异常，
        /// <paramref name="count"/> 置 0；成功时返回 <c>true</c>，<paramref name="count"/> 为实际
        /// 记录总数。默认实现按"try <see cref="RecordCount"/>，只捕获
        /// <see cref="System.InvalidOperationException"/>（<c>DataRegistry.EnsureReadable</c> 阻断态
        /// 精确抛出的类型，见该方法实现）"给出——不用 <c>catch (Exception)</c> 掩盖其它意外异常，
        /// 呼应 11 第 4 节"运行时不做静默降级"：这里的"降级"仅限于把已知的、契约明确的阻断异常
        /// 转换成 <c>false</c>，其它类型异常仍会照常向外抛出。带默认实现的接口成员新增，同样不构成
        /// "公开 API 表面"意义上的破坏性变更（见 <see cref="RecordCount"/> 同一处判断记录）。
        /// <see cref="Core.Foundation.DataRegistry.DataRegistry"/> 显式覆盖为永不抛出的直接实现
        /// （见该类型同名成员判断记录），不经过这里的 try/catch。
        /// </summary>
        bool TryGetRecordCount(out int count)
        {
            try
            {
                count = RecordCount;
                return true;
            }
            catch (System.InvalidOperationException)
            {
                count = 0;
                return false;
            }
        }

        /// <summary>
        /// 消费方反馈第三批第 20 条（2026-09-10，见
        /// architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md 第 20 条）：阻断态下的
        /// 只读通道——仅供内容工具使用（编辑器等场景需要在数据未通过校验时仍能读取当前已合并的
        /// 记录，供作者定位/修复问题；运行期宿主必须继续用 <see cref="GetAll"/>，阻断态即禁止
        /// 读取，见 11 第 4 节"运行时不做静默降级"，本成员不改变那条规则）。默认实现按
        /// "try <see cref="GetAll"/>，只捕获 <see cref="System.InvalidOperationException"/>
        /// （<c>DataRegistry.EnsureReadable</c> 阻断态精确抛出的类型）"给出——不用
        /// <c>catch (Exception)</c> 掩盖其它意外异常。阻断态返回 <c>false</c>，
        /// <paramref name="records"/> 置空列表；非阻断态与 <see cref="GetAll"/> 结果一致，返回
        /// <c>true</c>。带默认实现的接口成员新增，不构成"公开 API 表面"意义上的破坏性变更（与
        /// <see cref="RecordCount"/>/<see cref="TryGetRecordCount"/> 同一批判断记录）。
        /// <see cref="Core.Foundation.DataRegistry.DataRegistry"/> 显式覆盖为阻断态也能读取的
        /// 直接实现（见该类型同名成员判断记录），不经过这里的 try/catch。</summary>
        bool TryGetAll(string table, out IReadOnlyList<DataRecord> records)
        {
            try
            {
                records = GetAll(table);
                return true;
            }
            catch (System.InvalidOperationException)
            {
                records = System.Array.Empty<DataRecord>();
                return false;
            }
        }

        /// <summary>消费方反馈第三批第 20 条：<see cref="TryGetAll"/> 的同族成员，覆盖
        /// <see cref="Query(string, ExprNode)"/>（<c>ExprNode</c> 谓词重载）——语义与判断记录完全
        /// 相同，只是把 <see cref="GetAll"/> 换成 <see cref="Query(string, ExprNode)"/>。</summary>
        bool TryQuery(string table, ExprNode predicate, out IReadOnlyList<DataRecord> records)
        {
            try
            {
                records = Query(table, predicate);
                return true;
            }
            catch (System.InvalidOperationException)
            {
                records = System.Array.Empty<DataRecord>();
                return false;
            }
        }

        /// <summary>消费方反馈第三批第 20 条：<see cref="TryGetAll"/> 的同族成员，覆盖
        /// <see cref="Query(string, string)"/>（谓词文本重载）——语义与判断记录完全相同。</summary>
        bool TryQuery(string table, string predicateText, out IReadOnlyList<DataRecord> records)
        {
            try
            {
                records = Query(table, predicateText);
                return true;
            }
            catch (System.InvalidOperationException)
            {
                records = System.Array.Empty<DataRecord>();
                return false;
            }
        }
    }

    /// <summary>
    /// L0 基础层暴露给全架构的唯一数据入口（见 04 第 4 节、01_分层与依赖.md L0 模块表
    /// <c>data_registry</c> 行）。加载/注册/校验方法之外的只读查询面见 <see cref="IDataRegistryView"/>。
    /// </summary>
    public interface IDataRegistry : IDataRegistryView
    {
        void RegisterSchema(TableSchema schema);

        /// <summary>动态声明"某表某字段是指向另一张表的外键"（见 04 第 4 节
        /// <c>declareReference</c>），等价于把该字段标记为 <see cref="FieldKind.Reference"/>——
        /// 供尚未在 <see cref="TableSchema"/> 里声明该字段为 Reference 的场景补充声明，
        /// 不修改已注册的 <see cref="TableSchema"/> 本身。</summary>
        void DeclareReference(string fromTable, string field, string toTable);

        void RegisterValidationRule(IValidationRule rule);

        ValidationReport LoadAll();

        /// <summary>多根加载（数据目录框架/游戏分层任务新增，见 <c>data/README.md</c>"框架级数据表
        /// 与游戏数据目录并列加载"一节、<c>DataRegistry</c> 类型级判断记录"合并规则"）：同一张表
        /// 出现在多个 <paramref name="sources"/> 时合并其行；合并期间同一主键跨根重复、或同一表
        /// 跨根信封 <c>schema_version</c> 不一致，均判定为阻断错误。<see cref="LoadAll()"/>
        /// （无参）等价于 <c>LoadAll(new[] { 构造函数传入的单一 source })</c>，行为不变。</summary>
        ValidationReport LoadAll(IReadOnlyList<IDataSource> sources);

        ValidationReport Validate();

        /// <summary>仅限开发期使用的单表热重载（见 04 第 4 节"reload：建议，仅用于开发期内容
        /// 热更新"）：重新读取并解析 <paramref name="table"/>，替换内存态记录，随后重跑一次
        /// 全量 <see cref="Validate"/>（引用完整性等检查天然跨表，不能只校验单表）。</summary>
        ValidationReport Reload(string table);

        /// <summary>只读诊断（数据目录框架/游戏分层任务新增）：<paramref name="table"/> 本次加载
        /// 实际来自哪些数据根（<see cref="DataTableSource.Location"/> 列表）；单根加载时只有一条，
        /// 多根合并加载（见 <see cref="LoadAll(IReadOnlyList{IDataSource})"/>）时按参与合并的根
        /// 顺序排列；表未加载时返回空列表。不参与任何校验判定。判断记录：声明在 <see cref="IDataRegistry"/>
        /// 而不是更基础的 <see cref="IDataRegistryView"/>——<c>core/carriers/creature/tests</c>
        /// 下已有一个只实现 <see cref="IDataRegistryView"/>（不含本接口其余加载/注册方法）的测试替身
        /// （该目录本次任务不可改动，见任务书硬性规则 1），若加到 <see cref="IDataRegistryView"/>
        /// 会破坏其编译；只有 <c>DataRegistry</c> 本身实现完整的 <see cref="IDataRegistry"/>，加在
        /// 这一层没有同样的兼容性风险。</summary>
        IReadOnlyList<string> GetTableSourceLocations(string table);

        /// <summary>只读诊断（数据行覆盖语义任务新增，见 <see cref="OverrideDiagnostic"/>、
        /// <c>DataRegistry</c> 类型级判断记录"覆盖语义"）：最近一次 <see cref="LoadAll()"/>/
        /// <see cref="LoadAll(IReadOnlyList{IDataSource})"/>（以及随后任意次 <see cref="Reload(string)"/>，
        /// 按表增量更新）实际发生的全部行覆盖；不参与任何校验判定（覆盖成功不算警告也不算错误）。
        /// 声明位置的理由与 <see cref="GetTableSourceLocations(string)"/> 相同（<c>core/carriers/
        /// creature/tests</c> 下的 <see cref="IDataRegistryView"/> 测试替身不可改动，见任务书硬性
        /// 规则 1）。</summary>
        IReadOnlyList<OverrideDiagnostic> GetOverrideDiagnostics();
    }
}
