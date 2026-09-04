using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.SimLoop
{
    // ============================================================================
    // 离散时间模型（回合制）本项目暂不启用（见 ADR-0013"决策"第 3 条、
    // 落地方案与分阶段计划.md T1-5 禁止事项："禁止本任务实现离散模式（TurnScheduler/
    // PacingPolicy 只建接口骨架，逻辑留空并注明"本项目暂不启用""）。本文件只按
    // 03_运行时骨架.md 第 9 节签名建立接口骨架，供未来需要回合制战斗时再实现；
    // 默认实现见 core/NotEnabledTurnScheduler.cs，全部方法一律抛
    // System.NotSupportedException。
    // ============================================================================

    /// <summary>先攻策略（见 04_数据与内容管线.md 第 3.1 节 <c>initiative_policy</c>、
    /// ADR-0013 决策第 3 条）；<c>atb</c> 是预留扩展位，本版不展开，因此本枚举也不收录。</summary>
    public enum InitiativePolicy
    {
        InitiativeStat,
        ActionPoints,
        FixedOrder
    }

    /// <summary>
    /// 回合调度器（见 03 第 3.2、9 节签名）：按先攻规则维护本回合行动顺序、产生离散步
    /// 序列、管理等待输入与回合/轮次边界。本项目暂不启用，见文件头说明。
    /// <para>
    /// <c>submitIntent</c> 的 <c>intent</c> 参数类型对应 03 伪代码里的 <c>Intent</c>——
    /// 该类型尚未在本仓库任何模块中定义（属于更上层的意图数据结构，见 03 第 4.2 节步骤 1），
    /// 本骨架接口用 <see cref="object"/> 占位，不提前发明一个可能与后续设计冲突的具体类型；
    /// 真正启用离散模式时应替换为届时已定义的 Intent 类型。
    /// </para>
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

        /// <summary>玩家经窄契约提交本次行动意图（见 03 第 3.2 节步骤 3）。</summary>
        void SubmitIntent(Id actorId, object intent);

        /// <summary>提交"结束回合"意图（见 03 第 3.2 节步骤 5）。</summary>
        void EndTurn(Id actorId);

        /// <summary>取当前本轮行动顺序。</summary>
        IReadOnlyList<Id> GetOrder();

        /// <summary>取当前行动者；无行动中的回合返回空。</summary>
        Id? GetCurrentActor();
    }
}
