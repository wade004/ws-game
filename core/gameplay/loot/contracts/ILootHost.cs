using System.Collections.Generic;
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
    }
}
