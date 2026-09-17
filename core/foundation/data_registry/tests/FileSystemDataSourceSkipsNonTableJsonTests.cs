using System.Linq;
using Adapters.Stub;
using Core.Foundation.DataRegistry;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// 数据根非数据表 JSON 误判修复任务（2026-09-16）的回归测试：<see cref="FileSystemDataSource"/>
    /// 不应把数据根下"看起来不像数据表"的 <c>*.json</c> 文件（典型如游戏侧仓库 UPM 包根目录的
    /// <c>package.json</c>）当成数据表交给 <see cref="DataRegistry"/>，否则会被误判成表名
    /// <c>"package"</c> 的数据表，报出一堆"缺少顶层字段 table/schema_version/rows"的假错误（复现见
    /// <c>toolchain/validate_data.py --data-root data/_framework --data-root games/_template --strict</c>）。
    /// 判定规则详见 <see cref="FileSystemDataSource"/> 类型注释判断记录"非数据表 JSON 候选判定"；
    /// Python 侧同一条规则的独立实现与回归测试见
    /// <c>toolchain/validate_data.py</c> 的 <c>is_data_table_candidate</c>/
    /// <c>toolchain/tests/test_validate_data_skips_nontable_json.py</c>。
    /// </summary>
    public class FileSystemDataSourceSkipsNonTableJsonTests
    {
        private const string ValidEnvelope = "{\"table\": \"arch.class\", \"schema_version\": 1, \"rows\": []}";

        [Fact]
        public void ListTables_SkipsPackageJsonSittingDirectlyUnderRoot_ByDefault()
        {
            var fs = new StubFileSystem();
            fs.WriteTextAtomic("root/arch/arch.class.json", ValidEnvelope);
            fs.WriteTextAtomic("root/package.json", "{\"name\": \"com.example.game\", \"version\": \"1.0.0\"}");

            var source = new FileSystemDataSource(fs, "root");
            var tables = source.ListTables();

            Assert.Single(tables);
            Assert.Equal("arch.class", tables[0].TableName);

            Assert.Single(source.SkippedNonTableFiles);
            Assert.Equal("package.json", source.SkippedNonTableFiles[0]);
        }

        [Theory]
        [InlineData("camera", "camera_profile")]
        [InlineData("ui", "ui_layout_definition")]
        [InlineData("shell", "shell_menu_definition")]
        public void ListTables_KeepsSingleSegmentExceptionTables_WhenInMatchingDomainDirectory(string domainDir, string tableName)
        {
            var fs = new StubFileSystem();
            fs.WriteTextAtomic($"root/{domainDir}/{tableName}.json", $"{{\"table\": \"{tableName}\", \"schema_version\": 1, \"rows\": []}}");

            var source = new FileSystemDataSource(fs, "root");
            var tables = source.ListTables();

            Assert.Single(tables);
            Assert.Equal(tableName, tables[0].TableName);
            Assert.Empty(source.SkippedNonTableFiles);
        }

        [Fact]
        public void ListTables_SkipsSingleSegmentExceptionTable_WhenNotInMatchingDomainDirectory()
        {
            // camera_profile 放错了目录（不在 "camera/" 下）——按判定规则不是候选表，跳过而不是
            // 当成一张位置错误的表硬塞进 DataRegistry（错目录本身就应该被当作"这不是数据表文件"）。
            var fs = new StubFileSystem();
            fs.WriteTextAtomic("root/misc/camera_profile.json", "{\"table\": \"camera_profile\", \"schema_version\": 1, \"rows\": []}");

            var source = new FileSystemDataSource(fs, "root");
            var tables = source.ListTables();

            Assert.Empty(tables);
            Assert.Equal(new[] { "misc/camera_profile.json" }, source.SkippedNonTableFiles);
        }

        [Fact]
        public void ListTables_KeepsDottedDomainFile_DirectlyUnderRoot_FlatLayout()
        {
            // 兼容既有测试夹具常见的扁平布局（数据根下直接放 "test.thing.json"，不建 test/ 子目录）。
            var fs = new StubFileSystem();
            fs.WriteTextAtomic("root/test.thing.json", "{\"table\": \"test.thing\", \"schema_version\": 1, \"rows\": []}");

            var source = new FileSystemDataSource(fs, "root");
            var tables = source.ListTables();

            Assert.Single(tables);
            Assert.Equal("test.thing", tables[0].TableName);
            Assert.Empty(source.SkippedNonTableFiles);
        }

        [Fact]
        public void ListTables_SkipsDottedFile_WhenParentDirectoryDoesNotMatchDomain()
        {
            // "文件名凑巧带合法域前缀、但没放在对应域目录下"的配置文件（如误放的 arch.config.json）
            // 不应被当成 arch 域的数据表。
            var fs = new StubFileSystem();
            fs.WriteTextAtomic("root/tools/arch.config.json", "{\"table\": \"arch.config\", \"schema_version\": 1, \"rows\": []}");

            var source = new FileSystemDataSource(fs, "root");
            var tables = source.ListTables();

            Assert.Empty(tables);
            Assert.Equal(new[] { "tools/arch.config.json" }, source.SkippedNonTableFiles);
        }

        [Fact]
        public void ListTables_OptedOut_RestoresLegacyBehavior_TreatingEveryJsonAsATable()
        {
            var fs = new StubFileSystem();
            fs.WriteTextAtomic("root/arch/arch.class.json", ValidEnvelope);
            fs.WriteTextAtomic("root/package.json", "{\"name\": \"com.example.game\"}");

            var source = new FileSystemDataSource(fs, "root", new DataSourceOptions { SkipNonTableJsonFiles = false });
            var tables = source.ListTables();

            Assert.Equal(2, tables.Count);
            Assert.Contains(tables, t => t.TableName == "arch.class");
            Assert.Contains(tables, t => t.TableName == "package");
            Assert.Empty(source.SkippedNonTableFiles);
        }

        [Fact]
        public void ListTables_RealSampleDataTree_StillDiscoversAllKnownTables()
        {
            // 端到端防呆：真实 data/_sample 目录树（含三张单段名例外表）跑一遍磁盘发现，表数量不因
            // 本次修复而减少——见 toolchain/tests/test_validate_data_skips_nontable_json.py 同一意图
            // 的 Python 侧用例、DataRegistryTests.cs 既有 real-sample 用例。
            var repoRoot = FindRepoRoot();
            var fs = new Adapters.Stub.StubFileSystem();
            var sampleDir = System.IO.Path.Combine(repoRoot, "data", "_sample").Replace('\\', '/');
            foreach (var file in System.IO.Directory.EnumerateFiles(sampleDir, "*.json", System.IO.SearchOption.AllDirectories))
            {
                var relative = System.IO.Path.GetRelativePath(sampleDir, file).Replace('\\', '/');
                fs.WriteTextAtomic($"disk/{relative}", System.IO.File.ReadAllText(file));
            }

            var source = new FileSystemDataSource(fs, "disk");
            var tables = source.ListTables();

            Assert.Empty(source.SkippedNonTableFiles);
            Assert.Contains(tables, t => t.TableName == "camera_profile");
            Assert.Contains(tables, t => t.TableName == "ui_layout_definition");
            Assert.Contains(tables, t => t.TableName == "shell_menu_definition");
        }

        /// <summary>消费方反馈第 51 条（2026-09-17，core/sim 线程安全与并发排查）根治的回归测试：
        /// 此前 <c>ListTables()</c> 在共享的 <c>_skippedNonTableFiles</c> 字段上做 <c>Clear()</c>+
        /// 逐条 <c>Add()</c>，同一个 <see cref="FileSystemDataSource"/> 实例被多个线程并发调用
        /// <c>ListTables()</c> 时会在同一个 <c>List&lt;T&gt;</c> 上产生数据竞争（可能抛异常或损坏内部
        /// 状态，不只是"读到过期值"）。本用例并发调用同一实例的 <c>ListTables()</c> 数十次，断言
        /// 不抛异常、且每次返回的 <c>tables</c>（实际装载用的返回值，本就是每次调用新建的局部变量，
        /// 从未受这个字段影响）逐次结果一致——回归修复前用 <c>List&lt;T&gt;</c> 直接跑本用例在本地
        /// 多次运行能稳定复现 <c>ArgumentException</c>/<c>IndexOutOfRangeException</c> 或计数不稳定，
        /// 修复后（改为一次性整体替换 <see cref="IReadOnlyList{T}"/> 引用）恒定通过。</summary>
        [Fact]
        public void ListTables_CalledConcurrentlyOnSameInstance_DoesNotThrow_AndTableListIsStable()
        {
            var fs = new StubFileSystem();
            fs.WriteTextAtomic("root/arch/arch.class.json", ValidEnvelope);
            fs.WriteTextAtomic("root/package.json", "{\"name\": \"com.example.game\", \"version\": \"1.0.0\"}");
            var source = new FileSystemDataSource(fs, "root");

            const int degree = 16;
            const int iterationsPerThread = 50;
            var results = new System.Collections.Concurrent.ConcurrentBag<int>();

            System.Threading.Tasks.Parallel.For(0, degree, _ =>
            {
                for (var i = 0; i < iterationsPerThread; i++)
                {
                    var tables = source.ListTables();
                    results.Add(tables.Count);
                }
            });

            Assert.All(results, count => Assert.Equal(1, count));
            Assert.Equal(degree * iterationsPerThread, results.Count);
        }

        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            // 本源文件固定位于 <repoRoot>/core/foundation/data_registry/tests/FileSystemDataSourceSkipsNonTableJsonTests.cs，
            // 向上 4 级（tests → data_registry → foundation → core）即仓库根，惯例同
            // core/sim/tests/SimTestWorldFactory.cs 的同名方法。
            var dir = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(sourceFilePath)!);
            for (var i = 0; i < 4; i++)
            {
                dir = dir.Parent!;
            }
            return dir.FullName;
        }
    }
}
