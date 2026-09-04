using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary><c>feedback.binding</c> 一条记录的不可变运行期视图（见 09_表现层.md 第 6.1 节
    /// <c>FeedbackRule</c>）。<see cref="Condition"/> 在构造期解析一次（同
    /// <c>Core.Rules.Ai.CompiledRotationEntry</c> 惯例）。</summary>
    public sealed class FeedbackRule
    {
        public Id Id { get; }

        /// <summary>订阅的事件类型（如 <c>combat.damage_dealt</c>）。</summary>
        public Id EventKey { get; }

        /// <summary>使用全架构唯一条件语言（04 第 6 节）；未声明（记录未提供 <c>condition</c> 字段）
        /// 时为 null，表示"事件到达即无条件执行全部动作"。</summary>
        public ExprNode? Condition { get; }

        public IReadOnlyList<FeedbackAction> Actions { get; }

        public FeedbackRule(Id id, Id eventKey, ExprNode? condition, IReadOnlyList<FeedbackAction> actions)
        {
            Id = id;
            EventKey = eventKey;
            Condition = condition;
            Actions = actions ?? throw new System.ArgumentNullException(nameof(actions));
        }

        /// <summary>从一条已加载的 <c>feedback.binding</c> <see cref="DataRecord"/> 构造；
        /// <paramref name="exprSchema"/> 供 <see cref="Condition"/> 文本解析使用（04 第 6.2 节
        /// 九个宿主引用分组的登记表，通常传 <c>Core.Rules.ExprHost.RulesExprSchema.Base</c>，
        /// <c>event</c> 分组本就对任意未登记 key 放行为引用，见 <c>ExprParser.ParseIdentTerm</c>
        /// 判断记录，本模块不需要额外登记 <c>event.*</c>）。</summary>
        public static FeedbackRule FromRecord(DataRecord record, IExprSchema exprSchema)
        {
            var id = record.GetId("id");
            var eventKey = record.GetId("event");

            ExprNode? condition = null;
            if (record.TryGetString("condition", out var conditionText) && !string.IsNullOrEmpty(conditionText))
            {
                condition = ExprParser.Parse(conditionText, exprSchema);
            }

            var actionsArray = record.GetArray("actions");
            var actions = new List<FeedbackAction>(actionsArray.Count);
            for (var i = 0; i < actionsArray.Count; i++)
            {
                if (!(actionsArray[i] is JsonObject actionObj))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "actions", $"第 {i} 个元素不是对象");
                }
                actions.Add(ParseAction(record, i, actionObj));
            }

            return new FeedbackRule(id, eventKey, condition, actions);
        }

        private static FeedbackAction ParseAction(DataRecord record, int index, JsonObject actionObj)
        {
            var kind = RequireString(record, index, actionObj, "kind");
            var @params = actionObj.TryGetValue("params", out var paramsVal) && paramsVal is JsonObject p
                ? p
                : new JsonObjectBuilder().Build();

            switch (kind)
            {
                case "floating_text":
                    return new FloatingTextAction(
                        RequireId(record, index, @params, "style_id"),
                        TextSource.Parse(RequireString(record, index, @params, "text_source")));

                case "play_vfx":
                    return new PlayVfxAction(
                        OptionalId(@params, "vfx_id"),
                        OptionalEnum<FromDisplaySource>(@params, "from_display"),
                        RequireEnum<FeedbackAttachTarget>(record, index, @params, "attach"),
                        OptionalId(@params, "anchor_id"));

                case "play_sfx":
                    return new PlaySfxAction(
                        OptionalId(@params, "sfx_id"),
                        OptionalEnum<FromDisplaySource>(@params, "from_display"));

                case "freeze":
                    return new FreezeAction(RequireNumber(record, index, @params, "duration_ms"));

                case "shake_camera":
                    return new ShakeCameraAction(RequireId(record, index, @params, "profile_id"));

                case "flash":
                    return new FlashAction(
                        RequireId(record, index, @params, "profile_id"),
                        RequireEnum<FeedbackAttachTarget>(record, index, @params, "target"));

                default:
                    throw new DataFieldException(record.Table.Name, record.Key, "actions", $"第 {index} 个元素的 kind 非法：\"{kind}\"");
            }
        }

        private static string RequireString(DataRecord record, int index, JsonObject obj, string field)
        {
            if (obj.TryGetValue(field, out var v) && v is JsonString s)
            {
                return s.Value;
            }
            throw new DataFieldException(record.Table.Name, record.Key, "actions", $"第 {index} 个元素缺少必填字符串字段 \"{field}\"");
        }

        private static double RequireNumber(DataRecord record, int index, JsonObject obj, string field)
        {
            if (obj.TryGetValue(field, out var v) && v is JsonNumber n)
            {
                return n.Value;
            }
            throw new DataFieldException(record.Table.Name, record.Key, "actions", $"第 {index} 个元素缺少必填数值字段 \"{field}\"");
        }

        private static Id RequireId(DataRecord record, int index, JsonObject obj, string field)
        {
            if (obj.TryGetValue(field, out var v) && v is JsonString s && Id.TryParse(s.Value, out var id))
            {
                return id;
            }
            throw new DataFieldException(record.Table.Name, record.Key, "actions", $"第 {index} 个元素缺少必填 Id 字段 \"{field}\"");
        }

        private static Id? OptionalId(JsonObject obj, string field) =>
            obj.TryGetValue(field, out var v) && v is JsonString s && Id.TryParse(s.Value, out var id) ? id : (Id?)null;

        private static TEnum RequireEnum<TEnum>(DataRecord record, int index, JsonObject obj, string field) where TEnum : struct
        {
            if (obj.TryGetValue(field, out var v) && v is JsonString s && System.Enum.TryParse<TEnum>(s.Value, ignoreCase: true, out var e))
            {
                return e;
            }
            throw new DataFieldException(record.Table.Name, record.Key, "actions", $"第 {index} 个元素缺少必填枚举字段 \"{field}\"");
        }

        private static TEnum? OptionalEnum<TEnum>(JsonObject obj, string field) where TEnum : struct =>
            obj.TryGetValue(field, out var v) && v is JsonString s && System.Enum.TryParse<TEnum>(s.Value, ignoreCase: true, out var e)
                ? e
                : (TEnum?)null;
    }
}
