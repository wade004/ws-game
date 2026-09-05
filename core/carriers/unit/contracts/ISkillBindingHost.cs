using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 按 <paramref name="unitId"/>/<paramref name="skillId"/> 查询"该单位是否已学会这个技能"的具名
    /// 委托（惯例同 <c>Core.Carriers.Item.SkillGranter</c>——L3 <c>core/carriers/unit</c> 不得直接
    /// 引用 L2 <c>core/rules/skill</c> 的具体宿主类型，由组装层注入这份查询回调，见
    /// <c>Core.Carriers.Assembly.CarriersAssembly</c> 接线处判断记录）。
    /// </summary>
    public delegate bool KnownSkillQuery(Id unitId, Id skillId);

    /// <summary>
    /// 技能槽位绑定契约（缺口 4：动作条槽位 → 技能 id 的绑定关系，见 10_存档与持久化.md 第 2.2 节
    /// <c>player.skill_bindings: Map&lt;String, Id&gt;</c>）。槽位键是自由字符串（约定
    /// <c>"slot_0".."slot_N"</c>，具体槽数是口味配置项，不由本契约规定），值是已绑定的技能 id。
    /// </summary>
    public interface ISkillBindingHost
    {
        /// <summary>查询 <paramref name="unitId"/> 当前全部槽位绑定；未绑定过任何槽位的单位返回空
        /// 字典（不是 null）。</summary>
        IReadOnlyDictionary<string, Id> GetBindings(Id unitId);

        /// <summary>把 <paramref name="skillId"/> 绑定到 <paramref name="unitId"/> 的
        /// <paramref name="slot"/> 槽位（覆盖该槽位此前的绑定，若有）。<paramref name="skillId"/>
        /// 必须是该单位的已知技能（经 <see cref="KnownSkillQuery"/> 查询），否则绑定被拒绝、返回
        /// false，不改变任何状态。成功时发出 <c>unit.skill_binding_changed</c>。</summary>
        bool Bind(Id unitId, string slot, Id skillId);

        /// <summary>清空 <paramref name="unitId"/> 的 <paramref name="slot"/> 槽位绑定。该槽位本就
        /// 未绑定时视为幂等成功（返回 true，不发事件——没有实际变化）。</summary>
        bool Unbind(Id unitId, string slot);
    }
}
