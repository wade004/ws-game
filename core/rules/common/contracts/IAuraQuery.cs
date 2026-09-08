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
        /// 顺序由实现自行决定，本契约只约束"返回值 = 实际吸收量"这一语义）。等价于
        /// <c>ConsumeAbsorb(unitId, school, amount, triggerChainDepth: 0)</c>——见带触发链深度参数
        /// 的重载判断记录（C03 收口）。
        /// </summary>
        double ConsumeAbsorb(Id unitId, Id school, double amount);

        /// <summary>
        /// C03 收口（外部审计 7e63d66 第四轮）：吸收池耗尽会移除对应光环实例并发布
        /// <c>aura.removed</c> 事件（见 <c>AuraHost.ConsumeAbsorb</c> 实现），这个移除本应与
        /// <see cref="AuraRemovedEvent"/>/<see cref="ITriggerChainEvent"/> 已经确立的"移除事件携带
        /// 产生它的触发链深度、受 <c>SkillOptions.MaxTriggerDepth</c> 收敛约束"这套机制（N04/RC-01
        /// 收边补齐）保持一致——此前吸收耗尽的移除入口没有深度参数，恒以深度 0（根事件）发布，绕开
        /// 了收敛预算：一个监听 <c>aura.removed</c> 的永久 proc 光环可以借助"造成恰好耗尽自身吸收池
        /// 的伤害"反复触发自己，每次触发都被记成新的根事件链，不受 <c>MaxTriggerDepth</c> 限制。
        /// <para>
        /// 本重载让调用方（<c>Resolver</c> 伤害结算路径）把产生这次吸收扣减的
        /// <see cref="EffectContext.TriggerChainDepth"/> 显式传入，由实现（<c>AuraHost</c>）在吸收
        /// 耗尽移除实例时原样透传给 <c>aura.removed</c> 事件，使这条移除→再触发的链路和其它
        /// 移除入口（<c>dispel</c> 等）一样纳入统一的触发深度收敛预算。用 C#8 默认接口方法转发到
        /// 三参数重载（等价于此前恒 0 的行为）而不是把它做成必须实现的抽象成员：本接口已有多个
        /// combat/expr_host/carriers 测试假实现（改动范围不允许连带修改
        /// <c>core/carriers/item/**</c> 等其它 agent 负责的模块），默认转发对它们是安全的等价降级
        /// ——这些假实现原本就不模拟吸收耗尽的深度传播。<see cref="Core.Rules.Skill.AuraHost"/> 显式
        /// 提供真正实现；<c>RulesAssembly.DeferredAuraQuery</c> 代理同样显式转发（不能依赖默认接口
        /// 方法的隐式转发，否则会绕开代理转到 <c>Real</c> 的路径，深度参数会在代理层被默认方法悄悄
        /// 丢回 0——代理必须对每个成员显式转发，见该类型判断记录）。
        /// </para>
        /// </summary>
        double ConsumeAbsorb(Id unitId, Id school, double amount, int triggerChainDepth) =>
            ConsumeAbsorb(unitId, school, amount);

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

        /// <summary>
        /// C08 收口（外部审计 7e63d66 第四轮）：<c>AllowMultiSourceTiming == false</c>（默认）时，
        /// 同一 <c>(targetId, auraDefId)</c> 的多次施加共享同一个光环实例；一旦叠层超过
        /// <c>AuraDef.MaxStacks</c> 且 <c>SkillOptions.StackOverflowPolicy == Replace</c>，实现会
        /// 摘除旧实例、创建一个全新句柄的实例（见 <c>AuraHost.ReapplyExisting</c> 判断记录）——旧
        /// 句柄从此在光环系统内部已经不存在，但当初拿到那个旧句柄的调用方（如
        /// <c>core/carriers/item.EquipmentHost</c> 按装备实例记录自己"授予了哪个光环句柄"）对此一无
        /// 所知，继续把旧句柄当作仍然有效的引用计数依据——另一件同样援引同一光环槽位的装备卸下时，
        /// 会按自己持有的（可能已经是新句柄的）引用计数错误判断"是否还有其它来源"，把本该保留的
        /// 光环提前清空，或反过来让本该清空的旧句柄悬空引用永远不会被回收（外部审计 C08 复现）。
        /// <para>
        /// 本事件在 Replace 真正发生、旧实例已从光环系统摘除且新实例已创建完成的同一步同步触发
        /// （不经 <c>IEventBus</c> 异步队列——调用方必须能在自己发起的
        /// <c>ApplyAura</c> 调用返回之前就已经原子更新完自己的句柄映射，不能有一个"旧句柄已经
        /// 失效、但订阅者还没来得及收到通知"的中间态窗口），携带
        /// <c>(targetId, defId, oldInstanceId, newInstanceId)</c>，供订阅者把自己记录里全部仍引用
        /// <c>oldInstanceId</c> 的条目原子迁移到 <c>newInstanceId</c>。
        /// </para>
        /// <para>
        /// 默认空实现（<c>add</c>/<c>remove</c> 均不做任何事）：本接口已有多个 combat/expr_host/
        /// carriers 测试假实现与非 <c>core/rules/skill</c> 的其它实现（改动范围不允许连带修改它们），
        /// 它们本就不会触发 <c>Replace</c> 策略，默认空实现对它们是安全的等价降级——订阅者调用
        /// <c>+=</c> 不会抛异常，只是永远不会被触发。<see cref="Core.Rules.Skill.AuraHost"/> 提供
        /// 真正实现；<c>RulesAssembly.DeferredAuraQuery</c> 代理同样显式转发 <c>add</c>/<c>remove</c>
        /// 到 <c>Real</c>（不能依赖默认接口成员的隐式转发，见该类型判断记录、
        /// <see cref="ConsumeAbsorb(Id, Id, double, int)"/> 同一惯例）。
        /// </para>
        /// </summary>
        event Action<Id, Id, Id, Id> InstanceReplaced
        {
            add { }
            remove { }
        }

        /// <summary>
        /// CORE-170-01 根治（architecture/落地计划/audit-8160178-20260908，P2）：返回该单位当前
        /// 生效的 <paramref name="auraDefId"/> 光环实例句柄；未生效返回 null。供跨来源引用计数账本
        /// （<see cref="Core.Rules.Skill.AuraHandleLedger"/>）在"确认这个 <c>aura_def</c> 已经生效、
        /// 需要为一个新来源登记一份引用"时使用——不能通过重新调用 <see cref="IEffectSink.ApplyAura"/>
        /// 换取句柄，那会被 <c>AuraHost.ReapplyExisting</c> 当作又一次独立施加而叠加层数（见该方法
        /// stacking 语义），种族/职业被动一类"只想确认自己也持有一份既有共享实例引用、不想再叠一层"
        /// 的重放场景必须绕开 <c>ApplyAura</c>。默认返回 null（不支持）：本接口已有多个
        /// combat/expr_host/carriers 测试假实现（改动范围不允许连带修改它们），默认降级对它们是
        /// 安全的等价空实现（本来就不参与跨来源账本）。<see cref="Core.Rules.Skill.AuraHost"/> 提供
        /// 真正实现；<c>RulesAssembly.DeferredAuraQuery</c> 代理同样显式转发（不能依赖默认接口方法的
        /// 隐式转发，见 <see cref="ConsumeAbsorb(Id, Id, double, int)"/> 同一惯例）。
        /// </summary>
        AuraInstanceRef? TryGetInstanceRef(Id unitId, Id auraDefId) => null;
    }
}
