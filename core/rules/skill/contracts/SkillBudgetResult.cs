using Core.Foundation.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// T-N3-9（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 2；06 第
    /// 3.10 节"带宽内通过；带宽外警告……比值超硬上限且无超模说明为阻断"）：<see
    /// cref="SkillBudgetAnalyzer.Analyze"/> 的四种结论，见该类型判断记录。
    /// </summary>
    public enum SkillBudgetVerdict
    {
        /// <summary>技能效果列表不含任何结算类原语（<see cref="Core.Rules.Common.SettlementEffectKinds"/>，
        /// 精确判定含 <c>apply_aura</c> 引用的光环内容，见 <see cref="SkillBudgetAnalyzer"/> 判断
        /// 记录），不参与预算校验（06 第 3.2/3.10 节"效果列表不含结算类原语的技能不参与预算校验"）。</summary>
        NotApplicable,

        /// <summary>比值落在 <c>[1 - 带宽, 1 + 带宽]</c> 内（本任务只检查超出上界，见 <see
        /// cref="SkillBudgetAnalyzer"/> 判断记录），带宽内通过。</summary>
        Pass,

        /// <summary>比值超出带宽（或超出硬上限）但 <c>skill.def.budget_note</c> 非空——"已确认"组，
        /// 警告级、不阻断（硬性规则"禁止阻断带说明的超模技能"）。</summary>
        ConfirmedDeviation,

        /// <summary>比值超出带宽、未超硬上限、且 <c>skill.def.budget_note</c> 为空——"待确认"组，
        /// 警告级、不阻断。</summary>
        UnconfirmedDeviation,

        /// <summary>比值超过硬上限且 <c>skill.def.budget_note</c> 为空——阻断（04 第 5 节"技能预算
        /// 硬上限"，抓手滑）。</summary>
        HardCapExceeded,
    }

    /// <summary>
    /// <see cref="SkillBudgetAnalyzer.Analyze"/> 的不可变结果（照 <c>Core.Carriers.Item
    /// .EquipmentScoreResult</c>/<c>Core.Gameplay.Loot.LootTableAnalyzer</c> 契约面形态：纯数据、
    /// 无行为，字段只读）。
    /// </summary>
    public sealed class SkillBudgetResult
    {
        public Id SkillId { get; }

        /// <summary>本技能是否参与预算校验（见 <see cref="SkillBudgetVerdict.NotApplicable"/>）；
        /// 为 <c>false</c> 时其余数值字段（<see cref="EffectiveValue"/> 起）均为占位值 0，不代表
        /// "算出来是 0"。</summary>
        public bool Participates { get; }

        public SkillBudgetTier Tier { get; }

        /// <summary>技能等级（06 第 3.10 节"技能等级取 skill.book 的习得等级"），怪物档取引用它的
        /// 生物模板等级，两处均找不到时见 <see cref="SkillBudgetTier.Unattributed"/> 判断记录。</summary>
        public int Level { get; }

        /// <summary>实际价值（06 第 3.10 节公式"实际价值 = 基础值 + Σ(系数 × 该等级期望缩放属性)"，
        /// 本实现的精确取舍见 <see cref="SkillBudgetAnalyzer"/> 判断记录）。</summary>
        public double EffectiveValue { get; }

        /// <summary>施放时间当量 T（06 第 3.10 节"T = max(动作时长, 一拍常数)……周期效果按总持续
        /// 时间乘折价"）。</summary>
        public double TimeEquivalent { get; }

        public double CooldownPremium { get; }

        public double RangeDiscount { get; }

        public double CostPremium { get; }

        /// <summary>锚点秒伤 <c>DPS(</c><see cref="Level"/><c>)</c>，取自注入的
        /// <see cref="Core.Rules.Common.ISkillBudgetAnchorProvider"/>。</summary>
        public double AnchorDps { get; }

        /// <summary>预算上限（06 第 3.10 节公式 = <see cref="AnchorDps"/> × <see
        /// cref="TimeEquivalent"/> × <see cref="CooldownPremium"/> × <see cref="RangeDiscount"/> ×
        /// <see cref="CostPremium"/>）。</summary>
        public double BudgetLimit { get; }

        /// <summary><see cref="EffectiveValue"/> / <see cref="BudgetLimit"/>（<see
        /// cref="BudgetLimit"/> &lt;= 0 时的退化处理见 <see cref="SkillBudgetAnalyzer"/> 判断
        /// 记录）。</summary>
        public double Ratio { get; }

        /// <summary>本次判定实际采用的带宽（按 <see cref="Tier"/> 从 <c>skill.budget_rule</c> 选取
        /// <c>player_bandwidth</c>/<c>monster_bandwidth</c>）。</summary>
        public double Bandwidth { get; }

        /// <summary>本次判定实际采用的硬上限（按 <see cref="Tier"/> 选取 <c>player_hard_cap</c>/
        /// <c>monster_hard_cap</c>）。</summary>
        public double HardCap { get; }

        /// <summary><c>skill.def.budget_note</c> 原文（为空/未填时为 <c>null</c>）。</summary>
        public string? BudgetNote { get; }

        public SkillBudgetVerdict Verdict { get; }

        public SkillBudgetResult(
            Id skillId, bool participates, SkillBudgetTier tier, int level,
            double effectiveValue, double timeEquivalent, double cooldownPremium, double rangeDiscount,
            double costPremium, double anchorDps, double budgetLimit, double ratio,
            double bandwidth, double hardCap, string? budgetNote, SkillBudgetVerdict verdict)
        {
            SkillId = skillId;
            Participates = participates;
            Tier = tier;
            Level = level;
            EffectiveValue = effectiveValue;
            TimeEquivalent = timeEquivalent;
            CooldownPremium = cooldownPremium;
            RangeDiscount = rangeDiscount;
            CostPremium = costPremium;
            AnchorDps = anchorDps;
            BudgetLimit = budgetLimit;
            Ratio = ratio;
            Bandwidth = bandwidth;
            HardCap = hardCap;
            BudgetNote = budgetNote;
            Verdict = verdict;
        }
    }
}
