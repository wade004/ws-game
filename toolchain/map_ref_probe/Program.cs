using System;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Toolchain.MapRefProbe
{
    /// <summary>
    /// 见 MapRefProbe.csproj 头注释：消费方反馈第 75 条（ADR-0053）跨语言一致性测试专用探针，
    /// 不对外发行。接收一个 world.map 行 id（如 "world.sample_field"）作为唯一命令行参数，把
    /// <see cref="AssetRefConventions"/> 新增的 5 个地图分层路径方法的真实计算结果打印为一行 JSON
    /// （手写拼接，未引入 System.Text.Json 之外的依赖——字段值只含路径字符（小写字母/数字/下划线/
    /// 斜杠/点号），不需要转义）。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("用法: MapRefProbe <mapId>（如 world.sample_field）");
                return 1;
            }

            try
            {
                var mapId = new Id(args[0]);
                var directory = AssetRefConventions.MapDirectory(mapId);
                var ground = AssetRefConventions.MapGroundFile(mapId);
                var overlay = AssetRefConventions.MapOverlayFile(mapId);
                var decal = AssetRefConventions.MapDecalFile(mapId);
                var navHint = AssetRefConventions.MapNavHintFile(mapId);

                Console.WriteLine(
                    "{"
                    + $"\"directory\":\"{directory}\","
                    + $"\"ground\":\"{ground}\","
                    + $"\"overlay\":\"{overlay}\","
                    + $"\"decal\":\"{decal}\","
                    + $"\"nav_hint\":\"{navHint}\""
                    + "}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }
}
