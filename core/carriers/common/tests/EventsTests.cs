using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Xunit;

namespace Tests.Carriers.Common
{
    public class EventsTests
    {
        [Fact]
        public void CarriersEventKeys_MatchEventCatalogNaming()
        {
            Assert.Equal("item.added", CarriersEventKeys.ItemAdded.Value);
            Assert.Equal("item.removed", CarriersEventKeys.ItemRemoved.Value);
            Assert.Equal("item.equipped", CarriersEventKeys.ItemEquipped.Value);
            Assert.Equal("item.unequipped", CarriersEventKeys.ItemUnequipped.Value);
            Assert.Equal("creature.spawned", CarriersEventKeys.CreatureSpawned.Value);
            Assert.Equal("creature.despawned", CarriersEventKeys.CreatureDespawned.Value);
            Assert.Equal("gobj.interacted", CarriersEventKeys.GobjInteracted.Value);
            Assert.Equal("gobj.state_changed", CarriersEventKeys.GobjStateChanged.Value);
            Assert.Equal("summon.created", CarriersEventKeys.SummonCreated.Value);
            Assert.Equal("summon.expired", CarriersEventKeys.SummonExpired.Value);
            Assert.Equal("unit.moved", CarriersEventKeys.UnitMoved.Value);
            Assert.Equal("unit.state_changed", CarriersEventKeys.UnitStateChanged.Value);
        }

        [Fact]
        public void ItemAddedEvent_CarriesKeyAndFields()
        {
            var unitId = new Id("unit.hero");
            var instanceId = new Id("item.inst_1");
            var templateId = new Id("item.iron_sword");

            var evt = new ItemAddedEvent(unitId, instanceId, templateId, 2);

            Assert.Equal(CarriersEventKeys.ItemAdded, evt.Key);
            Assert.True(evt.TryGetField("unitId", out var unitValue));
            Assert.Equal(ExprValue.OfId(unitId), unitValue);
            Assert.True(evt.TryGetField("count", out var countValue));
            Assert.Equal(2L, countValue.AsInt);
        }

        [Fact]
        public void ItemRemovedEvent_ExposesReasonAsString()
        {
            var evt = new ItemRemovedEvent(new Id("unit.hero"), new Id("item.inst_1"), 1, "consumed");

            Assert.True(evt.TryGetField("reason", out var value));
            Assert.Equal("consumed", value.AsString);
        }

        [Fact]
        public void ItemEquippedEvent_And_ItemUnequippedEvent_CarrySlot()
        {
            var unitId = new Id("unit.hero");
            var instanceId = new Id("item.inst_1");
            var slot = new Id("item.slot.mainhand");

            var equipped = new ItemEquippedEvent(unitId, instanceId, slot);
            var unequipped = new ItemUnequippedEvent(unitId, slot, instanceId);

            Assert.Equal(CarriersEventKeys.ItemEquipped, equipped.Key);
            Assert.Equal(CarriersEventKeys.ItemUnequipped, unequipped.Key);
            Assert.True(equipped.TryGetField("slot", out var equippedSlot));
            Assert.True(unequipped.TryGetField("slot", out var unequippedSlot));
            Assert.Equal(equippedSlot, unequippedSlot);
        }

        [Fact]
        public void CreatureSpawnedEvent_And_CreatureDespawnedEvent_CarryEntityId()
        {
            var entityId = new Id("creature.inst_1");

            var spawned = new CreatureSpawnedEvent(entityId, new Id("creature.grey_wolf"));
            var despawned = new CreatureDespawnedEvent(entityId, "died");

            Assert.Equal(CarriersEventKeys.CreatureSpawned, spawned.Key);
            Assert.Equal(CarriersEventKeys.CreatureDespawned, despawned.Key);
            Assert.True(despawned.TryGetField("reason", out var reason));
            Assert.Equal("died", reason.AsString);
        }

        [Fact]
        public void GobjInteractedEvent_And_GobjStateChangedEvent_CarryFields()
        {
            var unitId = new Id("unit.hero");
            var gobjId = new Id("gobj.inst_1");

            var interacted = new GobjInteractedEvent(unitId, gobjId);
            var stateChanged = new GobjStateChangedEvent(
                gobjId, "open_state", ExprValue.OfBool(false), ExprValue.OfBool(true));

            Assert.Equal(CarriersEventKeys.GobjInteracted, interacted.Key);
            Assert.True(stateChanged.TryGetField("oldValue", out var oldValue));
            Assert.True(stateChanged.TryGetField("newValue", out var newValue));
            Assert.False(oldValue.AsBool);
            Assert.True(newValue.AsBool);
        }

        [Fact]
        public void SummonCreatedEvent_And_SummonExpiredEvent_CarryOwnerId()
        {
            var entityId = new Id("creature.inst_summon_1");
            var ownerId = new Id("unit.hero");

            var created = new SummonCreatedEvent(entityId, ownerId);
            var expired = new SummonExpiredEvent(entityId, ownerId);

            Assert.True(created.TryGetField("ownerId", out var createdOwner));
            Assert.True(expired.TryGetField("ownerId", out var expiredOwner));
            Assert.Equal(createdOwner, expiredOwner);
        }

        [Fact]
        public void UnitMovedEvent_ExposesUnitIdButNotPosition()
        {
            var unitId = new Id("unit.hero");
            var evt = new UnitMovedEvent(unitId, new Vec2(1, 2));

            Assert.Equal(CarriersEventKeys.UnitMoved, evt.Key);
            Assert.True(evt.TryGetField("unitId", out _));
            Assert.False(evt.TryGetField("position", out _));
            Assert.Equal(new Vec2(1, 2), evt.Position);
        }

        [Fact]
        public void UnitStateChangedEvent_CarriesOldAndNewStateStrings()
        {
            var unitId = new Id("unit.hero");
            var evt = new UnitStateChangedEvent(unitId, "Idle", "Walk");

            Assert.Equal(CarriersEventKeys.UnitStateChanged, evt.Key);
            Assert.True(evt.TryGetField("oldState", out var oldState));
            Assert.True(evt.TryGetField("newState", out var newState));
            Assert.Equal("Idle", oldState.AsString);
            Assert.Equal("Walk", newState.AsString);
        }
    }
}
