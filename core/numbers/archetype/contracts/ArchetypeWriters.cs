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

    /// <summary>
    /// W1 收边补齐（07 第 5 节 <c>arch.race.passive_auras</c>、A3 审计 #8）：把种族声明的被动光环
    /// 施加到光环宿主的具名委托，语义与用法见 <see cref="ArchetypeRegistry.ApplyTo"/>——本模块不
    /// 引用 <c>core/rules/skill</c> 的任何具体类型（<c>IEffectSink</c>/<c>AuraHost</c>），只按
    /// "targetId, auraDefId, sourceId" 三元组转发，与 <c>IEffectSink.ApplyAura</c> 前三个参数一一
    /// 对应（第四个可选的 <c>durationOverride</c> 参数不适用于"种族被动"这一持续到换种族/单位销毁
    /// 为止的场景，恒用 <c>aura_def.duration</c> 默认值，即恒传 null）。<paramref name="sourceId"/>
    /// 固定传种族自身 id（与 <see cref="StatModifierWriter"/> 的 <c>sourceId</c> 选取理由一致，见
    /// <see cref="ArchetypeRegistry"/> 类注释判断记录），供调用方需要撤销时能按来源整体识别。
    /// </summary>
    public delegate void AuraApplier(Id unitId, Id auraDefId, Id sourceId);

    /// <summary>
    /// T-N1-4（ADR-0030 决策 2"职业模板可覆盖派生系数（<c>arch.class.derivation_overrides</c>，
    /// 可选）"）：把职业模板声明的派生系数覆盖整体写入属性宿主的具名委托——本模块不引用
    /// <c>core/numbers/stat_block</c> 的任何具体类型（同 <see cref="StatBaseWriter"/>/<see
    /// cref="StatModifierWriter"/> 的既有分层理由），只把 <c>arch.class.derivation_overrides</c>
    /// 解析出的 <c>(目标派生属性, 来源属性, 系数)</c> 三元组列表原样转发。<paramref name="overrides"/>
    /// 是<b>全量替换</b>语义（对应 <c>StatHost.SetDerivationCoefficientOverrides</c> 的判断记录）：
    /// <see cref="ArchetypeRegistry.ApplyTo"/> 每次调用都会传入当前职业的完整覆盖列表（可能为空），
    /// 由接收方（属性宿主）负责"先清旧覆盖再写新覆盖"，本委托不需要配一个单独的"清除"委托。
    /// </summary>
    public delegate void DerivationCoefficientOverrideWriter(Id unitId, IReadOnlyList<(Id Stat, Id Source, double Coefficient)> overrides);
}
