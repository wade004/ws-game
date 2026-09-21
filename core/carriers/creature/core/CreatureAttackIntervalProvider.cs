using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Carriers.Creature
{
    /// <summary>
    /// <see cref="IAttackIntervalFallbackProvider"/> 的默认实现（ADR-0059，消费方反馈第三批第 5
    /// 条"普通攻击缺少框架原生执行机制"）：读 <see cref="CreatureTemplate.AttackInterval"/>（07 第
    /// 2.1 节新增可选字段），供 <c>core/rules/combat.AutoAttackHost</c> 在无武器时回退查询——惯例
    /// 与结构同 <see cref="CreatureImmunityProvider"/>（同一个"按 <c>unitId</c> 经 <see
    /// cref="IWorldSim.GetEntity"/> 取回 <see cref="Core.Carriers.Unit.CreatureUnit"/> 实例，非生物
    /// 单位/找不到实体一律返回安全缺省值"的判断记录）。
    /// </summary>
    public sealed class CreatureAttackIntervalProvider : IAttackIntervalFallbackProvider
    {
        private readonly IWorldSim _world;
        private readonly ICreatureTemplateQuery _templates;

        public CreatureAttackIntervalProvider(IWorldSim world, ICreatureTemplateQuery templates)
        {
            _world = world ?? throw new System.ArgumentNullException(nameof(world));
            _templates = templates ?? throw new System.ArgumentNullException(nameof(templates));
        }

        /// <summary><paramref name="unitId"/> 不是 <see cref="Core.Carriers.Unit.CreatureUnit"/>
        /// （如玩家单位、或单位已被销毁/不存在，同 <see cref="CreatureImmunityProvider"/> 判断记录）
        /// 时返回 <c>null</c>——玩家没有"生物模板"概念，普通攻击完全依赖武器数据。生物单位的
        /// <see cref="Core.Foundation.SimLoop.Entity.TemplateId"/>（<see
        /// cref="Core.Carriers.Unit.CreatureUnit"/> 构造期强制非空，见该类型判断记录）查不到对应
        /// 模板记录（理论上不会发生——模板 id 来自生成该实体时的合法引用）时同样按 <c>null</c>
        /// 处理，不抛异常（防御性姿态，惯例同 <see cref="IAttackIntervalFallbackProvider"/> 接口
        /// 判断记录"没有更好的值可返回时按 null 处理，不是运行时错误"）。</summary>
        public double? GetAttackIntervalSeconds(Id unitId)
        {
            if (!(_world.GetEntity(unitId) is Core.Carriers.Unit.CreatureUnit unit) || !unit.TemplateId.HasValue)
            {
                return null;
            }

            try
            {
                return _templates.Get(unit.TemplateId.Value).AttackInterval;
            }
            catch (System.ArgumentException)
            {
                return null;
            }
        }
    }
}
