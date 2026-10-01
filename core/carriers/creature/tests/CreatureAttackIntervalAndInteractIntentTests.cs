using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Carriers.Creature
{
    /// <summary>
    /// T-L12（测试覆盖剩余项 2026-10-01）：<see cref="CreatureAttackIntervalProvider"/>（无武器时普通攻击间隔的
    /// 生物模板回退）与 <see cref="CreatureInteractIntentTickHandler"/>（<c>interact</c> 意图中
    /// <c>creature_instance_id</c> 形状的分流）在 carriers 内的直接用例。此前只有 sim / gameplay 层的端到端
    /// 用例间接经过它们。
    /// </summary>
    public class CreatureAttackIntervalAndInteractIntentTests
    {
        private static readonly Id MapId = new Id("map.cai_test");
        private static readonly Id Faction = new Id("fac.test_monster");

        private static readonly Id TemplateWithInterval = new Id("creature.cai_fast");
        private static readonly Id TemplateWithoutInterval = new Id("creature.cai_plain");
        private const double FastInterval = 1.4;

        private static string Rows() =>
            "[" +
            "{\"id\": \"creature.cai_fast\", \"name_key\": \"l10n.creature.cai_fast.name\", " +
            "\"level\": 1, \"tier\": \"creature.tier.normal\", " +
            "\"base_stats\": {\"stat.power\": 10, \"stat.max_health\": 100}, " +
            "\"faction_id\": \"fac.test_monster\", \"display_ref\": \"display.cai_fast\", " +
            "\"attack_interval\": 1.4}," +
            "{\"id\": \"creature.cai_plain\", \"name_key\": \"l10n.creature.cai_plain.name\", " +
            "\"level\": 1, \"tier\": \"creature.tier.normal\", " +
            "\"base_stats\": {\"stat.power\": 10, \"stat.max_health\": 100}, " +
            "\"faction_id\": \"fac.test_monster\", \"display_ref\": \"display.cai_plain\"}" +
            "]";

        // ------------------------------ CreatureAttackIntervalProvider ------------------------------

        private sealed class ThrowingTemplateQuery : ICreatureTemplateQuery
        {
            public CreatureTemplate Get(Id templateId) => throw new InvalidOperationException("boom");
            public bool HasFlag(Id templateId, NpcFlag flag) => false;
        }

        private static (WorldSim World, CreatureAttackIntervalProvider Provider) BuildProvider()
        {
            var bus = CreatureTestSupport.CreateBus();
            var registry = CreatureTestSupport.MakeRegistry(bus, Rows());
            var world = new WorldSim(bus);
            var provider = new CreatureAttackIntervalProvider(world, new RegistryCreatureTemplateQuery(registry));
            return (world, provider);
        }

        [Fact]
        public void AttackInterval_Constructor_RejectsNullDependencies()
        {
            var bus = CreatureTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var query = new RegistryCreatureTemplateQuery(CreatureTestSupport.MakeRegistry(bus, Rows()));

            Assert.Throws<ArgumentNullException>(() => new CreatureAttackIntervalProvider(null!, query));
            Assert.Throws<ArgumentNullException>(() => new CreatureAttackIntervalProvider(world, null!));
        }

        [Fact]
        public void AttackInterval_CreatureWithDeclaredInterval_ReturnsTemplateValue()
        {
            var (world, provider) = BuildProvider();
            var wolf = new CreatureUnit(new Id("unit.cai_wolf"), MapId, Faction, TemplateWithInterval);
            world.AddEntity(wolf);

            Assert.Equal(FastInterval, provider.GetAttackIntervalSeconds(wolf.EntityId));
        }

        [Fact]
        public void AttackInterval_TemplateWithoutInterval_ReturnsNull()
        {
            var (world, provider) = BuildProvider();
            var plain = new CreatureUnit(new Id("unit.cai_plain"), MapId, Faction, TemplateWithoutInterval);
            world.AddEntity(plain);

            Assert.Null(provider.GetAttackIntervalSeconds(plain.EntityId));
        }

        [Fact]
        public void AttackInterval_NonCreatureOrUnknownEntity_ReturnsNull()
        {
            var (world, provider) = BuildProvider();
            var player = new PlayerUnit(new Id("unit.cai_player"), MapId, Faction, new Id("archetype.test"));
            world.AddEntity(player);

            Assert.Null(provider.GetAttackIntervalSeconds(player.EntityId));
            Assert.Null(provider.GetAttackIntervalSeconds(new Id("unit.cai_nobody")));
        }

        [Fact]
        public void AttackInterval_CreatureWithUnregisteredTemplate_ReturnsNull_InsteadOfThrowing()
        {
            var (world, provider) = BuildProvider();
            var orphan = new CreatureUnit(new Id("unit.cai_orphan"), MapId, Faction, new Id("creature.cai_not_registered"));
            world.AddEntity(orphan);

            var ex = Record.Exception(() => provider.GetAttackIntervalSeconds(orphan.EntityId));

            Assert.Null(ex);
            Assert.Null(provider.GetAttackIntervalSeconds(orphan.EntityId));
        }

        [Fact]
        public void AttackInterval_OnlyArgumentExceptionIsSwallowed_OtherFailuresPropagate()
        {
            var bus = CreatureTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var provider = new CreatureAttackIntervalProvider(world, new ThrowingTemplateQuery());
            var wolf = new CreatureUnit(new Id("unit.cai_wolf2"), MapId, Faction, TemplateWithInterval);
            world.AddEntity(wolf);

            Assert.Throws<InvalidOperationException>(() => provider.GetAttackIntervalSeconds(wolf.EntityId));
        }

        // ------------------------------ CreatureInteractIntentTickHandler ------------------------------

        private sealed class RecordingInteractionHost : ICreatureInteractionHost
        {
            public readonly List<(Id Unit, Id Creature)> Calls = new List<(Id, Id)>();
            public InteractResult Result = new InteractResult(true, InteractOutcome.Dialog);

            public InteractResult Interact(Id unitId, Id creatureInstanceId)
            {
                Calls.Add((unitId, creatureInstanceId));
                return Result;
            }
        }

        private static (WorldSim World, RecordingInteractionHost Host, InMemoryCreatureDiagnostics Diagnostics, Id Actor) BuildHandler()
        {
            var bus = CreatureTestSupport.CreateBus();
            var world = new WorldSim(bus);
            var host = new RecordingInteractionHost();
            var diagnostics = new InMemoryCreatureDiagnostics();
            world.RegisterPhaseHandler(TickPhase.TriggerEvaluation, new CreatureInteractIntentTickHandler(host, diagnostics));
            var actor = new Id("unit.cai_actor");
            world.AddEntity(new PlayerUnit(actor, MapId, new Id("fac.test_player"), new Id("archetype.test")));
            return (world, host, diagnostics, actor);
        }

        private static Intent Interact(Id actor, string key, JsonValue value) =>
            new Intent(actor, "interact", new JsonObjectBuilder().Add(key, value).Build());

        [Fact]
        public void InteractHandler_Constructor_RejectsNullHost()
        {
            Assert.Throws<ArgumentNullException>(() => new CreatureInteractIntentTickHandler(null!));
        }

        [Fact]
        public void InteractHandler_CreatureInstanceIdIntent_CallsHostOnce_WithActorAndCreatureIds_NoWarningOnSuccess()
        {
            var (world, host, diagnostics, actor) = BuildHandler();
            var creature = new Id("creature.inst_cai_1");

            world.SubmitIntent(Interact(actor, "creature_instance_id", new JsonString(creature.Value)));
            world.Tick(SimStep.Continuous(0.1));

            var call = Assert.Single(host.Calls);
            Assert.Equal(actor, call.Unit);
            Assert.Equal(creature, call.Creature);
            Assert.Empty(diagnostics.Warnings);
        }

        [Theory]
        [InlineData(InteractOutcome.Locked)]
        [InlineData(InteractOutcome.NoAction)]
        [InlineData(InteractOutcome.Unknown)]
        [InlineData(InteractOutcome.TargetDead)]
        [InlineData(InteractOutcome.ActorDead)]
        public void InteractHandler_FailedResult_RecordsOneWarningNamingTheOutcome_AndDoesNotThrow(InteractOutcome outcome)
        {
            var (world, host, diagnostics, actor) = BuildHandler();
            host.Result = new InteractResult(false, outcome);

            world.SubmitIntent(Interact(actor, "creature_instance_id", new JsonString("creature.inst_cai_2")));
            var ex = Record.Exception(() => world.Tick(SimStep.Continuous(0.1)));

            Assert.Null(ex);
            var warning = Assert.Single(diagnostics.Warnings);
            Assert.Contains(outcome.ToString(), warning);
            Assert.Contains("creature.inst_cai_2", warning);
        }

        [Fact]
        public void InteractHandler_GobjShapedInteract_IsSilentlyIgnored()
        {
            var (world, host, diagnostics, actor) = BuildHandler();

            world.SubmitIntent(Interact(actor, "gobj_instance_id", new JsonString("gobj.inst_cai_1")));
            world.Tick(SimStep.Continuous(0.1));

            Assert.Empty(host.Calls);
            Assert.Empty(diagnostics.Warnings);
        }

        [Fact]
        public void InteractHandler_NonStringCreatureInstanceId_IsSilentlyIgnored()
        {
            var (world, host, diagnostics, actor) = BuildHandler();

            world.SubmitIntent(Interact(actor, "creature_instance_id", new JsonNumber(7)));
            world.Tick(SimStep.Continuous(0.1));

            Assert.Empty(host.Calls);
            Assert.Empty(diagnostics.Warnings);
        }

        [Fact]
        public void InteractHandler_OtherIntentKinds_AreIgnored()
        {
            var (world, host, _, actor) = BuildHandler();
            var args = new JsonObjectBuilder().Add("creature_instance_id", new JsonString("creature.inst_cai_3")).Build();

            world.SubmitIntent(new Intent(actor, "attack", args));
            world.Tick(SimStep.Continuous(0.1));

            Assert.Empty(host.Calls);
        }

        [Fact]
        public void InteractHandler_MultipleIntentsInOneTick_AreHandledInSubmissionOrder()
        {
            var (world, host, _, actor) = BuildHandler();

            world.SubmitIntent(Interact(actor, "creature_instance_id", new JsonString("creature.inst_cai_b")));
            world.SubmitIntent(Interact(actor, "creature_instance_id", new JsonString("creature.inst_cai_a")));
            world.Tick(SimStep.Continuous(0.1));

            Assert.Equal(
                new[] { new Id("creature.inst_cai_b"), new Id("creature.inst_cai_a") },
                host.Calls.ConvertAll(c => c.Creature));
        }
    }
}
