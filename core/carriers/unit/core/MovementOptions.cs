using System;
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
        /// <c>movement_budget_rule: action_points</c>（以行动点计的移动预算）见
        /// <see cref="MovementBudgetRule"/>/<see cref="MovementActionCostPerUnit"/>：落地后，本字段
        /// 仍然是"该行动者这一步按速度会移动多远"的距离计算基准（<c>action_points</c> 规则只是在
        /// 这个距离之上叠加一层"够不够行动点"的门槛，见 <c>MovementTickHandler</c> 判断记录），不
        /// 是被替换掉的旧机制。
        /// </summary>
        public double DiscreteTurnEquivalentSeconds { get; set; } = 1.0;

        /// <summary>
        /// ADR-0013 补齐：离散模式下每回合移动预算的计算方式，取值同 <c>found.time_model.movement_budget_rule</c>
        /// （<c>"distance"</c> 或 <c>"action_points"</c>），默认 <c>"distance"</c>（本任务之前唯一
        /// 落地过的规则，行为不变）。
        /// <para>
        /// 判断记录（构造后回填而非构造期传入）：本模块（<c>core/carriers/unit</c>）构造早于
        /// <c>core/gameplay/assembly.TimeModelSwitch</c> 读出 <c>found.time_model</c> 数据的时机
        /// （惯例同 <c>Core.Rules.Combat.CombatOptions.LeaveCombatDelay</c> 判断记录、
        /// <c>Core.Rules.Combat.CombatTickHandler</c> 判断记录"事件订阅而非直接引用"的姊妹做法），
        /// 本字段与下面三个字段都设计成"构造后可写属性"，由 <c>GameplayAssembly</c> 在装配出
        /// <c>TimeModelSwitch</c> 之后回填同一个 <see cref="MovementOptions"/> 实例（
        /// <c>Core.Carriers.Assembly.CarriersAssembly.MovementOptions</c> 属性把它对外暴露）。
        /// </para>
        /// </summary>
        public string MovementBudgetRule { get; set; } = "distance";

        /// <summary><see cref="MovementBudgetRule"/> 为 <c>"action_points"</c> 时使用：移动 1 单位
        /// 距离消耗的行动点数（见 <see cref="MovementBudgetRule"/> 判断记录、04 第 3.1 节勘误
        /// <c>movement_action_cost_per_unit</c>）。默认 0（配合默认的 <c>"distance"</c> 规则时不会
        /// 被读取）。</summary>
        public double MovementActionCostPerUnit { get; set; } = 0.0;

        /// <summary>
        /// <see cref="MovementBudgetRule"/> 为 <c>"action_points"</c> 时，离散步移动前调用本委托
        /// 尝试扣减 <c>(actorId, 本次位移所需行动点)</c>；返回 <c>false</c> 表示预算不足，
        /// <see cref="MovementTickHandler"/> 据此拒绝本次移动意图（不产生任何位移）并调用
        /// <see cref="RequestEndTurn"/>。为空（未装配离散模式，或调用方未回填）时
        /// <see cref="MovementTickHandler"/> 不做任何行动点检查，行为与本任务之前一致。典型绑定：
        /// <c>Core.Foundation.SimLoop.TurnScheduler.TryConsumeActionPoints</c>（见该方法判断记录
        /// "与 TurnScheduler 的 action_points 策略共享同一预算"）。
        /// </summary>
        public Func<Id, double, bool>? TryConsumeActionPoints { get; set; }

        /// <summary>行动点耗尽、移动意图被拒绝时调用，结束该行动者的回合（06 第 6.2 节"直到本回合
        /// 行动点/移动预算耗尽...调用 TurnScheduler.endTurn"）。典型绑定：
        /// <c>Core.Foundation.SimLoop.TurnScheduler.EndTurn</c>。</summary>
        public Action<Id>? RequestEndTurn { get; set; }
    }
}
