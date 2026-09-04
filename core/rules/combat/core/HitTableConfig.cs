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

        public HitTableBranch(bool enabled, Id? stat, double @base)
        {
            Enabled = enabled;
            Stat = stat;
            Base = @base;
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

            return new HitTableBranch(enabled, stat, baseValue);
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
