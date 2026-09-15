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

        /// <summary>
        /// T-N3-6 新增（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 8；
        /// 06 第 3.3 节 2026-09-14 修订段）：该单位是否对指定控制类别（<see cref="ControlCategoryValues.All"/>
        /// 六值之一，如 <c>stun</c>/<c>root</c>）静态免疫，来源同样是内容配置（<c>creature.tier_definition</c>/
        /// <c>creature.template</c>）而非光环。
        /// <para>
        /// 默认接口实现（C# 8+ default interface member，ABI 安全的新增方式，见硬性规则）按"旧布尔
        /// 语义"转发到 <see cref="GetControlImmunity"/>：历史上唯一能表达"控制免疫"的入口是
        /// <see cref="GetControlImmunity"/> 返回的 <see cref="ControlFlags"/>（<c>tier.control_immune</c>
        /// 为真时的全部标志位，或按单项标志声明的 <c>control.&lt;flag&gt;</c> 条目），本默认实现把
        /// "该单位存在任意静态控制标志位免疫"等价为"对任意类别都免疫"（<c>GetControlImmunity(unitId)
        /// != ControlFlags.None</c>）——对尚未升级到按类别精细声明的既有实现，这是"未分类退回旧布尔
        /// 语义"这一约定在实现方一侧的自然结果，不需要逐一改动。
        /// </para>
        /// <para>
        /// 真正支持按类别精细声明的实现（本仓库 <c>CreatureImmunityProvider</c>）应显式覆盖本方法
        /// （见 <c>InterfaceDefaultMemberForwardingTests</c> 通用门禁：production 程序集内的实现方
        /// 必须显式转发/覆盖带默认实现的接口成员，不允许悄悄吃掉默认值）。
        /// </para>
        /// </summary>
        bool IsControlCategoryImmune(Id unitId, string category) => GetControlImmunity(unitId) != ControlFlags.None;
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

        /// <summary>显式转发（不吃默认实现——同 <see cref="InterfaceDefaultMemberForwardingTests"/>
        /// 门禁要求）：与 <see cref="GetControlImmunity"/> 同一"一律不免疫"语义，默认值本身即正确
        /// 结果，没有更好的值可转发。</summary>
        public bool IsControlCategoryImmune(Id unitId, string category) => false;
    }
}
