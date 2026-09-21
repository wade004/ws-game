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
    /// <para>
    /// 判断记录（消费方反馈第 79 条，ADR-0054"决策 1"纳入同一契约范围）：新增
    /// <see cref="TryGetDatasetName"/> 是 <see cref="DatasetDataDirectory"/> 的逆运算——消费方从
    /// "游戏仓库根 + 已探测到的数据集数据目录"定位资产文件时，此前"数据目录 → 数据集名"这一步没有
    /// 公开出处，只能自行切路径末段。边界口径（不在 <paramref name="dataRoot"/> 之下/是其本身/更
    /// 深层子目录如何表达、分隔符与大小写如何规整）见该方法 XML 注释；Python 侧同名逆运算
    /// <c>ref_conventions.try_get_dataset_name</c> 与之逐条对应、独立实现，跨语言一致性测试同上
    /// 一段"跨语言对照"判断记录所述机制，经 <c>toolchain/asset_root_probe</c> 扩充覆盖。
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

        /// <summary>
        /// <see cref="DatasetDataDirectory"/> 的逆运算（消费方反馈第 79 条，ADR-0054"决策 1"纳入
        /// 同一契约范围）：给定 <see cref="ResolveDataRoot"/> 算出的数据根目录，与磁盘上已经探测到
        /// 的一个目录路径，尝试还原出该目录对应的数据集名——消费方从"游戏仓库根 + 已探测到的数据
        /// 目录"定位资产文件时，中间这一步此前只能自行切路径末段。满足往返恒等式：对任意不含路径
        /// 分隔符的合法数据集名 <c>x</c>，
        /// <c>TryGetDatasetName(dataRoot, DatasetDataDirectory(dataRoot, x), out var y) &amp;&amp;
        /// y == x</c>。
        /// <para>
        /// 判断记录（边界口径，逐条对应消费方反馈原文"路径比较口径请框架定"）：
        /// </para>
        /// <list type="bullet">
        /// <item><description><b>不是 <paramref name="dataRoot"/> 的直接子目录</b>——更深层级
        /// （如 <c>&lt;dataRoot&gt;/a/b</c>）、不在 <paramref name="dataRoot"/> 之下、或就是
        /// <paramref name="dataRoot"/> 本身——均返回 <c>false</c>，不猜测、不编造数据集名：这类
        /// "答不上来"的输入用返回值表达，不抛异常，同本类型新增之前 <see cref="AssetRefConventions"/>
        /// 已有的 <c>TryParseSpriteSetId</c>/<c>TryParseIconId</c> 惯例（"解析失败返回
        /// <c>false</c>/<c>null</c>，不用异常表达业务上可预期的失败"）。</description></item>
        /// <item><description><b>路径分隔符 <c>/</c> 与 <c>\</c>、结尾多余的分隔符</b>：比较前把
        /// <c>/</c> 归一化为 <see cref="Path.DirectorySeparatorChar"/>、去掉末尾多余的分隔符，两侧
        /// 输入混用分隔符或结尾带斜杠不影响判定结果。不使用 <see cref="Path.GetFullPath(string)"/>
        /// 做归一化——该重载依赖当前工作目录（<c>Environment.CurrentDirectory</c>），与仓库既有代码
        /// 规则"保持确定性：不依赖系统时间/当前文化等运行环境状态"冲突，netstandard2.1 也没有
        /// "显式传入基准目录"的等价重载可用而不引入该依赖；因此改用不依赖环境状态的纯字符串
        /// 前缀/后缀处理。</description></item>
        /// <item><description><b><c>.</c>/<c>..</c> 路径段</b>：不做任何语义解析（原因同上，且
        /// <see cref="DatasetDataDirectory"/> 本身也只是 <see cref="Path.Combine(string, string)"/>
        /// 纯字符串拼接，不校验、不规整 <c>.</c>/<c>..</c> 段——正向方法都没有为这类输入定义行为，
        /// 逆运算不借机新增正向没有的语义）；含这类段的输入按字面字符比较，可能得到"技术上不出错但
        /// 语义上无意义"的结果，不特殊处理。真实来源（对目录做遍历取得的已存在路径）不会产生这类
        /// 段，不影响验收口径覆盖的实际场景。</description></item>
        /// <item><description><b>相对路径 vs 绝对路径</b>：不做绝对化处理，只按字面字符串比较——
        /// 调用方须保证 <paramref name="dataRoot"/> 与 <paramref name="datasetDataDirectory"/>
        /// 处于同一相对/绝对表示下（典型来源都经同一次 <see cref="ResolveDataRoot"/> 调用得到，
        /// 天然满足；混用两种表示属于调用方用法错误，返回 <c>false</c>，不视为框架缺陷）。
        /// </description></item>
        /// <item><description><b>大小写</b>：Windows 文件系统大小写不敏感，
        /// <paramref name="dataRoot"/> 与 <paramref name="datasetDataDirectory"/> 的父目录段按
        /// <see cref="StringComparison.OrdinalIgnoreCase"/>（不用文化相关比较，保持跨环境确定性）
        /// 比较是否指向同一目录；但还原出的数据集名取自 <paramref name="datasetDataDirectory"/>
        /// 最后一段的**原样字符**（调用方观测到的磁盘目录名字面量），不对大小写做任何变换、也不取
        /// 自 <paramref name="dataRoot"/> 或任何"标准"大小写——数据集名是内容标识，大小写以调用方
        /// 实际观测到的为准。</description></item>
        /// <item><description><b>合法数据集名的假定</b>：假定数据集名本身不含路径分隔符（与目录名
        /// 惯例一致，<see cref="DatasetDataDirectory"/> 对此也未做任何校验）；数据集名含分隔符属于
        /// 对 <see cref="DatasetDataDirectory"/> 的非法用法，其逆运算行为不在本方法契约范围内。
        /// </description></item>
        /// <item><description><b>空参数</b>：<paramref name="dataRoot"/> 为 <c>null</c> 时抛出
        /// <see cref="ArgumentNullException"/>（与本类型其余方法对"根目录"参数的既有约定一致——
        /// 根目录是调用方必须持有的配置值，缺失属于调用方编程错误）；
        /// <paramref name="datasetDataDirectory"/> 为 <c>null</c>/空字符串时返回 <c>false</c>
        /// （与 <see cref="AssetRefConventions"/> 既有 <c>TryXxx</c> 方法对"被探测的观测值"参数的
        /// 既有约定一致——该参数来自调用方对磁盘的探测结果，缺失/未知本身就是一种合法输入，不应
        /// 强迫调用方另行判空）。</description></item>
        /// </list>
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="dataRoot"/> 为 <c>null</c>。</exception>
        public static bool TryGetDatasetName(string dataRoot, string datasetDataDirectory, out string dataset)
        {
            dataset = string.Empty;
            if (dataRoot == null) throw new ArgumentNullException(nameof(dataRoot));
            if (string.IsNullOrEmpty(datasetDataDirectory))
            {
                return false;
            }

            var normalizedRoot = TrimTrailingSeparator(NormalizeSeparators(dataRoot));
            var normalizedDir = TrimTrailingSeparator(NormalizeSeparators(datasetDataDirectory));

            var lastSeparator = normalizedDir.LastIndexOf(Path.DirectorySeparatorChar);
            string parent;
            string name;
            if (lastSeparator < 0)
            {
                parent = string.Empty;
                name = normalizedDir;
            }
            else if (lastSeparator == 0)
            {
                // POSIX 风格根分隔符本身作为父目录（如 "/sample" 的父目录是 "/"），罕见场景（本框架
                // 数据根从不是文件系统根本身），仅为不崩溃、行为可预期，不特别测试覆盖。
                parent = normalizedDir.Substring(0, 1);
                name = normalizedDir.Substring(1);
            }
            else
            {
                parent = normalizedDir.Substring(0, lastSeparator);
                name = normalizedDir.Substring(lastSeparator + 1);
            }

            if (name.Length == 0)
            {
                return false;
            }

            if (!string.Equals(parent, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            dataset = name;
            return true;
        }

        /// <summary>把 <c>/</c> 归一化为 <see cref="Path.DirectorySeparatorChar"/>（Windows 下即
        /// <c>\</c>；非 Windows 平台 <see cref="Path.DirectorySeparatorChar"/> 本身就是 <c>/</c>，
        /// 该替换是无操作，<c>\</c> 按字面字符处理，不当作分隔符——与该平台的路径语义一致）。</summary>
        private static string NormalizeSeparators(string value) => value.Replace('/', Path.DirectorySeparatorChar);

        /// <summary>去掉末尾多余的目录分隔符（可能不止一个）；保留长度为 1 的字符串（如单独的盘符
        /// 分隔符）不再继续裁剪，避免把 <c>"C:\"</c> 之类的根路径裁成语义不同的 <c>"C:"</c>——这一
        /// 極端情形不在本框架真实数据根的取值范围内（数据根恒为 <c>&lt;仓库根&gt;/data</c> 或调用方
        /// 显式覆盖值，从不是裸盘符根），不专门测试覆盖。</summary>
        private static string TrimTrailingSeparator(string value)
        {
            while (value.Length > 1 && value[value.Length - 1] == Path.DirectorySeparatorChar)
            {
                value = value.Substring(0, value.Length - 1);
            }
            return value;
        }
    }
}
