using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Presentation.Common;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;
using Presentation.ViewBinding;
using Adapters.Stub;
using Tests.PresentationRender;
using Xunit;

namespace Tests.PresentationViewBinding
{
    /// <summary>
    /// T-M26 / T-M27（测试覆盖剩余项第四批）：
    /// 1. <see cref="ViewBinder"/> 在 <c>save.loaded</c> 时对保留 View 的装备外观对账段——逐实体失败隔离、
    ///    只对实现 <see cref="IEquipmentVisualResettable"/> 的 View 执行、陈旧 View 不参与；
    /// 2. ADR-0112：View 已提交过方向时 <see cref="ViewBinder.GetAnchorWorldPosition"/> 按"已显示方向"取偏移，
    ///    方向准备期间不提前跳到模拟朝向对应的新方向；未提交过方向时退回按模拟朝向解析。
    /// </summary>
    public class ViewBinderEquipmentReconcileTests
    {
        private static readonly Id MainHand = new Id("slot.hand_main");
        private static readonly Id Template = new Id("item.template.sword");

        // ---------------- 对账段 ----------------

        private sealed class ResettableView : IView, IEquipmentVisualResettable
        {
            public readonly List<IReadOnlyList<EquippedItemRef>> Resets = new List<IReadOnlyList<EquippedItemRef>>();
            public Func<Exception?>? ThrowOnReset;

            public Id EntityId { get; private set; }
            public bool IsAlive { get; private set; }

            public void Bind(Id entityId)
            {
                EntityId = entityId;
                IsAlive = true;
            }

            public void OnEvent(IEvent evt) { }
            public void SyncPose(Vec2 pos, Direction facing, double height) { }
            public void Destroy() => IsAlive = false;

            public void ResetEquipmentVisuals(IReadOnlyList<EquippedItemRef> equipped)
            {
                Resets.Add(equipped);
                var ex = ThrowOnReset?.Invoke();
                if (ex != null) throw ex;
            }
        }

        private sealed class MixedFactory : IViewFactory
        {
            public readonly Dictionary<Id, ResettableView> Resettables = new Dictionary<Id, ResettableView>();
            public readonly Dictionary<Id, FakeView> Plain = new Dictionary<Id, FakeView>();
            public readonly HashSet<Id> PlainEntities = new HashSet<Id>();

            public IView CreateView(ViewKind kind, Id displayId, Id entityId)
            {
                if (PlainEntities.Contains(entityId))
                {
                    var plain = new FakeView();
                    Plain[entityId] = plain;
                    return plain;
                }
                var view = new ResettableView();
                Resettables[entityId] = view;
                return view;
            }
        }

        private sealed class ReconcileFixture
        {
            public IEventBus Bus = null!;
            public MixedFactory Factory = null!;
            public LegacySimSnapshot Snapshot = null!;
            public PresentationDiagnosticsRecorder Diagnostics = null!;
            public ViewBinder Binder = null!;
            public EquipmentVisualSource Source = null!;
            public List<Id> ResolverCalls = new List<Id>();
            public Func<Id, Exception?>? ResolverThrows;
            public Id[] Entities = null!;

            public EquippedItemRef RefFor(Id entity) =>
                new EquippedItemRef(MainHand, new Id("item.instance." + entity.Value.Replace('.', '_')), Template);
        }

        private static ReconcileFixture Build(int count = 3, params int[] plainIndices)
        {
            var fx = new ReconcileFixture
            {
                Bus = ViewBindingTestSupport.CreateBus(),
                Factory = new MixedFactory(),
                Snapshot = new LegacySimSnapshot(),
                Diagnostics = new PresentationDiagnosticsRecorder(),
                Entities = new Id[count],
            };
            for (var i = 0; i < count; i++)
            {
                fx.Entities[i] = new Id("unit.reconcile_" + i);
                if (Array.IndexOf(plainIndices, i) >= 0) fx.Factory.PlainEntities.Add(fx.Entities[i]);
            }

            var catalog = new Dictionary<Id, EquipVisualDef>();
            fx.Source = new EquipmentVisualSource(fx.Bus, catalog, unitId =>
            {
                fx.ResolverCalls.Add(unitId);
                var ex = fx.ResolverThrows?.Invoke(unitId);
                if (ex != null) throw ex;
                return new[] { fx.RefFor(unitId) };
            });
            fx.Binder = new ViewBinder(
                fx.Bus, fx.Factory, fx.Snapshot, new FakeDisplayInfoRegistry(),
                diagnostics: fx.Diagnostics, equipmentVisualSource: fx.Source);
            foreach (var id in fx.Entities)
            {
                fx.Snapshot.SetAlive(id, Vec2.Zero);
                fx.Binder.OnEntityCreated(id, "player", id);
            }
            return fx;
        }

        private static void SaveLoaded(ReconcileFixture fx) =>
            fx.Bus.PublishImmediate(new SaveLoadedEvent(new Id("slot.reconcile_test")));

        [Fact]
        public void SaveLoaded_EveryRetainedResettableView_GetsOneResetWithItsOwnEquippedList()
        {
            var fx = Build();

            SaveLoaded(fx);

            Assert.Equal(fx.Entities, fx.ResolverCalls);
            foreach (var id in fx.Entities)
            {
                var reset = Assert.Single(fx.Factory.Resettables[id].Resets);
                Assert.Equal(fx.RefFor(id).ItemInstanceId, Assert.Single(reset).ItemInstanceId);
            }
            Assert.Empty(fx.Diagnostics.Warnings);
        }

        [Fact]
        public void SaveLoaded_ResolverThrowsForOneEntity_OthersStillReconciled_SingleDiagnosticNamingIt()
        {
            var fx = Build();
            var bad = fx.Entities[1];
            fx.ResolverThrows = id => id.Equals(bad) ? new InvalidOperationException("resolver boom") : null;

            var caught = Record.Exception(() => SaveLoaded(fx));

            Assert.Null(caught);
            Assert.Single(fx.Factory.Resettables[fx.Entities[0]].Resets);
            Assert.Empty(fx.Factory.Resettables[bad].Resets);
            Assert.Single(fx.Factory.Resettables[fx.Entities[2]].Resets);
            var warning = Assert.Single(fx.Diagnostics.Warnings);
            Assert.Contains("装备外观对账失败", warning);
            Assert.Contains(bad.Value, warning);
            Assert.Contains("resolver boom", warning);
        }

        [Fact]
        public void SaveLoaded_ViewResetThrowsForOneEntity_OthersStillReconciled_SingleDiagnosticNamingIt()
        {
            var fx = Build();
            var bad = fx.Entities[1];
            fx.Factory.Resettables[bad].ThrowOnReset = () => new InvalidOperationException("reset boom");

            var caught = Record.Exception(() => SaveLoaded(fx));

            Assert.Null(caught);
            Assert.Single(fx.Factory.Resettables[fx.Entities[0]].Resets);
            Assert.Single(fx.Factory.Resettables[bad].Resets); // 已被调用，抛出被隔离
            Assert.Single(fx.Factory.Resettables[fx.Entities[2]].Resets);
            var warning = Assert.Single(fx.Diagnostics.Warnings);
            Assert.Contains(bad.Value, warning);
            Assert.Contains("reset boom", warning);
        }

        [Fact]
        public void SaveLoaded_ViewNotImplementingResettable_IsSkipped_ResolverNotCalledForIt()
        {
            var fx = Build(3, plainIndices: 1);

            SaveLoaded(fx);

            Assert.Equal(new[] { fx.Entities[0], fx.Entities[2] }, fx.ResolverCalls);
            Assert.Empty(fx.Diagnostics.Warnings);
        }

        [Fact]
        public void SaveLoaded_StaleViewDestroyed_IsNotReconciled()
        {
            var fx = Build();
            fx.Snapshot.SetDestroyed(fx.Entities[1]);

            SaveLoaded(fx);

            Assert.Equal(new[] { fx.Entities[0], fx.Entities[2] }, fx.ResolverCalls);
            Assert.Empty(fx.Factory.Resettables[fx.Entities[1]].Resets);
            Assert.False(fx.Binder.TryGetView(fx.Entities[1], out _));
        }

        [Fact]
        public void SaveLoaded_Twice_ReconcilesAgainWithSameSnapshot_NoDiagnostics()
        {
            var fx = Build(2);

            SaveLoaded(fx);
            SaveLoaded(fx);

            foreach (var id in fx.Entities)
            {
                var resets = fx.Factory.Resettables[id].Resets;
                Assert.Equal(2, resets.Count);
                Assert.Equal(resets[0][0].ItemInstanceId, resets[1][0].ItemInstanceId);
            }
            Assert.Empty(fx.Diagnostics.Warnings);
        }

        [Fact]
        public void SaveLoaded_WithoutEquipmentVisualSource_NoResetCalls()
        {
            var bus = ViewBindingTestSupport.CreateBus();
            var factory = new MixedFactory();
            var snapshot = new LegacySimSnapshot();
            var id = new Id("unit.reconcile_none");
            var binder = new ViewBinder(bus, factory, snapshot, new FakeDisplayInfoRegistry());
            snapshot.SetAlive(id, Vec2.Zero);
            binder.OnEntityCreated(id, "player", id);

            bus.PublishImmediate(new SaveLoadedEvent(new Id("slot.reconcile_none")));

            Assert.Empty(factory.Resettables[id].Resets);
        }

        // ---------------- ADR-0112：锚点按已显示方向取值 ----------------

        private sealed class FacingSnapshot : ISimSnapshot
        {
            public double Facing;
            public Vec2 Position = new Vec2(10, 20);
            public Id DisplayId;

            public Vec2 GetPosition(Id entityId) => Position;
            public double GetFacing(Id entityId) => Facing;
            public double GetHeight(Id entityId) => 0.0;
            public bool Exists(Id entityId) => true;
            public Id? GetDisplayId(Id entityId) => DisplayId;
            public ViewKind? GetKind(Id entityId) => ViewKind.Unit;
        }

        private sealed class GatedFactory : IViewFactory
        {
            private readonly IDisplayInfoRegistry _registry;
            private readonly IRenderConventionHost _conventions;
            public GatedSpriteView? Last;

            public GatedFactory(IDisplayInfoRegistry registry, IRenderConventionHost conventions)
            {
                _registry = registry;
                _conventions = conventions;
            }

            public IView CreateView(ViewKind kind, Id displayId, Id entityId)
            {
                var info = _registry.Lookup(displayId)!;
                Last = new GatedSpriteView(new StubRenderer2D(), _conventions, info);
                return Last;
            }
        }

        private static readonly Id Hero = new Id("unit.anchor_hero");
        private static readonly Id LogicalId = new Id("creature.anchor_hero");
        private static readonly Vec2 DefaultOffset = new Vec2(1, 1);
        private static readonly Vec2 FrontOffset = new Vec2(2, 3);
        private static readonly Vec2 BackOffset = new Vec2(-5, 7);
        private static readonly Id AnchorId = new Id("anchor.hand_main");

        private static (ViewBinder Binder, FacingSnapshot Snapshot, GatedFactory Factory) BuildAnchor()
        {
            var anchors = new Dictionary<string, AnchorDef>
            {
                ["hand_main"] = new AnchorDef("layer.hand", DefaultOffset, new Dictionary<Id, Vec2>
                {
                    [DirectionSlots.Front] = FrontOffset,
                    [DirectionSlots.Back] = BackOffset,
                }),
            };
            var sprite = new SpriteInfo("sprite.anchor", directionCount: 8, new List<MirrorPair>(), paperdollLayers: null, anchorPoints: anchors);
            var info = new DisplayInfo(
                new Id("display.map.anchor"), DisplayCategory.Creature, LogicalId, DisplayKind.Sprite,
                iconId: null, vfxId: null, sfxId: null, scale: 1.0, shadow: Core.Foundation.DisplayInfo.ShadowMode.None, sortOffset: 0,
                weaponStyleRef: null, sprite: sprite, model: null);
            var registry = new FakeDisplayInfoRegistry();
            registry.Add(info);
            var conventions = new RenderConventionHost();
            var factory = new GatedFactory(registry, conventions);
            var snapshot = new FacingSnapshot { DisplayId = LogicalId };
            var binder = new ViewBinder(ViewBindingTestSupport.CreateBus(), factory, snapshot, registry, renderConvention: conventions);
            binder.OnEntityCreated(Hero, "player", LogicalId);
            return (binder, snapshot, factory);
        }

        private const double FrontRadians = Math.PI / 2;
        private const double BackRadians = Math.PI * 3 / 2;

        [Fact]
        public void Anchor_BeforeAnyDirectionCommitted_FollowsSimulationFacing()
        {
            var (binder, snapshot, factory) = BuildAnchor();
            snapshot.Facing = BackRadians;

            var anchor = binder.GetAnchorWorldPosition(Hero, AnchorId);

            Assert.False(factory.Last!.HasDisplayedDirection);
            Assert.Equal(snapshot.Position + BackOffset, anchor!.Value);
        }

        [Fact]
        public void Anchor_AfterCommit_UsesDisplayedDirection_NotTheLeadingSimulationFacing()
        {
            var (binder, snapshot, factory) = BuildAnchor();
            snapshot.Facing = FrontRadians;
            binder.SyncAll(0.0);   // 首次显示：提交 front
            Assert.Equal(DirectionSlots.Front, factory.Last!.DisplayedDirection.SlotId);

            factory.Last.Ready = false; // 方向准备未完成
            snapshot.Facing = BackRadians;
            binder.SyncAll(0.0);

            Assert.True(factory.Last.HasPendingDirectionSwitch);
            var anchor = binder.GetAnchorWorldPosition(Hero, AnchorId);
            Assert.Equal(snapshot.Position + FrontOffset, anchor!.Value); // 仍按已显示的 front，不跳到 back
        }

        [Fact]
        public void Anchor_SwitchesToNewDirection_OnceTheSwitchIsCommitted()
        {
            var (binder, snapshot, factory) = BuildAnchor();
            snapshot.Facing = FrontRadians;
            binder.SyncAll(0.0);
            factory.Last!.Ready = false;
            snapshot.Facing = BackRadians;
            binder.SyncAll(0.0);

            factory.Last.Ready = true;
            binder.SyncAll(0.0);

            Assert.False(factory.Last.HasPendingDirectionSwitch);
            var anchor = binder.GetAnchorWorldPosition(Hero, AnchorId);
            Assert.Equal(snapshot.Position + BackOffset, anchor!.Value);
        }

        [Fact]
        public void Anchor_PendingSwitchCancelled_StillUsesDisplayedDirection()
        {
            var (binder, snapshot, factory) = BuildAnchor();
            snapshot.Facing = FrontRadians;
            binder.SyncAll(0.0);
            factory.Last!.Ready = false;
            snapshot.Facing = BackRadians;
            binder.SyncAll(0.0);

            snapshot.Facing = FrontRadians; // 转回已显示方向：撤销待切换
            binder.SyncAll(0.0);

            Assert.False(factory.Last.HasPendingDirectionSwitch);
            Assert.Equal(snapshot.Position + FrontOffset, binder.GetAnchorWorldPosition(Hero, AnchorId)!.Value);
        }
    }
}
