using Core.Rules.Common;

namespace Core.Gameplay.Death
{
    /// <summary>
    /// <c>core/gameplay/death</c> 模块的对外窄契约（见 06_规则层_属性技能战斗AI.md 第 4.6 节、
    /// DECISIONS 拍板 3）：订阅 <c>unit.died</c>（仅玩家单位），按 <see cref="EffectivePolicy"/>
    /// 执行三种死亡复活策略之一。本接口只暴露只读的 <see cref="EffectivePolicy"/> 供调用方/测试
    /// 查询实际生效的策略来源（<see cref="DeathPolicyOptions.Policy"/> 覆盖，或回退
    /// <c>CombatOptions.DeathPolicy</c>），不暴露任何触发执行的方法——本模块完全由事件订阅驱动，
    /// 不需要外部主动调用。
    /// </summary>
    public interface IDeathPolicyHost
    {
        /// <summary>实际生效的死亡复活策略（见 <see cref="DeathPolicyOptions.Policy"/> 判断记录）。</summary>
        RespawnPolicy EffectivePolicy { get; }
    }
}
