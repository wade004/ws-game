using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Presentation.Common;
using Presentation.VfxSfx.Contracts;
using Presentation.ViewBinding;
using Xunit;

namespace Tests.PresentationViewBinding
{
    /// <summary>移动测试专用的最小 <see cref="ITickPhaseHandler"/>：消费 <c>Kind == "move"</c> 的
    /// 意图，按 <c>Args{dx,dy}</c> 平移对应实体（不发 <c>unit.moved</c> 事件——
    /// <see cref="ViewBinder"/> 的位置插值只依赖 <c>sim.tick_finished</c> 时刻的
    /// <see cref="ISimSnapshot"/> 快照，不依赖移动事件，见 <c>ViewBinder</c> 类型注释）。</summary>
    internal sealed class TestMoveHandler : ITickPhaseHandler
    {
        public void Execute(SimStep step, IWorldSim world)
        {
            var intents = world.CurrentIntents;
            for (var i = 0; i < intents.Count; i++)
            {
                var intent = intents[i];
                if (intent.Kind != "move")
                {
                    continue;
                }

                var entity = world.GetEntity(intent.ActorId);
                if (entity == null)
                {
                    continue;
                }

                var dx = intent.Args.TryGetValue("dx", out var dxVal) && dxVal is JsonNumber dxNum ? dxNum.Value : 0.0;
                var dy = intent.Args.TryGetValue("dy", out var dyVal) && dyVal is JsonNumber dyNum ? dyNum.Value : 0.0;
                entity.Position = new Vec2(entity.Position.X + dx, entity.Position.Y + dy);
            }
        }
    }

    public class ViewBinderTests
    {
        private static readonly Id MapId = new Id("map.test");

        private static JsonObject MoveArgs(double dx, double dy) =>
            new JsonObjectBuilder().Add("dx", new JsonNumber(dx)).Add("dy", new JsonNumber(dy)).Build();

        private static (IWorldSim World, IEventBus Bus) BuildWorld()
        {
            var bus = ViewBindingTestSupport.CreateBus();
            var world = new WorldSim(bus);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, new TestMoveHandler());
            return (world, bus);
        }

        // 判断记录：ViewBinder 需要一个 ISimSnapshot，而 ISimSnapshot 需要 IWorldSim；测试里
        // world 与 binder 必须共享同一个 bus 与同一个 world 实例，因此统一走这个显式带 world
        // 参数的构造帮助方法。
        private static ViewBinder BuildBinder(
            IWorldSim world,
            IEventBus bus,
            out FakeViewFactory factory,
            out FakeDisplayInfoRegistry displayInfo,
            ViewBinderOptions? options = null)
        {
            factory = new FakeViewFactory();
            displayInfo = new FakeDisplayInfoRegistry();
            var snapshot = new WorldSimSnapshot(world);
            return new ViewBinder(bus, factory, snapshot, displayInfo, options);
        }

        [Fact]
        public void OnEntityCreated_KnownKind_CreatesAndBindsView()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out var factory, out _);

            var entityId = new Id("unit.player_1");
            binder.OnEntityCreated(entityId, "player", new Id("creature.hero"));

            Assert.Equal(1, binder.Count);
            Assert.Single(factory.Calls);
            Assert.Equal(ViewKind.Unit, factory.Calls[0].Kind);
            Assert.Equal(new Id("creature.hero"), factory.Calls[0].DisplayId);
            Assert.True(factory.CreatedByEntityId[entityId].IsAlive);
        }

        [Fact]
        public void OnEntityCreated_UnknownKind_SkipsAndRecordsDiagnostic()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out var factory, out _);

            binder.OnEntityCreated(new Id("x.thing_1"), "totally_unmapped", new Id("x.thing"));

            Assert.Equal(0, binder.Count);
            Assert.Empty(factory.Calls);
            Assert.Contains("totally_unmapped", binder.UnmappedEntityKinds);
        }

        [Fact]
        public void OnEntityCreated_DuplicateId_DoesNotCreateSecondView()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out var factory, out _);
            var entityId = new Id("unit.player_1");

            binder.OnEntityCreated(entityId, "player", new Id("creature.hero"));
            binder.OnEntityCreated(entityId, "player", new Id("creature.hero"));

            Assert.Equal(1, binder.Count);
            Assert.Single(factory.Calls);
        }

        [Fact]
        public void OnEntityDestroyed_DestroysViewAndRemovesFromBindingTable()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out var factory, out _);
            var entityId = new Id("unit.player_1");
            binder.OnEntityCreated(entityId, "player", new Id("creature.hero"));

            binder.OnEntityDestroyed(entityId);

            Assert.Equal(0, binder.Count);
            Assert.True(factory.CreatedByEntityId[entityId].Destroyed);
            Assert.False(binder.TryGetView(entityId, out _));
        }

        [Fact]
        public void OnEntityDestroyed_UnknownId_NoOp()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out _, out _);

            var ex = Record.Exception(() => binder.OnEntityDestroyed(new Id("unit.missing")));

            Assert.Null(ex);
        }

        [Fact]
        public void WorldSim_EntityCreatedEvent_CreatesViewAutomatically()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out var factory, out _);

            var entity = new TestEntity(new Id("unit.auto_1"), MapId);
            world.AddEntity(entity);
            world.Tick(SimStep.Continuous(0.016));

            Assert.Equal(1, binder.Count);
            Assert.Single(factory.Calls);
        }

        [Fact]
        public void WorldSim_EntityDestroyedEvent_DestroysViewAutomatically()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out var factory, out _);

            var entity = new TestEntity(new Id("unit.auto_2"), MapId);
            world.AddEntity(entity);
            world.Tick(SimStep.Continuous(0.016));
            Assert.Equal(1, binder.Count);

            world.MarkForDestruction(entity.EntityId);
            world.Tick(SimStep.Continuous(0.016));

            Assert.Equal(0, binder.Count);
            Assert.True(factory.CreatedByEntityId[entity.EntityId].Destroyed);
        }

        [Fact]
        public void Interpolation_AcrossTwoTicks_Alpha0And1And0_5_ReturnExpectedPositions()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out _, out _);

            var entity = new TestEntity(new Id("unit.mover"), MapId) { Position = new Vec2(0, 0) };
            world.AddEntity(entity);
            world.Tick(SimStep.Continuous(0.016)); // tick 1：创建 View，prev=curr=(0,0)

            // tick 2：从 (0,0) 移动到 (10,0)
            world.SubmitIntent(new Intent(entity.EntityId, "move", MoveArgs(10, 0)));
            world.Tick(SimStep.Continuous(0.016));

            Assert.Equal(new Vec2(0, 0), binder.GetInterpolatedPosition(entity.EntityId, 0.0));
            Assert.Equal(new Vec2(10, 0), binder.GetInterpolatedPosition(entity.EntityId, 1.0));
            Assert.Equal(new Vec2(5, 0), binder.GetInterpolatedPosition(entity.EntityId, 0.5));
        }

        [Fact]
        public void Interpolation_ThirdTick_PrevBecomesSecondTickPosition()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out _, out _);

            var entity = new TestEntity(new Id("unit.mover"), MapId) { Position = new Vec2(0, 0) };
            world.AddEntity(entity);
            world.Tick(SimStep.Continuous(0.016));

            world.SubmitIntent(new Intent(entity.EntityId, "move", MoveArgs(10, 0)));
            world.Tick(SimStep.Continuous(0.016));

            world.SubmitIntent(new Intent(entity.EntityId, "move", MoveArgs(0, 4)));
            world.Tick(SimStep.Continuous(0.016));

            Assert.Equal(new Vec2(10, 0), binder.GetInterpolatedPosition(entity.EntityId, 0.0));
            Assert.Equal(new Vec2(10, 4), binder.GetInterpolatedPosition(entity.EntityId, 1.0));
        }

        [Fact]
        public void GetInterpolatedPosition_UnboundEntity_Throws()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out _, out _);

            Assert.Throws<InvalidOperationException>(() => binder.GetInterpolatedPosition(new Id("unit.missing"), 0.5));
        }

        [Fact]
        public void SyncAll_CallsSyncPoseOnceForEachBoundView_WithInterpolatedPosition()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out var factory, out _);

            var entity = new TestEntity(new Id("unit.mover"), MapId) { Position = new Vec2(0, 0) };
            world.AddEntity(entity);
            world.Tick(SimStep.Continuous(0.016));
            world.SubmitIntent(new Intent(entity.EntityId, "move", MoveArgs(2, 0)));
            world.Tick(SimStep.Continuous(0.016));

            binder.SyncAll(0.5);

            var view = factory.CreatedByEntityId[entity.EntityId];
            Assert.Single(view.SyncCalls);
            Assert.Equal(new Vec2(1, 0), view.SyncCalls[0].Pos);
        }

        [Fact]
        public void SyncAll_SpriteDisplayInfo_QuantizesDirection()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out var factory, out var displayInfo);

            var displayId = new Id("creature.hero");
            displayInfo.Add(new DisplayInfo(
                new Id("display.hero"), DisplayCategory.Creature, displayId, DisplayKind.Sprite,
                null, null, null, 1.0, ShadowMode.Blob, 0.0, null,
                new SpriteInfo("sprite.hero", 8), null));

            var entity = new TestEntity(new Id("unit.hero_1"), MapId) { Facing = 0.0 };
            entity.TemplateId = displayId;
            world.AddEntity(entity);
            world.Tick(SimStep.Continuous(0.016));

            binder.SyncAll(0.0);

            var facing = factory.CreatedByEntityId[entity.EntityId].SyncCalls[0].Facing;
            Assert.Equal(0, facing.Index);
            Assert.Equal(8, facing.DirectionCount);
        }

        [Fact]
        public void SyncAll_ModelDisplayInfo_UsesContinuousDirection()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out var factory, out var displayInfo);

            var displayId = new Id("creature.golem");
            displayInfo.Add(new DisplayInfo(
                new Id("display.golem"), DisplayCategory.Creature, displayId, DisplayKind.Model,
                null, null, null, 1.0, ShadowMode.Blob, 0.0, null,
                null, new ModelInfo(new Id("model.golem"), new Id("display.golem_anim"))));

            var entity = new TestEntity(new Id("unit.golem_1"), MapId) { Facing = 1.234 };
            entity.TemplateId = displayId;
            world.AddEntity(entity);
            world.Tick(SimStep.Continuous(0.016));

            binder.SyncAll(0.0);

            var facing = factory.CreatedByEntityId[entity.EntityId].SyncCalls[0].Facing;
            Assert.Equal(1.234, facing.RawRadians);
            Assert.Equal(0, facing.DirectionCount);
        }

        [Fact]
        public void SyncAll_NoDisplayInfo_FallsBackToDefaultDirectionCount()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out var factory, out _, new ViewBinderOptions(defaultDirectionCount: 4));

            var entity = new TestEntity(new Id("unit.nodisplay_1"), MapId) { Facing = 0.0 };
            world.AddEntity(entity);
            world.Tick(SimStep.Continuous(0.016));

            binder.SyncAll(0.0);

            var facing = factory.CreatedByEntityId[entity.EntityId].SyncCalls[0].Facing;
            Assert.Equal(4, facing.DirectionCount);
        }

        [Fact]
        public void EventForwarding_MatchingSourceId_ForwardsToView()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out var factory, out _);

            var entity = new TestEntity(new Id("unit.attacker"), MapId);
            world.AddEntity(entity);
            world.Tick(SimStep.Continuous(0.016));

            var evt = new CombatDamageDealtEvent(entity.EntityId, new Id("unit.other"), new Id("school.physical"), 10, false, HitResult.Hit);
            bus.PublishImmediate(evt);

            var view = factory.CreatedByEntityId[entity.EntityId];
            Assert.Single(view.ReceivedEvents);
            Assert.Same(evt, view.ReceivedEvents[0]);
        }

        [Fact]
        public void EventForwarding_MultipleFields_ForwardsToBothMatchingViews()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out var factory, out _);

            var attacker = new TestEntity(new Id("unit.attacker"), MapId);
            var target = new TestEntity(new Id("unit.target"), MapId);
            world.AddEntity(attacker);
            world.AddEntity(target);
            world.Tick(SimStep.Continuous(0.016));

            var evt = new CombatDamageDealtEvent(attacker.EntityId, target.EntityId, new Id("school.physical"), 10, false, HitResult.Hit);
            bus.PublishImmediate(evt);

            Assert.Single(factory.CreatedByEntityId[attacker.EntityId].ReceivedEvents);
            Assert.Single(factory.CreatedByEntityId[target.EntityId].ReceivedEvents);
        }

        [Fact]
        public void EventForwarding_NoMatchingBoundView_NotForwardedAnywhere()
        {
            var (world, bus) = BuildWorld();
            BuildBinder(world, bus, out var factory, out _);

            var evt = new CombatDamageDealtEvent(new Id("unit.ghost_a"), new Id("unit.ghost_b"), new Id("school.physical"), 10, false, HitResult.Hit);
            var ex = Record.Exception(() => bus.PublishImmediate(evt));

            Assert.Null(ex);
            Assert.Empty(factory.Calls);
        }

        [Fact]
        public void EventForwarding_UnitMovedEvent_NotForwarded_NotInDefaultKeys()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out var factory, out _);

            var entity = new TestEntity(new Id("unit.mover_evt"), MapId);
            world.AddEntity(entity);
            world.Tick(SimStep.Continuous(0.016));

            var evt = new Core.Carriers.Common.UnitMovedEvent(entity.EntityId, new Vec2(1, 1));
            bus.PublishImmediate(evt);

            Assert.Empty(factory.CreatedByEntityId[entity.EntityId].ReceivedEvents);
        }

        // -----------------------------------------------------------------
        // 缺口 5（退订）：Dispose 后不再响应 entity.created/entity.destroyed/sim.tick_finished。
        // -----------------------------------------------------------------

        [Fact]
        public void Dispose_ThenEntityCreatedEvent_DoesNotCreateView()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out var factory, out _);
            binder.Dispose();

            var entity = new TestEntity(new Id("unit.after_dispose"), MapId);
            world.AddEntity(entity);
            world.Tick(SimStep.Continuous(0.016));

            Assert.Equal(0, binder.Count);
            Assert.Empty(factory.Calls);
        }

        [Fact]
        public void Dispose_IsIdempotent_CalledTwiceDoesNotThrow()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out _, out _);

            var ex = Record.Exception(() =>
            {
                binder.Dispose();
                binder.Dispose();
            });

            Assert.Null(ex);
        }

        // -----------------------------------------------------------------
        // 缺口 6（IAnchorQuery）：占位英雄 hand_main 锚点在 front/side_l 两档位下的世界坐标。
        // -----------------------------------------------------------------

        private static DisplayInfo MakeSpriteDisplayInfo(Id displayId, Id logicalId, Vec2 handMainOffset)
        {
            var mirrorPairs = new List<MirrorPair>
            {
                new MirrorPair(DirectionSlots.SideL, DirectionSlots.SideR, flipX: true),
                new MirrorPair(DirectionSlots.FrontSideL, DirectionSlots.FrontSideR, flipX: true),
                new MirrorPair(DirectionSlots.BackSideL, DirectionSlots.BackSideR, flipX: true),
            };
            var anchorPoints = new Dictionary<string, Vec2> { ["hand_main"] = handMainOffset };
            var sprite = new SpriteInfo("sprite.placeholder_hero", directionCount: 8, mirrorPairs, paperdollLayers: null, anchorPoints: anchorPoints);
            return new DisplayInfo(
                displayId, DisplayCategory.Creature, logicalId, DisplayKind.Sprite,
                iconId: null, vfxId: null, sfxId: null, scale: 1.0, shadow: ShadowMode.Blob, sortOffset: 0,
                weaponStyleRef: null, sprite: sprite, model: null);
        }

        [Fact]
        public void GetAnchorWorldPosition_FrontAndSideL_MatchesAnchorPointsWithMirrorFlip()
        {
            var (world, bus) = BuildWorld();
            var binder = BuildBinder(world, bus, out _, out var displayInfo);

            // 判断记录：ViewBinder._displayIds 存的是 EntityCreatedEvent.DisplayId（=
            // entity.TemplateId ?? entity.EntityId），而 IDisplayInfoRegistry.Lookup 按
            // DisplayInfo.LogicalId 查（见 IDisplayInfoRegistry.Lookup 签名注释"按……逻辑对象的外形"），
            // 两者是同一个 id——生产环境下 entity.TemplateId（如 CreatureUnit 的 creature.template
            // id）与其 display.map 行的 logical_id 按惯例取同一个值，本测试直接令两者相等，不另造一个
            // "display.map.<name>" 记录自身 id 去冒充它（那是 DisplayInfo.Id，不参与本次查找）。
            var logicalId = new Id("creature.sample_hero");
            var handMainOffset = new Vec2(1.1875, 1.8125);
            displayInfo.Add(MakeSpriteDisplayInfo(new Id("display.map.sample_hero"), logicalId, handMainOffset));

            var entity = new TestEntity(new Id("unit.anchor_hero"), MapId) { Position = new Vec2(10, 20), TemplateId = logicalId };
            world.AddEntity(entity);
            world.Tick(SimStep.Continuous(0.016));

            // front：量化索引 2（8 方向），角度 90°（DirectionSlots 类型注释"8 方向完整对照表"）。
            entity.Facing = Math.PI / 2;
            var front = binder.GetAnchorWorldPosition(entity.EntityId, new Id("anchor.hand_main"));
            Assert.NotNull(front);
            Assert.Equal(10 + handMainOffset.X, front!.Value.X, 6);
            Assert.Equal(20 + handMainOffset.Y, front.Value.Y, 6);

            // side_l：量化索引 0，镜像自 side_r，水平分量取反。
            entity.Facing = 0.0;
            var sideL = binder.GetAnchorWorldPosition(entity.EntityId, new Id("anchor.hand_main"));
            Assert.NotNull(sideL);
            Assert.Equal(10 - handMainOffset.X, sideL!.Value.X, 6);
            Assert.Equal(20 + handMainOffset.Y, sideL.Value.Y, 6);
        }

        [Fact]
        public void GetAnchorWorldPosition_UnboundEntity_ReturnsNullAndRecordsDiagnostic()
        {
            var (world, bus) = BuildWorld();
            var diagnostics = new PresentationDiagnosticsRecorder();
            var factory = new FakeViewFactory();
            var displayInfo = new FakeDisplayInfoRegistry();
            var snapshot = new WorldSimSnapshot(world);
            var binder = new ViewBinder(bus, factory, snapshot, displayInfo, diagnostics: diagnostics);

            var result = binder.GetAnchorWorldPosition(new Id("unit.missing"), new Id("anchor.hand_main"));

            Assert.Null(result);
            Assert.NotEmpty(diagnostics.Warnings);
        }

        [Fact]
        public void GetAnchorWorldPosition_ModelKindDisplayInfo_ReturnsNullAndRecordsDiagnostic()
        {
            var (world, bus) = BuildWorld();
            var diagnostics = new PresentationDiagnosticsRecorder();
            var factory = new FakeViewFactory();
            var displayInfo = new FakeDisplayInfoRegistry();
            var snapshot = new WorldSimSnapshot(world);
            var binder = new ViewBinder(bus, factory, snapshot, displayInfo, diagnostics: diagnostics);

            var logicalId = new Id("creature.model_hero");
            var model = new ModelInfo(new Id("model.hero"), new Id("anim_set.hero"), sockets: null, slots: null, defaultSlotMeshes: null, materialParams: null);
            displayInfo.Add(new DisplayInfo(
                new Id("display.map.model_hero"), DisplayCategory.Creature, logicalId, DisplayKind.Model,
                iconId: null, vfxId: null, sfxId: null, scale: 1.0, shadow: ShadowMode.Blob, sortOffset: 0,
                weaponStyleRef: null, sprite: null, model: model));

            var entity = new TestEntity(new Id("unit.model_hero"), MapId) { TemplateId = logicalId };
            world.AddEntity(entity);
            world.Tick(SimStep.Continuous(0.016));

            var result = binder.GetAnchorWorldPosition(entity.EntityId, new Id("anchor.hand_main"));

            Assert.Null(result);
            Assert.NotEmpty(diagnostics.Warnings);
        }
    }
}
