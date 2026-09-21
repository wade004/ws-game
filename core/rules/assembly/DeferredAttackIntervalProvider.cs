using Core.Foundation.Common;
using Core.Rules.Common;

namespace Core.Rules.Assembly
{
    /// <summary>
    /// <see cref="IAttackIntervalFallbackProvider"/> 的延迟绑定代理（ADR-0059，惯例同
    /// <see cref="DeferredWeaponDamageQuery"/>/<see cref="DeferredEffectExtension"/> 判断记录——同
    /// 一类"A 需要 B、B 的真实实现却要等 A 构造完成之后才能装配出来"的循环，用代理打破）。
    /// <para>
    /// 具体循环：<c>Core.Rules.Combat.AutoAttackHost</c>（L2）在 <see cref="RulesAssembly"/> 构造期
    /// 就需要一份 <see cref="IAttackIntervalFallbackProvider"/> 才能在无武器时回退读生物模板
    /// <c>attack_interval</c>，但真实实现（<c>core/carriers/creature.CreatureAttackIntervalProvider</c>，
    /// L3）需要的 <c>ICreatureTemplateQuery</c>（由 <c>CreatureFactory</c> 兼实现）要到
    /// <see cref="RulesAssembly"/> 构造完成后、<c>CarriersAssembly</c> 第 5 步才存在。
    /// <see cref="RulesAssembly"/> 先用一份"暂未绑定真实实现"的代理构造
    /// <c>Core.Rules.Combat.AutoAttackHost</c>，L3 组装完成后（<c>CarriersAssembly</c>）再调
    /// <see cref="Bind"/> 换上真实的 <c>CreatureAttackIntervalProvider</c>——
    /// <c>AutoAttackHost</c> 早已持有的是这份代理本身的引用，绑定后无需重新构造。
    /// </para>
    /// <para>
    /// 与 <see cref="DeferredWeaponDamageQuery"/> 同一处理：<see cref="Bind"/> 之前调用
    /// <see cref="GetAttackIntervalSeconds"/> 不抛异常，返回 <c>null</c>（<see
    /// cref="IAttackIntervalFallbackProvider.GetAttackIntervalSeconds"/> 契约本就把"无回退数据"
    /// 定义为返回 <c>null</c>，未绑定与"绑定后查到该单位没有生物模板/字段"在调用方看来是同一种合法
    /// 结果，不需要用异常区分）。
    /// </para>
    /// </summary>
    public sealed class DeferredAttackIntervalProvider : IAttackIntervalFallbackProvider
    {
        private IAttackIntervalFallbackProvider? _real;

        /// <summary>换上真实的 <see cref="IAttackIntervalFallbackProvider"/> 实现（<c>CarriersAssembly</c>
        /// 构造完 <c>Creatures</c>（<c>CreatureFactory</c>）后调用）。可重复调用（覆盖上一次绑定），
        /// 不做"只能绑定一次"的限制，惯例同 <see cref="DeferredWeaponDamageQuery.Bind"/>。</summary>
        public void Bind(IAttackIntervalFallbackProvider real) => _real = real;

        public double? GetAttackIntervalSeconds(Id unitId) => _real?.GetAttackIntervalSeconds(unitId);
    }
}
