using System;
using Core.Foundation.Common;

namespace Core.Numbers.StatBlock
{
    /// <summary>
    /// 属性修正的运算类型（见 06_规则层_属性技能战斗AI.md 第 1.1/1.2 节三段式聚合）。
    /// <c>Flat</c>：基础值段加法叠加；<c>Pct</c>：百分比段各项相加后统一相乘；
    /// <c>Mult</c>：独立乘区，同一 <see cref="StatModifier.MultGroup"/> 内的多条 <c>Mult</c>
    /// 修正先相加，各乘区之间再相乘（06 第 1.3 节策略配置项"独立乘区的分类粒度，默认按效果
    /// 来源类别分区"）。
    /// </summary>
    public enum StatModifierOp
    {
        Flat,
        Pct,
        Mult,
    }

    /// <summary>
    /// 一条属性修正（见 06 第 1.2 节 <c>StatModifier</c>）：挂在某个单位的某条属性上，
    /// 按 <see cref="SourceId"/> 标记来源，供 <see cref="IStatHost.RemoveModifiersBySource"/>
    /// 整体撤销（不需要逐条匹配数值）。
    /// </summary>
    public readonly struct StatModifier
    {
        /// <summary>默认独立乘区名（06 第 1.3 节"默认按效果来源类别分区，游戏层可细化"——
        /// 本模块不预设细分类别，游戏层未显式指定 <see cref="MultGroup"/> 时全部落入同一个
        /// 默认乘区）。</summary>
        public const string DefaultMultGroup = "default";

        public Id Stat { get; }

        public StatModifierOp Op { get; }

        public double Value { get; }

        public Id SourceId { get; }

        /// <summary><see cref="StatModifierOp.Mult"/> 修正所属的独立乘区名；<c>Flat</c>/<c>Pct</c>
        /// 修正忽略本字段（不参与乘区分组）。未指定时默认 <see cref="DefaultMultGroup"/>。</summary>
        public string MultGroup { get; }

        public StatModifier(Id stat, StatModifierOp op, double value, Id sourceId, string? multGroup = null)
        {
            Stat = stat;
            Op = op;
            Value = value;
            SourceId = sourceId;
            MultGroup = string.IsNullOrEmpty(multGroup) ? DefaultMultGroup : multGroup!;
        }
    }
}
