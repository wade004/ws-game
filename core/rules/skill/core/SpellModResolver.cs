using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// SpellMod 修正聚合（见 06 第 3.5 节、落地方案 T2-6 行）：从施法者当前生效光环携带的
    /// <c>spell_mod</c> 效果（经 <see cref="AuraHost.GetActiveSpellModRefs"/> 收集）中，按
    /// <see cref="SpellModDimension"/> 与 <see cref="SkillFilter.Matches"/> 过滤出命中项，
    /// 按固定顺序聚合："先 Σflat 再 ×(1+Σpct)"（判断记录：06 原文只给出 flat/pct 两种运算类型，
    /// 未规定同维度多条修正之间如何组合，本模块按属性三段式聚合"同类项先加总、再统一相乘"的
    /// 既有惯例（见 06 第 1.1 节 StatBlock 三段式）类比处理，全模块统一用这一顺序，不逐条累乘）。
    /// </summary>
    public sealed class SpellModResolver
    {
        private readonly SkillDefCache _defs;
        private readonly AuraHost _auraHost;

        public SpellModResolver(SkillDefCache defs, AuraHost auraHost)
        {
            _defs = defs ?? throw new ArgumentNullException(nameof(defs));
            _auraHost = auraHost ?? throw new ArgumentNullException(nameof(auraHost));
        }

        /// <summary>对 <paramref name="baseValue"/> 应用某单位当前生效的、命中
        /// <paramref name="dimension"/> 与 <paramref name="skillId"/>/<paramref name="school"/>/
        /// <paramref name="tags"/> 过滤条件的全部 SpellMod。</summary>
        public double Apply(Id casterId, SpellModDimension dimension, Id skillId, Id school, IReadOnlyList<Id> tags, double baseValue)
        {
            double flatSum = 0;
            double pctSum = 0;

            foreach (var refId in _auraHost.GetActiveSpellModRefs(casterId))
            {
                if (!_defs.TryGetSpellModDef(refId, out var mod)) continue;
                if (mod.TargetDimension != dimension) continue;
                if (!mod.Affects.Matches(skillId, school, tags)) continue;

                if (mod.Op == SpellModOp.Flat) flatSum += mod.Value;
                else pctSum += mod.Value;
            }

            return (baseValue + flatSum) * (1 + pctSum);
        }
    }
}
