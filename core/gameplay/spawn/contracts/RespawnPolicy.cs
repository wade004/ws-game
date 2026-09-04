using System;
using System.Collections.Generic;

namespace Core.Gameplay.Spawn
{
    /// <summary>数据表 <c>spawn.table.respawn_policy</c> 的四种取值（见 05_对象模型与世界.md 第 5.2
    /// 节表格行序）。</summary>
    public enum RespawnPolicy
    {
        OnMapEnter,
        Once,
        Never,
        Timer,
    }

    /// <summary><see cref="RespawnPolicy"/> 与数据表 snake_case 文本互转（惯例同
    /// <c>Core.Gameplay.AreaTrigger.AreaTriggerTypeNames</c>）。</summary>
    public static class RespawnPolicyNames
    {
        private static readonly IReadOnlyDictionary<RespawnPolicy, string> ToName = new Dictionary<RespawnPolicy, string>
        {
            [RespawnPolicy.OnMapEnter] = "on_map_enter",
            [RespawnPolicy.Once] = "once",
            [RespawnPolicy.Never] = "never",
            [RespawnPolicy.Timer] = "timer",
        };

        private static readonly IReadOnlyDictionary<string, RespawnPolicy> FromName = BuildReverse(ToName);

        private static Dictionary<string, RespawnPolicy> BuildReverse(IReadOnlyDictionary<RespawnPolicy, string> map)
        {
            var reverse = new Dictionary<string, RespawnPolicy>(StringComparer.Ordinal);
            foreach (var pair in map)
            {
                reverse[pair.Value] = pair.Key;
            }

            return reverse;
        }

        public static string ToText(RespawnPolicy policy) => ToName[policy];

        public static bool TryParse(string text, out RespawnPolicy policy) => FromName.TryGetValue(text, out policy);
    }
}
