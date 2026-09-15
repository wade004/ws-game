using Core.Foundation.Common;
using Core.Rules.Common;

namespace Core.Rules.Ai
{
    /// <summary>
    /// T-N3-10（ADR-0031 决策 12"一键智能释放"；ADR-0035 决策 2"标准玩家生成器/仿真与 AI 共用同一
    /// 求值组件"）：独立于 <c>ai.behavior_profile</c>/<see cref="BehaviorState.Combat"/> 态的优先级表
    /// （<c>ai.rotation</c>）求值组件契约——按条目 <c>priority</c> 从高到低遍历，选出第一个条件为真、
    /// 经 <see cref="ISkillHost.GetSkillReadiness"/> 判定就绪、且经 <see cref="ISkillHost.CastSkill"/>
    /// 实际施放成功的技能。<see cref="AiHost"/>（仅在 <see cref="BehaviorState.Combat"/> 态）与任何
    /// "给玩家单位一键智能释放"的调用方（玩家输入层、仿真骨架的标准玩家生成器）共用同一实现，不各自
    /// 重复一遍相同的选择逻辑；后者不需要注册 <c>ai.behavior_profile</c>，也不需要该单位处于
    /// <see cref="BehaviorState.Combat"/> 态——只需要一个 <c>ai.rotation</c> 表 id。
    /// <para>
    /// 无状态契约（硬性规则）：实现类型禁止持有任何按单位区分的可变状态——<see cref="Evaluate"/> 每次
    /// 调用只读入参与构造期只读内容缓存（<c>ai.rotation</c> 编译结果，非"单位状态"，见
    /// <see cref="RotationEvaluator"/> 类型判断记录），不跨调用记忆任何单位相关信息，多个单位可安全
    /// 共享同一个实现实例并发/交替调用。
    /// </para>
    /// </summary>
    public interface IRotationEvaluator
    {
        /// <summary>
        /// 求值一次。<paramref name="rotationId"/> 未登记（未知的 <c>ai.rotation</c>）、或全部候选
        /// 条件为假/不就绪/<c>CastSkill</c> 施法失败时均返回 <c>null</c>——与
        /// <see cref="IAiHost.Evaluate"/>"无满足条件的条目时返回 null"同一惯例，调用方不需要区分
        /// 这两种"没有结果"的情形。
        /// </summary>
        /// <param name="unitId">施法者，同时也是求值 <c>entries[].condition</c> Expr 的 <c>self</c>
        /// 上下文。</param>
        /// <param name="rotationId">要求值的 <c>ai.rotation</c> 表 id。</param>
        /// <param name="targetId">当前追踪/选中的目标（可为空）——同时作为 Expr 求值的 <c>target</c>
        /// 上下文，以及"敌对单体"类技能强塞的目标（见 <c>CompiledRotationEntry.IsHostileSingleTarget</c>
        /// 判断记录）。</param>
        SkillCastRequest? Evaluate(Id unitId, Id rotationId, Id? targetId);

        /// <summary>供 <see cref="AiHost.SetRotation"/> 等调用方校验用：<paramref name="rotationId"/>
        /// 是否为已登记（构造期编译过）的 <c>ai.rotation</c>。</summary>
        bool HasRotation(Id rotationId);
    }
}
