using System;
using System.Collections.Generic;
using Core.Foundation.Expr;
using CommonId = Core.Foundation.Common.Id;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 消费方反馈第 45 条（2026-09-17，见
    /// architecture/落地计划/消费方反馈-2026-09-17-编辑器-第45条.md）：只读分析/展示类入口
    /// （<c>ItemBudgetCurve.BuildStatBudgetInfo</c>、<c>EquipmentScoreAnalyzer.Score</c>、
    /// <c>SkillBudgetAnalyzer.Analyze</c>、<c>ExpectedStatCalculator</c> 等——产出的是给人看的
    /// 分析/展示信息，不是驱动运行期业务逻辑的权威数据，见 architecture/11_工程规范与测试.md 第 4
    /// 节"运行时不做静默降级"判断记录：本类型不适用于运行期宿主，那些场景必须继续用 <see
    /// cref="IDataRegistryView.Get(string, string)"/>/<see cref="IDataRegistryView.GetAll"/> 原样
    /// 抛出）内部按主键/整表读取 <see cref="IDataRegistryView"/> 时，此前一律用严格的
    /// <c>Get</c>/<c>GetAll</c>——registry 处于阻断态（<c>DataRegistry.EnsureReadable</c> 的阻断
    /// 标记是整个 registry 级别的，不按表/记录粒度）时即便被改动的记录与当前读取的表毫无关系，仍会
    /// 抛 <see cref="InvalidOperationException"/>，炸穿"编辑器用户刚把数值改到触发某条 Error 校验
    /// 的那一刻，仍需要算出并展示预算消耗"这一类场景（反馈原文复现步骤）。
    /// <para>
    /// 本类型把任意 <see cref="IDataRegistryView"/> 包装成一个"读不到就退化为空/null，不向外抛
    /// 阻断异常"的只读视图：<see cref="Get(string, string)"/>/<see cref="GetAll"/>/
    /// <see cref="Query(string, ExprNode)"/>/<see cref="Query(string, string)"/> 一律先走内层
    /// <see cref="IDataRegistryView.TryGet(string, string, out DataRecord)"/>/<see
    /// cref="IDataRegistryView.TryGetAll"/>/<see cref="IDataRegistryView.TryQuery(string, ExprNode,
    /// out IReadOnlyList{DataRecord})"/> 容错通道，失败（阻断）时记录下这张表的名字（供
    /// <see cref="MissingTables"/> 读回）并返回 <c>null</c>/空集合，而不是向外传播异常——与直接调用
    /// 这些 Try* 成员的"手写包装"（内层若是具体 <see cref="DataRegistry"/>，其 <c>TryGetAll</c>/
    /// <c>TryGet</c> 显式实现本就绕开 <c>EnsureReadable</c> 直接读快照，因此绝大多数场景下阻断态
    /// 下这些支持表实际仍完整可读、结果并不真的"降级"，只是不再抛异常——见该两个成员判断记录）语义
    /// 完全一致，本类型只是把"逐处手写 try/TryXxx"这件事收敛成一个可重复使用的包装，供多个分析入口
    /// 共用，避免每个入口各自复刻一份。
    /// </para>
    /// <para>
    /// 判断记录（不是"内容工具需要自建包装"的框架版规避方案，而是分析入口内部实现细节）：本类型
    /// 只应由分析入口在自己方法体内部构造并使用（<c>new TolerantRegistryView(view)</c>，包起来后
    /// 全程只用包装后的引用，不再触碰原始 <paramref name="inner"/>），不改变调用方传入的原始
    /// <see cref="IDataRegistryView"/> 本身的阻断行为——同一个 <c>view</c> 实例若被运行期宿主
    /// （如 <c>EquipmentHost</c>/<c>SkillHost</c>）直接持有使用，读取时依然按 <c>EnsureReadable</c>
    /// 语义抛出，不受任何地方构造过的 <see cref="TolerantRegistryView"/> 影响（本类型不修改、不
    /// 缓存 <paramref name="inner"/> 的阻断状态，纯粹是转发时的异常处理策略不同）。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="MissingTables"/> 的"如实反映"语义）：只记录"因阻断读不到"这一种退化，
    /// 不包括"表/记录本就不存在"（属于内容配置缺失而不是数据整体阻断，两者语义独立，不应混为
    /// 一谈）。<c>GetAll</c>/<c>TryGetAll</c> 一侧沿用原判据（返回 <c>false</c> 即阻断，语义未受
    /// 下一段所述修正影响）。
    /// </para>
    /// <para>
    /// 判断记录（2026-09-19 补充，ADR-0041 <see cref="IDataRegistryView.TryGet(string, string, out DataRecord)"/>
    /// 契约行为修正的连带调整）：单记录一侧（<see cref="Get(string, string)"/>/本类型显式
    /// <c>TryGet</c>）此前直接借用 <c>_inner.TryGet</c> 的布尔返回值判定"是否阻断"，这依赖的是
    /// ADR-0041 之前的旧契约（阻断→<c>false</c>，记录不存在但不阻断→仍是 <c>true</c>）。ADR-0041
    /// 把 <c>TryGet</c> 的返回值改为诚实表达"是否找到记录"后，阻断与"记录本就不存在"两种情形都会
    /// 让 <c>TryGet</c> 返回 <c>false</c>，不能再单靠这一个布尔值反推是不是阻断；<see
    /// cref="Get(string, string)"/> 现改为"先按 <c>_inner.TryGet</c> 取记录，找不到时再探
    /// <c>_inner.TryGetAll(table, out _)</c>确认是否阻断"的两步判定（见该方法判断记录），恢复了
    /// 与本段"如实反映"语义完全一致的行为——具体 <see cref="DataRegistry"/> 实现下，
    /// <c>TryGetAll</c>/<c>TryGet</c> 显式实现均绕开 <c>EnsureReadable</c> 直接读内部表快照（阻断
    /// 只影响校验结论，不影响已成功解析入内存的表快照本身），因此用真实 <see cref="DataRegistry"/>
    /// 时 <see cref="MissingTables"/> 通常为空（阻断态下这些无关支持表依然完整可读，结果因此也是
    /// 完整、非近似的）；只有第三方/测试替身 <see cref="IDataRegistryView"/> 实现（未显式覆盖 Try*
    /// 成员、落回接口默认 try/catch 实现）在其 <c>Get</c>/<c>GetAll</c> 本身抛出时才会真正体现
    /// "退化"，与 ADR-0041 之前的结论一致，不是本次修正引入的新行为。
    /// </para>
    /// </summary>
    public sealed class TolerantRegistryView : IDataRegistryView
    {
        private readonly IDataRegistryView _inner;
        private readonly List<string> _missingTables = new List<string>();
        private readonly HashSet<string> _missingTableSet = new HashSet<string>(StringComparer.Ordinal);

        public TolerantRegistryView(IDataRegistryView inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>本次包装期间是否曾经因为阻断读不到过任何一张表（见类型判断记录"如实反映"
        /// 语义）。</summary>
        public bool IsDegraded => _missingTables.Count > 0;

        /// <summary>本次包装期间因阻断读不到的表名（按首次遇到的顺序，不重复；见类型判断记录）。</summary>
        public IReadOnlyList<string> MissingTables => _missingTables;

        /// <summary><see cref="MissingTables"/> 的按名查询版本——供调用方判断"刚刚那一次对某张具体
        /// 表/记录的读取，是不是恰好因为阻断才落空的"（而不是"这张表/这条记录本就不存在"，两者在
        /// <see cref="Get(string, string)"/>/<see cref="GetAll"/> 的返回值本身分不出来，见类型判断
        /// 记录"如实反映"语义），用于需要区分"降级返回"与"调用方传参错误"两种场景的入口（如
        /// <c>EquipmentScoreAnalyzer.Score</c> 对被评分的物品模板本身的查找）。</summary>
        public bool WasMissing(string table) => _missingTableSet.Contains(table);

        /// <summary><see cref="IsDegraded"/>/<see cref="MissingTables"/> 的快照打包（<see
        /// cref="TolerantReadDiagnostics"/>），供只读分析入口以 <c>out</c> 参数一次性回吐给调用方
        /// （见消费方反馈第 45 条各分析入口新增重载）。</summary>
        public TolerantReadDiagnostics Diagnostics => new TolerantReadDiagnostics(IsDegraded, MissingTables);

        private void NoteMissing(string table)
        {
            if (_missingTableSet.Add(table))
            {
                _missingTables.Add(table);
            }
        }

        /// <summary>
        /// 判断记录（2026-09-19，ADR-0041 <see cref="IDataRegistryView.TryGet(string, string, out DataRecord)"/>
        /// 契约行为修正的连带调整）：本方法此前直接把 <c>_inner.TryGet</c> 的布尔返回值当"是否因
        /// 阻断读不到"使用——这依赖的是修正前 <c>TryGet</c> 的旧语义（阻断→<c>false</c>，记录
        /// 不存在但不阻断→仍是 <c>true</c>）。ADR-0041 修正后，<c>TryGet</c> 的布尔返回值改为诚实
        /// 表达"是否找到记录"，阻断与"记录本就不存在"两种情形现在都会让 <c>TryGet</c> 返回
        /// <c>false</c>，不能再从这一个布尔值反推是不是阻断——如果继续按旧写法，本类型会把"记录
        /// 确实不存在"也误标记为 <see cref="IsDegraded"/>，违反类型级判断记录"如实反映"语义（只应
        /// 标记"因阻断读不到"这一种退化）。
        /// <para>
        /// 改为两步探测：先用 <c>_inner.TryGet</c> 取记录，找到就直接返回（不需要再判断是否阻断）；
        /// 找不到时，改探 <c>_inner.TryGetAll(table, out _)</c>（该成员语义未受本次修正影响——它
        /// 从不因"记录/表不存在"返回 <c>false</c>，只在 <see cref="Get(string, string)"/>/
        /// <see cref="GetAll"/> 抛出阻断异常时返回 <c>false</c>，见其判断记录）：<c>TryGetAll</c>
        /// 也失败，才说明这张表本身因阻断读不到，记下 <see cref="NoteMissing"/>；<c>TryGetAll</c>
        /// 成功则说明表可正常访问，只是这一条记录本就不存在，不标记退化。两次调用对同一个
        /// <see cref="DataRegistry"/> 实现均是直读内部快照（不重新解析/不重新加载），性能代价可
        /// 忽略；对遵循接口文档约定的第三方实现（阻断态是注册表级全局状态，<see cref="Get(string, string)"/>/
        /// <see cref="GetAll"/> 要么同时抛、要么都不抛）同样成立。
        /// </para>
        /// </summary>
        public DataRecord? Get(string table, string key)
        {
            if (_inner.TryGet(table, key, out var record))
            {
                return record;
            }

            if (!_inner.TryGetAll(table, out _))
            {
                NoteMissing(table);
            }

            return null;
        }

        public DataRecord? Get(string table, CommonId id) => Get(table, id.Value);

        public IReadOnlyList<DataRecord> GetAll(string table)
        {
            if (_inner.TryGetAll(table, out var records))
            {
                return records;
            }

            NoteMissing(table);
            return Array.Empty<DataRecord>();
        }

        public IReadOnlyList<DataRecord> Query(string table, ExprNode predicate)
        {
            if (_inner.TryQuery(table, predicate, out var records))
            {
                return records;
            }

            NoteMissing(table);
            return Array.Empty<DataRecord>();
        }

        public IReadOnlyList<DataRecord> Query(string table, string predicateText)
        {
            if (_inner.TryQuery(table, predicateText, out var records))
            {
                return records;
            }

            NoteMissing(table);
            return Array.Empty<DataRecord>();
        }

        public IReadOnlyList<string> Tables => _inner.Tables;

        public TableSchema? GetSchema(string table) => _inner.GetSchema(table);

        // -----------------------------------------------------------------
        // 显式转发全部带默认实现的 IDataRegistryView 成员（通用门禁
        // Tests.Presentation.Assembly.InterfaceDefaultMemberForwardingTests 要求：新增组合/包装
        // 实现方必须对每个默认接口成员显式转发/重写或登记豁免，不能悄悄落回接口默认实现——见该测试
        // 类型判断记录）。转发目标一律是 _inner 对应的 Try* 成员（阻断态下 false，同 Get/GetAll 的
        // 判断记录），RecordCount 例外——直接复用本类型自身已经容错的 GetAll，不转发 _inner
        // .RecordCount（后者对具体 DataRegistry 直读快照不受阻断影响，语义与本类型 Tables.Sum(GetAll)
        // 的既有默认公式一致，这里显式写一遍只是满足门禁"必须显式"的要求，不改变结果）。
        // -----------------------------------------------------------------

        public int RecordCount
        {
            get
            {
                var total = 0;
                foreach (var table in Tables)
                {
                    total += GetAll(table).Count;
                }
                return total;
            }
        }

        public bool TryGetRecordCount(out int count)
        {
            count = RecordCount;
            return true;
        }

        /// <summary>判断记录（2026-09-19，ADR-0041）：直接复用已经做过阻断/缺失探测的
        /// <see cref="Get(string, string)"/>（见其判断记录），不再重复一遍 <c>_inner.TryGet</c> +
        /// <c>_inner.TryGetAll</c> 的探测逻辑；返回值按 ADR-0041 新契约与 <see cref="Get(string, string)"/>
        /// 保持一致——<c>record != null</c> 才是 <c>true</c>。</summary>
        public bool TryGet(string table, string key, out DataRecord? record)
        {
            record = Get(table, key);
            return record != null;
        }

        public bool TryGet(string table, CommonId id, out DataRecord? record) => TryGet(table, id.Value, out record);

        public bool TryGetAll(string table, out IReadOnlyList<DataRecord> records)
        {
            if (_inner.TryGetAll(table, out records))
            {
                return true;
            }

            NoteMissing(table);
            records = Array.Empty<DataRecord>();
            return false;
        }

        public bool TryQuery(string table, ExprNode predicate, out IReadOnlyList<DataRecord> records)
        {
            if (_inner.TryQuery(table, predicate, out records))
            {
                return true;
            }

            NoteMissing(table);
            records = Array.Empty<DataRecord>();
            return false;
        }

        public bool TryQuery(string table, string predicateText, out IReadOnlyList<DataRecord> records)
        {
            if (_inner.TryQuery(table, predicateText, out records))
            {
                return true;
            }

            NoteMissing(table);
            records = Array.Empty<DataRecord>();
            return false;
        }

        public IReadOnlyList<ReferenceDeclaration> GetReferenceDeclarations() => _inner.GetReferenceDeclarations();

        /// <summary>消费方反馈第 45 条：<paramref name="view"/> 已经是 <see cref="TolerantRegistryView"/>
        /// 时原样返回，不重复包装（多个分析入口互相调用时——如 <c>EquipmentScoreAnalyzer.Score</c>
        /// 调用 <c>ItemBudgetCurve.BuildStatBudgetInfo</c>——避免嵌套包装导致 <see
        /// cref="MissingTables"/> 记录被截断在内层、外层看不到）；否则新建一层包装。</summary>
        public static TolerantRegistryView Wrap(IDataRegistryView view) =>
            view as TolerantRegistryView ?? new TolerantRegistryView(view);
    }

    /// <summary>消费方反馈第 45 条：<see cref="TolerantRegistryView.IsDegraded"/>/<see
    /// cref="TolerantRegistryView.MissingTables"/> 的不可变快照，供只读分析入口以 <c>out</c> 参数
    /// 回吐给调用方（内容工具据此提示"这份结果基于不完整数据"）。</summary>
    public readonly struct TolerantReadDiagnostics
    {
        /// <summary>不曾退化的中性值（<see cref="IsDegraded"/> 恒 <c>false</c>，<see
        /// cref="MissingTables"/> 恒空列表）——未经过 <see cref="TolerantRegistryView"/> 包装的
        /// 调用路径（如既有不带诊断输出的重载内部转调时用不到本值本身，但供测试/调用方需要一个
        /// 默认值占位时使用）。</summary>
        public static readonly TolerantReadDiagnostics None = new TolerantReadDiagnostics(false, Array.Empty<string>());

        public bool IsDegraded { get; }

        public IReadOnlyList<string> MissingTables { get; }

        public TolerantReadDiagnostics(bool isDegraded, IReadOnlyList<string> missingTables)
        {
            IsDegraded = isDegraded;
            MissingTables = missingTables ?? Array.Empty<string>();
        }
    }
}
