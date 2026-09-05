using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 五类不属于本模块职责的 Effect 原语（<c>summon</c>/<c>open_lock</c>/<c>create_item</c>/
    /// <c>set_world_flag</c>/<c>script</c>，见 06 第 3.2 节）的落地出口。
    /// 这五类原语分别需要 L3 载体层（召唤物/物品）、L4 玩法层（GameObject 开锁）或
    /// 脚本钩子（<c>found.hook</c>）才能真正落地，均不在 L2 规则层依赖范围内（见 01 第 3 节
    /// 依赖矩阵"L2 只允许依赖 L0/L1"），因此 <c>core/rules/skill</c> 只声明这一扩展点，具体
    /// 实现由持有 L3/L4 类型的宿主（游戏层或更上层集成任务）注入。未注入
    /// <see cref="IEffectExtension"/>（构造参数为 null）或注入后 <see cref="TryHandle"/> 返回
    /// false 时，<c>EffectDispatcher</c> 记一条诊断警告并返回一个"未处理"的
    /// <see cref="ResolveResult"/>（不抛异常，不阻断其余效果继续执行）。
    /// <para>
    /// 判断记录（收边任务：<c>projectile</c> 移出本扩展点）：<c>projectile</c> 原本也在这六类
    /// 之列，收边任务给它单开了专用的依赖倒置接口 <see cref="Core.Rules.Common.IProjectileSpawner"/>
    /// （见该接口判断记录），不再经本扩展点分派——因为投射物命中是"跨多个 tick 才发生"的异步
    /// 结果（不像 <c>summon</c>/<c>create_item</c> 等在效果落地的同一次调用内就能完成），
    /// <see cref="IProjectileSpawner.Spawn"/> 需要额外接收一个 <see cref="Core.Rules.Common.
    /// IEffectSink"/> 回调供命中时反向调用，这个签名形状超出了本接口"同步处理并返回
    /// <see cref="ResolveResult"/>"的设计（<see cref="TryHandle"/> 是同步、当场返回结果的模型，
    /// 不适合"结果在未来某个 tick 才产生"的场景）。
    /// </para>
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
