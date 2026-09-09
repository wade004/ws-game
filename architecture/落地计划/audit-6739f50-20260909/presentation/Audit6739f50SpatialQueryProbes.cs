#nullable enable
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;

namespace Adapter.Unity.Tests.Editor
{
    /// <summary>Audit-only spatial-index boundary probe; no production code is changed.</summary>
    public sealed class Audit6739f50SpatialQueryProbes
    {
        [Test]
        public void QueryRadius_CrossesBucketEdge_UsesEntryRadiusInCandidateSelection()
        {
            var query = new UnitySpatialQuery();
            var entity = new Id("audit.6739f50.spatial.bucket_edge");
            query.Register(entity, new Vec2(4.1, 0), 0.7);
            var center = new Vec2(3.5, 0);
            var radius = 0.1;
            var filter = QueryFilter.None;
            var radiusResult = query.QueryRadius(center, radius, filter);
            var rectOracle = query.QueryRect(new Vec2(3.4, -0.1), new Vec2(3.6, 0.1), filter);
            TestContext.Progress.WriteLine($"SPATIAL entity={entity} distance={Vec2.Distance(center, new Vec2(4.1, 0))} effectiveRadius={radius + 0.7} radiusCount={radiusResult.Count} rectOracleCount={rectOracle.Count}");
            Assert.That(Vec2.Distance(center, new Vec2(4.1, 0)), Is.LessThanOrEqualTo(radius + 0.7));
            Assert.That(rectOracle, Does.Contain(entity), "rectangle oracle confirms the entry overlaps the query neighborhood");
            Assert.That(radiusResult, Does.Contain(entity), "radius query must include an entry whose own radius reaches the query center");
        }
    }
}
