using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>
    /// 规则级命中帧同步声明（ADR-0017 决策 d，<c>feedback.binding.sync</c> 字段新增，见
    /// <see cref="Presentation.FeedbackBinder.Schema.FeedbackSchemas.Binding"/>）：
    /// <see cref="HitFrame"/> 表示该规则的全部动作应延迟到攻击方 rig 的命中帧再一并播放（09 第
    /// 4.3 节 <c>anim_keyframe_driven</c> 策略），<see cref="None"/> 表示按逻辑事件到达即刻播放（既有
    /// 默认行为，<see cref="Presentation.Render.HitFrameSyncStrategy.LogicDriven"/> 下恒等同
    /// <see cref="None"/>）。
    /// </summary>
    public enum FeedbackSyncMode
    {
        None,
        HitFrame,
    }

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

        /// <summary>见 <see cref="FeedbackSyncMode"/>。默认取值（记录未提供 <c>sync</c> 字段时）：
        /// <see cref="EventKey"/> 等于 <see cref="RulesEventKeys.CombatDamageDealt"/> 时默认
        /// <see cref="FeedbackSyncMode.HitFrame"/>（ADR-0017 决策 d"默认对 combat.damage_dealt 类命中
        /// 反馈开启"），其余事件默认 <see cref="FeedbackSyncMode.None"/>。显式提供 <c>sync</c> 字段
        /// 时以该字段取值为准，不受事件类型影响。</summary>
        public FeedbackSyncMode Sync { get; }

        public FeedbackRule(Id id, Id eventKey, ExprNode? condition, IReadOnlyList<FeedbackAction> actions, FeedbackSyncMode sync = FeedbackSyncMode.None)
        {
            Id = id;
            EventKey = eventKey;
            Condition = condition;
            Actions = actions ?? throw new System.ArgumentNullException(nameof(actions));
            Sync = sync;
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

            var sync = ParseSync(record, eventKey);

            return new FeedbackRule(id, eventKey, condition, actions, sync);
        }

        /// <summary>见 <see cref="Sync"/> 判断记录：显式 <c>sync</c> 字段优先；未提供时按 <paramref name="eventKey"/>
        /// 是否为 <see cref="RulesEventKeys.CombatDamageDealt"/> 决定默认值。</summary>
        private static FeedbackSyncMode ParseSync(DataRecord record, Id eventKey)
        {
            if (record.TryGetString("sync", out var syncText))
            {
                if (syncText == "hit_frame")
                {
                    return FeedbackSyncMode.HitFrame;
                }
                throw new DataFieldException(record.Table.Name, record.Key, "sync", $"未知的 sync 取值：\"{syncText}\"");
            }

            return eventKey == RulesEventKeys.CombatDamageDealt ? FeedbackSyncMode.HitFrame : FeedbackSyncMode.None;
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
