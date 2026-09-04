using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Numbers.Archetype
{
    /// <summary>把职业模板的基础属性写入属性宿主的具名委托（见任务书"并行开发期不引用属性
    /// 宿主模块（stat_block）的具体类型，通过具名委托注入"）。<paramref name="value"/> 是
    /// <c>arch.class.base_stats</c> 中登记的绝对值（不是增量），供属性宿主直接设为基础值。</summary>
    public delegate void StatBaseWriter(Id unitId, Id stat, double value);

    /// <summary>把种族的属性修正写入属性宿主的具名委托，语义与用法见
    /// <see cref="ArchetypeRegistry.ApplyTo"/>（种族修正以 <c>"flat"</c> 运算类型写入，来源固定为
    /// 种族自身的 id，便于按来源整体移除/替换种族）。</summary>
    public delegate void StatModifierWriter(Id unitId, Id stat, string op, double value, Id sourceId);

    /// <summary>把职业声明的资源类型集合注册到资源池宿主的具名委托（见 06_规则层_属性技能战斗AI.md
    /// 第 2.1 节"arch.class 引用一组资源类型定义"）。本模块不知道、也不依赖真正的资源池模块
    /// （power_set）的实现，只把 <c>arch.class.power_types</c> 原样转发。</summary>
    public delegate void PowerRegistrar(Id unitId, IReadOnlyList<Id> powerTypes);
}
