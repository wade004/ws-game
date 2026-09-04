using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// <see cref="IUnitAccess"/> 的真实实现（05/06 文档要求"L3 载体层把 Unit 运行期实例接入"，见
    /// <c>Core.Rules.Common.IUnitAccess</c> 顶部"实现方：L3 载体层"）——基于 <see cref="IWorldSim"/>
    /// 里的 <see cref="Unit"/> 实体，与 <c>core/rules/tests/Integration/WorldUnitAccess.cs</c>（该集成
    /// 测试专用的 <c>TestUnit</c> 版本）同构，本类型是它的正式对应物。
    /// </summary>
    public sealed class WorldUnitAccess : IUnitAccess
    {
        private readonly IWorldSim _world;
        private readonly ISpatialIndexSync? _spatialSync;
        private readonly double _spatialRadius;

        /// <summary>
        /// <paramref name="spatialSync"/> 可选（见 <see cref="ISpatialIndexSync"/> 顶部判断记录）：
        /// 提供时 <see cref="SetPosition"/> 在写入实体位置后同步登记新位置，未提供时只写位置，不做
        /// 任何空间索引同步。<paramref name="spatialRadius"/> 是同步登记时使用的统一半径（同
        /// <c>Adapters.Stub.StubSpatialQuery.Register</c> 惯例：每个登记对象带一个自身半径），
        /// 默认 0.1。
        /// </summary>
        public WorldUnitAccess(IWorldSim world, ISpatialIndexSync? spatialSync = null, double spatialRadius = 0.1)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _spatialSync = spatialSync;
            _spatialRadius = spatialRadius;
        }

        public bool Exists(Id unitId) => _world.GetEntity(unitId) is Unit;

        public IReadOnlyList<Id> AllUnits =>
            _world.QueryEntities(new EntityFilter(predicate: e => e is Unit))
                .Select(e => e.EntityId)
                .ToList();

        public Vec2 GetPosition(Id unitId) => Require(unitId).Position;

        /// <summary>写入位置后，若注入了 <see cref="ISpatialIndexSync"/>，同步登记新位置（见构造函数
        /// 判断记录）。</summary>
        public void SetPosition(Id unitId, Vec2 position)
        {
            Require(unitId).Position = position;
            _spatialSync?.Upsert(unitId, position, _spatialRadius);
        }

        public Id GetFaction(Id unitId) => Require(unitId).FactionId;

        public int GetLevel(Id unitId) => Require(unitId).Level;

        public double GetFacing(Id unitId) => Require(unitId).Facing;

        public bool IsAlive(Id unitId) => Require(unitId).Alive;

        /// <summary>只同步 <see cref="Unit.Alive"/> 字段，不销毁实体、不改变
        /// <see cref="Entity.Lifecycle"/>（死亡是逻辑状态，不是生命周期状态——尸体按 05 第 2 节对照
        /// 表"不单列 Corpse 类型"，继续以 <c>alive = false</c> 的形态存在于世界模拟中，直到刷新表/
        /// 复活策略另行处理，见 06 死亡与复活）。<see cref="Entity.Lifecycle"/> 本就是
        /// <c>internal set</c>（仅 <c>Core.Foundation.SimLoop</c> 程序集可写），本方法不持有、也无法
        /// 触碰该字段，天然满足"不销毁实体"的要求。</summary>
        public void SetAlive(Id unitId, bool alive) => Require(unitId).Alive = alive;

        public Id? GetTemplateId(Id unitId) => Require(unitId).TemplateId;

        public IReadOnlyList<Id> GetTags(Id unitId) => Require(unitId).Tags;

        /// <summary>覆盖 <see cref="IUnitAccess.GetMapId"/> 的默认接口实现（默认返回 null）：本类型
        /// 基于真实的 <see cref="Entity.MapId"/>，能够返回真实值（同
        /// <c>core/rules/tests/Integration/WorldUnitAccess.GetMapId</c> 判断记录）。</summary>
        public Id? GetMapId(Id unitId) => Require(unitId).MapId;

        private Unit Require(Id unitId)
        {
            if (_world.GetEntity(unitId) is Unit unit)
            {
                return unit;
            }

            throw new InvalidOperationException($"WorldUnitAccess: 单位 \"{unitId}\" 不存在或不是 Unit");
        }
    }
}
