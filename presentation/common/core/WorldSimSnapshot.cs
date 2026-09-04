using System;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;

namespace Presentation.Common
{
    /// <summary>
    /// <see cref="ISimSnapshot"/> 的默认实现：基于 <see cref="IWorldSim.GetEntity"/> 与
    /// <c>Entity</c>/<c>Unit</c>（<see cref="Unit.HeightOffset"/>）只读实现（见铁律 P1）。本类型
    /// 不持有任何可写引用、不缓存任何字段——每次调用都直接查询 <see cref="IWorldSim"/>，保证读到
    /// 的永远是当前最新的逻辑状态（表现层本身不缓存"权威"数据，见 01 第 6 节禁止事项 8）。
    /// </summary>
    public sealed class WorldSimSnapshot : ISimSnapshot
    {
        private readonly IWorldSim _world;

        public WorldSimSnapshot(IWorldSim world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public Vec2 GetPosition(Id entityId) => Require(entityId).Position;

        public double GetFacing(Id entityId) => Require(entityId).Facing;

        /// <summary>仅 <see cref="Unit"/> 子类携带 <see cref="Unit.HeightOffset"/>；非 Unit 实体
        /// （GameObject/Projectile/AreaTrigger/DroppedLoot 等）固定返回 0（见 05 第 3.3 节：高度偏移
        /// 是 <c>Unit</c> 字段表专属字段，本模块不为其它实体类型杜撰同名字段）。</summary>
        public double GetHeight(Id entityId) => Require(entityId) is Unit unit ? unit.HeightOffset : 0.0;

        public bool Exists(Id entityId) => _world.GetEntity(entityId) != null;

        /// <summary>用于查询外形的逻辑 id：<c>TemplateId ?? EntityId</c>（见 03 第 5 节"经 DisplayInfo
        /// 取得表现资源引用"、<see cref="EntityCreatedEvent.DisplayId"/> 同一约定）。</summary>
        public Id? GetDisplayId(Id entityId)
        {
            var entity = _world.GetEntity(entityId);
            return entity == null ? (Id?)null : entity.TemplateId ?? entity.EntityId;
        }

        public ViewKind? GetKind(Id entityId)
        {
            var entity = _world.GetEntity(entityId);
            if (entity == null)
            {
                return null;
            }

            return EntityKindMapping.TryMap(entity.Kind, out var kind) ? kind : (ViewKind?)null;
        }

        /// <summary>不存在时抛异常而非返回默认值——调用方（<c>ViewBinder</c> 等）总是先经
        /// <c>entity.created</c>/<c>entity.destroyed</c> 事件维护"哪些实体当前存活"，只应在确认
        /// 存活期间调用位置/朝向类查询；意外调用到已销毁实体属于调用方逻辑错误，及早抛出比静默
        /// 返回 <c>Vec2.Zero</c> 更利于发现问题（判断记录，见任务汇报"契约缺口"外的实现取舍）。</summary>
        private Entity Require(Id entityId)
        {
            var entity = _world.GetEntity(entityId);
            if (entity == null)
            {
                throw new InvalidOperationException($"实体 \"{entityId}\" 不存在或已销毁，调用前应先检查 Exists");
            }

            return entity;
        }
    }
}
