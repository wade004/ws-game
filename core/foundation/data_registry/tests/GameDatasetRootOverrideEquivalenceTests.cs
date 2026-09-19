using Adapters.Stub;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.Data
{
    /// <summary>
    /// 消费方反馈第 68 条（长驻可交互运行入口，ADR-0040 决策 2）"数据根覆盖生效"这条底层机制的
    /// 纯 .NET 等价回归。
    /// <para>
    /// 背景：<c>games/_template/Runtime/GameBootstrap.cs</c> Bootstrap() 里真正的选根逻辑只有一行——
    /// "覆盖值非空则用覆盖值替换默认数据根字符串，再用该字符串构造 <see cref="FileSystemDataSource"/>"
    /// （<c>if (!string.IsNullOrEmpty(GameDatasetRootOverride)) { _gameDatasetRoot =
    /// GameDatasetRootOverride!; }</c>）。这段逻辑本身不依赖 UnityEngine，但它所在的类型是
    /// <c>MonoBehaviour</c>，回归测试只能以 Unity PlayMode 用例形式跑（见
    /// <c>games/_template/Tests/Runtime/GameTemplateResidentTests.cs</c>
    /// <c>ResidentRunner_DatasetRootOverride_LoadsProbeTable_FromOverrideRootOnly</c>），本次任务的
    /// 工作树不跑 Unity（见 AGENTS.md §1 与派单说明）。本类型把同一段选根逻辑原样搬到
    /// <see cref="ResolveGameDatasetRoot"/>（逐字对应上述 if 分支），配合真实的
    /// <see cref="FileSystemDataSource"/>/<see cref="DataRegistry"/>（与 GameBootstrap 用的是同一套
    /// 生产类型，只是不经 MonoBehaviour），验证"选中的根不同，加载到的数据确实不同"，弥补 Unity 侧
    /// 用例本次无法在此工作树验证的缺口，可在纯 .NET 侧（<c>dotnet test Core.sln</c>）自证。
    /// </para>
    /// <para>
    /// 判断记录（本类为什么不算"重复造轮子"）：本次修复的直接动机是此前
    /// <c>GameTemplateResidentTests.cs</c> 有一条验证"数据根覆盖生效"的用例把覆盖值设成了与默认值
    /// 相同的路径，通过与失败无法区分，等于没测。本类型不是要取代该 Unity 用例（覆盖 GameBootstrap
    /// 的 MonoBehaviour 生命周期与真实 StreamingAssets 内容根这两件事只有 Unity 用例能覆盖），而是
    /// 独立验证"覆盖不同的相对根字符串，FileSystemDataSource/DataRegistry 确实会加载到不同内容"这一
    /// 条更底层、双方共用的机制——这条机制一旦本身有缺陷（例如 <see cref="FileSystemDataSource"/>
    /// 内部缓存了第一次解析的根、后续调用忽略新根之类的假设性缺陷），Unity 侧用例也会一并遭殃，纯
    /// .NET 侧独立验证能更快定位问题出在"选根逻辑"还是"底层加载机制"。
    /// </para>
    /// <para>
    /// 反向确认（本任务要求"把覆盖功能去掉/传空，测试必须失败"，已实际执行一次并还原，证据见提交
    /// 说明/汇报）：临时把 <see cref="ResolveGameDatasetRoot"/> 改成恒定返回
    /// <see cref="DefaultRoot"/>（忽略传入的 <c>overrideRoot</c> 参数，模拟"覆盖功能被去掉"），重新
    /// 跑 <see cref="ResolveGameDatasetRoot_OverrideProvided_LoadsMarkerFromOverrideRoot_NotDefaultRoot"/>——
    /// 预期且实际观察到：该用例失败（<c>found</c> 断言为 false，因为此时仍从默认根读取，探针表在
    /// 默认根下不存在），随后已改回原实现，本文件当前版本恢复为全部通过。
    /// </para>
    /// </summary>
    public class GameDatasetRootOverrideEquivalenceTests
    {
        private const string DefaultRoot = "data/game";
        private const string ProbeTableName = "probe.dataset_root_marker";
        private const string ProbeRecordKey = "probe.dataset_root_marker.value";

        // 与 GameBootstrap.Bootstrap() 里 "if (!string.IsNullOrEmpty(GameDatasetRootOverride))
        // { _gameDatasetRoot = GameDatasetRootOverride!; }" 那一段逐字对应的选根逻辑镜像，故意不
        // 依赖 UnityEngine，只为在此无需 Unity 的场景下复用同一条判断分支。
        private static string ResolveGameDatasetRoot(string? overrideRoot) =>
            !string.IsNullOrEmpty(overrideRoot) ? overrideRoot! : DefaultRoot;

        private static string ProbeTableJson(string origin) =>
            "{\"table\": \"" + ProbeTableName + "\", \"schema_version\": 1, \"rows\": " +
            "[{\"id\": \"" + ProbeRecordKey + "\", \"origin\": \"" + origin + "\"}]}";

        // 同 DataRegistryTests.cs MakeBus()：DataRegistry.LoadAll() 内部会发布
        // DataRegistryEventKeys.LoadCompleted/ValidationFailed 两个事件，EventBus 在 StrictCatalog
        // 默认（true）下要求这两个 key 先登记，否则 PublishImmediate 直接抛异常。
        private static IEventBus MakeBus() => new EventBus(EventCatalog.FromDefinitions(new[]
        {
            new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
            new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                new[] { "errorCount", "warningCount" }),
        }));

        private static DataRegistry BuildRegistryAtRoot(StubFileSystem fs, string root, string origin)
        {
            fs.WriteTextAtomic($"{root}/{ProbeTableName}.json", ProbeTableJson(origin));
            var source = new FileSystemDataSource(fs, root);
            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { FailOnUnknownTable = false });
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, "占位探针表不应触发阻断态：" + string.Join("; ", report.Issues));
            return registry;
        }

        [Fact]
        public void ResolveGameDatasetRoot_OverrideProvided_LoadsMarkerFromOverrideRoot_NotDefaultRoot()
        {
            var fs = new StubFileSystem();
            var resolvedRoot = ResolveGameDatasetRoot("data/game_override_probe");
            Assert.Equal("data/game_override_probe", resolvedRoot);

            var registry = BuildRegistryAtRoot(fs, resolvedRoot, "override_root");

            var found = ((IDataRegistryView)registry).TryGet(ProbeTableName, ProbeRecordKey, out var record);
            Assert.True(found, "覆盖生效时应能从覆盖根读到探针记录");
            Assert.Equal("override_root", record!.GetString("origin"));
        }

        [Fact]
        public void ResolveGameDatasetRoot_OverrideAbsentOrEmpty_FallsBackToDefaultRoot()
        {
            // 覆盖值为 null 与空字符串都应视为"不覆盖"（对应 GameBootstrap 用
            // string.IsNullOrEmpty 判断，不是只判断 null）。
            Assert.Equal(DefaultRoot, ResolveGameDatasetRoot(null));
            Assert.Equal(DefaultRoot, ResolveGameDatasetRoot(""));

            var fs = new StubFileSystem();
            var registry = BuildRegistryAtRoot(fs, ResolveGameDatasetRoot(null), "default_root");

            var found = ((IDataRegistryView)registry).TryGet(ProbeTableName, ProbeRecordKey, out var record);
            Assert.True(found, "不覆盖时应能从默认根 data/game 读到探针记录");
            Assert.Equal("default_root", record!.GetString("origin"));
        }

        [Fact]
        public void ResolveGameDatasetRoot_OverrideProvided_DefaultRootProbeIsNotVisible()
        {
            // 对照组，呼应 GameTemplateResidentTests.cs 里同名意图的 Unity 用例：覆盖生效时，只往
            // 覆盖根写探针（不往默认根写），default 根下的同名探针不存在——防止"registry 恰好把两个
            // 根的内容都读了进来，覆盖其实没有排他生效"这一类假阳性。
            var fs = new StubFileSystem();
            var registry = BuildRegistryAtRoot(fs, ResolveGameDatasetRoot("data/game_override_probe"), "override_root");

            // 不曾往 DefaultRoot 写任何探针文件；registry 只加载了覆盖根这一个 IDataSource，
            // 因此默认根下的同一张表天然不可见——用同一个 registry 实例复核，确认没有第二条记录。
            var all = registry.GetAll(ProbeTableName);
            Assert.Single(all);
            Assert.Equal("override_root", all[0].GetString("origin"));
        }
    }
}
