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
        public void ResolveDirectionSlot_CanonicalSlotWithNoDefaultMirror_ReturnsCanonicalSlot_NoFlip()
        {
            var sprite = new SpriteInfo("sprite.x", 8);
            var direction = Direction.FromQuantized(System.Math.PI / 2, 8); // 90 度 -> index 2 -> "dir.front"（面朝观察者，无镜像）

            var (slotId, flipX) = _host.ResolveDirectionSlot(direction, sprite);

            Assert.Equal(DirectionSlots.Front, slotId);
            Assert.False(flipX);
        }

        [Fact]
        public void ResolveDirectionSlot_LSlot_NoMirrorPairRegistered_FallsBackToDefault14MirrorTable()
        {
            var sprite = new SpriteInfo("sprite.x", 8); // 未登记 mirror_pairs
            var direction = Direction.FromQuantized(0.0, 8); // index 0 -> "dir.side_l"（14 默认镜像自 side_r）

            var (slotId, flipX) = _host.ResolveDirectionSlot(direction, sprite);

            Assert.Equal(DirectionSlots.SideR, slotId);
            Assert.True(flipX);
        }

        [Fact]
        public void ResolveDirectionSlot_ExplicitMirrorPair_OverridesDefault14MirrorTable()
        {
            // 内容侧显式登记的 mirror_pairs 优先于 14 的默认镜像表回退（09 第 3.2 节镜像规则表）。
            var mirrorPairs = new[] { new MirrorPair(DirectionSlots.SideL, DirectionSlots.Front, true) };
            var sprite = new SpriteInfo("sprite.x", 8, mirrorPairs);
            var direction = Direction.FromQuantized(0.0, 8); // index 0 -> "dir.side_l"

            var (slotId, flipX) = _host.ResolveDirectionSlot(direction, sprite);

            Assert.Equal(DirectionSlots.Front, slotId);
            Assert.True(flipX);
        }

        [Fact]
        public void ComposeSpriteLayers_PreservesOrder_AndAppliesSameSlotToAllLayers()
        {
            var sprite = new SpriteInfo("sprite.x", 8);
            var direction = Direction.FromQuantized(System.Math.PI / 2, 8); // index 2 -> "dir.front"
            var layers = new List<string> { "body", "chest_armor", "weapon" };

            var placements = _host.ComposeSpriteLayers(layers, sprite, direction);

            Assert.Equal(3, placements.Count);
            Assert.Equal("body", placements[0].LayerName);
            Assert.Equal("chest_armor", placements[1].LayerName);
            Assert.Equal("weapon", placements[2].LayerName);
            Assert.All(placements, p => Assert.Equal(DirectionSlots.Front, p.DirectionSlotId));
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

        // -----------------------------------------------------------------
        // 缺口 8（方向索引重映射策略）。
        // -----------------------------------------------------------------

        [Fact]
        public void ResolveDirectionSlot_WithDirectionIndexRemap_UsesRemappedIndex()
        {
            // 恒等映射下 index 2 -> "dir.front"；重映射把 2 指向 4（"dir.side_r"），验证 ResolveDirectionSlot
            // 换算规范档位前先经重映射表这一步真的生效。
            var remap = new[] { 0, 1, 4, 3, 2, 5, 6, 7 };
            var host = new RenderConventionHost(new RenderOptions { DirectionIndexRemap = remap });
            var sprite = new SpriteInfo("sprite.x", 8);
            var direction = Direction.FromQuantized(System.Math.PI / 2, 8); // index 2

            var (slotId, flipX) = host.ResolveDirectionSlot(direction, sprite);

            Assert.Equal(DirectionSlots.SideR, slotId);
            Assert.False(flipX);
        }

        [Fact]
        public void ResolveDirectionSlot_DirectionIndexRemap_LengthMismatch_FallsBackToIdentity()
        {
            // 重映射表长度（4）与当前方向档位数（8）不一致时按恒等映射处理，不抛异常。
            var remap = new[] { 0, 1, 2, 3 };
            var host = new RenderConventionHost(new RenderOptions { DirectionIndexRemap = remap });
            var sprite = new SpriteInfo("sprite.x", 8);
            var direction = Direction.FromQuantized(System.Math.PI / 2, 8); // index 2 -> "dir.front"

            var (slotId, flipX) = host.ResolveDirectionSlot(direction, sprite);

            Assert.Equal(DirectionSlots.Front, slotId);
            Assert.False(flipX);
        }

        [Fact]
        public void ResolveDirectionSlot_NoDirectionIndexRemap_DefaultsToIdentity()
        {
            var host = new RenderConventionHost();
            var sprite = new SpriteInfo("sprite.x", 8);
            var direction = Direction.FromQuantized(System.Math.PI / 2, 8);

            var (slotId, flipX) = host.ResolveDirectionSlot(direction, sprite);

            Assert.Equal(DirectionSlots.Front, slotId);
            Assert.False(flipX);
        }
    }
}
