using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Presentation.Common;
using Xunit;

namespace Tests.PresentationCommon
{
    /// <summary>
    /// 测试覆盖剩余项 T-M32：<see cref="WorldSimSnapshot"/> 的 <c>GetRawKind</c>、<c>GetAllEntityIds</c>
    /// （<c>ViewBinder</c> 读档对账依赖）直接测试，以及 <c>GetFacing</c>/<c>GetHeight</c> 缺实体抛异常的契约。
    /// </summary>
    public class WorldSimSnapshotQueryTests
    {
        private static readonly Id MapId = new Id("map.test");

        [Fact]
        public void GetRawKind_ReturnsEntityKindVerbatim_IncludingKindsNotMappedToViewKind()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            var mapped = new TestEntity(new Id("gobj.chest"), MapId, EntityKinds.Gobj);
            var unmapped = new TestEntity(new Id("x.odd"), MapId, "some_future_kind");
            world.AddEntity(mapped);
            world.AddEntity(unmapped);

            var snapshot = new WorldSimSnapshot(world);

            Assert.Equal(mapped.Kind, snapshot.GetRawKind(mapped.EntityId));
            // GetKind 对未登记类型返回 null，GetRawKind 仍应原样给出原始字符串（读档对账靠它区分“未知类型”与“不存在”）。
            Assert.Null(snapshot.GetKind(unmapped.EntityId));
            Assert.Equal(unmapped.Kind, snapshot.GetRawKind(unmapped.EntityId));
        }

        [Fact]
        public void GetRawKind_UnknownEntity_ReturnsNull()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);

            Assert.Null(new WorldSimSnapshot(world).GetRawKind(new Id("unit.missing")));
        }

        [Fact]
        public void GetRawKind_AfterImmediateRemoval_ReturnsNull()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            var entity = new TestEntity(new Id("unit.a"), MapId);
            world.AddEntity(entity);
            var snapshot = new WorldSimSnapshot(world);
            Assert.NotNull(snapshot.GetRawKind(entity.EntityId));

            Assert.True(world.RemoveEntityImmediately(entity.EntityId));

            Assert.Null(snapshot.GetRawKind(entity.EntityId));
        }

        [Fact]
        public void GetAllEntityIds_EmptyWorld_ReturnsEmptyList()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);

            var ids = new WorldSimSnapshot(world).GetAllEntityIds();

            Assert.NotNull(ids);
            Assert.Empty(ids);
        }

        [Fact]
        public void GetAllEntityIds_ReturnsEveryEntityOfEveryKind_InOrdinalIdOrder()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            // 故意乱序、跨类型加入：规则 = 不过滤任何类型，顺序同 IWorldSim.QueryEntities（EntityId 序数升序）。
            var added = new[] { "unit.z", "gobj.m", "loot.b", "unit.a", "unmapped.q" };
            foreach (var id in added)
            {
                world.AddEntity(new TestEntity(new Id(id), MapId, id.Split('.')[0]));
            }

            var ids = new WorldSimSnapshot(world).GetAllEntityIds();

            var expected = added.OrderBy(s => s, StringComparer.Ordinal).Select(s => new Id(s)).ToArray();
            Assert.Equal(expected, ids);
        }

        [Fact]
        public void GetAllEntityIds_TracksWorldChanges_AndPriorResultIsNotALiveView()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            var a = new TestEntity(new Id("unit.a"), MapId);
            var b = new TestEntity(new Id("unit.b"), MapId);
            world.AddEntity(a);
            var snapshot = new WorldSimSnapshot(world);

            var before = snapshot.GetAllEntityIds();
            world.AddEntity(b);
            var afterAdd = snapshot.GetAllEntityIds();
            world.RemoveEntityImmediately(a.EntityId);
            var afterRemove = snapshot.GetAllEntityIds();

            Assert.Equal(new[] { a.EntityId }, before);
            Assert.Equal(new[] { a.EntityId, b.EntityId }, afterAdd);
            Assert.Equal(new[] { b.EntityId }, afterRemove);
        }

        [Fact]
        public void GetAllEntityIds_AgreesWithExists_ForEveryReturnedId()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            world.AddEntity(new TestEntity(new Id("unit.a"), MapId));
            world.AddEntity(new TestEntity(new Id("gobj.b"), MapId, "gobj"));
            var snapshot = new WorldSimSnapshot(world);

            var ids = snapshot.GetAllEntityIds();

            Assert.Equal(world.EntityCount, ids.Count);
            Assert.All(ids, id => Assert.True(snapshot.Exists(id)));
        }

        [Fact]
        public void GetFacing_UnknownEntity_ThrowsInvalidOperation_MentioningTheId()
        {
            var snapshot = new WorldSimSnapshot(PresentationCommonTestSupport.CreateWorld(out _));
            var missing = new Id("unit.missing");

            var ex = Assert.Throws<InvalidOperationException>(() => snapshot.GetFacing(missing));

            Assert.Contains(missing.Value, ex.Message);
        }

        [Fact]
        public void GetHeight_UnknownEntity_ThrowsInvalidOperation_MentioningTheId()
        {
            var snapshot = new WorldSimSnapshot(PresentationCommonTestSupport.CreateWorld(out _));
            var missing = new Id("unit.missing");

            var ex = Assert.Throws<InvalidOperationException>(() => snapshot.GetHeight(missing));

            Assert.Contains(missing.Value, ex.Message);
        }

        [Fact]
        public void PoseQueries_AfterEntityRemoved_Throw_WhileExistsReportsFalse()
        {
            var world = PresentationCommonTestSupport.CreateWorld(out _);
            var entity = new TestEntity(new Id("unit.a"), MapId) { Facing = 0.5 };
            world.AddEntity(entity);
            var snapshot = new WorldSimSnapshot(world);
            Assert.Equal(0.5, snapshot.GetFacing(entity.EntityId));

            world.RemoveEntityImmediately(entity.EntityId);

            Assert.False(snapshot.Exists(entity.EntityId));
            Assert.Throws<InvalidOperationException>(() => snapshot.GetFacing(entity.EntityId));
            Assert.Throws<InvalidOperationException>(() => snapshot.GetHeight(entity.EntityId));
            Assert.Throws<InvalidOperationException>(() => snapshot.GetPosition(entity.EntityId));
        }
    }
}
