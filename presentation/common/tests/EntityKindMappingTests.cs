using Presentation.Common;
using Xunit;

namespace Tests.PresentationCommon
{
    public class EntityKindMappingTests
    {
        [Theory]
        [InlineData("player", ViewKind.Unit)]
        [InlineData("creature", ViewKind.Unit)]
        [InlineData("gobj", ViewKind.Gobj)]
        [InlineData("projectile", ViewKind.Projectile)]
        [InlineData("area_trigger", ViewKind.AreaTrigger)]
        [InlineData("loot", ViewKind.DroppedLoot)]
        public void TryMap_KnownKinds_ReturnsExpectedViewKind(string entityKind, ViewKind expected)
        {
            var ok = EntityKindMapping.TryMap(entityKind, out var kind);

            Assert.True(ok);
            Assert.Equal(expected, kind);
        }

        [Fact]
        public void TryMap_UnknownKind_ReturnsFalse()
        {
            var ok = EntityKindMapping.TryMap("something_unmapped", out _);

            Assert.False(ok);
        }
    }
}
