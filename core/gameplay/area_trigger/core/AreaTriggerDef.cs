using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;

namespace Core.Gameplay.AreaTrigger
{
    /// <summary>
    /// 一条 <c>area.trigger_def</c> 记录的强类型视图（见 05_对象模型与世界.md 第 1.5、7 节），从
    /// <see cref="DataRecord"/> 构造，构造期完成全部字段解析与合法性检查（惯例同
    /// <c>Core.Rules.Targeting.TargetChainDef</c>：非法数据在构造期即抛
    /// <see cref="DataFieldException"/>；内容管线期的非阻断式校验见
    /// <c>schema/AreaTriggerValidationRules.cs</c>）。
    /// </summary>
    public sealed class AreaTriggerDef
    {
        public Id Id { get; }

        public Id MapId { get; }

        /// <summary>触发范围（绝对世界坐标，见 <see cref="AreaTriggerShapeJson"/> 判断记录）。</summary>
        public Shape Shape { get; }

        public AreaTriggerType TriggerType { get; }

        /// <summary>附加触发条件的原始 Expr 文本；数据未提供该字段时为 null（见 05 第 1.5 节
        /// <c>condition: Optional&lt;Expr&gt;</c>）。</summary>
        public string? ConditionText { get; }

        /// <summary>是否只触发一次；数据未提供该字段时为 false。</summary>
        public bool OneShot { get; }

        /// <summary><see cref="TriggerType"/> 为 <see cref="AreaTriggerType.MapTransition"/> 时非空。</summary>
        public MapTransitionParams? MapTransition { get; }

        /// <summary><see cref="TriggerType"/> 为 <see cref="AreaTriggerType.EncounterStart"/> 时非空。</summary>
        public EncounterStartParams? EncounterStart { get; }

        /// <summary><see cref="TriggerType"/> 为 <see cref="AreaTriggerType.Script"/> 时非空。</summary>
        public ScriptParams? Script { get; }

        public Id? NameKey { get; }

        public static AreaTriggerDef FromRecord(DataRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            var id = record.GetId("id");
            var mapId = record.GetId("map_id");

            Shape shape;
            try
            {
                shape = AreaTriggerShapeJson.Parse(record.GetObject("shape"));
            }
            catch (ArgumentException ex)
            {
                throw new DataFieldException(record.Table.Name, record.Key, "shape", ex.Message);
            }

            var typeText = record.GetString("trigger_type");
            if (!AreaTriggerTypeNames.TryParse(typeText, out var triggerType))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "trigger_type",
                    $"非法取值 \"{typeText}\"，只允许 map_transition|quest_explore|encounter_start|script");
            }

            var conditionText = record.TryGetString("condition", out var condition) ? condition : null;
            var oneShot = record.TryGetBool("one_shot", out var os) && os;
            var nameKey = record.TryGetId("name_key", out var nk) ? (Id?)nk : null;

            var paramsObj = record.GetObject("params");
            MapTransitionParams? mapTransition = null;
            EncounterStartParams? encounterStart = null;
            ScriptParams? script = null;

            switch (triggerType)
            {
                case AreaTriggerType.MapTransition:
                    var targetMap = RequireParamId(record, paramsObj, "target_map");
                    var spawnPoint = TryParamId(paramsObj, "spawn_point");
                    mapTransition = new MapTransitionParams(targetMap, spawnPoint);
                    break;

                case AreaTriggerType.EncounterStart:
                    encounterStart = new EncounterStartParams(RequireParamId(record, paramsObj, "encounter_ref"));
                    break;

                case AreaTriggerType.Script:
                    script = new ScriptParams(RequireParamId(record, paramsObj, "hook_id"));
                    break;

                case AreaTriggerType.QuestExplore:
                    // 05 第 7 节：quest_explore 无需额外字段，params 允许为空对象。
                    break;
            }

            return new AreaTriggerDef(id, mapId, shape, triggerType, conditionText, oneShot,
                mapTransition, encounterStart, script, nameKey);
        }

        private AreaTriggerDef(
            Id id, Id mapId, Shape shape, AreaTriggerType triggerType, string? conditionText, bool oneShot,
            MapTransitionParams? mapTransition, EncounterStartParams? encounterStart, ScriptParams? script, Id? nameKey)
        {
            Id = id;
            MapId = mapId;
            Shape = shape;
            TriggerType = triggerType;
            ConditionText = conditionText;
            OneShot = oneShot;
            MapTransition = mapTransition;
            EncounterStart = encounterStart;
            Script = script;
            NameKey = nameKey;
        }

        private static Id RequireParamId(DataRecord record, JsonObject paramsObj, string field)
        {
            if (!paramsObj.TryGetValue(field, out var v) || !(v is JsonString s) || !Id.TryParse(s.Value, out var id))
            {
                throw new DataFieldException(record.Table.Name, record.Key, $"params.{field}", "缺少合法 Id 字段");
            }

            return id;
        }

        private static Id? TryParamId(JsonObject paramsObj, string field)
        {
            if (paramsObj.TryGetValue(field, out var v) && v is JsonString s && Id.TryParse(s.Value, out var id))
            {
                return id;
            }

            return null;
        }
    }
}
