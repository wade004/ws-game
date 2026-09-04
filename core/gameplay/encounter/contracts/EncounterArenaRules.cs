using Core.Foundation.EngineAdapter;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// <c>encounter.def.arena_rules</c>（见 08 第 4.1 节 <c>{boundsShape, resetIfLeave: Bool}</c>）：
    /// 场地边界与"玩家离开边界是否重置遭遇"的开关（第 4.3 节；具体触发流程见
    /// <see cref="EncounterHost"/> 判断记录）。<see cref="BoundsShape"/> 复用 05 第 3.5 节 Shape
    /// 联合类型（<see cref="Shape"/>，见 <c>core/foundation/engine_adapter</c>）。
    /// </summary>
    public sealed class EncounterArenaRules
    {
        public Shape BoundsShape { get; }

        public bool ResetIfLeave { get; }

        public EncounterArenaRules(Shape boundsShape, bool resetIfLeave)
        {
            BoundsShape = boundsShape;
            ResetIfLeave = resetIfLeave;
        }
    }
}
