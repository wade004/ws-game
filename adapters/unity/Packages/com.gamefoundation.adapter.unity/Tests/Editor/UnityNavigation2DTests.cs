#nullable enable
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using NUnit.Framework;

namespace Adapter.Unity.Tests.Editor
{
    public sealed class UnityNavigation2DTests
    {
        private static readonly Id MapId = new Id("map.test_arena");

        private UnityNavigation2D _nav = null!;

        [SetUp]
        public void SetUp()
        {
            _nav = new UnityNavigation2D();
        }

        [Test]
        public void IsWalkable_NoBlockingRects_DefaultsTrue()
        {
            Assert.IsTrue(_nav.IsWalkable(MapId, new Vec2(3, 3)));
        }

        [Test]
        public void RegisterBlockingRect_MakesPointInsideUnwalkable()
        {
            _nav.RegisterBlockingRect(MapId, new Vec2(0, 0), new Vec2(2, 2));

            Assert.IsFalse(_nav.IsWalkable(MapId, new Vec2(1, 1)));
            Assert.IsTrue(_nav.IsWalkable(MapId, new Vec2(5, 5)));
        }

        [Test]
        public void FindPath_NoObstacle_ReturnsPathFromStartToGoal()
        {
            _nav.BuildNavMesh(MapId);

            var path = _nav.FindPath(MapId, new Vec2(-3, -3), new Vec2(3, 3));

            Assert.IsNotNull(path);
            Assert.Greater(path!.Count, 0);
            Assert.Less(Vec2.Distance(path[0], new Vec2(-3, -3)), 1.0);
            Assert.Less(Vec2.Distance(path[path.Count - 1], new Vec2(3, 3)), 1.0);
        }

        [Test]
        public void FindPath_GoalFullyEnclosed_ReturnsNull()
        {
            // 用四条互相咬合、无缝隙的阻挡矩形围成一个实心方框，把起点完全困在框内
            // （上下两条横贯整个宽度，覆盖四角，避免 8 方向寻路从对角缝隙穿出）。
            _nav.RegisterBlockingRect(MapId, new Vec2(-6, 4), new Vec2(6, 6));    // 上（含四角）
            _nav.RegisterBlockingRect(MapId, new Vec2(-6, -6), new Vec2(6, -4)); // 下（含四角）
            _nav.RegisterBlockingRect(MapId, new Vec2(-6, -4), new Vec2(-4, 4)); // 左
            _nav.RegisterBlockingRect(MapId, new Vec2(4, -4), new Vec2(6, 4));   // 右
            _nav.BuildNavMesh(MapId);

            var path = _nav.FindPath(MapId, new Vec2(0, 0), new Vec2(10, 10));

            Assert.IsNull(path);
        }

        [Test]
        public void Raycast_HitsBlockingRect_ReturnsEntryPoint()
        {
            _nav.RegisterBlockingRect(MapId, new Vec2(2, -1), new Vec2(4, 1));

            var hit = _nav.Raycast(MapId, new Vec2(0, 0), new Vec2(10, 0));

            Assert.IsNotNull(hit);
            Assert.AreEqual(2.0, hit!.Value.X, 0.001);
        }

        [Test]
        public void Raycast_NoObstacle_ReturnsNull()
        {
            Assert.IsNull(_nav.Raycast(MapId, new Vec2(0, 0), new Vec2(10, 0)));
        }
    }
}
