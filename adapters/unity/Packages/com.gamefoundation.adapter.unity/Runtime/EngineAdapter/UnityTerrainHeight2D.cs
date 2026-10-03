#nullable enable
// UnityTerrainHeight2D：ITerrainHeight2D 的 Unity 物理射线实现（ADR-0130 追加决定"地形竖直阻挡"，竖直轴能力包补完 M4-V）。
//
// 用法（可选启用）：游戏在装配期把它交给 VerticalAxisOptions.Terrain——不赋值就没有地形能力（地面恒为 0，行为与 1.95.0 一致）。
// 数据驱动的地形（world.map.terrain）由核心层的 MapTerrainHeights 提供；本类型适合"高度来自场景里的碰撞体"的游戏（斜坡、台阶、
// 多层平台随美术摆放，不重复写一份数据）。两者实现同一接口，二选一或在游戏侧自行组合。
//
// 坐标约定：逻辑平面 (x, y) 映射到 Unity 世界的 (x, z)，高度对应 Unity 的 y（与 UnityRenderer2D/UnityRenderer3D 的高度偏移同一约定）。
//
// 判断记录：
//   1) 地面 = 自上而下的射线命中的第一个碰撞体表面（GroundMask 过滤）。射线起点高度是 ProbeHeight（缺省 100 世界单位）：
//      高于所有可站立表面、低于需要穿过的顶棚；天花板/顶棚这类"头顶的东西"请放到单独的 Layer，由 CeilingMask 选中，
//      不要放进 GroundMask，否则射线会把它当成地面。
//   2) 天花板 = 从该点地面高度起向上的射线命中 CeilingMask 里的第一个碰撞体的底面；CeilingMask 缺省为 0（没有天花板）。
//   3) 没有命中（脚下没有任何地面碰撞体）返回 0——与"没有地形能力"的平地一致，不返回负无穷，避免单位掉出世界。
//   4) mapId 区分（M4-W1a，ADR-0130 追加决定"地形查询按地图隔离"）：一张地图的地形碰撞体可以放在
//      a. 自己的 PhysicsScene 里（BindMap(mapId, physicsScene)：查询只打到那个物理场景，两张地图在世界坐标上完全重叠也互不可见——
//         Unity 官方的物理隔离手段，推荐用于"同一时刻有多张地图同时存在"的游戏）；
//      b. 同一物理场景的某个根物体之下（BindMapRoot(mapId, root)：查询取自上而下命中的第一个属于该根（含子孙）的碰撞体，别的根下的碰撞体即使
//         更高也被穿过；适合地图只是场景里的不同区域/层的游戏）；
//      c. 按 Layer 掩码区分（GroundMaskForMap/CeilingMaskForMap，既有）。
//      优先级 a > b > c；没有绑定的地图走 c（缺省 = 全局掩码，与 1.95.0 一致）。<see cref="StrictMaps"/> 为真时，没有任何绑定的地图
//      看不到地形（地面 0），防止未登记的地图读到别的地图的碰撞体。
//   5) 只读：本类型不创建、不移动、不销毁任何物体，也不写物理世界。查询是主线程同步调用，没有分配（RaycastNonAlloc 不需要，
//      单射线取第一个命中即可）。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityTerrainHeight2D : ITerrainHeight2D
    {
        /// <summary>射线起点离地面探测平面的偏移，避免起点落在地面碰撞体内部时漏检（天花板射线用）。</summary>
        public const double GroundSkin = 0.01;

        /// <summary>地面射线的起点高度（世界单位）。</summary>
        public double ProbeHeight { get; set; } = 100.0;

        /// <summary>射线最大长度（世界单位）。</summary>
        public double MaxProbeDistance { get; set; } = 1000.0;

        /// <summary>地面碰撞体所在 Layer 掩码（缺省 Unity 的默认射线层）。</summary>
        public int GroundMask { get; set; } = Physics.DefaultRaycastLayers;

        /// <summary>天花板碰撞体所在 Layer 掩码；0（缺省）= 没有天花板。</summary>
        public int CeilingMask { get; set; }

        /// <summary>按地图 id 取地面 Layer 掩码（可空；非空时覆盖 <see cref="GroundMask"/>）。</summary>
        public System.Func<Id, int>? GroundMaskForMap { get; set; }

        /// <summary>按地图 id 取天花板 Layer 掩码（可空；非空时覆盖 <see cref="CeilingMask"/>）。</summary>
        public System.Func<Id, int>? CeilingMaskForMap { get; set; }

        /// <summary>是否让触发器碰撞体参与（缺省不参与）。</summary>
        public QueryTriggerInteraction Triggers { get; set; } = QueryTriggerInteraction.Ignore;

        /// <summary>为真时，没有 <see cref="BindMap"/>/<see cref="BindMapRoot"/>/掩码钩子覆盖的地图看不到任何地形（地面 0、没有天花板）。缺省假（沿用全局掩码）。</summary>
        public bool StrictMaps { get; set; }

        private readonly Dictionary<Id, PhysicsScene> _scenes = new Dictionary<Id, PhysicsScene>();
        private readonly Dictionary<Id, Transform> _roots = new Dictionary<Id, Transform>();
        private RaycastHit[] _hitBuffer = new RaycastHit[16];

        /// <summary>把一张地图绑定到它自己的物理场景：该地图的地形查询只打到这个场景里的碰撞体。</summary>
        public void BindMap(Id mapId, PhysicsScene scene)
        {
            if (!scene.IsValid())
            {
                throw new ArgumentException("物理场景无效", nameof(scene));
            }

            _scenes[mapId] = scene;
            _roots.Remove(mapId);
        }

        /// <summary>把一张地图绑定到场景里的一个根物体：该地图的地形查询只认根（含子孙）之下的碰撞体。</summary>
        public void BindMapRoot(Id mapId, Transform root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            _roots[mapId] = root;
            _scenes.Remove(mapId);
        }

        /// <summary>解除一张地图的绑定（回到掩码/全局规则）。</summary>
        public void UnbindMap(Id mapId)
        {
            _scenes.Remove(mapId);
            _roots.Remove(mapId);
        }

        private bool HasBinding(Id mapId) =>
            _scenes.ContainsKey(mapId) || _roots.ContainsKey(mapId) || GroundMaskForMap != null || CeilingMaskForMap != null;

        /// <summary>按地图的绑定规则打一条射线，取该地图可见的第一个命中。</summary>
        private bool Cast(Id mapId, Vector3 origin, Vector3 direction, int mask, out RaycastHit best)
        {
            var max = (float)MaxProbeDistance;
            if (_scenes.TryGetValue(mapId, out var scene))
            {
                return scene.Raycast(origin, direction, out best, max, mask, Triggers);
            }

            if (_roots.TryGetValue(mapId, out var root))
            {
                if (root == null)
                {
                    best = default;
                    return false; // 根已被销毁：该地图没有地形
                }

                int count;
                while (true)
                {
                    count = Physics.RaycastNonAlloc(origin, direction, _hitBuffer, max, mask, Triggers);
                    if (count < _hitBuffer.Length) break;
                    _hitBuffer = new RaycastHit[_hitBuffer.Length * 2]; // 缓冲装满可能漏掉更近的命中：扩容重打
                }

                var found = false;
                best = default;
                for (var i = 0; i < count; i++)
                {
                    var hit = _hitBuffer[i];
                    if (!hit.collider.transform.IsChildOf(root)) continue;
                    if (!found || hit.distance < best.distance)
                    {
                        best = hit;
                        found = true;
                    }
                }

                return found;
            }

            if (StrictMaps && !HasBinding(mapId))
            {
                best = default;
                return false;
            }

            return Physics.Raycast(origin, direction, out best, max, mask, Triggers);
        }

        public double GetGroundHeight(Id mapId, Vec2 point)
        {
            var mask = GroundMaskForMap != null ? GroundMaskForMap(mapId) : GroundMask;
            var origin = new Vector3((float)point.X, (float)ProbeHeight, (float)point.Y);
            return Cast(mapId, origin, Vector3.down, mask, out var hit) ? hit.point.y : 0.0;
        }

        public double GetCeilingHeight(Id mapId, Vec2 point)
        {
            var mask = CeilingMaskForMap != null ? CeilingMaskForMap(mapId) : CeilingMask;
            if (mask == 0)
            {
                return double.PositiveInfinity;
            }

            var ground = GetGroundHeight(mapId, point);
            var origin = new Vector3((float)point.X, (float)(ground + GroundSkin), (float)point.Y);
            return Cast(mapId, origin, Vector3.up, mask, out var hit) ? hit.point.y : double.PositiveInfinity;
        }
    }
}
