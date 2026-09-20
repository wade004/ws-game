#nullable enable
// ValidationIssueJsonWriter：ADR-0047 运行期校验报告结构化转发出口（落盘文件）落地时，把此前只在
// toolchain/validator/Program.cs 内部私有的 ValidationIssue -> JSON 序列化逻辑（AppendIssueJson/
// JsonEscape）原样搬到本模块、改为公开静态方法，供 toolchain/validator 与运行期宿主的落盘出口
// （adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Diagnostics/
// ValidationReportFileOutlet.cs）共用同一份实现。
//
// 判断记录（为什么放在本模块、不是 toolchain 或 adapters/unity）：ValidationIssue 本身定义在本模块
// （contracts/ValidationReport.cs），是依赖图里的最下游——toolchain/validator 经
// presentation/Presentation.Common.csproj 的 ProjectReference 链（Presentation.Common ->
// Core.Gameplay -> Core.Carriers -> Core.Rules -> Core.Numbers -> Core.Foundation）能直接引用本
// 模块；adapters/unity 的 Runtime 程序集（Unity asmdef 的 precompiledReferences）本就固定包含
// Core.Foundation.dll。放在本模块是唯一不需要新增任何项目引用、两个上游都已经能直接看到的位置，
// 符合本模块 README"依赖：只依赖 core/foundation/common ... 与 .NET 标准库；不引用 adapters/ 下
// 任何具体实现"这条既有约束（本类型本身不引用 adapters/，只是被 adapters/unity 引用，方向不变）。
//
// 判断记录（为什么是"搬" 不是"抽公共基类/接口再各自实现"）：AppendIssueJson 的输出形状本身就是
// ADR-0047 要求"落盘文件与命令行 --json 输出的 issues[] 元素字段完全一致"的单一事实来源——两条
// 通道共用同一份实现，未来任一方新增/调整字段，另一方自动同步，不需要额外的一致性守护测试（对比
// ADR-0046 判断记录"两条通道天然共用同一份字段集合"，本次更进一步做到共用同一份序列化代码，不是
// 两处字段名凑巧相同）。toolchain/validator/Program.cs 原有的 private AppendIssueJson/JsonEscape
// 两个方法体已原样搬到这里，Program.cs 改为委托调用（见该文件同名方法的判断记录），不重复实现。
using System;
using System.Globalization;
using System.Text;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// <see cref="ValidationIssue"/> 的 JSON 序列化（单一实现，供命令行 <c>toolchain/validator
    /// --json</c> 出口与运行期落盘出口共用，见本文件顶部判断记录）。
    /// </summary>
    public static class ValidationIssueJsonWriter
    {
        /// <summary>把一条 <see cref="ValidationIssue"/> 追加为 JSON 对象（不含前后逗号/换行）。
        /// 字段集合与顺序：<c>severity</c>/<c>table</c>/<c>record_key</c>/<c>field</c>/<c>check</c>/
        /// <c>message</c>/<c>group</c>/<c>note</c>/<c>rule_id</c>，以及
        /// <see cref="ValidationIssue.AffectedNodeIds"/> 非空时追加的 <c>affected_node_ids</c>
        /// （空时整体省略，不是 <c>null</c>，与既有 group/note/rule_id 的"未填也输出 null"口径不同，
        /// 是该字段专属约定，见 <see cref="ValidationIssue.AffectedNodeIds"/> 判断记录）。</summary>
        public static void AppendIssueJson(StringBuilder sb, ValidationIssue issue)
        {
            sb.Append('{');
            sb.Append("\"severity\":\"").Append(issue.Severity == ValidationSeverity.Error ? "error" : "warning").Append("\",");
            sb.Append("\"table\":\"").Append(JsonEscape(issue.Table)).Append("\",");
            sb.Append("\"record_key\":").Append(issue.RecordKey == null ? "null" : "\"" + JsonEscape(issue.RecordKey) + "\"").Append(',');
            sb.Append("\"field\":").Append(issue.Field == null ? "null" : "\"" + JsonEscape(issue.Field) + "\"").Append(',');
            sb.Append("\"check\":\"").Append(JsonEscape(issue.Check)).Append("\",");
            sb.Append("\"message\":\"").Append(JsonEscape(issue.Message)).Append("\",");
            sb.Append("\"group\":").Append(issue.Group == null ? "null" : "\"" + JsonEscape(issue.Group) + "\"").Append(',');
            sb.Append("\"note\":").Append(issue.Note == null ? "null" : "\"" + JsonEscape(issue.Note) + "\"").Append(',');
            sb.Append("\"rule_id\":").Append(issue.RuleId == null ? "null" : "\"" + JsonEscape(issue.RuleId) + "\"");
            if (issue.AffectedNodeIds.Count > 0)
            {
                sb.Append(",\"affected_node_ids\":[");
                for (var i = 0; i < issue.AffectedNodeIds.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append('"').Append(JsonEscape(issue.AffectedNodeIds[i])).Append('"');
                }
                sb.Append(']');
            }
            sb.Append('}');
        }

        /// <summary>通用 JSON 字符串转义（控制字符转 <c>\uXXXX</c>），与
        /// <see cref="AppendIssueJson"/> 共用，也供调用方拼接本类型未直接覆盖的其它字段（如落盘出口
        /// 信封字段 <c>source</c>）时复用，避免另写一份转义逻辑。</summary>
        public static string JsonEscape(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
