// CollisionLayers / NavGridLayout 直接断言（测试覆盖第四批 T-M11）：两者是引擎适配层契约里被桩与
// 真实实现共用的“唯一定义点”，此前只被上层用例间接引用，没有对着自身的规则断言。
// 期望值一律由类型自己公开的常量/公式在用例里算出来，不写裸数。
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.StubAdapters
{
    public class CollisionLayersTests
    {
        private static readonly string[] All =
        {
            CollisionLayers.TerrainBlock, CollisionLayers.UnitBlock, CollisionLayers.TriggerOnly,
        };

        [Fact]
        public void ThreeLabels_AreNonEmpty_AndPairwiseDistinct()
        {
            Assert.All(All, label => Assert.False(string.IsNullOrWhiteSpace(label)));
            Assert.Equal(All.Length, All.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void Labels_AreLowerSnakeCase_SoTheyCanBeUsedAsDataTagsWithoutEscaping()
        {
            Assert.All(All, label => Assert.Matches("^[a-z]+(_[a-z]+)*$", label));
        }

        [Fact]
        public void Labels_SurviveAQueryFilterRoundTrip_AsOpaqueStringTags()
        {
            var filter = new QueryFilter(requiredTags: new[] { CollisionLayers.UnitBlock }, excludedTags: new[] { CollisionLayers.TriggerOnly });
            Assert.Contains(CollisionLayers.UnitBlock, filter.RequiredTags);
            Assert.Contains(CollisionLayers.TriggerOnly, filter.ExcludedTags);
        }
    }

    public class NavGridLayoutTests
    {
        private static Rect R(double x0, double y0, double x1, double y1) => new Rect(new Vec2(x0, y0), new Vec2(x1, y1));

        [Fact]
        public void Compute_NoBlockers_UsesDefaultBoundsPlusMargin_AndCellCountFollowsSpanOverCellSize()
        {
            // 规则：缺省包围盒 (-8,-8)..(8,8)，各向外扩 BoundsMargin。
            var layout = NavGridLayout.Compute(Array.Empty<Rect>());

            var lo = -8.0 - NavGridLayout.BoundsMargin;
            var hi = 8.0 + NavGridLayout.BoundsMargin;
            Assert.Equal(new Vec2(lo, lo), layout.Origin);
            Assert.Equal(NavGridLayout.DefaultCellSize, layout.CellSize);
            var expectedCells = (int)Math.Ceiling((hi - lo) / NavGridLayout.DefaultCellSize);
            Assert.Equal(expectedCells, layout.Width);
            Assert.Equal(expectedCells, layout.Height);
        }

        [Fact]
        public void Compute_NullBlockers_EqualsEmptyBlockers()
        {
            var a = NavGridLayout.Compute((IReadOnlyList<Rect>?)null);
            var b = NavGridLayout.Compute(Array.Empty<Rect>());
            Assert.Equal(a.Origin, b.Origin);
            Assert.Equal(a.CellSize, b.CellSize);
            Assert.Equal(a.Width, b.Width);
            Assert.Equal(a.Height, b.Height);
        }

        [Fact]
        public void Compute_WithBlockers_UsesUnionBoundingBoxOfThemOnly_NotTheDefaultBox()
        {
            var blockers = new[] { R(1, 2, 3, 4), R(-1, 0, 2, 9) };
            var layout = NavGridLayout.Compute(blockers);

            var loX = blockers.Min(b => b.Min.X) - NavGridLayout.BoundsMargin;
            var loY = blockers.Min(b => b.Min.Y) - NavGridLayout.BoundsMargin;
            var hiX = blockers.Max(b => b.Max.X) + NavGridLayout.BoundsMargin;
            var hiY = blockers.Max(b => b.Max.Y) + NavGridLayout.BoundsMargin;
            Assert.Equal(new Vec2(loX, loY), layout.Origin);
            Assert.Equal((int)Math.Ceiling((hiX - loX) / layout.CellSize), layout.Width);
            Assert.Equal((int)Math.Ceiling((hiY - loY) / layout.CellSize), layout.Height);
            // 没有被缺省的 (-8,-8)..(8,8) 并进去。
            Assert.True(layout.Origin.X > -8.0 - NavGridLayout.BoundsMargin);
        }

        [Fact]
        public void Compute_GridCoversTheWholePaddedBox_LastCellReachesPastHighEdge()
        {
            var blockers = new[] { R(0, 0, 3.3, 7.1) };
            var layout = NavGridLayout.Compute(blockers);

            var hiX = 3.3 + NavGridLayout.BoundsMargin;
            var hiY = 7.1 + NavGridLayout.BoundsMargin;
            Assert.True(layout.Origin.X + layout.Width * layout.CellSize >= hiX);
            Assert.True(layout.Origin.Y + layout.Height * layout.CellSize >= hiY);
            // 但不会多出整整一格。
            Assert.True(layout.Origin.X + (layout.Width - 1) * layout.CellSize < hiX);
            Assert.True(layout.Origin.Y + (layout.Height - 1) * layout.CellSize < hiY);
        }

        [Fact]
        public void Compute_HugeMap_ScalesCellSizeUpSoNoAxisExceedsMaxGridDimension_AndStillCoversTheBox()
        {
            var blockers = new[] { R(0, 0, 400, 100) };
            var layout = NavGridLayout.Compute(blockers);

            Assert.True(layout.CellSize > NavGridLayout.DefaultCellSize);
            Assert.True(layout.Width <= NavGridLayout.MaxGridDimension);
            Assert.True(layout.Height <= NavGridLayout.MaxGridDimension);
            var hiX = 400 + NavGridLayout.BoundsMargin;
            var hiY = 100 + NavGridLayout.BoundsMargin;
            Assert.True(layout.Origin.X + layout.Width * layout.CellSize >= hiX - 1e-9);
            Assert.True(layout.Origin.Y + layout.Height * layout.CellSize >= hiY - 1e-9);
        }

        [Fact]
        public void Compute_MapExactlyAtTheLimit_KeepsBaseCellSize()
        {
            // 外扩后恰好 MaxGridDimension 格宽：不触发放大。
            var span = NavGridLayout.MaxGridDimension * NavGridLayout.DefaultCellSize;
            var inner = span - 2 * NavGridLayout.BoundsMargin;
            var layout = NavGridLayout.Compute(new[] { R(0, 0, inner, inner) });

            Assert.Equal(NavGridLayout.DefaultCellSize, layout.CellSize);
            Assert.Equal(NavGridLayout.MaxGridDimension, layout.Width);
            Assert.Equal(NavGridLayout.MaxGridDimension, layout.Height);
        }

        [Fact]
        public void Compute_CustomBaseCellSize_IsHonouredWhenWithinLimit()
        {
            var layout = NavGridLayout.Compute(new[] { R(0, 0, 4, 4) }, baseCellSize: 0.5);
            Assert.Equal(0.5, layout.CellSize);
            Assert.Equal((int)Math.Ceiling((4 + 2 * NavGridLayout.BoundsMargin) / 0.5), layout.Width);
        }

        [Fact]
        public void Compute_DegenerateZeroSizeBlocker_StillYieldsAtLeastOneCell_AndMarginBox()
        {
            var layout = NavGridLayout.Compute(new[] { R(5, 5, 5, 5) });
            Assert.True(layout.Width >= 1);
            Assert.True(layout.Height >= 1);
            Assert.Equal(new Vec2(5 - NavGridLayout.BoundsMargin, 5 - NavGridLayout.BoundsMargin), layout.Origin);
        }

        [Fact]
        public void Compute_GenericOverload_AgreesWithRectConvenienceOverload()
        {
            var rects = new[] { R(-3, 1, 2, 5), R(7, -4, 9, 0) };
            var viaRect = NavGridLayout.Compute(rects);
            var viaGeneric = NavGridLayout.Compute(rects, r => r.Min, r => r.Max);

            Assert.Equal(viaRect.Origin, viaGeneric.Origin);
            Assert.Equal(viaRect.CellSize, viaGeneric.CellSize);
            Assert.Equal(viaRect.Width, viaGeneric.Width);
            Assert.Equal(viaRect.Height, viaGeneric.Height);
        }

        [Fact]
        public void Compute_IsOrderIndependent_OverBlockerPermutations()
        {
            var a = R(0, 0, 1, 1);
            var b = R(10, -5, 12, 2);
            var c = R(-4, 3, -2, 8);
            var first = NavGridLayout.Compute(new[] { a, b, c });
            var second = NavGridLayout.Compute(new[] { c, a, b });

            Assert.Equal(first.Origin, second.Origin);
            Assert.Equal(first.Width, second.Width);
            Assert.Equal(first.Height, second.Height);
            Assert.Equal(first.CellSize, second.CellSize);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(3, 5)]
        [InlineData(-1, 2)]
        public void CellCenter_IsOriginPlusHalfCellOffset_AdjacentCentersAreOneCellApart(int ix, int iy)
        {
            var layout = new NavGridLayout(new Vec2(-2, 4), 0.5, 10, 10);

            var center = layout.CellCenter(ix, iy);
            Assert.Equal(layout.Origin.X + (ix + 0.5) * layout.CellSize, center.X);
            Assert.Equal(layout.Origin.Y + (iy + 0.5) * layout.CellSize, center.Y);
            Assert.Equal(layout.CellSize, layout.CellCenter(ix + 1, iy).X - center.X, 9);
            Assert.Equal(layout.CellSize, layout.CellCenter(ix, iy + 1).Y - center.Y, 9);
        }

        [Fact]
        public void Constructor_StoresFieldsVerbatim()
        {
            var layout = new NavGridLayout(new Vec2(1, 2), 0.75, 11, 13);
            Assert.Equal(new Vec2(1, 2), layout.Origin);
            Assert.Equal(0.75, layout.CellSize);
            Assert.Equal(11, layout.Width);
            Assert.Equal(13, layout.Height);
        }
    }
}
