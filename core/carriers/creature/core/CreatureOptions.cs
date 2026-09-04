using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;

namespace Core.Carriers.Creature
{
    /// <summary>
    /// <see cref="CreatureFactory"/> 的口味配置项（惯例同 <c>core/carriers/unit</c> 的
    /// <c>MovementOptions</c>：速度属性一类"非架构固定语义"的取值放构造期可配置项，不硬编码）。
    /// </summary>
    public sealed class CreatureOptions
    {
        /// <summary>新生成生物默认注册的资源类型集合（见 07 第 2 节补充信息表"依赖层：L1
        /// Stat/Power/AI"），默认只有生命值一种（<see cref="WellKnownPowers.Health"/>）。游戏层
        /// 需要法力/怒气等额外资源池时，通过本选项按具体游戏口味扩充——架构本身不预设"生物必须
        /// 有几种资源池"（同 <c>Core.Numbers.PowerSet.IPowerHost.RegisterUnit</c> 注释"禁止把资源池
        /// 数量硬编码为固定值"）。</summary>
        public IReadOnlyList<Id> DefaultPowerTypes { get; set; } = new[] { WellKnownPowers.Health };

        /// <summary>
        /// <c>creature.tier_definition.control_immune</c> 为 true 时写入
        /// <c>CreatureUnit.Immunities</c> 的标记 Id（见 06 第 3.9 节"Boss/精英级免疫标志"）；为 null
        /// 时不写入任何标记（等价于关闭这一便捷开关，只保留 tier 数据本身的
        /// <c>control_immune</c> 位供游戏层自行按需读取）。
        /// <para>
        /// 判断记录：06/07 均未规定"控制免疫"在 <c>immunities: List&lt;Id&gt;</c>（07 第 2.1 节
        /// "免疫的学派/效果类型/控制类别"）里应该用哪个具体 Id 表达"控制类别"这一整体豁免；本模块
        /// 选定一个可配置的默认值 <c>immunity.control_immune</c>，供游戏层按需覆盖或关闭。
        /// </para>
        /// </summary>
        public Id? ImmunityTagPrefix { get; set; } = new Id("immunity.control_immune");
    }
}
