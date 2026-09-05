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
        public void SetBlocking_ContractMethod_MakesPointInsideUnwalkable()
        {
            // 契约方法（INavigation2D / ADR-0016 决策 7）：此前的非契约便捷方法 RegisterBlockingRect
            // 已删除，调用方自行收集矩形列表后一次性调用 SetBlocking。
            _nav.SetBlocking(MapId, new[] { new Rect(new Vec2(0, 0), new Vec2(2, 2)) });

            Assert.IsFalse(_nav.IsWalkable(MapId, new Vec2(1, 1)));
            Assert.IsTrue(_nav.IsWalkable(MapId, new Vec2(5, 5)));
        }

        [Test]
        public void SetBlocking_CalledAgain_ReplacesPreviousBatch_DoesNotAccumulate()
        {
            _nav.SetBlocking(MapId, new[] { new Rect(new Vec2(0, 0), new Vec2(2, 2)) });
            Assert.IsFalse(_nav.IsWalkable(MapId, new Vec2(1, 1)));

            _nav.SetBlocking(MapId, new[] { new Rect(new Vec2(10, 10), new Vec2(12, 12)) });

            Assert.IsTrue(_nav.IsWalkable(MapId, new Vec2(1, 1)), "第二次 SetBlocking 应整批替换，不是追加");
            Assert.IsFalse(_nav.IsWalkable(MapId, new Vec2(11, 11)));
        }

        [Test]
        public void Clear_ContractMethod_RemovesAllBlockingForMap()
        {
            _nav.SetBlocking(MapId, new[] { new Rect(new Vec2(0, 0), new Vec2(2, 2)) });

            _nav.Clear(MapId);

            Assert.IsTrue(_nav.IsWalkable(MapId, new Vec2(1, 1)));
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
            // （上下两条横贯整个宽度，覆盖四角，避免 8 方向寻路从对角缝隙穿出）。一次性经
            // SetBlocking 整批登记（不再是逐条 RegisterBlockingRect 追加）。
            _nav.SetBlocking(MapId, new[]
            {
                new Rect(new Vec2(-6, 4), new Vec2(6, 6)),    // 上（含四角）
                new Rect(new Vec2(-6, -6), new Vec2(6, -4)),  // 下（含四角）
                new Rect(new Vec2(-6, -4), new Vec2(-4, 4)),  // 左
                new Rect(new Vec2(4, -4), new Vec2(6, 4)),    // 右
            });
            _nav.BuildNavMesh(MapId);

            var path = _nav.FindPath(MapId, new Vec2(0, 0), new Vec2(10, 10));

            Assert.IsNull(path);
        }

        [Test]
        public void Raycast_HitsBlockingRect_ReturnsEntryPoint()
        {
            _nav.SetBlocking(MapId, new[] { new Rect(new Vec2(2, -1), new Vec2(4, 1)) });

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
