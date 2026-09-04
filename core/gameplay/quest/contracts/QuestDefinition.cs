using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;
using Core.Gameplay.Common;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// <c>quest.def</c> 一条记录的内存态表示（见 08 第 2.1 节字段表）。<see cref="QuestHost"/> 只
    /// 操作本类型，不直接依赖 <see cref="IDataRegistryView"/>——把 <see cref="DataRecord"/> 转换成
    /// 本类型是内容加载期（游戏组装根）的职责，见 <see cref="FromRecord"/>；本类型也可以完全脱离
    /// <c>data_registry</c> 用代码直接构造（测试与脱离引擎场景，见 08 第 9 节 Quest 行"测试方式：
    /// 脱离引擎构造一条任务与一串模拟事件"）。
    /// </summary>
    public sealed class QuestDefinition
    {
        public Id Id { get; }

        public IReadOnlyList<QuestObjective> Objectives { get; }

        /// <summary>前置条件（见 08 第 2.1 节 <c>prerequisite</c>）；未声明时视为恒真（无前置）。</summary>
        public ExprNode? Prerequisite { get; }

        public Id? ExclusiveGroup { get; }

        public QuestStartMethod StartMethod { get; }

        public QuestTurnInMethod TurnInMethod { get; }

        public RewardBundle Rewards { get; }

        public QuestRepeatable Repeatable { get; }

        public Id? TitleKey { get; }

        public Id? DescriptionKey { get; }

        public QuestDefinition(
            Id id,
            IReadOnlyList<QuestObjective> objectives,
            QuestStartMethod startMethod,
            QuestTurnInMethod turnInMethod,
            QuestRepeatable repeatable,
            ExprNode? prerequisite = null,
            Id? exclusiveGroup = null,
            RewardBundle? rewards = null,
            Id? titleKey = null,
            Id? descriptionKey = null)
        {
            Id = id;
            if (objectives == null || objectives.Count == 0)
            {
                throw new ArgumentException("quest.def.objectives 至少需要一条目标（见 08 第 2.1 节 objectives 必填）", nameof(objectives));
            }
            Objectives = objectives;
            Prerequisite = prerequisite;
            ExclusiveGroup = exclusiveGroup;
            StartMethod = startMethod;
            TurnInMethod = turnInMethod;
            Rewards = rewards ?? RewardBundle.Empty;
            Repeatable = repeatable;
            TitleKey = titleKey;
            DescriptionKey = descriptionKey;
        }

        /// <summary>
        /// 从一条 <c>quest.def</c> <see cref="DataRecord"/> 解析出 <see cref="QuestDefinition"/>。
        /// <paramref name="exprSchema"/> 供解析 <c>prerequisite</c> 与各 <c>event</c> 目标
        /// <c>param.eventFilter</c> 用（见 <see cref="QuestExprSchemaEntries"/> 判断记录：不能直接
        /// 复用 <c>core/rules/expr_host.RulesExprSchema</c>，需要调用方传入一份只精确登记
        /// <c>quest</c>/<c>player</c>/<c>world</c> 分组已知键、不做"已知分组一律放行"的 schema，
        /// 否则形如 <c>quest.is_active(quest.deliver_letter)</c> 的参数会被误判为引用而不是 Id
        /// 字面量，见 ADR-0015）。字段格式错误抛 <see cref="FormatException"/>/
        /// <see cref="ExprParseException"/>，由调用方（内容校验规则或组装根）决定如何呈现。
        /// </summary>
        public static QuestDefinition FromRecord(DataRecord record, IExprSchema exprSchema)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (exprSchema == null) throw new ArgumentNullException(nameof(exprSchema));

            var id = record.GetId("id");

            var objectivesArray = record.GetArray("objectives");
            var objectives = new List<QuestObjective>(objectivesArray.Count);
            foreach (var item in objectivesArray)
            {
                if (!(item is JsonObject o))
                {
                    throw new FormatException($"quest.def[{id}].objectives 的元素必须是对象");
                }
                objectives.Add(ParseObjective(id, o, exprSchema));
            }

            ExprNode? prerequisite = null;
            if (record.TryGetString("prerequisite", out var prereqText) && !string.IsNullOrEmpty(prereqText))
            {
                prerequisite = ExprParser.Parse(prereqText, exprSchema);
            }

            Id? exclusiveGroup = record.TryGetId("exclusive_group", out var excl) ? excl : (Id?)null;

            if (!record.TryGetString("start_method", out var startMethodText) ||
                !QuestEnumWireNames.TryParseStartMethod(startMethodText, out var startMethod))
            {
                throw new FormatException($"quest.def[{id}].start_method 缺失或取值非法");
            }

            if (!record.TryGetString("turn_in_method", out var turnInMethodText) ||
                !QuestEnumWireNames.TryParseTurnInMethod(turnInMethodText, out var turnInMethod))
            {
                throw new FormatException($"quest.def[{id}].turn_in_method 缺失或取值非法");
            }

            if (!record.TryGetString("repeatable", out var repeatableText) ||
                !QuestEnumWireNames.TryParseRepeatable(repeatableText, out var repeatable))
            {
                throw new FormatException($"quest.def[{id}].repeatable 缺失或取值非法");
            }

            var rewards = record.TryGetObject("rewards", out var rewardsObj) ? RewardBundle.FromRecord(rewardsObj) : RewardBundle.Empty;

            Id? titleKey = record.TryGetId("title_key", out var tk) ? tk : (Id?)null;
            Id? descKey = record.TryGetId("description_key", out var dk) ? dk : (Id?)null;

            return new QuestDefinition(id, objectives, startMethod, turnInMethod, repeatable, prerequisite, exclusiveGroup, rewards, titleKey, descKey);
        }

        private static QuestObjective ParseObjective(Id questId, JsonObject o, IExprSchema exprSchema)
        {
            if (!o.TryGetValue("type", out var typeVal) || !(typeVal is JsonString typeStr) ||
                !QuestObjectiveTypes.TryParse(typeStr.Value, out var type))
            {
                throw new FormatException($"quest.def[{questId}].objectives[].type 缺失或取值非法");
            }

            if (!o.TryGetValue("target_ref", out var targetVal) || !(targetVal is JsonString targetStr) ||
                !Id.TryParse(targetStr.Value, out var targetRef))
            {
                throw new FormatException($"quest.def[{questId}].objectives[].target_ref 缺失或不是合法 Id");
            }

            if (!o.TryGetValue("count", out var countVal) || !(countVal is JsonNumber countNum) || !countNum.TryGetInt64(out var countLong))
            {
                throw new FormatException($"quest.def[{questId}].objectives[].count 缺失或不是整数");
            }

            var consumeOnProgress = false;
            ExprNode? eventFilter = null;
            Id? escortRouteRef = null;

            if (o.TryGetValue("param", out var paramVal) && paramVal is JsonObject param)
            {
                if (param.TryGetValue("consume_on_progress", out var cop) && cop is JsonBool copBool)
                {
                    consumeOnProgress = copBool.Value;
                }

                if (param.TryGetValue("eventFilter", out var ef) && ef is JsonString efStr && !string.IsNullOrEmpty(efStr.Value))
                {
                    eventFilter = ExprParser.Parse(efStr.Value, exprSchema);
                }

                if (param.TryGetValue("escort_route_ref", out var route) && route is JsonString routeStr && Id.TryParse(routeStr.Value, out var routeId))
                {
                    escortRouteRef = routeId;
                }
            }

            Id? descriptionKey = null;
            if (o.TryGetValue("description_key", out var dk) && dk is JsonString dkStr && Id.TryParse(dkStr.Value, out var dkId))
            {
                descriptionKey = dkId;
            }

            return new QuestObjective(type, targetRef, (int)countLong, consumeOnProgress, eventFilter, escortRouteRef, descriptionKey);
        }
    }
}
