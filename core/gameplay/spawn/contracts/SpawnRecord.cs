using Core.Foundation.Common;

namespace Core.Gameplay.Spawn
{
    /// <summary>
    /// 某刷新点当前运行期记录的只读快照（见 05_对象模型与世界.md 第 5.3 节
    /// <c>SpawnHost.getSpawnRecord</c>、任务书拍板字段集合）。由
    /// <see cref="ISpawnHost.GetSpawnRecord"/> 按需组装返回；<see cref="OnceTriggered"/> 的权威存储是
    /// <see cref="Core.Gameplay.WorldState.IWorldState"/> 标志（见 <see cref="SpawnHost"/> 判断记录），
    /// 不是本类型自身持有的可变状态，因此本类型是不可变值对象。
    /// </summary>
    public sealed class SpawnRecord
    {
        public Id SpawnId { get; }

        /// <summary>当前存活实例的运行期 id；无存活实例时为 null。</summary>
        public Id? EntityId { get; }

        /// <summary><c>respawn_policy = once</c> 是否已生成过（见 05 第 5.2 节）。</summary>
        public bool OnceTriggered { get; }

        /// <summary><c>respawn_policy = timer</c> 的剩余重生时间（游戏内秒）；未在计时或策略非
        /// <c>timer</c> 时为 null。</summary>
        public double? RespawnRemaining { get; }

        /// <summary>该刷新点累计生成次数。</summary>
        public int SpawnCount { get; }

        public SpawnRecord(Id spawnId, Id? entityId, bool onceTriggered, double? respawnRemaining, int spawnCount)
        {
            SpawnId = spawnId;
            EntityId = entityId;
            OnceTriggered = onceTriggered;
            RespawnRemaining = respawnRemaining;
            SpawnCount = spawnCount;
        }
    }
}
