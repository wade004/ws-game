using Core.Foundation.Common;

namespace Core.Sim
{
    /// <summary>消费方反馈第 49 条（2026-09-17）：<see cref="FightRunnerOptions.CaptureEvents"/> 开启时
    /// <see cref="FightResult.CapturedEvents"/> 逐条记录的战斗日志行——从战斗过程中现有可拿到的事件类型
    /// 映射而来（不是原样透传 <c>IEvent</c>：<c>IEvent</c> 本身不含 <c>tick</c>，各事件类型字段形状不
    /// 统一，调用方逐个 <c>is</c> 判断类型比这里统一映射一次更繁琐），覆盖伤害、治疗、施法成功/失败、
    /// 增益施加/移除、死亡、资源变化六类——见 <see cref="FightLogEventCategory"/> 判断记录"覆盖范围"。</summary>
    public sealed class FightLogEntry
    {
        /// <summary>本条目产生时所在的 tick（从 1 起，同 <see cref="ResourceSample.Tick"/> 惯例）。</summary>
        public int Tick { get; }

        public FightLogEventCategory Category { get; }

        /// <summary>来源单位——非全部类别都有意义（如 <see cref="FightLogEventCategory.UnitDied"/> 用它
        /// 携带击杀者，环境死亡时为 <c>null</c>），具体含义按 <see cref="Category"/> 解释。</summary>
        public Id? SourceId { get; }

        /// <summary>目标单位，含义同 <see cref="SourceId"/> 按 <see cref="Category"/> 解释。</summary>
        public Id? TargetId { get; }

        /// <summary>技能 id（施法类）或效果/光环 id（增益类），伤害/治疗/资源变化事件本身不携带技能 id
        /// （见 <see cref="FightRunner"/> 判断记录"采样口径"——<c>combat.damage_dealt</c> 事件契约本就
        /// 没有这个字段），本字段为 <c>null</c>。</summary>
        public Id? SkillOrEffectId { get; }

        /// <summary>数值——伤害/治疗为落地量，光环施加为叠加层数，资源变化为净变化量
        /// （<c>NewValue-OldValue</c>），其余类别为 <c>null</c>。</summary>
        public double? Amount { get; }

        /// <summary>结果标记——自由文本，含义按 <see cref="Category"/> 解释（伤害："hit"/"crit"/
        /// <c>HitResult</c> 原始取值小写；施法失败：<c>CastFailureReason</c> 取值；光环移除：移除原因
        /// 如 "expired"/"dispelled"），无对应语义时为 <c>null</c>。</summary>
        public string? ResultTag { get; }

        public FightLogEntry(
            int tick, FightLogEventCategory category, Id? sourceId, Id? targetId, Id? skillOrEffectId,
            double? amount, string? resultTag)
        {
            Tick = tick;
            Category = category;
            SourceId = sourceId;
            TargetId = targetId;
            SkillOrEffectId = skillOrEffectId;
            Amount = amount;
            ResultTag = resultTag;
        }
    }

    /// <summary>
    /// 判断记录（覆盖范围：为何只有这七类，不是全部 18 种战斗相关事件）：任务书原文明确列出"伤害、
    /// 治疗、施法成功/失败、增益施加/移除、死亡、资源变化中现有能拿到的"——不含施法开始/被打断
    /// （<c>SkillCastStartEvent</c>/<c>SkillCastInterruptedEvent</c>，读条/引导过程本身不是"结果"）、
    /// 不含 <c>AuraStackChangedEvent</c>/<c>ProcTriggeredEvent</c>/仇恨/进战离战/AI 决策/目标选择/
    /// 时间模型缩放（这些要么是过程性中间状态、要么与"战斗日志"字面语义关系不直接），按明确范围收窄，
    /// 避免"顺手多映射几种"造成的字段语义膨胀——后续若有真实消费需求，按 ABI 只新增原则追加即可。
    /// </summary>
    public enum FightLogEventCategory
    {
        Damage,
        Heal,
        SkillCastSuccess,
        SkillCastFailed,
        AuraApplied,
        AuraRemoved,
        UnitDied,
        ResourceChanged,
    }
}
