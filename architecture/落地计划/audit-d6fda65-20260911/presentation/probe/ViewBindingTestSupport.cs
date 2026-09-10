using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Presentation.Common;

namespace Tests.PresentationViewBinding
{
    /// <summary>测试共用的最小 <see cref="Entity"/> 子类（惯例同
    /// <c>core/foundation/sim_loop/tests/TestEntity.cs</c>）。</summary>
    internal sealed class TestEntity : Entity
    {
        public override string Kind { get; }

        public TestEntity(Id entityId, Id mapId, string kind = "player")
            : base(entityId, mapId)
        {
            Kind = kind;
        }
    }

    /// <summary>记录每次 <see cref="IView.SyncPose"/> 调用参数的最小假 <see cref="IView"/> 实现。</summary>
    internal sealed class FakeView : IView
    {
        public Id EntityId { get; private set; }

        public bool IsAlive { get; private set; }

        public bool Destroyed { get; private set; }

        public readonly List<IEvent> ReceivedEvents = new List<IEvent>();

        public readonly List<(Vec2 Pos, Direction Facing, double Height)> SyncCalls =
            new List<(Vec2, Direction, double)>();

        public void Bind(Id entityId)
        {
            EntityId = entityId;
            IsAlive = true;
        }

        public void OnEvent(IEvent evt) => ReceivedEvents.Add(evt);

        public void SyncPose(Vec2 pos, Direction facing, double height) => SyncCalls.Add((pos, facing, height));

        public void Destroy()
        {
            IsAlive = false;
            Destroyed = true;
        }
    }

    /// <summary>记录每次 <see cref="IViewFactory.CreateView"/> 调用参数，并把创建出的
    /// <see cref="FakeView"/> 按实体 id 存起来供测试断言用。</summary>
    internal sealed class FakeViewFactory : IViewFactory
    {
        public readonly List<(ViewKind Kind, Id DisplayId, Id EntityId)> Calls =
            new List<(ViewKind, Id, Id)>();

        public readonly Dictionary<Id, FakeView> CreatedByEntityId = new Dictionary<Id, FakeView>();

        public IView CreateView(ViewKind kind, Id displayId, Id entityId)
        {
            Calls.Add((kind, displayId, entityId));
            var view = new FakeView();
            CreatedByEntityId[entityId] = view;
            return view;
        }
    }

    /// <summary>最小假 <see cref="IDisplayInfoRegistry"/>：按 <c>LogicalId</c> 登记/查询，其余成员
    /// 返回空（测试只用到 <see cref="Lookup"/>）。</summary>
    internal sealed class FakeDisplayInfoRegistry : IDisplayInfoRegistry
    {
        private readonly Dictionary<Id, DisplayInfo> _byLogicalId = new Dictionary<Id, DisplayInfo>();

        public void Add(DisplayInfo info) => _byLogicalId[info.LogicalId] = info;

        public DisplayInfo? Lookup(Id logicalId) =>
            _byLogicalId.TryGetValue(logicalId, out var info) ? info : null;

        public IReadOnlyList<DisplayInfo> LookupByCategory(DisplayCategory category) => Array.Empty<DisplayInfo>();

        public IReadOnlyList<DisplayInfo> All => Array.Empty<DisplayInfo>();

        public void Reload()
        {
        }
    }

    /// <summary>
    /// PRES-180 版本判据根治回归专用（见 <see cref="ISimSnapshot"/> 类型注释、
    /// <c>ViewBinder.OnSaveLoaded</c> 方法注释两处"安全退化"判断记录）：只实现
    /// <see cref="ISimSnapshot"/> 在 <see cref="ISimSnapshot.GetAllEntityIds"/>/
    /// <see cref="ISimSnapshot.GetRawKind"/> 改为 C#8 默认接口方法之前就存在的旧成员集合，故意不
    /// 覆盖这两个新成员——用来验证"默认接口方法"这一改动本身就足以让这类历史实现继续编译通过，且
    /// 走到默认实现（空集合/<c>null</c>）时 <c>ViewBinder.OnSaveLoaded</c> 不会抛异常或误判。不依赖
    /// <see cref="IWorldSim"/>，用测试直接控制的"存活实体 id → 位置"字典模拟只读快照。
    /// </summary>
    internal sealed class LegacySimSnapshot : ISimSnapshot
    {
        private readonly Dictionary<Id, Vec2> _alive = new Dictionary<Id, Vec2>();

        public void SetAlive(Id entityId, Vec2 position) => _alive[entityId] = position;

        public void SetDestroyed(Id entityId) => _alive.Remove(entityId);

        public Vec2 GetPosition(Id entityId) =>
            _alive.TryGetValue(entityId, out var pos)
                ? pos
                : throw new InvalidOperationException($"实体 \"{entityId}\" 不存在或已销毁");

        public double GetFacing(Id entityId) => 0.0;

        public double GetHeight(Id entityId) => 0.0;

        public bool Exists(Id entityId) => _alive.ContainsKey(entityId);

        public Id? GetDisplayId(Id entityId) => _alive.ContainsKey(entityId) ? (Id?)entityId : null;

        public ViewKind? GetKind(Id entityId) => _alive.ContainsKey(entityId) ? ViewKind.Unit : (ViewKind?)null;

        // 判断记录：故意不覆盖 GetRawKind/GetAllEntityIds——两者走 ISimSnapshot 的默认接口实现
        // （分别恒定返回 null/空集合），这正是本类型要验证的行为，不是遗漏。
    }

    internal static class ViewBindingTestSupport
    {
        public static IEventBus CreateBus()
        {
            var catalog = EventCatalog.FromDefinitions(Array.Empty<EventDefinition>());
            return new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
        }
    }
}
