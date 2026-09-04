using Core.Foundation.Common;
using Core.Foundation.SimLoop;

namespace Tests.Foundation.Determinism
{
    /// <summary>
    /// 阶段 6"触发评估"（<see cref="TickPhase.TriggerEvaluation"/>）测试处理器：第 10 个
    /// <see cref="Execute"/>（对应 <c>DeterminismTests</c> 意图脚本约定里的 tick 10）创建一个
    /// 实体（触发 <c>entity.created</c>），第 20 个 <see cref="Execute"/> 把它标记待销毁
    /// （阶段 8 生命周期清理阶段真正移除并触发 <c>entity.destroyed</c>）。
    /// <para>
    /// 用本处理器自身的调用计数（<see cref="_localTick"/>）判断"第几个 tick"，而不是查询
    /// <see cref="IWorldSim"/>——<see cref="IWorldSim"/> 契约没有暴露 tick 序号（只有具体实现
    /// <c>WorldSim</c> 才有），本处理器只依赖契约接口；由于 <see cref="Execute"/> 严格按
    /// tick 顺序被调用一次，本地计数与"第几个 tick"天然一一对应，结果确定。
    /// </para>
    /// </summary>
    internal sealed class SpawnDespawnHandler : ITickPhaseHandler
    {
        private readonly Id _mapId;
        private long _localTick;
        private Id? _spawnedId;

        public SpawnDespawnHandler(Id mapId)
        {
            _mapId = mapId;
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            _localTick++;

            if (_localTick == 10)
            {
                var id = world.AllocateEntityId("unit_spawned");
                _spawnedId = id;
                world.AddEntity(new DeterministicUnit(id, _mapId, "spawned"));
            }
            else if (_localTick == 20 && _spawnedId.HasValue)
            {
                world.MarkForDestruction(_spawnedId.Value);
            }
        }
    }
}
