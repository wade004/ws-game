using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// <c>encounter.def.phases[]</c> 的一条阶段定义（见 08 第 4.1 节 <c>Phase</c> 结构
    /// <c>{enterCondition: Expr, aiRotationOverride: Map&lt;Id, Id&gt;, onEnterHook: Optional&lt;Id&gt;}</c>、
    /// 第 4.3 节"阶段切换只做两件事：替换涉及单位的 ai.rotation 引用、调用 onEnterHook"、06 第
    /// 6.4 节"Boss 阶段 = 换 Rotation"）。<see cref="AiRotationOverride"/> 的键"为具体参战单位 id
    /// 或模板 id 时对该模板的全部参战单位"（任务书原句），二者的区分规则见
    /// <see cref="EncounterHost"/> 判断记录。
    /// </summary>
    public sealed class EncounterPhaseDefinition
    {
        public string EnterConditionText { get; }

        public IReadOnlyDictionary<Id, Id> AiRotationOverride { get; }

        public Id? OnEnterHook { get; }

        public EncounterPhaseDefinition(string enterConditionText, IReadOnlyDictionary<Id, Id> aiRotationOverride, Id? onEnterHook)
        {
            EnterConditionText = enterConditionText;
            AiRotationOverride = aiRotationOverride;
            OnEnterHook = onEnterHook;
        }
    }
}
