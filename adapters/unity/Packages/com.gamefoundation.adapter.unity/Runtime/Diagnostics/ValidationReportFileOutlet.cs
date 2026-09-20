#nullable enable
// ValidationReportFileOutlet：运行期校验报告跨进程落盘出口（ADR-0047，续 ADR-0046 决策 6 明确留白的
// "跨进程消费诉求"）。
//
// 背景：ADR-0046 已让"与运行期宿主同进程内、订阅 data_registry 事件总线"的消费方拿到逐条结构化
// ValidationIssue（DataValidationFailedEvent.Issues），但独立进程（如随游戏走的编辑器）拿不到进程内
// 事件——ADR-0046 决策 6 明确标注这是已知局限、留白等真实需求。本类型是该留白的落地：把每次校验的
// 完整报告（不论通过与否）原子落盘为 JSON，供外部进程按文件监视/轮询读取，不逼消费方解析任何日志
// 文本（ADR-0046 决策 1"日志文本永远不是契约"同样约束本次落地）。
//
// 判断记录（为什么是落盘文件，不是固定前缀结构化日志行）：ADR-0046 决策 6"备选方案"把两者并列为
// 候选，本次设计层否决日志行方案——固定前缀的结构化文本行本质仍是"文本"，消费方要按行扫描宿主日志
// 流、用约定前缀切出 JSON 片段再解析，这正是 ADR-0046 决策 1 要求下游避免的"解析呈现层文本"，只是
// 把"解析一整行人类可读文本"换成了"解析一行加了前缀的文本"，脆弱性的根源（宿主日志混排其它输出、
// 行内换行/编码问题、多进程/多宿主日志交织难以定位"这一行属于哪一次校验"）没有解决。落盘文件是一个
// 独立于宿主日志流的通道，消费方直接用文件系统 API 读取一个内容自洽的 JSON 文档，不需要做任何行级
// 文本切分。
//
// 判断记录（为什么路径由选项显式指定、不设默认路径）：默认路径等于制造一个"即使没人要这份文件，
// 进程也会往磁盘写东西"的隐藏副作用——运行期宿主可能被非开发场景启动（正式发布的玩家进程），此时
// 既不应该产生这份文件，也不应该让"要不要产生"这件事取决于某个约定俗成但未显式声明的路径是否恰好
// 可写。未显式指定 <see cref="ResolvePath"/> 返回 null 时，全部调用点（见
// games/_template/Runtime/GameBootstrap.cs、DataHotReload.cs、
// Adapter.Unity.Bootstrap.GameFoundationBootstrap、Adapter.Unity.Shell.FrameworkResidentHost）
// 都直接跳过写文件，不产生任何磁盘 I/O，与本次改动前的行为完全一致。
//
// 判断记录（为什么校验通过时也要写）：若只在阻断态写文件，消费方拿到一份文件后无法区分"最近一次
// 校验其实已经通过、只是文件还停留在上一次失败的旧内容"与"本次校验尚未跑完、文件还没更新"——两者
// 对消费方（如编辑器要不要继续显示一个过期的错误提示）的正确反应完全相反，却无法从文件本身区分。
// 每次校验（不论 issues 是否为空）都写一次，配合下面的单调递增序号，消费方能明确判断"这是不是最新
// 一次结果"与"最新结果是不是空（通过）"。
//
// 判断记录（选项命名与既有覆盖类选项同惯例）：命令行参数 + 环境变量两种入口、命令行优先，惯例同
// games/_template/Runtime/ContentSourceRootOverride.cs（消费方反馈第 70 条）与
// Adapter.Unity.Shell.SmokeRunner 的 "-gf*" 系列命令行参数——都是"这一次进程/这台开发机"级别的
// 开发期/工具期覆盖，不适合放进随游戏序列化分发的 Inspector 配置（同 ContentSourceRootOverride
// 判断记录 1 的理由）。四个已知校验点（games/_template 与 adapters/unity 各两处）共用同一个选项名，
// 不是各自独立的四个选项——外部工具（如编辑器）启动游戏进程时只需要设置一次，不需要关心这一次进程
// 内部实际由哪个组合根完成装配。
//
// 判断记录（原子写）：先写一个同目录下的临时文件，再用 File.Move 改名——同一文件系统内的重命名是
// 原子操作，监视/轮询该路径的外部进程要么看到改名前的旧文件（不存在时看到"文件不存在"），要么看到
// 改名后已经完整写入的新文件，不存在"读到写了一半的半截 JSON"这种中间态。若目标路径已存在旧文件，
// 先删除再改名（两步之间有极短的"文件不存在"窗口，但绝不会出现"文件存在但内容不完整"这种更危险的
// 中间态——消费方按"文件不存在就跳过/重试一次"处理即可，比解析半截 JSON 简单得多）。
//
// ADR-0055 跟进（2026-09-21，消费方反馈第 78 条）：信封顶层新增可选字段 table——此前顶层只有
// sequence/source/timestamp_utc/issues 四个字段，"这次校验确切针对哪张表"完全没有结构化通道，独立
// 进程消费方（如随游戏走的编辑器）唯一能拿到这个信息的办法是解析 Debug.Log 文本，与本文件判断记录
// "日志文本永远不是契约"自相矛盾。DataHotReload.ReloadTable(string table) 早就持有确切的表名，只是
// 从未传给 WriteIfConfigured；本次改为新增一组带 table 形参的重载（ABI 只新增，既有重载保留并委托，
// table 缺省值 null，行为与改动前逐位一致），HotReload 来源传入被重载的表名，三处 Startup 来源
// 调用点原样不动（继续走不带 table 的既有重载，等价于显式传 null）——启动全量校验本就没有单一确定
// 的表，不编造空字符串/"all" 之类会被误读成真实表名的占位值，详见 BuildJson 判断记录与
// architecture/adr/0055-运行期校验报告落盘出口补表名字段.md。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Core.Foundation.DataRegistry;

namespace Adapter.Unity.Diagnostics
{
    /// <summary>触发本次落盘的来源标识（ADR-0047）：固定取值集合，不接受任意字符串，避免不同调用点
    /// 各自写出拼写不一致的自由文本，让消费方无法可靠按来源过滤/展示。新增触发点时在此追加新常量
    /// （ABI 只新增）。</summary>
    public static class ValidationReportTriggerSource
    {
        /// <summary>宿主启动阶段的首次数据加载校验（<c>LoadAll</c>）。</summary>
        public const string Startup = "startup";

        /// <summary>开发期数据热重载触发的重新校验（<c>DataRegistry.Reload</c>）。</summary>
        public const string HotReload = "hot_reload";
    }

    /// <summary>
    /// 运行期宿主把 <see cref="ValidationReport"/>/<see cref="ValidationIssue"/> 落盘为 JSON 的共享
    /// 出口（ADR-0047）。不引用任何 UnityEngine API（同目录 <c>DiagnosticsHub.cs</c> 同款判断记录），
    /// 供 <c>adapters/unity/DiagnosticsForwarding/Adapters.Unity.DiagnosticsForwarding.csproj</c>
    /// 按引用纳入 <c>dotnet test</c> 编译，不需要起 Unity 即可验证。
    /// </summary>
    public static class ValidationReportFileOutlet
    {
        /// <summary>命令行参数名（后跟一个值，即落盘文件的绝对/相对路径），惯例同
        /// <c>Game.Template.ContentSourceRootOverride</c>——本文件不引用 Game.Template（避免反向
        /// 依赖，Adapter.Unity 不引用 Game.Template 是既有依赖方向），常量独立声明但取名/语义同
        /// 惯例。</summary>
        public const string CommandLineFlag = "-gfValidationReportPath";

        /// <summary>环境变量名兜底，命令行参数未指定时读取。</summary>
        public const string EnvironmentVariable = "GF_VALIDATION_REPORT_PATH";

        private static readonly object SequenceLock = new object();

        /// <summary>按落盘文件的规范化完整路径分别计数（同一路径下第二次及以后写入的序号在第一次
        /// 基础上递增），不是全进程共享单一计数器——避免未来若真的出现"同一进程给两个不同路径分别落盘"
        /// 的场景时，彼此的序号互相干扰。</summary>
        private static readonly Dictionary<string, long> SequenceByPath = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        /// <summary>解析可选的落盘路径（未做任何存在性校验——这是一个输出路径，父目录不存在时
        /// <see cref="Write"/> 会自动创建，见该方法判断记录）：命令行参数优先于环境变量；两者都未
        /// 指定（或环境变量为空字符串）时返回 <c>null</c>，即"不落盘"（见类型头判断记录"为什么路径
        /// 由选项显式指定、不设默认路径"）。</summary>
        public static string? ResolvePath(IReadOnlyList<string> commandLineArgs, Func<string, string?> getEnvironmentVariable)
        {
            if (commandLineArgs == null) throw new ArgumentNullException(nameof(commandLineArgs));
            if (getEnvironmentVariable == null) throw new ArgumentNullException(nameof(getEnvironmentVariable));

            for (var i = 0; i < commandLineArgs.Count - 1; i++)
            {
                if (string.Equals(commandLineArgs[i], CommandLineFlag, StringComparison.Ordinal))
                {
                    return commandLineArgs[i + 1];
                }
            }

            var env = getEnvironmentVariable(EnvironmentVariable);
            return string.IsNullOrEmpty(env) ? null : env;
        }

        /// <summary>既有三参数重载：不携带表名（<c>table</c> 视为 <c>null</c>），保持既有调用点
        /// 行为不变（ABI 只新增，见下方 ADR-0055 新增的带 <c>table</c> 重载）。</summary>
        public static void WriteIfConfigured(
            string? filePath,
            string source,
            IReadOnlyList<ValidationIssue> issues,
            Func<DateTime>? utcNowProvider = null) =>
            WriteIfConfigured(filePath, source, issues, table: null, utcNowProvider);

        /// <summary>ADR-0055 新增的带 <paramref name="table"/> 重载：各校验点的实际调用入口。
        /// <paramref name="filePath"/> 为 <c>null</c>/空字符串时（即 <see cref="ResolvePath"/> 未
        /// 解析出选项）直接返回，不产生任何磁盘 I/O（见类型头判断记录"为什么路径由选项显式指定、
        /// 不设默认路径"）；否则委托 <see cref="Write(string, string, IReadOnlyList{ValidationIssue}, string?, Func{DateTime}?)"/>。
        /// <paramref name="table"/> 语义见该重载判断记录。</summary>
        public static void WriteIfConfigured(
            string? filePath,
            string source,
            IReadOnlyList<ValidationIssue> issues,
            string? table,
            Func<DateTime>? utcNowProvider = null)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            Write(filePath!, source, issues, table, utcNowProvider);
        }

        /// <summary>既有四参数重载：不携带表名（<c>table</c> 视为 <c>null</c>），保持既有调用点
        /// 行为不变（ABI 只新增，见下方 ADR-0055 新增的带 <c>table</c> 重载）。</summary>
        public static long Write(
            string filePath,
            string source,
            IReadOnlyList<ValidationIssue> issues,
            Func<DateTime>? utcNowProvider = null) =>
            Write(filePath, source, issues, table: null, utcNowProvider);

        /// <summary>ADR-0055 新增的带 <paramref name="table"/> 重载：无条件写一次（不检查
        /// <paramref name="filePath"/> 是否为空——调用方明确要写时用这个重载，如测试；正常宿主调用点
        /// 一律走 <see cref="WriteIfConfigured(string?, string, IReadOnlyList{ValidationIssue}, string?, Func{DateTime}?)"/>）。
        /// <paramref name="table"/>：这次校验确切针对哪一张表——热重载场景（<see
        /// cref="ValidationReportTriggerSource.HotReload"/>）传入被重载的表名；启动全量校验场景
        /// （<see cref="ValidationReportTriggerSource.Startup"/>）本就没有单一确定的表，传
        /// <c>null</c>（不传空字符串或 <c>"all"</c> 之类会被误读成真实表名的占位值，见
        /// <see cref="BuildJson(long, string, DateTime, IReadOnlyList{ValidationIssue}, string?)"/>
        /// 判断记录与 ADR-0055 决策）。返回本次写入使用的单调递增序号，供测试断言。</summary>
        public static long Write(
            string filePath,
            string source,
            IReadOnlyList<ValidationIssue> issues,
            string? table,
            Func<DateTime>? utcNowProvider = null)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("filePath 不能为空", nameof(filePath));
            if (string.IsNullOrEmpty(source)) throw new ArgumentException("source 不能为空", nameof(source));
            if (issues == null) throw new ArgumentNullException(nameof(issues));

            var fullPath = Path.GetFullPath(filePath);
            long sequence;
            lock (SequenceLock)
            {
                SequenceByPath.TryGetValue(fullPath, out var previous);
                sequence = previous + 1;
                SequenceByPath[fullPath] = sequence;
            }

            var timestampUtc = utcNowProvider != null ? utcNowProvider() : DateTime.UtcNow;
            var json = BuildJson(sequence, source, timestampUtc, issues, table);
            AtomicWriteAllText(fullPath, json);
            return sequence;
        }

        /// <summary>既有四参数重载：不携带表名（<c>table</c> 视为 <c>null</c>），保持既有调用点
        /// 行为不变（ABI 只新增，见下方 ADR-0055 新增的带 <c>table</c> 重载）。</summary>
        public static string BuildJson(long sequence, string source, DateTime timestampUtc, IReadOnlyList<ValidationIssue> issues) =>
            BuildJson(sequence, source, timestampUtc, issues, table: null);

        /// <summary>ADR-0055 新增的带 <paramref name="table"/> 重载：组装落盘 JSON 文档：
        /// <c>sequence</c>（单调递增序号，<see cref="long"/> 精确写出，不经 double 中转）、
        /// <c>source</c>（<see cref="ValidationReportTriggerSource"/> 取值之一）、<c>table</c>
        /// （ADR-0055：这次校验确切针对哪一张表；<see cref="ValidationReportTriggerSource.HotReload"/>
        /// 下是被重载的表名，<see cref="ValidationReportTriggerSource.Startup"/> 下固定写出 JSON
        /// <c>null</c>——启动全量校验本就没有单一确定的表，不编造空字符串或 <c>"all"</c> 这类会被
        /// 误读成真实表名的占位值；与本文件 <c>record_key</c>/<c>field</c>/<c>group</c>/<c>note</c>/
        /// <c>rule_id</c> 等既有"未填也输出 <c>null</c>、不整体省略字段"的惯例一致，消费方按可选字段
        /// 处理即可，不需要区分"字段缺失"与"字段为 <c>null</c>"两种形态）、<c>timestamp_utc</c>
        /// （见 <see cref="FormatTimestampUtc"/>）、<c>issues</c>（元素形状与 <c>toolchain/validator
        /// --json</c> 的 <c>issues[]</c> 完全一致，见 <see cref="ValidationIssueJsonWriter.AppendIssueJson"/>，
        /// 二者共用同一份实现，不会漂移；逐条问题自己已经带有 <c>table</c> 字段——那是"这一条问题
        /// 出在哪张表"，本信封新增的 <c>table</c> 字段是"这一次校验触发时确切针对哪张表"，两者语义
        /// 不同、不能互相替代：启动全量校验下每条问题的 <c>table</c> 字段仍然各自有值，但信封级
        /// <c>table</c> 依然是 <c>null</c>；热重载下若本次校验干净（<c>issues</c> 为空数组），
        /// 消费方只能从信封级 <c>table</c> 知道"刚才重载的是哪张表"，逐条问题里完全没有这个信息）。
        /// 公开（而非 internal）：本类型所在的 Unity 包未对任何测试程序集声明
        /// <c>InternalsVisibleTo</c>（勘察确认，同 <c>core/sim</c> 若干类型的既有判断记录），公开
        /// 让 <c>adapters/unity/DiagnosticsForwarding/tests/</c> 能直接断言 JSON 形状，不必迂回读回
        /// 磁盘文件再解析。</summary>
        public static string BuildJson(long sequence, string source, DateTime timestampUtc, IReadOnlyList<ValidationIssue> issues, string? table)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (issues == null) throw new ArgumentNullException(nameof(issues));

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"sequence\":").Append(sequence.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"source\":\"").Append(ValidationIssueJsonWriter.JsonEscape(source)).Append("\",");
            sb.Append("\"table\":").Append(table == null ? "null" : "\"" + ValidationIssueJsonWriter.JsonEscape(table) + "\"").Append(',');
            sb.Append("\"timestamp_utc\":\"").Append(FormatTimestampUtc(timestampUtc)).Append("\",");
            sb.Append("\"issues\":[");
            for (var i = 0; i < issues.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                ValidationIssueJsonWriter.AppendIssueJson(sb, issues[i]);
            }
            sb.Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>固定格式 <c>yyyy-MM-ddTHH:mm:ss.fffZ</c>、不变文化（AGENTS.md 第 3 节"浮点/时间
        /// 格式化固定用不变文化"）：<paramref name="timestampUtc"/> 非 UTC 时先转换，保证输出恒为
        /// UTC（<c>Z</c> 后缀），不随调用进程所在时区/文化设置漂移。</summary>
        private static string FormatTimestampUtc(DateTime timestampUtc)
        {
            var utc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime();
            return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        /// <summary>原子写：见类型头判断记录"原子写"。临时文件与目标文件同目录，保证
        /// <see cref="File.Move(string, string)"/> 落在同一个文件系统卷内，是真正的原子重命名（跨卷
        /// 移动在部分文件系统上会退化为"拷贝+删除"，不再原子）。</summary>
        private static void AtomicWriteAllText(string fullPath, string content)
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            var tempPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(tempPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
                File.Move(tempPath, fullPath);
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
                throw;
            }
        }
    }
}
