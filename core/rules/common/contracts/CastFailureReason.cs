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
    /// 以下七项 06 原文未列出，属任务书要求的补充（"06 §3.6 原因码 + 必要补充"），标注「补充」：
    /// <see cref="None"/>（补充，表示尚未失败/无原因，供 <see cref="CastResult"/> 成功分支使用）、
    /// <see cref="UnknownSkill"/>（补充，<c>skillId</c> 未在 <c>skill.def</c> 登记，早于步骤 1 的前置校验）、
    /// <see cref="PassiveSkill"/>（补充，<c>kind = passive</c> 的技能不可主动施放，见 06 第 3.1 节
    /// <c>kind</c> 字段说明，同样早于步骤 1）、<see cref="NoCharges"/>（补充，细化步骤 3"充能是否 &gt; 0"
    /// 这一具体失败分支，区别于普通冷却未就绪）、<see cref="Busy"/>（集成任务补充，见其注释）、
    /// <see cref="InsufficientActionPoints"/>（W1 收边补充，见其注释）、<see cref="QueueCleared"/>
    /// （消费方反馈 2026-09-10 补充，见其注释）。
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

        /// <summary>
        /// W1 收边补充（06 第 3.1 节 <c>action_cost</c>）：离散步内，<c>skill.def.action_cost</c>
        /// 大于 0 且调用方已装配 <see cref="Core.Rules.Skill.SkillOptions.IsDiscreteStep"/>/
        /// <see cref="Core.Rules.Skill.SkillOptions.TryConsumeActionPoints"/> 时，施法者当前剩余行动点
        /// 不足以支付本次施放。是步骤 5"资源"检查在离散模式下的补充分支（与 <see cref="InsufficientPower"/>
        /// 同属"资源不够"这一大类，用独立原因码区分"资源池不足"与"行动点不足"，便于 AI Rotation
        /// 条件与 UI 分别处理）。连续模式恒不触发本原因码。
        /// </summary>
        InsufficientActionPoints,

        /// <summary>
        /// 消费方反馈 2026-09-10（施法生命周期事件缺少实例关联标识建议，见 architecture/落地计划/
        /// 消费方反馈-2026-09-10-施法时序与实例标识.md）补充：本次施法请求已经成功进入法术队列
        /// （06 第 3.6 节"法术队列"，<c>CastResult.CastInstanceId</c> 已经分配并返回给调用方），但
        /// 在真正轮到它开始之前，队列槽位被清空——两种情形共用本原因码：a) 同一施法者又发起一次
        /// 新的施法请求，落在队列窗口内，覆盖了尚未执行的旧排队请求；b) 占用队列槽位的那次读条/
        /// 引导本身被打断（<c>CastPipeline.Interrupt</c>），排队的下一个技能连同打断一起被清空、
        /// 永远不会开始。<c>SkillCastFailedEvent.CastInstanceId</c> 携带被清空的那个排队请求自己的
        /// 实例 id（不是覆盖它的新请求、也不是被打断的当前施法的 id），供消费方确认"我排的这个队
        /// 最终没有被执行"而不是永远等不到任何结果通知。
        /// </summary>
        QueueCleared,
    }
}
