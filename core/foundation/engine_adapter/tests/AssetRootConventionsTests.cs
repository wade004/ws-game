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
    }
}
