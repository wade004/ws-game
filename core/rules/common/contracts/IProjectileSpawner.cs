namespace Core.Rules.Common
{
    /// <summary>
    /// 依赖倒置接口（同 <c>core/carriers/common/contracts/ILootRoller.cs</c> 顶部判断记录"依赖
    /// 倒置"）：<c>projectile</c> 效果原语（06 第 3.2 节"生成一个 <c>Projectile</c> 实体，见 05"）
    /// 需要真正生成一个 05 第 1.4 节定义的 <c>Projectile</c> 实体并驱动它逐 tick 飞行，但
    /// <c>Projectile</c> 属于 L3 载体层（<c>core/carriers/projectile</c>，见 01 依赖矩阵"L2 只允许
    /// 依赖 L0/L1"），<c>core/rules/skill</c>（L2）不得直接引用 L3 类型。本接口是"生成一个投射物"
    /// 这一最小能力的契约，由 L3 <c>core/carriers/projectile.ProjectileHost</c> 实现，组装期
    /// （<c>Core.Carriers.Assembly.CarriersAssembly</c>）注入给 <see cref="Core.Rules.Skill.SkillHost"/>
    /// 构造函数（经 <c>Core.Rules.Assembly.RulesAssembly</c> 转手），最终换入
    /// <c>core/rules/skill/core/EffectDispatcher.cs</c> 处理 <see cref="EffectKind.Projectile"/>
    /// 效果原语的落地出口。
    /// </summary>
    public interface IProjectileSpawner
    {
        /// <summary>
        /// 按 <paramref name="context"/> 生成一个投射物：<see cref="EffectContext.SourceId"/> 是
        /// 发射者（对应 05 第 1.4 节 <c>Projectile.sourceUnitId</c>），
        /// <see cref="EffectContext.TargetId"/> 是瞄准目标（用于计算初始飞行方向；<c>homing</c>
        /// 飞行方式下逐 tick 重新瞄准该目标当前位置），<see cref="EffectContext.SkillId"/> 是
        /// 关联的技能上下文（对应 <c>Projectile.skillContextId</c>，供命中后重新结算效果时的
        /// SpellMod 过滤等使用），<see cref="EffectContext.Params"/> 携带飞行方式
        /// （<c>travel_mode</c>）、命中行为（<c>hit_behavior</c>）与命中后效果列表
        /// （<c>on_hit_effects</c>，形状同 <c>skill.def.effects[]</c>）等参数，具体字段形状见
        /// <c>core/carriers/projectile/README.md</c>。
        /// </summary>
        /// <param name="effectSink">
        /// 命中后把 <c>on_hit_effects</c> 逐项交回 L2 效果管线（<see cref="IEffectSink.ApplyEffect"/>）
        /// 的回调出口——实现方需要保存这个引用直到投射物命中/到期（飞行跨多个 tick，命中时机与
        /// 本次调用不在同一 tick），命中时以投射物 <c>sourceUnitId</c> 为 <c>SourceId</c>、被命中
        /// 单位为 <c>TargetId</c> 构造新的 <see cref="EffectContext"/> 逐项调用。
        /// </param>
        void Spawn(EffectContext context, IEffectSink effectSink);
    }
}
