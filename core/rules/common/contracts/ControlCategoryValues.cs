using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Core.Rules.Common
{
    /// <summary>
    /// 控制类别固定取值集合（T-N3-6，[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md)
    /// 决策 8；06 第 3.3 节 2026-09-14 修订段"<c>control</c> 效果新增 <c>category</c>
    /// （<c>stun|root|silence|disarm|fear|polymorph</c>）"）：固定六值，顺序同 06 原文枚举顺序。
    /// <para>
    /// 供 <c>skill.aura_def.effects[kind=control].params.category</c>（<c>SkillSchemas</c>，
    /// <c>core/rules/skill</c>）与 <c>creature.tier_definition.control_immune_categories</c>
    /// （<c>CreatureSchemas</c>，<c>core/carriers/creature</c>，T-N3-6 新增并存字段）两处共用同一份
    /// 取值集合，避免两个模块各自维护一份可能漂移的字符串数组——两者均在 <c>Core.Rules</c> 程序集
    /// 可达范围内（<c>core/carriers/creature</c> 经 <c>Core.Carriers.csproj</c> -&gt;
    /// <c>Core.Rules.csproj</c> 引用），惯例同本文件同目录下 <see cref="EffectKind"/>/
    /// <see cref="EffectKindNames"/>、<see cref="SettlementEffectKinds"/> 供
    /// <c>core/rules/skill</c> 与 <c>core/carriers/creature</c> 两侧共用的既有做法。
    /// </para>
    /// </summary>
    public static class ControlCategoryValues
    {
        /// <summary>固定六值、固定顺序（同 06 原文），只读不可变——包装为
        /// <see cref="ReadOnlyCollection{T}"/>，转型为可写集合接口后调用变更方法会抛
        /// <see cref="System.NotSupportedException"/>。</summary>
        public static readonly IReadOnlyList<string> All = new ReadOnlyCollection<string>(new[]
        {
            "stun",
            "root",
            "silence",
            "disarm",
            "fear",
            "polymorph",
        });
    }
}
