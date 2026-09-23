using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;

namespace Presentation.ViewBinding
{
    /// <summary>
    /// ADR-0078：步幅位移事件——由表现层按累计位移派生 <c>unit.stride_completed</c>，不改动仿真
    /// 主循环、不进规则层。订阅 <see cref="UnitMovedEvent"/>（<c>unit.moved</c>），按单位在
    /// <c>display.map</c> 登记的步幅距离（<see cref="DisplayInfo.StrideDistance"/>，可选字段，未登记
    /// 或 <![CDATA[<=]]> 0 时该单位完全不发本事件，零开销 opt-in）累计位移，达到步幅距离阈值即发一次
    /// <see cref="UnitStrideCompletedEvent"/> 并扣减累计值（保留余数，不清零——同框架既有"保留余数
    /// 不产生漂移"折算惯例，如 <c>SimTimers.RescaleAll</c>/<c>CooldownTracker</c>）。
    /// <para>
    /// 单位 → 步幅距离的解析：同 <c>ViewBinder.OnEntityCreated</c>/<c>WorldSim.AddEntity</c> 既有惯例
    /// （<c>displayId = entity.TemplateId ?? entity.EntityId</c>），本类型按 <c>unitId</c> 查
    /// <see cref="IWorldSim.GetEntity"/> 取 <see cref="Entity.TemplateId"/>（为空退回 <c>unitId</c>
    /// 本身）作为 <see cref="IDisplayInfoRegistry.Lookup"/> 的逻辑 id，与 <c>display.map.logical_id</c>
    /// 同一套映射规则，不新建第二套"实体 → 外形"解析路径。
    /// </para>
    /// <para>
    /// 判断记录（本事件只陈述几何事实，不预设呈现含义）：事件字段只有 <c>unitId</c>/<c>position</c>，
    /// 不携带任何"该不该出声""是不是确认类操作"一类呈现意图字段——同一个信号可能被具体游戏用来触发
    /// 脚步声、扬尘特效、镜头微晃、地面痕迹，也可能什么都不接，框架不替具体游戏做这个决定（13 第 5
    /// 节"框架只给状态，呈现归游戏"）。本类型自身也不登记、不预设任何默认音效/特效绑定，
    /// <c>data/_sample/feedback/feedback.binding.json</c> 里的示例行只是"怎么用"的示范，不是推荐配置。
    /// </para>
    /// <para>
    /// 判断记录（单次位移钳制）：传送/瞬移等大跨度单帧位移不按"距离/步幅距离"整除发多条——那会在
    /// 瞬移落地的同一帧内刷出大量 <c>unit.stride_completed</c>，语义已经不是"走过了一个步幅"。钳制
    /// 规则：单次 <c>unit.moved</c> 位移超过 <see cref="TeleportDistanceMultiplier"/>（8）倍步幅距离
    /// 时，只发一条，并把该单位的累计位移清零（不保留余数——大位移的"多余部分"不是行走产生的，没有
    /// "下一步该扣多少"的几何意义，清零避免下一次正常行走时被这笔跳变位移污染）。
    /// </para>
    /// </summary>
    public sealed class StrideEmitter : IDisposable
    {
        /// <summary>单次位移超过步幅距离的这个倍数即视为瞬移，只发一条并清零累计（见类型注释
        /// "判断记录（单次位移钳制）"）。</summary>
        public const double TeleportDistanceMultiplier = 8.0;

        private readonly IWorldSim _world;
        private readonly IDisplayInfoRegistry _displayInfo;
        private readonly IEventBus _bus;
        private readonly SubscriptionHandle _subscription;

        private readonly Dictionary<Id, Vec2> _lastPosition = new Dictionary<Id, Vec2>();
        private readonly Dictionary<Id, double> _accumulated = new Dictionary<Id, double>();

        public StrideEmitter(IWorldSim world, IDisplayInfoRegistry displayInfo, IEventBus bus)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _displayInfo = displayInfo ?? throw new ArgumentNullException(nameof(displayInfo));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _subscription = bus.Subscribe<UnitMovedEvent>(CarriersEventKeys.UnitMoved, OnUnitMoved);
        }

        private void OnUnitMoved(UnitMovedEvent evt)
        {
            var unitId = evt.UnitId;
            var strideDistance = ResolveStrideDistance(unitId);
            if (strideDistance == null || strideDistance.Value <= 0)
            {
                // 未登记步幅距离（或登记为 <=0）：完全不发本事件，也不维护该单位的位移累计状态
                // （若此前登记过、现在退化为未登记，顺带清掉残留状态，避免下次重新登记时被污染）。
                _lastPosition.Remove(unitId);
                _accumulated.Remove(unitId);
                return;
            }

            if (!_lastPosition.TryGetValue(unitId, out var last))
            {
                // 首次观测到该单位的位置，没有"上一次位置"可比较，不产生虚假位移。
                _lastPosition[unitId] = evt.Position;
                return;
            }

            var delta = Vec2.Distance(last, evt.Position);
            _lastPosition[unitId] = evt.Position;

            if (delta <= 0)
            {
                return;
            }

            if (delta > strideDistance.Value * TeleportDistanceMultiplier)
            {
                // 瞬移钳制：只发一条，累计清零（见类型注释"判断记录（单次位移钳制）"）。
                _accumulated[unitId] = 0;
                _bus.Enqueue(new UnitStrideCompletedEvent(unitId, evt.Position));
                return;
            }

            var accumulated = (_accumulated.TryGetValue(unitId, out var prevAcc) ? prevAcc : 0) + delta;
            while (accumulated >= strideDistance.Value)
            {
                accumulated -= strideDistance.Value;
                _bus.Enqueue(new UnitStrideCompletedEvent(unitId, evt.Position));
            }
            _accumulated[unitId] = accumulated;
        }

        /// <summary>见类型注释"单位 → 步幅距离的解析"。</summary>
        private double? ResolveStrideDistance(Id unitId)
        {
            var entity = _world.GetEntity(unitId);
            var displayId = entity?.TemplateId ?? unitId;
            return _displayInfo.Lookup(displayId)?.StrideDistance;
        }

        public void Dispose()
        {
            _subscription.Dispose();
        }
    }
}
