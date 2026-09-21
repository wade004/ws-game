using System;
using Core.Foundation.EngineAdapter;

namespace Toolchain.AssetRootProbe
{
    /// <summary>
    /// 见 AssetRootProbe.csproj 头注释：消费方反馈第 76/79 条（ADR-0054）跨语言一致性测试专用探针，
    /// 不对外发行。接收 4 或 5 个命令行参数：<c>repoRoot</c>、<c>assetsRootOverride</c>（空字符串
    /// 表示未传，对应 Python 侧 <c>None</c>）、<c>dataRootOverride</c>（同上）、<c>dataset</c>、
    /// 可选第 5 个 <c>inverseProbeDirectory</c>（消费方反馈第 79 条 <see cref="AssetRootConventions.
    /// TryGetDatasetName"/> 逆运算探针用；省略或空字符串表示"用本次刚算出的 <c>dataset_data_dir</c>
    /// 做往返探测"，传非空值则改用该值本身去调用 <c>TryGetDatasetName</c>——用于探测跳出往返路径的
    /// 边界输入，如更深子目录、数据根之外的路径、大小写/分隔符差异等），把
    /// <see cref="AssetRootConventions"/> 全部方法（含新增的 <c>TryGetDatasetName</c>）的真实计算
    /// 结果打印为一行 JSON（手写拼接，字段值只含路径字符，不需要转义；Windows 路径分隔符 <c>\</c>
    /// 按 JSON 字符串转义规则处理）。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 4 && args.Length != 5)
            {
                Console.Error.WriteLine(
                    "用法: AssetRootProbe <repoRoot> <assetsRootOverride|空字符串> <dataRootOverride|空字符串> "
                    + "<dataset> [inverseProbeDirectory|空字符串]");
                return 1;
            }

            try
            {
                var repoRoot = args[0];
                string? assetsRootOverride = args[1].Length == 0 ? null : args[1];
                string? dataRootOverride = args[2].Length == 0 ? null : args[2];
                var dataset = args[3];
                string? inverseProbeDirectory = args.Length == 5 && args[4].Length > 0 ? args[4] : null;

                var assetsRoot = AssetRootConventions.ResolveAssetsRoot(repoRoot, assetsRootOverride);
                var dataRoot = AssetRootConventions.ResolveDataRoot(repoRoot, dataRootOverride);
                var datasetAssetsDir = AssetRootConventions.DatasetAssetsDirectory(assetsRoot, dataset);
                var datasetDataDir = AssetRootConventions.DatasetDataDirectory(dataRoot, dataset);

                var inverseInput = inverseProbeDirectory ?? datasetDataDir;
                var inverseOk = AssetRootConventions.TryGetDatasetName(dataRoot, inverseInput, out var inverseDataset);

                Console.WriteLine(
                    "{"
                    + $"\"assets_root\":\"{JsonEscape(assetsRoot)}\","
                    + $"\"data_root\":\"{JsonEscape(dataRoot)}\","
                    + $"\"dataset_assets_dir\":\"{JsonEscape(datasetAssetsDir)}\","
                    + $"\"dataset_data_dir\":\"{JsonEscape(datasetDataDir)}\","
                    + $"\"inverse_ok\":{(inverseOk ? "true" : "false")},"
                    + $"\"inverse_dataset\":\"{JsonEscape(inverseDataset)}\""
                    + "}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static string JsonEscape(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
