using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 地形高度查询（可选能力，ADR-0130 追加决定"地面高度"）：按平面坐标回答"这一点的地面有多高、头顶有没有天花板"。
    /// 只在装配了竖直轴（<c>VerticalAxisOptions</c>）且声明了本能力时被读取；没有声明（缺省）等价 <see cref="FlatTerrainHeight2D"/>——
    /// 地面恒为 0、没有天花板，行为与引入本接口之前逐位一致。
    /// <para>
    /// 高度是<b>绝对脚下高度</b>（世界单位，与 <c>Unit.HeightOffset</c> 同一量纲；平地为 0，平台为正）。本接口只回答"哪里有多高"，
    /// 单位怎么落地、怎么贴合坡面、台阶挡不挡路由竖直运动服务与移动系统决定。引擎适配层可以用物理射线实现它（从高处向下探地面、
    /// 从地面向上探天花板），无头宿主用数据（<c>world.map.terrain</c>）实现。
    /// </para>
    /// </summary>
    public interface ITerrainHeight2D
    {
        /// <summary>该点的地面高度（世界单位，绝对脚下高度；没有地面信息的点返回 0）。</summary>
        double GetGroundHeight(Id mapId, Vec2 point);

        /// <summary>
        /// 该点头顶天花板的绝对高度（世界单位）；没有天花板返回 <see cref="double.PositiveInfinity"/>（默认实现）。
        /// 天花板只约束向上运动：上升中的单位脚下高度碰到它时竖直速度清零，开始下落。
        /// </summary>
        double GetCeilingHeight(Id mapId, Vec2 point) => double.PositiveInfinity;
    }

    /// <summary>缺省地形：地面恒为 0、没有天花板（等价于没有声明地形高度能力）。</summary>
    public sealed class FlatTerrainHeight2D : ITerrainHeight2D
    {
        public static readonly FlatTerrainHeight2D Instance = new FlatTerrainHeight2D();

        private FlatTerrainHeight2D()
        {
        }

        public double GetGroundHeight(Id mapId, Vec2 point) => 0.0;

        public double GetCeilingHeight(Id mapId, Vec2 point) => double.PositiveInfinity;
    }
}
