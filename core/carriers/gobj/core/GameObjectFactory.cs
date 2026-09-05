using System;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;

namespace Core.Carriers.Gobj
{
    /// <summary>
    /// 按模板生成/移除游戏对象实体（惯例同 07 第 2 节 <c>Creature</c> 一侧的
    /// <c>Core.Carriers.Common.ICreatureFactory</c>，但本类型不对外暴露为
    /// <c>core/carriers/common</c> 契约接口——07 第 9 节契约汇总表 GameObject 行只列出
    /// <c>GameObjectHost</c>（<c>interact</c>/<c>tryUnlock</c>）一个契约，"按模板生成实例"不是其它
    /// 模块需要跨模块调用的能力，故只在本模块内部提供，不新增 common 契约）。不读 <c>gobj.template</c>
    /// 装配任何字段——<see cref="GameObjectEntity"/> 只持有 <c>templateId</c>/<c>lockId</c>，具体模板
    /// 字段（<c>kind</c>/<c>type_data</c>/...）由 <see cref="GameObjectHost"/> 按需通过
    /// <c>IDataRegistryView</c> 现查现解析（同 <c>core/rules/skill</c> 的 <c>SkillDefCache</c> 惯例
    /// 之外的另一种选择：本模块记录条数少、不在每帧热路径上，未引入缓存层，见 schema/README.md
    /// "判断记录"）。
    /// </summary>
    public sealed class GameObjectFactory
    {
        private readonly IWorldSim _world;

        public GameObjectFactory(IWorldSim world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <summary>在 <paramref name="mapId"/> 的 <paramref name="position"/>/<paramref name="facing"/>
        /// 生成一个游戏对象实体，<paramref name="lockId"/> 缺省时可后续由调用方按
        /// <c>GameObjectTemplate.LockId</c> 显式设置 <see cref="GameObjectEntity.LockId"/>。返回生成的
        /// 运行期实体 id（<c>"gobj.inst_&lt;n&gt;"</c>，见 <see cref="IWorldSim.AllocateEntityId"/>）。</summary>
        public Id Spawn(Id templateId, Id mapId, Vec2 position, double facing, Id? lockId = null)
        {
            var entityId = _world.AllocateEntityId(EntityKinds.Gobj);
            var entity = new GameObjectEntity(entityId, mapId, templateId, lockId)
            {
                Position = position,
                Facing = facing,
            };

            _world.AddEntity(entity);
            return entityId;
        }

        /// <summary>移除一个游戏对象实体（见 <see cref="IWorldSim.MarkForDestruction"/>：真正的移除
        /// 发生在下一次生命周期清理阶段）。07 第 9 节契约汇总表未给 GameObject 登记
        /// <c>gobj.despawned</c>/<c>gobj.spawned</c> 一类事件（不同于 <c>creature.spawned</c>/
        /// <c>creature.despawned</c>），本方法不额外发事件，只依赖 <see cref="IWorldSim"/> 自身的
        /// <c>entity.created</c>/<c>entity.destroyed</c> 通用事件。</summary>
        public void Despawn(Id entityId) => _world.MarkForDestruction(entityId);
    }
}
