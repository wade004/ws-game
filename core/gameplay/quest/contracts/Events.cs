using System;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Gameplay.Quest
{
    /// <summary>本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// data/_sample/found/found.event_catalog.json <c>quest.*</c> 五行）。惯例同
    /// <c>core/gameplay/world_state</c> 的 <c>WorldStateEventKeys</c>：模块自持一份常量，不依赖
    /// 生成物。</summary>
    public static class QuestEventKeys
    {
        public static readonly Id Accepted = new Id("quest.accepted");
        public static readonly Id ObjectiveProgress = new Id("quest.objective_progress");
        public static readonly Id Completed = new Id("quest.completed");
        public static readonly Id TurnedIn = new Id("quest.turned_in");
        public static readonly Id Failed = new Id("quest.failed");
    }

    /// <summary>任务被接取时触发（见 found.event_catalog <c>quest.accepted</c> 行、08 第 2.2、9 节）。</summary>
    public sealed class QuestAcceptedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => QuestEventKeys.Accepted;

        public Id UnitId { get; }

        public Id QuestId { get; }

        public QuestAcceptedEvent(Id unitId, Id questId)
        {
            UnitId = unitId;
            QuestId = questId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "questId": value = ExprValue.OfId(QuestId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>任务目标推进时触发（见 found.event_catalog <c>quest.objective_progress</c> 行；
    /// 该行建议字段为 <c>objectiveId</c>，本类型改用 <see cref="ObjectiveIndex"/>——判断记录：
    /// <c>quest.def.objectives</c> 是数组，08 第 2.1 节未给每条目标单独定义 id 字段，
    /// <c>found.event_catalog</c> 该行字段本就标注"字段为建议值"（非强约束），用数组下标是本模块
    /// 内唯一可行的"目标标识"，比强行伪造一个不存在的 id 更贴合实际数据结构。</summary>
    public sealed class QuestObjectiveProgressEvent : IEvent, IExprReadableEvent
    {
        public Id Key => QuestEventKeys.ObjectiveProgress;

        public Id UnitId { get; }

        public Id QuestId { get; }

        public int ObjectiveIndex { get; }

        public int Progress { get; }

        public QuestObjectiveProgressEvent(Id unitId, Id questId, int objectiveIndex, int progress)
        {
            UnitId = unitId;
            QuestId = questId;
            ObjectiveIndex = objectiveIndex;
            Progress = progress;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "questId": value = ExprValue.OfId(QuestId); return true;
                case "objectiveIndex": value = ExprValue.OfInt(ObjectiveIndex); return true;
                case "progress": value = ExprValue.OfInt(Progress); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>全部目标达标、状态转移为 ObjectivesComplete 时触发（见 found.event_catalog
    /// <c>quest.completed</c> 行、任务书拍板"转移时 quest.completed"——与 <c>quest.turned_in</c>
    /// 的关系见该 event_catalog 行 description"与本行的完整覆盖关系待设计层核对"：本模块采用
    /// "completed = 目标达标（进行中 → 可交付）、turned_in = 实际交付（可交付 → 已交付）"的两阶段
    /// 划分，二者不是同一时机。</summary>
    public sealed class QuestCompletedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => QuestEventKeys.Completed;

        public Id UnitId { get; }

        public Id QuestId { get; }

        public QuestCompletedEvent(Id unitId, Id questId)
        {
            UnitId = unitId;
            QuestId = questId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "questId": value = ExprValue.OfId(QuestId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>任务交付完成时触发（见 found.event_catalog <c>quest.turned_in</c> 行）。</summary>
    public sealed class QuestTurnedInEvent : IEvent, IExprReadableEvent
    {
        public Id Key => QuestEventKeys.TurnedIn;

        public Id UnitId { get; }

        public Id QuestId { get; }

        public QuestTurnedInEvent(Id unitId, Id questId)
        {
            UnitId = unitId;
            QuestId = questId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "questId": value = ExprValue.OfId(QuestId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>任务失败判定时触发（见 found.event_catalog <c>quest.failed</c> 行）。</summary>
    public sealed class QuestFailedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => QuestEventKeys.Failed;

        public Id UnitId { get; }

        public Id QuestId { get; }

        public string Reason { get; }

        public QuestFailedEvent(Id unitId, Id questId, string reason)
        {
            UnitId = unitId;
            QuestId = questId;
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "questId": value = ExprValue.OfId(QuestId); return true;
                case "reason": value = ExprValue.OfString(Reason); return true;
                default: value = default; return false;
            }
        }
    }
}
