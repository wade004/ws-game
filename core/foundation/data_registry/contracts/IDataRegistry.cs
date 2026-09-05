using System.Collections.Generic;
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
