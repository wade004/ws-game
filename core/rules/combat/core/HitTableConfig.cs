using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Rules.Combat
{
    /// <summary>
    /// 命中表六个分支之一的强类型视图（见 06_规则层_属性技能战斗AI.md 第 4.2 节）：
    /// <c>enabled</c> 为 false 时 <see cref="Resolver"/> 完全不掷骰、该分支恒不触发；
    /// <see cref="Stat"/> 提供时概率取 <c>IStatHost.GetStat</c>，否则取 <see cref="Base"/> 常量。
    /// </summary>
    public sealed class HitTableBranch
    {
        public bool Enabled { get; }

        public Id? Stat { get; }

        public double Base { get; }

        /// <summary>
        /// T-N1-8（ADR-0030 决策 6；06 第 4.2 节修订段"未命中率 = 基础未命中 − 攻击者命中属性 +
        /// 目标闪避属性 + 未命中加成(Δ)"）：仅 <c>miss</c> 分支消费的扩展字段——攻击者命中属性，
        /// 提供时从 <see cref="Resolver.DetermineHit"/> 计算出的未命中率里额外减去该属性当前值。
        /// 判断记录（不是重用既有 <see cref="Stat"/>）：<see cref="Stat"/> 在六个分支里的既有语义是
        /// "提供时概率直接取该属性当前值，否则取 <see cref="Base"/>"（见 <c>CombatSchemas</c> 判断
        /// 记录 11），与本字段"从 base 计算出的概率上再减去一个命中属性"是不同的运算——复用 <c>stat</c>
        /// 会与既有语义冲突，因此单开一个字段名 <c>hit_stat</c>，且只在 <c>miss</c> 分支的字段列表里
        /// 登记（<c>CombatSchemas.MissBranchFieldList</c>），其余五个分支即便数据里误填也没有任何
        /// 消费点，不产生效果。06 第 4.2 节修订段原文里的"目标闪避属性"一项：<c>dodge</c> 分支已经是
        /// 独立判定、单独消耗一次掷骰（见本模块 README 判断记录 2"dodge 查询防御者"），未命中率公式
        /// 里若再叠加一次目标闪避属性会与 <c>dodge</c> 分支重复计入同一件事——本实现选择不在 <c>miss</c>
        /// 公式里重复叠加"目标闪避属性"这一项，只保留"基础未命中 − 攻击者命中属性 + 未命中加成(Δ)"，
        /// 六分支既有优先序（miss → dodge → parry → glancing_blow → block → crit）不变，见
        /// <see cref="Resolver"/> 判断记录。
        /// </summary>
        public Id? HitStat { get; }

        public HitTableBranch(bool enabled, Id? stat, double @base) : this(enabled, stat, @base, hitStat: null)
        {
        }

        /// <summary>T-N1-8 新增重载：追加 <see cref="HitStat"/>（ABI 兼容惯例同
        /// <c>Core.Rules.Common.EffectContext</c> 多参构造重载——不改动既有三参构造函数的物理签名，
        /// 新增一个参数个数不同的重载）。</summary>
        public HitTableBranch(bool enabled, Id? stat, double @base, Id? hitStat)
        {
            Enabled = enabled;
            Stat = stat;
            Base = @base;
            HitStat = hitStat;
        }

        internal static HitTableBranch Parse(DataRecord record, string field)
        {
            var obj = record.GetObject(field);

            var enabled = obj.TryGetValue("enabled", out var enabledVal) && enabledVal is JsonBool b && b.Value;

            Id? stat = null;
            if (obj.TryGetValue("stat", out var statVal) && statVal is JsonString s && Id.TryParse(s.Value, out var id))
            {
                stat = id;
            }

            double baseValue = 0.0;
            if (obj.TryGetValue("base", out var baseVal) && baseVal is JsonNumber n)
            {
                baseValue = n.Value;
            }

            Id? hitStat = null;
            if (obj.TryGetValue("hit_stat", out var hitStatVal) && hitStatVal is JsonString hs && Id.TryParse(hs.Value, out var hitId))
            {
                hitStat = hitId;
            }

            return new HitTableBranch(enabled, stat, baseValue, hitStat);
        }
    }

    /// <summary>
    /// 一条 <c>combat.hit_table_config</c> 记录的强类型视图（见 06 第 4.2 节命中表六项、
    /// 本模块 README"命中表"一节）。从 <see cref="DataRecord"/> 构造，构造期完成全部字段解析，
    /// 非法数据在构造期即抛 <see cref="DataFieldException"/>（惯例同
    /// <c>Core.Numbers.PowerSet.PowerTypeDefinition</c>）。
    /// </summary>
    public sealed class HitTableConfig
    {
        public Id Id { get; }

        public HitTableBranch Miss { get; }

        public HitTableBranch Dodge { get; }

        public HitTableBranch Parry { get; }

        public HitTableBranch GlancingBlow { get; }

        public HitTableBranch Block { get; }

        public HitTableBranch Crit { get; }

        public Id? CritMultiplierStat { get; }

        /// <summary>暴击倍率默认值，未提供时为 2.0（见本模块 README"默认值由口味清单给出"）。</summary>
        public double CritMultiplierBase { get; }

        /// <summary>偏斜命中保留的伤害比例（0~1），未提供时为 0。</summary>
        public double GlancingDamagePct { get; }

        public Id? BlockValueStat { get; }

        public HitTableConfig(DataRecord record)
        {
            Id = record.GetId("id");
            Miss = HitTableBranch.Parse(record, "miss");
            Dodge = HitTableBranch.Parse(record, "dodge");
            Parry = HitTableBranch.Parse(record, "parry");
            GlancingBlow = HitTableBranch.Parse(record, "glancing_blow");
            Block = HitTableBranch.Parse(record, "block");
            Crit = HitTableBranch.Parse(record, "crit");

            CritMultiplierStat = record.TryGetId("crit_multiplier_stat", out var critStat) ? critStat : (Id?)null;
            CritMultiplierBase = record.TryGetNumber("crit_multiplier_base", out var critBase) ? critBase : 2.0;
            GlancingDamagePct = record.TryGetNumber("glancing_damage_pct", out var glancingPct) ? glancingPct : 0.0;
            BlockValueStat = record.TryGetId("block_value_stat", out var blockStat) ? blockStat : (Id?)null;
        }
    }
}
