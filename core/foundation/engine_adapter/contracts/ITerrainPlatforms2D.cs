using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>一次平台接触：踩中（或着陆在）哪块平台、平台顶面此刻的绝对高度。</summary>
    public readonly struct PlatformContact
    {
        public string PlatformId { get; }

        /// <summary>平台顶面的绝对高度（世界单位，与 <c>Unit.HeightOffset</c> 同一量纲）。</summary>
        public double Height { get; }

        public PlatformContact(string platformId, double height)
        {
            PlatformId = platformId;
            Height = height;
        }
    }

    /// <summary>一块移动平台在最近一次 <see cref="ITerrainPlatforms2D.Advance"/> 里的位移（平面位移与高度位移），站在它上面的单位据此被带走。</summary>
    public readonly struct PlatformMotion
    {
        public string PlatformId { get; }

        public Id MapId { get; }

        public Vec2 Delta { get; }

        public double HeightDelta { get; }

        public PlatformMotion(string platformId, Id mapId, Vec2 delta, double heightDelta)
        {
            PlatformId = platformId;
            MapId = mapId;
            Delta = delta;
            HeightDelta = heightDelta;
        }
    }

    /// <summary>
    /// 单向平台（跳穿平台）与移动平台能力（可选，ADR-0170）：<see cref="ITerrainHeight2D"/> 对每个平面点只回答"一个"地面高度，
    /// 表达不了"同一点上方悬着一块只能从上面站、从下面能跳穿"的平台，也没有随时间移动的表面。本接口补这两件事：
    /// 平台是<b>单向</b>的——下方的单位（脚下低于平台顶面）与上升中的单位完全不受影响，只有从上方下落、脚下穿过顶面的单位会落在它上面；
    /// 平台可以按解析式随平台时钟移动，站在上面的单位由竖直运动服务带走（<see cref="Advance"/> 之后读 <see cref="LastMotions"/>）。
    /// <para>
    /// 只在 <c>VerticalAxisOptions.Platforms</c> 声明后被读取（同时必须声明 <c>Terrain</c>：平台叠在地形之上）；缺省 null 世界逐位不变。
    /// 数据实现见 <c>MapPlatforms</c>（<c>world.map.platforms</c>）；引擎适配层可用物理单向碰撞体实现同一接口。
    /// </para>
    /// </summary>
    public interface ITerrainPlatforms2D
    {
        /// <summary>平台时钟（秒）：移动平台的位姿是它的解析函数。由 <see cref="Advance"/> 累加；存档读回时用 <see cref="SetTime"/> 复原。</summary>
        double TimeSeconds { get; }

        /// <summary>把平台时钟直接设为 <paramref name="seconds"/>（所有移动平台跳到该时刻的位姿，不产生位移记录）。</summary>
        void SetTime(double seconds);

        /// <summary>平台时钟前进 <paramref name="dt"/> 秒，重新计算位姿，并把每块移动平台的位移记入 <see cref="LastMotions"/>。</summary>
        void Advance(double dt);

        /// <summary>最近一次 <see cref="Advance"/> 里产生了位移的移动平台（按平台声明顺序；静止平台不出现）。</summary>
        IReadOnlyList<PlatformMotion> LastMotions { get; }

        /// <summary>
        /// 下落着陆检测：单位这一步脚下高度从 <paramref name="footBefore"/> 降到 <paramref name="footAfter"/>，平面位置 <paramref name="point"/>——
        /// 脚下起点不低于某块平台顶面（上一步在平台上方或齐平）且终点不高于它的顶面时着陆，多块满足取最高的一块。
        /// <paramref name="ignorePlatformId"/> 非空时跳过该平台（主动下穿）。
        /// </summary>
        bool TryLand(Id mapId, Vec2 point, double footBefore, double footAfter, string? ignorePlatformId, out PlatformContact contact);

        /// <summary>
        /// 该点上顶面与 <paramref name="foot"/> 齐平（浮点容差内）的平台：单位"正站在某块平台上"的判定（出生/传送到平台上时认出支撑）。
        /// 移动平台在最近一次 <see cref="Advance"/> 里动过时，与它上一步的顶面齐平也算（刚放上去的单位还没来得及被带动）。
        /// </summary>
        bool TryGetSupport(Id mapId, Vec2 point, double foot, out PlatformContact contact);

        /// <summary>指定平台在 <paramref name="point"/> 处的顶面高度；该点不在平台当前范围内返回 false（站在上面的单位走出了平台边缘）。</summary>
        bool TryGetTop(Id mapId, string platformId, Vec2 point, out double height);
    }
}
