using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// <c>encounter.def.waves[]</c> 的一条波次定义（见 08 第 4.1 节 <c>Wave</c> 结构
    /// <c>{triggerCondition: Expr, spawnRefs: List&lt;Id&gt;}</c>）。<see cref="TriggerConditionText"/>
    /// 是未解析的原始 Expr 文本——解析需要 <see cref="Core.Foundation.Expr.IExprSchema"/>，属于
    /// <see cref="EncounterHost"/> 构造期职责（惯例同 <c>AchievementCriterion</c>）。
    /// <see cref="SpawnRefs"/> 引用 <c>spawn.table</c> 条目（08 第 8 节"spawnRefs 域名 spawn"，见
    /// <see cref="EncounterContentValidationRule"/>）。
    /// </summary>
    public sealed class EncounterWaveDefinition
    {
        public string TriggerConditionText { get; }

        public IReadOnlyList<Id> SpawnRefs { get; }

        public EncounterWaveDefinition(string triggerConditionText, IReadOnlyList<Id> spawnRefs)
        {
            TriggerConditionText = triggerConditionText;
            SpawnRefs = spawnRefs;
        }
    }
}
