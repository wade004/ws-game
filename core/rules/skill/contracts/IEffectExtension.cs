using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 六类不属于本模块职责的 Effect 原语（<c>projectile</c>/<c>summon</c>/<c>open_lock</c>/
    /// <c>create_item</c>/<c>set_world_flag</c>/<c>script</c>，见 06 第 3.2 节）的落地出口。
    /// 这六类原语分别需要 L3 载体层（抛射物/召唤物/物品）、L4 玩法层（GameObject 开锁）或
    /// 脚本钩子（<c>found.hook</c>）才能真正落地，均不在 L2 规则层依赖范围内（见 01 第 3 节
    /// 依赖矩阵"L2 只允许依赖 L0/L1"），因此 <c>core/rules/skill</c> 只声明这一扩展点，具体
    /// 实现由持有 L3/L4 类型的宿主（游戏层或更上层集成任务）注入。未注入
    /// <see cref="IEffectExtension"/>（构造参数为 null）或注入后 <see cref="TryHandle"/> 返回
    /// false 时，<c>EffectDispatcher</c> 记一条诊断警告并返回一个"未处理"的
    /// <see cref="ResolveResult"/>（不抛异常，不阻断其余效果继续执行）。
    /// </summary>
    public interface IEffectExtension
    {
        /// <summary>
        /// 尝试处理 <paramref name="context"/>（<see cref="EffectContext.Kind"/> 恒为上述六类之一）。
        /// 返回 true 时 <paramref name="result"/> 为本次处理的结果；返回 false 时
        /// <paramref name="result"/> 的内容由调用方（<c>EffectDispatcher</c>）兜底填充。
        /// </summary>
        bool TryHandle(EffectContext context, out ResolveResult result);
    }
}
