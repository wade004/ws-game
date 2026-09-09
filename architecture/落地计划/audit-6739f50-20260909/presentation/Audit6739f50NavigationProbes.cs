#nullable enable
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using NUnit.Framework;
using FoundationRect = Core.Foundation.Common.Rect;

namespace Adapter.Unity.Tests.Editor
{
    /// <summary>Audit-only 1.11 navigation regression and boundary probes.</summary>
    public sealed class Audit6739f50NavigationProbes
    {
        private static void AssertPathClear(UnityNavigation2D nav, Id map, Vec2 from, Vec2 to,
            IReadOnlyList<Vec2> path)
        {
            Assert.AreEqual(from.X, path[0].X, 0.0);
            Assert.AreEqual(from.Y, path[0].Y, 0.0);
            Assert.AreEqual(to.X, path[path.Count - 1].X, 0.0);
            Assert.AreEqual(to.Y, path[path.Count - 1].Y, 0.0);
            for (var i = 0; i < path.Count - 1; i++)
            {
                Assert.IsNull(nav.Raycast(map, path[i], path[i + 1]),
                    $"path segment {i} must pass the same Raycast contract");
            }
        }

        [Test]
        public void NAV110_01_EndpointCellCenterBlocked_ExactEndpointAndDirectRaycastClear_ReturnsNonNullPath()
        {
            var nav = new UnityNavigation2D();
            var map = new Id("audit.6739f50.nav110.01");
            nav.SetBlocking(map, new[] { new FoundationRect(new Vec2(0, 0), new Vec2(1.2, 1)) });
            var from = new Vec2(1.21, 0.5);
            var to = new Vec2(1.8, 0.5);
            nav.BuildNavMesh(map);
            var path = nav.FindPath(map, from, to);
            TestContext.Progress.WriteLine($"NAV110_01 fromWalkable={nav.IsWalkable(map, from)} toWalkable={nav.IsWalkable(map, to)} direct={(nav.Raycast(map, from, to).HasValue ? "hit" : "null")} path={(path == null ? "null" : path.Count.ToString())}");
            Assert.IsTrue(nav.IsWalkable(map, from));
            Assert.IsTrue(nav.IsWalkable(map, to));
            Assert.IsNull(nav.Raycast(map, from, to));
            Assert.IsNotNull(path, "exact endpoints should use ResolveEntryCell rather than reject a blocked cell center");
            AssertPathClear(nav, map, from, to, path!);
        }

        [Test]
        public void NAV110_02_ThinWallNarrowerThanGrid_DetourExists_ReturnsNonNullPath()
        {
            var nav = new UnityNavigation2D();
            var map = new Id("audit.6739f50.nav110.02");
            nav.SetBlocking(map, new[] { new FoundationRect(new Vec2(0.1, -0.5), new Vec2(0.15, 0.5)) });
            var from = new Vec2(-1, 0);
            var to = new Vec2(1, 0);
            nav.BuildNavMesh(map);
            var oracle = new[] { from, new Vec2(-0.2, -0.6), new Vec2(0.2, -0.6), to };
            var oracleHits = new List<string>();
            for (var i = 0; i < oracle.Length - 1; i++)
                oracleHits.Add(nav.Raycast(map, oracle[i], oracle[i + 1]).HasValue ? "hit" : "null");
            var path = nav.FindPath(map, from, to);
            TestContext.Progress.WriteLine($"NAV110_02 path={(path == null ? "null" : path.Count.ToString())} oracle={string.Join(",", oracleHits)}");
            Assert.That(oracleHits, Is.All.EqualTo("null"));
            Assert.IsNotNull(path, "fine-grid fallback should find a detour around a sub-cell wall");
            AssertPathClear(nav, map, from, to, path!);
        }

        [Test]
        public void NAV110_03_NarrowCorridor_ExactPointsAndDirectRaycastClear_ShouldRemainReachable()
        {
            var nav = new UnityNavigation2D();
            var map = new Id("audit.6739f50.nav110.03");
            nav.SetBlocking(map, new[]
            {
                new FoundationRect(new Vec2(-1, -1), new Vec2(1, 0)),
                new FoundationRect(new Vec2(-1, 0.1), new Vec2(1, 1))
            });
            var from = new Vec2(-0.5, 0.05);
            var to = new Vec2(0.5, 0.05);
            nav.BuildNavMesh(map);
            var path = nav.FindPath(map, from, to);
            TestContext.Progress.WriteLine($"NAV110_03 fromWalkable={nav.IsWalkable(map, from)} toWalkable={nav.IsWalkable(map, to)} direct={(nav.Raycast(map, from, to).HasValue ? "hit" : "null")} path={(path == null ? "null" : path.Count.ToString())}");
            Assert.IsTrue(nav.IsWalkable(map, from));
            Assert.IsTrue(nav.IsWalkable(map, to));
            Assert.IsNull(nav.Raycast(map, from, to));
            Assert.IsNotNull(path, "a point-navigation contract has a clear 0.1-wide corridor");
            AssertPathClear(nav, map, from, to, path!);
        }

        [Test]
        public void NAV110_04_FineGrid_WiderSubCellWall_DetourRemainsClear()
        {
            var nav = new UnityNavigation2D();
            var map = new Id("audit.6739f50.nav110.04");
            nav.SetBlocking(map, new[] { new FoundationRect(new Vec2(0.1, -0.5), new Vec2(0.3, 0.5)) });
            var from = new Vec2(-1, 0);
            var to = new Vec2(1, 0);
            nav.BuildNavMesh(map);
            var oracle = new[] { from, new Vec2(-0.2, -0.6), new Vec2(0.4, -0.6), to };
            for (var i = 0; i < oracle.Length - 1; i++)
                Assert.IsNull(nav.Raycast(map, oracle[i], oracle[i + 1]), $"oracle segment {i} should be clear");
            var path = nav.FindPath(map, from, to);
            TestContext.Progress.WriteLine($"NAV110_04 path={(path == null ? "null" : path.Count.ToString())}");
            Assert.IsNotNull(path, "fine-grid fallback should cover a wall wider than its fallback cell but below the main cell");
            AssertPathClear(nav, map, from, to, path!);
        }
    }
}
