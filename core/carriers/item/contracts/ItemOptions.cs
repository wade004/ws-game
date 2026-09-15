using Core.Carriers.Common;
using Core.Foundation.Common;

namespace Core.Carriers.Item
{
    /// <summary>
    /// 背包满时的处理策略（见 08_交互与经济玩法.md 第 1.2 节"拾取满背包策略"、任务书
    /// "InventoryOptions { int MaxSlots=0（不限）; FullPolicy Reject|Partial }"）。<see cref="InventoryHost"/>
    /// 把"格子"理解为"一个 <see cref="Core.Carriers.Common.ItemInstance"/>"（同模板可堆叠到
    /// <c>stack_size</c> 上限前不占用新格子），见 <c>InventoryHost</c> 顶部判断记录。
    /// </summary>
    public enum InventoryFullPolicy
    {
        /// <summary>背包无法容纳本次加入的全部数量时，整次操作不生效（原子失败），返回 false。</summary>
        Reject,

        /// <summary>尽量填满已有堆叠与剩余格子，返回实际加入数量是否大于 0（部分加入也算成功）。</summary>
        Partial,
    }

    /// <summary>
    /// <see cref="InventoryHost"/> 的构造期策略配置（见任务书 InventoryOptions 定义）。
    /// </summary>
    public sealed class InventoryOptions
    {
        /// <summary>背包格子上限；0 表示不限（默认）。</summary>
        public int MaxSlots { get; set; } = 0;

        public InventoryFullPolicy FullPolicy { get; set; } = InventoryFullPolicy.Reject;
    }

    /// <summary>
    /// <see cref="EquipmentHost"/> 的构造期策略配置（见任务书"EquipmentHost……ItemOptions"）。
    /// </summary>
    public sealed class ItemOptions
    {
        /// <summary>预算超标校验使用的 <c>item.budget_curve</c> id（见 07 第 1.2 节"曲线 id 由
        /// ItemOptions.BudgetCurveId 指定"、<see cref="ItemBudgetValidationRule"/>）。校验规则单独
        /// 构造时需要显式传入，本字段只是给同一份配置对象一个统一存放位置，供宿主一次性配置。</summary>
        public Id BudgetCurveId { get; set; } = new Id("item.budget.default");

        /// <summary>是否在 <see cref="EquipmentHost.Equip"/> 中校验 <c>requirements.level</c>（见 07
        /// 第 1.1 节 <c>requirements</c> 补录字段、<see cref="EquipFailureReason.RequirementNotMet"/>）。
        /// 默认开启；关闭时任何等级需求一律视为满足（供不接入等级系统的最小化测试/原型场景使用）。</summary>
        public bool EnforceRequirements { get; set; } = true;

        /// <summary>分阶段落地计划 T-N2-5（ADR-0032 决策 4；07 第 1.2 节修订段"护甲值 =
        /// item.armor_curve(item_level) × 槽位系数"）：护甲曲线 <c>item.armor_curve</c> 的 id，同
        /// <see cref="BudgetCurveId"/> 惯例——曲线 id 是调用方配置项，不预设/硬编码唯一默认曲线。
        /// 缺省 <c>item.armor.default</c>（与 T-N2-1 落地的 <c>data/_sample/item/item.armor_curve
        /// .json</c> 样例 id 一致）。该曲线在已加载数据里找不到对应记录时，<see
        /// cref="EquipmentHost"/> 按"不写护甲"处理，不抛异常（见该类型 <c>ApplyArmorValue</c> 判断
        /// 记录）。</summary>
        public Id ArmorCurveId { get; set; } = new Id("item.armor.default");

        /// <summary>分阶段落地计划 T-N2-5（ADR-0032 决策 5；07 第 1.1/1.2 节修订段"requirements.level
        /// 未填时按 item.req_level_curve 由 item_level 反推"）：需求等级曲线 <c>item.req_level_curve</c>
        /// 的 id，同 <see cref="BudgetCurveId"/>/<see cref="ArmorCurveId"/> 惯例。缺省
        /// <c>item.req_level.default</c>（与 T-N2-1 落地的样例 id 一致）。曲线找不到时按"无等级限制"
        /// 处理（同未登记 <c>requirements</c> 字段时的既有行为），不抛异常。</summary>
        public Id ReqLevelCurveId { get; set; } = new Id("item.req_level.default");

        /// <summary>分阶段落地计划 T-N2-5（ADR-0032 决策 4；任务书"护甲写入哪条属性……在载体层需要
        /// 一个可配置的'护甲属性 id'（ItemOptions/EquipmentOptions 新增可选属性，缺省
        /// stat.armor）"）：护甲值经 <see cref="IStatHost.AddModifier"/> 写入的目标属性 id。缺省
        /// <c>stat.armor</c>，与 <c>Core.Rules.Combat.CombatOptions.ArmorStat</c> 默认值同名——但本
        /// 模块（L3 <c>core/carriers/item</c>）不引用 <c>core/rules/combat</c>（分层依赖方向不允许
        /// L3 反向依赖 L2 具体子模块，同 07 第 1 节"本层不引入与 06 并行的第二套效果系统"一贯口径），
        /// 两处各自维护一份手抄默认值字面量，不是同一常量的两处引用；游戏层若把护甲改挂到其它属性
        /// id，需要同时改这两处配置（同 <see cref="Core.Rules.Combat.CombatOptions.ArmorStat"/> 顶部
        /// 注释"属性缺失按 0 处理"一贯的"调用方自行保证配置一致"惯例）。</summary>
        public Id ArmorStatId { get; set; } = new Id("stat.armor");
    }
}
