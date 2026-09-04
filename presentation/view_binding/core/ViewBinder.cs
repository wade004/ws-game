using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Presentation.Common;

namespace Presentation.ViewBinding
{
    /// <summary>
    /// <see cref="IViewBinder"/> 的默认实现（见 01 L5 模块表 <c>view_binding</c> 行、03 第 5、9 节、
    /// 09 第 2 节）：订阅 <c>entity.created</c>/<c>entity.destroyed</c> 创建/销毁 View、维护
    /// "实体 id → View" 绑定表、在 <c>sim.tick_finished</c> 捕获位置快照供插值、把选定事件转发给
    /// 相关 View 的 <see cref="IView.OnEvent"/>。
    /// <para>
    /// 插值：<see cref="SyncAll"/>/<see cref="GetInterpolatedPosition"/> 用
    /// <c>pos = prev + (curr - prev) × alpha</c>（见 03 第 3.1 节）；<c>prev</c>/<c>curr</c> 两份
    /// 快照只在每次 <c>sim.tick_finished</c> 时更新一次（<see cref="OnTickFinished"/>），
    /// <see cref="ISimSnapshot"/> 之间的其它任意时刻读到的都是同一个 <c>curr</c>（WorldSim 只在
    /// <c>Tick</c> 内部修改实体状态，两次 tick 之间状态稳定），因此不需要在每次 <see cref="SyncAll"/>
    /// 调用时重新查询 <see cref="ISimSnapshot"/> 来"追新"。
    /// </para>
    /// <para>
    /// 转发规则：<see cref="ViewBinderOptions.ForwardedEventKeys"/> 里的每个事件 key，收到后按
    /// <see cref="IExprReadableEvent.TryGetField"/> 依次尝试 <c>unitId</c>/<c>targetId</c>/
    /// <c>sourceId</c>/<c>entityId</c>/<c>casterId</c>/<c>gobjInstanceId</c> 六个候选字段名：
    /// 命中且该 id 对应一个已绑定 View 时，把原始事件转发给该 View 的 <see cref="IView.OnEvent"/>；
    /// 一个事件可以同时携带多个候选字段（如 <c>combat.damage_dealt</c> 同时有 <c>sourceId</c> 与
    /// <c>targetId</c>），此时对应的每个已绑定 View 都会各收到一次（例如攻击者与目标各自触发不同的
    /// 反馈表现），同一 View 不会因为多个字段解析出同一个 id 而重复收到两次（用 <see cref="HashSet{T}"/>
    /// 去重）。
    /// </para>
    /// <para>
    /// 判断记录：<see cref="SyncAll"/>/<see cref="Count"/>/<see cref="TryGetView"/> 三个成员未出现在
    /// 03 第 9 节给出的 <c>ViewBinder</c> 接口签名（只有 <c>onEntityCreated</c>/<c>onEntityDestroyed</c>/
    /// <c>getInterpolatedPosition</c> 三个方法）——本类型把接口 <see cref="IViewBinder"/> 严格对齐
    /// 文档给出的最小契约，三个补充成员只作为具体类 <see cref="ViewBinder"/> 的公开成员（供主循环
    /// 组装代码持有具体类型调用），不污染文档定义的最小接口面。
    /// </para>
    /// </summary>
    public sealed class ViewBinder : IViewBinder
    {
        private static readonly string[] CandidateEntityFields =
        {
            "unitId", "targetId", "sourceId", "entityId", "casterId", "gobjInstanceId"
        };

        private readonly IViewFactory _factory;
        private readonly ISimSnapshot _snapshot;
        private readonly IDisplayInfoRegistry _displayInfo;
        private readonly ViewBinderOptions _options;

        private readonly Dictionary<Id, IView> _views = new Dictionary<Id, IView>();
        private readonly Dictionary<Id, Id> _displayIds = new Dictionary<Id, Id>();
        private readonly Dictionary<Id, Vec2> _prevPositions = new Dictionary<Id, Vec2>();
        private readonly Dictionary<Id, Vec2> _currPositions = new Dictionary<Id, Vec2>();

        /// <summary>创建但映射不出已知 <see cref="ViewKind"/> 的实体 <c>Kind</c> 字符串（诊断用，
        /// 见 <see cref="Common.EntityKindMapping"/> 契约缺口说明），供测试/日志查看，不参与任何
        /// 业务判断。</summary>
        public IReadOnlyList<string> UnmappedEntityKinds => _unmappedEntityKinds;
        private readonly List<string> _unmappedEntityKinds = new List<string>();

        public ViewBinder(
            IEventBus bus,
            IViewFactory factory,
            ISimSnapshot snapshot,
            IDisplayInfoRegistry displayInfo,
            ViewBinderOptions? options = null)
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _displayInfo = displayInfo ?? throw new ArgumentNullException(nameof(displayInfo));
            _options = options ?? new ViewBinderOptions();

            bus.Subscribe<EntityCreatedEvent>(SimEventKeys.EntityCreated, e => OnEntityCreated(e.EntityId, e.Kind, e.DisplayId));
            bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, e => OnEntityDestroyed(e.EntityId));
            bus.Subscribe<SimTickFinishedEvent>(SimEventKeys.TickFinished, _ => OnTickFinished());

            for (var i = 0; i < _options.ForwardedEventKeys.Count; i++)
            {
                bus.Subscribe(_options.ForwardedEventKeys[i], OnForwardableEvent);
            }
        }

        /// <summary>当前绑定的 View 数量。</summary>
        public int Count => _views.Count;

        /// <summary>按实体 id 查找已绑定的 View；未绑定返回 false。</summary>
        public bool TryGetView(Id entityId, out IView? view) => _views.TryGetValue(entityId, out view);

        public void OnEntityCreated(Id entityId, string kind, Id displayId)
        {
            if (_views.ContainsKey(entityId))
            {
                return;
            }

            if (!EntityKindMapping.TryMap(kind, out var viewKind))
            {
                _unmappedEntityKinds.Add(kind);
                return;
            }

            var view = _factory.CreateView(viewKind, displayId, entityId);
            view.Bind(entityId);

            _views[entityId] = view;
            _displayIds[entityId] = displayId;

            // 初始化 prev = curr = 当前位置，避免第一帧从 (0,0) 插值出跳变（见类型注释）。
            if (_snapshot.Exists(entityId))
            {
                var pos = _snapshot.GetPosition(entityId);
                _prevPositions[entityId] = pos;
                _currPositions[entityId] = pos;
            }
        }

        public void OnEntityDestroyed(Id entityId)
        {
            if (_views.TryGetValue(entityId, out var view))
            {
                view.Destroy();
                _views.Remove(entityId);
            }

            _displayIds.Remove(entityId);
            _prevPositions.Remove(entityId);
            _currPositions.Remove(entityId);
        }

        public Vec2 GetInterpolatedPosition(Id entityId, double alpha)
        {
            if (!_currPositions.TryGetValue(entityId, out var curr))
            {
                throw new InvalidOperationException($"实体 \"{entityId}\" 未绑定 View，无法插值位置");
            }

            var prev = _prevPositions.TryGetValue(entityId, out var p) ? p : curr;
            return prev + (curr - prev) * alpha;
        }

        /// <summary>驱动全部已绑定 View 的 <see cref="IView.SyncPose"/>（见 09 第 2 节
        /// "syncPose 由主循环在插值阶段调用"）。<paramref name="alpha"/> 通常来自
        /// <see cref="IPresentationClock.Alpha"/>。</summary>
        public void SyncAll(double alpha)
        {
            foreach (var pair in _views)
            {
                var entityId = pair.Key;
                var view = pair.Value;

                var pos = GetInterpolatedPosition(entityId, alpha);
                var facing = _snapshot.Exists(entityId) ? _snapshot.GetFacing(entityId) : 0.0;
                var height = _snapshot.Exists(entityId) ? _snapshot.GetHeight(entityId) : 0.0;
                var direction = ResolveDirection(facing, entityId);

                view.SyncPose(pos, direction, height);
            }
        }

        /// <summary>方向经 <see cref="Direction.FromQuantized"/>（sprite 型，档位来自该 View 的
        /// <c>DisplayInfo.Sprite.DirectionCount</c>）或 <see cref="Direction.Continuous"/>（model 型，
        /// 见 09 第 3.2 节）；查不到 <c>DisplayInfo</c>（未登记/尚未 Reload）时按
        /// <see cref="ViewBinderOptions.DefaultDirectionCount"/> 兜底量化，不让整条 SyncAll 因为
        /// 单个缺失的 DisplayInfo 行而抛异常。</summary>
        private Direction ResolveDirection(double facingRadians, Id entityId)
        {
            if (_displayIds.TryGetValue(entityId, out var displayId))
            {
                var info = _displayInfo.Lookup(displayId);
                if (info != null)
                {
                    if (info.Kind == DisplayKind.Model)
                    {
                        return Direction.Continuous(facingRadians);
                    }

                    if (info.Kind == DisplayKind.Sprite && info.Sprite != null)
                    {
                        return Direction.FromQuantized(facingRadians, info.Sprite.DirectionCount);
                    }
                }
            }

            return Direction.FromQuantized(facingRadians, _options.DefaultDirectionCount);
        }

        private void OnTickFinished()
        {
            foreach (var entityId in new List<Id>(_views.Keys))
            {
                if (!_snapshot.Exists(entityId))
                {
                    continue;
                }

                var curr = _snapshot.GetPosition(entityId);
                var previousCurr = _currPositions.TryGetValue(entityId, out var storedCurr) ? storedCurr : curr;

                _prevPositions[entityId] = previousCurr;
                _currPositions[entityId] = curr;
            }
        }

        private void OnForwardableEvent(IEvent evt)
        {
            if (!(evt is IExprReadableEvent readable))
            {
                return;
            }

            HashSet<Id>? matchedEntityIds = null;

            for (var i = 0; i < CandidateEntityFields.Length; i++)
            {
                if (!readable.TryGetField(CandidateEntityFields[i], out var value) || value.Kind != ExprValueKind.Id)
                {
                    continue;
                }

                var id = value.AsId;
                if (!_views.ContainsKey(id))
                {
                    continue;
                }

                (matchedEntityIds ??= new HashSet<Id>()).Add(id);
            }

            if (matchedEntityIds == null)
            {
                return;
            }

            foreach (var id in matchedEntityIds)
            {
                _views[id].OnEvent(evt);
            }
        }
    }
}
