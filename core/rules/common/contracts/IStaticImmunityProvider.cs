using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// 内容驱动的"静态免疫"只读查询出口（阶段 3 整理：生物静态免疫接入结算，见任务书"事项三"）。
    /// 区别于光环系统的"动态免疫"——<see cref="IAuraQuery.IsImmune"/>/<see cref="IAuraQuery.GetControlFlags"/>
    /// 反映的是当前生效的免疫/控制类光环，随光环施加/移除实时变化；本契约反映的是不随光环增删而
    /// 变化的、来自 <c>creature.template</c>/<c>creature.tier_definition</c> 一类内容配置的固定免疫
    /// （见 06 第 3.9 节"Boss/精英级免疫标志"）。由 <c>core/carriers/creature</c> 实现
    /// （<c>CreatureImmunityProvider</c>，读 <c>CreatureUnit.Immunities</c>），供
    /// <c>core/rules/combat</c>（<c>Resolver</c> 结算免疫判定）与 <c>core/rules/skill</c>
    /// （<c>AuraHost.IsImmune</c> 光环免疫判定、<c>AuraHost.ApplyAura</c> 对 <c>control</c> 类光环的
    /// 生效判定）在查询光环免疫之外叠加查询。
    /// <para>
    /// 可选依赖：<see cref="Resolver"/>/<see cref="AuraHost"/> 均把本契约作为可选构造参数（默认
    /// <see cref="NullStaticImmunityProvider.Instance"/>），未注入时一律返回 false/None，不影响任何
    /// 现有行为——生物载体层（L3）是否接入完全由组装根（<c>RulesAssembly</c>）决定，L2 规则层本身
    /// 不产生对 L3 的编译期依赖（<c>IStaticImmunityProvider</c> 只是一个 L2 契约，真正的实现类在
    /// L3，构造期经接口注入，方向仍是"上层依赖下层契约、下层实现向上注入"）。
    /// </para>
    /// </summary>
    public interface IStaticImmunityProvider
    {
        /// <summary>该单位对指定学派 + 效果原语类型的组合是否免疫（语义同
        /// <see cref="IAuraQuery.IsImmune"/>，来源是内容配置而非光环）。</summary>
        bool IsImmune(Id unitId, Id school, EffectKind kind);

        /// <summary>该单位静态免疫的控制标志位组合——返回值里置位的标志，控制类光环对该单位一律
        /// 不生效（见 <see cref="AuraHost.ApplyAura"/> 对 <c>control</c> 类 <c>AuraEffect</c> 的处理）。</summary>
        ControlFlags GetControlImmunity(Id unitId);
    }

    /// <summary><see cref="IStaticImmunityProvider"/> 的空对象默认实现：一律不免疫（见接口顶部
    /// 判断记录"未注入时一律返回 false/None"）。<see cref="Resolver"/>/<see cref="AuraHost"/> 的
    /// <c>staticImmunity</c> 构造参数缺省时取本单例，调用方内部代码因此不需要对该字段做 null 判断。</summary>
    public sealed class NullStaticImmunityProvider : IStaticImmunityProvider
    {
        public static readonly NullStaticImmunityProvider Instance = new NullStaticImmunityProvider();

        private NullStaticImmunityProvider()
        {
        }

        public bool IsImmune(Id unitId, Id school, EffectKind kind) => false;

        public ControlFlags GetControlImmunity(Id unitId) => ControlFlags.None;
    }
}
