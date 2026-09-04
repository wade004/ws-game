using Core.Rules.Common;
using Core.Rules.Skill;

namespace Core.Rules.Assembly
{
    /// <summary>
    /// <see cref="IEffectExtension"/> 的延迟绑定代理（阶段 3 整理"事项四"，惯例同
    /// <see cref="RulesAssembly"/> 内部的 <c>DeferredAuraQuery</c>——同一类"A 需要 B、B 的真实实现
    /// 却要等 A 构造完成之后才能装配出来"的循环，用代理打破）。
    /// <para>
    /// 具体循环：<see cref="SkillHost"/>（L2）在 <see cref="RulesAssembly"/> 构造期就需要一份
    /// <see cref="IEffectExtension"/> 才能正确处理 <c>create_item</c>/<c>open_lock</c>/<c>summon</c>
    /// 三类效果原语，但这三类的真实实现（<c>core/carriers/item.ItemEffectExtension</c>/
    /// <c>core/carriers/gobj.GobjEffectExtension</c>/<c>core/carriers/summon.SummonEffectExtension</c>，
    /// L3）恰恰需要先拿到 <see cref="RulesAssembly"/> 构造完成后才存在的
    /// <c>Ai.RegisterUnit</c>/<c>Ai.SetRotation</c>（供 <c>AiRegistrar</c> 委托）才能装配出
    /// <c>CreatureFactory</c>/<c>SummonHost</c>。本类型让 <see cref="RulesAssembly"/> 先用一份"暂未绑定
    /// 真实实现"的代理构造 <see cref="SkillHost"/>，L3 组装完成后（<c>CarriersAssembly</c>）再调
    /// <see cref="Bind"/> 换上真实的组合实现——<see cref="EffectDispatcher"/> 早已持有的是这份代理
    /// 本身的引用，绑定后无需重新构造 <see cref="SkillHost"/>。
    /// </para>
    /// <para>
    /// 与 <c>DeferredAuraQuery</c> 的一处刻意不同：<see cref="Bind"/> 之前调用 <see cref="TryHandle"/>
    /// 不抛异常，直接返回 <c>false</c>——"未处理"本就是 <see cref="IEffectExtension"/> 契约里定义好的
    /// 合法状态（见该接口注释"未注入……或注入后 TryHandle 返回 false 时，EffectDispatcher 记一条
    /// 诊断警告并返回一个'未处理'的 ResolveResult，不抛异常"），不是"不应该发生的用法错误"，因此不需要
    /// 用异常去防御——调用方（<see cref="RulesAssembly"/> 构造完成、<c>CarriersAssembly</c> 完成 L3
    /// 装配之前的这段时间窗口内，若真的有 <c>create_item</c>/<c>open_lock</c>/<c>summon</c> 效果被
    /// 结算）本就会走到这条既有的兜底路径。
    /// </para>
    /// </summary>
    public sealed class DeferredEffectExtension : IEffectExtension
    {
        private IEffectExtension? _real;

        /// <summary>换上真实的（通常是组合了多个来源的）<see cref="IEffectExtension"/> 实现。可重复
        /// 调用（覆盖上一次绑定），不做"只能绑定一次"的限制——同 <c>DeferredAuraQuery.Bind</c> 惯例
        /// 之外的额外宽松点，见类型顶部判断记录"不抛异常"。</summary>
        public void Bind(IEffectExtension real) => _real = real;

        public bool TryHandle(EffectContext context, out ResolveResult result)
        {
            if (_real != null)
            {
                return _real.TryHandle(context, out result);
            }

            result = null!;
            return false;
        }
    }
}
