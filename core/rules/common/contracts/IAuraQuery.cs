using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// 光环状态的只读查询出口（见任务书"Combat/Expr/AI 读光环状态用"）。由 <c>core/rules/skill</c>
    /// 实现（光环实例状态本就是 skill 模块管理的数据），供 combat（免疫/吸收判定、控制标志影响
    /// 进出战斗与行动）与 ai（<c>Rotation</c> 条件里判断"目标是否带某光环"）调用。
    /// </summary>
    public interface IAuraQuery
    {
        bool HasAura(Id unitId, Id auraDefId);

        /// <summary>该光环当前层数；未生效返回 0。</summary>
        int GetStacks(Id unitId, Id auraDefId);

        /// <summary>合并该单位身上全部 <c>control</c> 类光环的标志位（见 06 第 3.3 节 <c>control</c> 行）。</summary>
        ControlFlags GetControlFlags(Id unitId);

        /// <summary>该单位对指定学派 + 效果原语类型的组合是否免疫（见 06 第 3.3 节 <c>immunity</c> 行）。</summary>
        bool IsImmune(Id unitId, Id school, EffectKind kind);

        /// <summary>
        /// 从该单位身上 <c>absorb</c> 类光环的吸收池中扣减最多 <paramref name="amount"/>，返回实际
        /// 扣减掉的量（可能小于请求量，池耗尽后不再继续扣减；有多个吸收池时按 06 未规定的具体消耗
        /// 顺序由实现自行决定，本契约只约束"返回值 = 实际吸收量"这一语义）。
        /// </summary>
        double ConsumeAbsorb(Id unitId, Id school, double amount);

        /// <summary>该单位当前全部生效光环的 <c>aura_def</c> id 列表（不含层数等细节，只列出"带了哪些"）。</summary>
        IReadOnlyList<Id> GetActiveAuraDefs(Id unitId);
    }
}
