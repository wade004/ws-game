#nullable enable
// LabHostTestSupport：实验室引擎宿主 PlayMode 测试的公用件——宿主（每个测试类一份，数据集只装载一次）与运行时间控制。
//
// 运行时间控制（环境变量，缺省值保证门禁在分钟级完成）：
//   GF_LAB_CELLS     跨宿主逻辑一致测试覆盖的格子，逗号分隔；"*" 为全部可运行格子；缺省 "2d_action"。
//   GF_LAB_MAX_RUNS  该测试最多跑多少个（脚本，格子）组合，缺省 0 表示不限。
using System;
using System.Collections.Generic;
using Adapter.Unity.LabHost;
using Lab;

namespace Adapter.Unity.Tests.LabHost
{
    internal static class LabHostTestSupport
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

        /// <summary>手感场景脚本（脚本带 <c>feel</c> 声明）。</summary>
        public static List<InputScript> FeelScripts()
        {
            var result = new List<InputScript>();
            foreach (var script in Host.LoadScripts())
            {
                if (script.Meta.Feel)
                {
                    result.Add(script);
                }
            }

            return result;
        }

        public static HashSet<string>? CellFilter()
        {
            var text = Environment.GetEnvironmentVariable("GF_LAB_CELLS");
            if (string.IsNullOrWhiteSpace(text))
            {
                text = "2d_action";
            }

            if (string.Equals(text!.Trim(), "*", StringComparison.Ordinal))
            {
                return null;
            }

            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in text.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    set.Add(trimmed);
                }
            }

            return set;
        }

        public static int MaxRuns()
        {
            var text = Environment.GetEnvironmentVariable("GF_LAB_MAX_RUNS");
            return int.TryParse(text, out var value) && value > 0 ? value : 0;
        }
    }
}
