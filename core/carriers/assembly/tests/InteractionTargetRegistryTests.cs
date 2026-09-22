using System;
using Core.Carriers.Assembly;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Carriers.Assembly
{
    /// <summary>
    /// ADR-0062（消费方反馈第五批第 1 条续）：<see cref="InteractionTargetRegistry"/> 独立单元测试
    /// （惯例同 <c>EntitySpatialSyncHostTests</c>：不经过完整 <see cref="CarriersAssembly"/> 装配、
    /// 不需要任何数据表，直接用 <see cref="WorldSim"/>）。分类只依赖 <see cref="Entity.Kind"/>
    /// 字符串（见 <see cref="IInteractionTargetRegistry"/> 类型注释判断记录），因此用一个最小的
    /// <see cref="FakeInteractableEntity"/> 覆盖 gobj/creature/loot 三种分类，不需要真正构造
    /// <c>GameObjectEntity</c>/<c>DroppedLootEntity</c>（后者是 L4 类型，本项目不依赖 L4）——三类
    /// 目标经生产装配入口的贯通验证见 <c>Tests.Presentation.Assembly.PresentationAssemblyTests.
    /// InteractPathProvider_*</c>。
    /// </summary>
    public class InteractionTargetRegistryTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id OtherMapId = new Id("map.other");
        private static readonly Id FactionId = new Id("fac.player");

        private sealed class FakeInteractableEntity : Entity
        {
            private readonly string _kind;

            public FakeInteractableEntity(Id id, Id mapId, string kind) : base(id, mapId)
            {
                _kind = kind;
            }

            public override string Kind => _kind;
        }

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        private static (WorldSim World, WorldUnitAccess Units, InteractionTargetRegistry Registry, PlayerUnit Player) NewFixture()
        {
            var bus = NewBus();
            var world = new WorldSim(bus);
            var units = new WorldUnitAccess(world);
            var registry = new InteractionTargetRegistry(world, units);

            var player = new PlayerUnit(new Id("unit.player"), MapId, FactionId, new Id("archetype.test")) { Position = new Vec2(0, 0) };
            world.AddEntity(player);

            return (world, units, registry, player);
        }

        [Fact]
        public void TryFindNearest_ReturnsClosestCandidate_AmongThreeKinds()
        {
            var (world, _, registry, player) = NewFixture();

            world.AddEntity(new FakeInteractableEntity(new Id("gobj.inst_1"), MapId, EntityKinds.Gobj) { Position = new Vec2(5, 0) });
            world.AddEntity(new FakeInteractableEntity(new Id("creature.inst_1"), MapId, EntityKinds.Creature) { Position = new Vec2(3, 0) });
            world.AddEntity(new FakeInteractableEntity(new Id("loot.inst_1"), MapId, EntityKinds.Loot) { Position = new Vec2(1, 0) });

            var found = registry.TryFindNearest(player.EntityId, null, out var target);

            Assert.True(found);
            Assert.Equal(new Id("loot.inst_1"), target.EntityId);
            Assert.Equal(InteractionTargetKind.Loot, target.Kind);
            Assert.Equal(1.0, target.Distance);
        }

        [Fact]
        public void TryFindNearest_FallsBackToNextClosest_AfterNearestIsDestroyed()
        {
            var (world, _, registry, player) = NewFixture();

            world.AddEntity(new FakeInteractableEntity(new Id("gobj.inst_1"), MapId, EntityKinds.Gobj) { Position = new Vec2(5, 0) });
            world.AddEntity(new FakeInteractableEntity(new Id("loot.inst_1"), MapId, EntityKinds.Loot) { Position = new Vec2(1, 0) });

            Assert.True(registry.TryFindNearest(player.EntityId, null, out var before));
            Assert.Equal(InteractionTargetKind.Loot, before.Kind);

            // 模拟"掉落物被拾取"：标记待销毁 + 推进一次 tick 完成生命周期清理（同 LootHost.DestroyDropped
            // 的既有做法——本类不另开登记表，直接查 IWorldSim 现场结果，见类型判断记录）。
            world.MarkForDestruction(new Id("loot.inst_1"));
            world.Tick(SimStep.Continuous(0.01));

            Assert.True(registry.TryFindNearest(player.EntityId, null, out var after));
            Assert.Equal(new Id("gobj.inst_1"), after.EntityId);
            Assert.Equal(InteractionTargetKind.GameObject, after.Kind);
        }

        [Fact]
        public void TryFindNearest_MaxRange_ExcludesFartherCandidates()
        {
            var (world, _, registry, player) = NewFixture();
            world.AddEntity(new FakeInteractableEntity(new Id("loot.inst_1"), MapId, EntityKinds.Loot) { Position = new Vec2(10, 0) });

            Assert.False(registry.TryFindNearest(player.EntityId, 5.0, out _));
            Assert.True(registry.TryFindNearest(player.EntityId, 10.0, out var target));
            Assert.Equal(new Id("loot.inst_1"), target.EntityId);
        }

        [Fact]
        public void TryFindNearest_IgnoresCandidatesOnOtherMaps()
        {
            var (world, _, registry, player) = NewFixture();
            world.AddEntity(new FakeInteractableEntity(new Id("loot.inst_other_map"), OtherMapId, EntityKinds.Loot) { Position = new Vec2(0, 0) });

            Assert.False(registry.TryFindNearest(player.EntityId, null, out _));
        }

        [Fact]
        public void TryFindNearest_UnknownUnit_ReturnsFalse()
        {
            var (_, _, registry, _) = NewFixture();

            Assert.False(registry.TryFindNearest(new Id("unit.never_spawned"), null, out _));
        }

        [Fact]
        public void TryFindNearest_NoCandidates_ReturnsFalse()
        {
            var (_, _, registry, player) = NewFixture();

            Assert.False(registry.TryFindNearest(player.EntityId, null, out _));
        }

        // -----------------------------------------------------------------
        // ADR-0065（消费方反馈第七批第 1 条根治）：死亡生物不是"最近可交互目标"的候选——存活判定用
        // IUnitAccess.Exists×IsAlive（与 ADR-0061 player.alive/target.alive 同一权威口径），因此这三
        // 例必须用真正的 CreatureUnit（经 WorldUnitAccess 才能读到 Alive 字段），不能像上面几例那样
        // 用只携带 Kind 字符串的 FakeInteractableEntity——那个假实现不是 Unit 子类，IUnitAccess.Exists
        // 对它恒返回 false，测不出"死亡"与"存活但不是 Unit"两种情况的区别。
        // -----------------------------------------------------------------

        private static CreatureUnit NewCreature(string id, Vec2 position, bool alive = true) =>
            new CreatureUnit(new Id(id), MapId, new Id("fac.hostile"), new Id("creature.template.test"))
            {
                Position = position,
                Alive = alive,
            };

        [Fact]
        public void TryFindNearest_DeadCreature_IsNotACandidate()
        {
            var (world, _, registry, player) = NewFixture();
            world.AddEntity(NewCreature("creature.inst_dead", new Vec2(1, 0), alive: false));

            Assert.False(registry.TryFindNearest(player.EntityId, null, out _));
        }

        [Fact]
        public void TryFindNearest_DeadCreatureCloserThanLivingCreature_ReturnsLivingCreature()
        {
            var (world, _, registry, player) = NewFixture();
            var dead = NewCreature("creature.inst_dead", new Vec2(1, 0), alive: false);
            var living = NewCreature("creature.inst_alive", new Vec2(5, 0));
            world.AddEntity(dead);
            world.AddEntity(living);

            var found = registry.TryFindNearest(player.EntityId, null, out var target);

            Assert.True(found);
            Assert.Equal(living.EntityId, target.EntityId);
            Assert.Equal(InteractionTargetKind.Creature, target.Kind);
        }

        [Fact]
        public void TryFindNearest_AliveCreature_IsStillACandidate()
        {
            var (world, _, registry, player) = NewFixture();
            var creature = NewCreature("creature.inst_alive", new Vec2(2, 0));
            world.AddEntity(creature);

            var found = registry.TryFindNearest(player.EntityId, null, out var target);

            Assert.True(found);
            Assert.Equal(creature.EntityId, target.EntityId);
            Assert.Equal(InteractionTargetKind.Creature, target.Kind);
            Assert.Equal(2.0, target.Distance);
        }

        [Fact]
        public void TryFindNearest_DeadCreatureSameCoordinateAsItsOwnLoot_ReturnsLoot()
        {
            // 复现消费方反馈第七批第 1 条：尸体与它自己的战利品同坐标、等距时按 EntityId 序数取小，
            // creature.* 恒小于 loot.*，修复前恒返回尸体。
            var (world, _, registry, player) = NewFixture();
            world.AddEntity(NewCreature("creature.inst_dead", new Vec2(1, 0), alive: false));
            world.AddEntity(new FakeInteractableEntity(new Id("loot.inst_1"), MapId, EntityKinds.Loot) { Position = new Vec2(1, 0) });

            var found = registry.TryFindNearest(player.EntityId, null, out var target);

            Assert.True(found);
            Assert.Equal(new Id("loot.inst_1"), target.EntityId);
            Assert.Equal(InteractionTargetKind.Loot, target.Kind);
        }

        // -----------------------------------------------------------------
        // ADR-0069（消费方反馈——游戏接入方第十四批）：生物候选新增"是否有可交互内容"核对，经构造
        // 重载注入 ICreatureInteractionHost。本组用例只测注册表这一层的过滤接线本身（是否正确调用
        // 注入的查询、未注入时是否保持旧行为），与 Interact/HasInteractableContent 的判定一致性由
        // Tests.Carriers.Creature.CreatureInteractionHostHasInteractableContentTests 覆盖，生产装配
        // 贯通验收见 Tests.Gameplay.Assembly.GameplayAssemblyNearestInteractableCreatureContentTests。
        // -----------------------------------------------------------------

        /// <summary>最小可控的 <see cref="ICreatureInteractionHost"/> 测试替身：按显式登记表回答
        /// <see cref="HasInteractableContent"/>，未登记的 id 恒返回 <c>false</c>（本组用例不需要
        /// <see cref="Interact"/> 产生任何真实效果）。</summary>
        private sealed class FakeCreatureInteractionHost : ICreatureInteractionHost
        {
            private readonly System.Collections.Generic.HashSet<Id> _withContent;

            public FakeCreatureInteractionHost(params Id[] withContent) => _withContent = new System.Collections.Generic.HashSet<Id>(withContent);

            public InteractResult Interact(Id unitId, Id creatureInstanceId) =>
                new InteractResult(false, InteractOutcome.Unknown);

            public bool HasInteractableContent(Id creatureInstanceId) => _withContent.Contains(creatureInstanceId);
        }

        [Fact]
        public void TryFindNearest_CreatureWithoutInteractableContent_IsNotACandidate_EvenIfCloser()
        {
            var bus = NewBus();
            var world = new WorldSim(bus);
            var units = new WorldUnitAccess(world);
            var contentId = new Id("creature.inst_with_content");
            var contentlessId = new Id("creature.inst_without_content");
            var host = new FakeCreatureInteractionHost(contentId);
            var registry = new InteractionTargetRegistry(world, units, host);

            var player = new PlayerUnit(new Id("unit.player"), MapId, FactionId, new Id("archetype.test")) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            world.AddEntity(NewCreature(contentlessId.Value, new Vec2(1, 0))); // 更近，但没有可交互内容。
            world.AddEntity(NewCreature(contentId.Value, new Vec2(5, 0))); // 更远，有可交互内容。

            var found = registry.TryFindNearest(player.EntityId, null, out var target);

            Assert.True(found);
            Assert.Equal(contentId, target.EntityId);
            Assert.Equal(InteractionTargetKind.Creature, target.Kind);
        }

        [Fact]
        public void TryFindNearest_CreatureWithInteractableContent_IsStillACandidate()
        {
            var bus = NewBus();
            var world = new WorldSim(bus);
            var units = new WorldUnitAccess(world);
            var contentId = new Id("creature.inst_with_content");
            var host = new FakeCreatureInteractionHost(contentId);
            var registry = new InteractionTargetRegistry(world, units, host);

            var player = new PlayerUnit(new Id("unit.player"), MapId, FactionId, new Id("archetype.test")) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            world.AddEntity(NewCreature(contentId.Value, new Vec2(2, 0)));

            var found = registry.TryFindNearest(player.EntityId, null, out var target);

            Assert.True(found);
            Assert.Equal(contentId, target.EntityId);
        }

        [Fact]
        public void TryFindNearest_NoCreatureInteractionHostInjected_KeepsPriorBehavior_AllAliveCreaturesAreCandidates()
        {
            // 未注入 ICreatureInteractionHost（旧的两参构造函数）：行为与本次改动之前逐位一致——存活
            // 生物即候选，不核对内容（ABI 兼容，既有调用方不必跟改）。
            var (world, _, registry, player) = NewFixture();
            world.AddEntity(NewCreature("creature.inst_alive", new Vec2(3, 0)));

            var found = registry.TryFindNearest(player.EntityId, null, out var target);

            Assert.True(found);
            Assert.Equal(new Id("creature.inst_alive"), target.EntityId);
        }
    }
}
