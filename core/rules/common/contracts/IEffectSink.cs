using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// 效果落地出口（见 06 第 7 节 <c>EffectSink</c>）。由 <c>core/rules/skill</c> 实现，供
    /// combat/ai 调用（applyAura 用于 <c>apply_aura</c> 效果原语落地光环实例；ai 模块用于状态判断
    /// 前置查询依赖 <see cref="IAuraQuery"/>，见 README 矩阵）。
    /// <para>
    /// 判断记录（W1 收边补齐，A3 审计 #9）：本接口的 <see cref="ApplyAura"/> 刻意<b>不</b>扩展
    /// "标签"参数——本接口被 <c>core/gameplay</c>（W1 写入范围之外，另有并行任务在改）等多处实现，
    /// 扩大公开契约的形状会波及这些实现方。周期性光环效果的标签透传改在 <c>core/rules/skill</c>
    /// 内部完成：<c>EffectDispatcher.ApplyAuraEffectPrimitive</c> 直接调用具体类型
    /// <c>AuraHost.ApplyAura</c>（非本接口方法）的重载，携带 <c>EffectContext.Tags</c>，见
    /// <c>AuraHost.ApplyAura</c>/<c>AuraInstanceState.Tags</c> 判断记录。经本接口
    /// （<c>IEffectSink.ApplyAura</c>，如 <c>core/carriers/item</c> 装备特效授予、种族被动光环）
    /// 施加的光环仍按空标签处理，与本次改动之前完全一致——这些调用点本就没有"技能标签"这个概念
    /// 可传。
    /// </para>
    /// </summary>
    public interface IEffectSink
    {
        ResolveResult ApplyEffect(EffectContext context);

        /// <summary>施加一个光环实例（见 06 第 7 节 <c>applyAura(targetId, auraDefId, sourceId)</c>，
        /// <paramref name="durationOverride"/> 为任务书拍板补充参数，对应 06 第 3.2 节 <c>apply_aura</c>
        /// 效果原语参数"持续时间覆盖（可选）"——原始签名遗漏了这一在 06 §3.2 已声明存在的参数，
        /// 此处补齐；null 表示使用 <c>aura_def.duration</c> 的默认值）。</summary>
        AuraInstanceRef ApplyAura(Id targetId, Id auraDefId, Id sourceId, double? durationOverride = null);

        void RemoveAura(Id targetId, AuraInstanceRef auraInstanceRef);
    }
}
