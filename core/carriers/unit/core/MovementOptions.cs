using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// <c>MovementTickHandler</c> 的口味配置项（见任务书拍板：速度属性 id 与缺省速度、到达判定
    /// 阈值，均可按具体游戏口味调整，不属于架构层面的固定语义，惯例同 <c>core/rules/ai</c> 的
    /// <c>AiOptions</c>）。
    /// </summary>
    public sealed class MovementOptions
    {
        /// <summary>速度来源属性 id（见 05 第 6.2 节"速度来源：来自该 Unit 的 StatBlock 中的移动速度
        /// 属性"），默认 <c>stat.move_speed</c>。</summary>
        public Id MoveSpeedStat { get; set; } = new Id("stat.move_speed");

        /// <summary><see cref="MoveSpeedStat"/> 缺失（<c>IStatHost.GetStat</c> 返回非正值，判断记录见
        /// <c>MovementTickHandler.ResolveSpeed</c>）时使用的缺省速度，默认 4（同
        /// <c>core/rules/ai</c> <c>AiOptions.MoveSpeed</c> 默认值同量级）。</summary>
        public double DefaultSpeed { get; set; } = 4.0;

        /// <summary>"已到达"路点判定的距离阈值，默认 0.01（同 <c>MoveIntentHandler</c> 一类最小实现
        /// 惯例，取一个远小于典型移动速度×步长的量级，避免因浮点误差导致永远差一点点到不了）。</summary>
        public double ArrivalEpsilon { get; set; } = 0.01;

        /// <summary>
        /// 离散模式（ADR-0013）下每回合移动预算的距离换算：<c>movement_budget_rule: distance</c>
        /// 时，本回合可移动距离 = 该单位速度属性 × 本值（见 06_规则层_属性技能战斗AI.md"每回合
        /// 移动预算"、03 第 4.2 节步骤 4"离散步下按该行动者的每回合移动预算结算位移，而非按连续
        /// 时间的速度积分"）。判断记录：把"每回合等效秒数"设为可配置项而不是固定距离常量，复用
        /// 现有"速度属性 × 时间"的计算路径（<see cref="MovementTickHandler"/> 内部不需要为离散模式
        /// 另写一套位移公式），默认 1.0（一回合 ≈ 一秒的移动量，具体数值由游戏层按口味调整）。
        /// <c>movement_budget_rule: action_points</c>（以行动点计的移动预算）本任务未落地，仍按本
        /// 字段的距离预算处理，已在交付报告"做不了的事"列出。
        /// </summary>
        public double DiscreteTurnEquivalentSeconds { get; set; } = 1.0;
    }
}
