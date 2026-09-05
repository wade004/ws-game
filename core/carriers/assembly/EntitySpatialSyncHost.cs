using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;

namespace Core.Carriers.Assembly
{
    /// <summary>
    /// 把实体创建/销毁同步进 <see cref="ISpatialQuery"/>（ADR-0016 决策 7：WorldSim 在对象创建时
    /// 调用 Register、销毁时调用 Unregister）。订阅 <see cref="SimEventKeys.EntityCreated"/>/
    /// <see cref="SimEventKeys.EntityDestroyed"/>，按 <see cref="Entity.Kind"/> 是否在
    /// <see cref="_kinds"/> 配置清单里决定是否登记，登记时带上按种类区分的标签。
    /// <para>
    /// 判断记录（登记时机三分：创建/移动/销毁分属不同类型）：<see cref="Core.Carriers.Unit.WorldUnitAccess.SetPosition"/>
    /// 只负责"移动"这一个时机（经 <see cref="ISpatialQuery.UpdatePosition"/>），因为它只在
    /// <c>MovementTickHandler</c> 位移推进时被调用，不覆盖"创建"（`WorldSim.AddEntity` 时单位/
    /// 物件的初始位置已经确定，但那一刻不经过 <c>WorldUnitAccess.SetPosition</c>）与"销毁"
    /// （`WorldSim` 生命周期清理，同样不经过 `WorldUnitAccess`）两个时机。本类型统一承担"创建"与
    /// "销毁"，与 `WorldUnitAccess` 的"移动"职责互补，三者合起来满足 ADR-0016 决策 7 的完整登记
    /// 时机要求。
    /// </para>
    /// <para>
    /// 判断记录（用标签区分 unit/gobj，避免"最近敌人"误捞物件）：<c>Core.Rules.Ai.AiHost</c>
    /// 的 <c>FindNearestHostile</c> 目前用 <c>QueryFilter.None</c>（不做标签过滤）查询空间索引，
    /// 靠随后对每个候选调用 <c>IUnitAccess.Exists</c> 过滤掉非 <c>Unit</c> 的 id 来保证正确性——
    /// 也就是说即使不打标签，现有调用方也不会因为物件混入候选列表而出错。本类型仍然按种类打上
    /// <c>"unit"</c>/<c>"gobj"</c> 标签，把"数据从哪来、是什么"在登记时点显式表达出来，供以后需要
    /// 用 <c>QueryFilter.RequiredTags</c> 做更严格早期过滤的调用方（减少不必要的
    /// <c>IUnitAccess.Exists</c> 探测）直接复用，不需要再回来改登记逻辑。
    /// </para>
    /// </summary>
    public sealed class EntitySpatialSyncHost
    {
        /// <summary>某个 <see cref="Entity.Kind"/> 参与空间索引时使用的半径与标签。</summary>
        public readonly struct KindConfig
        {
            public double Radius { get; }
            public IReadOnlyList<string> Tags { get; }

            public KindConfig(double radius, IReadOnlyList<string> tags)
            {
                Radius = radius;
                Tags = tags;
            }
        }

        private readonly IWorldSim _world;
        private readonly ISpatialQuery _spatial;
        private readonly IReadOnlyDictionary<string, KindConfig> _kinds;
        private readonly HashSet<Id> _registered = new HashSet<Id>();

        public EntitySpatialSyncHost(
            IEventBus bus,
            IWorldSim world,
            ISpatialQuery spatial,
            IReadOnlyDictionary<string, KindConfig> kinds)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
            _kinds = kinds ?? throw new ArgumentNullException(nameof(kinds));

            bus.Subscribe<EntityCreatedEvent>(SimEventKeys.EntityCreated, OnEntityCreated);
            bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, OnEntityDestroyed);
        }

        private void OnEntityCreated(EntityCreatedEvent evt)
        {
            if (!_kinds.TryGetValue(evt.Kind, out var config))
            {
                return;
            }

            var entity = _world.GetEntity(evt.EntityId);
            if (entity == null)
            {
                return;
            }

            _spatial.Register(evt.EntityId, entity.Position, config.Radius, config.Tags);
            _registered.Add(evt.EntityId);
        }

        private void OnEntityDestroyed(EntityDestroyedEvent evt)
        {
            if (_registered.Remove(evt.EntityId))
            {
                _spatial.Unregister(evt.EntityId);
            }
        }
    }
}
