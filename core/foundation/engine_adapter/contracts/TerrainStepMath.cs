using System;
using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 地形台阶/坡度判定的数学（ADR-0130 追加决定"台阶判定精确化"）：<c>VerticalMotionHost</c>（移动阻挡、走出平台下落）、
    /// 导航里的地形感知寻路（<see cref="TerrainStepPathPlanner"/>）与移动系统的路径校验共用这一份，规则只在这里写。
    /// <para>
    /// <b>滑窗规则</b>（取代此前"以线段起点为锚的相邻采样点差"）：沿线段按弧长 <c>s</c> 记地面高度 <c>g(s)</c>，窗口长度取
    /// <c>window</c>（<c>VerticalAxisOptions.StepSampleDistance</c>）。贴地行走者在 <c>s</c> 处被挡住，当且仅当
    /// <c>g(s) − g(max(0, s − window)) &gt; step</c>——即"任意一个窗口长度内地面升高超过台阶高度"：高于台阶的悬崖边缘处立刻成立
    /// （窗口越过边缘），超过 <c>step / window</c> 的陡坡在坡脚之后 <c>step / 坡度</c> 处成立。判定与线段从哪里出发、
    /// 采样格怎么对齐<b>无关</b>（同一面坡从不同起点走出的停点相同），停点用二分求精到 <see cref="RefineTolerance"/>。
    /// 空中单位的参照是当前脚下高度（<see cref="FirstRiseBlockFromFoot"/>）：前方地面比脚下高出超过台阶高度才挡。
    /// </para>
    /// <para>
    /// 判断记录（扫描分辨率）：线段上每隔 <c>window / 2</c> 取一个扫描点，相邻扫描点之间出现"先成立后又不成立"的特征
    /// （比扫描间隔更窄的凸起）可能漏检；<c>window</c> 就是地形台阶特征的分辨率（<c>StepSampleDistance</c> 的含义），不是另一个口味开关。
    /// 扫描点之间只有一次成立/不成立的转换时，二分给出该转换点（悬崖边缘、坡脚），不受扫描间隔限制。
    /// </para>
    /// </summary>
    public static class TerrainStepMath
    {
        /// <summary>二分求精的区间宽度（世界单位），远小于验收口径的 1e-6。</summary>
        public const double RefineTolerance = 1e-10;

        private const int MaxRefineIterations = 80;

        private enum Rule
        {
            WindowRise,
            FootRise,
            WindowDrop,
        }

        /// <summary>
        /// 贴地行走者沿 <paramref name="from"/> → <paramref name="to"/> 第一个被台阶/陡坡挡住的点（滑窗规则，见类型注释）；没有返回 <c>null</c>。
        /// </summary>
        public static Vec2? FirstRiseBlock(ITerrainHeight2D terrain, Id mapId, Vec2 from, Vec2 to, double step, double window) =>
            Scan(terrain, mapId, from, to, step, window, Rule.WindowRise, 0.0);

        /// <summary>
        /// 空中单位（脚下高度 <paramref name="foot"/>）沿线段第一个被挡住的点：前方地面高出 <paramref name="foot"/> 超过 <paramref name="step"/> 即挡。
        /// </summary>
        public static Vec2? FirstRiseBlockFromFoot(ITerrainHeight2D terrain, Id mapId, Vec2 from, Vec2 to, double step, double window, double foot) =>
            Scan(terrain, mapId, from, to, step, window, Rule.FootRise, foot);

        /// <summary>
        /// 沿线段第一个"地面在一个窗口长度内下降超过 <paramref name="drop"/>"的点（走出平台边缘、走下悬崖）；没有返回 <c>null</c>。
        /// 与 <see cref="FirstRiseBlock"/> 同口径的对称规则，贴地行走者据此判断"脚下的地面是不是突然消失了"。
        /// </summary>
        public static Vec2? FirstDrop(ITerrainHeight2D terrain, Id mapId, Vec2 from, Vec2 to, double drop, double window) =>
            Scan(terrain, mapId, from, to, drop, window, Rule.WindowDrop, 0.0);

        private static Vec2? Scan(
            ITerrainHeight2D terrain, Id mapId, Vec2 from, Vec2 to, double limit, double window, Rule rule, double foot)
        {
            var delta = to - from;
            var length = delta.Length;
            if (length <= 1e-12)
            {
                return null;
            }

            var dir = new Vec2(delta.X / length, delta.Y / length);
            var h = window * 0.5;
            var n = (int)Math.Ceiling(length / h);
            if (n < 1)
            {
                n = 1;
            }

            var g0 = terrain.GetGroundHeight(mapId, from);
            var gMinus2 = g0; // g(s_{k-2})
            var gMinus1 = g0; // g(s_{k-1})
            var prevS = 0.0;
            for (var k = 1; k <= n; k++)
            {
                var last = k == n;
                var s = last ? length : k * h;
                var g = terrain.GetGroundHeight(mapId, from + dir * s);
                double reference;
                if (rule == Rule.FootRise)
                {
                    reference = foot;
                }
                else if (last)
                {
                    // 末点不一定落在扫描网格上：窗口起点直接取值。
                    reference = terrain.GetGroundHeight(mapId, from + dir * Math.Max(0.0, s - window));
                }
                else
                {
                    reference = gMinus2; // s - window = s_{k-2}（k < 2 时为起点）
                }

                if (Holds(rule, g, reference, limit))
                {
                    var lo = prevS;
                    var hi = s;
                    for (var i = 0; i < MaxRefineIterations && hi - lo > RefineTolerance; i++)
                    {
                        var mid = 0.5 * (lo + hi);
                        if (HoldsAt(terrain, mapId, from, dir, mid, limit, window, rule, foot))
                        {
                            hi = mid;
                        }
                        else
                        {
                            lo = mid;
                        }
                    }

                    return from + dir * hi;
                }

                gMinus2 = gMinus1;
                gMinus1 = g;
                prevS = s;
            }

            return null;
        }

        private static bool HoldsAt(
            ITerrainHeight2D terrain, Id mapId, Vec2 from, Vec2 dir, double s, double limit, double window, Rule rule, double foot)
        {
            var g = terrain.GetGroundHeight(mapId, from + dir * s);
            var reference = rule == Rule.FootRise
                ? foot
                : terrain.GetGroundHeight(mapId, from + dir * Math.Max(0.0, s - window));
            return Holds(rule, g, reference, limit);
        }

        private static bool Holds(Rule rule, double g, double reference, double limit)
        {
            switch (rule)
            {
                case Rule.WindowDrop:
                    return reference - g > limit;
                default:
                    return g - reference > limit;
            }
        }
    }
}
