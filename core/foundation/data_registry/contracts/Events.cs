using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Foundation.DataRegistry
{
    /// <summary>本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// data/_sample/found/found.event_catalog.json、01_分层与依赖.md L0 模块表
    /// <c>data_registry</c> 行"主要事件：data.load_completed、data.validation_failed"）。</summary>
    public static class DataRegistryEventKeys
    {
        public static readonly Id LoadCompleted = new Id("data.load_completed");
        public static readonly Id ValidationFailed = new Id("data.validation_failed");
    }

    /// <summary>
    /// <see cref="DataRegistry.LoadAll()"/> 完成一批数据表加载后触发（无论报告是否阻断，都会
    /// 发出——阻断时紧接着还会再发 <see cref="DataValidationFailedEvent"/>，见
    /// <c>data_registry/README.md</c>"加载流程"）。
    /// <para>
    /// 判断记录：<c>found.event_catalog.json</c> 原登记 <c>data.load_completed</c> 行的
    /// <c>fields</c> 只有 <c>["tableCount"]</c>（该行 description 标注"字段为建议值"）。本类型
    /// 额外携带 <see cref="RecordCount"/>/<see cref="ErrorCount"/>/<see cref="WarningCount"/>，
    /// 因为下游（如加载画面、CI 日志）普遍需要知道本次加载的记录总数与校验结果概览，省得
    /// 再单独查询 <see cref="ValidationReport"/>；已同步把登记表该行的 <c>fields</c> 改为与本类型
    /// 字段一致（"若登记表字段与实现不一致，按任务书要求以本模块实现为准并同步登记表"）。
    /// </para>
    /// </summary>
    public sealed class DataLoadCompletedEvent : IEvent
    {
        public Id Key => DataRegistryEventKeys.LoadCompleted;

        public int TableCount { get; }

        public int RecordCount { get; }

        public int ErrorCount { get; }

        public int WarningCount { get; }

        public DataLoadCompletedEvent(int tableCount, int recordCount, int errorCount, int warningCount)
        {
            TableCount = tableCount;
            RecordCount = recordCount;
            ErrorCount = errorCount;
            WarningCount = warningCount;
        }
    }

    /// <summary>
    /// 校验报告为阻断态（<see cref="ValidationReport.IsBlocking"/>）时紧接着触发（见 04 第 5 节
    /// 校验器、01 模块表 <c>data_registry</c> 行）。
    /// <para>
    /// 判断记录：<c>found.event_catalog.json</c> 原登记行 <c>fields</c> 只有 <c>["errorCount"]</c>，
    /// 本类型额外携带 <see cref="WarningCount"/>（阻断可能来自 <c>WarningsBlock</c> 严格级别下
    /// 仅有 Warning 的情形，下游需要区分"因错误阻断"还是"因警告阻断"），已同步登记表。
    /// </para>
    /// </summary>
    public sealed class DataValidationFailedEvent : IEvent
    {
        public Id Key => DataRegistryEventKeys.ValidationFailed;

        public int ErrorCount { get; }

        public int WarningCount { get; }

        public DataValidationFailedEvent(int errorCount, int warningCount)
        {
            ErrorCount = errorCount;
            WarningCount = warningCount;
        }
    }
}
