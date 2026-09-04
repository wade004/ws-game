using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// AI 模块对外契约（见 06 第 6.5/7 节 <c>AiHost</c>）。由 <c>core/rules/ai</c> 实现，供主循环
    /// （连续模式按 tick 频率、离散模式由 <c>TurnScheduler</c> 按行动者调用，见 06 第 6.5 节"调用
    /// 时机"）调用。
    /// </summary>
    public interface IAiHost
    {
        /// <summary>求值一次该单位的 <c>Rotation</c>，返回应执行的施法请求；无满足条件的条目或
        /// 当前不该行动时返回 null。</summary>
        SkillCastRequest? Evaluate(Id unitId);

        BehaviorState GetBehaviorState(Id unitId);

        /// <summary>供遭遇脚本强制切换阶段/状态（见 06 第 6.5 节 <c>forceState</c> 注释）。</summary>
        void ForceState(Id unitId, BehaviorState state);

        /// <summary>
        /// 补充：把该单位当前引用的 <c>ai.rotation</c> 换成 <paramref name="rotationId"/> 指向的另一张
        /// 表（见 06 第 6.4 节"Boss 阶段 = 换 Rotation...不引入新的 AI 状态类型，只是把该 CreatureUnit
        /// 当前引用的 ai.rotation 替换为另一张表"）。
        /// </summary>
        void SetRotation(Id unitId, Id rotationId);
    }
}
