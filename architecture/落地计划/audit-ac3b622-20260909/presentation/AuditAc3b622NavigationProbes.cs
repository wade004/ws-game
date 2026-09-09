#nullable enable
using System;
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using NUnit.Framework;
using UnityEngine;
using FoundationRect = Core.Foundation.Common.Rect;

namespace Adapter.Unity.Tests.Editor
{
    /// <summary>
    /// Audit-only probes. These tests intentionally document two candidate boundaries without changing
    /// the product or the frozen repository's existing tests.
    /// </summary>
    public sealed class AuditAc3b622NavigationProbes
    {
        private static readonly Id MapA = new Id("audit.ac3b622.nav.a");
        private static readonly Id MapB = new Id("audit.ac3b622.nav.b");

        [Test]
        public void CandidateA_EndpointCellCenterBlockedDespiteExactEndpointAndDirectRaycastClear()
        {
            var nav = new UnityNavigation2D();
            nav.SetBlocking(MapA, new[] { new FoundationRect(new Vec2(0, 0), new Vec2(1.2, 1)) });
            var from = new Vec2(1.21, 0.5);
            var to = new Vec2(1.8, 0.5);
            nav.BuildNavMesh(MapA);

            var fromWalkable = nav.IsWalkable(MapA, from);
            var toWalkable = nav.IsWalkable(MapA, to);
            var directHit = nav.Raycast(MapA, from, to);
            var path = nav.FindPath(MapA, from, to);

            TestContext.Progress.WriteLine($"PROBE=A fromWalkable={fromWalkable} toWalkable={toWalkable} directRaycast={(directHit.HasValue ? directHit.Value.ToString() : "null")} path={(path == null ? "null" : path.Count.ToString())}");
            Assert.IsTrue(fromWalkable);
            Assert.IsTrue(toWalkable);
            Assert.IsNull(directHit);
            Assert.IsNull(path, "Observed candidate: endpoint's sampled grid cell center is inside the blocker.");
        }

        [Test]
        public void CandidateB_ThinWallReturnsNullWhileManualDetourEverySegmentIsClear()
        {
            var nav = new UnityNavigation2D();
            nav.SetBlocking(MapB, new[] { new FoundationRect(new Vec2(0.1, -0.5), new Vec2(0.15, 0.5)) });
            var from = new Vec2(-1, 0);
            var to = new Vec2(1, 0);
            nav.BuildNavMesh(MapB);

            var path = nav.FindPath(MapB, from, to);
            var oracle = new[]
            {
                from,
                new Vec2(-0.2, -0.6),
                new Vec2(0.2, -0.6),
                to
            };
            var oracleHits = new List<string>();
            for (var i = 0; i < oracle.Length - 1; i++)
            {
                var hit = nav.Raycast(MapB, oracle[i], oracle[i + 1]);
                oracleHits.Add(hit.HasValue ? hit.Value.ToString() : "null");
            }

            TestContext.Progress.WriteLine($"PROBE=B path={(path == null ? "null" : path.Count.ToString())} oracleRaycasts={string.Join(",", oracleHits)}");
            Debug.Log($"PROBE=B path={(path == null ? "null" : path.Count.ToString())} oracleRaycasts={string.Join(",", oracleHits)}");
            Assert.IsNull(path, "Observed candidate: center-sampled A* sees no blocked grid cell and final guard rejects the straight route.");
            Assert.That(oracleHits, Is.All.EqualTo("null"));
        }
    }
}
