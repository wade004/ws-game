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

        /// <summary>
        /// T-N3-7 补（周期效果来源缺失冻结缓存清理）：光环实例 <paramref name="auraInstanceId"/> 已经
        /// 从光环系统移除（到期、<c>RemoveAura</c>、<c>Dispel</c>、吸收耗尽、叠加溢出 <c>Replace</c>、
        /// 目标销毁——见 <c>Core.Rules.Skill.AuraHost.RemoveInstanceInternal</c> 判断记录"统一收口"）
        /// 时调用，供实现方清理该实例在"周期效果来源缺失冻结"（见 ADR-0031 决策 4；06 第 3.3 节
        /// 2026-09-14 修订段）机制里缓存的"最后一跳值"，避免长会话内缓存随光环实例产生/移除无界
        /// 增长、以及（理论上）实例 id 被复用时读到陈旧冻结值。用 C#8 默认接口方法（空实现）而不是
        /// 必须实现的抽象成员：本接口已有多个 <c>core/gameplay</c>/测试假实现（改动范围不允许连带
        /// 修改它们，且它们根本不涉及"周期效果冻结缓存"这个 <c>core/rules/skill</c> 内部概念），
        /// 默认空实现对它们是安全的等价降级（本来就没有任何缓存需要清理）。
        /// <see cref="Core.Rules.Skill.EffectDispatcher"/> 提供真正实现；
        /// <see cref="Core.Rules.Skill.AuraHost"/> 在 <c>RemoveInstanceInternal</c> 统一调用，唯一
        /// 收口点覆盖全部移除路径，不需要逐个移除入口分别接线。
        /// </summary>
        void ForgetPeriodicCache(Id auraInstanceId) { }

        /// <summary>
        /// T-N3-7 补：清空全部"周期效果来源缺失冻结"缓存（见 <see cref="ForgetPeriodicCache"/> 判断
        /// 记录）。判断记录（未在生产装配路径接线）：本仓库当前没有"同一个 <c>AuraHost</c>/
        /// <c>EffectDispatcher</c> 实例原地批量清空后继续复用"这一生产路径——世界级重置
        /// （<c>IWorldSim.ClearAll</c>）已经对每个实体派发 <c>entity.destroyed</c>，经既有
        /// <c>AuraHost.OnEntityDestroyed</c> 订阅逐个走到 <c>RemoveInstanceInternal</c>→
        /// <see cref="ForgetPeriodicCache"/>，最终清空的是同一批缓存条目；读档/场景重建则是构造
        /// 全新的 <c>SkillHost</c>（连带全新的 <c>AuraHost</c>/<c>EffectDispatcher</c>，见
        /// <c>SkillHost</c> 构造函数），旧实例（与其缓存）整体被丢弃，不存在"新 <c>AuraHost</c> 配
        /// 旧 <c>EffectDispatcher</c> 缓存"这种搭配。本方法目前只供测试直接验证"全清"语义与未来若
        /// 出现"原地重置复用"这类新路径时使用，默认空实现同 <see cref="ForgetPeriodicCache"/> 惯例。
        /// </summary>
        void ClearPeriodicCache() { }
    }
}
