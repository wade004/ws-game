using System;
using System.IO;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 消费方反馈第 76 条（[ADR-0054](../../../../architecture/adr/0054-资产数据根目录与目录内固定文件名纳入公开契约.md)）：
    /// 把"从仓库根 + 数据集名推导资产根/数据根目录，以及 <c>--assets-root</c>/<c>--data-root</c>
    /// 省略时的默认值"这条此前只存在于 <c>toolchain/asset_import/common.py</c> 的
    /// <c>resolve_root</c> 函数与各子命令文档字符串里的约定收口为单一公开静态类，与
    /// <see cref="AssetRefConventions"/> 同一惯例——但输入形状不同：本类型全部方法接受"仓库根路径 +
    /// 数据集名/根目录覆盖值"，不接受资源引用 <see cref="Core.Foundation.Common.Id"/>，因此单独
    /// 建类，不并入 <see cref="AssetRefConventions"/>（该类型全部既有方法都以资源引用 id 为输入，
    /// 两者是不同维度的两条约定：本类型回答"资产根/数据根目录本身在哪"，
    /// <see cref="AssetRefConventions"/> 回答"给定一个资源引用 id，它在资产根目录下的哪个相对路径"，
    /// 消费方需要先用本类型定位到 <c>&lt;dataset&gt;</c> 目录，再拼接
    /// <see cref="AssetRefConventions"/> 各方法返回的相对路径才能得到磁盘文件的完整路径）。
    /// <para>
    /// 判断记录（返回值用 <see cref="Path.Combine(string, string)"/> 而不是
    /// <see cref="AssetRefConventions"/> 惯例的正斜杠拼接字符串）：<see cref="AssetRefConventions"/>
    /// 各方法返回的是"资源引用 id 编码出的相对路径空间"，不代表真实文件系统路径，正斜杠拼接是该
    /// 路径空间自身的记法（可能跑在非 Windows 环境，如 CI 校验脚本）；本类型各方法的入参/返回值
    /// 则是真实的文件系统路径（供调用方直接做 <c>File.Exists</c>/<c>Directory.Exists</c> 等操作），
    /// 用 <see cref="Path"/> 系列 API 处理，能正确处理调用方所在平台的路径分隔符与相对/绝对路径
    /// 判定，不比 <c>toolchain/asset_import/common.py</c>（用 <c>pathlib.Path</c>）更弱，也不用
    /// 硬编码分隔符字面量。<see cref="Path.Combine(string, string)"/> 的结果与 <c>pathlib.Path</c>
    /// 的 <c>/</c> 运算符对"第二段为绝对路径时丢弃第一段"这一行为不同（<c>Path.Combine</c> 对绝对
    /// 路径同样会丢弃前一段，语义一致，见 .NET 文档），且本类型的绝对/相对判定发生在调用
    /// <see cref="Path.Combine(string, string)"/> 之前（见 <see cref="ResolveAssetsRoot"/> 判断
    /// 记录），不依赖 <see cref="Path.Combine(string, string)"/> 自身这一冷门行为。
    /// </para>
    /// <para>
    /// 判断记录（Python 侧独立实现，不共享源码，与 <see cref="AssetRefConventions"/> 顶部
    /// "跨语言对照"判断记录同一惯例）：<c>toolchain/asset_import/ref_conventions.py</c> 新增的
    /// <c>resolve_assets_root</c>/<c>resolve_data_root</c>/<c>dataset_assets_directory</c>/
    /// <c>dataset_data_directory</c> 四个函数是本类型四个方法的 Python 对应实现，两侧各自独立
    /// 维护，靠 <c>toolchain/tests/test_ref_conventions.py</c> 与本类型对应的 C# 单元测试
    /// （<c>AssetRootConventionsTests.cs</c>）用同一组输入互相对照，另有跨语言一致性测试
    /// （经 <c>toolchain/map_ref_probe</c> 子进程取得 C# 真实运行期结果）与 Python 侧断言比对。
    /// <c>toolchain/asset_import/common.py</c> 原有的 <c>resolve_root(value, repo_root,
    /// default_name)</c> 改为对 <c>default_name in {"assets", "data"}</c> 两种已知取值委托给
    /// <c>ref_conventions.resolve_assets_root</c>/<c>resolve_data_root</c>，其余取值（当前六个
    /// 子命令调用点均未使用）保留原有内联逻辑不变，不引入行为变化。
    /// </para>
    /// </summary>
    public static class AssetRootConventions
    {
        /// <summary>
        /// 把 <c>--assets-root</c> 命令行参数解析为绝对（或调用方传入 <paramref name="repoRoot"/>
        /// 本身的相对形态）资产根目录路径。对应
        /// <c>toolchain/asset_import/common.py</c> <c>resolve_root(value, repo_root, "assets")</c>：
        /// 省略（<paramref name="assetsRootOverride"/> 为 <c>null</c>）时默认
        /// <c>&lt;repoRoot&gt;/assets</c>；传入已是"完全限定"路径（<see cref="Path.IsPathFullyQualified(string)"/>，
        /// 与 Python <c>pathlib.Path.is_absolute()</c> 同一判定标准——均要求 Windows 下带盘符，
        /// 不满足于形如 <c>"/foo"</c> 这种"根相对"写法）时原样返回；否则按
        /// <paramref name="repoRoot"/> 解析。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="repoRoot"/> 为 <c>null</c>。</exception>
        public static string ResolveAssetsRoot(string repoRoot, string? assetsRootOverride) =>
            ResolveRoot(repoRoot, assetsRootOverride, "assets");

        /// <summary>
        /// 把 <c>--data-root</c> 命令行参数解析为数据根目录路径，规则与 <see cref="ResolveAssetsRoot"/>
        /// 完全一致（对应 <c>resolve_root(value, repo_root, "data")</c>），仅默认段名不同：
        /// <c>&lt;repoRoot&gt;/data</c>。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="repoRoot"/> 为 <c>null</c>。</exception>
        public static string ResolveDataRoot(string repoRoot, string? dataRootOverride) =>
            ResolveRoot(repoRoot, dataRootOverride, "data");

        private static string ResolveRoot(string repoRoot, string? overrideValue, string defaultSegmentName)
        {
            if (repoRoot == null) throw new ArgumentNullException(nameof(repoRoot));
            if (overrideValue == null) return Path.Combine(repoRoot, defaultSegmentName);
            return Path.IsPathFullyQualified(overrideValue) ? overrideValue : Path.Combine(repoRoot, overrideValue);
        }

        /// <summary>
        /// 给定 <see cref="ResolveAssetsRoot"/> 算出的资产根目录与数据集名，返回该数据集的资产目录
        /// （<c>&lt;assetsRoot&gt;/&lt;dataset&gt;</c>）——<see cref="AssetRefConventions"/> 各方法
        /// 返回的相对路径均以此目录为基准拼接（消费方反馈第 75 条答复文档"dataset 段由谁拼接"一节：
        /// "调用方自行拼接 <c>assets/&lt;dataset&gt;/</c> 前缀"，本方法把这一步也收口为公开契约，
        /// 调用方不必再自行拼接分隔符）。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="assetsRoot"/> 或
        /// <paramref name="dataset"/> 为 <c>null</c>。</exception>
        public static string DatasetAssetsDirectory(string assetsRoot, string dataset)
        {
            if (assetsRoot == null) throw new ArgumentNullException(nameof(assetsRoot));
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            return Path.Combine(assetsRoot, dataset);
        }

        /// <summary>
        /// 给定 <see cref="ResolveDataRoot"/> 算出的数据根目录与数据集名，返回该数据集的数据目录
        /// （<c>&lt;dataRoot&gt;/&lt;dataset&gt;</c>），与 <see cref="DatasetAssetsDirectory"/> 同一
        /// 惯例。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="dataRoot"/> 或
        /// <paramref name="dataset"/> 为 <c>null</c>。</exception>
        public static string DatasetDataDirectory(string dataRoot, string dataset)
        {
            if (dataRoot == null) throw new ArgumentNullException(nameof(dataRoot));
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            return Path.Combine(dataRoot, dataset);
        }
    }
}
