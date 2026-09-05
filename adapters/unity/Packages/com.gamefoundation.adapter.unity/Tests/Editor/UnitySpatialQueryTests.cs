#nullable enable
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;

namespace Adapter.Unity.Tests.Editor
{
    public sealed class UnitySpatialQueryTests
    {
        private UnitySpatialQuery _query = null!;

        [SetUp]
        public void SetUp()
        {
            _query = new UnitySpatialQuery();
        }

        [Test]
        public void QueryRadius_ReturnsMatchingIdsSortedById()
        {
            _query.Register(new Id("creature.zebra"), new Vec2(1, 0), 0.5);
            _query.Register(new Id("creature.ant"), new Vec2(0, 1), 0.5);
            _query.Register(new Id("creature.far_away"), new Vec2(100, 100), 0.5);

            var result = _query.QueryRadius(Vec2.Zero, 5, QueryFilter.None);

            CollectionAssert.AreEqual(
                new[] { new Id("creature.ant"), new Id("creature.zebra") },
                result);
        }

        [Test]
        public void QueryRadius_RespectsRequiredAndExcludedTags()
        {
            _query.Register(new Id("unit.ally1"), new Vec2(0, 0), 0.1, new[] { "ally" });
            _query.Register(new Id("unit.enemy1"), new Vec2(0, 0), 0.1, new[] { "enemy" });

            var allies = _query.QueryRadius(Vec2.Zero, 1, new QueryFilter(requiredTags: new[] { "ally" }));
            var notEnemies = _query.QueryRadius(Vec2.Zero, 1, new QueryFilter(excludedTags: new[] { "enemy" }));

            CollectionAssert.AreEqual(new[] { new Id("unit.ally1") }, allies);
            CollectionAssert.AreEqual(new[] { new Id("unit.ally1") }, notEnemies);
        }

        [Test]
        public void QueryRect_FindsOverlappingObjects()
        {
            _query.Register(new Id("prop.inside"), new Vec2(2, 2), 0.1);
            _query.Register(new Id("prop.outside"), new Vec2(50, 50), 0.1);

            var result = _query.QueryRect(new Vec2(0, 0), new Vec2(5, 5), QueryFilter.None);

            CollectionAssert.AreEqual(new[] { new Id("prop.inside") }, result);
        }

        [Test]
        public void Nearest_ReturnsClosestObject()
        {
            _query.Register(new Id("unit.near"), new Vec2(1, 0), 0.1);
            _query.Register(new Id("unit.far"), new Vec2(9, 0), 0.1);

            var nearest = _query.Nearest(Vec2.Zero, QueryFilter.None);

            Assert.AreEqual(new Id("unit.near"), nearest);
        }

        [Test]
        public void Unregister_RemovesObjectFromFutureQueries()
        {
            var id = new Id("unit.temp");
            _query.Register(id, Vec2.Zero, 0.1);
            _query.Unregister(id);

            var result = _query.QueryRadius(Vec2.Zero, 1, QueryFilter.None);

            CollectionAssert.IsEmpty(result);
        }

        [Test]
        public void HasLineOfSight_DefaultsTrue_WithoutBlocker()
        {
            Assert.IsTrue(_query.HasLineOfSight(Vec2.Zero, new Vec2(10, 10)));
        }

        [Test]
        public void UpdatePosition_MovesRegisteredObject_QueryReflectsNewPosition()
        {
            var id = new Id("unit.mover");
            _query.Register(id, Vec2.Zero, 0.1);

            _query.UpdatePosition(id, new Vec2(10, 10));

            CollectionAssert.IsEmpty(_query.QueryRadius(Vec2.Zero, 1, QueryFilter.None));
            CollectionAssert.Contains(_query.QueryRadius(new Vec2(10, 10), 1, QueryFilter.None), id);
        }

        [Test]
        public void UpdatePosition_UnregisteredId_IsNoOp_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _query.UpdatePosition(new Id("unit.never_registered"), new Vec2(1, 1)));
        }

        [Test]
        public void Clear_RemovesAllRegisteredObjects()
        {
            _query.Register(new Id("unit.a"), Vec2.Zero, 0.1);
            _query.Register(new Id("unit.b"), new Vec2(5, 5), 0.1);

            _query.Clear();

            Assert.IsNull(_query.Nearest(Vec2.Zero, QueryFilter.None));
        }
    }
}
