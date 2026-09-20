using System;
using Core.Foundation.EngineAdapter;

namespace Toolchain.AssetRootProbe
{
    /// <summary>
    /// 见 AssetRootProbe.csproj 头注释：消费方反馈第 76 条（ADR-0054）跨语言一致性测试专用探针，
    /// 不对外发行。接收 4 个命令行参数：<c>repoRoot</c>、<c>assetsRootOverride</c>（空字符串表示
    /// 未传，对应 Python 侧 <c>None</c>）、<c>dataRootOverride</c>（同上）、<c>dataset</c>，把
    /// <see cref="AssetRootConventions"/> 四个方法的真实计算结果打印为一行 JSON（手写拼接，字段值
    /// 只含路径字符，不需要转义；Windows 路径分隔符 <c>\</c> 按 JSON 字符串转义规则处理）。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 4)
            {
                Console.Error.WriteLine(
                    "用法: AssetRootProbe <repoRoot> <assetsRootOverride|空字符串> <dataRootOverride|空字符串> <dataset>");
                return 1;
            }

            try
            {
                var repoRoot = args[0];
                string? assetsRootOverride = args[1].Length == 0 ? null : args[1];
                string? dataRootOverride = args[2].Length == 0 ? null : args[2];
                var dataset = args[3];

                var assetsRoot = AssetRootConventions.ResolveAssetsRoot(repoRoot, assetsRootOverride);
                var dataRoot = AssetRootConventions.ResolveDataRoot(repoRoot, dataRootOverride);
                var datasetAssetsDir = AssetRootConventions.DatasetAssetsDirectory(assetsRoot, dataset);
                var datasetDataDir = AssetRootConventions.DatasetDataDirectory(dataRoot, dataset);

                Console.WriteLine(
                    "{"
                    + $"\"assets_root\":\"{JsonEscape(assetsRoot)}\","
                    + $"\"data_root\":\"{JsonEscape(dataRoot)}\","
                    + $"\"dataset_assets_dir\":\"{JsonEscape(datasetAssetsDir)}\","
                    + $"\"dataset_data_dir\":\"{JsonEscape(datasetDataDir)}\""
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
