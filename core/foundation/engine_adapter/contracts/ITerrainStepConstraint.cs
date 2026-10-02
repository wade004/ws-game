using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 地形台阶/坡度对寻路的约束（可选能力，ADR-0130 追加决定"寻路感知台阶"）：回答"一个贴地行走者沿线段
    /// <paramref name="from"/> → <paramref name="to"/> 走，第一个被台阶或过陡的坡挡住的点在哪"。
    /// <para>
    /// 它是<b>有向</b>的：只有"向上"的落差会挡（上台阶、爬陡坡），向下的落差不挡（走出平台边缘会离地下落，由竖直运动服务处理），
    /// 所以 <c>FirstStepBlock(a, b)</c> 与 <c>FirstStepBlock(b, a)</c> 一般不同。规则与移动系统实际执行的台阶阻挡是同一份
    /// （<c>VerticalMotionHost</c> 实现本接口，沿用 <see cref="TerrainStepMath"/> 的滑窗规则），因此"寻路说能走的路，移动时不会被台阶截断"。
    /// </para>
    /// <para>
    /// 用法：移动系统在声明了地形高度能力与台阶高度时，把本约束交给 <see cref="INavigation2D.FindPath(Id, Vec2, Vec2, ITerrainStepConstraint?)"/>
    /// 与 <see cref="INavigation2D.TryFindNearestReachable(Id, Vec2, Vec2, double, ITerrainStepConstraint?, out Vec2)"/>；没有声明（缺省）时不传，
    /// 导航行为与引入本接口之前逐位一致。
    /// </para>
    /// </summary>
    public interface ITerrainStepConstraint
    {
        /// <summary>
        /// 贴地行走者沿 <paramref name="from"/> → <paramref name="to"/> 走时第一个被台阶挡住的点；没有挡住返回 <c>null</c>。
        /// 返回点是第一个"落差超限"的位置（与 <c>Raycast</c> 的命中点同口径，调用方照常回退一个到达容差）。
        /// </summary>
        Vec2? FirstStepBlock(Id mapId, Vec2 from, Vec2 to);
    }
}
