namespace Core.Rules.Skill
{
    /// <summary>
    /// T-N3-9（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 2；06 第
    /// 3.10 节"玩家档/怪物档由反向引用决定：只被 <c>creature.template</c> 引用的技能按怪物档，出现
    /// 在任一 <c>skill.book</c> 的按玩家档"）：技能预算校验用到的档位判定结果，见
    /// <see cref="SkillDefCache.TryResolveBudgetAttribution"/>。
    /// </summary>
    public enum SkillBudgetTier
    {
        /// <summary>出现在至少一本 <c>skill.book</c> 里（不论是否同时被 <c>creature.template</c>
        /// 引用——ADR-0031 后果段"技能同时被两边引用时按玩家档"）。</summary>
        Player,

        /// <summary>未出现在任何 <c>skill.book</c>，但被至少一个 <c>creature.template</c>（经其
        /// <c>ai_rotation_ref</c> 指向的 <c>ai.rotation.entries[].skill_id</c>）引用。</summary>
        Monster,

        /// <summary>
        /// 契约疑点（上报，待设计层确认；临时判断已实现，见 <see cref="SkillDefCache"/> 判断记录）：
        /// 06 第 3.10 节只给出"玩家档/怪物档"二分，未规定"两处反向引用都找不到"（如：只被
        /// <c>item.template.grants.skills</c> 授予、或纯粹未被任何内容引用的技能）该归哪一档。本
        /// 任务临时判定：既不属于玩家档也不属于怪物档时归为本值，<c>SkillBudgetAnalyzer</c> 按玩家档
        /// 带宽/硬上限判定（更严格的一档，"宁可多报警告，不可放过手滑"），等级取 <c>1</c>（无法推断
        /// 真实等级时的保守缺省）。
        /// </summary>
        Unattributed,
    }
}
