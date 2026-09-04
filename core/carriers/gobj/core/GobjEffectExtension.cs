using System;
using Core.Carriers.Common;
using Core.Rules.Common;
using Core.Rules.Skill;

namespace Core.Carriers.Gobj
{
    /// <summary>
    /// <see cref="IEffectExtension"/> 六类扩展效果里 <c>open_lock</c> 一项的落地（见 06 第 3.2 节
    /// "对目标 GameObject 尝试开锁（结合 07 的锁规则）"、07 第 3.6 节"tryUnlock……供未直接经 interact
    /// 触发的开锁场景（如 open_lock 效果）复用"）。<see cref="EffectContext.SourceId"/> 是施法者，
    /// <see cref="EffectContext.TargetId"/> 是本次效果的目标——按 06 原文语义即"被开锁的那个
    /// GameObject 实例 id"（<c>open_lock</c> 效果的目标解析在 06 施法管线的目标解析步骤完成，不属于
    /// 本扩展点职责，本类型只管"给定 source/target 之后如何真正尝试开锁"）。其余五类扩展效果
    /// （<c>projectile</c>/<c>summon</c>/<c>create_item</c>/<c>set_world_flag</c>/<c>script</c>）不属于
    /// <c>gobj</c> 模块职责，<see cref="TryHandle"/> 对它们一律返回 false，交由
    /// <c>EffectDispatcher</c>（或另一个 <see cref="IEffectExtension"/> 实现，见该接口顶部"具体实现
    /// 由持有 L3/L4 类型的宿主……注入"——若某游戏需要多类扩展效果同时生效，由游戏组装根自己写一个
    /// 转发多个 <see cref="IEffectExtension"/> 的外壳，不是本类型的职责）兜底。
    /// </summary>
    public sealed class GobjEffectExtension : IEffectExtension
    {
        private readonly IGameObjectHost _host;

        public GobjEffectExtension(IGameObjectHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public bool TryHandle(EffectContext context, out ResolveResult result)
        {
            if (context.Kind != EffectKind.OpenLock)
            {
                result = NoOp(context);
                return false;
            }

            var success = _host.TryUnlock(context.SourceId, context.TargetId);
            result = new ResolveResult(
                success ? HitResult.Hit : HitResult.Miss,
                requestedAmount: 0,
                finalAmount: 0,
                absorbed: 0,
                immune: false,
                isHeal: false,
                steps: new[] { $"open_lock target={context.TargetId} success={success}" });
            return true;
        }

        /// <summary>惯例同 <c>core/carriers/item</c> 的 <c>ItemEffectExtension.NoOp</c>：
        /// <see cref="TryHandle"/> 返回 false 时 <paramref name="context"/> 未处理，占位结果的具体
        /// 内容由调用方（<c>EffectDispatcher</c>）兜底填充（见 <see cref="IEffectExtension"/> 顶部
        /// 注释），本类型只需保证 <c>out</c> 参数有一个非空的合法值。</summary>
        private static ResolveResult NoOp(EffectContext context) =>
            new ResolveResult(HitResult.Hit, 0, 0, 0, immune: false, isHeal: false);
    }
}
