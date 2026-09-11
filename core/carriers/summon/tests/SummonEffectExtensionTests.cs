using System;
using Core.Carriers.Creature;
using Core.Carriers.Summon;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Tests.Carriers.Creature;
using Xunit;

namespace Tests.Carriers.Summon
{
    public class SummonEffectExtensionTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id CasterId = new Id("unit.caster_test");
        private static readonly Id CasterFaction = new Id("fac.test_player");
        private static readonly Id ArchetypeId = new Id("arch.class.sample");
        private static readonly Id BasicTemplateId = new Id("creature.sample_basic");
        private static readonly Id SkillId = new Id("skill.sample_summon");
        private static readonly Id SchoolId = new Id("school.sample_arcane");

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public SummonHost SummonHost = null!;
            public SummonEffectExtension Extension = null!;
        }

        private static Fixture Build()
        {
            var bus = CreatureTestSupport.CreateBus();
            var registry = CreatureTestSupport.MakeRegistry(bus);
            var world = new WorldSim(bus);
            var units = new WorldUnitAccess(world);
            var stats = CreatureTestSupport.MakeStatHost(registry, bus);
            var powers = CreatureTestSupport.MakePowerHost(registry, bus, stats);
            var progression = CreatureTestSupport.MakeProgressionHost(registry, bus, stats);
            AiRegistrar registrar = (unitId, profileId, spawnPoint, rotationId) => { };
            var factory = new CreatureFactory(registry, world, bus, stats, powers, progression, units, registrar);

            var caster = new PlayerUnit(CasterId, MapId, CasterFaction, ArchetypeId) { Position = new Vec2(2, 3), Facing = 0.0 };
            world.AddEntity(caster);

            var summonHost = new SummonHost(factory, world, units, bus);
            var extension = new SummonEffectExtension(summonHost, units);

            return new Fixture { World = world, Units = units, SummonHost = summonHost, Extension = extension };
        }

        private static EffectContext MakeContext(EffectKind kind, JsonObject @params) =>
            new EffectContext(CasterId, CasterId, SkillId, kind, SchoolId, baseValue: 0, coefficient: 0, @params: @params);

        [Fact]
        public void TryHandle_WrongEffectKind_ReturnsFalse()
        {
            var f = Build();
            var context = MakeContext(EffectKind.Heal, new JsonObjectBuilder().Build());

            var handled = f.Extension.TryHandle(context, out _);

            Assert.False(handled);
        }

        [Fact]
        public void TryHandle_MissingCreatureTemplateParam_ReturnsFalse()
        {
            var f = Build();
            var context = MakeContext(EffectKind.Summon, new JsonObjectBuilder().Build());

            var handled = f.Extension.TryHandle(context, out _);

            Assert.False(handled);
        }

        [Fact]
        public void TryHandle_CreatesSummon_WithDefaultPositionOffsetFromCaster()
        {
            var f = Build();
            var @params = new JsonObjectBuilder()
                .Add("creature_template", new JsonString(BasicTemplateId.Value))
                .Build();
            var context = MakeContext(EffectKind.Summon, @params);

            var handled = f.Extension.TryHandle(context, out var result);

            Assert.True(handled);
            var summonId = f.SummonHost.GetSummons(CasterId)[0];
            var unit = (CreatureUnit)f.World.GetEntity(summonId)!;
            // 施法者朝向 0（正 x 方向），默认偏移 1：(2,3) + (1,0) = (3,3)。
            Assert.Equal(3.0, unit.Position.X, 6);
            Assert.Equal(3.0, unit.Position.Y, 6);
            Assert.False(result.Immune);
        }

        [Fact]
        public void TryHandle_CreatesSummon_WithExplicitPosition()
        {
            var f = Build();
            var position = new JsonObjectBuilder()
                .Add("x", new JsonNumber(10))
                .Add("y", new JsonNumber(20))
                .Build();
            var @params = new JsonObjectBuilder()
                .Add("creature_template", new JsonString(BasicTemplateId.Value))
                .Add("position", position)
                .Build();
            var context = MakeContext(EffectKind.Summon, @params);

            f.Extension.TryHandle(context, out _);

            var summonId = f.SummonHost.GetSummons(CasterId)[0];
            var unit = (CreatureUnit)f.World.GetEntity(summonId)!;
            Assert.Equal(10.0, unit.Position.X, 6);
            Assert.Equal(20.0, unit.Position.Y, 6);
        }

        [Fact]
        public void TryHandle_WithDuration_RegistersFiniteRemainingLifetime()
        {
            var f = Build();
            var @params = new JsonObjectBuilder()
                .Add("creature_template", new JsonString(BasicTemplateId.Value))
                .Add("duration", new JsonNumber(30))
                .Build();
            var context = MakeContext(EffectKind.Summon, @params);

            f.Extension.TryHandle(context, out _);

            var summonId = f.SummonHost.GetSummons(CasterId)[0];
            // 到期推进：剩余 30，推进 40 后应视为到期（>0 之前、<=0 之后）。
            Assert.False(f.SummonHost.AdvanceAndCheckExpired(summonId, 10));
            Assert.True(f.SummonHost.AdvanceAndCheckExpired(summonId, 30));
        }

        [Fact]
        public void TryHandle_ResultIsNotHeal_AndNotImmune()
        {
            var f = Build();
            var @params = new JsonObjectBuilder()
                .Add("creature_template", new JsonString(BasicTemplateId.Value))
                .Build();
            var context = MakeContext(EffectKind.Summon, @params);

            f.Extension.TryHandle(context, out var result);

            Assert.False(result.IsHeal);
            Assert.Equal(HitResult.Hit, result.Hit);
        }
    }
}
