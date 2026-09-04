using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// 效果落地出口（见 06 第 7 节 <c>EffectSink</c>）。由 <c>core/rules/skill</c> 实现，供
    /// combat/ai 调用（applyAura 用于 <c>apply_aura</c> 效果原语落地光环实例；ai 模块用于状态判断
    /// 前置查询依赖 <see cref="IAuraQuery"/>，见 README 矩阵）。
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
