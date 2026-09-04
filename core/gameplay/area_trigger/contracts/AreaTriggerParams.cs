using Core.Foundation.Common;

namespace Core.Gameplay.AreaTrigger
{
    /// <summary><c>trigger_type = map_transition</c> 时 <c>params</c> 字段的强类型视图（见 05 第 7
    /// 节表格该行、任务书拍板 <c>{target_map, spawn_point}</c>）。<see cref="SpawnPoint"/> 可空：
    /// 缺省时由 <see cref="Core.Foundation.SceneRouter.ISceneRouter"/> 落在目标地图的默认出生点
    /// （见 05 第 4.1 节 <c>world.map.spawn_points</c> 第 0 个元素）。</summary>
    public readonly struct MapTransitionParams
    {
        public Id TargetMap { get; }

        public Id? SpawnPoint { get; }

        public MapTransitionParams(Id targetMap, Id? spawnPoint)
        {
            TargetMap = targetMap;
            SpawnPoint = spawnPoint;
        }
    }

    /// <summary><c>trigger_type = encounter_start</c> 时 <c>params</c> 字段的强类型视图（见 05 第 7
    /// 节表格该行、任务书拍板 <c>{encounter_ref}</c>）。</summary>
    public readonly struct EncounterStartParams
    {
        public Id EncounterRef { get; }

        public EncounterStartParams(Id encounterRef)
        {
            EncounterRef = encounterRef;
        }
    }

    /// <summary><c>trigger_type = script</c> 时 <c>params</c> 字段的强类型视图（见 05 第 7 节表格该
    /// 行、任务书拍板 <c>{hook_id}</c>，<c>hook_id</c> 指向 <c>found.hook</c> 登记的挂载点）。</summary>
    public readonly struct ScriptParams
    {
        public Id HookId { get; }

        public ScriptParams(Id hookId)
        {
            HookId = hookId;
        }
    }
}
