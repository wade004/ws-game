using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Foundation.Common;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// 掉落抽取契约（见 08_玩法层_掉落任务对话关卡.md 第 9 节汇总表 Loot 行原文签名
    /// <c>LootHost.roll(tableId: Id, context: RollContext): List&lt;ItemStack&gt;</c>）。<see
    /// cref="LootHost"/> 是唯一实现，同时实现 <see cref="ILootRoller"/>（L3 载体层依赖倒置接口，
    /// 见 <see cref="ILootRoller"/> 类型注释）供 <c>core/carriers/gobj</c>/<c>core/carriers/creature</c>
    /// 一类需要产出掉落的 L3 实现在组装期注入。
    /// </summary>
    public interface ILootHost
    {
        /// <summary>按 <paramref name="tableId"/>（<c>loot.table</c> 记录）抽取一次掉落，返回按模板
        /// 合并堆叠后的结果（见 <c>core/carriers/common/contracts/ItemStack.cs</c>）。同一
        /// <c>IRngHost</c> 内部流状态下两次独立调用产生相同结果——确定性要求见落地方案与分阶段
        /// 计划.md 第 13 节验收标准 2。</summary>
        IReadOnlyList<ItemStack> Roll(Id tableId, RollContext context);

        /// <summary>
        /// T-N2-8 新增（ADR-0032 决策 7；硬性规则"禁止改既有 <c>Roll</c> 签名"，本方法是新增方法，
        /// 不是签名变更）：带身份的掉落抽取——按 <paramref name="tableId"/> 抽取一次掉落，返回
        /// <see cref="LootRollOutcome"/>（模板 id、数量、品质骰/词缀骰结果、来源折算的物品等级）列表，
        /// 不按模板合并（同一模板不同品质的两次抽取各自成一条，不像 <see cref="Roll"/> 那样合并堆叠）。
        /// <see cref="LootHost"/> 的具体实现里，<see cref="Roll"/> 反过来调用本方法再投影/合并（保证
        /// 两条路径消耗同一份 <c>IRngHost</c> 序列，不会重复抽取，见 <see cref="LootHost"/> 判断记录）。
        /// <para>
        /// 默认实现（供 <see cref="ILootHost"/> 的其它/组合实现方兜底）：转发 <see cref="Roll"/> 并把
        /// 每条 <see cref="ItemStack"/> 投影为"未额外指定"的 <see cref="LootRollOutcome"/>（<see
        /// cref="LootRollOutcome.FromStack"/>，即模板品质 + 空词缀 + 模板物品等级，均用 <c>null</c>
        /// 表达"回退到模板自身"，见该类型判断记录）——组合/包装实现方若能拿到更精确的结果（如转发到
        /// 另一个同样实现了本接口的真实宿主），应显式重写本方法转发，不应该悄悄吃掉默认值（见
        /// <c>Tests.Presentation.Assembly.InterfaceDefaultMemberForwardingTests</c> 门禁）。
        /// </para>
        /// </summary>
        IReadOnlyList<LootRollOutcome> RollDetailed(Id tableId, RollContext context) =>
            Roll(tableId, context).Select(LootRollOutcome.FromStack).ToList();
    }
}
