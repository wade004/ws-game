using System;
using System.Collections.Generic;
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
    /// <c>DataRegistry.LoadAll</c> 完成一批数据表加载后触发（无论报告是否阻断，都会
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
    /// <para>
    /// 判断记录（消费方反馈第 71 条根治，2026-09-20，ADR-0046）：新增只读属性 <see cref="Issues"/>
    /// 与对应三参数构造函数（ABI 只新增；既有两参数构造函数保留、内部委派到三参数构造并把
    /// <see cref="Issues"/> 置为共享的空列表单例，行为对既有调用点逐字节不变）。背景：此前本事件
    /// 只带 <see cref="ErrorCount"/>/<see cref="WarningCount"/> 两个汇总计数，同进程内订阅事件总线
    /// 的消费方拿不到逐条 <see cref="ValidationIssue"/>，只能退回去读运行期宿主
    /// （<c>GameBootstrap</c>/<c>DataHotReload</c>）打印的 <see cref="ValidationIssue.ToString()"/>
    /// 拼接文本——而该方法的文档已明确声明"不是稳定契约"。<see cref="Issues"/> 直接暴露
    /// <see cref="ValidationReport.Issues"/> 本身（不做任何投影/裁剪），其元素的公开只读属性
    /// （<see cref="ValidationIssue.Severity"/>/<see cref="ValidationIssue.Table"/>/
    /// <see cref="ValidationIssue.RecordKey"/>/<see cref="ValidationIssue.Field"/>/
    /// <see cref="ValidationIssue.Check"/>/<see cref="ValidationIssue.Message"/>/
    /// <see cref="ValidationIssue.Group"/>/<see cref="ValidationIssue.Note"/>/
    /// <see cref="ValidationIssue.RuleId"/>/<see cref="ValidationIssue.AffectedNodeIds"/>）与
    /// <c>toolchain/validator --json</c> 的 <c>issues[]</c> 元素字段一一对应（该命令行输出正是把
    /// 这些同名属性序列化为 JSON，见 <c>toolchain/validator/Program.cs</c> 的
    /// <c>AppendIssueJson</c>），两条通道天然共用同一份数据形状、不需要额外的映射/对齐代码，也不会
    /// 因为两处各自维护一份序列化逻辑而漂移。
    /// </para>
    /// <para>
    /// 判断记录（局限，如实标注）：本扩展只服务与本进程同进程内、订阅事件总线的 C#
    /// 消费方（如未来可能出现的 Unity 编辑器内工具）。若消费方是完全独立的外部进程（如
    /// ADR-0018 确立的"编辑器随游戏走"独立工程），本扩展不解决它读取运行期校验结果的诉求——
    /// 这需要另一种能跨进程送达的结构化出口（如约定格式的落盘文件、或带固定前缀的结构化日志行），
    /// 那属于新对外契约，需要设计层单独拍板格式稳定承诺，不在本次改动范围内，见 ADR-0046"备选
    /// 方案与为什么不选"一节。
    /// </para>
    /// </summary>
    public sealed class DataValidationFailedEvent : IEvent
    {
        private static readonly ValidationIssue[] NoIssues = Array.Empty<ValidationIssue>();

        public Id Key => DataRegistryEventKeys.ValidationFailed;

        public int ErrorCount { get; }

        public int WarningCount { get; }

        /// <summary>本次校验阻断报告的逐条问题（ADR-0046）；经两参数构造函数构造时为空集合（不是
        /// <c>null</c>），惯例同 <see cref="ValidationIssue.AffectedNodeIds"/>。</summary>
        public IReadOnlyList<ValidationIssue> Issues { get; }

        public DataValidationFailedEvent(int errorCount, int warningCount)
            : this(errorCount, warningCount, NoIssues)
        {
        }

        /// <summary>ADR-0046 新增：附带逐条问题清单的构造。</summary>
        public DataValidationFailedEvent(int errorCount, int warningCount, IReadOnlyList<ValidationIssue> issues)
        {
            ErrorCount = errorCount;
            WarningCount = warningCount;
            Issues = issues ?? NoIssues;
        }
    }
}
