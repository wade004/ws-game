using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 导航网格、路径查询、可行走判定（见 02_引擎适配层.md 第 1.8 节）。可选接口——
    /// 不依赖寻路的游戏（例如 AI 只做原地/直线追击）可以不实现完整寻路，退化为
    /// IsWalkable 与直线移动。
    /// </summary>
    public interface INavigation2D
    {
        /// <summary>在场景加载时根据地图数据构建二维可行走区域。</summary>
        void BuildNavMesh(Id mapId);

        bool IsWalkable(Id mapId, Vec2 point);

        /// <summary>
        /// 返回一串路径点供移动系统跟随；找不到可行路径时返回 null（而非空列表），
        /// 供调用方明确区分"无路径"与"路径长度为零"。
        /// </summary>
        IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to);

        /// <summary>
        /// 返回从起点到终点连线上第一个阻挡点；若无阻挡则返回 null。
        /// 视线是否受阻即 "Raycast 返回值是否非空"。
        /// </summary>
        Vec2? Raycast(Id mapId, Vec2 from, Vec2 to);

        /// <summary>
        /// 登记一批运行时动态阻挡矩形（如临时关闭的门、被摧毁的可破坏物），供
        /// IsWalkable/FindPath/Raycast 在原有静态导航数据之上叠加判定，不需要重新
        /// BuildNavMesh 整图（见 ADR-0016 决策 7）。同一 mapId 再次调用以传入的整批矩形
        /// 替换此前登记的动态阻挡（不是追加）。
        /// </summary>
        void SetBlocking(Id mapId, IReadOnlyList<Rect> rects);

        /// <summary>清空某地图的全部动态阻挡登记，供场景卸载时重置。</summary>
        void Clear(Id mapId);
    }
}
