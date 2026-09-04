using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Tests.Rules.Integration
{
    /// <summary>
    /// <see cref="IUnitAccess"/> 的集成测试实现：基于 <see cref="IWorldSim"/> 里的 <see cref="TestUnit"/>
    /// 实体（见任务书"WorldUnitAccess : IUnitAccess（基于 WorldSim 实体）"）。与
    /// <c>core/rules/*/tests/</c> 各模块自己的纯内存 <c>FakeUnitAccess</c> 不同——本类型真正读写
    /// <see cref="Entity"/> 的字段，验证 L2 规则层对接一个"像真的一样"的单位存储时行为正确，
    /// 不是又一份摆布用的假实现。
    /// </summary>
    internal sealed class WorldUnitAccess : IUnitAccess
    {
        private readonly IWorldSim _world;

        public WorldUnitAccess(IWorldSim world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public bool Exists(Id unitId) => _world.GetEntity(unitId) is TestUnit;

        public IReadOnlyList<Id> AllUnits =>
            _world.QueryEntities(new EntityFilter(kind: "unit")).Select(e => e.EntityId).ToList();

        public Vec2 GetPosition(Id unitId) => Require(unitId).Position;

        public void SetPosition(Id unitId, Vec2 position) => Require(unitId).Position = position;

        public Id GetFaction(Id unitId) => Require(unitId).FactionId;

        public int GetLevel(Id unitId) => Require(unitId).Level;

        public double GetFacing(Id unitId) => Require(unitId).Facing;

        public bool IsAlive(Id unitId) => Require(unitId).Alive;

        public void SetAlive(Id unitId, bool alive) => Require(unitId).Alive = alive;

        public Id? GetTemplateId(Id unitId) => Require(unitId).TemplateId;

        public IReadOnlyList<Id> GetTags(Id unitId) => Require(unitId).Tags;

        /// <summary>覆盖 <see cref="IUnitAccess.GetMapId"/> 的默认接口实现（默认返回 null，见该方法
        /// 注释）：本类型基于真实的 <see cref="Entity.MapId"/>，能够返回真实值。</summary>
        public Id? GetMapId(Id unitId) => Require(unitId).MapId;

        private TestUnit Require(Id unitId)
        {
            if (_world.GetEntity(unitId) is TestUnit unit)
            {
                return unit;
            }

            throw new InvalidOperationException($"WorldUnitAccess: 单位 \"{unitId}\" 不存在或不是 TestUnit");
        }
    }
}
