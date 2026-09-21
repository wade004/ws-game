using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// ADR-0059（消费方反馈第三批第 5 条"普通攻击缺少框架原生执行机制"）：普通攻击挥击间隔的
    /// "无武器"回退数据源只读查询出口——<c>Core.Rules.Combat.AutoAttackHost</c> 决定某单位挥击
    /// 计时的周期时，优先取 <see cref="IWeaponDamageQuery.GetWeaponAttackIntervalSeconds"/>（已装备
    /// 武器的 <c>weapon_profile.speed</c>），该方法返回 <c>null</c>（未装备武器）时才查询本接口
    /// （生物模板 <c>attack_interval</c> 字段，见 07 第 2.1 节新增可选字段）——两者都是 <c>null</c>
    /// 时按"运行时不静默降级"处理，不取任何隐含默认值。
    /// <para>
    /// 依赖倒置理由同 <see cref="IStaticImmunityProvider"/>/<see cref="IWeaponDamageQuery"/> 判断
    /// 记录："生物模板"数据（<c>creature.template</c>）属于 <c>core/carriers/creature</c>（L3），
    /// <c>core/rules</c>（L2）不得反向依赖 L3；本接口由 <c>core/carriers/creature
    /// .CreatureAttackIntervalProvider</c> 实现，经 <c>Core.Rules.Assembly
    /// .DeferredAttackIntervalProvider</c> 延迟绑定注入 <see cref="Core.Rules.Combat.AutoAttackHost"/>
    /// （同 <c>DeferredWeaponDamageQuery</c>/<c>DeferredEffectExtension</c> 判断记录"A 需要 B、B 的
    /// 真实实现却要等 A 构造完成之后才能装配出来"同一种循环，见 <c>CarriersAssembly</c> 装配顺序：
    /// <c>CreatureFactory</c>——本接口真实实现的数据来源——要到 <c>RulesAssembly</c> 构造完成之后
    /// 才存在）。
    /// </para>
    /// <para>
    /// 可选依赖：<see cref="Core.Rules.Combat.AutoAttackHost"/> 把本契约作为可选构造参数（默认
    /// <see cref="NullAttackIntervalFallbackProvider.Instance"/>），未注入时（如只装配
    /// <c>core/rules</c> 不装配 <c>core/carriers</c> 的纯 L2 测试/集成）一律返回 <c>null</c>，等价于
    /// "没有生物模板回退数据"——不会抛异常，也不会静默假造一个间隔，调用方仍按既有诊断口径处理。
    /// </para>
    /// </summary>
    public interface IAttackIntervalFallbackProvider
    {
        /// <summary>
        /// <paramref name="unitId"/> 所属生物模板登记的普通攻击挥击间隔（秒，<c>creature.template
        /// .attack_interval</c>，见 <c>Core.Carriers.Creature.CreatureTemplate.AttackInterval</c>）。
        /// <paramref name="unitId"/> 不是生物单位（如玩家单位——玩家没有"生物模板"概念，普通攻击
        /// 完全依赖武器数据）、或该单位没有对应的生物模板、或模板未声明本字段时返回 <c>null</c>。
        /// </summary>
        double? GetAttackIntervalSeconds(Id unitId);
    }

    /// <summary><see cref="IAttackIntervalFallbackProvider"/> 的空对象默认实现：一律返回
    /// <c>null</c>（见接口顶部判断记录"未注入时一律返回 null"）。<see
    /// cref="Core.Rules.Combat.AutoAttackHost"/> 的 <c>attackIntervalFallback</c> 构造参数缺省时取
    /// 本单例，调用方内部代码因此不需要对该字段做 null 判断。</summary>
    public sealed class NullAttackIntervalFallbackProvider : IAttackIntervalFallbackProvider
    {
        public static readonly NullAttackIntervalFallbackProvider Instance = new NullAttackIntervalFallbackProvider();

        private NullAttackIntervalFallbackProvider()
        {
        }

        public double? GetAttackIntervalSeconds(Id unitId) => null;
    }
}
