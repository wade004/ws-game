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
        /// <summary>
        /// 消费方反馈第 33 条（示例技能消耗 <c>arch.power.mana</c> 时
        /// <c>Core.Numbers.PowerSet.IPowerHost.GetPower</c> 直接抛异常，因为 <see cref="CreatureFactory.Spawn"/>
        /// 此前恒按本选项的默认值 <c>[<see cref="WellKnownPowers.Health"/>]</c> 注册资源类型，不管
        /// 模板/数据集实际定义了哪些资源类型）：新生成生物注册的资源类型集合，<c>null</c>（新默认值）
        /// 表示"不显式覆盖"——<see cref="CreatureFactory.Spawn"/> 按以下顺序解析实际注册的资源类型：
        /// ①模板/职业实际声明或引用的资源类型（当前 <c>creature.template</c> 本身未登记此类字段，
        /// 该步骤恒空，为未来扩展预留，见 <see cref="CreatureFactory"/> 判断记录）；②无声明则回落到
        /// 当前数据集里全部已登记的 <c>arch.power_type</c> 定义（按 id 升序，保证确定性）。
        /// <para>
        /// 显式设置为非 <c>null</c> 值（哪怕设成与旧默认值相同的 <c>[Health]</c>）时，视为游戏层
        /// 按具体口味显式覆盖，优先于①②两步、对全部生成的生物统一生效——架构本身仍不预设"生物必须
        /// 有几种资源池"（同 <c>Core.Numbers.PowerSet.IPowerHost.RegisterUnit</c> 注释"禁止把资源池
        /// 数量硬编码为固定值"），本选项只是提供一个"一刀切"的覆盖口味，与①②两步的"按数据自动推导"
        /// 二选一。
        /// </para>
        /// </summary>
        public IReadOnlyList<Id>? DefaultPowerTypes { get; set; }

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
