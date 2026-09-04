using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// 单个单位对单条任务的持久化进度快照（见 08 第 2.2 节"状态变化持久化在
    /// <c>PlayerUnit.questLog</c>"、10 第 2.2 节 <c>quest_state: Map&lt;Id, QuestProgress&gt;</c>）。
    /// </summary>
    public sealed class QuestProgress
    {
        public Id QuestId { get; }

        public QuestState State { get; }

        /// <summary>每条 <c>objectives[i]</c> 当前累计计数，下标与
        /// <see cref="QuestDefinition.Objectives"/> 对齐。</summary>
        public IReadOnlyList<int> ObjectiveCounts { get; }

        /// <summary>累计完成（交付）次数——<c>none</c> 可重复性下最多为 1，
        /// <c>daily</c>/<c>unlimited</c> 下可累加，供 <c>quest.is_completed</c> 判定使用。</summary>
        public int CompletionCount { get; }

        /// <summary>最近一次交付完成的模拟日（<c>daily</c> 可重复性的同日拒绝再接判定依据）；
        /// 从未交付过或非 <c>daily</c> 时为 null。</summary>
        public long? LastCompletedDay { get; }

        public QuestProgress(Id questId, QuestState state, IReadOnlyList<int> objectiveCounts, int completionCount, long? lastCompletedDay)
        {
            QuestId = questId;
            State = state;
            ObjectiveCounts = objectiveCounts;
            CompletionCount = completionCount;
            LastCompletedDay = lastCompletedDay;
        }
    }
}
