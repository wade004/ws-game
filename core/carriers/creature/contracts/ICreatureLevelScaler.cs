using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Carriers.Creature
{
    /// <summary>
    /// T-N6-3b 新增（ADR-0035 决策 3"生物模板按指定等级出生"；架构 01 第 8 节"窄接口反向注入"）：
    /// <see cref="CreatureFactory"/> 按指定等级出生（见 <c>ICreatureFactory.Spawn(...,int level)</c>）
    /// 时，用来把 <c>creature.template.base_stats</c> 从模板自身登记等级换算到目标出生等级的窄接口。
    /// <para>
    /// 判断记录（为什么是"反向注入"、本接口为什么放在本模块）：真正的等级换算算法（如按锚点表插值）
    /// 依赖的数据（"锚点表"）登记在更上层模块（<c>core/sim</c>，供仿真/数值核对使用），而
    /// <c>core/carriers/creature</c>（L3）不能反向依赖 <c>core/sim</c>（更上层）——本接口按依赖倒置
    /// 原则声明在被依赖的一侧（L3），由上层模块在装配期实现并注入具体缩放器，L3 自身不知道、也不需要
    /// 知道锚点表长什么样，只知道"给定模板与两端等级、换算基础属性"这一薄契约。
    /// </para>
    /// <para>
    /// 判断记录（未注入时的语义"等级改、属性不变"，见 <see cref="CreatureFactory"/> 构造函数/
    /// <c>Spawn</c> 判断记录）：<see cref="CreatureFactory"/> 未持有任何
    /// <see cref="ICreatureLevelScaler"/> 实现时，按指定等级出生只改变
    /// <c>Core.Carriers.Unit.CreatureUnit.Level</c>（及以其为准的经验发放/等级差计算等消费方），
    /// 基础属性仍按模板自身 <c>base_stats</c>（× 分档 <c>stat_multiplier</c>，顺序不变）——不做任何
    /// 隐式外推，避免在没有权威锚点数据时凭空编造数值。
    /// </para>
    /// </summary>
    public interface ICreatureLevelScaler
    {
        /// <summary>
        /// 把 <paramref name="template"/>（<c>creature.template</c> 强类型视图）在登记等级
        /// <paramref name="templateLevel"/>（通常等于 <c>template.Level</c>，显式传入避免实现方反过来
        /// 读取 <see cref="CreatureTemplate"/> 内部字段）下的基础属性 <paramref name="baseStats"/>
        /// （<c>StatKey → Number</c>，未经分档 <c>stat_multiplier</c> 处理的原始模板值）换算为目标
        /// 出生等级 <paramref name="targetLevel"/> 下的基础属性。返回值同样是未经分档倍率处理的原始
        /// 值——<see cref="CreatureFactory"/> 换算之后仍按既有顺序乘一次
        /// <c>creature.tier_definition.stat_multiplier</c>，不由本接口重复处理分档倍率。
        /// </summary>
        IReadOnlyDictionary<Id, double> ScaleBaseStats(
            CreatureTemplate template,
            int templateLevel,
            int targetLevel,
            IReadOnlyDictionary<Id, double> baseStats);
    }
}
