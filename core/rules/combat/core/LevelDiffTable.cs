using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Core.Rules.Combat
{
    /// <summary>
    /// <c>combat.level_diff_table</c> 一条记录的强类型视图（T-N1-8；ADR-0030 决策 6；06 第 4.2 节
    /// 修订段）：三条以 Δ = 目标有效等级 − 攻击者有效等级为横轴的断点表曲线，加一条以攻击者有效等级
    /// 为横轴的灰名界线（见 <see cref="CombatSchemas.LevelDiffTable"/> 判断记录）。<see cref="XpFactor"/>/
    /// <see cref="GreyLine"/> 本任务只登记加载，不在本模块内求值消费——消费者在阶段 N4 Progression
    /// 接入（同上判断记录 1）。
    /// </summary>
    public sealed class LevelDiffTable
    {
        public Id Id { get; }

        /// <summary>未命中加成断点表，横轴 Δ（06 第 4.2 节修订段）。</summary>
        public PiecewiseCurve MissBonus { get; }

        /// <summary>暴击压制断点表，横轴 Δ（06 第 4.2 节修订段）。</summary>
        public PiecewiseCurve CritSuppression { get; }

        /// <summary>经验系数断点表，横轴 Δ；本模块不消费，阶段 N4 接入。</summary>
        public PiecewiseCurve XpFactor { get; }

        /// <summary>灰名界线断点表，横轴是攻击者有效等级（不是 Δ，见类型判断记录 2）；本模块不消费，
        /// 消费公式待阶段 N4 确认。</summary>
        public PiecewiseCurve GreyLine { get; }

        public LevelDiffTable(DataRecord record)
        {
            Id = record.GetId("id");
            MissBonus = CurveSchema.ReadBreakpoints(record, "miss_bonus");
            CritSuppression = CurveSchema.ReadBreakpoints(record, "crit_suppression");
            XpFactor = CurveSchema.ReadBreakpoints(record, "xp_factor");
            GreyLine = CurveSchema.ReadBreakpoints(record, "grey_line");
        }
    }
}
