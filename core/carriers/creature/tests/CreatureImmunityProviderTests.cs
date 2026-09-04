using Core.Carriers.Creature;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Creature
{
    /// <summary>
    /// 阶段 3 整理"事项三"：<see cref="CreatureImmunityProvider"/> 对 <see cref="CreatureUnit.Immunities"/>
    /// 三种格式（学派 id / <c>effect.&lt;kind&gt;</c> / <c>control.&lt;flag&gt;</c>）以及"tier
    /// control_immune → 全部控制免疫"标记的解析，直接构造 <see cref="CreatureUnit"/> 验证，不经过
    /// <see cref="Core.Carriers.Creature.CreatureFactory"/>/DataRegistry（那条路径由
    /// <c>CreatureFactoryTests.Spawn_WritesImmunitiesFromTemplateAndTierControlImmune</c> 覆盖）。
    /// </summary>
    public class CreatureImmunityProviderTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id FactionId = new Id("fac.test_monster");
        private static readonly Id TemplateId = new Id("creature.sample_basic");

        private static (WorldSim world, CreatureImmunityProvider provider) NewWorld()
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            return (world, new CreatureImmunityProvider(world));
        }

        private static CreatureUnit AddUnit(WorldSim world, string entityIdSuffix, params string[] immunities)
        {
            var unit = new CreatureUnit(new Id($"creature.inst_{entityIdSuffix}"), MapId, FactionId, TemplateId);
            foreach (var immunity in immunities)
            {
                unit.Immunities.Add(new Id(immunity));
            }
            world.AddEntity(unit);
            return unit;
        }

        [Fact]
        public void IsImmune_SchoolEntry_BlocksAnyEffectKindOfThatSchool()
        {
            var (world, provider) = NewWorld();
            var unit = AddUnit(world, "1", "school.fire");

            Assert.True(provider.IsImmune(unit.EntityId, new Id("school.fire"), EffectKind.SchoolDamage));
            Assert.True(provider.IsImmune(unit.EntityId, new Id("school.fire"), EffectKind.Heal));
            Assert.False(provider.IsImmune(unit.EntityId, new Id("school.frost"), EffectKind.SchoolDamage));
        }

        [Fact]
        public void IsImmune_EffectKindEntry_BlocksThatKindRegardlessOfSchool()
        {
            var (world, provider) = NewWorld();
            var unit = AddUnit(world, "2", "effect.heal");

            Assert.True(provider.IsImmune(unit.EntityId, new Id("school.fire"), EffectKind.Heal));
            Assert.True(provider.IsImmune(unit.EntityId, new Id("school.frost"), EffectKind.Heal));
            Assert.False(provider.IsImmune(unit.EntityId, new Id("school.fire"), EffectKind.SchoolDamage));
        }

        [Fact]
        public void GetControlImmunity_ControlFlagEntry_ReturnsOnlyThatFlag()
        {
            var (world, provider) = NewWorld();
            var unit = AddUnit(world, "3", "control.no_move");

            Assert.Equal(ControlFlags.NoMove, provider.GetControlImmunity(unit.EntityId));
        }

        [Fact]
        public void GetControlImmunity_TierControlImmuneMarker_ReturnsAllFlags()
        {
            var (world, provider) = NewWorld();
            var unit = AddUnit(world, "4", "immunity.control_immune");

            var expected = ControlFlags.NoMove | ControlFlags.NoCast | ControlFlags.NoAttack | ControlFlags.NoInteract;
            Assert.Equal(expected, provider.GetControlImmunity(unit.EntityId));

            // 标记只影响控制免疫，不参与学派/效果免疫判定。
            Assert.False(provider.IsImmune(unit.EntityId, new Id("school.fire"), EffectKind.SchoolDamage));
        }

        [Fact]
        public void IsImmune_And_GetControlImmunity_NonCreatureOrMissingUnit_ReturnFalseAndNone()
        {
            var (_, provider) = NewWorld();
            var missingId = new Id("creature.inst_missing");

            Assert.False(provider.IsImmune(missingId, new Id("school.fire"), EffectKind.SchoolDamage));
            Assert.Equal(ControlFlags.None, provider.GetControlImmunity(missingId));
        }
    }
}
