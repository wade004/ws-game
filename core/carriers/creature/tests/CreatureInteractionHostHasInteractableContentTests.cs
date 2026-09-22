using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Carriers.Creature
{
    /// <summary>
    /// ADR-0069（消费方反馈——游戏接入方第十四批）：<see cref="ICreatureInteractionHost.HasInteractableContent"/>
    /// 必须与 <see cref="CreatureInteractionHost.Interact"/> 共用同一份内容判定逻辑（<c>HasContent</c>
    /// 私有方法，见该类型判断记录），本文件逐条核对两者不会出现"查询说能交互、分流说不能"的分歧。
    /// </summary>
    public class CreatureInteractionHostHasInteractableContentTests
    {
        private static readonly Id MapId = new Id("map.test");

        // creature.sample_talkative 配置了 gossip_menu_ref（见 CreatureTestSupport.TemplateRows
        // 判断记录），creature.sample_basic 是既有的、未配置 gossip_menu_ref 的模板，直接复用为
        // "无内容"分支，不新增重复模板。
        private static readonly Id TalkativeTemplateId = new Id("creature.sample_talkative");
        private static readonly Id SilentTemplateId = new Id("creature.sample_basic");

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public CreatureFactory Factory = null!;
            public CreatureInteractionHost Host = null!;
            public PlayerUnit Player = null!;
            public List<(Id UnitId, Id CreatureInstanceId, Id DialogRef)> OpenedDialogs = null!;
        }

        private static Fixture Build(bool injectGossipOpener)
        {
            var bus = CreatureTestSupport.CreateBus();
            var registry = CreatureTestSupport.MakeRegistry(bus);
            var world = new WorldSim(bus);
            var units = new WorldUnitAccess(world);
            var stats = CreatureTestSupport.MakeStatHost(registry, bus);
            var powers = CreatureTestSupport.MakePowerHost(registry, bus, stats);
            var progression = CreatureTestSupport.MakeProgressionHost(registry, bus, stats);

            AiRegistrar registrar = (unitId, profileId, spawnPoint, rotationId) => { };
            var factory = new CreatureFactory(registry, world, bus, stats, powers, progression, units, registrar, null);

            var opened = new List<(Id, Id, Id)>();
            var options = new CreatureInteractOptions();
            if (injectGossipOpener)
            {
                options.GossipOpener = (unitId, creatureInstanceId, dialogRef) => opened.Add((unitId, creatureInstanceId, dialogRef));
            }

            var host = new CreatureInteractionHost(world, units, factory, options);

            var player = new PlayerUnit(new Id("unit.player"), MapId, new Id("fac.player"), new Id("archetype.test"))
            {
                Position = Vec2.Zero,
            };
            world.AddEntity(player);

            return new Fixture
            {
                World = world,
                Units = units,
                Factory = factory,
                Host = host,
                Player = player,
                OpenedDialogs = opened,
            };
        }

        [Fact]
        public void HasInteractableContent_CreatureWithGossipMenuRef_ReturnsTrue_ConsistentWithInteractOpeningDialog()
        {
            var f = Build(injectGossipOpener: true);
            var id = f.Factory.Spawn(TalkativeTemplateId, MapId, Vec2.Zero, 0);
            var query = (ICreatureInteractionHost)f.Host;

            Assert.True(query.HasInteractableContent(id));

            var result = f.Host.Interact(f.Player.EntityId, id);

            Assert.True(result.Success);
            Assert.Equal(InteractOutcome.Dialog, result.Outcome);
            Assert.Single(f.OpenedDialogs);
        }

        [Fact]
        public void HasInteractableContent_CreatureWithoutGossipMenuRef_ReturnsFalse_ConsistentWithInteractReturningNoAction()
        {
            var f = Build(injectGossipOpener: true);
            var id = f.Factory.Spawn(SilentTemplateId, MapId, Vec2.Zero, 0);
            var query = (ICreatureInteractionHost)f.Host;

            Assert.False(query.HasInteractableContent(id));

            var result = f.Host.Interact(f.Player.EntityId, id);

            Assert.Equal(InteractOutcome.NoAction, result.Outcome);
            Assert.Empty(f.OpenedDialogs);
        }

        [Fact]
        public void HasInteractableContent_GossipMenuRefConfiguredButOpenerNotInjected_ReturnsFalse_ConsistentWithInteractReturningNoAction()
        {
            var f = Build(injectGossipOpener: false);
            var id = f.Factory.Spawn(TalkativeTemplateId, MapId, Vec2.Zero, 0);
            var query = (ICreatureInteractionHost)f.Host;

            Assert.False(query.HasInteractableContent(id));

            var result = f.Host.Interact(f.Player.EntityId, id);

            Assert.False(result.Success);
            Assert.Equal(InteractOutcome.NoAction, result.Outcome);
        }

        [Fact]
        public void HasInteractableContent_DeadCreatureWithGossipMenuRef_StillReturnsTrue_IgnoresAliveState()
        {
            // ADR-0069：本查询不判存活（存活判定留给 InteractionTargetRegistry 按 ADR-0065/0067
            // 口径单独核对）——即便目标已死亡，只要内容配置存在，查询仍应为真；与 Interact 因
            // ADR-0067 的存活核对在内容判定之前就短路返回 TargetDead（不属于"无内容类"结果）不矛盾，
            // 两者服务不同的问题（"有没有内容" vs "现在发起交互会不会成功"）。
            var f = Build(injectGossipOpener: true);
            var id = f.Factory.Spawn(TalkativeTemplateId, MapId, Vec2.Zero, 0);
            f.Units.SetAlive(id, false);
            var query = (ICreatureInteractionHost)f.Host;

            Assert.True(query.HasInteractableContent(id));

            var result = f.Host.Interact(f.Player.EntityId, id);
            Assert.Equal(InteractOutcome.TargetDead, result.Outcome);
        }

        [Fact]
        public void HasInteractableContent_UnregisteredCreatureId_ReturnsFalse()
        {
            var f = Build(injectGossipOpener: true);
            var query = (ICreatureInteractionHost)f.Host;

            Assert.False(query.HasInteractableContent(new Id("creature.never_spawned")));
        }

        [Fact]
        public void ICreatureInteractionHost_DefaultInterfaceMember_FallsBackToTrue_WhenNotOverridden()
        {
            // ABI 只加不改：默认接口成员服务未升级的既有 ICreatureInteractionHost 实现方，降级口径
            // 恒返回 true（"有内容"）——保持它们此前从不参与"是否有内容"过滤的既有行为，不会被新查询
            // 误伤（同 IEncounterHost.TryStart 判断记录同一类降级口径）。
            ICreatureInteractionHost legacyImpl = new LegacyHostWithoutOverride();

            Assert.True(legacyImpl.HasInteractableContent(new Id("creature.whatever")));
        }

        private sealed class LegacyHostWithoutOverride : ICreatureInteractionHost
        {
            public InteractResult Interact(Id unitId, Id creatureInstanceId) =>
                new InteractResult(false, InteractOutcome.Unknown);
        }
    }
}
