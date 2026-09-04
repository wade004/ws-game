using System;
using Core.Foundation.Common;
using Core.Foundation.Expr;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// 一条任务目标（见 08 第 2.1 节 <c>QuestObjective</c> 结构 + 八种 type 对照表的 <c>param</c>
    /// 要点列）。<c>param</c> 本身在 08 原文是 <c>Optional&lt;Map&lt;String, Value&gt;&gt;</c>
    /// 自由结构，本类型按各 <see cref="QuestObjectiveType"/> 实际用到的 param 键把它们拆成强类型
    /// 属性（<see cref="ConsumeOnProgress"/>/<see cref="EventFilter"/>/<see cref="EscortRouteRef"/>），
    /// 不保留原始 <c>Map</c> 形状——调用方（<c>QuestHost</c>）按 <see cref="Type"/> 只会用到对应的
    /// 那一个属性，强类型属性比"按字符串键查 Map"更不容易在实现里打错键名。
    /// </summary>
    public sealed class QuestObjective
    {
        public QuestObjectiveType Type { get; }

        /// <summary>依 <see cref="Type"/> 指向的目标：creature.template/item.template/gobj.template/
        /// area.trigger_def/skill.def/dialog 节点/事件 key（见 08 第 2.1 节对照表 targetRef 列）。</summary>
        public Id TargetRef { get; }

        /// <summary>需求数量；<see cref="QuestObjectiveTypes.RequiresCountOne"/> 为 true 的类型恒为 1。</summary>
        public int Count { get; }

        /// <summary><c>collect</c> 专属：进度是否随拾取即时消耗物品（见 08 第 2.1 节该行 param 要点）。
        /// 其它类型该值无意义，恒为 false。</summary>
        public bool ConsumeOnProgress { get; }

        /// <summary><c>event</c> 专属：触发条件表达式（见 08 第 2.1 节该行 param 要点
        /// <c>eventFilter: Expr</c>）；其它类型为 null。</summary>
        public ExprNode? EventFilter { get; }

        /// <summary><c>escort</c> 专属：护送路径引用（见 08 第 2.1 节该行 param 要点
        /// <c>escort_route_ref</c>）；其它类型为 null。</summary>
        public Id? EscortRouteRef { get; }

        /// <summary>该目标的描述文本键（08 原文 <c>QuestObjective</c> 结构未列出，任务书拍板补录
        /// <c>description_key?</c>，供任务日志 UI 展示每条目标的文案）；可选。</summary>
        public Id? DescriptionKey { get; }

        public QuestObjective(
            QuestObjectiveType type,
            Id targetRef,
            int count,
            bool consumeOnProgress = false,
            ExprNode? eventFilter = null,
            Id? escortRouteRef = null,
            Id? descriptionKey = null)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "QuestObjective.Count 必须为正数");
            }

            if (QuestObjectiveTypes.RequiresCountOne(type) && count != 1)
            {
                throw new ArgumentException(
                    $"目标类型 {type} 的 count 必须恒为 1（见 08 第 2.1 节对照表），实际为 {count}",
                    nameof(count));
            }

            var requiredDomain = QuestObjectiveTypes.RequiredTargetDomain(type);
            if (requiredDomain != null && targetRef.Domain != requiredDomain)
            {
                throw new ArgumentException(
                    $"目标类型 {type} 的 targetRef 域名必须是 \"{requiredDomain}\"（见 08 第 2.1 节对照表），实际为 \"{targetRef}\"",
                    nameof(targetRef));
            }

            Type = type;
            TargetRef = targetRef;
            Count = count;
            ConsumeOnProgress = consumeOnProgress;
            EventFilter = eventFilter;
            EscortRouteRef = escortRouteRef;
            DescriptionKey = descriptionKey;
        }
    }
}
