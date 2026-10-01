using System;
using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// <see cref="INavigation2D.RaycastWithNormal"/> 的返回值：射线命中的第一个阻挡点，加上该处阻挡表面的法线。
    /// <para>
    /// <see cref="Point"/> 与 <see cref="INavigation2D.Raycast"/> 返回的点逐位相同（同一次判定的结果，实现不得给出两个不同的命中点）。
    /// <see cref="Normal"/> 是单位向量，**指向阻挡区域外侧**（即朝向射线起点所在的自由空间一侧）：射线从自由空间穿入阻挡区域内部，
    /// 法线就是被穿入的那个表面的外法线。沿墙滑动时，位移的法向分量 <c>v·Normal</c>（穿入时为负）被去掉，剩下的就是切向分量。
    /// </para>
    /// <para>
    /// 退化情形：射线恰好穿过两个表面的交线（凸角点，或两块阻挡矩形在同一点同时被命中的内角）时，法线取两个表面外法线之和的
    /// 归一化（对角方向）；射线起点本身已在阻挡内部、没有"穿入"表面可言，或实现不能确定法线时，<see cref="Normal"/> 为零向量——
    /// 调用方应当把零向量视为"无法滑动，整体停下"，不得自行猜测一个方向。
    /// </para>
    /// </summary>
    public readonly struct NavRayHit : IEquatable<NavRayHit>
    {
        /// <summary>第一个阻挡点（与 <see cref="INavigation2D.Raycast"/> 的返回值同一个点）。</summary>
        public Vec2 Point { get; }

        /// <summary>阻挡表面的单位外法线（指向自由空间一侧）；无法确定时为 <see cref="Vec2.Zero"/>。</summary>
        public Vec2 Normal { get; }

        public NavRayHit(Vec2 point, Vec2 normal)
        {
            Point = point;
            Normal = normal;
        }

        /// <summary>法线是否可用（非零向量）。</summary>
        public bool HasNormal => Normal.X != 0.0 || Normal.Y != 0.0;

        public bool Equals(NavRayHit other) => Point.Equals(other.Point) && Normal.Equals(other.Normal);

        public override bool Equals(object? obj) => obj is NavRayHit other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Point.GetHashCode() * 397) ^ Normal.GetHashCode();
            }
        }

        public override string ToString() => $"hit {Point} normal {Normal}";

        public static bool operator ==(NavRayHit left, NavRayHit right) => left.Equals(right);

        public static bool operator !=(NavRayHit left, NavRayHit right) => !left.Equals(right);
    }

    /// <summary>
    /// <see cref="INavigation2D.RaycastWithNormal"/> 的几何小工具：轴对齐矩形的穿入面法线、多个同距命中的法线合并、以及默认实现使用的
    /// 轴向探测近似。内置的两个实现（测试桩与引擎适配层）都经这里取法线，不各抄一份公式。
    /// </summary>
    public static class NavRaycastNormals
    {
        /// <summary>默认实现里"贴墙探测"的回退距离（世界单位），取与移动系统缺省到达容差相同的量级。</summary>
        public const double DefaultProbeBackoff = 0.01;

        private static readonly double InvSqrt2 = Math.Sqrt(0.5);

        /// <summary>
        /// 线段 [<paramref name="from"/>, <paramref name="to"/>] 穿入轴对齐矩形 [<paramref name="min"/>, <paramref name="max"/>] 时的
        /// 外法线。调用方保证线段确实穿入了该矩形（命中点由各实现自己的相交判定给出）。
        /// <para>
        /// 做法：按 slab 法算出两个轴各自的穿入参数 <c>t</c>（与实现的相交判定同一个算式，因此与命中点一致），最晚穿入的轴就是被穿入的
        /// 面——<c>dx &gt; 0</c> 穿入的是左面（法线 <c>(-1, 0)</c>），<c>dx &lt; 0</c> 穿入右面（<c>(+1, 0)</c>），y 轴同理；
        /// 两个轴同时穿入（恰好经过角点）时取两个面法线之和归一化。起点已在矩形内部（两个轴的穿入参数都小于零）时返回零向量。
        /// 返回的轴向法线分量恰为 <c>±1</c> 与 <c>0</c>（不经归一化运算），保证轴对齐阻挡下的滑墙结果与逐轴处理逐位一致。
        /// </para>
        /// </summary>
        public static Vec2 RectEntryNormal(Vec2 from, Vec2 to, Vec2 min, Vec2 max)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var hasX = Math.Abs(dx) >= double.Epsilon;
            var hasY = Math.Abs(dy) >= double.Epsilon;

            var enterX = double.NegativeInfinity;
            var enterY = double.NegativeInfinity;
            if (hasX) enterX = dx > 0.0 ? (min.X - from.X) / dx : (max.X - from.X) / dx;
            if (hasY) enterY = dy > 0.0 ? (min.Y - from.Y) / dy : (max.Y - from.Y) / dy;

            var tHit = Math.Max(Math.Max(0.0, enterX), enterY);
            var viaX = hasX && enterX == tHit;
            var viaY = hasY && enterY == tHit;
            var nx = dx > 0.0 ? -1.0 : 1.0;
            var ny = dy > 0.0 ? -1.0 : 1.0;

            if (viaX && viaY) return new Vec2(nx * InvSqrt2, ny * InvSqrt2);
            if (viaX) return new Vec2(nx, 0.0);
            if (viaY) return new Vec2(0.0, ny);
            return Vec2.Zero;
        }

        /// <summary>
        /// 合并同一命中点上多个表面的法线（两块阻挡矩形在同一点同时被命中的内角）：相同则原样返回；任一为零向量取另一个；
        /// 否则取和归一化，和为零（两个表面背对背）时返回零向量。
        /// </summary>
        public static Vec2 Merge(Vec2 a, Vec2 b)
        {
            if (a.X == 0.0 && a.Y == 0.0) return b;
            if (b.X == 0.0 && b.Y == 0.0) return a;
            if (a.Equals(b)) return a;

            var sum = a + b;
            var length = sum.Length;
            return length < 1e-9 ? Vec2.Zero : new Vec2(sum.X / length, sum.Y / length);
        }

        /// <summary>
        /// <see cref="INavigation2D.RaycastWithNormal"/> 默认实现的近似：只用 <see cref="INavigation2D.Raycast"/> 取法线——从命中点沿射线
        /// 回退一小段（避开命中点浮点误差落进阻挡内部），再沿射线方向的 x、y 两个轴各向前探一小段，恰有一个轴被挡即该轴为墙法向。
        /// 只对轴对齐阻挡成立；两轴都被挡（内角）、都不挡（擦角）、射线长度为零时返回零向量。不知道自己表面几何的第三方实现用它；
        /// 能给出精确法线的实现（网格实现、测试桩）应当覆盖 <see cref="INavigation2D.RaycastWithNormal"/>。
        /// </summary>
        public static Vec2 ProbeAxisNormal(INavigation2D navigation, Id mapId, Vec2 from, Vec2 to, Vec2 hit)
        {
            var d = to - from;
            var length = d.Length;
            if (length <= 0.0) return Vec2.Zero;

            var unit = new Vec2(d.X / length, d.Y / length);
            var back = Math.Min((hit - from).Length, DefaultProbeBackoff);
            var p1 = hit - unit * back;
            var probe = DefaultProbeBackoff * 2.0;

            var blockedX = Math.Abs(unit.X) > 1e-9 &&
                           navigation.Raycast(mapId, p1, p1 + new Vec2(Math.Sign(unit.X) * probe, 0.0)).HasValue;
            var blockedY = Math.Abs(unit.Y) > 1e-9 &&
                           navigation.Raycast(mapId, p1, p1 + new Vec2(0.0, Math.Sign(unit.Y) * probe)).HasValue;
            if (blockedX == blockedY) return Vec2.Zero;

            return blockedX ? new Vec2(-Math.Sign(unit.X), 0.0) : new Vec2(0.0, -Math.Sign(unit.Y));
        }
    }
}
