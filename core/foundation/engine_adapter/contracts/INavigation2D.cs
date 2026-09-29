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
        /// <para>
        /// 端点契约（见 02 第 1.8 节勘误）：<c>from</c>/<c>to</c> 任一不可行走（<see cref="IsWalkable"/>
        /// 为 false）返回 null；否则 <c>|from-to| &lt;= 1e-6</c>（零长度目标）返回单元素路径
        /// <c>[from]</c>；否则返回的路径必须满足 <c>path[0]</c> 精确等于 <paramref name="from"/>、
        /// <c>path[^1]</c> 精确等于 <paramref name="to"/>——网格实现内部先在网格上寻路，再把网格路径
        /// 与精确端点"接合"（首尾替换/追加为精确坐标），接合段必须通过与 <see cref="Raycast"/> 相同的
        /// 阻挡判定；接合不通过时改选相邻可行走格作为接合点，仍不通过则整体返回 null（而不是返回一条
        /// 端点不精确或穿墙的路径）；找不到可行路径同样返回 null。
        /// </para>
        /// <para>
        /// 视线剪枝（ADR-0101）：实现应在返回前对网格路径做视线剪枝（string pulling），省略掉
        /// 能被一条通畅直线跨过的中间路点；两端点之间直线通畅时应返回 <c>[from, to]</c>。沿路径
        /// 移动的朝向由每一段的方向决定，因此剪枝后的停步朝向即最后一段的行进方向。
        /// </para>
        /// </summary>
        IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to);

        /// <summary>
        /// 返回从起点到终点连线上第一个阻挡点；若无阻挡则返回 null。
        /// 视线是否受阻即 "Raycast 返回值是否非空"。
        /// <para>
        /// 可通行判定统一规则（见 02 第 1.8 节勘误、05 第 6 节）：线段与阻挡区域**内部**相交才算受阻，
        /// 仅与阻挡区域的边界或角点相切（不进入内部）不算受阻；<see cref="FindPath"/> 内部对每一段
        /// 路径使用与本方法完全相同的判定——因此 <see cref="FindPath"/> 成功返回的路径，其每一段
        /// <c>Raycast(mapId, path[i], path[i+1])</c> 必为 null（否则视为实现缺陷）。网格实现在生成
        /// 网格路径时，对角邻居仅当两个正交邻居都可行走时才允许联通（不允许贴着阻挡格"切角"）。
        /// </para>
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

        /// <summary>
        /// 该地图动态阻挡数据的版本号（见 02 第 1.8 节勘误）：实现每次因
        /// <see cref="SetBlocking"/>/<see cref="Clear"/>/<see cref="BuildNavMesh"/> 改变该
        /// <paramref name="mapId"/> 的可行走判定结果时递增（同一 mapId 独立计数，具体起始值与递增
        /// 幅度不作约定，调用方只应比较"是否变化"，不应假设具体数值）；返回 <c>0</c> 表示该实现
        /// 不支持版本追踪（默认实现），调用方对 <c>0</c> 一律视为"不做自动重验"（见 05 第 6 节移动
        /// 与导航 tick 步骤）。默认接口实现恒返回 0，保证既有实现（未显式重写本成员）源码兼容，
        /// 不强制要求每个 <see cref="INavigation2D"/> 实现都支持阻挡变化自动重验——这是可选能力
        /// （惯例同 <c>Core.Foundation.SaveSystem.ISimSnapshot</c> 新增成员改用默认实现保持源码兼容）。
        /// </summary>
        int GetBlockingVersion(Id mapId) => 0;

        /// <summary>
        /// ADR-0110：在以 <paramref name="point"/> 为圆心、<paramref name="maxRadius"/> 为半径的范围内找
        /// 离 <paramref name="point"/> 最近的可走点。排序规则见 <see cref="NearestWalkableSearch"/>：
        /// <b>主键</b> = 到 <paramref name="point"/> 的距离按导航格宽量化（同一量化档并列）；<b>次键</b> =
        /// 到 <paramref name="preferNear"/> 的距离（小者优先，典型传入移动单位的当前位置——薄墙居中点击的
        /// 并列情形取单位所在一侧）；仍并列按坐标字典序。<paramref name="point"/> 本身可走
        /// （<see cref="IsWalkable"/> 为 true）时原样返回它。范围内没有可走点返回 <c>false</c>
        /// （<paramref name="walkable"/> 为 default）。
        /// <para>
        /// 返回的点必满足 <see cref="IsWalkable"/> 为 true，且把它作为 <see cref="FindPath"/> 的终点不会
        /// 因"贴边/落在格边界"被判不可走（取格心或等价的安全点）——可走不等于可达：孤岛上的点仍可能
        /// <see cref="FindPath"/> 失败，调用方按需用 <see cref="FindNearestWalkableCandidates"/> 取后续候选。
        /// </para>
        /// <para>
        /// 默认实现委托 <see cref="FindNearestWalkableCandidates"/>（<c>maxCount = 1</c>）；网格实现应覆盖
        /// 后者，本成员随之自动得到网格精确的结果，也可显式覆盖（同一份排序规则）。
        /// </para>
        /// </summary>
        bool TryFindNearestWalkable(Id mapId, Vec2 point, double maxRadius, Vec2 preferNear, out Vec2 walkable)
        {
            var results = new List<Vec2>(1);
            if (FindNearestWalkableCandidates(mapId, point, maxRadius, preferNear, 1, results) > 0)
            {
                walkable = results[0];
                return true;
            }

            walkable = default;
            return false;
        }

        /// <summary>
        /// ADR-0110：按 <see cref="TryFindNearestWalkable"/> 同一排序规则，取前 <paramref name="maxCount"/>
        /// 个可走候选点（第一个就是 <see cref="TryFindNearestWalkable"/> 的结果）。先清空
        /// <paramref name="results"/>，再把候选依次追加进去，返回追加个数（范围内可走点不足时少于
        /// <paramref name="maxCount"/>，无可走点时为 0）。供"最近点作为终点寻路失败（落在不可达孤岛上）
        /// 时依次尝试后续候选"使用，排序规则只有 <see cref="NearestWalkableSearch"/> 这一份实现。
        /// <para>
        /// 默认实现只用 <see cref="IsWalkable"/> 做同心环采样
        /// （<see cref="NearestWalkableSearch.CollectSampled"/>：步长 <see cref="NearestWalkableSearch.SampledStep"/>
        /// = 0.25 世界单位，环 k 半径 <c>k × 0.25</c>，每环 <c>max(8, ceil(2πr / 0.25))</c> 个方向，
        /// 最多 <see cref="NearestWalkableSearch.MaxSampledRings"/> 环），主键（环号）精确、环上两个
        /// 采样点之间的可走点可能漏掉——不知道网格的第三方实现的近似；网格实现应覆盖为
        /// <see cref="NearestWalkableSearch.CollectOnGrid"/>（按自身格子精确搜索）。
        /// </para>
        /// </summary>
        int FindNearestWalkableCandidates(
            Id mapId, Vec2 point, double maxRadius, Vec2 preferNear, int maxCount, List<Vec2> results) =>
            NearestWalkableSearch.CollectSampled(p => IsWalkable(mapId, p), point, maxRadius, preferNear, maxCount, results);
    }
}
