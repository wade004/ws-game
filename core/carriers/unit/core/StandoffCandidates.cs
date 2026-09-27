using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// ADR-0103（抽取自 <see cref="MovementTickHandler"/> 的 <c>TryFindStandoffCandidatePath</c>，
    /// 见 <a href="../../../architecture/adr/0102-追击规划点不可达时采样候选站位点.md">ADR-0102</a>
    /// 决策 2）：以某个圆心为中心、给定半径的圆上，从"正对接近者的方向"起按左右交替外扩的角度序列
    /// <c>+δ, -δ, +2δ, -2δ, …</c>（<c>δ = 2π / count</c>）依次生成候选点。角度偏移最小的候选排在
    /// 最前面（绕路最短）。
    /// <para>
    /// 不产出偏移 0（即"正对接近者的方向"本身对应的直接回退点）——调用方在采样之前已经/将要单独
    /// 尝试过那一个点，本方法只补齐圆上其余 <paramref name="count"/> - 1 个候选。追击
    /// （<see cref="MovementTickHandler"/> 的 <c>TryFindStandoffCandidatePath</c>，追击单位不可达
    /// 时绕开局部阻挡）与召唤物跟随（<c>Core.Carriers.Summon.SummonTickHandler.TryFollow</c>，跟随
    /// 点落进 owner 紧贴的阻挡格时绕开）共用同一套采样公式——两者都是"某单位需要停在另一单位附近
    /// 某个方向、直接方向不可用时在停止距离圆上找一个可用方向"的同一形状问题。
    /// </para>
    /// </summary>
    internal static class StandoffCandidates
    {
        /// <summary>
        /// 生成候选点序列。
        /// </summary>
        /// <param name="center">圆心（追击场景是目标当前位置，跟随场景是 owner 当前位置）。</param>
        /// <param name="directionToApproacher">从 <paramref name="center"/> 指向"正在接近的一方"
        /// （追击单位/召唤物）的方向向量，只取其角度（<c>Math.Atan2</c>），不要求已归一化、也不要求
        /// 长度非零之外的任何约束——长度为零时角度退化为 0，等同于把候选圆的起始角固定在 +X 轴，
        /// 仍能生成合法的候选点集合，只是不再"正对"接近者（该边界情形由调用方在更早的距离判断里
        /// 排除，本方法不重复校验）。</param>
        /// <param name="radius">候选圆半径（追击场景是停止距离，跟随场景是 <c>FollowStopDistance</c>）。</param>
        /// <param name="count">候选点总数上限（含未产出的偏移 0），<c>&lt;= 1</c> 时不产出任何候选
        /// （调用方应先行判断这一情形以完全跳过采样，本方法在该输入下返回空序列而不是抛异常，供
        /// 调用方少写一次防御性判断）。</param>
        public static IEnumerable<Vec2> Enumerate(Vec2 center, Vec2 directionToApproacher, double radius, int count)
        {
            if (count <= 1)
            {
                yield break;
            }

            var baseAngle = Math.Atan2(directionToApproacher.Y, directionToApproacher.X);
            var delta = 2.0 * Math.PI / count;

            for (var i = 1; i < count; i++)
            {
                var sign = i % 2 == 1 ? 1 : -1;
                var magnitude = (i + 1) / 2;
                var angle = baseAngle + sign * magnitude * delta;
                yield return new Vec2(
                    center.X + radius * Math.Cos(angle),
                    center.Y + radius * Math.Sin(angle));
            }
        }
    }
}
