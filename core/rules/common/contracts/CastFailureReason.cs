namespace Core.Rules.Common
{
    /// <summary>
    /// 施法管线各步骤失败原因码（见 06_规则层_属性技能战斗AI.md 第 3.6 节"固定检查顺序...每一步
    /// 失败即中止并返回对应失败原因码"）。<see cref="Dead"/>/<see cref="Stunned"/>/<see cref="Silenced"/>
    /// 对应步骤 1（06 原文示例列出三者）；<see cref="SchoolLocked"/> 对应步骤 2；<see cref="OnCooldown"/>
    /// 对应步骤 3；<see cref="GcdActive"/> 对应步骤 4；<see cref="InsufficientPower"/> 对应步骤 5；
    /// <see cref="NoValidTarget"/> 对应步骤 6；<see cref="OutOfRange"/>/<see cref="LineOfSight"/> 对应
    /// 步骤 7；<see cref="Interrupted"/> 对应步骤 8。
    /// <para>
    /// 以下五项 06 原文未列出，属任务书要求的补充（"06 §3.6 原因码 + 必要补充"），标注「补充」：
    /// <see cref="None"/>（补充，表示尚未失败/无原因，供 <see cref="CastResult"/> 成功分支使用）、
    /// <see cref="UnknownSkill"/>（补充，<c>skillId</c> 未在 <c>skill.def</c> 登记，早于步骤 1 的前置校验）、
    /// <see cref="PassiveSkill"/>（补充，<c>kind = passive</c> 的技能不可主动施放，见 06 第 3.1 节
    /// <c>kind</c> 字段说明，同样早于步骤 1）、<see cref="NoCharges"/>（补充，细化步骤 3"充能是否 &gt; 0"
    /// 这一具体失败分支，区别于普通冷却未就绪）、<see cref="Busy"/>（集成任务补充，见其注释）。
    /// </para>
    /// </summary>
    public enum CastFailureReason
    {
        /// <summary>补充：无失败，仅供成功结果使用。</summary>
        None,

        Dead,
        Stunned,
        Silenced,
        SchoolLocked,
        OnCooldown,
        GcdActive,
        InsufficientPower,
        NoValidTarget,
        OutOfRange,
        LineOfSight,
        Interrupted,

        /// <summary>补充：技能 id 未登记。</summary>
        UnknownSkill,

        /// <summary>补充：目标是被动技能，不可经施法管线主动施放。</summary>
        PassiveSkill,

        /// <summary>补充：充能耗尽（步骤 3 冷却检查的细分分支）。</summary>
        NoCharges,

        /// <summary>
        /// 集成任务补充：施法者正在读条/引导中，且新请求落在 06 第 3.6 节法术队列窗口之外
        /// （<c>core/rules/skill</c> 模块 <c>SkillOptions.QueueWindow</c>）。契约缺口修正：
        /// <c>core/rules/skill/core/CastPipeline.cs</c> 原实现在这一分支复用了
        /// <see cref="OnCooldown"/>（该模块 README"判断记录"称其为"最贴近的既有原因码"，但施法者
        /// 此刻并非真的处于技能冷却中，只是"忙"——读条/引导占用中，与真正的冷却语义不同，调用方
        /// 如 AI 的 Rotation 条件、UI 提示文案需要能区分两者），本原因码专门覆盖这一分支，
        /// <see cref="OnCooldown"/> 恢复只表示步骤 3 冷却/充能未就绪这一单一语义。
        /// </summary>
        Busy,
    }
}
