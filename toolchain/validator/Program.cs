using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Core.Carriers.Assembly;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Rules.Assembly;

namespace Toolchain.Validator
{
    /// <summary>
    /// 数据校验器阶段 2 的真实校验入口（落地方案 T2-12，阶段 3 整理"事项四"升级为 L0～L3）：复用
    /// <c>core/carriers/assembly</c> 的 <see cref="CarriersSchemaCatalog"/> 一次性注册 L0～L3 全部
    /// <c>TableSchema</c>/<see cref="IValidationRule"/>（先 L0～L2 经
    /// <see cref="RulesSchemaCatalog"/>，再补 L3 item/creature/gobj——不含 L4，见
    /// <see cref="CarriersSchemaCatalog"/> 顶部"分层判断记录"），对 <c>--data-root</c> 指向的磁盘
    /// 目录跑一遍 <see cref="DataRegistry.LoadAll"/>，逐条打印校验问题。
    /// <para>
    /// 判断记录：本类是唯一的真实校验逻辑实现——<c>toolchain/validate_data.py</c> 只做骨架级
    /// 信封/表名/id 格式检查（阶段 0），不得与本类重复实现任何字段级/引用完整性/Expr 规则（落地
    /// 计划 T2-12 明文禁止"与 core 内校验逻辑重复实现两套判断"）；<c>validate_data.py</c> 在骨架
    /// 检查通过后，用子进程调用本工具完成第二道真实校验。
    /// </para>
    /// <para>
    /// 判断记录：本工具依赖 <c>adapters/stub</c> 提供的 <c>StubFileSystem</c> 只有内存实现，无法
    /// 读取真实磁盘文件，因此本工具自带 <see cref="DiskFileSystem"/>（只读，见其文件头判断记录），
    /// 不修改 <c>core/</c> 下任何已有类型。
    /// </para>
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // 校验问题消息里的中文文本（来自 core 内各校验规则）默认按控制台代码页输出会乱码——
            // Windows 控制台默认代码页通常不是 UTF-8。显式设为 UTF-8（无 BOM）保证标准输出/标准
            // 错误可读、可被下游（如 toolchain/validate_data.py 的 subprocess 管道）正确解码。
            try
            {
                Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }
            catch (IOException)
            {
                // 标准输出被重定向到某些不支持设置编码的目标时可能抛出；不影响功能，只是退化为
                // 使用调用方已设置的编码，不视为致命错误。
            }

            string? dataRootArg = null;
            var strict = false;
            var jsonOutput = false;
            var listTables = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--data-root":
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("参数错误：--data-root 需要一个目录参数");
                            return 2;
                        }
                        dataRootArg = args[++i];
                        break;

                    case "--strict":
                        strict = true;
                        break;

                    case "--json":
                        jsonOutput = true;
                        break;

                    case "--list-tables":
                        listTables = true;
                        break;

                    default:
                        Console.Error.WriteLine($"参数错误：未知参数 \"{args[i]}\"");
                        return 2;
                }
            }

            if (dataRootArg == null)
            {
                Console.Error.WriteLine(
                    "参数错误：缺少必填参数 --data-root <dir>\n" +
                    "用法：dotnet run --project toolchain/validator -- --data-root <dir> [--strict] [--json] [--list-tables]");
                return 2;
            }

            // 相对路径相对当前工作目录解析——调用方（toolchain/validate_data.py 或用户）需要在
            // 仓库根目录下运行本工具，此时"相对路径"与"相对仓库根"是同一件事，惯例同
            // toolchain/validate_data.py"从仓库根目录运行"。绝对路径原样使用。
            var dataRoot = Path.IsPathRooted(dataRootArg)
                ? dataRootArg
                : Path.Combine(Directory.GetCurrentDirectory(), dataRootArg);
            dataRoot = Path.GetFullPath(dataRoot).Replace('\\', '/');

            if (!Directory.Exists(dataRoot))
            {
                Console.Error.WriteLine($"参数错误：目录不存在：{dataRoot}");
                return 2;
            }

            var fs = new DiskFileSystem();
            var source = new FileSystemDataSource(fs, dataRoot);

            // 非严格 EventBus：本工具是一次性命令行进程，不关心 data.load_completed/
            // data.validation_failed 之外的任何事件登记，未登记的事件 key 只记警告、不抛异常
            // （见 EventBusOptions.StrictCatalog 文档）。仍然登记这两个 key 本身，避免产生多余的
            // 诊断噪音。
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
            });
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false });

            DataLoadCompletedEvent? loadCompleted = null;
            bus.Subscribe<DataLoadCompletedEvent>(DataRegistryEventKeys.LoadCompleted, e => loadCompleted = e);

            var options = RulesSchemaCatalog.CreateOptions();
            options.FailOnUnknownTable = true;
            options.Strictness = strict ? DataRegistryStrictness.WarningsBlock : DataRegistryStrictness.WarningsAllowed;

            var registry = new DataRegistry(source, bus, options);
            CarriersSchemaCatalog.RegisterAll(registry);

            var report = registry.LoadAll();
            var tableCount = registry.Tables.Count;
            var recordCount = loadCompleted?.RecordCount ?? 0;

            if (jsonOutput)
            {
                PrintJson(report, tableCount, recordCount, listTables ? registry : null);
            }
            else
            {
                if (listTables)
                {
                    foreach (var table in registry.Tables.OrderBy(t => t, StringComparer.Ordinal))
                    {
                        var count = report.IsBlocking ? -1 : registry.GetAll(table).Count;
                        Console.WriteLine(count < 0 ? $"table: {table} (? 条记录，数据未通过校验)" : $"table: {table} ({count} 条记录)");
                    }
                }

                foreach (var issue in report.Issues)
                {
                    Console.WriteLine(FormatIssue(issue));
                }

                Console.WriteLine($"tables {tableCount}, records {recordCount}, errors {report.ErrorCount}, warnings {report.WarningCount}");
            }

            return report.IsBlocking ? 1 : 0;
        }

        private static string FormatIssue(ValidationIssue issue)
        {
            var severity = issue.Severity == ValidationSeverity.Error ? "error" : "warning";
            string loc;
            if (issue.RecordKey == null)
            {
                loc = issue.Table;
            }
            else if (issue.Field == null)
            {
                loc = $"{issue.Table}/{issue.RecordKey}";
            }
            else
            {
                loc = $"{issue.Table}/{issue.RecordKey}/{issue.Field}";
            }

            return $"[{severity}] {loc}: {issue.Check}: {issue.Message}";
        }

        private static void PrintJson(ValidationReport report, int tableCount, int recordCount, IDataRegistryView? tablesForListing)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"tables\":").Append(tableCount).Append(',');
            sb.Append("\"records\":").Append(recordCount).Append(',');
            sb.Append("\"errors\":").Append(report.ErrorCount).Append(',');
            sb.Append("\"warnings\":").Append(report.WarningCount).Append(',');
            sb.Append("\"blocking\":").Append(report.IsBlocking ? "true" : "false").Append(',');

            if (tablesForListing != null)
            {
                sb.Append("\"tables_list\":[");
                var names = new List<string>(tablesForListing.Tables);
                names.Sort(StringComparer.Ordinal);
                for (var i = 0; i < names.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var count = report.IsBlocking ? -1 : tablesForListing.GetAll(names[i]).Count;
                    sb.Append('{').Append("\"name\":\"").Append(JsonEscape(names[i])).Append("\",\"record_count\":").Append(count).Append('}');
                }
                sb.Append("],");
            }

            sb.Append("\"issues\":[");
            for (var i = 0; i < report.Issues.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendIssueJson(sb, report.Issues[i]);
            }
            sb.Append(']');
            sb.Append('}');
            Console.WriteLine(sb.ToString());
        }

        private static void AppendIssueJson(StringBuilder sb, ValidationIssue issue)
        {
            sb.Append('{');
            sb.Append("\"severity\":\"").Append(issue.Severity == ValidationSeverity.Error ? "error" : "warning").Append("\",");
            sb.Append("\"table\":\"").Append(JsonEscape(issue.Table)).Append("\",");
            sb.Append("\"record_key\":").Append(issue.RecordKey == null ? "null" : "\"" + JsonEscape(issue.RecordKey) + "\"").Append(',');
            sb.Append("\"field\":").Append(issue.Field == null ? "null" : "\"" + JsonEscape(issue.Field) + "\"").Append(',');
            sb.Append("\"check\":\"").Append(JsonEscape(issue.Check)).Append("\",");
            sb.Append("\"message\":\"").Append(JsonEscape(issue.Message)).Append('"');
            sb.Append('}');
        }

        private static string JsonEscape(string value)
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
