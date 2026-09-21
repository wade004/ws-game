using System;
using System.IO;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.Foundation.EngineAdapter
{
    /// <summary>
    /// 消费方反馈第 76 条（ADR-0054）验收测试：<see cref="AssetRootConventions"/> 的资产/数据根目录
    /// 推导。
    /// <para>
    /// 判断记录（跨语言对照）：本文件与 <c>toolchain/tests/test_ref_conventions.py</c> 使用同一组
    /// 样例，两侧各自独立实现同一条规则、断言同一个期望路径；两侧是否实际算出同一个值另有一组更强
    /// 的跨语言一致性测试，见 <c>test_ref_conventions.py</c> 里经 <c>toolchain/asset_root_probe</c>
    /// 子进程对照的用例（本文件所在语言运行时无法直接调用 Python，不在本文件内重复该项）。
    /// </para>
    /// </summary>
    public class AssetRootConventionsTests
    {
        [Fact]
        public void ResolveAssetsRoot_OverrideOmitted_DefaultsToRepoRootAssets()
        {
            var repoRoot = Path.Combine("D:", "repo");
            Assert.Equal(Path.Combine(repoRoot, "assets"), AssetRootConventions.ResolveAssetsRoot(repoRoot, null));
        }

        [Fact]
        public void ResolveAssetsRoot_RelativeOverride_ResolvesAgainstRepoRoot()
        {
            var repoRoot = Path.Combine("D:", "repo");
            Assert.Equal(
                Path.Combine(repoRoot, "custom_assets"),
                AssetRootConventions.ResolveAssetsRoot(repoRoot, "custom_assets"));
        }

        [Fact]
        public void ResolveAssetsRoot_AbsoluteOverride_UsedAsIs()
        {
            var repoRoot = Path.Combine("D:", "repo");
            var absoluteOverride = Path.Combine("C:", "abs", "assets");
            Assert.Equal(absoluteOverride, AssetRootConventions.ResolveAssetsRoot(repoRoot, absoluteOverride));
        }

        [Fact]
        public void ResolveDataRoot_OverrideOmitted_DefaultsToRepoRootData()
        {
            var repoRoot = Path.Combine("D:", "repo");
            Assert.Equal(Path.Combine(repoRoot, "data"), AssetRootConventions.ResolveDataRoot(repoRoot, null));
        }

        [Fact]
        public void ResolveDataRoot_RelativeOverride_ResolvesAgainstRepoRoot()
        {
            var repoRoot = Path.Combine("D:", "repo");
            Assert.Equal(
                Path.Combine(repoRoot, "custom_data"),
                AssetRootConventions.ResolveDataRoot(repoRoot, "custom_data"));
        }

        [Fact]
        public void ResolveDataRoot_AbsoluteOverride_UsedAsIs()
        {
            var repoRoot = Path.Combine("D:", "repo");
            var absoluteOverride = Path.Combine("C:", "abs", "data");
            Assert.Equal(absoluteOverride, AssetRootConventions.ResolveDataRoot(repoRoot, absoluteOverride));
        }

        [Fact]
        public void ResolveAssetsRoot_NullRepoRoot_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => AssetRootConventions.ResolveAssetsRoot(null!, null));
        }

        [Fact]
        public void ResolveDataRoot_NullRepoRoot_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => AssetRootConventions.ResolveDataRoot(null!, null));
        }

        [Fact]
        public void DatasetAssetsDirectory_AppendsDatasetSegment()
        {
            var assetsRoot = Path.Combine("D:", "repo", "assets");
            Assert.Equal(
                Path.Combine(assetsRoot, "_sample"),
                AssetRootConventions.DatasetAssetsDirectory(assetsRoot, "_sample"));
        }

        [Fact]
        public void DatasetDataDirectory_AppendsDatasetSegment()
        {
            var dataRoot = Path.Combine("D:", "repo", "data");
            Assert.Equal(
                Path.Combine(dataRoot, "_sample"),
                AssetRootConventions.DatasetDataDirectory(dataRoot, "_sample"));
        }

        [Fact]
        public void DatasetAssetsDirectory_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => AssetRootConventions.DatasetAssetsDirectory(null!, "_sample"));
            Assert.Throws<ArgumentNullException>(() => AssetRootConventions.DatasetAssetsDirectory("D:\\repo\\assets", null!));
        }

        [Fact]
        public void DatasetDataDirectory_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => AssetRootConventions.DatasetDataDirectory(null!, "_sample"));
            Assert.Throws<ArgumentNullException>(() => AssetRootConventions.DatasetDataDirectory("D:\\repo\\data", null!));
        }

        [Fact]
        public void FullChain_RepoRootAndDatasetToAssetsDirectory_MatchesManualConcatenation()
        {
            // 验收标准：从"游戏仓库根 + 数据集名"到磁盘目录的全过程只调用框架公开方法。
            var repoRoot = Path.Combine("D:", "repo");
            var assetsRoot = AssetRootConventions.ResolveAssetsRoot(repoRoot, null);
            var datasetDir = AssetRootConventions.DatasetAssetsDirectory(assetsRoot, "_sample");
            Assert.Equal(Path.Combine(repoRoot, "assets", "_sample"), datasetDir);
        }

        // --- 消费方反馈第 79 条（ADR-0054）：TryGetDatasetName 是 DatasetDataDirectory 的逆运算。
        // 三类测试：往返（round-trip）、边界口径、（跨语言一致性另见
        // toolchain/tests/test_ref_conventions.py 经 toolchain/asset_root_probe 子进程对照）。---

        [Theory]
        [InlineData("_sample")]
        [InlineData("_framework")]
        [InlineData("dataset_with_underscores")]
        [InlineData("dataset123")]
        public void TryGetDatasetName_RoundTrip_DefaultDataRoot_RecoversOriginalDatasetName(string dataset)
        {
            var dataRoot = AssetRootConventions.ResolveDataRoot(Path.Combine("D:", "repo"), null);
            var dir = AssetRootConventions.DatasetDataDirectory(dataRoot, dataset);

            Assert.True(AssetRootConventions.TryGetDatasetName(dataRoot, dir, out var recovered));
            Assert.Equal(dataset, recovered);
        }

        [Theory]
        [InlineData("_sample")]
        [InlineData("dataset_with_underscores")]
        public void TryGetDatasetName_RoundTrip_OverrideDataRoot_RecoversOriginalDatasetName(string dataset)
        {
            var repoRoot = Path.Combine("D:", "repo");
            var dataRoot = AssetRootConventions.ResolveDataRoot(repoRoot, "custom_data");
            var dir = AssetRootConventions.DatasetDataDirectory(dataRoot, dataset);

            Assert.True(AssetRootConventions.TryGetDatasetName(dataRoot, dir, out var recovered));
            Assert.Equal(dataset, recovered);
        }

        [Fact]
        public void TryGetDatasetName_RoundTrip_AbsoluteOverrideDataRoot_RecoversOriginalDatasetName()
        {
            var repoRoot = Path.Combine("D:", "repo");
            var dataRoot = AssetRootConventions.ResolveDataRoot(repoRoot, Path.Combine("C:", "abs", "data"));
            var dir = AssetRootConventions.DatasetDataDirectory(dataRoot, "_sample");

            Assert.True(AssetRootConventions.TryGetDatasetName(dataRoot, dir, out var recovered));
            Assert.Equal("_sample", recovered);
        }

        [Fact]
        public void TryGetDatasetName_DirectoryDeeperThanDirectChild_ReturnsFalse()
        {
            var dataRoot = Path.Combine("D:", "repo", "data");
            var deeper = Path.Combine(dataRoot, "_sample", "inner");

            Assert.False(AssetRootConventions.TryGetDatasetName(dataRoot, deeper, out var dataset));
            Assert.Equal(string.Empty, dataset);
        }

        [Fact]
        public void TryGetDatasetName_DirectoryIsDataRootItself_ReturnsFalse()
        {
            var dataRoot = Path.Combine("D:", "repo", "data");

            Assert.False(AssetRootConventions.TryGetDatasetName(dataRoot, dataRoot, out var dataset));
            Assert.Equal(string.Empty, dataset);
        }

        [Fact]
        public void TryGetDatasetName_DirectoryOutsideDataRoot_ReturnsFalse()
        {
            var dataRoot = Path.Combine("D:", "repo", "data");
            var outside = Path.Combine("D:", "other", "thing");

            Assert.False(AssetRootConventions.TryGetDatasetName(dataRoot, outside, out var dataset));
            Assert.Equal(string.Empty, dataset);
        }

        [Fact]
        public void TryGetDatasetName_MixedSeparators_StillMatches()
        {
            var dataRoot = Path.Combine("D:", "repo", "data");
            // 数据根用反斜杠、待判定目录用正斜杠——分隔符风格不一致不影响判定。
            var dirWithForwardSlashes = dataRoot.Replace('\\', '/') + "/_sample";

            Assert.True(AssetRootConventions.TryGetDatasetName(dataRoot, dirWithForwardSlashes, out var dataset));
            Assert.Equal("_sample", dataset);
        }

        [Fact]
        public void TryGetDatasetName_TrailingSeparator_StillMatches()
        {
            var dataRoot = Path.Combine("D:", "repo", "data");
            var dir = Path.Combine(dataRoot, "_sample") + Path.DirectorySeparatorChar;

            Assert.True(AssetRootConventions.TryGetDatasetName(dataRoot, dir, out var dataset));
            Assert.Equal("_sample", dataset);
        }

        [Fact]
        public void TryGetDatasetName_DataRootCaseDiffersFromObservedDirectory_MatchesCaseInsensitively()
        {
            // Windows 文件系统大小写不敏感：dataRoot 与 datasetDataDirectory 父目录段大小写不同
            // 时仍应判定为同一目录。
            var dataRoot = Path.Combine("D:", "Repo", "Data");
            var dir = Path.Combine("d:", "repo", "data", "_Sample");

            Assert.True(AssetRootConventions.TryGetDatasetName(dataRoot, dir, out var dataset));
            Assert.Equal("_Sample", dataset);
        }

        [Fact]
        public void TryGetDatasetName_ObservedDirectoryCasingIsPreservedVerbatim()
        {
            // 还原出的数据集名取自 datasetDataDirectory 最后一段的原样字符，不做任何大小写变换、
            // 也不取自正向方法本来传入的原始大小写。
            var dataRoot = Path.Combine("D:", "repo", "data");
            var forwardDir = AssetRootConventions.DatasetDataDirectory(dataRoot, "_Sample");
            var observedWithDifferentCase = Path.Combine(dataRoot, "_sAmple");

            Assert.True(AssetRootConventions.TryGetDatasetName(dataRoot, forwardDir, out var recoveredOriginalCase));
            Assert.Equal("_Sample", recoveredOriginalCase);

            Assert.True(
                AssetRootConventions.TryGetDatasetName(dataRoot, observedWithDifferentCase, out var recoveredObservedCase));
            Assert.Equal("_sAmple", recoveredObservedCase);
        }

        [Fact]
        public void TryGetDatasetName_NullDataRoot_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => AssetRootConventions.TryGetDatasetName(null!, Path.Combine("D:", "repo", "data", "_sample"), out _));
        }

        [Fact]
        public void TryGetDatasetName_NullOrEmptyDatasetDataDirectory_ReturnsFalse()
        {
            var dataRoot = Path.Combine("D:", "repo", "data");

            Assert.False(AssetRootConventions.TryGetDatasetName(dataRoot, null!, out var dataset1));
            Assert.Equal(string.Empty, dataset1);

            Assert.False(AssetRootConventions.TryGetDatasetName(dataRoot, string.Empty, out var dataset2));
            Assert.Equal(string.Empty, dataset2);
        }
    }
}
