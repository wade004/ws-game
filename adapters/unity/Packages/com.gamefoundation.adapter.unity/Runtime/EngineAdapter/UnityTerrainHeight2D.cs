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
//   4) mapId 不参与物理查询（Unity 物理世界按 PhysicsScene 而不是按逻辑地图划分）；同一物理场景里叠放多张地图时，
//      用 GroundMaskForMap/CeilingMaskForMap 按地图 id 返回各自的 Layer 掩码来区分。
//   5) 只读：本类型不创建、不移动、不销毁任何物体，也不写物理世界。查询是主线程同步调用，没有分配（RaycastNonAlloc 不需要，
//      单射线取第一个命中即可）。
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

        public double GetGroundHeight(Id mapId, Vec2 point)
        {
            var mask = GroundMaskForMap != null ? GroundMaskForMap(mapId) : GroundMask;
            var origin = new Vector3((float)point.X, (float)ProbeHeight, (float)point.Y);
            return Physics.Raycast(origin, Vector3.down, out var hit, (float)MaxProbeDistance, mask, Triggers)
                ? hit.point.y
                : 0.0;
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
            return Physics.Raycast(origin, Vector3.up, out var hit, (float)MaxProbeDistance, mask, Triggers)
                ? hit.point.y
                : double.PositiveInfinity;
        }
    }
}
