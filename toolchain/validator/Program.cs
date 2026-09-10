using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Core.Foundation.DataRegistry;
using Presentation.Assembly;

namespace Toolchain.Validator
{
    /// <summary>
    /// 数据校验器阶段 2 的真实校验入口（落地方案 T2-12，阶段 3 集成收尾"事项四"升级到 L0～L4，
    /// 阶段 4 收敛 B 再升级到 L0～L5）：复用 <c>presentation/assembly</c> 的
    /// <see cref="PresentationSchemaCatalog"/> 一次性注册 L0～L5 全部
    /// <c>TableSchema</c>/<see cref="IValidationRule"/>（先 L0～L4 经
    /// <see cref="Core.Gameplay.Assembly.GameplaySchemaCatalog"/>——该方法内部再先经
    /// <see cref="Core.Carriers.Assembly.CarriersSchemaCatalog"/> 登记 L0～L3——，再补 L5 表现层
    /// 全部表：<c>vfx.def</c>/<c>sfx.def</c>/<c>display.weapon_style</c>/<c>feedback.binding</c>/
    /// <c>feedback.floating_text_style</c>/<c>camera_profile</c>/<c>ui_layout_definition</c>/
    /// <c>shell_menu_definition</c>/<c>display.map</c>/<c>display.anim_set</c>/
    /// <c>display.equip_visual</c>/<c>l10n.locale</c>/<c>l10n.text</c>，见
    /// <see cref="PresentationSchemaCatalog"/> 类型注释判断记录），对 <c>--data-root</c> 指向的磁盘
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
    /// <para>
    /// 判断记录（ADR-0018 决策 3，校验装配入口）：本类不再自行内联"建 EventBus + 建 DataRegistry +
    /// <see cref="PresentationSchemaCatalog.RegisterAll(IDataRegistry, Core.Foundation.Common.Id?, Core.Carriers.Creature.ICreatureTemplateQuery?)"/>
    /// + <c>LoadAll</c> + 汇总"这一整段装配逻辑——改为调用 <see cref="ContentValidationAssembly.Run"/>
    /// （核心库内单一公开入口），保证本工具与编辑器基础套件（ADR-0018 决策 1/2 的独立消费方项目）
    /// 使用同一份注册顺序与选项默认值，即"编辑器里看到的红线 = 门禁会报的错"。
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

            // 判断记录（数据目录框架/游戏分层任务，多根加载）：--data-root 由"最多一个"改为
            // "可重复传入"，收集到 dataRootArgs 列表；每个根各自构造一个 FileSystemDataSource，
            // 一起传给 DataRegistry.LoadAll(IReadOnlyList<IDataSource>)（见该方法类型级判断记录
            // "合并规则"）——同名表跨根合并、主键冲突/schema_version 不一致跨根阻断，均由该方法
            // 实现，本文件不重复实现任何判断逻辑（见类型头判断记录"唯一实现"）。只传一个
            // --data-root 时行为与改动前完全一致（单元素列表）。
            var dataRootArgs = new List<string>();
            var strict = false;
            var jsonOutput = false;
            var listTables = false;
            var schemaAudit = false;
            string? allowlistPath = null;
            IReadOnlyList<(string table, string idField)>? displayMapSources = null;

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
                        dataRootArgs.Add(args[++i]);
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

                    // 判断记录（F3 元数据门禁）：--schema-audit 是一种完全不同的运行模式——不需要
                    // --data-root（不加载任何实际数据，只审计代码里已登记的 TableSchema 结构本身，
                    // 见 Presentation.Assembly.SchemaAudit 类型注释），因此下面 dataRootArgs.Count==0
                    // 的必填校验对这一模式不生效，见本方法后续分支判断。
                    case "--schema-audit":
                        schemaAudit = true;
                        break;

                    case "--allowlist":
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("参数错误：--allowlist 需要一个文件路径参数");
                            return 2;
                        }
                        allowlistPath = args[++i];
                        break;

                    case "--display-map-sources":
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("参数错误：--display-map-sources 需要一个参数，形如 \"table1:idField1,table2:idField2\"");
                            return 2;
                        }
                        if (!TryParseDisplayMapSources(args[++i], out displayMapSources, out var parseError))
                        {
                            Console.Error.WriteLine($"参数错误：--display-map-sources 格式非法：{parseError}");
                            return 2;
                        }
                        break;

                    default:
                        Console.Error.WriteLine($"参数错误：未知参数 \"{args[i]}\"");
                        return 2;
                }
            }

            if (schemaAudit)
            {
                return RunSchemaAudit(allowlistPath, jsonOutput);
            }

            if (dataRootArgs.Count == 0)
            {
                Console.Error.WriteLine(
                    "参数错误：缺少必填参数 --data-root <dir>（可重复传入以合并多个数据根）\n" +
                    "用法：dotnet run --project toolchain/validator -- --data-root <dir> [--data-root <dir2> ...] [--strict] [--json] [--list-tables] [--display-map-sources <table:idField,...>]\n" +
                    "或元数据门禁：dotnet run --project toolchain/validator -- --schema-audit [--allowlist <path>] [--json]");
                return 2;
            }

            // 相对路径相对当前工作目录解析——调用方（toolchain/validate_data.py 或用户）需要在
            // 仓库根目录下运行本工具，此时"相对路径"与"相对仓库根"是同一件事，惯例同
            // toolchain/validate_data.py"从仓库根目录运行"。绝对路径原样使用。
            var sources = new List<IDataSource>(dataRootArgs.Count);
            var fs = new DiskFileSystem();
            foreach (var dataRootArg in dataRootArgs)
            {
                var dataRoot = Path.IsPathRooted(dataRootArg)
                    ? dataRootArg
                    : Path.Combine(Directory.GetCurrentDirectory(), dataRootArg);
                dataRoot = Path.GetFullPath(dataRoot).Replace('\\', '/');

                if (!Directory.Exists(dataRoot))
                {
                    Console.Error.WriteLine($"参数错误：目录不存在：{dataRoot}");
                    return 2;
                }

                sources.Add(new FileSystemDataSource(fs, dataRoot));
            }

            // ADR-0018 决策 3（校验装配入口）：本工具不再自行内联"建 EventBus + 建 DataRegistry +
            // PresentationSchemaCatalog.RegisterAll + LoadAll + 汇总"这一整段装配逻辑——改为调用
            // Presentation.Assembly.ContentValidationAssembly.Run（核心库内的单一公开入口，供本工具
            // 与编辑器基础套件共用同一份装配代码，见该类型注释判断记录）。--display-map-sources 是
            // 本工具唯一新增的可选规则接线参数（DisplayMapCoverageRule）；SpawnSummonOnlyCreatureRule
            // 仍不接线——本工具运行时机（一次性命令行进程）没有真正的 ICreatureTemplateQuery 实现
            // 可用（同 ContentValidationAssembly.CreateRegistry 判断记录），与改动前行为一致。
            var validationOptions = new ContentValidationOptions
            {
                FailOnUnknownTable = true,
                Strictness = strict ? DataRegistryStrictness.WarningsBlock : DataRegistryStrictness.WarningsAllowed,
                DisplayMapCoverageSources = displayMapSources,
            };

            var run = ContentValidationAssembly.Run(sources, validationOptions);
            var report = run.Report;
            var tableCount = run.TableCount;
            var recordCount = run.RecordCount;

            // 判断记录（数据行覆盖语义任务）：覆盖诊断（见 DataRegistry 类型级判断记录"覆盖语义"、
            // OverrideDiagnostic）不是 ValidationIssue（既非 Warning 也非 Error），report.Issues 里
            // 看不到；单独从 run.Overrides 取出打印，供人工核对"这次加载真的按预期覆盖了哪些行"
            // （如 arch.power.health 是否确实被 _sample 覆盖）。
            var overrides = run.Overrides;

            if (jsonOutput)
            {
                PrintJson(report, tableCount, recordCount, listTables ? run.Registry : null, overrides, run.DisabledOptionalRules, run.EnabledOptionalRules);
            }
            else
            {
                if (listTables)
                {
                    foreach (var table in run.Registry.Tables.OrderBy(t => t, StringComparer.Ordinal))
                    {
                        var count = report.IsBlocking ? -1 : run.Registry.GetAll(table).Count;
                        Console.WriteLine(count < 0 ? $"table: {table} (? 条记录，数据未通过校验)" : $"table: {table} ({count} 条记录)");

                        // ADR-0021 决策 4："导出给内容工具"：把该表已登记的字段范围约束一并列出
                        // （供人工核对/编辑器接入前的手工检查），见 SchemaFieldRangeExport 判断记录。
                        var schema = run.Registry.GetSchema(table);
                        if (schema != null)
                        {
                            foreach (var r in SchemaFieldRangeExport.Collect(schema))
                            {
                                Console.WriteLine($"  range: {r.FieldPath}: {r.Kind} {r.Range.Describe()}");
                            }
                        }
                    }
                }

                foreach (var issue in report.Issues)
                {
                    Console.WriteLine(FormatIssue(issue));
                }

                if (overrides.Count > 0)
                {
                    Console.WriteLine($"覆盖清单（{overrides.Count} 条，见 data/README.md\"多根加载与合并规则\"）：");
                    foreach (var diag in overrides.OrderBy(d => d.Table, StringComparer.Ordinal).ThenBy(d => d.RecordKey, StringComparer.Ordinal))
                    {
                        Console.WriteLine($"  [override] {diag.Table}[{diag.RecordKey}]: \"{diag.OverridingLocation}\" 覆盖 \"{diag.OverriddenLocation}\"");
                    }
                }

                Console.WriteLine($"tables {tableCount}, records {recordCount}, errors {report.ErrorCount}, warnings {report.WarningCount}, overrides {overrides.Count}");

                // ADR-0018 决策 3 新增：把 ContentValidationAssembly.Run 如实汇报的"本次未启用的
                // 可选规则清单"追加为最后一行，不静默跳过（见 ContentValidationOptions 两个可选
                // 接线参数的判断记录）；均已启用时打印 "none"。追加在既有末尾汇总行之后，不改动
                // 既有任何一行的内容，保持此前行为逐字节兼容。
                var disabledSummary = run.DisabledOptionalRules.Count == 0
                    ? "none"
                    : string.Join(", ", run.DisabledOptionalRules);
                Console.WriteLine($"optional rules disabled: {disabledSummary}");
            }

            return report.IsBlocking ? 1 : 0;
        }

        /// <summary>
        /// F3 元数据门禁（ADR-0018 决策 3、ADR-0019 决策 4）：--schema-audit 模式的完整流程——
        /// 用 <see cref="SchemaAudit.EnumerateRegisteredSchemas"/> 建一个只登记 schema、不加载任何
        /// 数据的 registry（复用 <see cref="ContentValidationAssembly"/> 同一份装配顺序），读出全部
        /// <see cref="TableSchema"/>，交给 <see cref="SchemaAudit.Run"/> 审计。<paramref name="allowlistPath"/>
        /// 省略时使用空白名单（不豁免任何 composite_without_substructure）。
        /// </summary>
        private static int RunSchemaAudit(string? allowlistPath, bool jsonOutput)
        {
            SchemaAuditAllowlist allowlist;
            if (allowlistPath == null)
            {
                allowlist = SchemaAuditAllowlist.Empty;
            }
            else
            {
                string allowlistText;
                try
                {
                    allowlistText = File.ReadAllText(allowlistPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"参数错误：读取白名单文件失败：{allowlistPath}：{ex.Message}");
                    return 2;
                }

                try
                {
                    allowlist = SchemaAuditAllowlist.Parse(allowlistText);
                }
                catch (FormatException ex)
                {
                    Console.Error.WriteLine($"参数错误：白名单文件格式非法：{allowlistPath}：{ex.Message}");
                    return 2;
                }
            }

            var schemas = SchemaAudit.EnumerateRegisteredSchemas();
            var report = SchemaAudit.Run(schemas, allowlist);

            if (jsonOutput)
            {
                PrintSchemaAuditJson(report);
            }
            else
            {
                foreach (var issue in report.Issues)
                {
                    var loc = issue.FieldPath.Length == 0 ? issue.Table : $"{issue.Table}/{issue.FieldPath}";
                    Console.WriteLine($"[{issue.Severity}] {loc}: {issue.Check}: {issue.Message}");
                }
                Console.WriteLine($"tables {report.TableCount}, fields {report.FieldCount}, errors {report.ErrorCount}, warnings {report.WarningCount}");
            }

            return report.IsBlocking ? 1 : 0;
        }

        private static void PrintSchemaAuditJson(SchemaAuditReport report)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"tables\":").Append(report.TableCount).Append(',');
            sb.Append("\"fields\":").Append(report.FieldCount).Append(',');
            sb.Append("\"errors\":").Append(report.ErrorCount).Append(',');
            sb.Append("\"warnings\":").Append(report.WarningCount).Append(',');
            sb.Append("\"blocking\":").Append(report.IsBlocking ? "true" : "false").Append(',');
            sb.Append("\"issues\":[");
            for (var i = 0; i < report.Issues.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var issue = report.Issues[i];
                sb.Append('{');
                sb.Append("\"severity\":\"").Append(JsonEscape(issue.Severity)).Append("\",");
                sb.Append("\"table\":\"").Append(JsonEscape(issue.Table)).Append("\",");
                sb.Append("\"field_path\":\"").Append(JsonEscape(issue.FieldPath)).Append("\",");
                sb.Append("\"check\":\"").Append(JsonEscape(issue.Check)).Append("\",");
                sb.Append("\"message\":\"").Append(JsonEscape(issue.Message)).Append('"');
                sb.Append('}');
            }
            sb.Append(']');
            sb.Append('}');
            Console.WriteLine(sb.ToString());
        }

        /// <summary>解析 <c>--display-map-sources</c> 的值：形如 <c>"table1:idField1,table2:idField2"</c>
        /// （逗号分隔多组，每组用一个冒号分隔表名与字段名，两侧均不能为空）。</summary>
        private static bool TryParseDisplayMapSources(
            string raw, out IReadOnlyList<(string table, string idField)>? sources, out string error)
        {
            var result = new List<(string table, string idField)>();
            var entries = raw.Split(',');
            foreach (var entry in entries)
            {
                var trimmedEntry = entry.Trim();
                if (trimmedEntry.Length == 0)
                {
                    continue;
                }

                var parts = trimmedEntry.Split(':');
                if (parts.Length != 2 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0)
                {
                    sources = null;
                    error = $"条目 \"{trimmedEntry}\" 不是 \"table:idField\" 形式";
                    return false;
                }

                result.Add((parts[0].Trim(), parts[1].Trim()));
            }

            if (result.Count == 0)
            {
                sources = null;
                error = "至少需要一组 \"table:idField\"";
                return false;
            }

            sources = result;
            error = "";
            return true;
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

        private static void PrintJson(
            ValidationReport report, int tableCount, int recordCount, IDataRegistryView? tablesForListing,
            IReadOnlyList<OverrideDiagnostic> overrides,
            IReadOnlyList<string> disabledOptionalRules, IReadOnlyList<string> enabledOptionalRules)
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
                    sb.Append('{').Append("\"name\":\"").Append(JsonEscape(names[i])).Append("\",\"record_count\":").Append(count).Append(',');

                    // ADR-0021 决策 4："导出给内容工具"：随每张表一并输出已登记的字段范围约束
                    // （SchemaFieldRangeExport 判断记录），编辑器可据此在内容作者输入时就地校验，
                    // 不必等到一次完整的 DataRegistry.LoadAll。
                    sb.Append("\"field_ranges\":[");
                    var schema = tablesForListing.GetSchema(names[i]);
                    if (schema != null)
                    {
                        var ranges = SchemaFieldRangeExport.Collect(schema);
                        for (var r = 0; r < ranges.Count; r++)
                        {
                            if (r > 0) sb.Append(',');
                            AppendFieldRangeJson(sb, ranges[r]);
                        }
                    }
                    sb.Append(']');

                    sb.Append('}');
                }
                sb.Append("],");
            }

            sb.Append("\"issues\":[");
            for (var i = 0; i < report.Issues.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendIssueJson(sb, report.Issues[i]);
            }
            sb.Append("],");

            sb.Append("\"overrides\":[");
            for (var i = 0; i < overrides.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendOverrideJson(sb, overrides[i]);
            }
            sb.Append("],");

            // ADR-0018 决策 3 新增：如实汇报 ContentValidationAssembly.Run 返回的可选规则接线状态
            // （不静默跳过，见 ContentValidationOptions 两个可选接线参数的判断记录）。追加在既有
            // "overrides" 字段之后、闭合大括号之前，不改动任何既有字段，保持此前 --json 输出逐字节
            // 兼容。
            sb.Append("\"disabled_optional_rules\":[");
            for (var i = 0; i < disabledOptionalRules.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(JsonEscape(disabledOptionalRules[i])).Append('"');
            }
            sb.Append("],");

            sb.Append("\"enabled_optional_rules\":[");
            for (var i = 0; i < enabledOptionalRules.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(JsonEscape(enabledOptionalRules[i])).Append('"');
            }
            sb.Append(']');

            sb.Append('}');
            Console.WriteLine(sb.ToString());
        }

        private static void AppendOverrideJson(StringBuilder sb, OverrideDiagnostic diag)
        {
            sb.Append('{');
            sb.Append("\"table\":\"").Append(JsonEscape(diag.Table)).Append("\",");
            sb.Append("\"record_key\":\"").Append(JsonEscape(diag.RecordKey)).Append("\",");
            sb.Append("\"overriding_location\":\"").Append(JsonEscape(diag.OverridingLocation)).Append("\",");
            sb.Append("\"overridden_location\":\"").Append(JsonEscape(diag.OverriddenLocation)).Append('"');
            sb.Append('}');
        }

        private static void AppendFieldRangeJson(StringBuilder sb, FieldRangeInfo info)
        {
            var range = info.Range;
            sb.Append('{');
            sb.Append("\"field_path\":\"").Append(JsonEscape(info.FieldPath)).Append("\",");
            sb.Append("\"kind\":\"").Append(JsonEscape(info.Kind)).Append("\",");
            sb.Append("\"min\":").Append(range.Min.HasValue ? range.Min.Value.ToString(CultureInfo.InvariantCulture) : "null").Append(',');
            sb.Append("\"min_exclusive\":").Append(range.MinExclusive ? "true" : "false").Append(',');
            sb.Append("\"max\":").Append(range.Max.HasValue ? range.Max.Value.ToString(CultureInfo.InvariantCulture) : "null").Append(',');
            sb.Append("\"max_exclusive\":").Append(range.MaxExclusive ? "true" : "false").Append(',');
            sb.Append("\"describe\":\"").Append(JsonEscape(range.Describe())).Append('"');
            sb.Append('}');
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
