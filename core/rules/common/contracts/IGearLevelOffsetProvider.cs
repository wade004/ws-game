using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// T-N1-8（ADR-0030 决策 6；06_规则层_属性技能战斗AI.md 第 4.2 节修订段"有效等级是否计入装备
    /// 等级偏移"）：装备等级偏移量的查询钩子——返回"偏移曲线(平均装备等级 − 期望装备等级)"已求值后
    /// 的等效等级增量，供 <see cref="Core.Rules.Combat.Resolver"/> 在
    /// <see cref="Core.Rules.Combat.CombatOptions.EffectiveLevelIncludesGearOffset"/> 开启时叠加到
    /// 角色等级上得到"有效等级"。
    /// <para>
    /// 判断记录（本接口刻意只暴露"最终偏移量"这一个数，不暴露"平均装备等级"/"期望装备等级"两个
    /// 中间量本身）：装备栏（<c>core/carriers/item.IEquipmentHost</c>，L3）与仿真锚点表
    /// <c>sim.anchor</c>（"期望装备等级曲线 E(L)"，归阶段 N6）都不是本模块（<c>combat</c>，L2）允许
    /// 依赖的对象（见本模块 README 依赖声明"不引用 core/rules/skill/targeting/ai 之外、更不引用
    /// L3 具体类型"）；求值这一步（读平均装备等级、读期望装备等级曲线、代入偏移曲线）必须留给能同时
    /// 看到两者的调用方（游戏层/L3 装配代码）完成，本接口只负责把求好的一个数字传进来。
    /// </para>
    /// <para>
    /// T-N1-8 范围内没有可用的真实实现——期望装备等级曲线归属阶段 N6 的 <c>sim.anchor</c> 表、
    /// 平均装备等级查询归属阶段 N2 的装备模块，均晚于本阶段（属性）落地；本任务因此只落地这个接口
    /// 钩子与 <see cref="Core.Rules.Combat.CombatOptions.EffectiveLevelIncludesGearOffset"/> 策略项，
    /// 真实实现与装配点（把 <c>core/carriers/item</c> 的装备查询、未来 <c>sim.anchor</c> 曲线接到
    /// 本接口）留给后续阶段接入——这是本任务向设计层的一项如实上报（见任务书"平均装备等级来源看现有
    /// 装备/物品接口，没有就上报并只留策略项与接口钩子"）。本任务测试用 Fake 实现验证策略项开关本身
    /// 在 <see cref="Core.Rules.Combat.Resolver"/> 里生效（见 core/rules/combat/tests）。
    /// </para>
    /// </summary>
    public interface IGearLevelOffsetProvider
    {
        /// <summary>返回 <paramref name="unitId"/> 的装备等级偏移量（已完成"偏移曲线(平均装备等级 −
        /// 期望装备等级)"求值），与角色等级同单位，可正可负、可为小数。</summary>
        double GetGearLevelOffset(Id unitId);
    }

    /// <summary>
    /// 缺省实现：恒返回 0（等价于"没有接入装备等级偏移来源"）。保证
    /// <see cref="Core.Rules.Combat.CombatOptions.EffectiveLevelIncludesGearOffset"/> 开启但未注入
    /// 真实 <see cref="IGearLevelOffsetProvider"/> 时不抛异常，退化为与关闭该策略项时相同的有效等级
    /// （角色等级本身）——同 <c>NullStaticImmunityProvider</c>/<c>IUnitAccess.GetMapId</c> 等既有
    /// "未接入时零成本退化"惯例。
    /// </summary>
    public sealed class NullGearLevelOffsetProvider : IGearLevelOffsetProvider
    {
        public static readonly NullGearLevelOffsetProvider Instance = new NullGearLevelOffsetProvider();

        private NullGearLevelOffsetProvider()
        {
        }

        public double GetGearLevelOffset(Id unitId) => 0.0;
    }
}
