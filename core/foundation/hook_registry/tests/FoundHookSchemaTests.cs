using System;
using System.IO;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.HookRegistry;
using Xunit;

namespace Tests.Foundation.HookRegistry
{
    /// <summary>
    /// <see cref="FoundHookSchema.LoadDefinitions"/>（收边任务补齐的契约缺口）：从
    /// <c>found.hook</c> 表构造挂载点定义列表，缺表时退化为 <see cref="WellKnownHooks"/> 两个
    /// 内置常量。
    /// </summary>
    public class FoundHookSchemaTests
    {
        private static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
            });
            return new EventBus(catalog);
        }

        private static IDataRegistry BuildRegistry(params (string TableName, string Json)[] tables)
        {
            var source = new InMemoryDataSource();
            foreach (var (tableName, json) in tables)
            {
                source.Add(tableName, json);
            }
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(FoundHookSchema.Table);
            registry.LoadAll();
            return registry;
        }

        // 1. 表缺失时退化为 WellKnownHooks 两个内置常量。
        [Fact]
        public void LoadDefinitions_TableMissing_FallsBackToWellKnownHooks()
        {
            var registry = BuildRegistry();

            var definitions = FoundHookSchema.LoadDefinitions(registry);

            Assert.Equal(2, definitions.Count);
            Assert.Contains(definitions, d => d.HookId == WellKnownHooks.ScenePreUnload);
            Assert.Contains(definitions, d => d.HookId == WellKnownHooks.ScenePostLoad);
        }

        // 2. 从表行正确解析，allow_multiple 缺省为 true。
        [Fact]
        public void LoadDefinitions_ParsesRowsFromTable()
        {
            var json = @"{
                ""table"": ""found.hook"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""found.hook.custom_point"", ""signature"": ""unitId: Id"", ""description"": ""自定义挂载点"" }
                ]
            }";
            var registry = BuildRegistry(("found.hook", json));

            var definitions = FoundHookSchema.LoadDefinitions(registry);

            Assert.Single(definitions);
            Assert.Equal(new Core.Foundation.Common.Id("found.hook.custom_point"), definitions[0].HookId);
            Assert.Equal("unitId: Id", definitions[0].Signature);
            Assert.True(definitions[0].AllowMultiple);
        }

        // 3. allow_multiple 显式为 false 时被正确解析（不是缺省值）。
        [Fact]
        public void LoadDefinitions_RespectsExplicitAllowMultipleFalse()
        {
            var json = @"{
                ""table"": ""found.hook"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""found.hook.single_point"", ""signature"": ""none"", ""allow_multiple"": false }
                ]
            }";
            var registry = BuildRegistry(("found.hook", json));

            var definitions = FoundHookSchema.LoadDefinitions(registry);

            Assert.False(definitions[0].AllowMultiple);
        }

        // 4. 一致性守护：WellKnownHooks 的全部常量都能在真实 data/_framework/found/found.hook.json
        //    里找到对应行（代码常量与表一致性，任务书明确要求"用一个测试守住"）。
        [Fact]
        public void WellKnownHooks_AllConstants_ExistInRealFrameworkDataFile()
        {
            var repoRoot = FindRepoRoot();
            var path = Path.Combine(repoRoot, "data", "_framework", "found", "found.hook.json");
            var json = File.ReadAllText(path);
            var registry = BuildRegistry(("found.hook", json));

            var definitions = FoundHookSchema.LoadDefinitions(registry);

            Assert.Contains(definitions, d => d.HookId == WellKnownHooks.ScenePreUnload);
            Assert.Contains(definitions, d => d.HookId == WellKnownHooks.ScenePostLoad);
            // 反向：数据文件里不应出现 WellKnownHooks 之外、代码没有对应常量却被当作"已知"的
            // 幽灵挂载点——本仓库当前只有这两个架构文档点名的挂载点。
            Assert.Equal(2, definitions.Count);
        }

        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空"));
            for (int i = 0; i < 4; i++)
            {
                dir = dir.Parent ?? throw new InvalidOperationException($"源文件路径层级不足，无法定位仓库根目录：{sourceFilePath}");
            }
            return dir.FullName;
        }
    }
}
