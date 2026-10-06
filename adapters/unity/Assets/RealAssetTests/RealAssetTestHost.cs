#nullable enable
// RealAssetTestHost：真实美术回归用例的公用件——实验室宿主（每个进程一份，数据集只装载一次）与脚本查找。
// 判断记录（ADR-0160）：这些用例依赖框架仓库的目录布局（参考皮肤包、占位皮肤、界面资源契约清单），所以放在工作台工程的 Assets 里，
// 不在任何发布包里；它们直接使用手感实验室可选包的公开接口。
using System;
using FeelLab.Unity;
using Lab;

namespace Framework.RealAssetTests
{
    internal static class RealAssetTestHost
    {
        private static EngineLabHost? _host;

        public static EngineLabHost Host => _host ??= EngineLabHost.Open();

        public static InputScript Script(string id)
        {
            foreach (var script in Host.LoadScripts())
            {
                if (string.Equals(script.Meta.ScriptId, id, StringComparison.Ordinal))
                {
                    return script;
                }
            }

            throw new InvalidOperationException("夹具里没有脚本 " + id);
        }
    }
}
