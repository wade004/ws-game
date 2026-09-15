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
    /// <para>
    /// T-N3-4（ADR-0031 决策 9/10，06 第 3.1/3.6 节 2026-09-14 修订）新增两项：<see cref="ConditionNotMet"/>
    /// 对应新插入的步骤 1.5"使用条件"；<see cref="ActionLocked"/> 对应步骤 4"节拍锁"泛化后、
    /// <c>SkillOptions.GcdEnabled=false</c> 时的新分支（见两者各自注释）。二者追加在枚举末尾，不改变
    /// 既有成员的数值（本枚举非 <c>[Flags]</c>，调用方按值比较，不依赖具体整数）。
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

        /// <summary>
        /// ADR-0027《地面坐标施法请求》补充：<see cref="ISkillHost.CastSkillAtGround"/> 目标的技能
        /// 未在 <c>skill.def</c> 声明允许地面坐标目标（<c>ground_target</c> 缺省 <c>false</c>，见
        /// <c>Core.Rules.Skill.SkillDef.AllowGroundTarget</c>）。<see cref="ISkillHost.CastSkillAtGround"/>
        /// 默认接口实现恒返回本原因（见该方法判断记录），生产实现
        /// <c>Core.Rules.Skill.CastPipeline.CastSkillAtGround</c> 在技能未声明时同样返回本原因，
        /// 与既有单位目标 <see cref="ISkillHost.CastSkill"/> 逐字节不变——地面坐标是新增能力，未
        /// 声明的既有技能继续只能走单位目标入口。
        /// </summary>
        GroundTargetUnsupported,

        /// <summary>
        /// ADR-0027 补充：地面坐标施法请求的落点不可行走（<c>INavigation2D.IsWalkable</c> 判 false，
        /// 见 <c>CastPipeline.ValidateGroundPoint</c> 判断记录）——与 <see cref="OutOfRange"/>/
        /// <see cref="GroundTargetNoLineOfSight"/> 是同一批地面坐标专属校验的三个独立分支，互不
        /// 覆盖。未注入 <c>INavigation2D</c>（可选依赖，见 02 第 1.8 节"可选接口"）或该单位未接入
        /// 地图概念（<c>IUnitAccess.GetMapId</c> 返回 null）时本校验跳过（宁可漏判，不误判——没有
        /// 导航数据无法判定"是否可行走"，不应该悄悄拒绝）。
        /// </summary>
        GroundTargetUnreachable,

        /// <summary>
        /// ADR-0027 补充：施法者到地面坐标落点之间无视线（复用既有 <c>ISpatialQuery.HasLineOfSight</c>，
        /// 与步骤 7 单位目标 <see cref="LineOfSight"/> 同一判定方法，只是终点从目标单位坐标换成
        /// 地面落点坐标）——与 <see cref="LineOfSight"/> 拆成独立原因码，供消费方区分"这是一次地面
        /// 坐标请求被挡"还是"这是一次单位目标请求被挡"。仅当 <c>skill.def.range &gt; 0</c> 时校验
        /// （<c>range == 0</c> 表示无限制，见 06 第 3.1 节，同 <see cref="OutOfRange"/> 判断记录），
        /// 未注入 <c>ISpatialQuery</c> 时本校验跳过（同步骤 7 既有降级惯例）。
        /// </summary>
        GroundTargetNoLineOfSight,

        /// <summary>
        /// T-N3-4（ADR-0031 决策 9，06 第 3.1/3.6 节 2026-09-14 修订）补充：新插入的步骤 1.5"使用条件"
        /// ——<c>skill.def.use_condition</c>（宿主为施法者上下文，见 <c>core/rules/expr_host</c>）求值为
        /// 假。检查位置在步骤 1（存活与状态）之后、步骤 2（学派锁定）之前，早于步骤 6 的目标解析——
        /// 条件引用 <c>target.*</c> 分组时，只有调用方显式传入了 <c>targets</c> 才有候选目标可绑定
        /// （见 <see cref="Core.Rules.Skill.CastPipeline"/> 判断记录"使用条件的目标绑定"），未解析出
        /// 目标时按 <c>core/rules/expr_host</c> 既有"引用对象暂缺按默认值处理"惯例，不是本原因码的
        /// 独立分支。<see cref="Core.Rules.Skill.ISkillHost.GetSkillReadiness"/> 的只读查询同步纳入本项
        /// （<see cref="Core.Rules.Skill.SkillReadinessBlockers.ConditionNotMet"/>）。
        /// </summary>
        ConditionNotMet,

        /// <summary>
        /// T-N3-4（ADR-0031 决策 10，06 第 3.1/3.6 节 2026-09-14 修订）补充：步骤 4"节拍锁"泛化后，
        /// <c>SkillOptions.GcdEnabled=false</c> 分支的失败原因——施法者正处于另一个技能的动作时长内
        /// （读条/引导尚未结束，<c>CastPipeline.IsCasting</c> 为真），且本次请求的技能
        /// <c>respects_gcd=true</c>。<c>respects_gcd=false</c> 的反应类技能（打断/格挡/保命）不落入
        /// 本分支，可在他技能动作中插入（见 <c>CastPipeline.CastSkill</c> 判断记录"反应类插入"）。
        /// <c>GcdEnabled=true</c> 时本分支不生效，节拍锁仍是既有 <see cref="GcdActive"/>（步骤 4 原有
        /// 分支，逐字节不变）——两者互斥，同一次调用只会返回其中之一。与既有 <see cref="Busy"/>
        /// 的关系：<see cref="Busy"/> 覆盖"落在法术队列窗口之外的忙碌拒绝"这一路径本身不区分
        /// <c>respects_gcd</c>；本原因码是 <c>GcdEnabled=false</c> 时对同一路径里
        /// "respects_gcd=true 因而必然被节拍锁拒绝"这一具体成因的精确化——两者不是同一层级的互斥
        /// 分类，<see cref="Busy"/> 在 <c>GcdEnabled=false</c> 下只保留给"反应类技能因自身需要非瞬发
        /// 读条/引导、无法安全插入现有 <c>CastState</c> 槽位"这一边缘情形（见
        /// <c>core/rules/skill/README.md</c> 判断记录"反应类插入的槽位保护"），不再是
        /// <c>GcdEnabled=false</c> 下"忙碌"的默认原因码。<see cref="Core.Rules.Skill.ISkillHost.GetSkillReadiness"/>
        /// 的只读查询同步纳入本项（<see cref="Core.Rules.Skill.SkillReadinessBlockers.ActionLocked"/>）。
        /// </summary>
        ActionLocked,
    }
}
