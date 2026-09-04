using Core.Foundation.Common;

namespace Core.Numbers.Progression
{
    /// <summary>
    /// 把升级带来的属性成长写入属性宿主的具名委托（见任务书"并行开发期不引用属性宿主模块
    /// （stat_block）的具体类型，通过具名委托注入，由上层把真实 <c>IStatHost</c>
    /// 接进来实现"）。<see cref="IProgressionHost"/> 只按等级曲线算出该写什么，不知道、也不依赖
    /// 真正的属性宿主是谁。
    /// </summary>
    /// <param name="unitId">目标单位。</param>
    /// <param name="stat">要修改的属性 id（<c>stat.*</c>）。</param>
    /// <param name="op">修正运算类型，本模块固定传 <c>"flat"</c>（见 11_工程规范与测试.md 第 3 节
    /// "常量/枚举值"约定的 StatMod 运算类型示例）。</param>
    /// <param name="value">修正数值。</param>
    /// <param name="sourceId">修正来源 id，供属性宿主按来源分组叠加/移除，本模块固定传
    /// <c>prog.growth</c>（见 <see cref="ProgressionHost"/> 判断记录）。</param>
    public delegate void StatModifierWriter(Id unitId, Id stat, string op, double value, Id sourceId);

    /// <summary>移除某个来源在某单位身上写入的全部属性修正（用于升级时先清空旧成长再按
    /// 新等级重新写入累计值，见 <see cref="StatModifierWriter"/>）。</summary>
    /// <param name="unitId">目标单位。</param>
    /// <param name="sourceId">要移除的修正来源 id。</param>
    public delegate void StatModifierRemover(Id unitId, Id sourceId);
}
