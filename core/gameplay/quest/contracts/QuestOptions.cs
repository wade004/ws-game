namespace Core.Gameplay.Quest
{
    /// <summary>
    /// <see cref="QuestHost"/> 的构造期策略配置（见 08 第 9 节 Quest 行"策略配置项：可重复性、
    /// 多选一奖励（建议扩展）、escort 是否允许失败"——可重复性已由 <c>quest.def.repeatable</c>
    /// 逐条数据决定，不是全局开关；多选一奖励见 08 第 2.4 节"留待后续 ADR"，本版不实现；
    /// <see cref="AllowFail"/> 是"escort 是否允许失败"策略开关的落地，覆盖 <see cref="IQuestHost.Fail"/>
    /// 全部任务，不仅限 escort——08 原文只点名 escort/event 类"可能失败"，未说明其它类型是否也可以
    /// 被判定失败，本模块把 <see cref="AllowFail"/> 做成任务级全局开关而非按目标类型区分，更简单且
    /// 不违反原文，具体是否只对 escort/event 生效由调用方在决定何时调用 <see cref="IQuestHost.Fail"/>
    /// 时自行把控）。
    /// </summary>
    public sealed class QuestOptions
    {
        /// <summary>是否允许 <see cref="IQuestHost.Fail"/> 生效；默认 true。为 false 时
        /// <see cref="IQuestHost.Fail"/> 恒返回 false、不改变任何状态。</summary>
        public bool AllowFail { get; set; } = true;
    }
}
