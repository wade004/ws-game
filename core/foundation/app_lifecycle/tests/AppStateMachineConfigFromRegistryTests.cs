using System;
using System.IO;
using Core.Foundation.AppLifecycle;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.AppLifecycle
{
    /// <summary>
    /// <see cref="AppStateMachineConfig.FromRegistry"/>（收边任务补齐的契约缺口 2）：从
    /// <c>found.game_state</c> 表构造配置，缺表时退化为 <see cref="AppStateMachineConfig.Default"/>。
    /// </summary>
    public class AppStateMachineConfigFromRegistryTests
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
            registry.RegisterSchema(FoundGameStateSchema.Table);
            registry.LoadAll();
            return registry;
        }

        // 1. 表缺失（未加载任何行）时退化为 Default()，行为不变。
        [Fact]
        public void FromRegistry_TableMissing_FallsBackToDefault()
        {
            var registry = BuildRegistry();

            var config = AppStateMachineConfig.FromRegistry(registry);

            Assert.True(config.IsTransitionAllowed(AppState.Boot, AppState.MainMenu));
            Assert.True(config.IsSubTransitionAllowed(SubStateId.Combat, SubStateId.MenuOverlay));
        }

        // 2. 从表行正确构造出主状态与子状态两类转移。
        [Fact]
        public void FromRegistry_ParsesMainAndSubRowsFromTable()
        {
            var json = @"{
                ""table"": ""found.game_state"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""found.state.boot_to_main_menu"", ""from"": ""Boot"", ""to"": ""MainMenu"", ""kind"": ""main"" },
                    { ""id"": ""found.state.explore_to_combat"", ""from"": ""Explore"", ""to"": ""Combat"", ""kind"": ""sub"" }
                ]
            }";
            var registry = BuildRegistry(("found.game_state", json));

            var config = AppStateMachineConfig.FromRegistry(registry);

            Assert.True(config.IsTransitionAllowed(AppState.Boot, AppState.MainMenu));
            Assert.True(config.IsSubTransitionAllowed(SubStateId.Explore, SubStateId.Combat));
            // 未在表中登记的转移不应被允许（不是"在默认表基础上叠加"，见 FromDefinitions 判断记录）。
            Assert.False(config.IsTransitionAllowed(AppState.InWorld, AppState.Pause));
        }

        // 3. registry 为 null 时抛异常（防御性检查，惯例同其它 Assembly 入口）。
        [Fact]
        public void FromRegistry_NullRegistry_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => AppStateMachineConfig.FromRegistry(null!));
        }

        // 4. 真实 data/_framework/found/found.game_state.json 与 Default() 等价（正常装配路径下
        //    两者结果一致，见类型判断记录）。
        [Fact]
        public void FromRegistry_RealFrameworkDataFile_EquivalentToDefault()
        {
            var repoRoot = FindRepoRoot();
            var path = Path.Combine(repoRoot, "data", "_framework", "found", "found.game_state.json");
            var json = File.ReadAllText(path);
            var registry = BuildRegistry(("found.game_state", json));

            var fromTable = AppStateMachineConfig.FromRegistry(registry);
            var fromDefault = AppStateMachineConfig.Default();

            // 03 第 2 节默认表的全部主状态转移在数据文件里都能找到等价行。
            Assert.True(fromTable.IsTransitionAllowed(AppState.Boot, AppState.MainMenu));
            Assert.True(fromTable.IsTransitionAllowed(AppState.MainMenu, AppState.Loading));
            Assert.True(fromTable.IsTransitionAllowed(AppState.Loading, AppState.InWorld));
            Assert.True(fromTable.IsTransitionAllowed(AppState.Loading, AppState.MainMenu));
            Assert.True(fromTable.IsTransitionAllowed(AppState.InWorld, AppState.Pause));
            Assert.True(fromTable.IsTransitionAllowed(AppState.InWorld, AppState.MainMenu));
            Assert.True(fromTable.IsTransitionAllowed(AppState.InWorld, AppState.Loading));
            Assert.True(fromTable.IsTransitionAllowed(AppState.Pause, AppState.InWorld));
            Assert.True(fromTable.IsTransitionAllowed(AppState.Pause, AppState.MainMenu));
            Assert.Equal(
                fromDefault.IsSubTransitionAllowed(SubStateId.Combat, SubStateId.MenuOverlay),
                fromTable.IsSubTransitionAllowed(SubStateId.Combat, SubStateId.MenuOverlay));
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
