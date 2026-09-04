using System;
using Core.Foundation.Common;

namespace Core.Rules.Ai
{
    /// <summary>
    /// AI 模块的口味配置项（见任务书拍板：<c>AttackRange</c>/<c>MoveSpeed</c>/<c>FleeReengage</c>/
    /// <c>RandomTieBreak</c>/<c>RngStream</c>/<c>ArrivalEpsilon</c> 六项 + 判断记录补充的
    /// <see cref="MapId"/>）。全部字段可按具体游戏口味调整，不属于架构层面的固定语义。
    /// </summary>
    public sealed class AiOptions
    {
        /// <summary>进入攻击范围（<c>chase</c>→<c>combat</c>）与被追上（<c>flee</c>→<c>combat</c>）
        /// 判定用的距离阈值，默认 2。</summary>
        public double AttackRange { get; set; } = 2.0;

        /// <summary>位移意图的速度，单位为"每模拟时间单位移动的距离"，默认 4。</summary>
        public double MoveSpeed { get; set; } = 4.0;

        /// <summary><c>flee</c> 态被追上后是否允许回到 <c>combat</c>，默认 true（见 06 第 6.1 节
        /// "被追上继续战斗则回 combat（策略配置）"）。</summary>
        public bool FleeReengage { get; set; } = true;

        /// <summary>选目标/选平局候选时是否允许经 <see cref="Core.Foundation.Rng.IRngHost"/> 分流随机，
        /// 默认 false（默认平局按 Id 序，见本模块 README）。</summary>
        public bool RandomTieBreak { get; set; } = false;

        /// <summary><see cref="RandomTieBreak"/> 为 true 时使用的 RNG 分流 stream id。</summary>
        public Id RngStream { get; set; } = new Id("ai.decision");

        /// <summary>"已到达"判定的距离阈值（<c>return</c> 到出生点/巡逻路径起点、<c>patrol</c> 到
        /// 路径点），默认 0.5（见任务书 <c>return → idle/patrol：到达 SpawnPoint（距离 &lt; 0.5）</c>）。</summary>
        public double ArrivalEpsilon { get; set; } = 0.5;

        /// <summary>
        /// 判断记录（契约缺口，集成任务已补齐）：本字段原是"<c>IUnitAccess</c> 不暴露单位所属
        /// 地图 id"这一契约缺口的权宜之计——只适用于单地图场景，多地图场景无法按单位分别寻路。
        /// 集成任务已给 <c>IUnitAccess</c> 补上 <see cref="Core.Rules.Common.IUnitAccess.GetMapId"/>，
        /// <see cref="AiHost"/> 的 <c>ComputeDirection</c> 现在优先用
        /// <c>IUnitAccess.GetMapId(unitId)</c>，只有它返回 <c>null</c>（未接入地图概念的实现，如
        /// 测试假实现）时才回退到本字段——两种口味二选一保留字段（任务书"保留字段但标记过时也可，
        /// 二选一说明"），本任务选择保留 + 标记 <see cref="ObsoleteAttribute"/> 而不是直接删除：
        /// 删除会导致既有引用本字段的调用方（游戏层配置、已发布的口味清单）编译失败，标记过时
        /// 既提示"有更好的替代方案"又不破坏向后兼容；默认 <c>null</c> 表示不提供单地图兜底值。
        /// </summary>
        [Obsolete("改用 IUnitAccess.GetMapId(unitId)；本字段仅在 GetMapId 返回 null 时作为单地图场景的兜底值")]
        public Id? MapId { get; set; } = null;
    }
}
