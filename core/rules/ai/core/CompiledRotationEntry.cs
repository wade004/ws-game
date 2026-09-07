using Core.Foundation.Common;
using Core.Foundation.Expr;

namespace Core.Rules.Ai
{
    /// <summary>
    /// <c>ai.rotation.entries</c> 一条记录解析后的运行期形态（见 06 第 6.2 节 <c>RotationEntry</c>：
    /// <c>{priority: Int, condition: Expr, skillId: Id}</c>）。<see cref="Condition"/> 是
    /// <c>condition</c> 字段文本的解析结果（构造期解析一次）。本类型只在本模块内部使用。
    /// </summary>
    internal sealed class CompiledRotationEntry
    {
        public int Priority { get; }

        public ExprNode Condition { get; }

        public Id SkillId { get; }

        /// <summary>
        /// RC-10 收边补齐：本条目引用的技能，其 <c>target_shape_ref</c> 指向的
        /// <c>target.chain_def</c> 是否为"敌对单体"（<c>filters</c> 含 <c>relation:hostile</c>、
        /// 不含 <c>relation:friendly</c>，且 <c>max_targets == 1</c>，见
        /// <c>AiHost.ClassifyIsHostileSingleTarget</c> 判断记录）——构造期（<c>AiHost.LoadRotations</c>）
        /// 计算一次并缓存，不在每次 <c>Evaluate</c> 求值时重复查表。只有这一类技能，
        /// <see cref="AiHost.Evaluate"/> 才会把 AI 当前追踪的敌人（<c>AiState.Target</c>）强制当作
        /// 目标传给 <c>CastSkill</c>；其余（自疗/友疗/AOE/未知）一律传空目标，交由技能自己的
        /// 目标链（<see cref="Core.Rules.Common.ISkillHost.CastSkill"/> 内 <c>CastPipeline</c> 步骤 6）
        /// 解析——原实现无条件强塞 <c>state.Target</c>，自疗类技能会被错误地作用于敌人、友疗/AOE
        /// 类技能的过滤/多目标解析被完全跳过（见外部审计 RC-10）。
        /// </summary>
        public bool IsHostileSingleTarget { get; }

        public CompiledRotationEntry(int priority, ExprNode condition, Id skillId, bool isHostileSingleTarget)
        {
            Priority = priority;
            Condition = condition;
            SkillId = skillId;
            IsHostileSingleTarget = isHostileSingleTarget;
        }
    }
}
