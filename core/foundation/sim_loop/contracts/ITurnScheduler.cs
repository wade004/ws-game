using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.SimLoop
{
    // ============================================================================
    // 离散时间模型（回合制）落地（ADR-0013、architecture/落地计划/落地方案与分阶段计划.md T1-5
    // 的"暂不启用"限制已由后续拍板解除，见 architecture/03_运行时骨架.md 第 3.2、9 节、
    // architecture/adr/0013-*.md）。默认实现见 core/TurnScheduler.cs。
    // ============================================================================

    /// <summary>先攻策略（见 04_数据与内容管线.md 第 3.1 节 <c>initiative_policy</c>、
    /// ADR-0013 决策第 3 条）。<see cref="Atb"/> 是预留扩展位，登记为合法策略名，但
    /// <see cref="TurnScheduler.Configure"/> 遇到时抛 <see cref="System.NotSupportedException"/>
    /// （见该方法注释）。</summary>
    public enum InitiativePolicy
    {
        InitiativeStat,
        ActionPoints,
        FixedOrder,
        Atb
    }

    /// <summary>
    /// 回合调度器（见 03 第 3.2、9 节签名）：按先攻规则维护本回合行动顺序、产生离散步
    /// 序列、管理等待输入与回合/轮次边界。默认实现见 <see cref="TurnScheduler"/>。
    /// </summary>
    public interface ITurnScheduler
    {
        /// <summary>配置先攻策略与参数（见 04 <c>found.time_model</c> 字段表）。</summary>
        void Configure(InitiativePolicy policy, IReadOnlyDictionary<string, object> parameters);

        /// <summary>进入离散模式，按先攻策略计算并维护本轮行动顺序（见 03 第 3.2 节步骤 1）。</summary>
        void BeginCombat(IReadOnlyList<Id> participants);

        /// <summary>退出离散模式（见 03 第 3.3 节步骤 3）。</summary>
        void EndCombat();

        /// <summary>产生下一个离散步；返回空表示进入 <c>awaiting_input</c> 或
        /// <c>playing_back</c>，主循环本步不推进模拟（见 03 第 3.2 节步骤 2）。</summary>
        SimStep? NextStep();

        /// <summary>
        /// 玩家（或代表玩家的调用方）经窄契约提交本次行动意图（见 03 第 3.2 节步骤 3、第 9 节
        /// <c>Intent</c> 结构）。只允许为 <see cref="GetCurrentActor"/> 提交，且
        /// <paramref name="intent"/>.ActorId 必须与 <paramref name="actorId"/> 一致，否则抛
        /// <see cref="System.ArgumentException"/>。
        /// </summary>
        void SubmitIntent(Id actorId, Intent intent);

        /// <summary>提交"结束回合"意图（见 03 第 3.2 节步骤 5）。</summary>
        void EndTurn(Id actorId);

        /// <summary>取当前本轮行动顺序。</summary>
        IReadOnlyList<Id> GetOrder();

        /// <summary>取当前行动者；无行动中的回合返回空。</summary>
        Id? GetCurrentActor();
    }
}
