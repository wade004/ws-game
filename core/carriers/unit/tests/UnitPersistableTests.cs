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

        // -----------------------------------------------------------------
        // ArchetypeId（W1 收边补齐：A4 审计 F1，player.archetype 段）
        // -----------------------------------------------------------------

        [Fact]
        public void ArchetypeId_SectionKey_MatchesSaveSections()
        {
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            var persistable = UnitPersistable.ArchetypeId(player);

            Assert.Equal(SaveSections.PlayerArchetype, persistable.SectionKey);
        }

        [Fact]
        public void ArchetypeId_RoundTrips()
        {
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            var persistable = UnitPersistable.ArchetypeId(player);

            var saved = persistable.Save();

            var loadedInto = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, new Id("arch.class.other"));
            UnitPersistable.ArchetypeId(loadedInto).Load(saved);

            Assert.Equal(ArchetypeId, loadedInto.ArchetypeId);
        }

        [Fact]
        public void ArchetypeId_Load_RejectsWrongShape()
        {
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            var persistable = UnitPersistable.ArchetypeId(player);

            Assert.Throws<System.FormatException>(() => persistable.Load(new JsonObjectBuilder().Build()));
        }

        [Fact]
        public void ArchetypeId_Load_NullData_LeavesUnchanged()
        {
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            var persistable = UnitPersistable.ArchetypeId(player);

            persistable.Load(JsonNull.Instance);

            Assert.Equal(ArchetypeId, player.ArchetypeId);
        }

        // -----------------------------------------------------------------
        // RaceId（种族被动光环跨图丢失根治，architecture/落地计划/audit-85f1f4f-20260908，
        // player.race_id 段）
        // -----------------------------------------------------------------

        [Fact]
        public void RaceId_SectionKey_MatchesSaveSections()
        {
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            var persistable = UnitPersistable.RaceId(player);

            Assert.Equal(SaveSections.PlayerRaceId, persistable.SectionKey);
        }

        [Fact]
        public void RaceId_RoundTrips_WhenSet()
        {
            var raceId = new Id("arch.race.sample");
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId) { RaceId = raceId };
            var persistable = UnitPersistable.RaceId(player);

            var saved = persistable.Save();

            var loadedInto = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            UnitPersistable.RaceId(loadedInto).Load(saved);

            Assert.Equal(raceId, loadedInto.RaceId);
        }

        [Fact]
        public void RaceId_Save_WhenUnset_ProducesJsonNull()
        {
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            var persistable = UnitPersistable.RaceId(player);

            var saved = persistable.Save();

            Assert.IsType<JsonNull>(saved);
        }

        /// <summary>
        /// 与 <see cref="CurrentMapId_Load_NullData_LeavesUnchanged"/>/<see
        /// cref="ArchetypeId_Load_NullData_LeavesUnchanged"/>（两者都显式声明
        /// <c>KeepStateWhenSectionMissing=true</c>）刻意不同：<c>Id?</c> 本身有明确无歧义的空值，
        /// <see cref="RaceIdPersistable"/> 不声明该例外，缺段/为 <c>null</c> 按默认语义清空为
        /// <c>null</c>——即便读档前已经设置过种族。
        /// </summary>
        [Fact]
        public void RaceId_Load_NullData_ClearsToNull_EvenIfPreviouslySet()
        {
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId) { RaceId = new Id("arch.race.sample") };
            var persistable = UnitPersistable.RaceId(player);

            persistable.Load(JsonNull.Instance);

            Assert.Null(player.RaceId);
        }

        [Fact]
        public void RaceId_Load_RejectsWrongShape()
        {
            var player = new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);
            var persistable = UnitPersistable.RaceId(player);

            Assert.Throws<System.FormatException>(() => persistable.Load(new JsonObjectBuilder().Build()));
        }
    }
}
