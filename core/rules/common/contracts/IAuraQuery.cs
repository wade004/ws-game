using System;
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

        /// <summary>
        /// 集成任务补齐的契约缺口：该单位当前生效的全部 <c>spell_mod</c> 引用（见 06 第 3.5 节
        /// "通过 apply_aura 附带 spell_mod 类型的 AuraEffect 生效"），供 <c>SpellModResolver</c>
        /// 一类聚合器收集后按 <see cref="SkillFilter"/> 过滤应用。<c>core/rules/skill</c> 模块内部
        /// 早已有等价实现（<c>AuraHost.GetActiveSpellModRefs</c>），本次把它提升到共享契约上，
        /// 供 combat/ai 等其它模块也能读到——原契约只声明了免疫/吸收/控制/层数四类查询，遗漏了
        /// SpellMod 引用这一光环状态。用 C#8 默认接口方法（返回空列表）而不是必须实现的抽象
        /// 成员：本接口已有 <c>combat</c> 模块测试假实现 <c>FakeAuraQuery</c>（改动范围不允许连带
        /// 修改 <c>combat</c>），默认空列表对它是安全的等价降级（本来就不产生任何 SpellMod）。
        /// </summary>
        IReadOnlyList<Id> GetActiveSpellModRefs(Id unitId) => Array.Empty<Id>();

        /// <summary>
        /// 集成任务补齐的契约缺口：该单位身上是否存在把 <paramref name="skillId"/> 重定向到另一个
        /// 技能的 <c>override_skill</c> 光环效果（见 06 第 3.3 节），存在时返回重定向目标技能 id，
        /// 否则返回 null。<c>core/rules/skill</c> 模块内部早已有等价实现（原名
        /// <c>AuraHost.ResolveOverride</c>，本次改名为与本方法一致的 <c>ResolveSkillOverride</c>
        /// 并提升到共享契约上），供 <c>combat</c>/<c>ai</c> 等模块判断"这个技能实际会释放成哪一个"
        /// 时复用，不必各自重新实现一遍光环遍历。默认返回 null（无重定向），惯例同
        /// <see cref="GetActiveSpellModRefs"/>。
        /// </summary>
        Id? ResolveSkillOverride(Id unitId, Id skillId) => null;
    }
}
