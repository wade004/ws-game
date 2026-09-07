using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// 任务系统对外契约（见 08 第 9 节汇总表 Quest 行 <c>QuestHost.accept/turnIn/updateProgress(...)</c>，
    /// 本接口按任务书拍板把该签名展开为完整的状态查询/转移方法集）。由 <see cref="QuestHost"/> 实现。
    /// </summary>
    public interface IQuestHost
    {
        /// <summary>
        /// 当前状态。<see cref="QuestState.Unavailable"/>/<see cref="QuestState.Available"/> 两态
        /// 不持久化，每次调用按 <c>prerequisite</c> 实时求值（见 08 第 2.2 节
        /// "unavailable → available：prerequisite 满足"）；其余四态（Active/ObjectivesComplete/
        /// Failed/TurnedIn）来自持久化的运行期记录。
        /// </summary>
        QuestState GetState(Id unitId, Id questId);

        /// <summary>
        /// 接取任务（08 第 2.2 节"available → active：按 start_method 被接取"）。要求当前状态为
        /// <see cref="QuestState.Available"/>，且 <c>exclusive_group</c>（若声明）内没有其它已经
        /// Active/ObjectivesComplete 的任务，否则返回 false、不改变任何状态。成功发
        /// <c>quest.accepted</c>。
        /// </summary>
        bool Accept(Id unitId, Id questId);

        /// <summary>
        /// 手动推进某条目标的进度（主要供 <c>escort</c> 类目标——本类型没有自动事件驱动，见 08
        /// 第 2.1 节 <c>escort</c> 行；其余类型的进度通常由 <see cref="QuestHost"/> 内部的事件订阅
        /// 自动推进，但调用方也可以用本方法手动推进，例如脚本/关卡逻辑显式驱动）。要求任务当前为
        /// <see cref="QuestState.Active"/>，<paramref name="objectiveIndex"/> 越界返回 false。
        /// 计数被夹在 <c>[0, objective.Count]</c> 之间；发 <c>quest.objective_progress</c>；
        /// 全部目标达标时状态转移为 <see cref="QuestState.ObjectivesComplete"/> 并额外发
        /// <c>quest.completed</c>。
        /// </summary>
        bool UpdateProgress(Id unitId, Id questId, int objectiveIndex, int delta);

        /// <summary>
        /// 交付任务（08 第 2.2 节"objectives_complete → turned_in：按 turn_in_method 交付，结算
        /// rewards"）。等价于 <c>TurnIn(unitId, questId, out _)</c>，不关心失败原因。
        /// </summary>
        bool TurnIn(Id unitId, Id questId);

        /// <summary>
        /// 交付任务，同上，额外通过 <paramref name="failure"/> 报告失败原因（N02/N11 根治，见
        /// <see cref="QuestTurnInFailure"/>）。要求当前状态为 <see
        /// cref="QuestState.ObjectivesComplete"/>；交付时先按实际库存核验 <c>collect</c> 且
        /// <c>consume_on_progress=false</c> 的目标能否扣除完整数量（不足则整体失败，见 08 第 2.1 节
        /// 该行 param 要点），确认可扣除后才实际移除，随后经 <see
        /// cref="Core.Gameplay.Common.IRewardDispatcher"/> 结算 <c>rewards</c>——奖励发放原子：
        /// 物品奖励因背包已满无法完整发放时，整批奖励不生效，已扣除的 collect 物品回滚放回背包，
        /// 交付整体失败、任务保持 <see cref="QuestState.ObjectivesComplete"/>（不丢奖励、不误置
        /// <see cref="QuestState.TurnedIn"/>）。交付成功时按 <c>repeatable</c> 决定交付后的状态
        /// （<c>none</c> 终止于 TurnedIn；<c>daily</c> 回落 Available 但记录完成日、同日不可再接；
        /// <c>unlimited</c> 立即回落 Available），并发 <c>quest.turned_in</c>。
        /// </summary>
        bool TurnIn(Id unitId, Id questId, out QuestTurnInFailure failure);

        /// <summary>
        /// 判定任务失败（08 第 2.2 节"escort/event 类可能失败，策略配置是否允许失败"）。要求
        /// <see cref="QuestOptions.AllowFail"/> 开启，且当前状态为 Active 或 ObjectivesComplete，
        /// 否则返回 false。成功发 <c>quest.failed</c>（附 <paramref name="reason"/>）。
        /// </summary>
        bool Fail(Id unitId, Id questId, string reason);

        /// <summary>该单位全部有持久化记录（曾经 Accept 过，含已完成/已失败）的任务进度快照，
        /// 供存档/UI 展示（见 <see cref="QuestPersistable"/>）。</summary>
        IReadOnlyList<QuestProgress> GetLog(Id unitId);

        /// <summary>该单位当前全部 Active 任务里尚未达标的目标（questId, objectiveIndex, targetRef）
        /// 三元组列表（见 08 第 2.3 节"每个激活任务的每个未完成目标，可关联一个地图标记……逻辑层只
        /// 暴露当前激活目标的位置查询接口"——本方法只给出 targetRef，具体位置由调用方按 targetRef
        /// 对应实体查询，本模块不涉及空间查询）。</summary>
        IReadOnlyList<(Id QuestId, int ObjectiveIndex, Id TargetRef)> GetActiveObjectives(Id unitId);

        /// <summary>
        /// 驱动 <c>start_method=auto</c>/<c>turn_in_method=auto</c> 两类自动转移（见 08 第 2.1 节
        /// 该两字段 <c>auto</c> 取值）：把当前 Available 且 <c>start_method=auto</c> 的任务自动
        /// Accept；把当前 ObjectivesComplete 且 <c>turn_in_method=auto</c> 的任务自动 TurnIn。
        /// 调用方按需（如每次场景加载、每次事件驱动进度更新之后）调用；本方法不会被
        /// <see cref="UpdateProgress"/> 自动触发（避免"进度更新"这一相对高频操作里隐式发生任务
        /// 交付这类有较重副作用（结算奖励）的转移，见判断记录）。
        /// </summary>
        void Update(Id unitId);
    }
}
