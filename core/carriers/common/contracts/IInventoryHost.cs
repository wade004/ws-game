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

        /// <summary>
        /// C05 根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：同 <see cref="AddItem"/>，
        /// 额外经 <paramref name="actualCount"/> 返回本次调用实际新增的数量——<see
        /// cref="InventoryFullPolicy.Partial"/> 下可能小于 <paramref name="count"/>（少量加入仍算
        /// 成功，见 <c>InventoryHost.AddItem</c> 判断记录 2）；<see cref="InventoryFullPolicy.Reject"/>
        /// 下失败时为 0，成功时与 <paramref name="count"/> 相等。
        /// <para>
        /// 判断记录（默认实现 = 历史行为，只有 <c>InventoryHost</c> 需要覆盖）：<see
        /// cref="RewardDispatcher.Grant"/> 此前按"请求量"而不是"实际落地量"记账，Partial 下少量
        /// 加入仍返回 true 时，请求量与实际量的差额会在同批其它奖励失败回滚时被错误地从背包里多扣
        /// 走（把该批之前就已存在的同模板堆叠也一并删掉，见该问题判断记录）。<see cref="AddItem"/>
        /// 的既有签名（只有 bool）无法区分"请求量"与"实际量"，因此新增本方法而不是改
        /// <see cref="AddItem"/> 的返回类型（破坏性变更，需要 12 §5 走 ADR）——本方法用默认接口
        /// 实现（C# 8 起支持，见 Directory.Build.props <c>LangVersion</c>）把"实际量＝请求量"这一
        /// 历史行为原样保留给未覆盖本方法的既有实现（各模块测试用的最小 Fake 均不支持 Partial 部分
        /// 吞没语义，这一默认值对它们而言就是它们本来的行为，不需要逐个改造）；只有真正支持 Partial
        /// 语义的 <c>InventoryHost</c> 需要覆盖为真实分摊量。
        /// </para>
        /// </summary>
        bool TryAddItem(Id unitId, Id templateId, int count, out int actualCount)
        {
            var ok = AddItem(unitId, templateId, count);
            actualCount = ok ? count : 0;
            return ok;
        }

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

        /// <summary>
        /// 分阶段落地计划 T-N2-9（ADR-0034 决策 8；07 第 1.3 节修订段"背包容量来源"）：查询
        /// <paramref name="unitId"/> 当前的背包容量（格子数上限）；<see cref="int.MaxValue"/> 表示
        /// 不限。
        /// <para>
        /// 判断记录（默认实现选择"转发到既有语义"而不是抛异常）：本接口在新增该成员之前完全没有
        /// "容量"概念——<c>InventoryOptions</c> 是 <c>InventoryHost</c> 的构造期私有配置，不是接口
        /// 契约的一部分，默认实现无法得知任何实现方的具体数值上限。凡是未显式覆盖本方法的既有实现
        /// （含各模块测试用的最小 Fake），在本成员新增之前，调用方看到的就是"这份契约压根不提供
        /// 容量信息"，等价于"没有已知上限"；默认值取 <see cref="int.MaxValue"/> 是把这一历史现状
        /// 原样表达为返回值，不对未覆盖的既有实现引入新的行为断言（同 <see cref="TryAddItem"/> 顶部
        /// 判断记录"未覆盖本方法的既有实现……这一历史行为原样保留"一贯做法）。备选方案"默认抛
        /// <see cref="System.NotSupportedException"/>"被否决——那会让任何既有 Fake 在被换成读
        /// <see cref="GetCapacity"/> 的新调用路径时从"能用"变成"抛异常崩溃"，与默认接口成员"不破坏
        /// 既有实现编译期兼容性、也不应破坏其运行期既有行为"的设计初衷相反。真正需要精确容量语义的
        /// 唯一生产实现 <see cref="Core.Carriers.Item.InventoryHost"/> 显式覆盖本方法（见该类型
        /// <c>GetCapacity</c> 判断记录），不依赖此默认值。
        /// </para>
        /// </summary>
        int GetCapacity(Id unitId) => int.MaxValue;
    }
}
