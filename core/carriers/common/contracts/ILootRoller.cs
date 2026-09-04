using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Carriers.Common
{
    /// <summary>
    /// 依赖倒置接口（同 <see cref="IWorldFlags"/> 顶部判断记录）：掉落规则 <c>LootHost</c> 属于 L4
    /// 玩法层（见 01 L4 模块表 <c>loot</c> 行），但 07 文档要求 <c>gobj</c> 模块的 <c>chest</c>/
    /// <c>gather_node</c> 两类物件在开箱/采集时产出掉落（见 07 第 3.1 节该两行 <c>loot_table_ref</c>
    /// 字段），本模块（L3）不得直接引用 L4 的 <c>LootHost</c>。本接口是"按掉落表 id + 来源单位
    /// （+ 可选击杀者）抽取一组物品堆叠"这一最小能力的只读子集，由 L4（或游戏组装根）实现，组装期
    /// 注入给 L3 的 <see cref="IGameObjectHost"/>/<see cref="ICreatureFactory"/> 一类需要产出掉落的实现。
    /// </summary>
    public interface ILootRoller
    {
        /// <summary>按 <paramref name="lootTableId"/> 抽取一次掉落。<paramref name="sourceUnitId"/> 是
        /// 掉落来源（箱子/生物等的运行期实例 id或所属单位 id，由调用方按语境约定）；
        /// <paramref name="killerId"/> 供按击杀者分档（如个人贡献相关规则）的掉落表使用，无明确击杀者
        /// （如开箱、采集）时为 null。</summary>
        IReadOnlyList<ItemStack> Roll(Id lootTableId, Id sourceUnitId, Id? killerId);
    }
}
