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

        /// <summary>T-N4-4 新增（ADR-0033 决策 3"任务定义的经验奖励字段写当量不写绝对数"；08 第
        /// 2.1 节 2026-09-14 修订段）：经验当量（同级怪当量 N）——非 <c>null</c> 时
        /// <see cref="Core.Gameplay.Common.RewardDispatcher"/> 改经
        /// <see cref="Core.Numbers.Progression.IProgressionHost.GrantXp"/> 折算发放（<c>硬性规则
        /// "禁止奖励直发绝对数"</c> 对新写法的要求），取代旧字段 <see cref="Xp"/> 的绝对数直发路径；
        /// <c>null</c>（默认）时沿用旧字段（兼容读取，见类型/字段判断记录）。缺省 <c>null</c>，
        /// 不能为负数（构造期硬约束，业务判断见各表 ContentValidationRule）。</summary>
        public double? XpEquivalent { get; }

        /// <summary>T-N4-4 新增：<see cref="XpEquivalent"/> 折算所需的"任务/遭遇等级"（ADR-0033
        /// 决策 3"任务 = 当量 N × 击杀基数(任务等级) × 经验系数(Δ)"的"任务等级"一项）——
        /// <c>quest.def</c>/<c>encounter.def</c> 本身未登记等级字段（08 未给出该字段挂在哪张表，
        /// 契约疑点上报，见 <see cref="FromRecord"/> 判断记录"reward_level 挂载点"），本任务落地
        /// 为挂在 <c>rewards</c> 子结构自身（<c>rewards.level</c>）：同一个奖励包自带折算所需的
        /// 全部输入，不需要额外反查所属任务/遭遇定义。<see cref="XpEquivalent"/> 非
        /// <c>null</c> 而本字段为 <c>null</c>（内容作者遗漏）时，<see cref="RewardDispatcher"/>
        /// 退化取 1（同 <c>discovery</c> 来源"等级未知按 1 级同级怪当量"最小假设，避免因缺字段抛
        /// 异常整批奖励失败），不能为负数（构造期硬约束）。</summary>
        public int? RewardLevel { get; }

        public RewardBundle(
            IReadOnlyList<ItemStack> items,
            long xp,
            IReadOnlyList<(Id CurrencyId, long Amount)> currency,
            IReadOnlyList<Id> skills,
            IReadOnlyList<(Id FlagKey, ExprValue Value)> worldFlags,
            int talentPoints)
            : this(items, xp, currency, skills, worldFlags, talentPoints, xpEquivalent: null, rewardLevel: null)
        {
        }

        /// <summary>T-N4-4 新增构造重载：接受 <see cref="XpEquivalent"/>/<see cref="RewardLevel"/>
        /// （ABI 门禁"公开 API 只能新增"：本次改动只新增这一个构造重载，未带这两个参数的旧构造
        /// 函数签名原样保留，内部转发本重载并传 <c>null</c>，两者共用同一份校验逻辑，行为对既有
        /// 调用方完全透明）。</summary>
        public RewardBundle(
            IReadOnlyList<ItemStack> items,
            long xp,
            IReadOnlyList<(Id CurrencyId, long Amount)> currency,
            IReadOnlyList<Id> skills,
            IReadOnlyList<(Id FlagKey, ExprValue Value)> worldFlags,
            int talentPoints,
            double? xpEquivalent,
            int? rewardLevel)
        {
            Items = items ?? throw new ArgumentNullException(nameof(items));
            if (xp < 0) throw new ArgumentOutOfRangeException(nameof(xp), xp, "xp 不能为负数");
            Xp = xp;
            Currency = currency ?? throw new ArgumentNullException(nameof(currency));
            Skills = skills ?? throw new ArgumentNullException(nameof(skills));
            WorldFlags = worldFlags ?? throw new ArgumentNullException(nameof(worldFlags));
            if (talentPoints < 0) throw new ArgumentOutOfRangeException(nameof(talentPoints), talentPoints, "talentPoints 不能为负数");
            TalentPoints = talentPoints;
            if (xpEquivalent.HasValue && xpEquivalent.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(xpEquivalent), xpEquivalent, "xpEquivalent 不能为负数");
            }
            XpEquivalent = xpEquivalent;
            if (rewardLevel.HasValue && rewardLevel.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rewardLevel), rewardLevel, "rewardLevel 不能为负数");
            }
            RewardLevel = rewardLevel;
        }

        /// <summary>
        /// 是否为空奖励（各字段均为空/零），供调用方跳过"发空奖励事件"一类无意义处理。
        /// <see cref="XpEquivalent"/>/<see cref="RewardLevel"/> 不参与判定——<see cref="Xp"/>
        /// 已经能表达"这份奖励是否携带经验"这一维度（<see cref="XpEquivalent"/> 非空时
        /// <see cref="RewardDispatcher"/> 改经它折算，但奖励包本身"是否为空"这个整体判断不需要
        /// 为新增的折算输入再加一条独立分支）。
        /// </summary>
        public bool IsEmpty =>
            Items.Count == 0 && Xp == 0 && Currency.Count == 0 && Skills.Count == 0 &&
            WorldFlags.Count == 0 && TalentPoints == 0 && !XpEquivalent.HasValue;

        /// <summary>
        /// 从 <c>rewards</c> 字段对应的 <see cref="JsonObject"/>（<c>quest.def</c>/<c>encounter.def</c>/
        /// <c>achv.def</c> 该字段的原始 JSON 值）解析出一个 <see cref="RewardBundle"/>。
        /// <paramref name="record"/> 为 null 或字段全部缺省时返回 <see cref="Empty"/>（08 第 2.1 节
        /// <c>rewards</c> 字段整体标注"否"——非必填）。
        /// <para>
        /// T-N4-4 新增（ADR-0033 决策 3；08 第 2.1 节 2026-09-14 修订段）：新增 <c>xp_equivalent</c>
        /// （<see cref="XpEquivalent"/>）/<c>level</c>（<see cref="RewardLevel"/>）两个可选子字段——
        /// 旧字段 <c>xp</c>（<see cref="Xp"/>）标废弃但保留一个版本周期，仍可正常解析（兼容读取，
        /// 惯例同 <c>prog.xp_source.base_xp</c>/<c>weight</c> 拍板 4）。判断记录（<c>reward_level</c>
        /// 挂载点）：08 原文只给出"任务定义的经验奖励字段写当量不写绝对数"，未给出"任务等级"这个
        /// 折算输入从哪张表/哪个字段读取的字面结论（<c>quest.def</c>/<c>encounter.def</c> 均未登记
        /// 等级字段）——契约疑点上报/临时判断：本任务把它落在 <c>rewards</c> 子结构自身
        /// （<c>rewards.level</c>，字段名任务书原文给出"level（或 reward_level）"，本类型选用
        /// 前者），供设计层复核是否需要改为挂在 <c>quest.def</c>/<c>encounter.def</c> 顶层（那将是
        /// 独立于本次改动的新字段登记，需要新任务）。
        /// </para>
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
            var xpEquivalent = ParseXpEquivalent(record);
            var rewardLevel = ParseRewardLevel(record);

            return new RewardBundle(items, xp, currency, skills, worldFlags, talentPoints, xpEquivalent, rewardLevel);
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

        /// <summary>T-N4-4 新增：<c>rewards.xp_equivalent</c>，缺省 <c>null</c>（同量级字段判断记录
        /// 见 <see cref="FromRecord"/>）。</summary>
        private static double? ParseXpEquivalent(JsonObject record)
        {
            if (!record.TryGetValue("xp_equivalent", out var raw) || raw.Kind == JsonKind.Null)
            {
                return null;
            }

            if (raw is JsonNumber n)
            {
                return n.Value;
            }

            throw new FormatException("rewards.xp_equivalent 必须是数值");
        }

        /// <summary>T-N4-4 新增：<c>rewards.level</c>，缺省 <c>null</c>（挂载点判断记录见
        /// <see cref="FromRecord"/>）。</summary>
        private static int? ParseRewardLevel(JsonObject record)
        {
            if (!record.TryGetValue("level", out var raw) || raw.Kind == JsonKind.Null)
            {
                return null;
            }

            return (int)RequireInt(record, "level", "rewards.level");
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
