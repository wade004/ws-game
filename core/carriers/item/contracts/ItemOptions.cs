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
    }
}
