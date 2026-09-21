using System;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Carriers.Assembly
{
    /// <summary>
    /// ADR-0062：<see cref="IInteractionTargetRegistry"/> 的默认实现——见该接口类型注释"不另开登记表"
    /// 判断记录，本类型只是 <see cref="IWorldSim.QueryEntities"/> 的一层薄封装，不持有任何自己的状态。
    /// <para>
    /// 放在 <c>core/carriers/assembly</c>（而不是 <c>core/carriers/common</c>——该目录"只定义类型与
    /// 接口，禁止任何业务逻辑"，见其 README）：本类型与该目录既有的
    /// <see cref="EntitySpatialSyncHost"/>/<c>NullWorldFlags</c> 同一类"跨多个载体模块的组合期通用
    /// 实现"，不归属 gobj/creature/loot 中任何单一模块，由 <c>CarriersAssembly</c> 在
    /// <see cref="Units"/>（<see cref="IUnitAccess"/>）就绪后立即构造（不需要等 gobj/creature 具体
    /// 宿主构造完成——本类型不持有它们的引用，只经 <see cref="EntityKinds"/> 字符串常量识别目标类型）。
    /// </para>
    /// <para>
    /// ADR-0065：候选判定额外持有 <see cref="IUnitAccess"/> 存活口径（见 <see
    /// cref="IsInteractionCandidate"/> 判断记录）——生物类实体死亡后仍继续以 <c>alive = false</c> 的
    /// 形态留在世界模拟中（不会被自动 <c>Despawn</c>），本类型据此把它们排除出候选，避免尸体与它
    /// 自己死亡结算生成的地面掉落物同坐标时按 <see cref="Entity.EntityId"/> 序数抢先命中。
    /// </para>
    /// </summary>
    public sealed class InteractionTargetRegistry : IInteractionTargetRegistry
    {
        private readonly IWorldSim _world;
        private readonly IUnitAccess _units;

        public InteractionTargetRegistry(IWorldSim world, IUnitAccess units)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _units = units ?? throw new ArgumentNullException(nameof(units));
        }

        public bool TryFindNearest(Id unitId, double? maxRange, out InteractionTarget target)
        {
            target = default;

            if (!_units.Exists(unitId))
            {
                return false;
            }

            var mapId = _units.GetMapId(unitId);
            if (!mapId.HasValue)
            {
                return false;
            }

            var origin = _units.GetPosition(unitId);
            var filter = new EntityFilter(
                mapId: mapId.Value,
                predicate: entity => IsInteractionCandidate(entity) && !entity.EntityId.Equals(unitId));

            var found = false;
            var bestDistance = 0.0;
            Entity? bestEntity = null;

            // QueryEntities 按 EntityId 序数排序返回（见 IWorldSim.QueryEntities 类型注释），本循环
            // 只在严格更近时才替换 best，等距离候选保留先遇到的（即 EntityId 序数最小者）——确定性，
            // 不依赖字典枚举顺序（同 AGENTS.md §3）。
            foreach (var entity in _world.QueryEntities(filter))
            {
                var distance = Vec2.Distance(origin, entity.Position);
                if (maxRange.HasValue && distance > maxRange.Value)
                {
                    continue;
                }

                if (!found || distance < bestDistance)
                {
                    found = true;
                    bestDistance = distance;
                    bestEntity = entity;
                }
            }

            if (!found || bestEntity == null)
            {
                return false;
            }

            target = new InteractionTarget(bestEntity.EntityId, ToKind(bestEntity.Kind), bestDistance);
            return true;
        }

        /// <summary>
        /// ADR-0065：生物类实体只有在存活时才是候选——存活判定用规则层权威口径
        /// <see cref="IUnitAccess.Exists"/>×<see cref="IUnitAccess.IsAlive"/>（与 ADR-0061
        /// <c>player.alive</c>/<c>target.alive</c> 同一口径，不看生命值资源池是否 <c>&lt;= 0</c>）。
        /// 场景物件、地面掉落物不接入这条判定——它们不是 <see cref="Core.Carriers.Unit.Unit"/> 子类，
        /// <see cref="IUnitAccess"/> 天然不适用，也没有"存活"这个概念。
        /// </summary>
        private bool IsInteractionCandidate(Entity entity)
        {
            if (entity.Kind == EntityKinds.Creature)
            {
                return _units.Exists(entity.EntityId) && _units.IsAlive(entity.EntityId);
            }

            return entity.Kind == EntityKinds.Gobj || entity.Kind == EntityKinds.Loot;
        }

        private static InteractionTargetKind ToKind(string kind)
        {
            if (kind == EntityKinds.Gobj)
            {
                return InteractionTargetKind.GameObject;
            }

            if (kind == EntityKinds.Creature)
            {
                return InteractionTargetKind.Creature;
            }

            return InteractionTargetKind.Loot;
        }
    }
}
