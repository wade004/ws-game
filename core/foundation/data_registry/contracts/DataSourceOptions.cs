namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// <see cref="FileSystemDataSource"/> 的可选构造项。
    /// <para>
    /// 判断记录（数据根非数据表 JSON 误判修复任务，2026-09-16）：<see cref="FileSystemDataSource.ListTables"/>
    /// 此前对 <c>rootDir</c> 下递归找到的每个 <c>*.json</c> 文件一律当成数据表候选——这在数据根目录
    /// 本身干净（只含 <c>&lt;域&gt;/&lt;域&gt;.&lt;表名&gt;.json</c> 这一种文件）时没问题，但游戏侧仓库
    /// 常见"数据目录旁边/上层还有 <c>package.json</c> 之类配置文件"的布局（如 UPM 包根目录），一旦
    /// <c>--data-root</c> 指向的目录把这些文件也递归进去，会被误判成表名 <c>"package"</c> 的数据表，
    /// 报出一堆"缺少顶层字段 table/schema_version/rows"的假错误（见
    /// <c>toolchain/validate_data.py</c> 同名判断记录、<c>toolchain/tests/test_validate_data_skips_nontable_json.py</c>
    /// 复现用例）。<see cref="SkipNonTableJsonFiles"/>（默认 <c>true</c>）开启后，
    /// <see cref="FileSystemDataSource.ListTables"/> 按 04 第 2.2 节"域"约定的最小规则（见该类型判断
    /// 记录）筛掉不像数据表的 JSON 文件，不再把它们当表交给 <c>DataRegistry</c>。
    /// </para>
    /// <para>
    /// ABI 判断记录：本类型与 <see cref="FileSystemDataSource"/> 新增的三参数构造函数都是纯新增——
    /// 原有 <c>FileSystemDataSource(IFileSystem, string)</c> 两参构造函数签名不变（内部委派给三参数
    /// 版本、默认 <see cref="SkipNonTableJsonFiles"/> = <c>true</c>），不破坏任何已编译消费方的二进制
    /// 兼容性；需要恢复"递归目录下全部 JSON 都当表"旧行为的调用方，显式传入
    /// <c>new DataSourceOptions { SkipNonTableJsonFiles = false }</c> 即可。
    /// </para>
    /// </summary>
    public sealed class DataSourceOptions
    {
        /// <summary>是否跳过"看起来不是数据表"的 JSON 文件（见类型判断记录）。默认 <c>true</c>——
        /// 游戏运行时（<c>Adapter.Unity</c> 的 <c>GameFoundationBootstrap</c>/<c>FrameworkResidentHost</c>
        /// 均用两参构造函数，走本默认值）与内容工具（<c>toolchain/validator</c>）都受益于同一个默认
        /// 开启的过滤，不需要分别接线。</summary>
        public bool SkipNonTableJsonFiles { get; set; } = true;
    }
}
