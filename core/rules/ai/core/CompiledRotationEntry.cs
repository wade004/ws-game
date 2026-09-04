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

        public CompiledRotationEntry(int priority, ExprNode condition, Id skillId)
        {
            Priority = priority;
            Condition = condition;
            SkillId = skillId;
        }
    }
}
