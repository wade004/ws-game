using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;
using Xunit;

namespace Tests.Carriers.Unit
{
    public class UnitPersistableTests
    {
        private static readonly Id MapId = new Id("map.town_square");
        private static readonly Id FactionId = new Id("fac.player");
        private static readonly Id ArchetypeId = new Id("arch.class.sample");

        [Fact]
        public void CurrentMapId_SectionKey_MatchesSaveSections()
        {
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            var persistable = UnitPersistable.CurrentMapId(player);

            Assert.Equal(SaveSections.WorldCurrentMapId, persistable.SectionKey);
        }

        [Fact]
        public void CurrentPosition_SectionKey_MatchesSaveSections()
        {
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            var persistable = UnitPersistable.CurrentPosition(player);

            Assert.Equal(SaveSections.WorldCurrentPosition, persistable.SectionKey);
        }

        [Fact]
        public void CurrentMapId_RoundTrips()
        {
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            var persistable = UnitPersistable.CurrentMapId(player);

            var saved = persistable.Save();

            var loadedInto = new PlayerUnit(new Id("unit.hero"), new Id("map.other"), FactionId, ArchetypeId);
            UnitPersistable.CurrentMapId(loadedInto).Load(saved);

            Assert.Equal(MapId, loadedInto.MapId);
        }

        [Fact]
        public void CurrentPosition_RoundTrips()
        {
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId) { Position = new Vec2(12.5, -3.25) };
            var persistable = UnitPersistable.CurrentPosition(player);

            var saved = persistable.Save();

            var loadedInto = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            UnitPersistable.CurrentPosition(loadedInto).Load(saved);

            Assert.Equal(new Vec2(12.5, -3.25), loadedInto.Position);
        }

        [Fact]
        public void CurrentPosition_Load_RejectsWrongShape()
        {
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            var persistable = UnitPersistable.CurrentPosition(player);

            Assert.Throws<System.FormatException>(() => persistable.Load(new JsonString("not-an-object")));
        }

        [Fact]
        public void CurrentMapId_Load_NullData_LeavesUnchanged()
        {
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            var persistable = UnitPersistable.CurrentMapId(player);

            persistable.Load(JsonNull.Instance);

            Assert.Equal(MapId, player.MapId);
        }
    }
}
