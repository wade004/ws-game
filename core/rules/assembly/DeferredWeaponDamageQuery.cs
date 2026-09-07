using Core.Foundation.Common;
using Core.Rules.Common;

namespace Core.Rules.Assembly
{
    /// <summary>
    /// <see cref="IWeaponDamageQuery"/> 的延迟绑定代理（RC-11 收边补齐，惯例同
    /// <see cref="DeferredEffectExtension"/> 判断记录——同一类"A 需要 B、B 的真实实现却要等 A
    /// 构造完成之后才能装配出来"的循环，用代理打破）。
    /// <para>
    /// 具体循环：<c>Core.Rules.Skill.SkillHost</c>（L2）在 <see cref="RulesAssembly"/> 构造期就需要
    /// 一份 <see cref="IWeaponDamageQuery"/> 才能正确处理 <c>weapon_damage_pct</c> 效果原语，但真实
    /// 实现（<c>core/carriers/item.EquipmentHost</c>，L3）恰恰需要先拿到 <see cref="RulesAssembly"/>
    /// 构造完成后才存在的 <c>Rules.Stats</c>/<c>Rules.Skill.EffectSink</c> 才能装配出来（见
    /// <c>CarriersAssembly</c> 构造 <c>EquipmentHost</c> 那一步）。<see cref="RulesAssembly"/> 先用
    /// 一份"暂未绑定真实实现"的代理构造 <c>SkillHost</c>，L3 组装完成后（<c>CarriersAssembly</c>）
    /// 再调 <see cref="Bind"/> 换上真实的 <c>EquipmentHost</c>——<c>EffectDispatcher</c> 早已持有的是
    /// 这份代理本身的引用，绑定后无需重新构造 <c>SkillHost</c>。
    /// </para>
    /// <para>
    /// 与 <see cref="DeferredEffectExtension"/> 同一处理：<see cref="Bind"/> 之前调用
    /// <see cref="GetWeaponBaseDamage"/> 不抛异常，返回 0（<see cref="IWeaponDamageQuery.GetWeaponBaseDamage"/>
    /// 契约本就把"无武器"定义为返回 0，未绑定与"绑定后查到无武器"在调用方看来是同一种合法结果，
    /// 不需要用异常区分"这是不应该发生的用法错误"）。
    /// </para>
    /// </summary>
    public sealed class DeferredWeaponDamageQuery : IWeaponDamageQuery
    {
        private IWeaponDamageQuery? _real;

        /// <summary>换上真实的 <see cref="IWeaponDamageQuery"/> 实现（<c>CarriersAssembly</c> 构造完
        /// <c>EquipmentHost</c> 后调用）。可重复调用（覆盖上一次绑定），不做"只能绑定一次"的限制，
        /// 惯例同 <see cref="DeferredEffectExtension.Bind"/>。</summary>
        public void Bind(IWeaponDamageQuery real) => _real = real;

        public double GetWeaponBaseDamage(Id unitId) => _real?.GetWeaponBaseDamage(unitId) ?? 0.0;
    }
}
