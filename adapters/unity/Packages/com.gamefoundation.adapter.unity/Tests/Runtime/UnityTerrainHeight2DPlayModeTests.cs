#nullable enable
// UnityTerrainHeight2DPlayModeTests：地面高度的物理射线实现（ADR-0130 追加决定"地形竖直阻挡"，竖直轴能力包补完 M4-V）的引擎侧冒烟。
//
// 验收对象（读运行时可观测量，期望值由几何规则算出，不写死裸数）：
//   一、平台：射线读到的地面高度 = 碰撞体上表面高度；平台之外 = 0；
//   二、斜坡：旋转的盒子，地面高度 = 上表面直线方程在该点的值；
//   三、天花板：CeilingMask 选中的碰撞体底面高度；没配 CeilingMask / 头顶无物 = +inf；
//   四、与核心竖直运动服务联动：被抛起的单位落在平台上表面（而不是 0）、撞天花板后速度清零且不越过；
//   五、按地图 id 的掩码钩子：不同地图看到不同地面；
//   六、按地图 id 隔离（M4-W1a）：两张地图的碰撞体在世界坐标上完全重叠，各自读到自己的地面/天花板、互不串——
//      a. 每张地图一个 PhysicsScene（BindMap）；b. 同一物理场景里每张地图一个根物体（BindMapRoot）；c. StrictMaps 下未绑定的地图看不到地形。
//
// 判断记录（地面与天花板各用一个专属 Layer，同 CombatStanceAnimFixture 的"项目 TagManager 里 8~31 全部未使用"）：
// 本类型只创建没有渲染器的碰撞体，不涉及渲染隔离；用 Layer 29/30 把本套件的碰撞体与其它用例（默认层）隔开，
// 掩码显式指定，不依赖场景里是否碰巧还留着别的碰撞体。碰撞体用例结束即销毁。
using System;
using System.Collections;
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

using Vec2 = Core.Foundation.Common.Vec2;

namespace Adapter.Unity.Tests.Runtime
{
    [Category("module:unit")]
    [Category("interaction:vertical_terrain")]
    public sealed class UnityTerrainHeight2DPlayModeTests : PlayModeTestBase
    {
        private const int GroundLayer = 29;
        private const int CeilingLayer = 30;
        private const double Tolerance = 1e-3; // 射线命中点是 float
        private static readonly Id MapId = new Id("map.vertical_terrain_test");

        private readonly List<GameObject> _created = new List<GameObject>();

        private readonly List<Scene> _scenes = new List<Scene>();

        [TearDown]
        public void DestroyColliders()
        {
            foreach (var scene in _scenes)
            {
                if (scene.IsValid()) SceneManager.UnloadSceneAsync(scene);
            }
            _scenes.Clear();

            foreach (var go in _created)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        private GameObject Box(string name, int layer, Vector3 center, Vector3 size, Quaternion? rotation = null)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.position = center;
            go.transform.rotation = rotation ?? Quaternion.identity;
            var collider = go.AddComponent<BoxCollider>();
            collider.size = size;
            _created.Add(go);
            return go;
        }

        private static UnityTerrainHeight2D Terrain() => new UnityTerrainHeight2D
        {
            GroundMask = 1 << GroundLayer,
            CeilingMask = 0,
        };

        /// <summary>创建碰撞体后等一个物理步，保证物理世界已同步。</summary>
        private static IEnumerator Settle()
        {
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
        }

        // ---------- 一、平台 ----------

        [UnityTest]
        public IEnumerator Ground_PlatformTopSurface_IsTheGroundHeight_AndOutsideIsZero()
        {
            // 盒子中心 (7.5, 1, 0)，尺寸 (5, 2, 10)：上表面 y = 1 + 2/2 = 2，覆盖 x∈[5,10]、逻辑 y(= Unity z)∈[-5,5]。
            Box("platform", GroundLayer, new Vector3(7.5f, 1f, 0f), new Vector3(5f, 2f, 10f));
            yield return Settle();
            var terrain = Terrain();

            Assert.AreEqual(2.0, terrain.GetGroundHeight(MapId, new Vec2(7, 0)), Tolerance);
            Assert.AreEqual(2.0, terrain.GetGroundHeight(MapId, new Vec2(5.5, 4.5)), Tolerance);
            Assert.AreEqual(0.0, terrain.GetGroundHeight(MapId, new Vec2(2, 0)), "平台之外没有地面碰撞体：与没有地形能力一致，地面 0");
            Assert.AreEqual(0.0, terrain.GetGroundHeight(MapId, new Vec2(7, 8)), "逻辑平面的 y 对应 Unity 的 z，超出盒子范围");
        }

        [UnityTest]
        public IEnumerator Ground_IgnoresCollidersOutsideTheGroundMask()
        {
            Box("platform", GroundLayer, new Vector3(7.5f, 1f, 0f), new Vector3(5f, 2f, 10f));
            Box("roof", CeilingLayer, new Vector3(7.5f, 9f, 0f), new Vector3(5f, 2f, 10f)); // 顶棚：底面 8，上表面 10
            yield return Settle();

            // 地面射线从 100 高处自上而下：若顶棚被当成地面会读到 10。
            Assert.AreEqual(2.0, Terrain().GetGroundHeight(MapId, new Vec2(7, 0)), Tolerance);
        }

        // ---------- 二、斜坡 ----------

        [UnityTest]
        public IEnumerator Ground_RotatedBox_FollowsTheSurfaceLineEquation()
        {
            // 斜坡：厚 0.2、长 10 的盒子绕 z 轴转 θ（tanθ = 0.5），中心 (20, 1, 0)。
            // 上表面是过 "中心 + 法线 × 半厚" 的直线：y(x) = cy + (halfThickness + (x − cx)·sinθ) / cosθ。
            var theta = Math.Atan(0.5);
            Box("ramp", GroundLayer, new Vector3(20f, 1f, 0f), new Vector3(10f, 0.2f, 10f), Quaternion.Euler(0f, 0f, (float)(theta * 180.0 / Math.PI)));
            yield return Settle();
            var terrain = Terrain();

            foreach (var x in new[] { 18.0, 20.0, 22.0 })
            {
                var expected = 1.0 + (0.1 + (x - 20.0) * Math.Sin(theta)) / Math.Cos(theta);
                Assert.AreEqual(expected, terrain.GetGroundHeight(MapId, new Vec2(x, 0)), Tolerance, $"x={x}");
            }
        }

        // ---------- 三、天花板 ----------

        [UnityTest]
        public IEnumerator Ceiling_IsTheUndersideOfTheCeilingCollider_AndInfiniteWithoutOne()
        {
            Box("platform", GroundLayer, new Vector3(7.5f, 1f, 0f), new Vector3(5f, 2f, 10f));
            Box("roof", CeilingLayer, new Vector3(7.5f, 7f, 0f), new Vector3(5f, 2f, 10f)); // 底面 y = 7 - 1 = 6
            yield return Settle();

            var withCeiling = Terrain();
            withCeiling.CeilingMask = 1 << CeilingLayer;
            Assert.AreEqual(6.0, withCeiling.GetCeilingHeight(MapId, new Vec2(7, 0)), Tolerance);
            Assert.IsTrue(double.IsPositiveInfinity(withCeiling.GetCeilingHeight(MapId, new Vec2(2, 0))), "头顶无物");

            Assert.IsTrue(double.IsPositiveInfinity(Terrain().GetCeilingHeight(MapId, new Vec2(7, 0))), "没配 CeilingMask：没有天花板");
        }

        // ---------- 四、与竖直运动服务联动 ----------

        private sealed class Rig
        {
            public WorldSim World = null!;
            public PlayerUnit Hero = null!;
            public VerticalMotionHost Host = null!;
        }

        private static Rig BuildRig(UnityTerrainHeight2D terrain, Vec2 position)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var hero = new PlayerUnit(new Id("unit.vt_hero"), MapId, new Id("fac.vt"), new Id("arch.class.sample")) { Position = position };
            world.AddEntity(hero);
            var host = new VerticalMotionHost(world, new VerticalAxisOptions { Gravity = 24.0, Terrain = terrain });
            return new Rig { World = world, Hero = hero, Host = host };
        }

        [UnityTest]
        public IEnumerator Launch_LandsOnThePlatformTopSurface_NotOnZero()
        {
            Box("platform", GroundLayer, new Vector3(7.5f, 1f, 0f), new Vector3(5f, 2f, 10f));
            yield return Settle();
            var rig = BuildRig(Terrain(), new Vec2(7, 0));

            rig.Host.Advance(1.0 / 60.0); // 首次观测：脚下低于地面，抬到平台
            Assert.AreEqual(2.0, rig.Hero.HeightOffset, Tolerance);

            Assert.IsTrue(rig.Host.LaunchToApex(rig.Hero.EntityId, 1.5));
            var peak = 0.0;
            for (var i = 0; i < 600 && rig.Host.IsAirborne(rig.Hero.EntityId); i++)
            {
                rig.Host.Advance(1.0 / 60.0);
                peak = Math.Max(peak, rig.Hero.HeightOffset);
            }

            Assert.IsFalse(rig.Host.IsAirborne(rig.Hero.EntityId));
            Assert.AreEqual(2.0, rig.Hero.HeightOffset, Tolerance, "落在平台上表面（2），不是 0");
            Assert.AreEqual(3.5, peak, 0.05, "顶点 = 平台高度 + 抛起顶点高度（离散步至多亏欠半步）");
        }

        [UnityTest]
        public IEnumerator Launch_HitsTheCeiling_ZeroesTheRisingSpeedAndNeverPassesIt()
        {
            Box("platform", GroundLayer, new Vector3(7.5f, 1f, 0f), new Vector3(5f, 2f, 10f));
            Box("roof", CeilingLayer, new Vector3(7.5f, 7f, 0f), new Vector3(5f, 2f, 10f)); // 底面 6
            yield return Settle();
            var terrain = Terrain();
            terrain.CeilingMask = 1 << CeilingLayer;
            var rig = BuildRig(terrain, new Vec2(7, 0));
            rig.Host.Advance(1.0 / 60.0);

            rig.Host.LaunchToApex(rig.Hero.EntityId, 20.0); // 不受限的顶点远高于天花板
            var hit = false;
            for (var i = 0; i < 1000 && rig.Host.IsAirborne(rig.Hero.EntityId); i++)
            {
                rig.Host.Advance(1.0 / 60.0);
                Assert.LessOrEqual(rig.Hero.HeightOffset, 6.0 + Tolerance, "脚下不得越过天花板底面");
                if (!hit && Math.Abs(rig.Hero.HeightOffset - 6.0) < Tolerance)
                {
                    hit = true;
                    Assert.AreEqual(0.0, rig.Host.GetVerticalSpeed(rig.Hero.EntityId), 1e-9, "撞顶那一刻竖直速度清零");
                }
            }

            Assert.IsTrue(hit);
            Assert.AreEqual(2.0, rig.Hero.HeightOffset, Tolerance);
        }

        // ---------- 五、按地图 id 的掩码钩子 ----------

        [UnityTest]
        public IEnumerator PerMapMask_LetsDifferentMapsSeeDifferentGround()
        {
            Box("platform", GroundLayer, new Vector3(7.5f, 1f, 0f), new Vector3(5f, 2f, 10f));
            yield return Settle();
            var visible = new Id("map.visible");
            var hidden = new Id("map.hidden");
            var terrain = Terrain();
            terrain.GroundMaskForMap = map => map.Equals(visible) ? 1 << GroundLayer : 0;

            Assert.AreEqual(2.0, terrain.GetGroundHeight(visible, new Vec2(7, 0)), Tolerance);
            Assert.AreEqual(0.0, terrain.GetGroundHeight(hidden, new Vec2(7, 0)), "该地图的掩码选不中任何碰撞体：地面 0");
        }

        // ---------- 六、按地图 id 隔离（M4-W1a） ----------

        private static readonly Id MapOne = new Id("map.iso_one");
        private static readonly Id MapTwo = new Id("map.iso_two");

        /// <summary>在 <paramref name="parent"/>（可空）之下造一个盒子碰撞体；位置先设后加碰撞体，保证物理世界立即登记。</summary>
        private GameObject BoxUnder(string name, Transform? parent, int layer, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name) { layer = layer };
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
            _created.Add(go);
            return go;
        }

        private Scene NewPhysicsScene(string name)
        {
            var scene = SceneManager.CreateScene(name, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _scenes.Add(scene);
            return scene;
        }

        private GameObject BoxInScene(Scene scene, string name, int layer, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name) { layer = layer };
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
            _created.Add(go);
            return go;
        }

        [UnityTest]
        public IEnumerator PerMapPhysicsScene_TwoOverlappingMaps_EachSeeOnlyTheirOwnGroundAndCeiling()
        {
            var sceneOne = NewPhysicsScene("iso_one");
            var sceneTwo = NewPhysicsScene("iso_two");
            // 同一 (x, z) 处：地图一的平台上表面 2、地图二的平台上表面 5；地图二另有顶棚（底面 9）。
            BoxInScene(sceneOne, "one_platform", GroundLayer, new Vector3(7.5f, 1f, 0f), new Vector3(5f, 2f, 10f));
            BoxInScene(sceneTwo, "two_platform", GroundLayer, new Vector3(7.5f, 2.5f, 0f), new Vector3(5f, 5f, 10f));
            BoxInScene(sceneTwo, "two_roof", CeilingLayer, new Vector3(7.5f, 10f, 0f), new Vector3(5f, 2f, 10f));
            yield return Settle();

            var terrain = Terrain();
            terrain.CeilingMask = 1 << CeilingLayer;
            terrain.BindMap(MapOne, sceneOne.GetPhysicsScene());
            terrain.BindMap(MapTwo, sceneTwo.GetPhysicsScene());

            var p = new Vec2(7, 0);
            Assert.AreEqual(2.0, terrain.GetGroundHeight(MapOne, p), Tolerance, "地图一读到自己的平台，不受地图二更高的平台影响");
            Assert.AreEqual(5.0, terrain.GetGroundHeight(MapTwo, p), Tolerance, "地图二读到自己的平台");
            Assert.IsTrue(double.IsPositiveInfinity(terrain.GetCeilingHeight(MapOne, p)), "地图二的顶棚不属于地图一");
            Assert.AreEqual(9.0, terrain.GetCeilingHeight(MapTwo, p), Tolerance);

            // 解除绑定后回到全局规则（默认物理场景里没有这些碰撞体）：地面 0。
            terrain.UnbindMap(MapOne);
            Assert.AreEqual(0.0, terrain.GetGroundHeight(MapOne, p), Tolerance);
        }

        [UnityTest]
        public IEnumerator PerMapRoot_TwoMapsInOneScene_EachSeeOnlyCollidersUnderTheirOwnRoot()
        {
            var rootOne = new GameObject("iso_root_one");
            var rootTwo = new GameObject("iso_root_two");
            _created.Add(rootOne);
            _created.Add(rootTwo);
            BoxUnder("one_platform", rootOne.transform, GroundLayer, new Vector3(7.5f, 1f, 0f), new Vector3(5f, 2f, 10f));
            BoxUnder("two_platform", rootTwo.transform, GroundLayer, new Vector3(7.5f, 2.5f, 0f), new Vector3(5f, 5f, 10f)); // 上表面 5，更高
            yield return Settle();

            var terrain = Terrain();
            terrain.BindMapRoot(MapOne, rootOne.transform);
            terrain.BindMapRoot(MapTwo, rootTwo.transform);

            var p = new Vec2(7, 0);
            Assert.AreEqual(2.0, terrain.GetGroundHeight(MapOne, p), Tolerance, "地图二的平台更高、先被射线命中，但不属于地图一的根：被穿过");
            Assert.AreEqual(5.0, terrain.GetGroundHeight(MapTwo, p), Tolerance);
            Assert.AreEqual(0.0, terrain.GetGroundHeight(MapOne, new Vec2(2, 0)), "根之下没有碰撞体的位置：地面 0");

            // 不绑定的地图（非严格）：沿用全局掩码，读到最高的那个（与 1.95.0 一致）。
            Assert.AreEqual(5.0, terrain.GetGroundHeight(new Id("map.iso_unbound"), p), Tolerance);
        }

        [UnityTest]
        public IEnumerator StrictMaps_AMapWithoutAnyBinding_SeesNoTerrain()
        {
            var root = new GameObject("iso_root_strict");
            _created.Add(root);
            BoxUnder("platform", root.transform, GroundLayer, new Vector3(7.5f, 1f, 0f), new Vector3(5f, 2f, 10f));
            yield return Settle();

            var terrain = Terrain();
            terrain.StrictMaps = true;
            terrain.BindMapRoot(MapOne, root.transform);
            var p = new Vec2(7, 0);
            Assert.AreEqual(2.0, terrain.GetGroundHeight(MapOne, p), Tolerance);
            Assert.AreEqual(0.0, terrain.GetGroundHeight(MapTwo, p), "严格模式：没有任何绑定的地图看不到别的地图的碰撞体");
        }

        [UnityTest]
        public IEnumerator PerMapRoot_DestroyedRoot_MeansNoTerrainForThatMap()
        {
            var root = new GameObject("iso_root_gone");
            BoxUnder("platform", root.transform, GroundLayer, new Vector3(7.5f, 1f, 0f), new Vector3(5f, 2f, 10f));
            yield return Settle();
            var terrain = Terrain();
            terrain.BindMapRoot(MapOne, root.transform);
            Assert.AreEqual(2.0, terrain.GetGroundHeight(MapOne, new Vec2(7, 0)), Tolerance);
            UnityEngine.Object.DestroyImmediate(root);
            Assert.AreEqual(0.0, terrain.GetGroundHeight(MapOne, new Vec2(7, 0)));
        }
    }
}
