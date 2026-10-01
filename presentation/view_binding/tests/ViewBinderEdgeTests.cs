// ViewBinderEdgeTests：T-M45（测试覆盖剩余项第四批）。既有 ViewBinderTests 覆盖主路径与两个锚点告警
// 分支（实体未绑定、model 型外形）；这里补：
//   1. [Obsolete] 七参构造（1.12.0/1.13.0 物理签名兼容 façade）：签名存在且带 Obsolete/EditorBrowsable
//      标注、行为与默认八参构造一致（无装备外观联动），空参守卫一致；
//   2. SyncAll 中 Exists=false 分支：实体已不在快照时朝向/高度退为 0、位置沿用最后一次缓存，不抛不记诊断；
//   3. 另外几个锚点告警分支：查不到 DisplayInfo、未登记该锚点、实体不在快照中（各自一条诊断且返回 null）。
using System;
using System.Collections.Generic;
using System.Reflection;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Presentation.Common;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;
using Presentation.ViewBinding;
using Xunit;

namespace Tests.PresentationViewBinding
{
    public class ViewBinderEdgeTests
    {
        private static readonly Id Hero = new Id("unit.edge_hero");

        /// <summary>可控快照：存活时给定位置/朝向/高度，销毁后 <see cref="Exists"/> 为 false。</summary>
        private sealed class ControllableSnapshot : ISimSnapshot
        {
            public Vec2 Position = Vec2.Zero;
            public double Facing;
            public double Height;
            public bool Alive = true;
            public Id? DisplayId;

            public Vec2 GetPosition(Id entityId) => Alive ? Position : throw new InvalidOperationException("entity gone");
            public double GetFacing(Id entityId) => Alive ? Facing : throw new InvalidOperationException("entity gone");
            public double GetHeight(Id entityId) => Alive ? Height : throw new InvalidOperationException("entity gone");
            public bool Exists(Id entityId) => Alive;
            public Id? GetDisplayId(Id entityId) => Alive ? (DisplayId ?? entityId) : (Id?)null;
            public ViewKind? GetKind(Id entityId) => Alive ? ViewKind.Unit : (ViewKind?)null;
        }

        // -----------------------------------------------------------------
        // 1. [Obsolete] 七参构造
        // -----------------------------------------------------------------

        private static readonly Type[] SevenParameterTypes =
        {
            typeof(IEventBus), typeof(IViewFactory), typeof(ISimSnapshot), typeof(IDisplayInfoRegistry),
            typeof(ViewBinderOptions), typeof(IRenderConventionHost), typeof(IPresentationDiagnostics),
        };

        [Fact]
        public void SevenParameterConstructor_StillExists_MarkedObsoleteAndHiddenFromEditor()
        {
            var ctor = typeof(ViewBinder).GetConstructor(SevenParameterTypes);

            Assert.NotNull(ctor); // ABI：旧编译的消费方调用的就是这个物理签名。
            Assert.NotNull(ctor!.GetCustomAttribute<ObsoleteAttribute>());
            var browsable = ctor.GetCustomAttribute<System.ComponentModel.EditorBrowsableAttribute>();
            Assert.NotNull(browsable);
            Assert.Equal(System.ComponentModel.EditorBrowsableState.Never, browsable!.State);
        }

        [Fact]
        public void SevenParameterConstructor_BehavesLikeDefaultConstructor_BindsViewsAndForwardsDiagnostics()
        {
            var bus = ViewBindingTestSupport.CreateBus();
            var factory = new FakeViewFactory();
            var snapshot = new ControllableSnapshot();
            var diagnostics = new PresentationDiagnosticsRecorder();
            var ctor = typeof(ViewBinder).GetConstructor(SevenParameterTypes)!;

            var binder = (ViewBinder)ctor.Invoke(new object?[]
            {
                bus, factory, snapshot, new FakeDisplayInfoRegistry(), null, null, diagnostics,
            });

            binder.OnEntityCreated(Hero, "player", Hero);
            Assert.True(binder.TryGetView(Hero, out var view));
            Assert.NotNull(view);
            Assert.Same(diagnostics, binder.Diagnostics);

            // 经事件订阅同样生效（构造期订阅已建立）。
            var other = new Id("unit.edge_other");
            snapshot.Position = new Vec2(1, 2);
            bus.Enqueue(new Core.Foundation.SimLoop.EntityCreatedEvent(other, "player", other));
            bus.DispatchPending();
            Assert.True(binder.TryGetView(other, out _));
        }

        [Fact]
        public void SevenParameterConstructor_NullGuards_MatchPrimaryConstructor()
        {
            var bus = ViewBindingTestSupport.CreateBus();
            var factory = new FakeViewFactory();
            var snapshot = new ControllableSnapshot();
            var display = new FakeDisplayInfoRegistry();
            var ctor = typeof(ViewBinder).GetConstructor(SevenParameterTypes)!;

            object?[] Args(IEventBus? b, IViewFactory? f, ISimSnapshot? s, IDisplayInfoRegistry? d) =>
                new object?[] { b, f, s, d, null, null, null };

            foreach (var args in new[]
            {
                Args(null, factory, snapshot, display),
                Args(bus, null, snapshot, display),
                Args(bus, factory, null, display),
                Args(bus, factory, snapshot, null),
            })
            {
                var ex = Assert.Throws<TargetInvocationException>(() => ctor.Invoke(args));
                Assert.IsType<ArgumentNullException>(ex.InnerException);
            }
        }

        // -----------------------------------------------------------------
        // 2. SyncAll 中 Exists=false 分支
        // -----------------------------------------------------------------

        [Fact]
        public void SyncAll_EntityGoneFromSnapshot_UsesZeroFacingAndHeight_KeepsLastCachedPosition_NoDiagnostics()
        {
            var bus = ViewBindingTestSupport.CreateBus();
            var factory = new FakeViewFactory();
            var diagnostics = new PresentationDiagnosticsRecorder();
            var snapshot = new ControllableSnapshot { Position = new Vec2(5, 6), Facing = Math.PI / 2, Height = 1.5 };
            var options = new ViewBinderOptions();
            var binder = new ViewBinder(bus, factory, snapshot, new FakeDisplayInfoRegistry(), options, diagnostics: diagnostics);
            binder.OnEntityCreated(Hero, "player", Hero);
            var view = factory.CreatedByEntityId[Hero];
            var convention = new RenderConventionHost();

            // 存活：朝向/高度取快照值。
            binder.SyncAll(1.0);
            var alive = Assert.Single(view.SyncCalls);
            Assert.Equal(snapshot.Position, alive.Pos);
            Assert.Equal(snapshot.Height, alive.Height);
            Assert.Equal(
                Direction.FromQuantized(convention.ApplyFacingConvention(snapshot.Facing), options.DefaultDirectionCount),
                alive.Facing);

            // 快照里已无该实体（View 仍在绑定表）：朝向/高度退为 0，位置沿用缓存，不抛不记诊断。
            snapshot.Alive = false;
            binder.SyncAll(1.0);

            Assert.Equal(2, view.SyncCalls.Count);
            var gone = view.SyncCalls[1];
            Assert.Equal(alive.Pos, gone.Pos);
            Assert.Equal(0.0, gone.Height);
            Assert.Equal(
                Direction.FromQuantized(convention.ApplyFacingConvention(0.0), options.DefaultDirectionCount),
                gone.Facing);
            Assert.Empty(diagnostics.Warnings);
        }

        // -----------------------------------------------------------------
        // 3. 锚点查询告警分支
        // -----------------------------------------------------------------

        private static DisplayInfo SpriteInfoWithAnchors(Id logicalId, IReadOnlyDictionary<string, AnchorDef> anchors)
        {
            var sprite = new SpriteInfo("sprite.edge", directionCount: 8, new List<MirrorPair>(), paperdollLayers: null, anchorPoints: anchors);
            return new DisplayInfo(
                new Id("display.map.edge"), DisplayCategory.Creature, logicalId, DisplayKind.Sprite,
                iconId: null, vfxId: null, sfxId: null, scale: 1.0, shadow: ShadowMode.None, sortOffset: 0,
                weaponStyleRef: null, sprite: sprite, model: null);
        }

        private static (ViewBinder Binder, ControllableSnapshot Snapshot, FakeDisplayInfoRegistry Display, PresentationDiagnosticsRecorder Diag)
            BuildBound(Id logicalId)
        {
            var bus = ViewBindingTestSupport.CreateBus();
            var snapshot = new ControllableSnapshot { Position = new Vec2(10, 20), DisplayId = logicalId };
            var display = new FakeDisplayInfoRegistry();
            var diag = new PresentationDiagnosticsRecorder();
            var binder = new ViewBinder(bus, new FakeViewFactory(), snapshot, display, diagnostics: diag);
            binder.OnEntityCreated(Hero, "player", logicalId);
            return (binder, snapshot, display, diag);
        }

        [Fact]
        public void GetAnchorWorldPosition_DisplayInfoNotRegistered_ReturnsNullWithOneDiagnostic()
        {
            var logicalId = new Id("creature.edge_unregistered");
            var (binder, _, _, diag) = BuildBound(logicalId); // display 注册表里没有该逻辑 id

            var result = binder.GetAnchorWorldPosition(Hero, new Id("anchor.hand_main"));

            Assert.Null(result);
            var warning = Assert.Single(diag.Warnings);
            Assert.Contains(logicalId.Value, warning);
        }

        [Fact]
        public void GetAnchorWorldPosition_AnchorNotRegisteredOnSprite_ReturnsNullWithOneDiagnostic()
        {
            var logicalId = new Id("creature.edge_sprite");
            var (binder, _, display, diag) = BuildBound(logicalId);
            display.Add(SpriteInfoWithAnchors(logicalId, new Dictionary<string, AnchorDef>
            {
                ["hand_main"] = new AnchorDef("layer.hand", new Vec2(1, 1), null),
            }));
            var missingAnchor = new Id("anchor.head_top");

            var result = binder.GetAnchorWorldPosition(Hero, missingAnchor);

            Assert.Null(result);
            var warning = Assert.Single(diag.Warnings);
            Assert.Contains(missingAnchor.Value, warning);
        }

        [Fact]
        public void GetAnchorWorldPosition_EntityGoneFromSnapshot_ReturnsNullWithOneDiagnostic()
        {
            var logicalId = new Id("creature.edge_gone");
            var (binder, snapshot, display, diag) = BuildBound(logicalId);
            display.Add(SpriteInfoWithAnchors(logicalId, new Dictionary<string, AnchorDef>
            {
                ["hand_main"] = new AnchorDef("layer.hand", new Vec2(1, 1), null),
            }));
            snapshot.Alive = false; // View 仍在绑定表，但快照里已没有该实体

            var result = binder.GetAnchorWorldPosition(Hero, new Id("anchor.hand_main"));

            Assert.Null(result);
            var warning = Assert.Single(diag.Warnings);
            Assert.Contains(Hero.Value, warning);
        }

        [Fact]
        public void GetAnchorWorldPosition_ResolvesBareAnchorNameFromLastDotSegment()
        {
            // anchor_points 的键是裸名（hand_main）；调用方传带前缀的 Id（anchor.hand_main），取最后一个点分段。
            var logicalId = new Id("creature.edge_bare");
            var (binder, snapshot, display, diag) = BuildBound(logicalId);
            var offset = new Vec2(2, 3);
            snapshot.Facing = Math.PI / 2; // front 档位：不是 _l 镜像档，偏移不取反
            display.Add(SpriteInfoWithAnchors(logicalId, new Dictionary<string, AnchorDef>
            {
                ["hand_main"] = new AnchorDef("layer.hand", offset, null),
            }));

            var viaPrefix = binder.GetAnchorWorldPosition(Hero, new Id("anchor.hand_main"));
            var viaDeeperPrefix = binder.GetAnchorWorldPosition(Hero, new Id("anchor.sub.hand_main"));

            Assert.NotNull(viaPrefix);
            Assert.NotNull(viaDeeperPrefix);
            Assert.Equal(viaPrefix, viaDeeperPrefix);
            Assert.Empty(diag.Warnings);
            // front 档位：位置 = 实体位置 + 默认偏移。
            Assert.Equal(snapshot.Position + offset, viaPrefix!.Value);
        }
    }
}
