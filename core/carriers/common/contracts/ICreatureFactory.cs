using Core.Foundation.Common;

namespace Core.Carriers.Common
{
    /// <summary>
    /// 按模板生成/移除生物实例的接口（见 07 第 2 节 Creature、05 第 5.3 节 <c>SpawnHost</c> 依赖的
    /// "按模板生成实体"这一底层能力）。由 <c>core/carriers/creature</c> 实现；<c>core/gameplay/spawn</c>
    /// （L4，不在本任务范围）与 <see cref="ISummonHost"/> 的实现均可复用本接口生成生物实体，避免各自
    /// 重复一遍"读 <c>creature.template</c> → 装配 <c>CreatureUnit</c> → 接入 <c>IWorldSim</c>"的流程。
    /// </summary>
    public interface ICreatureFactory
    {
        /// <summary>按 <paramref name="templateId"/>（<c>creature.template</c>）在 <paramref name="mapId"/>
        /// 的 <paramref name="position"/>/<paramref name="facing"/> 生成一个生物实体，成功后发出
        /// <c>creature.spawned</c>；<paramref name="ownerId"/> 非空时生成的生物携带该拥有者（见 07 第
        /// 4 节 <c>ownerId</c>，供 <see cref="ISummonHost"/> 复用本方法生成召唤物）。返回生成的运行期
        /// 实体 id。</summary>
        Id Spawn(Id templateId, Id mapId, Vec2 position, double facing, Id? ownerId = null);

        /// <summary>移除一个生物实体（死亡结算之外的场景，如刷新表清理、召唤物到期），
        /// <paramref name="reason"/> 是自由文本分类（如 <c>"died"</c>/<c>"despawned"</c>，见 05 第
        /// 5.3 节 <c>SpawnHost.notifyDespawn(entityId, reason: died|despawned)</c> 契约、
        /// <c>CreatureDespawnedEvent.Reason</c> 判断记录：与该字段一致处理为自由字符串，不强转成固定
        /// 枚举，因为 <c>Despawn</c> 的调用方不止刷新表一处，未来可能出现刷新表两值之外的分类），
        /// 成功后发出 <c>creature.despawned</c>。</summary>
        void Despawn(Id entityId, string reason);
    }
}
