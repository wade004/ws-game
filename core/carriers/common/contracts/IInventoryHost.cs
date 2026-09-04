using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Carriers.Common
{
    /// <summary>
    /// 背包接口（见 07 第 1.3 节 <c>InventoryHost</c>）。由 <c>core/carriers/item</c> 实现（见 01 L3
    /// 模块表 <c>item</c> 行契约接口名），供该单位背包读写的一切调用方（装备、掉落拾取、商店交易等）
    /// 使用。
    /// </summary>
    public interface IInventoryHost
    {
        /// <summary>加入 <paramref name="count"/> 个 <paramref name="templateId"/> 模板的物品到
        /// <paramref name="unitId"/> 背包，成功后发出 <c>item.added</c>（见 07 第 1.3 节原文签名）。</summary>
        bool AddItem(Id unitId, Id templateId, int count);

        /// <summary>从 <paramref name="unitId"/> 背包移除指定实例 <paramref name="count"/> 个（堆叠类
        /// 物品可部分移除），成功后发出 <c>item.removed</c>（见 07 第 1.3 节原文签名）。</summary>
        bool RemoveItem(Id unitId, Id instanceId, int count);

        /// <summary>列出该单位背包全部物品堆叠（见 07 第 1.3 节原文签名）。</summary>
        IReadOnlyList<ItemInstance> ListItems(Id unitId);

        /// <summary>补充：统计该单位背包中指定模板物品的总数量（跨多个堆叠实例累加），供数量类前置
        /// 判断（如任务目标"收集 N 个"）复用，不必调用方自己遍历 <see cref="ListItems"/> 求和。</summary>
        int CountOf(Id unitId, Id templateId);

        /// <summary>补充：按实例 id 查找该单位背包中的一件具体物品；不存在或不属于该单位背包时返回
        /// null。</summary>
        ItemInstance? FindInstance(Id unitId, Id instanceId);
    }
}
