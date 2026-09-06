using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
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
        private readonly ISpatialQuery? _spatial;
        private readonly HealthFractionSetter? _healthFractionSetter;

        /// <summary>
        /// <paramref name="spatial"/> 可选：提供时 <see cref="SetPosition"/> 在写入实体位置后经
        /// <see cref="ISpatialQuery.UpdatePosition"/> 同步新位置（ADR-0016 决策 7：单位移动时调用
        /// UpdatePosition 同步空间索引），未提供时只写位置，不做任何空间索引同步。首次登记
        /// （<see cref="ISpatialQuery.Register"/>）不在本类型职责内——单位创建时机由
        /// <c>core/carriers/assembly/EntitySpatialSyncHost</c> 订阅 <c>entity.created</c> 统一处理
        /// （创建、移动、销毁三个时机分属不同类型，见该类型判断记录）。
        /// <paramref name="healthFractionSetter"/> 可选（W1 收边补齐，见 <see cref="Revive"/>）：
        /// 未注入时 <see cref="Revive"/> 只恢复存活状态与坐标，不触碰生命值。
        /// </summary>
        public WorldUnitAccess(IWorldSim world, ISpatialQuery? spatial = null, HealthFractionSetter? healthFractionSetter = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _spatial = spatial;
            _healthFractionSetter = healthFractionSetter;
        }

        public bool Exists(Id unitId) => _world.GetEntity(unitId) is Unit;

        public IReadOnlyList<Id> AllUnits =>
            _world.QueryEntities(new EntityFilter(predicate: e => e is Unit))
                .Select(e => e.EntityId)
                .ToList();

        public Vec2 GetPosition(Id unitId) => Require(unitId).Position;

        /// <summary>写入位置后，若注入了 <see cref="ISpatialQuery"/>，同步更新空间索引里的位置
        /// （见构造函数判断记录）。</summary>
        public void SetPosition(Id unitId, Vec2 position)
        {
            Require(unitId).Position = position;
            _spatial?.UpdatePosition(unitId, position);
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

        /// <summary>
        /// W1 收边补齐（拍板 3 前置：死亡复活链路，见 <c>architecture/adr/</c> DECISIONS 拍板 3
        /// "DeathPolicyHost：respawn_point 策略……置于当前地图 spawn_points[0]、恢复生命并发
        /// unit.respawned"）：复活单位的窄契约——恢复 <see cref="Unit.Alive"/>、写入复活坐标
        /// <paramref name="position"/>（与 <see cref="SetPosition"/> 同样经 <see cref="ISpatialQuery"/>
        /// 同步空间索引），并（若已注入 <see cref="HealthFractionSetter"/>）按
        /// <paramref name="healthFraction"/>（<c>[0,1]</c>，越界自动夹取）恢复生命值。
        /// <para>
        /// 本方法只是一个可供调用的窄契约，<b>不</b>自行判断死亡复活策略、<b>不</b>自行订阅
        /// <c>unit.died</c>、<b>不</b>自行发布 <c>unit.respawned</c>——这些属于死亡复活的执行主体
        /// （L4 <c>core/gameplay/death.DeathPolicyHost</c>，见 06 第 4.6 节、DECISIONS 拍板 3）的
        /// 职责，本模块（L3 载体层）不持有 <c>ITurnScheduler</c>/存档系统/主菜单跳转等 L4 依赖，
        /// 无法承担整条复活流程，只提供它需要的"改写单位运行期状态"这一步。
        /// </para>
        /// </summary>
        public void Revive(Id unitId, Vec2 position, double healthFraction)
        {
            var unit = Require(unitId);
            unit.Alive = true;
            unit.Position = position;
            _spatial?.UpdatePosition(unitId, position);
            _healthFractionSetter?.Invoke(unitId, Math.Clamp(healthFraction, 0.0, 1.0));
        }

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
