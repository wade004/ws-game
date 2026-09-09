#nullable enable
using System;
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

        // 审计 architecture/落地计划/audit-6739f50-20260909/AUDIT_REPORT.md SPATIAL-111-01 的复现
        // 测试（见 presentation/spatial-probe-v2.log 同名场景）：审计副本
        // Audit6739f50SpatialQueryProbes.QueryRadius_CrossesBucketEdge_UsesEntryRadiusInCandidateSelection
        // 记录了这条"观察到的候选缺陷现象"，这里按既有惯例改写为断言根治后正确行为的正式回归用例，
        // 并补齐边界/移动/注销三个场景，纳入本模块基线。内部分桶尺寸（BucketSize=4.0，私有常量）
        // 由查询中心 3.5 与实体中心 4.1 横跨桶边界（3.5 在桶 0，4.1 在桶 1）这一事实反推得出，
        // 不直接引用私有常量。

        [Test]
        public void SPATIAL111_01_QueryRadius_EntityAcrossBucketEdge_LargeSelfRadius_IsIncluded()
        {
            // 复现：查询中心 (3.5,0) 半径 0.1；实体中心 (4.1,0) 自身半径 0.7，圆心距 0.6，满足
            // 0.1+0.7=0.8 的判定阈值——但实体所在的桶（4.1 落在下一个分桶）在候选桶只按查询半径
            // 0.1（不含实体自身半径）扩张时完全没被枚举到，根治前 QueryRadius 会漏掉这个实体。
            var entity = new Id("audit.spatial111_01.bucket_edge");
            _query.Register(entity, new Vec2(4.1, 0), 0.7);
            var center = new Vec2(3.5, 0);
            var radius = 0.1;

            var radiusResult = _query.QueryRadius(center, radius, QueryFilter.None);
            var rectOracle = _query.QueryRect(new Vec2(3.4, -0.1), new Vec2(3.6, 0.1), QueryFilter.None);

            Assert.That(rectOracle, Does.Contain(entity),
                "前置条件：QueryRect（本身不分桶，逐个遍历）确认该实体确实与查询邻域重叠");
            Assert.That(radiusResult, Does.Contain(entity),
                "QueryRadius 的候选桶范围必须按查询半径 + 索引内最大实体半径扩张，不能漏掉跨桶边界的大半径实体");
        }

        [Test]
        public void SPATIAL111_01_QueryCone_EntityAcrossBucketEdge_LargeSelfRadius_IsIncluded()
        {
            // 同一根因的 QueryCone 回归：IsInCone 的判定同样是 distance <= range + entry.Radius，
            // 候选桶范围需要同步扩张。锥形沿 +X 方向、半张角覆盖到实体方向，range 与半径搭配同
            // QueryRadius 用例。
            var entity = new Id("audit.spatial111_01.cone_bucket_edge");
            _query.Register(entity, new Vec2(4.1, 0), 0.7);
            var origin = new Vec2(3.5, 0);

            var coneResult = _query.QueryCone(origin, direction: 0.0, angle: Math.PI, range: 0.1, QueryFilter.None);

            Assert.That(coneResult, Does.Contain(entity),
                "QueryCone 的候选桶范围同样必须按查询半径 + 索引内最大实体半径扩张");
        }

        [Test]
        public void SPATIAL111_01_QueryRadius_AfterUpdatePositionAcrossBucket_StillFound()
        {
            // 移动场景：实体最初登记在与查询中心同一个桶内（能被找到），UpdatePosition 把它移动到
            // 跨桶边界、自身半径覆盖查询邻域的位置——候选桶扩张必须对移动后的新位置同样生效，
            // 不是只在 Register 时算一次。
            var entity = new Id("audit.spatial111_01.moved_across_bucket");
            _query.Register(entity, new Vec2(3.5, 0), 0.7);
            var center = new Vec2(3.5, 0);
            var radius = 0.1;
            Assert.That(_query.QueryRadius(center, radius, QueryFilter.None), Does.Contain(entity));

            _query.UpdatePosition(entity, new Vec2(4.1, 0));

            Assert.That(_query.QueryRadius(center, radius, QueryFilter.None), Does.Contain(entity),
                "UpdatePosition 把实体移到跨桶边界之后，候选桶扩张仍应覆盖到它");
        }

        [Test]
        public void SPATIAL111_01_QueryRadius_AfterUnregister_ExcludedEvenThoughWithinExpandedRange()
        {
            // 注销场景：候选桶扩张不应让"已经注销"的对象死灰复燃——Unregister 之后即便另一个仍在
            // 场的大半径实体把候选范围撑得很宽，被注销对象本身也绝不应该出现在结果里。
            var stillRegistered = new Id("audit.spatial111_01.still_registered");
            var unregistered = new Id("audit.spatial111_01.unregistered");
            _query.Register(stillRegistered, new Vec2(4.1, 0), 0.7);
            _query.Register(unregistered, new Vec2(4.1, 0.2), 0.7);
            var center = new Vec2(3.5, 0);
            var radius = 0.1;
            Assert.That(_query.QueryRadius(center, radius, QueryFilter.None), Does.Contain(unregistered));

            _query.Unregister(unregistered);
            var result = _query.QueryRadius(center, radius, QueryFilter.None);

            Assert.That(result, Does.Contain(stillRegistered), "仍在场、跨桶边界的大半径实体应继续被查到");
            CollectionAssert.DoesNotContain(result, unregistered, "已注销的对象不应再出现在候选桶扩张后的结果里");
        }
    }
}
