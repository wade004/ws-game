using System;
using System.IO;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Creature;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Gameplay.Spawn;
using Presentation.Assembly;
using Xunit;

namespace Tests.Gameplay.Spawn
{
    /// <summary>
    /// 消费方反馈第 44 条正例演示：<c>data/_sample/creature/creature.template.json</c> 新增的
    /// <c>creature.sample_summon_totem</c>（<c>npc_flags</c> 含 <c>npc_flag.summon_only</c>）配合真实
    /// <see cref="ContentValidationAssembly"/> 装配，证明 <see cref="SpawnSummonOnlyCreatureRule"/> 的
    /// Error 分支能用示例数据触发——反馈原文核实"未经修改的示例数据 spawn_summon_only_creature
    /// 检查项 0 条诊断，必须像消费方自己的 PluginValidationWiringIntegrationTests 那样在内存里现改
    /// creature.sample_beast.npc_flags 才能观测到该规则的正例（Error）分支"，本类用真实新增的示例
    /// 生物模板本身（不改写既有生物）证明这条路径开箱可验证。
    /// <para>
    /// 判断记录（不把违规行写进 <c>data/_sample/spawn/spawn.table.json</c>）：示例数据集的既有基线是
    /// "干净可通过全量校验"（<c>validate_data.py --strict</c> 0 错误 0 警告），故意违规的
    /// <c>content_ref</c> 行只在本测试内存里追加（见 <see cref="BuildExtraSpawnRowSource"/>），走多根
    /// 合并（同 <c>creature.sample_summon_totem</c> 一样不出现在 <c>spawn.table.json</c> 里，反馈原文
    /// 建议二选一"里挑的那一个）。
    /// </para>
    /// </summary>
    public sealed class SpawnSummonOnlyCreatureRealSampleDataTests
    {
        // -----------------------------------------------------------------
        // 定位仓库根（惯例同 core/foundation/data_registry/tests/DataRegistryTests.cs、
        // core/numbers/tests/L1SampleDataTests.cs 同名方法判断记录：不用 AppContext.BaseDirectory，
        // dotnet 命令按任务书要求加了 --artifacts-path 指到仓库外，运行期程序集目录不在仓库树下）。
        // 本文件路径固定是 <repoRoot>/core/gameplay/spawn/tests/SpawnSummonOnlyCreatureRealSampleDataTests.cs，
        // 向上 4 级（tests → spawn → gameplay → core）即仓库根。
        // -----------------------------------------------------------------
        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空"));
            for (int i = 0; i < 4; i++)
            {
                dir = dir.Parent ?? throw new InvalidOperationException($"源文件路径层级不足，无法定位仓库根目录：{sourceFilePath}");
            }
            return dir.FullName;
        }

        /// <summary>把 <c>data/_framework</c>/<c>data/_sample</c> 两个真实目录整棵拷进
        /// <see cref="StubFileSystem"/>（同 <c>DataRegistryTests.BuildRealFrameworkAndSampleSources</c>
        /// 同一手法），各自包成一个 <see cref="FileSystemDataSource"/>，供
        /// <see cref="IDataRegistry.LoadAll(System.Collections.Generic.IReadOnlyList{IDataSource})"/>
        /// 多根加载合并。</summary>
        private static (FileSystemDataSource Framework, FileSystemDataSource Sample) BuildRealFrameworkAndSampleSources()
        {
            var repoRoot = FindRepoRoot();
            var fs = new StubFileSystem();

            FileSystemDataSource BuildOne(string datasetDirName)
            {
                var root = Path.Combine(repoRoot, "data", datasetDirName);
                foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                {
                    var rel = file.Substring(root.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    fs.WriteTextAtomic("data/" + datasetDirName + "/" + rel, File.ReadAllText(file));
                }
                return new FileSystemDataSource(fs, "data/" + datasetDirName);
            }

            return (BuildOne("_framework"), BuildOne("_sample"));
        }

        /// <summary>只贡献额外一条 <c>spawn.table</c> 行（指向 <c>creature.sample_summon_totem</c>），
        /// 与 <c>data/_sample/spawn/spawn.table.json</c> 既有两行同表不同主键，走多根合并（不覆盖既有
        /// 行，见 <c>DataRegistry.LoadAll(IReadOnlyList)</c> 类型级判断记录"合并规则"）。</summary>
        private static InMemoryDataSource BuildExtraSpawnRowSource() => new InMemoryDataSource()
            .Add("spawn.table",
                "{\"table\": \"spawn.table\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"spawn.sample_summon_totem_should_not_exist\", \"map_id\": \"world.sample_field\", " +
                "\"content_ref\": \"creature.sample_summon_totem\", \"position\": {\"x\": 0, \"y\": 0}, " +
                "\"facing\": 0, \"respawn_policy\": \"never\"}]}");

        /// <summary>
        /// <see cref="ICreatureTemplateQuery"/> 的一个通用只读实现：直接读已加载的
        /// <see cref="IDataRegistryView"/>（不像 <see cref="Core.Carriers.Creature.CreatureFactory"/>
        /// 那样预先解析出强类型 <see cref="CreatureTemplate"/> 索引，本用例只需要
        /// <see cref="HasFlag"/>）。
        /// </summary>
        /// <remarks>
        /// 判断记录：<see cref="ContentValidationOptions.CreatureTemplateQuery"/> 必须在
        /// <see cref="ContentValidationAssembly.CreateRegistry"/> 调用时就提供（用于决定
        /// <c>SpawnSummonOnlyCreatureRule</c> 是否注册），但 registry 本身要到该方法返回后才存在——
        /// 先有 registry 才能构造一个"读 registry"的查询、查询又必须在此之前就绪，本类型用
        /// <see cref="Func{TResult}"/> 把"拿 view"这一步延迟到
        /// <see cref="IDataRegistry.LoadAll(System.Collections.Generic.IReadOnlyList{IDataSource})"/>
        /// 真正跑校验规则那一刻才解引用，打破这个顺序依赖（<c>toolchain/validator</c> 未采用同样的
        /// 接线方式，见该工具 <c>Program.cs</c> 判断记录"本工具运行时机没有真正的 ICreatureTemplateQuery
        /// 实现可用"——那是该工具"避免注册一条只在未接线时产生 Warning 噪音的规则"的既有取舍，与本类型
        /// 是否可行是两回事，本测试文件不改动该工具）。
        /// </remarks>
        private sealed class LazyRegistryCreatureTemplateQuery : ICreatureTemplateQuery
        {
            private readonly Func<IDataRegistryView> _viewAccessor;

            public LazyRegistryCreatureTemplateQuery(Func<IDataRegistryView> viewAccessor)
            {
                _viewAccessor = viewAccessor;
            }

            public CreatureTemplate Get(Id templateId) => throw new NotSupportedException("测试夹具未实现 Get：本用例只用到 HasFlag");

            public bool HasFlag(Id templateId, NpcFlag flag)
            {
                var record = _viewAccessor().Get("creature.template", templateId)
                    ?? throw new ArgumentException($"未登记的模板：\"{templateId}\"");
                return record.TryGetIdList("npc_flags", out var flags) && flags.Contains(NpcFlagIds.ToId(flag));
            }
        }

        [Fact]
        public void RealSampleData_SummonOnlyTotemInSpawnTable_ReportsSpawnSummonOnlyCreatureError()
        {
            var (framework, sample) = BuildRealFrameworkAndSampleSources();
            var extraSpawnRow = BuildExtraSpawnRowSource();

            IDataRegistryView? viewHolder = null;
            var query = new LazyRegistryCreatureTemplateQuery(() =>
                viewHolder ?? throw new InvalidOperationException("registry 尚未就绪"));

            var options = new ContentValidationOptions
            {
                CreatureTemplateQuery = query,
            };

            var registry = ContentValidationAssembly.CreateRegistry(framework, options, out var disabledOptionalRules);
            viewHolder = registry;

            // 接线生效的前提：SpawnSummonOnlyCreatureRule 真的注册了（不是"Disabled 列表里没写名字，
            // 但规则实际没跑"）。
            Assert.DoesNotContain("SpawnSummonOnlyCreatureRule", disabledOptionalRules);

            var report = registry.LoadAll(new IDataSource[] { framework, sample, extraSpawnRow });

            Assert.Contains(report.Issues, i =>
                i.Check == "spawn_summon_only_creature" &&
                i.Check == SpawnSummonOnlyCreatureRule.CheckName &&
                i.Severity == ValidationSeverity.Error &&
                i.RecordKey == "spawn.sample_summon_totem_should_not_exist");
            Assert.True(report.IsBlocking);
        }
    }
}
