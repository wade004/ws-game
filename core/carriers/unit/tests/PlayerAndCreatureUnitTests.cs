using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Unit
{
    public class PlayerAndCreatureUnitTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id FactionId = new Id("fac.player");

        [Fact]
        public void PlayerUnit_Kind_IsPlayer()
        {
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, new Id("arch.class.sample"));

            Assert.Equal("player", player.Kind);
            Assert.Equal(new Id("arch.class.sample"), player.ArchetypeId);
            Assert.Empty(player.Talents);
            Assert.Empty(player.QuestLog);
        }

        [Fact]
        public void PlayerUnit_InheritsUnitDefaults()
        {
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, new Id("arch.class.sample"));

            Assert.True(player.Alive);
            Assert.Equal(UnitCombatState.Out, player.CombatState);
            Assert.Equal(1, player.Level);
            Assert.Equal(MoveMode.Idle, player.MovementState.Mode);
        }

        [Fact]
        public void CreatureUnit_Kind_IsCreature_And_TemplateIdIsRequired()
        {
            var templateId = new Id("creature.grey_wolf");
            var creature = new CreatureUnit(new Id("unit.wolf"), MapId, FactionId, templateId);

            Assert.Equal("creature", creature.Kind);
            Assert.Equal(templateId, creature.TemplateId);
            Assert.Equal(BehaviorState.Idle, creature.AiState);
            Assert.Empty(creature.Immunities);
            Assert.Empty(creature.NpcFlags);
            Assert.Null(creature.OwnerId);
        }

        [Fact]
        public void CreatureUnit_OwnerId_CanBeSetForSummons()
        {
            var owner = new Id("unit.hero");
            var creature = new CreatureUnit(new Id("unit.summon_1"), MapId, FactionId, new Id("creature.wisp"))
            {
                OwnerId = owner,
            };

            Assert.Equal(owner, creature.OwnerId);
        }
    }
}
