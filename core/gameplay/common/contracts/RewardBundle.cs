using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Expr;

namespace Core.Gameplay.Common
{
    /// <summary>
    /// 奖励包结构（见 08_玩法层_掉落任务对话关卡.md 第 2.1 节 <c>quest.def.rewards</c> 字段：
    /// <c>{items: List&lt;{itemId, count}&gt;, xp: Number, currency: List&lt;{currencyId, amount}&gt;,
    /// skills: List&lt;Id&gt;, world_flags: List&lt;{flagKey, value}&gt;, talent_points: Int}</c>）。
    /// 同一结构被 <c>quest.def.rewards</c>、<c>encounter.def.rewards</c>、<c>achv.def.rewards</c>
    /// 三处复用（见 08 第 4.1、6.1 节"同 quest.def.rewards 结构"），因此本类型放在
    /// <c>core/gameplay/common</c>，供各玩法子模块共同引用，不在每个模块各自重复定义一套等价结构。
    /// </summary>
    public sealed class RewardBundle
    {
        public static readonly RewardBundle Empty = new RewardBundle(
            Array.Empty<ItemStack>(), 0, Array.Empty<(Id, long)>(), Array.Empty<Id>(),
            Array.Empty<(Id, ExprValue)>(), 0);

        public IReadOnlyList<ItemStack> Items { get; }

        public long Xp { get; }

        public IReadOnlyList<(Id CurrencyId, long Amount)> Currency { get; }

        public IReadOnlyList<Id> Skills { get; }

        public IReadOnlyList<(Id FlagKey, ExprValue Value)> WorldFlags { get; }

        public int TalentPoints { get; }

        public RewardBundle(
            IReadOnlyList<ItemStack> items,
            long xp,
            IReadOnlyList<(Id CurrencyId, long Amount)> currency,
            IReadOnlyList<Id> skills,
            IReadOnlyList<(Id FlagKey, ExprValue Value)> worldFlags,
            int talentPoints)
        {
            Items = items ?? throw new ArgumentNullException(nameof(items));
            if (xp < 0) throw new ArgumentOutOfRangeException(nameof(xp), xp, "xp 不能为负数");
            Xp = xp;
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            Skills = skills ?? throw new ArgumentNullException(nameof(skills));
            WorldFlags = worldFlags ?? throw new ArgumentNullException(nameof(worldFlags));
            if (talentPoints < 0) throw new ArgumentOutOfRangeException(nameof(talentPoints), talentPoints, "talentPoints 不能为负数");
            TalentPoints = talentPoints;
        }

        /// <summary>
        /// 是否为空奖励（各字段均为空/零），供调用方跳过"发空奖励事件"一类无意义处理。
        /// </summary>
        public bool IsEmpty =>
            Items.Count == 0 && Xp == 0 && Currency.Count == 0 && Skills.Count == 0 &&
            WorldFlags.Count == 0 && TalentPoints == 0;

        /// <summary>
        /// 从 <c>rewards</c> 字段对应的 <see cref="JsonObject"/>（<c>quest.def</c>/<c>encounter.def</c>/
        /// <c>achv.def</c> 该字段的原始 JSON 值）解析出一个 <see cref="RewardBundle"/>。
        /// <paramref name="record"/> 为 null 或字段全部缺省时返回 <see cref="Empty"/>（08 第 2.1 节
        /// <c>rewards</c> 字段整体标注"否"——非必填）。
        /// </summary>
        public static RewardBundle FromRecord(JsonObject? record)
        {
            if (record == null)
            {
                return Empty;
            }

            var items = ParseItems(record);
            var xp = ParseXp(record);
            var currency = ParseCurrency(record);
            var skills = ParseSkills(record);
            var worldFlags = ParseWorldFlags(record);
            var talentPoints = ParseTalentPoints(record);

            return new RewardBundle(items, xp, currency, skills, worldFlags, talentPoints);
        }

        private static IReadOnlyList<ItemStack> ParseItems(JsonObject record)
        {
            if (!record.TryGetValue("items", out var raw) || !(raw is JsonArray arr))
            {
                return Array.Empty<ItemStack>();
            }

            var result = new List<ItemStack>(arr.Count);
            foreach (var entry in arr)
            {
                if (!(entry is JsonObject o))
                {
                    throw new FormatException("rewards.items 的元素必须是对象 {itemId, count}");
                }

                var itemId = RequireId(o, "itemId", "rewards.items[].itemId");
                var count = (int)RequireInt(o, "count", "rewards.items[].count");
                result.Add(new ItemStack(itemId, count));
            }
            return result;
        }

        private static long ParseXp(JsonObject record)
        {
            if (!record.TryGetValue("xp", out var raw) || raw.Kind == JsonKind.Null)
            {
                return 0;
            }

            if (raw is JsonNumber n)
            {
                return (long)Math.Round(n.Value, MidpointRounding.AwayFromZero);
            }

            throw new FormatException("rewards.xp 必须是数值");
        }

        private static IReadOnlyList<(Id, long)> ParseCurrency(JsonObject record)
        {
            if (!record.TryGetValue("currency", out var raw) || !(raw is JsonArray arr))
            {
                return Array.Empty<(Id, long)>();
            }

            var result = new List<(Id, long)>(arr.Count);
            foreach (var entry in arr)
            {
                if (!(entry is JsonObject o))
                {
                    throw new FormatException("rewards.currency 的元素必须是对象 {currencyId, amount}");
                }

                var currencyId = RequireId(o, "currencyId", "rewards.currency[].currencyId");
                var amount = RequireInt(o, "amount", "rewards.currency[].amount");
                result.Add((currencyId, amount));
            }
            return result;
        }

        private static IReadOnlyList<Id> ParseSkills(JsonObject record)
        {
            if (!record.TryGetValue("skills", out var raw) || !(raw is JsonArray arr))
            {
                return Array.Empty<Id>();
            }

            var result = new List<Id>(arr.Count);
            foreach (var entry in arr)
            {
                if (!(entry is JsonString s) || !Id.TryParse(s.Value, out var id))
                {
                    throw new FormatException("rewards.skills 的元素必须是合法 Id 字符串");
                }
                result.Add(id);
            }
            return result;
        }

        private static IReadOnlyList<(Id, ExprValue)> ParseWorldFlags(JsonObject record)
        {
            if (!record.TryGetValue("world_flags", out var raw) || !(raw is JsonArray arr))
            {
                return Array.Empty<(Id, ExprValue)>();
            }

            var result = new List<(Id, ExprValue)>(arr.Count);
            foreach (var entry in arr)
            {
                if (!(entry is JsonObject o))
                {
                    throw new FormatException("rewards.world_flags 的元素必须是对象 {flagKey, value}");
                }

                var flagKey = RequireId(o, "flagKey", "rewards.world_flags[].flagKey");
                if (!o.TryGetValue("value", out var valueRaw))
                {
                    throw new FormatException("rewards.world_flags[].value 缺失");
                }
                result.Add((flagKey, ExprValueJson.Parse(valueRaw)));
            }
            return result;
        }

        private static int ParseTalentPoints(JsonObject record)
        {
            if (!record.TryGetValue("talent_points", out var raw) || raw.Kind == JsonKind.Null)
            {
                return 0;
            }

            return (int)RequireInt(record, "talent_points", "rewards.talent_points");
        }

        private static Id RequireId(JsonObject o, string field, string path)
        {
            if (o.TryGetValue(field, out var v) && v is JsonString s && Id.TryParse(s.Value, out var id))
            {
                return id;
            }
            throw new FormatException($"{path} 必须是合法 Id 字符串");
        }

        private static long RequireInt(JsonObject o, string field, string path)
        {
            if (o.TryGetValue(field, out var v) && v is JsonNumber n && n.TryGetInt64(out var i))
            {
                return i;
            }
            throw new FormatException($"{path} 必须是整数");
        }
    }
}
