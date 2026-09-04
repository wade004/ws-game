using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Gameplay.Quest;
using Xunit;

namespace Tests.PresentationUi
{
    public class UiDataSourceTests
    {
        private static readonly Id Health = new Id("arch.power.health");
        private static readonly Id Strength = new Id("stat.strength");

        [Fact]
        public void Query_player_power_current_and_max()
        {
            var world = new UiWorldFixture();
            world.PowerHost.RegisterUnit(world.PlayerId, new[] { Health });
            world.PowerHost.SetForTest(world.PlayerId, Health, 40, 100);

            Assert.Equal(40, world.DataSource.Query($"player.power.{Health}.current")!.Value.AsNumber);
            Assert.Equal(100, world.DataSource.Query($"player.power.{Health}.max")!.Value.AsNumber);
        }

        [Fact]
        public void Query_player_stat()
        {
            var world = new UiWorldFixture();
            world.StatHost.SetBase(world.PlayerId, Strength, 12);

            var value = world.DataSource.Query($"player.stat.{Strength}");
            Assert.Equal(12, value!.Value.AsNumber);
        }

        [Fact]
        public void Query_player_level_xp_and_xp_to_next()
        {
            var world = new UiWorldFixture();
            world.Progression.SetForTest(world.PlayerId, 5, 120, 500);

            Assert.Equal(5, world.DataSource.Query("player.level")!.Value.AsInt);
            Assert.Equal(120, world.DataSource.Query("player.xp")!.Value.AsInt);
            Assert.Equal(500, world.DataSource.Query("player.xp_to_next")!.Value.AsInt);
        }

        [Fact]
        public void Query_inventory_count_and_indexed_fields()
        {
            var world = new UiWorldFixture();
            var templateId = new Id("item.iron_sword");
            var instanceId = world.Inventory.AddItemForTest(world.PlayerId, templateId, 3);

            Assert.Equal(1, world.DataSource.Query("player.inventory.count")!.Value.AsInt);
            Assert.Equal(templateId, world.DataSource.Query("player.inventory[0].template")!.Value.AsId);
            Assert.Equal(3, world.DataSource.Query("player.inventory[0].count")!.Value.AsInt);
            Assert.Equal(instanceId, world.DataSource.Query("player.inventory[0].instance")!.Value.AsId);

            Assert.Null(world.DataSource.Query("player.inventory[5].template"));
        }

        [Fact]
        public void Query_equipment_slot()
        {
            var world = new UiWorldFixture();
            var slot = new Id("equip.main_hand");
            var instanceId = new Id("item.instance_0");
            world.Equipment.Equip(world.PlayerId, instanceId, slot);

            Assert.Equal(instanceId, world.DataSource.Query($"player.equipment.{slot}")!.Value.AsId);
            Assert.Null(world.DataSource.Query("player.equipment.equip.off_hand"));
        }

        [Fact]
        public void Query_quest_state_and_objective()
        {
            var world = new UiWorldFixture();
            var questId = new Id("quest.find_the_missing_child");
            world.Quest.SeedQuestForTest(questId, QuestState.Active, new[] { 2, 0 });

            Assert.Equal("Active", world.DataSource.Query($"player.quest.{questId}.state")!.Value.AsString);
            Assert.Equal(2, world.DataSource.Query($"player.quest.{questId}.objective[0]")!.Value.AsInt);
            Assert.Null(world.DataSource.Query($"player.quest.{questId}.objective[9]"));
        }

        [Fact]
        public void Query_currency_balance()
        {
            var world = new UiWorldFixture();
            var gold = new Id("econ.currency.gold");
            world.Economy.SetBalanceForTest(world.PlayerId, gold, 250);

            Assert.Equal(250, world.DataSource.Query($"player.currency.{gold}")!.Value.AsInt);
        }

        [Fact]
        public void Query_skills_index_and_skill_cooldown()
        {
            var world = new UiWorldFixture();
            var fireball = new Id("skill.fireball");
            world.SkillBook.LearnForTest(world.PlayerId, fireball);
            world.SkillBook.SetCooldownForTest(world.PlayerId, fireball, 1.5);

            Assert.Equal(fireball, world.DataSource.Query("player.skills[0]")!.Value.AsId);
            Assert.Null(world.DataSource.Query("player.skills[1]"));
            Assert.Equal(1.5, world.DataSource.Query($"player.skill.{fireball}.cooldown")!.Value.AsNumber);
        }

        [Fact]
        public void Query_target_returns_null_without_diagnostics_when_no_target_selected()
        {
            var world = new UiWorldFixture();
            world.CurrentTarget = null;

            Assert.Null(world.DataSource.Query($"target.power.{Health}.current"));
            Assert.Empty(world.Diagnostics.Warnings);
        }

        [Fact]
        public void Query_target_resolves_through_target_resolver()
        {
            var world = new UiWorldFixture();
            world.CurrentTarget = world.TargetId;
            world.PowerHost.RegisterUnit(world.TargetId, new[] { Health });
            world.PowerHost.SetForTest(world.TargetId, Health, 30, 60);

            Assert.Equal(30, world.DataSource.Query($"target.power.{Health}.current")!.Value.AsNumber);
            Assert.Equal(60, world.DataSource.Query($"target.power.{Health}.max")!.Value.AsNumber);
        }

        [Fact]
        public void Query_unit_by_id_resolves_power_and_stat()
        {
            var world = new UiWorldFixture();
            world.PowerHost.RegisterUnit(world.TargetId, new[] { Health });
            world.PowerHost.SetForTest(world.TargetId, Health, 10, 20);
            world.StatHost.SetBase(world.TargetId, Strength, 7);

            Assert.Equal(10, world.DataSource.Query($"unit.{world.TargetId}.power.{Health}.current")!.Value.AsNumber);
            Assert.Equal(7, world.DataSource.Query($"unit.{world.TargetId}.stat.{Strength}")!.Value.AsNumber);
        }

        [Fact]
        public void Query_unknown_root_returns_null_and_records_diagnostic()
        {
            var world = new UiWorldFixture();

            Assert.Null(world.DataSource.Query("shrine.blessing"));
            Assert.Single(world.Diagnostics.Warnings);
        }

        [Fact]
        public void Query_malformed_path_returns_null_and_records_diagnostic()
        {
            var world = new UiWorldFixture();

            Assert.Null(world.DataSource.Query("player..level"));
            Assert.Single(world.Diagnostics.Warnings);
        }

        [Fact]
        public void Subscribe_forwards_to_event_bus_and_fires_on_publish()
        {
            var world = new UiWorldFixture();
            var key = new Id("power.changed");
            var received = 0;
            var handle = world.DataSource.Subscribe(key, evt => received++);

            world.EventBus.PublishImmediate(new TestEvent(key));
            Assert.Equal(1, received);

            world.DataSource.Unsubscribe(handle);
            world.EventBus.PublishImmediate(new TestEvent(key));
            Assert.Equal(1, received);
        }

        private sealed class TestEvent : Core.Foundation.EventBus.IEvent
        {
            public TestEvent(Id key) => Key = key;
            public Id Key { get; }
        }
    }
}
