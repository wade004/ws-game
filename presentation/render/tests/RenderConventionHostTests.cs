using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Presentation.Common;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    public class RenderConventionHostTests
    {
        private readonly RenderConventionHost _host = new RenderConventionHost();

        [Fact]
        public void ComputeSortY_AddsSortOffsetToLogicalY()
        {
            var sortY = _host.ComputeSortY(new Vec2(10, 20), 1.5);

            Assert.Equal(21.5, sortY);
        }

        [Fact]
        public void TieBreakComparer_EqualSortY_OrdersByIdAscending()
        {
            var a = (Id: new Id("unit.a"), SortY: 5.0);
            var b = (Id: new Id("unit.b"), SortY: 5.0);

            Assert.True(_host.TieBreakComparer.Compare(a, b) < 0);
            Assert.True(_host.TieBreakComparer.Compare(b, a) > 0);
        }

        [Fact]
        public void TieBreakComparer_DifferentSortY_OrdersBySortYRegardlessOfId()
        {
            var lower = (Id: new Id("unit.z"), SortY: 1.0);
            var higher = (Id: new Id("unit.a"), SortY: 2.0);

            Assert.True(_host.TieBreakComparer.Compare(lower, higher) < 0);
        }

        [Fact]
        public void ResolveDirectionSlot_NoMirrorPair_ReturnsCanonicalSlot_NoFlip()
        {
            var sprite = new SpriteInfo("sprite.x", 8);
            var direction = Direction.FromQuantized(0.0, 8); // index 0 -> "dir.e"

            var (slotId, flipX) = _host.ResolveDirectionSlot(direction, sprite);

            Assert.Equal(new Id("dir.e"), slotId);
            Assert.False(flipX);
        }

        [Fact]
        public void ResolveDirectionSlot_WithMirrorPair_ReturnsMirrorOfAndFlip()
        {
            var mirrorPairs = new[] { new MirrorPair(new Id("dir.nw"), new Id("dir.ne"), true) };
            var sprite = new SpriteInfo("sprite.x", 8, mirrorPairs);
            var direction = Direction.FromQuantized(System.Math.PI * 3 / 4, 8); // 135 度 -> index 3 -> "dir.nw"

            var (slotId, flipX) = _host.ResolveDirectionSlot(direction, sprite);

            Assert.Equal(new Id("dir.ne"), slotId);
            Assert.True(flipX);
        }

        [Theory]
        [InlineData(0, "dir.e")]
        [InlineData(1, "dir.ne")]
        [InlineData(2, "dir.n")]
        [InlineData(3, "dir.nw")]
        [InlineData(4, "dir.w")]
        [InlineData(5, "dir.sw")]
        [InlineData(6, "dir.s")]
        [InlineData(7, "dir.se")]
        public void CanonicalDirectionSlotId_8Directions_MatchesCompassLabels(int index, string expected)
        {
            Assert.Equal(new Id(expected), RenderConventionHost.CanonicalDirectionSlotId(index, 8));
        }

        [Theory]
        [InlineData(0, "dir.e")]
        [InlineData(1, "dir.n")]
        [InlineData(2, "dir.w")]
        [InlineData(3, "dir.s")]
        public void CanonicalDirectionSlotId_4Directions_MatchesCompassLabels(int index, string expected)
        {
            Assert.Equal(new Id(expected), RenderConventionHost.CanonicalDirectionSlotId(index, 4));
        }

        [Fact]
        public void CanonicalDirectionSlotId_16Directions_FallsBackToNumericSlot()
        {
            Assert.Equal(new Id("dir.slot_5"), RenderConventionHost.CanonicalDirectionSlotId(5, 16));
        }

        [Fact]
        public void ComposeSpriteLayers_PreservesOrder_AndAppliesSameSlotToAllLayers()
        {
            var sprite = new SpriteInfo("sprite.x", 8);
            var direction = Direction.FromQuantized(0.0, 8);
            var layers = new List<string> { "body", "chest_armor", "weapon" };

            var placements = _host.ComposeSpriteLayers(layers, sprite, direction);

            Assert.Equal(3, placements.Count);
            Assert.Equal("body", placements[0].LayerName);
            Assert.Equal("chest_armor", placements[1].LayerName);
            Assert.Equal("weapon", placements[2].LayerName);
            Assert.All(placements, p => Assert.Equal(new Id("dir.e"), p.DirectionSlotId));
        }

        [Fact]
        public void HeightOffsetToPixels_MultipliesByPixelsPerUnit()
        {
            Assert.Equal(64.0, _host.HeightOffsetToPixels(2.0, 32.0));
        }

        [Fact]
        public void ShadowSpec_ResolveAnchor_ReturnsLogicalPositionUnchanged()
        {
            var pos = new Vec2(3, 4);

            Assert.Equal(pos, ShadowSpec.ResolveAnchor(pos));
        }

        [Theory]
        [InlineData(ShadowMode.None, Core.Foundation.EngineAdapter.ShadowMode.None)]
        [InlineData(ShadowMode.Blob, Core.Foundation.EngineAdapter.ShadowMode.Blob)]
        [InlineData(ShadowMode.Projected, Core.Foundation.EngineAdapter.ShadowMode.Projected)]
        public void ShadowSpec_ToEngineShadowMode_MapsEachValue(ShadowMode dataMode, Core.Foundation.EngineAdapter.ShadowMode expected)
        {
            Assert.Equal(expected, ShadowSpec.ToEngineShadowMode(dataMode));
        }
    }
}
