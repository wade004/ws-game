using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Core.Foundation.EngineAdapter;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// <see cref="IDataSource"/> 的 <see cref="IFileSystem"/> 实现：遍历
    /// <c>fs.ListFiles(rootDir)</c> 中以 <c>.json</c> 结尾的项，表名取文件名（去掉 <c>.json</c>
    /// 扩展名，不含目录部分）；文件名与文件内顶层 <c>table</c> 字段是否一致由
    /// <see cref="DataRegistry"/> 加载期的 <c>envelope</c> 校验负责，本类不做该检查。
    /// <para>
    /// <c>ListFiles</c> 语义判断记录：02_引擎适配层.md 第 1.6 节只给出签名
    /// <c>listFiles(dirPath): List&lt;String&gt;</c>，未规定返回值是绝对路径还是相对
    /// <c>dirPath</c> 的路径、是否递归。本模块按"实现级约定"（见
    /// <c>core/foundation/engine_adapter/README.md</c>"IFileSystem"一节）采用：返回
    /// <c>rootDir</c>之下（递归）全部文件、相对 <c>rootDir</c>、
    /// 用 <c>/</c> 分隔、按序数排序——因此本类把 <c>rootDir</c> 与每个相对路径拼接回完整路径
    /// 后才传给 <see cref="IFileSystem.ReadText"/>。
    /// </para>
    /// <para>
    /// 判断记录（数据根非数据表 JSON 误判修复任务，2026-09-16，"非数据表 JSON 候选判定"）：
    /// <see cref="DataSourceOptions.SkipNonTableJsonFiles"/>（默认开启，见该类型判断记录）开启时，
    /// <see cref="ListTables"/> 只把"看起来是数据表"的 <c>*.json</c> 文件交给
    /// <see cref="DataRegistry"/>，其余的计入 <see cref="SkippedNonTableFiles"/>（本类型不做任何
    /// 控制台/日志输出——是否、如何展示交给调用方，如 <c>toolchain/validator/Program.cs</c>，保持
    /// core 库不直接做 I/O 的既有分层）。判定规则（读 <c>data/README.md</c>"两类目录"一节 +
    /// <c>core/foundation/data_registry/contracts/TableSchema.cs</c> <c>Domain</c>/<c>WithDomain</c>
    /// 判断记录"04 第 2.2 节勘误登记的单段名命名例外"总结出的最小规则，不与两处的既有判断逻辑重复
    /// 实现——本类型只做"文件名/相对路径形状"层面的粗筛，真正的表名合法性仍由
    /// <see cref="DataRegistry"/> 加载期的 envelope/schema 校验判定）：
    /// <list type="number">
    /// <item>文件名（去掉 <c>.json</c>）含至少一个 <c>.</c> 时，取第一个 <c>.</c> 之前的字符串作为
    /// "域"；域必须匹配 <c>^[a-z][a-z0-9_]*$</c>（与 <c>toolchain/validate_data.py</c> 的 <c>ID_RE</c>
    /// domain 段同一字符集）——覆盖绝大多数表名 <c>&lt;域&gt;.&lt;表名&gt;.json</c> 的既有约定。</item>
    /// <item>文件名（去掉 <c>.json</c>）完全等于 <see cref="SingleSegmentTableDomains"/> 登记的三个
    /// 单段名例外（<c>camera_profile</c>/<c>ui_layout_definition</c>/<c>shell_menu_definition</c>，
    /// 与 <c>TableSchema.cs</c> 该判断记录逐字一致）时，域取该字典登记的固定值（<c>camera</c>/
    /// <c>ui</c>/<c>shell</c>）。</item>
    /// <item>以上两种方式都得不到域的文件（如 <c>package.json</c>：无 <c>.</c>，也不在例外表里）
    /// 判定为"非数据表文件"，跳过。</item>
    /// <item>能得到域的文件还需满足"文件直接在数据根下（相对路径只有一段），或文件所在的直接上级
    /// 目录名等于该域"——对应 <c>data/README.md</c> 记录的
    /// <c>data/_framework/&lt;domain&gt;/&lt;table&gt;.json</c> 目录约定；"直接在数据根下"这一分支
    /// 专为兼容测试夹具里常见的扁平布局（数据根下直接放 <c>test.thing.json</c>，不建 <c>test/</c>
    /// 子目录）保留，不因此判定为跳过。不满足则判定为"非数据表文件"，跳过——这一条挡住形如
    /// <c>some_unrelated_dir/arch.config.json</c> 这类"文件名凑巧带合法域前缀、但没放在对应域目录
    /// 下"的配置文件被误当表处理（`xxx.config.json` 场景，见任务判断记录）。</item>
    /// </list>
    /// 与 <c>toolchain/validate_data.py</c> 的 <c>_resolve_table_domain</c>/<c>_is_data_table_candidate</c>
    /// 是同一条规则的两份独立实现（两个工具链各自的运行时/语言边界不共享代码，见该文件同一判断
    /// 记录）——修改本规则时两处必须同步更新，回归测试见
    /// <c>core/foundation/data_registry/tests/FileSystemDataSourceSkipsNonTableJsonTests.cs</c> 与
    /// <c>toolchain/tests/test_validate_data_skips_nontable_json.py</c>。
    /// </para>
    /// </summary>
    public sealed class FileSystemDataSource : IDataSource
    {
        private const string JsonExtension = ".json";

        /// <summary>04 第 2.2 节勘误登记的三张单段名命名例外，与
        /// <c>core/foundation/data_registry/contracts/TableSchema.cs</c> <c>WithDomain</c> 判断记录
        /// 登记的调用点逐字一致（<c>presentation/camera/schema/CameraSchemas.cs</c>、
        /// <c>presentation/ui/schema/UiLayoutSchema.cs</c>、
        /// <c>presentation/shell/schema/ShellMenuSchema.cs</c>）。</summary>
        private static readonly IReadOnlyDictionary<string, string> SingleSegmentTableDomains =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["camera_profile"] = "camera",
                ["ui_layout_definition"] = "ui",
                ["shell_menu_definition"] = "shell",
            };

        private static readonly Regex DomainTokenRegex = new Regex("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

        private readonly IFileSystem _fs;
        private readonly string _rootDir;
        private readonly DataSourceOptions _options;

        /// <summary>消费方反馈第 51 条（2026-09-17，core/sim 线程安全与并发排查）根治：此前是
        /// <c>readonly List&lt;string&gt;</c>，<see cref="ListTables"/> 每次调用先 <c>Clear()</c> 再
        /// 逐条 <c>Add</c>——同一个 <see cref="FileSystemDataSource"/> 实例若被多个线程并发调用
        /// <see cref="ListTables"/>（如调用方在并发跑的多个 <c>Core.Sim.*.Run</c> 之间复用同一份
        /// <c>IReadOnlyList&lt;IDataSource&gt;</c>，见 <c>core/sim/README.md</c>"线程安全与并发"章节
        /// "唯一需要留意的例外"），会在同一个 <c>List&lt;T&gt;</c> 上产生真正的数据竞争（并发
        /// <c>Clear</c>/<c>Add</c> 可能互相踩踏内部数组状态，不只是"读到过期值"这种良性竞态）——
        /// <see cref="DataRegistry"/> 加载期无条件调用 <see cref="ListTables"/>（见该类型判断记录），
        /// 是每次 <c>HeadlessWorldBuilder.Build</c>/<c>Core.Sim.*.Run</c> 调用都会走到的路径，因此这
        /// 不是一个边缘场景。改为每次调用在方法内部新建一份局部 <c>List&lt;string&gt;</c>，最后一次性
        /// 整体替换 <see cref="_skippedNonTableFiles"/> 引用（引用赋值在 CLR 上是原子操作，不会读到
        /// "半份"列表）——<see cref="ListTables"/> 返回值本身（供 <see cref="DataRegistry"/> 实际装载
        /// 用的 <c>result</c>）此前就已经是每次调用新建的局部变量，不受这个修复影响；唯一改变的是
        /// <see cref="SkippedNonTableFiles"/> 这条诊断信息在并发调用下的语义——从"可能损坏"变为
        /// "多次并发调用里最后一次写入生效"（诊断用途，不影响任何实际装载结果，可接受）。</summary>
        private volatile IReadOnlyList<string> _skippedNonTableFiles = Array.Empty<string>();

        public FileSystemDataSource(IFileSystem fs, string rootDir)
            : this(fs, rootDir, new DataSourceOptions())
        {
        }

        /// <summary>判断记录（ABI 只新增，见 <see cref="DataSourceOptions"/> 类型判断记录）：本构造
        /// 函数是纯新增重载，原两参数构造函数签名不变、内部委派到本构造函数并使用默认
        /// <see cref="DataSourceOptions"/>（<see cref="DataSourceOptions.SkipNonTableJsonFiles"/>
        /// 默认 <c>true</c>）。</summary>
        public FileSystemDataSource(IFileSystem fs, string rootDir, DataSourceOptions options)
        {
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
            _rootDir = rootDir ?? throw new ArgumentNullException(nameof(rootDir));
            _options = options ?? new DataSourceOptions();
        }

        /// <summary>消费方反馈第三批第 22 条：显式覆盖 <see cref="IDataSource.Root"/> 默认实现，
        /// 返回构造时传入的根目录——<see cref="ListTables"/> 产出的每条 <see cref="DataTableSource.Location"/>
        /// 都以 <c>CombinePath(_rootDir, rel)</c> 拼成，本属性即那个前缀本身。</summary>
        public string? Root => _rootDir;

        /// <summary>上一次 <see cref="ListTables"/> 调用中，因不满足"非数据表 JSON 候选判定"规则
        /// （见类型判断记录）而被跳过的文件，相对 <see cref="Root"/>、用 <c>/</c> 分隔——与
        /// <see cref="DataTableSource.Location"/> 的路径风格一致，便于调用方直接拼接展示。
        /// <see cref="DataSourceOptions.SkipNonTableJsonFiles"/> 为 <c>false</c> 时恒为空列表。
        /// 本类型不做任何控制台/日志输出，是否、如何展示这份清单交给调用方（如
        /// <c>toolchain/validator/Program.cs</c>）决定。</summary>
        public IReadOnlyList<string> SkippedNonTableFiles => _skippedNonTableFiles;

        public IReadOnlyList<DataTableSource> ListTables()
        {
            var relatives = _fs.ListFiles(_rootDir);
            var result = new List<DataTableSource>();
            var skipped = new List<string>();

            foreach (var rel in relatives)
            {
                if (!rel.EndsWith(JsonExtension, StringComparison.Ordinal))
                {
                    continue;
                }

                var lastSlash = rel.LastIndexOf('/');
                var fileName = lastSlash < 0 ? rel : rel.Substring(lastSlash + 1);
                var tableName = fileName.Substring(0, fileName.Length - JsonExtension.Length);

                if (_options.SkipNonTableJsonFiles && !IsDataTableCandidate(tableName, rel))
                {
                    skipped.Add(rel);
                    continue;
                }

                var fullPath = CombinePath(_rootDir, rel);
                result.Add(new DataTableSource(tableName, fullPath, () => ReadTextOrThrow(fullPath)));
            }

            // 见 _skippedNonTableFiles 字段判断记录：一次性整体替换引用，不在共享的可变容器上做
            // Clear()+Add() 这类多步骤原地修改。
            _skippedNonTableFiles = skipped;
            return result;
        }

        /// <summary>类型判断记录"非数据表 JSON 候选判定"规则的实现：先解析出 <paramref name="tableName"/>
        /// 对应的"域"，再核对 <paramref name="relativePath"/>（相对数据根、<c>/</c> 分隔）是否放在了
        /// 该域对应的目录下（或直接在数据根下）。</summary>
        private static bool IsDataTableCandidate(string tableName, string relativePath)
        {
            if (!TryResolveDomain(tableName, out var domain))
            {
                return false;
            }

            var parts = relativePath.Split('/');
            if (parts.Length <= 1)
            {
                // 直接在数据根下：兼容测试夹具常见的扁平布局（如 "test.thing.json" 不建 test/ 子目录）。
                return true;
            }

            var parentDir = parts[parts.Length - 2];
            return string.Equals(parentDir, domain, StringComparison.Ordinal);
        }

        private static bool TryResolveDomain(string tableName, out string domain)
        {
            if (SingleSegmentTableDomains.TryGetValue(tableName, out var mapped))
            {
                domain = mapped;
                return true;
            }

            var dot = tableName.IndexOf('.');
            if (dot <= 0)
            {
                domain = string.Empty;
                return false;
            }

            var firstSegment = tableName.Substring(0, dot);
            if (!DomainTokenRegex.IsMatch(firstSegment))
            {
                domain = string.Empty;
                return false;
            }

            domain = firstSegment;
            return true;
        }

        private string ReadTextOrThrow(string fullPath)
        {
            var text = _fs.ReadText(fullPath);
            if (text == null)
            {
                throw new InvalidOperationException($"文件不存在或读取失败：{fullPath}");
            }
            return text;
        }

        private static string CombinePath(string root, string relative)
        {
            if (root.Length == 0) return relative;
            return root.EndsWith("/", StringComparison.Ordinal) ? root + relative : root + "/" + relative;
        }
    }
}
