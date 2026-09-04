using Core.Foundation.Common;

namespace Core.Numbers.PowerSet
{
    /// <summary>
    /// "上限引用属性"的查询出口（见 06 第 2.1 节"上限来源：固定值或引用属性"）。
    /// 本模块不引用 <c>stat_block</c> 模块的具体类型（并行开发、避免编译期耦合，见任务书
    /// "并行注意"）：<see cref="PowerHost"/> 构造期只接收这一个具名委托，由上层把
    /// <c>IStatHost.GetStat</c> 一类真实实现接进来；委托签名固定为
    /// "给定单位与属性 id，返回该单位当前该属性的数值"。
    /// </summary>
    /// <param name="unitId">要查询的单位。</param>
    /// <param name="stat">要查询的属性 id。</param>
    /// <returns>该单位当前该属性的数值。</returns>
    public delegate double StatLookup(Id unitId, Id stat);
}
